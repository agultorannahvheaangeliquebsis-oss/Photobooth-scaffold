using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Dashboard over AdminDashboardRepository (sessions today, revenue by
/// mode, low-inventory alerts -- read-only) plus an editable Settings
/// section (countdown duration, Glam Booth mode -- backed by the
/// Location row's own columns) and a Frame library section (add/retire the
/// overlays guests can pick during FramePicker). Reached from MainWindow
/// via F12 (see Window_KeyDown there), never from the guest-facing surface.
/// </summary>
/// <summary>Flattened view of a GuestbookVideoRecord for GuestbookVideosList's
/// bindings (Tag values need the raw FilePath/GuestbookVideoId, not just a
/// display string, unlike RevenueList/InventoryList's plain-string rows).</summary>
public record GuestbookVideoRow(int GuestbookVideoId, string FilePath, string Label);

public partial class AdminWindow : Window
{
    private readonly AdminDashboardRepository _repository = new();
    private readonly LocationRepository _locations = new();
    private readonly FrameRepository _frames = new();
    private readonly SurveyRepository _survey = new();
    private readonly VirtualAttendantClipRepository _attendantClips = new();

    // Which event/location this dashboard is editing. A booth machine can now
    // have several saved events (see EventLauncherWindow) -- _requestedLocationId
    // is the one the caller asked for (e.g. the event KioskWindow is actually
    // running), falling back to the first Location row if null, same as this
    // window's original "one booth machine has one location" assumption did.
    private readonly int? _requestedLocationId;
    private int _locationId;

    private string? _pendingFrameImagePath;
    private List<FrameRecord> _stickerFrames = new();
    private int _stickerPreviewIndex;
    private string? _pendingThemeLogoPath;
    private string? _existingThemeLogoPath;
    private PrintTemplate _currentPrintTemplate = PrintTemplate.Default;

    private string? _pendingWatermarkPath;
    private string? _existingWatermarkPath;
    private string? _pendingGreenScreenBackgroundPath;
    private string? _existingGreenScreenBackgroundPath;

    // Source of truth for the six Randomize toggles now embedded in the Virtual
    // Attendant tile grid (see BuildAttendantStageCard) -- a plain dictionary
    // rather than named CheckBox fields, since the cards themselves are rebuilt
    // from scratch on every LoadAttendantClipsAsync call (add/delete/reorder),
    // so there's no stable control instance across rebuilds to read from at
    // save time the way BeautyFilterCheckBox etc. elsewhere in this file can.
    private readonly Dictionary<BoothState, bool> _randomizeByStage = new();

    /// <summary>The Virtual Attendant tile grid's stages, in guest-journey order,
    /// with the friendly label shown on each card. Labels borrow dslrBooth's own
    /// Virtual Attendant vocabulary (Start Screen/Select an Effect/Countdown
    /// Video/After Capture/During Processing/End of Session) wherever this
    /// app's actual BoothState flow lines up with it; stages dslrBooth's screen
    /// has no equivalent for keep this app's own names. SupportsRandomize
    /// mirrors exactly the six columns VirtualAttendantSettings has (Consent/
    /// Countdown/Capturing/Reviewing/Printing/Complete) -- the other nine
    /// stages (including FilterPicker) can still hold a clip pool, just always
    /// played in SortOrder.</summary>
    private static readonly (BoothState Stage, string Label, bool SupportsRandomize)[] AttendantStageDefinitions =
    [
        (BoothState.Setup, "Setup Screen", false),
        (BoothState.Idle, "Start Screen", false),
        (BoothState.Consent, "Before Countdown (Consent)", true),
        (BoothState.Countdown, "Countdown Video", true),
        (BoothState.Capturing, "Capturing", true),
        (BoothState.FilterPicker, "Select an Effect (Filters)", false),
        (BoothState.Reviewing, "After Capture (Reviewing)", true),
        (BoothState.FramePicker, "Frame / Sticker Picker", false),
        (BoothState.Payment, "Payment", false),
        (BoothState.Printing, "During Processing (Printing)", true),
        (BoothState.Complete, "End of Session (Complete)", true),
        (BoothState.Guestbook, "Guestbook", false),
        (BoothState.Feedback, "Feedback", false),
        (BoothState.Survey, "Survey", false),
        (BoothState.Error, "Error", false),
    ];

    // Loaded fresh in LoadAsync, then applied on top of via `with` in
    // SaveParitySettingsButton_Click -- BoothIconsEnabled/ShowLiveView have no
    // editable UI here (still only edited via ScreenTemplateEditorWindow's own
    // Settings tab), so this is what keeps them from being clobbered back to
    // defaults when Camera Settings' own fields (Mirror/Rotation/EnableWebcams/
    // WebcamResolutionQuality/AudioInputDeviceName) get saved.
    private ScreenSettings _currentScreenSettings = ScreenSettings.Default;

    public AdminWindow(int? locationId = null, string initialSection = "General")
    {
        InitializeComponent();
        _requestedLocationId = locationId;
        Loaded += async (_, _) => await LoadAsync();
        Loaded += (_, _) => ShowSection(initialSection);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Flips an On/Off-labeled toggle CheckBox's own Content between
    /// those two words as it's checked/unchecked -- the shared toggle-switch
    /// CheckBox style (see Window.Resources) conveys state via thumb position
    /// alone, so without this the "On" label set in XAML would keep reading
    /// "On" even once switched off. Used by Camera Settings' Enable Webcams/
    /// Mirror Live View toggles; also fires once during InitializeComponent for
    /// their XAML-default IsChecked value, and again whenever LoadCameraSettings
    /// sets IsChecked from the loaded event's real settings.</summary>
    private void OnOffToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            checkBox.Content = checkBox.IsChecked == true ? "On" : "Off";
        }
    }

    /// <summary>Sets an On/Off-labeled toggle CheckBox's value AND its Content
    /// together -- used by the Load* methods instead of a bare `.IsChecked =`
    /// assignment. Setting IsChecked alone only raises Checked/Unchecked (and
    /// so only updates Content via OnOffToggle_Changed above) when the value
    /// actually changes from what it already was; loading a value that happens
    /// to match the control's current/XAML-default state would otherwise leave
    /// a stale Content label, which this sidesteps by always setting both.</summary>
    private static void SetOnOffToggle(CheckBox checkBox, bool value)
    {
        checkBox.IsChecked = value;
        checkBox.Content = value ? "On" : "Off";
    }

    // ---- Two-layer admin navigation (Layer 1 dropdown mega-menu + Layer 2
    // per-section wizard breadcrumb), replicated from dslrBooth's admin UI --
    // see BUILD_PLAN.md. All section content still lives in one ScrollViewer;
    // ShowSection just flips which StackPanel is Visible so every existing
    // control/handler below keeps working untouched. ----

    /// <summary>Friendly titles for the dropdown-menu-only entries that have
    /// no dedicated section panel/backing functionality yet -- they route to
    /// the shared PlaceholderSectionPanel instead.</summary>
    private static readonly Dictionary<string, string> PlaceholderTitles = new()
    {
        ["Slideshow"] = "Slideshow",
        ["SharingStatus"] = "Sharing Status",
        ["ExportEvent"] = "Export Event",
        ["EventFolder"] = "Event folder",
        ["RemoteControl"] = "Remote Control",
        ["ShowLockScreen"] = "Show Lock Screen",
        ["Language"] = "Language",
        ["Subscription"] = "Subscription",
        ["About"] = "About dslrBooth",
        ["Help"] = "Help",
    };

    private Dictionary<string, FrameworkElement>? _sectionPanels;

    private Dictionary<string, FrameworkElement> SectionPanels => _sectionPanels ??= new()
    {
        ["PrintLayout"] = PrintLayoutSectionPanel,
        ["General"] = GeneralSectionPanel,
        ["CaptureSettings"] = CaptureSettingsSectionPanel,
        ["CameraSettings"] = CameraSettingsSectionPanel,
        ["VirtualAttendant"] = VirtualAttendantSectionPanel,
        ["EffectsStickers"] = EffectsStickersSectionPanel,
        ["GreenScreen"] = GreenScreenSectionPanel,
        ["Survey"] = SurveySectionPanel,
        ["Disclaimer"] = DisclaimerSectionPanel,
        ["SharingSettings"] = SharingSettingsSectionPanel,
        ["PrintSetup"] = PrintSetupSectionPanel,
    };

    /// <summary>Shows the named section (a wizard section key, or one of
    /// PlaceholderTitles' keys) and hides every other section panel. The
    /// Layer 1 nav lives in a dropdown (NavMenuToggle/NavMenuPopup) rather
    /// than a permanently expanded grid, so it stays reachable on every
    /// section without taking up space while closed.</summary>
    private void ShowSection(string key)
    {
        foreach (FrameworkElement panel in SectionPanels.Values)
        {
            panel.Visibility = Visibility.Collapsed;
        }
        PlaceholderSectionPanel.Visibility = Visibility.Collapsed;

        if (SectionPanels.TryGetValue(key, out FrameworkElement? sectionPanel))
        {
            sectionPanel.Visibility = Visibility.Visible;
        }
        else if (PlaceholderTitles.TryGetValue(key, out string? title))
        {
            PlaceholderTitleText.Text = title;
            PlaceholderSectionPanel.Visibility = Visibility.Visible;
        }
        else
        {
            GeneralSectionPanel.Visibility = Visibility.Visible;
        }
    }

    private void MenuLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ShowSection(key);
            NavMenuToggle.IsChecked = false;
        }
    }

    private void PreviousSection_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ShowSection(key);
        }
    }

    private void NextSection_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ShowSection(key);
        }
    }

    /// <summary>Print Setup is the last wizard section -- its "Next" reads
    /// "Launch event" and, same as dslrBooth's own flow, just closes this
    /// dashboard back to the guest-facing Setup/Launch Event screen (see
    /// CloseButton_Click).</summary>
    private void LaunchEvent_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();

    /// <summary>Countdown/Glam/PIN (General) and the print template (Print
    /// Layout) save together through one call (LocationRepository.
    /// UpdateSettingsAsync); both sections' Save buttons trigger this same
    /// handler, so status/enabled state is mirrored to both status texts and
    /// both buttons rather than just whichever one was clicked.</summary>
    private void SetSettingsStatus(string text, bool isError)
    {
        System.Windows.Media.Brush brush = isError
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
        SettingsStatusText.Text = text;
        SettingsStatusText.Foreground = brush;
        PrintLayoutStatusText.Text = text;
        PrintLayoutStatusText.Foreground = brush;
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountdownSecondsBox.Text, out int countdownSeconds) || countdownSeconds <= 0)
        {
            SetSettingsStatus("Countdown must be a whole number of seconds greater than 0.", isError: true);
            return;
        }

        string layout = StripLayoutRadio.IsChecked == true ? "Strip" : "Single";
        if (!double.TryParse(PrintWidthBox.Text, out double widthInches)
            || !double.TryParse(PrintHeightBox.Text, out double heightInches)
            || !int.TryParse(StripCopiesBox.Text, out int stripCopies))
        {
            SetSettingsStatus("Print width/height and strip copies must be numbers.", isError: true);
            return;
        }

        var printTemplate = new PrintTemplate(layout, widthInches, heightInches, stripCopies);
        if (!printTemplate.IsValid)
        {
            SetSettingsStatus("Print width/height must be greater than 0 and strip copies at least 1.", isError: true);
            return;
        }

        string adminPin = AdminPinBox.Text.Trim();
        if (adminPin.Length == 0)
        {
            SetSettingsStatus("Admin PIN can't be blank.", isError: true);
            return;
        }

        SaveSettingsButton.IsEnabled = false;
        SavePrintLayoutButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSettingsAsync(_locationId, countdownSeconds, GlamFilterCheckBox.IsChecked == true, printTemplate, adminPin);
            SetSettingsStatus("Saved -- takes effect for the next guest session.", isError: false);
        }
        catch (Exception ex)
        {
            SetSettingsStatus($"Couldn't save: {ex.Message}", isError: true);
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
            SavePrintLayoutButton.IsEnabled = true;
        }
    }

    private void ThemeColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        Border? swatch = box.Name switch
        {
            nameof(AccentColorBox) => AccentColorSwatch,
            nameof(CanvasColorBox) => CanvasColorSwatch,
            nameof(InkColorBox) => InkColorSwatch,
            _ => null,
        };
        if (swatch is null)
        {
            return;
        }

        try
        {
            swatch.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(box.Text)!;
        }
        catch (Exception)
        {
            // Invalid/partial hex while typing -- leave the swatch showing
            // whatever it last successfully parsed rather than crashing.
        }
    }

    private void BrowseThemeLogoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a logo image",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingThemeLogoPath = dialog.FileName;
            SelectedThemeLogoText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    private async void SaveThemeButton_Click(object sender, RoutedEventArgs e)
    {
        // A newly-picked logo gets copied into a local Assets/Theme folder,
        // same "own local copy, not a reference to wherever the admin picked
        // it from" reasoning AddFrameButton_Click already established --
        // otherwise nothing not-yet-saved gets a path, so keep whatever
        // logo is already on file.
        string? logoPath = _existingThemeLogoPath;
        if (_pendingThemeLogoPath is not null)
        {
            string themeDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Theme");
            System.IO.Directory.CreateDirectory(themeDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingThemeLogoPath)}";
            logoPath = System.IO.Path.Combine(themeDirectory, storedFileName);
            System.IO.File.Copy(_pendingThemeLogoPath, logoPath, overwrite: true);
        }

        var theme = new BoothTheme(AccentColorBox.Text, CanvasColorBox.Text, InkColorBox.Text, logoPath, EventNameBox.Text.Trim());
        if (!theme.IsValid)
        {
            ThemeStatusText.Text = "Colors must be #RRGGBB hex, and the event name can't be blank.";
            ThemeStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        SaveThemeButton.IsEnabled = false;
        try
        {
            await _locations.UpdateThemeAsync(_locationId, theme);
            _existingThemeLogoPath = logoPath;
            _pendingThemeLogoPath = null;
            ThemeStatusText.Text = "Saved -- takes effect for the next guest session.";
            ThemeStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            ThemeStatusText.Text = $"Couldn't save: {ex.Message}";
            ThemeStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SaveThemeButton.IsEnabled = true;
        }
    }

    private async void EditPrintTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new PrintTemplateEditorWindow(_currentPrintTemplate, _locationId) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            // The editor already persisted the elements itself on Save --
            // reload from source of truth same as the Frame section already
            // does after add/delete, rather than trust the in-memory copy.
            await LoadAsync();
        }
    }

    private async void EditScreenLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        var existing = await new ScreenTemplateElementRepository().GetAllByLocationAsync(_locationId);
        var editor = new ScreenTemplateEditorWindow(existing, _locationId, _currentScreenSettings) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            // The editor's Settings tab can change ScreenSettings (Booth
            // Icons/live view show-mirror-rotate), which LoadAsync populates
            // into _currentScreenSettings -- reload so a second edit doesn't
            // clobber that change back to what was loaded before this one.
            // ScreenTemplateElement itself still needs no reload here (not
            // read into any LoadAsync field, read fresh by KioskWindow).
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            int sessionsToday = await _repository.GetSessionsTodayCountAsync();
            SessionsTodayText.Text = sessionsToday.ToString();

            var revenue = await _repository.GetRevenueByModeAsync();
            RevenueList.ItemsSource = revenue.Select(r => $"{r.Mode}: {r.Revenue:C}").ToList();
            RevenueEmptyText.Visibility = revenue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            FeedbackSummary feedbackSummary = await _repository.GetFeedbackSummaryAsync();
            FeedbackAverageText.Text = feedbackSummary.RatingCount == 0
                ? "No ratings yet."
                : $"{feedbackSummary.AverageRating:0.0} / 5 ({feedbackSummary.RatingCount} rating{(feedbackSummary.RatingCount == 1 ? "" : "s")})";

            var recentComments = await _repository.GetRecentCommentsAsync();
            FeedbackCommentsList.ItemsSource = recentComments
                .Select(c => $"“{c.Comment}” — session {c.SessionId}")
                .ToList();

            var alerts = await _repository.GetLowInventoryAlertsAsync();
            InventoryList.ItemsSource = alerts
                .Select(a => $"{a.Model} -- {a.ItemType}: {a.QuantityRemaining} remaining")
                .ToList();
            InventoryEmptyText.Visibility = alerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var locations = await _locations.GetAllAsync();
            if (locations.Count > 0)
            {
                LocationRecord location = (_requestedLocationId is int requestedId
                    ? locations.FirstOrDefault(l => l.LocationId == requestedId)
                    : null) ?? locations[0];

                _locationId = location.LocationId;
                TopNavEventNameText.Text = location.Name;
                CountdownSecondsBox.Text = location.CountdownSeconds.ToString();
                GlamFilterCheckBox.IsChecked = location.GlamFilterEnabled;
                AdminPinBox.Text = location.AdminPin;

                _currentPrintTemplate = location.PrintTemplate;
                SingleLayoutRadio.IsChecked = _currentPrintTemplate.Layout != "Strip";
                StripLayoutRadio.IsChecked = _currentPrintTemplate.Layout == "Strip";
                PrintWidthBox.Text = _currentPrintTemplate.WidthInches.ToString();
                PrintHeightBox.Text = _currentPrintTemplate.HeightInches.ToString();
                StripCopiesBox.Text = _currentPrintTemplate.StripCopies.ToString();

                BoothTheme theme = location.Theme;
                AccentColorBox.Text = theme.AccentColorHex;
                CanvasColorBox.Text = theme.CanvasColorHex;
                InkColorBox.Text = theme.InkColorHex;
                EventNameBox.Text = theme.EventName;
                _existingThemeLogoPath = theme.LogoImagePath;
                _pendingThemeLogoPath = null;
                SelectedThemeLogoText.Text = theme.LogoImagePath is null
                    ? "No logo selected."
                    : System.IO.Path.GetFileName(theme.LogoImagePath);

                _currentScreenSettings = location.Screen;
                LoadCameraSettings(location.Screen);

                CaptureSettings capture = location.Capture;
                CaptureModePhotoRadio.IsChecked = capture.Mode == "Photo";
                CaptureModeGifRadio.IsChecked = capture.Mode == "GIF";
                CaptureModeBoomerangRadio.IsChecked = capture.Mode == "Boomerang";
                CaptureModeVideoRadio.IsChecked = capture.Mode == "Video";
                AlsoCreateGifCheckBox.IsChecked = capture.AlsoCreateGif;
                FrameCountBox.Text = capture.FrameCount.ToString();
                FrameDelayBox.Text = capture.FrameDelayMs.ToString();
                VideoDurationBox.Text = capture.VideoDurationSeconds.ToString();

                EffectsSettings effects = location.Effects;
                SetOnOffToggle(BeautyFilterCheckBox, effects.BeautyFilterEnabled);
                SetOnOffToggle(BeautyFilterAlsoDuringCountdownCheckBox, effects.BeautyFilterAlsoDuringCountdown);
                SetOnOffToggle(FiltersEnabledCheckBox, effects.FiltersEnabled);
                FiltersModeAskRadio.IsChecked = effects.FiltersMode != "Auto";
                FiltersModeAutoRadio.IsChecked = effects.FiltersMode == "Auto";
                SetOnOffToggle(PostProcessingEnabledCheckBox, effects.PostProcessingEnabled);
                PostProcessingApplicationPathBox.Text = effects.PostProcessingApplicationPath ?? string.Empty;
                SetOnOffToggle(StickersEnabledCheckBox, effects.StickersEnabled);
                SetOnOffToggle(WatermarkEnabledCheckBox, effects.WatermarkEnabled);
                _existingWatermarkPath = effects.WatermarkImagePath;
                _pendingWatermarkPath = null;
                SelectedWatermarkText.Text = effects.WatermarkImagePath is null
                    ? "No watermark selected."
                    : System.IO.Path.GetFileName(effects.WatermarkImagePath);

                GreenScreenSettings greenScreen = location.GreenScreen;
                GreenScreenEnabledCheckBox.IsChecked = greenScreen.Enabled;
                _existingGreenScreenBackgroundPath = greenScreen.BackgroundImagePath;
                _pendingGreenScreenBackgroundPath = null;
                SelectedGreenScreenBackgroundText.Text = greenScreen.BackgroundImagePath is null
                    ? "No background selected."
                    : System.IO.Path.GetFileName(greenScreen.BackgroundImagePath);

                SurveyEnabledCheckBox.IsChecked = location.Survey.Enabled;

                VirtualAttendantSettings attendant = location.VirtualAttendant;
                AttendantEnabledCheckBox.IsChecked = attendant.Enabled;
                AttendantStyleCombo.SelectedIndex = attendant.Style == "Formal" ? 1 : 0;
                _randomizeByStage[BoothState.Consent] = attendant.RandomizeConsent;
                _randomizeByStage[BoothState.Countdown] = attendant.RandomizeCountdown;
                _randomizeByStage[BoothState.Capturing] = attendant.RandomizeCapturing;
                _randomizeByStage[BoothState.Reviewing] = attendant.RandomizeReviewing;
                _randomizeByStage[BoothState.Printing] = attendant.RandomizePrinting;
                _randomizeByStage[BoothState.Complete] = attendant.RandomizeComplete;

                DisclaimerSettings disclaimer = location.Disclaimer;
                DisclaimerHeaderBox.Text = disclaimer.Header;
                DisclaimerTextBox.Text = disclaimer.Text;

                SharingSettings sharing = location.Sharing;
                EmailEnabledCheckBox.IsChecked = sharing.EmailEnabled;
                SmsEnabledCheckBox.IsChecked = sharing.SmsEnabled;
                QrEnabledCheckBox.IsChecked = sharing.QrEnabled;

                PrintOptions printOptions = location.PrintOptions;
                PrintAutomaticallyCheckBox.IsChecked = printOptions.PrintAutomatically;
                ShowPrintButtonCheckBox.IsChecked = printOptions.ShowPrintButton;
                PrintLimitPerEventBox.Text = printOptions.PrintLimitPerEvent.ToString();
                PrintLimitPerSessionBox.Text = printOptions.PrintLimitPerSession.ToString();
                PrintSharpeningLowRadio.IsChecked = printOptions.PrintSharpening == "Low";
                PrintSharpeningMediumRadio.IsChecked = printOptions.PrintSharpening == "Medium";
                PrintSharpeningHighRadio.IsChecked = printOptions.PrintSharpening == "High";

                await LoadFramesAsync();
                await LoadGuestbookVideosAsync();
                await LoadSurveyQuestionsAsync();
                await LoadSurveyResponsesAsync();
                await LoadAttendantClipsAsync();
            }
        }
        catch (Exception ex)
        {
            SessionsTodayText.Text = $"Couldn't load: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    /// <summary>Populates Camera Settings' controls -- Enable Webcams/Webcam
    /// Resolution/Audio Input are new here; Mirror/Rotation reuse the same
    /// ScreenSettings fields ScreenTemplateEditorWindow's own Settings tab also
    /// edits (two admin surfaces over the same two columns, same as dslrBooth's
    /// own Camera Settings and Screen Editor both touching mirror/rotate). The
    /// audio device list is re-enumerated on every load rather than cached --
    /// it's a cheap winmm call and a USB mic could be plugged in between opens.</summary>
    private void LoadCameraSettings(ScreenSettings screen)
    {
        SetOnOffToggle(EnableWebcamsCheckBox, screen.EnableWebcams);
        WebcamResolutionSlider.Value = screen.WebcamResolutionQuality;
        SetOnOffToggle(CameraMirrorLiveViewCheckBox, screen.MirrorLiveView);
        CameraRotationCombo.SelectedIndex = screen.LiveViewRotation switch
        {
            90 => 1,
            180 => 2,
            270 => 3,
            _ => 0,
        };

        AudioInputCombo.Items.Clear();
        AudioInputCombo.Items.Add(new ComboBoxItem { Content = "System Default", Tag = null });
        foreach (string deviceName in AudioInputDevices.EnumerateNames())
        {
            AudioInputCombo.Items.Add(new ComboBoxItem { Content = deviceName, Tag = deviceName });
        }
        AudioInputCombo.SelectedIndex = 0;
        if (screen.AudioInputDeviceName is string savedDeviceName)
        {
            for (int i = 0; i < AudioInputCombo.Items.Count; i++)
            {
                if (((ComboBoxItem)AudioInputCombo.Items[i]).Tag as string == savedDeviceName)
                {
                    AudioInputCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private async Task LoadGuestbookVideosAsync()
    {
        var videos = await _repository.GetRecentGuestbookVideosAsync();
        GuestbookVideosList.ItemsSource = videos
            .Select(v => new GuestbookVideoRow(
                v.GuestbookVideoId, v.FilePath,
                $"Session {v.SessionId} -- {v.DurationSeconds}s -- {v.RecordedAt:g}"))
            .ToList();
        GuestbookVideosEmptyText.Visibility = videos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenGuestbookVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath })
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open the recording: {ex.Message}", "Focus & Snap -- admin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void DeleteGuestbookVideoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int guestbookVideoId })
        {
            await _repository.DeleteGuestbookVideoAsync(guestbookVideoId);
            await LoadGuestbookVideosAsync();
        }
    }

    private async Task LoadFramesAsync()
    {
        var frames = await _frames.GetAllByLocationAsync(_locationId);
        FramesList.ItemsSource = frames;
        FramesEmptyText.Visibility = frames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Same list, reused for the Effects & Stickers card's quick prev/next
        // preview -- see the Stickers card's own comment for why this reuses
        // the Frame library instead of a second asset store.
        _stickerFrames = frames;
        _stickerPreviewIndex = 0;
        UpdateStickerPreviewText();
    }

    private void UpdateStickerPreviewText()
    {
        if (_stickerFrames.Count == 0)
        {
            StickerPreviewText.Text = "No stickers added yet -- dslrBooth-style built-in defaults are used instead.";
            return;
        }

        _stickerPreviewIndex = ((_stickerPreviewIndex % _stickerFrames.Count) + _stickerFrames.Count) % _stickerFrames.Count;
        FrameRecord current = _stickerFrames[_stickerPreviewIndex];
        StickerPreviewText.Text = $"{_stickerPreviewIndex + 1} of {_stickerFrames.Count}: {current.Name}{(current.IsActive ? "" : " (inactive)")}";
    }

    private void PreviousStickerButton_Click(object sender, RoutedEventArgs e)
    {
        _stickerPreviewIndex--;
        UpdateStickerPreviewText();
    }

    private void NextStickerButton_Click(object sender, RoutedEventArgs e)
    {
        _stickerPreviewIndex++;
        UpdateStickerPreviewText();
    }

    /// <summary>Quick-add shortcut for the Effects & Stickers card: same copy-
    /// into-Assets/Frames-then-insert behavior as AddFrameButton_Click, just
    /// without a separate name field -- the file's own name stands in, and an
    /// admin who wants to rename/reorder/retire it still has the full Frame
    /// library further down in General.</summary>
    private async void ChooseStickerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Transparent PNG (*.png)|*.png",
            Title = "Choose a sticker/prop overlay image",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ChooseStickerButton.IsEnabled = false;
        try
        {
            string framesDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Frames");
            System.IO.Directory.CreateDirectory(framesDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(dialog.FileName)}";
            string storedPath = System.IO.Path.Combine(framesDirectory, storedFileName);
            System.IO.File.Copy(dialog.FileName, storedPath, overwrite: true);

            var existing = await _frames.GetAllByLocationAsync(_locationId);
            await _frames.InsertAsync(_locationId, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), storedPath, sortOrder: existing.Count);

            await LoadFramesAsync();
            _stickerPreviewIndex = _stickerFrames.Count - 1;
            UpdateStickerPreviewText();
        }
        catch (Exception ex)
        {
            StickerPreviewText.Text = $"Couldn't add sticker: {ex.Message}";
        }
        finally
        {
            ChooseStickerButton.IsEnabled = true;
        }
    }

    private void ConfigureBeautyFilterButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Skin-smoothing intensity controls aren't built yet -- this toggle is saved but not yet applied to captured photos (real beauty filtering needs face detection, which is separate, unbuilt work).",
            "Focus & Snap", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ConfigureFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        new FilterLibraryWindow(_locationId) { Owner = this }.ShowDialog();
    }

    private void ChoosePostProcessingApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Choose the post-processing application",
        };
        if (dialog.ShowDialog() == true)
        {
            PostProcessingApplicationPathBox.Text = dialog.FileName;
        }
    }

    private void BrowseFrameImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a frame overlay image",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingFrameImagePath = dialog.FileName;
            SelectedFrameImageText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    /// <summary>Copies the chosen image into a local Assets/Frames folder (same
    /// "own local copy, not a reference to wherever the admin picked it from"
    /// reasoning MockCameraService's captures/ folder already uses) and inserts
    /// the Frame row.</summary>
    private async void AddFrameButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NewFrameNameBox.Text.Trim();
        if (name.Length == 0 || _pendingFrameImagePath is null)
        {
            FrameStatusText.Text = "Enter a name and choose an image first.";
            FrameStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        AddFrameButton.IsEnabled = false;
        try
        {
            string framesDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Frames");
            System.IO.Directory.CreateDirectory(framesDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingFrameImagePath)}";
            string storedPath = System.IO.Path.Combine(framesDirectory, storedFileName);
            System.IO.File.Copy(_pendingFrameImagePath, storedPath, overwrite: true);

            var existing = await _frames.GetAllByLocationAsync(_locationId);
            await _frames.InsertAsync(_locationId, name, storedPath, sortOrder: existing.Count);

            NewFrameNameBox.Text = string.Empty;
            _pendingFrameImagePath = null;
            SelectedFrameImageText.Text = "No image selected.";
            FrameStatusText.Text = "Frame added -- available to the next guest.";
            FrameStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            await LoadFramesAsync();
        }
        catch (Exception ex)
        {
            FrameStatusText.Text = $"Couldn't add frame: {ex.Message}";
            FrameStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            AddFrameButton.IsEnabled = true;
        }
    }

    private async void FrameActiveCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: int frameId } checkBox)
        {
            await _frames.SetActiveAsync(frameId, checkBox.IsChecked == true);
            await LoadFramesAsync();
        }
    }

    private async void DeleteFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int frameId })
        {
            await _frames.DeleteAsync(frameId);
            await LoadFramesAsync();
        }
    }

    private async Task LoadSurveyQuestionsAsync()
    {
        var questions = await _survey.GetAllByLocationAsync(_locationId);
        SurveyQuestionsList.ItemsSource = questions;
        SurveyQuestionsEmptyText.Visibility = questions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadSurveyResponsesAsync()
    {
        var responses = await _survey.GetResponsesByLocationAsync(_locationId);
        SurveyResponsesList.ItemsSource = responses
            .Select(r => $"Session {r.SessionId} -- \"{r.QuestionText}\" -> {r.Answer} ({r.RecordedAt:g})")
            .ToList();
        SurveyResponsesEmptyText.Visibility = responses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddSurveyQuestionButton_Click(object sender, RoutedEventArgs e)
    {
        string text = NewSurveyQuestionBox.Text.Trim();
        if (text.Length == 0)
        {
            SurveyQuestionStatusText.Text = "Enter a question first.";
            SurveyQuestionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        AddSurveyQuestionButton.IsEnabled = false;
        try
        {
            var existing = await _survey.GetAllByLocationAsync(_locationId);
            await _survey.InsertQuestionAsync(_locationId, text, sortOrder: existing.Count);

            NewSurveyQuestionBox.Text = string.Empty;
            SurveyQuestionStatusText.Text = "Question added -- shown to the next guest.";
            SurveyQuestionStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            await LoadSurveyQuestionsAsync();
        }
        catch (Exception ex)
        {
            SurveyQuestionStatusText.Text = $"Couldn't add question: {ex.Message}";
            SurveyQuestionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            AddSurveyQuestionButton.IsEnabled = true;
        }
    }

    private async void DeleteSurveyQuestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int surveyQuestionId })
        {
            await _survey.DeleteQuestionAsync(surveyQuestionId);
            await LoadSurveyQuestionsAsync();
        }
    }

    private void BrowseWatermarkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a watermark image",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingWatermarkPath = dialog.FileName;
            SelectedWatermarkText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    private void BrowseGreenScreenBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Choose a green screen background image",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingGreenScreenBackgroundPath = dialog.FileName;
            SelectedGreenScreenBackgroundText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    /// <summary>Saves Capture/Effects/Green Screen/Survey/Disclaimer/Print/Sharing
    /// settings added in Phase 1 (BUILD_PLAN.md's dslrBooth feature-parity plan).
    /// Kept as its own button/status text, same one-save-button-per-section
    /// precedent as SaveSettingsButton/SaveThemeButton above. Also reachable via
    /// SaveSurveySettingsButton directly on the Survey page -- that page's
    /// Enabled checkbox otherwise has no save path of its own until the wizard
    /// reaches Print Setup (the only page this button used to live on), so a
    /// guest opening straight to Survey from the settings dropdown and closing
    /// the window afterward would silently discard the toggle. Both buttons
    /// save the exact same bundle (not just Survey) since that's already how
    /// this data is grouped in the database.</summary>
    private async void SaveParitySettingsButton_Click(object sender, RoutedEventArgs e)
        => await SaveParitySettingsAsync(SaveParitySettingsButton, ParitySettingsStatusText);

    private async void SaveSurveySettingsButton_Click(object sender, RoutedEventArgs e)
        => await SaveParitySettingsAsync(SaveSurveySettingsButton, SurveySettingsStatusText);

    private async Task SaveParitySettingsAsync(Button triggerButton, TextBlock statusText)
    {
        if (!int.TryParse(FrameCountBox.Text, out int frameCount) || frameCount <= 0
            || !int.TryParse(FrameDelayBox.Text, out int frameDelayMs) || frameDelayMs <= 0
            || !int.TryParse(VideoDurationBox.Text, out int videoDurationSeconds) || videoDurationSeconds <= 0)
        {
            statusText.Text = "Frame count, frame delay, and video duration must be whole numbers greater than 0.";
            statusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        if (!int.TryParse(PrintLimitPerEventBox.Text, out int printLimitPerEvent) || printLimitPerEvent <= 0
            || !int.TryParse(PrintLimitPerSessionBox.Text, out int printLimitPerSession) || printLimitPerSession <= 0)
        {
            statusText.Text = "Print limits must be whole numbers greater than 0.";
            statusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        string captureMode = CaptureModeGifRadio.IsChecked == true ? "GIF"
            : CaptureModeBoomerangRadio.IsChecked == true ? "Boomerang"
            : CaptureModeVideoRadio.IsChecked == true ? "Video"
            : "Photo";
        string filtersMode = FiltersModeAutoRadio.IsChecked == true ? "Auto" : "Ask";
        string printSharpening = PrintSharpeningLowRadio.IsChecked == true ? "Low"
            : PrintSharpeningHighRadio.IsChecked == true ? "High"
            : "Medium";

        // Same "own local copy" pattern as SaveThemeButton_Click/AddFrameButton_Click:
        // only copy a newly-picked file, otherwise keep whatever is already on file.
        string? watermarkPath = _existingWatermarkPath;
        if (_pendingWatermarkPath is not null)
        {
            string watermarksDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Watermarks");
            System.IO.Directory.CreateDirectory(watermarksDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingWatermarkPath)}";
            watermarkPath = System.IO.Path.Combine(watermarksDirectory, storedFileName);
            System.IO.File.Copy(_pendingWatermarkPath, watermarkPath, overwrite: true);
        }

        string? greenScreenBackgroundPath = _existingGreenScreenBackgroundPath;
        if (_pendingGreenScreenBackgroundPath is not null)
        {
            string greenScreenDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "GreenScreen");
            System.IO.Directory.CreateDirectory(greenScreenDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingGreenScreenBackgroundPath)}";
            greenScreenBackgroundPath = System.IO.Path.Combine(greenScreenDirectory, storedFileName);
            System.IO.File.Copy(_pendingGreenScreenBackgroundPath, greenScreenBackgroundPath, overwrite: true);
        }

        var capture = new CaptureSettings(captureMode, AlsoCreateGifCheckBox.IsChecked == true, frameCount, frameDelayMs, videoDurationSeconds);
        int liveViewRotation = CameraRotationCombo.SelectedItem is ComboBoxItem { Tag: string rotationTag } ? int.Parse(rotationTag) : 0;
        string? audioInputDeviceName = AudioInputCombo.SelectedItem is ComboBoxItem { Tag: string deviceName } ? deviceName : null;
        var screen = _currentScreenSettings with
        {
            MirrorLiveView = CameraMirrorLiveViewCheckBox.IsChecked == true,
            LiveViewRotation = liveViewRotation,
            EnableWebcams = EnableWebcamsCheckBox.IsChecked == true,
            WebcamResolutionQuality = (int)WebcamResolutionSlider.Value,
            AudioInputDeviceName = audioInputDeviceName,
        };
        string? postProcessingApplicationPath = PostProcessingApplicationPathBox.Text.Trim() is { Length: > 0 } postProcessingPath ? postProcessingPath : null;
        var effects = new EffectsSettings(
            BeautyFilterCheckBox.IsChecked == true, filtersMode, watermarkPath,
            BeautyFilterAlsoDuringCountdownCheckBox.IsChecked == true,
            FiltersEnabledCheckBox.IsChecked == true,
            PostProcessingEnabledCheckBox.IsChecked == true,
            postProcessingApplicationPath,
            StickersEnabledCheckBox.IsChecked == true,
            WatermarkEnabledCheckBox.IsChecked == true);
        var greenScreen = new GreenScreenSettings(GreenScreenEnabledCheckBox.IsChecked == true, greenScreenBackgroundPath);
        var survey = new SurveySettings(SurveyEnabledCheckBox.IsChecked == true);
        var disclaimer = new DisclaimerSettings(DisclaimerHeaderBox.Text.Trim(), DisclaimerTextBox.Text);
        var printOptions = new PrintOptions(
            PrintAutomaticallyCheckBox.IsChecked == true, ShowPrintButtonCheckBox.IsChecked == true,
            printLimitPerEvent, printLimitPerSession, printSharpening);
        var sharing = new SharingSettings(EmailEnabledCheckBox.IsChecked == true, SmsEnabledCheckBox.IsChecked == true, QrEnabledCheckBox.IsChecked == true);

        triggerButton.IsEnabled = false;
        try
        {
            await _locations.UpdateDslrBoothParitySettingsAsync(_locationId, capture, screen, effects, greenScreen, survey, disclaimer, printOptions, sharing);
            _currentScreenSettings = screen;
            _existingWatermarkPath = watermarkPath;
            _pendingWatermarkPath = null;
            _existingGreenScreenBackgroundPath = greenScreenBackgroundPath;
            _pendingGreenScreenBackgroundPath = null;
            statusText.Text = "Saved -- takes effect for the next guest session.";
            statusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            statusText.Text = $"Couldn't save: {ex.Message}";
            statusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            triggerButton.IsEnabled = true;
        }
    }

    private async void SaveAttendantSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        string style = AttendantStyleCombo.SelectedItem is ComboBoxItem { Tag: "Formal" } ? "Formal" : "Friendly";
        var settings = new VirtualAttendantSettings(
            AttendantEnabledCheckBox.IsChecked == true,
            style,
            _randomizeByStage.GetValueOrDefault(BoothState.Consent),
            _randomizeByStage.GetValueOrDefault(BoothState.Countdown),
            _randomizeByStage.GetValueOrDefault(BoothState.Capturing),
            _randomizeByStage.GetValueOrDefault(BoothState.Reviewing),
            _randomizeByStage.GetValueOrDefault(BoothState.Printing),
            _randomizeByStage.GetValueOrDefault(BoothState.Complete));

        SaveAttendantSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateVirtualAttendantSettingsAsync(_locationId, settings);
            AttendantSettingsStatusText.Text = "Saved -- takes effect for the very next state transition.";
            AttendantSettingsStatusText.Foreground = (Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            AttendantSettingsStatusText.Text = $"Couldn't save: {ex.Message}";
            AttendantSettingsStatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            SaveAttendantSettingsButton.IsEnabled = true;
        }
    }

    /// <summary>Rebuilds the Virtual Attendant tile grid from scratch against the
    /// current clip pool -- called after every load and after any add/delete/
    /// reorder, same "just reload" simplicity FramesList/SurveyQuestionsList
    /// etc. already use elsewhere in this file.</summary>
    private async Task LoadAttendantClipsAsync()
    {
        List<VirtualAttendantClipRecord> clips = await _attendantClips.GetAllByLocationAsync(_locationId);

        AttendantStageCardsPanel.Children.Clear();
        foreach ((BoothState stage, string label, bool supportsRandomize) in AttendantStageDefinitions)
        {
            List<VirtualAttendantClipRecord> clipsForStage = clips
                .Where(c => c.Stage == stage.ToString())
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.ClipId)
                .ToList();
            AttendantStageCardsPanel.Children.Add(BuildAttendantStageCard(stage, label, supportsRandomize, clipsForStage));
        }
    }

    /// <summary>Builds one Virtual Attendant tile -- same "generate a tile per
    /// item in code" pattern EventLauncherWindow.BuildTile already established
    /// for its own event-picker grid, used here instead of an ItemsControl/
    /// DataTemplate since each card needs its own local prev/next preview index
    /// and (for six stages) a Randomize toggle backed by _randomizeByStage.</summary>
    private Border BuildAttendantStageCard(BoothState stage, string label, bool supportsRandomize, List<VirtualAttendantClipRecord> clipsForStage)
    {
        var content = new StackPanel();

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelText, 0);
        headerRow.Children.Add(labelText);

        if (supportsRandomize)
        {
            var randomizeCheckBox = new CheckBox
            {
                Content = "Randomize",
                Foreground = (Brush)FindResource("InkBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                IsChecked = _randomizeByStage.GetValueOrDefault(stage),
            };
            randomizeCheckBox.Checked += (_, _) => _randomizeByStage[stage] = true;
            randomizeCheckBox.Unchecked += (_, _) => _randomizeByStage[stage] = false;
            Grid.SetColumn(randomizeCheckBox, 1);
            headerRow.Children.Add(randomizeCheckBox);
        }
        content.Children.Add(headerRow);

        var previewText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 8, 0, 8),
        };
        content.Children.Add(previewText);

        int previewIndex = 0;
        void UpdatePreviewText()
        {
            if (clipsForStage.Count == 0)
            {
                previewText.Text = "No clip added yet.";
                return;
            }
            previewIndex = ((previewIndex % clipsForStage.Count) + clipsForStage.Count) % clipsForStage.Count;
            previewText.Text = $"{previewIndex + 1} of {clipsForStage.Count}: {System.IO.Path.GetFileName(clipsForStage[previewIndex].FilePath)}";
        }
        UpdatePreviewText();

        var buttonsRow = new WrapPanel();

        var chooseButton = new Button { Content = "Choose", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
        chooseButton.Click += async (_, _) => await ChooseAttendantClipAsync(stage);
        buttonsRow.Children.Add(chooseButton);

        if (clipsForStage.Count > 0)
        {
            var prevButton = new Button { Content = "<", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            prevButton.Click += (_, _) => { previewIndex--; UpdatePreviewText(); };
            buttonsRow.Children.Add(prevButton);

            var nextButton = new Button { Content = ">", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            nextButton.Click += (_, _) => { previewIndex++; UpdatePreviewText(); };
            buttonsRow.Children.Add(nextButton);

            var upButton = new Button { Content = "^", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            upButton.Click += async (_, _) => await MoveAttendantClipAsync(clipsForStage[previewIndex].ClipId, up: true);
            buttonsRow.Children.Add(upButton);

            var downButton = new Button { Content = "v", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            downButton.Click += async (_, _) => await MoveAttendantClipAsync(clipsForStage[previewIndex].ClipId, up: false);
            buttonsRow.Children.Add(downButton);

            var deleteButton = new Button { Content = "Delete", Padding = new Thickness(10, 4, 10, 4) };
            deleteButton.Click += async (_, _) =>
            {
                await _attendantClips.DeleteAsync(clipsForStage[previewIndex].ClipId);
                await LoadAttendantClipsAsync();
            };
            buttonsRow.Children.Add(deleteButton);
        }
        content.Children.Add(buttonsRow);

        return new Border
        {
            Width = 240,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 12, 12),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("FieldBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    /// <summary>Copies the chosen clip into a local Assets/AttendantClips folder (same
    /// "own local copy" reasoning AddFrameButton_Click already established) and inserts
    /// the VirtualAttendantClip row at the end of that stage's existing pool -- the
    /// stage comes from which card's Choose button was clicked, not a separate picker,
    /// since the tile grid already has one card per stage.</summary>
    private async Task ChooseAttendantClipAsync(BoothState stage)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio/video files (*.mp3;*.wav;*.mp4;*.wmv)|*.mp3;*.wav;*.mp4;*.wmv",
            Title = "Choose a Virtual Attendant clip",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string clipsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AttendantClips");
        System.IO.Directory.CreateDirectory(clipsDirectory);
        string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(dialog.FileName)}";
        string storedPath = System.IO.Path.Combine(clipsDirectory, storedFileName);
        System.IO.File.Copy(dialog.FileName, storedPath, overwrite: true);

        string stageName = stage.ToString();
        var existingInStage = (await _attendantClips.GetAllByLocationAsync(_locationId))
            .Where(c => c.Stage == stageName)
            .ToList();
        await _attendantClips.InsertAsync(_locationId, stage, storedPath, sortOrder: existingInStage.Count);

        await LoadAttendantClipsAsync();
    }

    /// <summary>Swaps the given clip's SortOrder with whichever clip is adjacent to it
    /// within the same stage (two UpdateSortOrderAsync calls), rather than renumbering
    /// the whole pool -- a no-op if it's already first/last in its stage.</summary>
    private async Task MoveAttendantClipAsync(int clipId, bool up)
    {
        List<VirtualAttendantClipRecord> allClips = await _attendantClips.GetAllByLocationAsync(_locationId);
        VirtualAttendantClipRecord? current = allClips.FirstOrDefault(c => c.ClipId == clipId);
        if (current is null)
        {
            return;
        }

        List<VirtualAttendantClipRecord> sameStage = allClips
            .Where(c => c.Stage == current.Stage)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.ClipId)
            .ToList();
        int index = sameStage.FindIndex(c => c.ClipId == clipId);
        int swapIndex = up ? index - 1 : index + 1;
        if (swapIndex < 0 || swapIndex >= sameStage.Count)
        {
            return;
        }

        VirtualAttendantClipRecord swapWith = sameStage[swapIndex];
        await _attendantClips.UpdateSortOrderAsync(current.ClipId, swapWith.SortOrder);
        await _attendantClips.UpdateSortOrderAsync(swapWith.ClipId, current.SortOrder);
        await LoadAttendantClipsAsync();
    }
}
