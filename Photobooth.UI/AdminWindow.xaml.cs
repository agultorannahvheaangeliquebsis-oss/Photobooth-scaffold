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
    private readonly SharingLogRepository _sharingLog = new();

    // Which event/location this dashboard is editing. A booth machine can now
    // have several saved events (see EventLauncherWindow) -- _requestedLocationId
    // is the one the caller asked for (e.g. the event KioskWindow is actually
    // running), falling back to the first Location row if null, same as this
    // window's original "one booth machine has one location" assumption did.
    private readonly int? _requestedLocationId;
    private int _locationId;

    /// <summary>Set by KioskWindow when this dashboard is opened from a live
    /// kiosk session (see KioskAdminViewModel.OnLockChanged) -- lets the Show
    /// Lock Screen section's Lock Now/Unlock apply immediately to that
    /// session instead of only on its next re-read at Idle. Null when this
    /// window is opened without a live kiosk behind it (e.g. standalone),
    /// in which case Lock Now/Unlock still persists to the DB, it just has
    /// no live session to notify.</summary>
    private readonly Action<bool>? _onLockChanged;

    /// <summary>Real captures directory this event's photos/GIFs/videos are
    /// written to -- see BoothCompositionRoot.ResolveCapturesDirectory for why
    /// this isn't simply relative to this window's own process directory.</summary>
    public static string CapturesDirectory { get; } = BoothCompositionRoot.ResolveCapturesDirectory();

    private List<FrameRecord> _stickerFrames = new();
    private int _stickerPreviewIndex;
    private string? _pendingThemeLogoPath;
    private string? _existingThemeLogoPath;
    private PrintTemplate _currentPrintTemplate = PrintTemplate.Default;
    private BoothTheme _currentTheme = BoothTheme.Default;

    private string? _pendingWatermarkPath;
    private string? _existingWatermarkPath;
    private string? _pendingGreenScreenBackgroundPath;
    private string? _existingGreenScreenBackgroundPath;

    // Show Lock Screen's current state, and the rows behind Sharing Status'
    // list (kept so RetrySharingLogButton_Click can look up a row's
    // Method/Destination/PhotoUrl from just the SharingLogId its Tag carries).
    private bool _isLocked;
    private List<SharingLogRow> _sharingLogRows = new();

    // DPAPI-protected (SecretProtector) values already on file -- kept so
    // Save can fall back to them when the admin leaves the password/token
    // box blank, same "own local copy, only overwrite on a real change"
    // pattern _existingWatermarkPath/_existingGreenScreenBackgroundPath
    // already establish for files.
    private string _existingEmailSmtpPasswordProtected = "";
    private string _existingTwilioAuthTokenProtected = "";

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

    public AdminWindow(int? locationId = null, string initialSection = "General", Action<bool>? onLockChanged = null)
    {
        InitializeComponent();
        _requestedLocationId = locationId;
        _onLockChanged = onLockChanged;
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

    /// <summary>Friendly titles for a dropdown-menu entry with no dedicated
    /// section panel -- empty now that every entry from the original
    /// dslrBooth-parity menu has a real section (see SectionPanels below).
    /// Kept as a dictionary (rather than removed outright) since ShowSection's
    /// fallback branch still needs somewhere to route an unrecognized key.</summary>
    private static readonly Dictionary<string, string> PlaceholderTitles = new();

    private Dictionary<string, FrameworkElement>? _sectionPanels;

    private Dictionary<string, FrameworkElement> SectionPanels => _sectionPanels ??= new()
    {
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
        ["Slideshow"] = SlideshowSectionPanel,
        ["SharingStatus"] = SharingStatusSectionPanel,
        ["ExportEvent"] = ExportEventSectionPanel,
        ["EventFolder"] = EventFolderSectionPanel,
        ["RemoteControl"] = RemoteControlSectionPanel,
        ["ShowLockScreen"] = ShowLockScreenSectionPanel,
        ["Language"] = LanguageSectionPanel,
        ["Subscription"] = SubscriptionSectionPanel,
        ["About"] = AboutSectionPanel,
        ["Help"] = HelpSectionPanel,
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

    /// <summary>Print Layout is its own full-screen editor window
    /// (PrintTemplateEditorWindow), not a ShowSection panel -- see
    /// OpenPrintTemplateEditorAsync's own doc comment.</summary>
    private async void PrintLayoutMenuLink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        NavMenuToggle.IsChecked = false;
        await OpenPrintTemplateEditorAsync();
    }

    private async void PrintLayoutBreadcrumbLink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => await OpenPrintTemplateEditorAsync();

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

    /// <summary>Countdown/Glam/PIN save through LocationRepository.
    /// UpdateSettingsAsync, which also takes a PrintTemplate parameter for its
    /// geometry columns -- always passed through as _currentPrintTemplate
    /// unchanged here, since print geometry now saves independently via
    /// PrintTemplateEditorWindow's own UpdatePrintGeometryAsync (see
    /// OpenPrintTemplateEditorAsync), so General's Save can't clobber it.</summary>
    private void SetSettingsStatus(string text, bool isError)
    {
        System.Windows.Media.Brush brush = isError
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
        SettingsStatusText.Text = text;
        SettingsStatusText.Foreground = brush;
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountdownSecondsBox.Text, out int countdownSeconds) || countdownSeconds <= 0)
        {
            SetSettingsStatus("Countdown must be a whole number of seconds greater than 0.", isError: true);
            return;
        }

        string adminPin = AdminPinBox.Text.Trim();
        if (adminPin.Length == 0)
        {
            SetSettingsStatus("Admin PIN can't be blank.", isError: true);
            return;
        }

        SaveSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSettingsAsync(_locationId, countdownSeconds, GlamFilterCheckBox.IsChecked == true, _currentPrintTemplate, adminPin);
            BoothSettingsChanged.Publish(_locationId);
            SetSettingsStatus("Saved -- applied to the live kiosk.", isError: false);
        }
        catch (Exception ex)
        {
            SetSettingsStatus($"Couldn't save: {ex.Message}", isError: true);
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
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
            BoothSettingsChanged.Publish(_locationId);
            _existingThemeLogoPath = logoPath;
            _pendingThemeLogoPath = null;
            ThemeStatusText.Text = "Saved -- applied to the live kiosk.";
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

    /// <summary>Opens the merged Print Layout editor (paper size, print
    /// template elements, and the Frame library) -- reached from the nav menu
    /// (PrintLayoutMenuLink_MouseLeftButtonDown), General's own breadcrumb
    /// (PrintLayoutBreadcrumbLink_MouseLeftButtonDown), and chained into from
    /// the Screen Editor's "Print Layout &gt;" breadcrumb below.</summary>
    private async Task OpenPrintTemplateEditorAsync()
    {
        // _currentPrintTemplate.Elements is always empty here -- LocationRepository.
        // GetAllAsync never queries PrintTemplateElement, only SqlBoothSettingsProvider
        // does that for the live BoothStateMachine path -- so the editor needs its own
        // fetch here or it would always open showing a blank canvas regardless of what
        // was last saved.
        List<PrintTemplateElement> liveElements = await new PrintTemplateElementRepository().GetAllByLocationAsync(_locationId);
        var editor = new PrintTemplateEditorWindow(_currentPrintTemplate with { Elements = liveElements }, _locationId) { Owner = this };
        bool saved = editor.ShowDialog() == true;
        string? requestedNavigation = editor.RequestedNavigation;
        if (saved)
        {
            // The editor already persisted the elements itself on Save --
            // reload from source of truth same as the Frame section already
            // does after add/delete, rather than trust the in-memory copy.
            await LoadAsync();
        }

        if (requestedNavigation == "ScreenEditor")
        {
            await EditScreenLayoutButtonAsync();
        }
        else if (requestedNavigation is not null)
        {
            ShowSection(requestedNavigation);
        }
    }

    /// <summary>Opens the Screen Editor -- chained into from
    /// OpenPrintTemplateEditorAsync when the print editor's own "&lt; Screen
    /// Editor" breadcrumb is clicked, and this method's own chain back the other
    /// direction via requestedNavigation == "PrintLayout" below.</summary>
    private async Task EditScreenLayoutButtonAsync()
    {
        var existing = await new ScreenTemplateElementRepository().GetAllByLocationAsync(_locationId);
        var editor = new ScreenTemplateEditorWindow(existing, _locationId, _currentScreenSettings, _currentTheme) { Owner = this };
        bool saved = editor.ShowDialog() == true;
        string? requestedNavigation = editor.RequestedNavigation;
        if (saved)
        {
            // The editor's Settings tab can change ScreenSettings (Booth
            // Icons/live view show-mirror-rotate), which LoadAsync populates
            // into _currentScreenSettings -- reload so a second edit doesn't
            // clobber that change back to what was loaded before this one.
            // ScreenTemplateElement itself still needs no reload here (not
            // read into any LoadAsync field, read fresh by KioskWindow).
            await LoadAsync();
        }

        if (requestedNavigation == "PrintLayout")
        {
            // Chains straight into the separate Print Layout editor, same
            // "Print Layout >" breadcrumb dslrBooth's own Screen Editor
            // carries -- the two remain distinct windows/pages, this just
            // avoids making the admin close this one and hunt for the menu
            // link themselves.
            await OpenPrintTemplateEditorAsync();
        }
        else if (requestedNavigation is not null)
        {
            // Virtual Attendant / Countdown settings (-> CaptureSettings) /
            // Sharing Settings breadcrumbs -- these sections already live in
            // this same AdminWindow, so no new window is needed, just a
            // section switch (see ShowSection).
            ShowSection(requestedNavigation);
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

                BoothTheme theme = location.Theme;
                _currentTheme = theme;
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
                TwitterEnabledCheckBox.IsChecked = sharing.TwitterEnabled;
                PrintEnabledCheckBox.IsChecked = sharing.PrintEnabled;
                EmailFromAddressBox.Text = sharing.EmailFromAddress;
                EmailSubjectBox.Text = sharing.EmailSubject;
                EmailSmtpHostBox.Text = sharing.EmailSmtpHost;
                EmailSmtpPortBox.Text = sharing.EmailSmtpPort.ToString();
                EmailSmtpUsernameBox.Text = sharing.EmailSmtpUsername;
                EmailUseSslCheckBox.IsChecked = sharing.EmailUseSsl;
                TwilioAccountSidBox.Text = sharing.TwilioAccountSid;
                TwilioFromNumberBox.Text = sharing.TwilioFromNumber;
                // Password/token boxes stay blank -- never round-trip a
                // decrypted secret back into the UI, same reasoning
                // BoothConfiguration set for the DB connection string. The
                // hint line is the only signal of whether one's configured.
                EmailSmtpPasswordBox.Password = string.Empty;
                TwilioAuthTokenBox.Password = string.Empty;
                _existingEmailSmtpPasswordProtected = sharing.EmailSmtpPasswordProtected;
                _existingTwilioAuthTokenProtected = sharing.TwilioAuthTokenProtected;
                EmailSmtpPasswordHintText.Text = sharing.EmailSmtpPasswordProtected.Length > 0
                    ? "A password is already saved. Leave blank to keep it."
                    : "No password saved yet.";
                TwilioAuthTokenHintText.Text = sharing.TwilioAuthTokenProtected.Length > 0
                    ? "An auth token is already saved. Leave blank to keep it."
                    : "No auth token saved yet.";

                PrintOptions printOptions = location.PrintOptions;
                PrintAutomaticallyCheckBox.IsChecked = printOptions.PrintAutomatically;
                ShowPrintButtonCheckBox.IsChecked = printOptions.ShowPrintButton;
                PrintLimitPerEventBox.Text = printOptions.PrintLimitPerEvent.ToString();
                PrintLimitPerSessionBox.Text = printOptions.PrintLimitPerSession.ToString();
                PrintSharpeningLowRadio.IsChecked = printOptions.PrintSharpening == "Low";
                PrintSharpeningMediumRadio.IsChecked = printOptions.PrintSharpening == "Medium";
                PrintSharpeningHighRadio.IsChecked = printOptions.PrintSharpening == "High";

                SlideshowSettings slideshow = location.Slideshow;
                SetOnOffToggle(SlideshowEnabledCheckBox, slideshow.Enabled);
                SlideshowIntervalBox.Text = slideshow.IntervalSeconds.ToString();
                SlideshowTransitionFadeRadio.IsChecked = slideshow.Transition == "Fade";
                SlideshowTransitionSlideRadio.IsChecked = slideshow.Transition == "Slide";
                SlideshowTransitionKenBurnsRadio.IsChecked = slideshow.Transition == "Ken Burns";
                SetOnOffToggle(SlideshowShowLogoCheckBox, slideshow.ShowLogoOverlay);
                SetOnOffToggle(SlideshowShowQrCheckBox, slideshow.ShowQrOverlay);

                SetOnOffToggle(RemoteControlEnabledCheckBox, location.RemoteControlEnabled);
                RemoteControlUrlText.Text = RemoteControlServer.Url;
                RemoteControlStatusText.Text = location.RemoteControlEnabled
                    ? "Enabled -- the running kiosk starts listening the next time it returns to Idle."
                    : "Disabled.";

                _isLocked = location.IsLocked;
                UpdateLockScreenStatusText();

                SubscriptionLicensedToText.Text = location.Name;
                AboutVersionText.Text = $"Version {AppVersionText()}";
                AboutLocationText.Text = $"Location: {location.Name} ({location.Type})";

                await LoadFramesAsync();
                await LoadGuestbookVideosAsync();
                await LoadSurveyQuestionsAsync();
                await LoadSurveyResponsesAsync();
                await LoadAttendantClipsAsync();
                await LoadSharingStatusAsync();
                LoadEventFolder();

                if (!System.IO.Directory.Exists(ExportDestinationBox.Text))
                {
                    ExportDestinationBox.Text = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "EventExports", SafeFileName(location.Name));
                }
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

    /// <summary>Frame library CRUD (add/delete/browse/toggle-active) now lives
    /// entirely in PrintTemplateEditorWindow (see the merged Print Layout
    /// editor) -- this window only still needs the frame *list* itself, to
    /// feed the Effects & Stickers card's quick prev/next preview below.</summary>
    private async Task LoadFramesAsync()
    {
        var frames = await _frames.GetAllByLocationAsync(_locationId);
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

    private async void SaveSharingSettingsButton_Click(object sender, RoutedEventArgs e)
        => await SaveParitySettingsAsync(SaveSharingSettingsButton, SharingSettingsStatusText);

    /// <summary>Builds a SharingSettings from the Sharing Settings section's
    /// current field values -- shared by the real Save path and the two
    /// Send Test buttons, so "what would be saved" and "what gets tested"
    /// can never drift apart. The two secret fields fall back to whatever's
    /// already on file (_existingEmailSmtpPasswordProtected/
    /// _existingTwilioAuthTokenProtected) when left blank, same reasoning
    /// SaveParitySettingsAsync's watermark/green-screen file handling
    /// already establishes for "only overwrite on a real change".</summary>
    private SharingSettings BuildSharingSettingsFromForm()
    {
        string emailPasswordProtected = EmailSmtpPasswordBox.Password.Length > 0
            ? SecretProtector.Protect(EmailSmtpPasswordBox.Password)
            : _existingEmailSmtpPasswordProtected;
        string twilioTokenProtected = TwilioAuthTokenBox.Password.Length > 0
            ? SecretProtector.Protect(TwilioAuthTokenBox.Password)
            : _existingTwilioAuthTokenProtected;
        int.TryParse(EmailSmtpPortBox.Text, out int smtpPort);

        return new SharingSettings(EmailEnabledCheckBox.IsChecked == true, SmsEnabledCheckBox.IsChecked == true, QrEnabledCheckBox.IsChecked == true)
        {
            TwitterEnabled = TwitterEnabledCheckBox.IsChecked == true,
            PrintEnabled = PrintEnabledCheckBox.IsChecked == true,
            EmailFromAddress = EmailFromAddressBox.Text.Trim(),
            EmailSubject = EmailSubjectBox.Text.Trim(),
            EmailSmtpHost = EmailSmtpHostBox.Text.Trim(),
            EmailSmtpPort = smtpPort > 0 ? smtpPort : 587,
            EmailSmtpUsername = EmailSmtpUsernameBox.Text.Trim(),
            EmailUseSsl = EmailUseSslCheckBox.IsChecked == true,
            EmailSmtpPasswordProtected = emailPasswordProtected,
            TwilioAccountSid = TwilioAccountSidBox.Text.Trim(),
            TwilioFromNumber = TwilioFromNumberBox.Text.Trim(),
            TwilioAuthTokenProtected = twilioTokenProtected,
        };
    }

    /// <summary>Adapts one fixed BoothSettings for the two Send Test
    /// buttons -- SmtpEmailDeliveryService/TwilioSmsDeliveryService take an
    /// IBoothSettingsProvider (so they can re-read settings fresh on every
    /// real guest send), but a test send has nothing to re-read: it's
    /// testing the form's current, possibly-unsaved values.</summary>
    private sealed class StaticSettingsProvider(BoothSettings settings) : IBoothSettingsProvider
    {
        public Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(settings);
    }

    private async void SendTestEmailButton_Click(object sender, RoutedEventArgs e)
    {
        string testAddress = TestEmailAddressBox.Text.Trim();
        if (testAddress.Length == 0)
        {
            EmailTestStatusText.Text = "Enter an address to send the test to.";
            EmailTestStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var provider = new StaticSettingsProvider(new BoothSettings(0, false, PrintTemplate.Default) { Sharing = BuildSharingSettingsFromForm() });
        var emailService = new SmtpEmailDeliveryService(provider);

        SendTestEmailButton.IsEnabled = false;
        EmailTestStatusText.Text = "Sending...";
        EmailTestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        try
        {
            await emailService.SendPhotoLinkAsync(testAddress, new Uri("https://example.com/photobooth-test"));
            EmailTestStatusText.Text = $"Test email sent to {testAddress}.";
        }
        catch (Exception ex)
        {
            EmailTestStatusText.Text = $"Test failed: {ex.Message}";
            EmailTestStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SendTestEmailButton.IsEnabled = true;
        }
    }

    private async void SendTestSmsButton_Click(object sender, RoutedEventArgs e)
    {
        string testPhone = TestPhoneNumberBox.Text.Trim();
        if (testPhone.Length == 0)
        {
            SmsTestStatusText.Text = "Enter a phone number to send the test to.";
            SmsTestStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var provider = new StaticSettingsProvider(new BoothSettings(0, false, PrintTemplate.Default) { Sharing = BuildSharingSettingsFromForm() });
        var smsService = new TwilioSmsDeliveryService(provider);

        SendTestSmsButton.IsEnabled = false;
        SmsTestStatusText.Text = "Sending...";
        SmsTestStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        try
        {
            await smsService.SendPhotoLinkAsync(testPhone, new Uri("https://example.com/photobooth-test"));
            SmsTestStatusText.Text = $"Test SMS sent to {testPhone}.";
        }
        catch (Exception ex)
        {
            SmsTestStatusText.Text = $"Test failed: {ex.Message}";
            SmsTestStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SendTestSmsButton.IsEnabled = true;
        }
    }

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
        var sharing = BuildSharingSettingsFromForm();

        triggerButton.IsEnabled = false;
        try
        {
            await _locations.UpdateDslrBoothParitySettingsAsync(_locationId, capture, screen, effects, greenScreen, survey, disclaimer, printOptions, sharing);
            BoothSettingsChanged.Publish(_locationId);
            _currentScreenSettings = screen;
            _existingWatermarkPath = watermarkPath;
            _pendingWatermarkPath = null;
            _existingGreenScreenBackgroundPath = greenScreenBackgroundPath;
            _pendingGreenScreenBackgroundPath = null;
            _existingEmailSmtpPasswordProtected = sharing.EmailSmtpPasswordProtected;
            _existingTwilioAuthTokenProtected = sharing.TwilioAuthTokenProtected;
            EmailSmtpPasswordBox.Password = string.Empty;
            TwilioAuthTokenBox.Password = string.Empty;
            EmailSmtpPasswordHintText.Text = sharing.EmailSmtpPasswordProtected.Length > 0
                ? "A password is already saved. Leave blank to keep it."
                : "No password saved yet.";
            TwilioAuthTokenHintText.Text = sharing.TwilioAuthTokenProtected.Length > 0
                ? "An auth token is already saved. Leave blank to keep it."
                : "No auth token saved yet.";
            statusText.Text = "Saved -- applied to the live kiosk.";
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
            BoothSettingsChanged.Publish(_locationId);
            AttendantSettingsStatusText.Text = "Saved -- applied to the live kiosk.";
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
    /// reorder, same "just reload" simplicity SurveyQuestionsList etc.
    /// already use elsewhere in this file.</summary>
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

    // ================================================================
    // Slideshow
    // ================================================================

    private async void SaveSlideshowSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SlideshowIntervalBox.Text, out int intervalSeconds) || intervalSeconds <= 0)
        {
            SlideshowStatusText.Text = "Seconds per photo must be a whole number greater than 0.";
            SlideshowStatusText.Foreground = Brushes.Firebrick;
            return;
        }

        string transition = SlideshowTransitionSlideRadio.IsChecked == true ? "Slide"
            : SlideshowTransitionKenBurnsRadio.IsChecked == true ? "Ken Burns"
            : "Fade";
        var settings = new SlideshowSettings(
            SlideshowEnabledCheckBox.IsChecked == true, intervalSeconds, transition,
            SlideshowShowLogoCheckBox.IsChecked == true, SlideshowShowQrCheckBox.IsChecked == true);

        SaveSlideshowSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSlideshowSettingsAsync(_locationId, settings);
            BoothSettingsChanged.Publish(_locationId);
            SlideshowStatusText.Text = "Saved.";
            SlideshowStatusText.Foreground = (Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            SlideshowStatusText.Text = $"Couldn't save: {ex.Message}";
            SlideshowStatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            SaveSlideshowSettingsButton.IsEnabled = true;
        }
    }

    private void LaunchSlideshowButton_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(SlideshowIntervalBox.Text, out int intervalSeconds);
        string transition = SlideshowTransitionSlideRadio.IsChecked == true ? "Slide"
            : SlideshowTransitionKenBurnsRadio.IsChecked == true ? "Ken Burns"
            : "Fade";
        var settings = new SlideshowSettings(
            true, intervalSeconds > 0 ? intervalSeconds : SlideshowSettings.Default.IntervalSeconds, transition,
            SlideshowShowLogoCheckBox.IsChecked == true, SlideshowShowQrCheckBox.IsChecked == true);

        // Not modal (.Show, not .ShowDialog) -- a slideshow is meant to run
        // alongside the dashboard (e.g. dragged onto a second monitor) while
        // an admin keeps working here, not block this window.
        new SlideshowWindow(CapturesDirectory, EventNameBox.Text, _existingThemeLogoPath, settings) { Owner = this }.Show();
    }

    // ================================================================
    // Sharing Status
    // ================================================================

    private record SharingStatusRow(int SharingLogId, string Summary, string Detail, Visibility RetryVisibility);

    private async Task LoadSharingStatusAsync()
    {
        _sharingLogRows = await _sharingLog.GetRecentAsync(_locationId);
        (int sent, int failed) = await _sharingLog.GetSummaryAsync(_locationId);
        SharingStatusSummaryText.Text = $"Sent {sent} -- Failed {failed}";

        SharingStatusList.ItemsSource = _sharingLogRows.Select(row =>
        {
            string summary = $"Session #{row.SessionId} -- {row.Method} -- {MaskDestination(row.Destination)} -- {row.Status}";
            string detail = row.Status == "Failed" && row.ErrorMessage is not null
                ? $"{row.ErrorMessage} -- {row.SentAt:g}"
                : $"{row.SentAt:g}";
            return new SharingStatusRow(
                row.SharingLogId, summary, detail,
                row.Status == "Failed" ? Visibility.Visible : Visibility.Collapsed);
        }).ToList();
        SharingStatusEmptyText.Visibility = _sharingLogRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Masks all but a short recognizable prefix of an email/phone,
    /// same "don't show a guest's full contact info on a shared admin
    /// screen" reasoning the Slideshow/Export Event mockup's own masked
    /// sample data (j***@gmail.com etc.) was modeling.</summary>
    private static string MaskDestination(string destination)
    {
        int atIndex = destination.IndexOf('@');
        if (atIndex > 1)
        {
            return $"{destination[..1]}***{destination[atIndex..]}";
        }
        return destination.Length > 4 ? $"{destination[..4]}***" : destination;
    }

    /// <summary>Re-sends a Failed row through the real delivery service (same
    /// SmtpEmailDeliveryService/TwilioSmsDeliveryService AdminWindow's own
    /// Send Test buttons already construct directly) and logs the outcome as
    /// a new row -- the original Failed row is left as history, not
    /// overwritten, same "append, don't mutate" reasoning every other log in
    /// this codebase (Session/Payment/etc.) already follows.</summary>
    private async void RetrySharingLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int sharingLogId } button)
        {
            return;
        }
        SharingLogRow? row = _sharingLogRows.FirstOrDefault(r => r.SharingLogId == sharingLogId);
        if (row is null)
        {
            return;
        }

        button.IsEnabled = false;
        var provider = new SqlBoothSettingsProvider(_locationId);
        try
        {
            var photoUrl = new Uri(row.PhotoUrl);
            if (row.Method == "Email")
            {
                await new SmtpEmailDeliveryService(provider).SendPhotoLinkAsync(row.Destination, photoUrl);
            }
            else
            {
                await new TwilioSmsDeliveryService(provider).SendPhotoLinkAsync(row.Destination, photoUrl);
            }
            await _sharingLog.InsertAsync(row.SessionId, row.Method, row.Destination, row.PhotoUrl, "Sent", null);
        }
        catch (Exception ex)
        {
            await _sharingLog.InsertAsync(row.SessionId, row.Method, row.Destination, row.PhotoUrl, "Failed", ex.Message);
        }
        finally
        {
            await LoadSharingStatusAsync();
        }
    }

    // ================================================================
    // Export Event
    // ================================================================

    private void BrowseExportDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an export destination" };
        if (dialog.ShowDialog() == true)
        {
            ExportDestinationBox.Text = dialog.FolderName;
        }
    }

    private async void ExportEventButton_Click(object sender, RoutedEventArgs e)
    {
        string destinationRoot = ExportDestinationBox.Text.Trim();
        if (destinationRoot.Length == 0)
        {
            ExportEventStatusText.Text = "Choose a destination first.";
            ExportEventStatusText.Foreground = Brushes.Firebrick;
            return;
        }

        ExportEventButton.IsEnabled = false;
        ExportEventStatusText.Text = "Exporting...";
        ExportEventStatusText.Foreground = (Brush)FindResource("MutedBrush");
        try
        {
            string exportFolder = System.IO.Path.Combine(destinationRoot, $"{SafeFileName(EventNameBox.Text)}-{DateTime.Now:yyyy-MM-dd_HHmmss}");
            System.IO.Directory.CreateDirectory(exportFolder);

            int fileCount = 0;
            if (ExportPhotosCheckBox.IsChecked == true && System.IO.Directory.Exists(CapturesDirectory))
            {
                string mediaFolder = System.IO.Path.Combine(exportFolder, "Media");
                System.IO.Directory.CreateDirectory(mediaFolder);
                foreach (string filePath in System.IO.Directory.EnumerateFiles(CapturesDirectory))
                {
                    System.IO.File.Copy(filePath, System.IO.Path.Combine(mediaFolder, System.IO.Path.GetFileName(filePath)), overwrite: true);
                    fileCount++;
                }
            }

            if (ExportFeedbackCheckBox.IsChecked == true)
            {
                var feedback = await _repository.GetAllFeedbackAsync(_locationId);
                WriteCsv(
                    System.IO.Path.Combine(exportFolder, "Feedback.csv"),
                    new[] { "SessionId", "Rating", "Comment", "RecordedAt" },
                    feedback.Select(f => new[] { f.SessionId.ToString(), f.Rating?.ToString() ?? "", f.Comment ?? "", f.RecordedAt.ToString("o") }));
                fileCount++;
            }

            if (ExportSessionLogCheckBox.IsChecked == true)
            {
                var sessions = await _repository.GetSessionLogAsync(_locationId);
                WriteCsv(
                    System.IO.Path.Combine(exportFolder, "Sessions.csv"),
                    new[] { "SessionId", "Mode", "StartedAt", "EndedAt", "Status" },
                    sessions.Select(s => new[] { s.SessionId.ToString(), s.Mode, s.StartedAt.ToString("o"), s.EndedAt?.ToString("o") ?? "", s.Status }));
                fileCount++;
            }

            string resultPath = exportFolder;
            if (ExportZipCheckBox.IsChecked == true)
            {
                string zipPath = exportFolder + ".zip";
                if (System.IO.File.Exists(zipPath))
                {
                    System.IO.File.Delete(zipPath);
                }
                System.IO.Compression.ZipFile.CreateFromDirectory(exportFolder, zipPath);
                System.IO.Directory.Delete(exportFolder, recursive: true);
                resultPath = zipPath;
            }

            ExportEventStatusText.Text = $"Exported to {resultPath}.";
        }
        catch (Exception ex)
        {
            ExportEventStatusText.Text = $"Couldn't export: {ex.Message}";
            ExportEventStatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            ExportEventButton.IsEnabled = true;
        }
    }

    private static void WriteCsv(string path, string[] headers, IEnumerable<string[]> rows)
    {
        using var writer = new System.IO.StreamWriter(path, append: false, System.Text.Encoding.UTF8);
        writer.WriteLine(string.Join(",", headers.Select(CsvField)));
        foreach (string[] row in rows)
        {
            writer.WriteLine(string.Join(",", row.Select(CsvField)));
        }
    }

    private static string CsvField(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // ================================================================
    // Event folder
    // ================================================================

    private void LoadEventFolder()
    {
        EventFolderPathText.Text = CapturesDirectory;
        if (!System.IO.Directory.Exists(CapturesDirectory))
        {
            EventFolderStatsText.Text = "This folder doesn't exist yet -- it's created automatically the first time a guest photo is captured.";
            return;
        }

        var files = System.IO.Directory.EnumerateFiles(CapturesDirectory).ToList();
        long totalBytes = files.Sum(f => new System.IO.FileInfo(f).Length);
        EventFolderStatsText.Text = $"{files.Count} file{(files.Count == 1 ? "" : "s")} -- {totalBytes / 1024.0 / 1024.0:0.0} MB";
    }

    private void OpenEventFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(CapturesDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CapturesDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open the folder: {ex.Message}", "Focus & Snap -- admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================================================================
    // Remote Control
    // ================================================================

    private async void SaveRemoteControlSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = RemoteControlEnabledCheckBox.IsChecked == true;
        SaveRemoteControlSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateRemoteControlEnabledAsync(_locationId, enabled);
            BoothSettingsChanged.Publish(_locationId);
            RemoteControlStatusText.Text = enabled
                ? "Saved -- the running kiosk starts listening the next time it returns to Idle."
                : "Saved -- disabled.";
            RemoteControlStatusText.Foreground = (Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            RemoteControlStatusText.Text = $"Couldn't save: {ex.Message}";
            RemoteControlStatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            SaveRemoteControlSettingsButton.IsEnabled = true;
        }
    }

    // ================================================================
    // Show Lock Screen
    // ================================================================

    private void UpdateLockScreenStatusText()
    {
        LockScreenStatusText.Text = _isLocked ? "Currently locked." : "Currently unlocked.";
        LockScreenStatusText.Foreground = _isLocked ? Brushes.Firebrick : (Brush)FindResource("AccentBrush");
    }

    private async void LockNowButton_Click(object sender, RoutedEventArgs e) => await SetLockedAsync(true);

    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await SetLockedAsync(false);

    private async Task SetLockedAsync(bool locked)
    {
        LockNowButton.IsEnabled = false;
        UnlockButton.IsEnabled = false;
        try
        {
            await _locations.UpdateLockedAsync(_locationId, locked);
            BoothSettingsChanged.Publish(_locationId);
            _isLocked = locked;
            UpdateLockScreenStatusText();
            // Applies immediately to a live kiosk session, if this dashboard
            // was opened from one -- see KioskAdminViewModel.OnLockChanged.
            // Null (opened standalone) just means there's no live session to
            // notify; the DB value above is still the source of truth the
            // next time any kiosk reads it.
            _onLockChanged?.Invoke(locked);
        }
        catch (Exception ex)
        {
            LockScreenStatusText.Text = $"Couldn't save: {ex.Message}";
            LockScreenStatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            LockNowButton.IsEnabled = true;
            UnlockButton.IsEnabled = true;
        }
    }

    // ================================================================
    // About / Help
    // ================================================================

    private static string AppVersionText()
    {
        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Walks up from this process's own build output to find
    /// README.md next to Photobooth.sln -- same "dev layout: walk up to the
    /// solution root" resolution BoothCompositionRoot.ResolveCameraBridgeHostPath
    /// already uses for a different file next to the same marker.</summary>
    private static string? ResolveReadmePath()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Photobooth.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return null;
        }
        string readmePath = System.IO.Path.Combine(dir.FullName, "README.md");
        return System.IO.File.Exists(readmePath) ? readmePath : null;
    }

    private void OpenReadmeButton_Click(object sender, RoutedEventArgs e)
    {
        string? readmePath = ResolveReadmePath();
        if (readmePath is null)
        {
            MessageBox.Show("Couldn't find README.md next to Photobooth.sln.", "Focus & Snap -- admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(readmePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open README.md: {ex.Message}", "Focus & Snap -- admin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Strips characters Windows doesn't allow in a file/folder name
    /// -- used for both Export Event's destination folder name and its
    /// default suggestion (see LoadAsync), both derived from the admin-typed
    /// event/brand name.</summary>
    private static string SafeFileName(string name)
    {
        string result = name;
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }
        return result.Trim().Length == 0 ? "Event" : result.Trim();
    }
}
