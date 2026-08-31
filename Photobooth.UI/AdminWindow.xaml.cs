using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

/// <summary>Flattened view of a VirtualAttendantClipRecord for AttendantClipsList's
/// bindings -- same reasoning as GuestbookVideoRow above (WPF bindings can't call
/// Path.GetFileName directly).</summary>
public record AttendantClipRow(int ClipId, string Stage, string FileName);

public partial class AdminWindow : Window
{
    private readonly AdminDashboardRepository _repository = new();
    private readonly LocationRepository _locations = new();
    private readonly FrameRepository _frames = new();
    private readonly SurveyRepository _survey = new();
    private readonly VirtualAttendantClipRepository _attendantClips = new();

    // "One booth machine has one location" -- same assumption
    // DatabaseInitializer's own seeding already makes -- so Settings and the
    // Frame library always target the first (only) Location row rather than
    // needing one passed in.
    private int _locationId;

    private string? _pendingFrameImagePath;
    private string? _pendingThemeLogoPath;
    private string? _existingThemeLogoPath;
    private PrintTemplate _currentPrintTemplate = PrintTemplate.Default;

    private string? _pendingWatermarkPath;
    private string? _existingWatermarkPath;
    private string? _pendingGreenScreenBackgroundPath;
    private string? _existingGreenScreenBackgroundPath;
    private string? _pendingAttendantClipPath;

    // ScreenSettings has no editable UI in this phase (see BUILD_PLAN.md Phase 5,
    // guest-facing screens) -- loaded and passed straight back through on save so
    // it isn't clobbered back to defaults.
    private ScreenSettings _currentScreenSettings = ScreenSettings.Default;

    public AdminWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
        Loaded += (_, _) => ShowSection("General");
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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
        ["SharingStatus"] = "Sharing Status",
        ["ExportEvent"] = "Export Event",
        ["EventFolder"] = "Event folder",
        ["RemoteControl"] = "Remote Control",
        ["ShowLockScreen"] = "Show Lock Screen",
        ["Language"] = "Language",
        ["Help"] = "Help",
        ["Subscription"] = "Subscription",
        ["About"] = "About dslrBooth",
    };

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
    };

    /// <summary>Shows the named section (a wizard section key, or one of
    /// PlaceholderTitles' keys) and hides every other section panel. Also
    /// closes the Layer 1 dropdown menu, same "pick a destination, menu goes
    /// away" behavior dslrBooth's own mega-menu has.</summary>
    private void ShowSection(string key)
    {
        MainMenuPanel.Visibility = Visibility.Collapsed;
        MainMenuToggle.IsChecked = false;

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

    private void MainMenuToggle_Click(object sender, RoutedEventArgs e)
    {
        MainMenuPanel.Visibility = MainMenuToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MenuLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ShowSection(key);
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

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountdownSecondsBox.Text, out int countdownSeconds) || countdownSeconds <= 0)
        {
            SettingsStatusText.Text = "Countdown must be a whole number of seconds greater than 0.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        string layout = StripLayoutRadio.IsChecked == true ? "Strip" : "Single";
        if (!double.TryParse(PrintWidthBox.Text, out double widthInches)
            || !double.TryParse(PrintHeightBox.Text, out double heightInches)
            || !int.TryParse(StripCopiesBox.Text, out int stripCopies))
        {
            SettingsStatusText.Text = "Print width/height and strip copies must be numbers.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        var printTemplate = new PrintTemplate(layout, widthInches, heightInches, stripCopies);
        if (!printTemplate.IsValid)
        {
            SettingsStatusText.Text = "Print width/height must be greater than 0 and strip copies at least 1.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        string adminPin = AdminPinBox.Text.Trim();
        if (adminPin.Length == 0)
        {
            SettingsStatusText.Text = "Admin PIN can't be blank.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        SaveSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateSettingsAsync(_locationId, countdownSeconds, GlamFilterCheckBox.IsChecked == true, printTemplate, adminPin);
            SettingsStatusText.Text = "Saved -- takes effect for the next guest session.";
            SettingsStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Couldn't save: {ex.Message}";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
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
        var editor = new ScreenTemplateEditorWindow(existing, _locationId) { Owner = this };
        editor.ShowDialog();
        // No LoadAsync() reload needed after this one -- ScreenTemplateElement
        // isn't read into any of the fields LoadAsync populates (unlike
        // _currentPrintTemplate above), it's read fresh by MainWindow itself.
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
                _locationId = locations[0].LocationId;
                CountdownSecondsBox.Text = locations[0].CountdownSeconds.ToString();
                GlamFilterCheckBox.IsChecked = locations[0].GlamFilterEnabled;
                AdminPinBox.Text = locations[0].AdminPin;

                _currentPrintTemplate = locations[0].PrintTemplate;
                SingleLayoutRadio.IsChecked = _currentPrintTemplate.Layout != "Strip";
                StripLayoutRadio.IsChecked = _currentPrintTemplate.Layout == "Strip";
                PrintWidthBox.Text = _currentPrintTemplate.WidthInches.ToString();
                PrintHeightBox.Text = _currentPrintTemplate.HeightInches.ToString();
                StripCopiesBox.Text = _currentPrintTemplate.StripCopies.ToString();

                BoothTheme theme = locations[0].Theme;
                AccentColorBox.Text = theme.AccentColorHex;
                CanvasColorBox.Text = theme.CanvasColorHex;
                InkColorBox.Text = theme.InkColorHex;
                EventNameBox.Text = theme.EventName;
                _existingThemeLogoPath = theme.LogoImagePath;
                _pendingThemeLogoPath = null;
                SelectedThemeLogoText.Text = theme.LogoImagePath is null
                    ? "No logo selected."
                    : System.IO.Path.GetFileName(theme.LogoImagePath);

                _currentScreenSettings = locations[0].Screen;

                CaptureSettings capture = locations[0].Capture;
                CaptureModePhotoRadio.IsChecked = capture.Mode == "Photo";
                CaptureModeGifRadio.IsChecked = capture.Mode == "GIF";
                CaptureModeBoomerangRadio.IsChecked = capture.Mode == "Boomerang";
                CaptureModeVideoRadio.IsChecked = capture.Mode == "Video";
                AlsoCreateGifCheckBox.IsChecked = capture.AlsoCreateGif;
                FrameCountBox.Text = capture.FrameCount.ToString();
                FrameDelayBox.Text = capture.FrameDelayMs.ToString();
                VideoDurationBox.Text = capture.VideoDurationSeconds.ToString();

                EffectsSettings effects = locations[0].Effects;
                BeautyFilterCheckBox.IsChecked = effects.BeautyFilterEnabled;
                FiltersModeAskRadio.IsChecked = effects.FiltersMode != "Auto";
                FiltersModeAutoRadio.IsChecked = effects.FiltersMode == "Auto";
                _existingWatermarkPath = effects.WatermarkImagePath;
                _pendingWatermarkPath = null;
                SelectedWatermarkText.Text = effects.WatermarkImagePath is null
                    ? "No watermark selected."
                    : System.IO.Path.GetFileName(effects.WatermarkImagePath);

                GreenScreenSettings greenScreen = locations[0].GreenScreen;
                GreenScreenEnabledCheckBox.IsChecked = greenScreen.Enabled;
                _existingGreenScreenBackgroundPath = greenScreen.BackgroundImagePath;
                _pendingGreenScreenBackgroundPath = null;
                SelectedGreenScreenBackgroundText.Text = greenScreen.BackgroundImagePath is null
                    ? "No background selected."
                    : System.IO.Path.GetFileName(greenScreen.BackgroundImagePath);

                SurveyEnabledCheckBox.IsChecked = locations[0].Survey.Enabled;

                VirtualAttendantSettings attendant = locations[0].VirtualAttendant;
                AttendantEnabledCheckBox.IsChecked = attendant.Enabled;
                AttendantStyleFriendlyRadio.IsChecked = attendant.Style != "Formal";
                AttendantStyleFormalRadio.IsChecked = attendant.Style == "Formal";
                AttendantRandomizeConsentCheckBox.IsChecked = attendant.RandomizeConsent;
                AttendantRandomizeCountdownCheckBox.IsChecked = attendant.RandomizeCountdown;
                AttendantRandomizeCapturingCheckBox.IsChecked = attendant.RandomizeCapturing;
                AttendantRandomizeReviewingCheckBox.IsChecked = attendant.RandomizeReviewing;
                AttendantRandomizePrintingCheckBox.IsChecked = attendant.RandomizePrinting;
                AttendantRandomizeCompleteCheckBox.IsChecked = attendant.RandomizeComplete;

                DisclaimerSettings disclaimer = locations[0].Disclaimer;
                DisclaimerHeaderBox.Text = disclaimer.Header;
                DisclaimerTextBox.Text = disclaimer.Text;

                SharingSettings sharing = locations[0].Sharing;
                EmailEnabledCheckBox.IsChecked = sharing.EmailEnabled;
                SmsEnabledCheckBox.IsChecked = sharing.SmsEnabled;
                QrEnabledCheckBox.IsChecked = sharing.QrEnabled;

                PrintOptions printOptions = locations[0].PrintOptions;
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
    /// precedent as SaveSettingsButton/SaveThemeButton above.</summary>
    private async void SaveParitySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FrameCountBox.Text, out int frameCount) || frameCount <= 0
            || !int.TryParse(FrameDelayBox.Text, out int frameDelayMs) || frameDelayMs <= 0
            || !int.TryParse(VideoDurationBox.Text, out int videoDurationSeconds) || videoDurationSeconds <= 0)
        {
            ParitySettingsStatusText.Text = "Frame count, frame delay, and video duration must be whole numbers greater than 0.";
            ParitySettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        if (!int.TryParse(PrintLimitPerEventBox.Text, out int printLimitPerEvent) || printLimitPerEvent <= 0
            || !int.TryParse(PrintLimitPerSessionBox.Text, out int printLimitPerSession) || printLimitPerSession <= 0)
        {
            ParitySettingsStatusText.Text = "Print limits must be whole numbers greater than 0.";
            ParitySettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
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
        var screen = _currentScreenSettings;
        var effects = new EffectsSettings(BeautyFilterCheckBox.IsChecked == true, filtersMode, watermarkPath);
        var greenScreen = new GreenScreenSettings(GreenScreenEnabledCheckBox.IsChecked == true, greenScreenBackgroundPath);
        var survey = new SurveySettings(SurveyEnabledCheckBox.IsChecked == true);
        var disclaimer = new DisclaimerSettings(DisclaimerHeaderBox.Text.Trim(), DisclaimerTextBox.Text);
        var printOptions = new PrintOptions(
            PrintAutomaticallyCheckBox.IsChecked == true, ShowPrintButtonCheckBox.IsChecked == true,
            printLimitPerEvent, printLimitPerSession, printSharpening);
        var sharing = new SharingSettings(EmailEnabledCheckBox.IsChecked == true, SmsEnabledCheckBox.IsChecked == true, QrEnabledCheckBox.IsChecked == true);

        SaveParitySettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateDslrBoothParitySettingsAsync(_locationId, capture, screen, effects, greenScreen, survey, disclaimer, printOptions, sharing);
            _existingWatermarkPath = watermarkPath;
            _pendingWatermarkPath = null;
            _existingGreenScreenBackgroundPath = greenScreenBackgroundPath;
            _pendingGreenScreenBackgroundPath = null;
            ParitySettingsStatusText.Text = "Saved -- takes effect for the next guest session.";
            ParitySettingsStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            ParitySettingsStatusText.Text = $"Couldn't save: {ex.Message}";
            ParitySettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SaveParitySettingsButton.IsEnabled = true;
        }
    }

    private async void SaveAttendantSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        string style = AttendantStyleFormalRadio.IsChecked == true ? "Formal" : "Friendly";
        var settings = new VirtualAttendantSettings(
            AttendantEnabledCheckBox.IsChecked == true,
            style,
            AttendantRandomizeConsentCheckBox.IsChecked == true,
            AttendantRandomizeCountdownCheckBox.IsChecked == true,
            AttendantRandomizeCapturingCheckBox.IsChecked == true,
            AttendantRandomizeReviewingCheckBox.IsChecked == true,
            AttendantRandomizePrintingCheckBox.IsChecked == true,
            AttendantRandomizeCompleteCheckBox.IsChecked == true);

        SaveAttendantSettingsButton.IsEnabled = false;
        try
        {
            await _locations.UpdateVirtualAttendantSettingsAsync(_locationId, settings);
            AttendantSettingsStatusText.Text = "Saved -- takes effect for the very next state transition.";
            AttendantSettingsStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        catch (Exception ex)
        {
            AttendantSettingsStatusText.Text = $"Couldn't save: {ex.Message}";
            AttendantSettingsStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            SaveAttendantSettingsButton.IsEnabled = true;
        }
    }

    private async Task LoadAttendantClipsAsync()
    {
        var clips = await _attendantClips.GetAllByLocationAsync(_locationId);
        AttendantClipsList.ItemsSource = clips
            .Select(c => new AttendantClipRow(c.ClipId, c.Stage, System.IO.Path.GetFileName(c.FilePath)))
            .ToList();
        AttendantClipsEmptyText.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseAttendantClipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio/video files (*.mp3;*.wav;*.mp4;*.wmv)|*.mp3;*.wav;*.mp4;*.wmv",
            Title = "Choose a Virtual Attendant clip",
        };
        if (dialog.ShowDialog() == true)
        {
            _pendingAttendantClipPath = dialog.FileName;
            SelectedAttendantClipText.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
    }

    /// <summary>Copies the chosen clip into a local Assets/AttendantClips folder (same
    /// "own local copy" reasoning AddFrameButton_Click already established) and inserts
    /// the VirtualAttendantClip row at the end of that stage's existing pool.</summary>
    private async void AddAttendantClipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingAttendantClipPath is null || AttendantClipStageCombo.SelectedItem is not ComboBoxItem { Tag: string stageName })
        {
            AttendantClipStatusText.Text = "Choose a stage and a clip file first.";
            AttendantClipStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            return;
        }

        AddAttendantClipButton.IsEnabled = false;
        try
        {
            string clipsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AttendantClips");
            System.IO.Directory.CreateDirectory(clipsDirectory);
            string storedFileName = $"{Guid.NewGuid():N}{System.IO.Path.GetExtension(_pendingAttendantClipPath)}";
            string storedPath = System.IO.Path.Combine(clipsDirectory, storedFileName);
            System.IO.File.Copy(_pendingAttendantClipPath, storedPath, overwrite: true);

            var stage = Enum.Parse<BoothState>(stageName);
            var existingInStage = (await _attendantClips.GetAllByLocationAsync(_locationId))
                .Where(c => c.Stage == stageName)
                .ToList();
            await _attendantClips.InsertAsync(_locationId, stage, storedPath, sortOrder: existingInStage.Count);

            _pendingAttendantClipPath = null;
            SelectedAttendantClipText.Text = "No clip selected.";
            AttendantClipStatusText.Text = "Clip added -- available for the next guest.";
            AttendantClipStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            await LoadAttendantClipsAsync();
        }
        catch (Exception ex)
        {
            AttendantClipStatusText.Text = $"Couldn't add clip: {ex.Message}";
            AttendantClipStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        finally
        {
            AddAttendantClipButton.IsEnabled = true;
        }
    }

    private async void DeleteAttendantClipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int clipId })
        {
            await _attendantClips.DeleteAsync(clipId);
            await LoadAttendantClipsAsync();
        }
    }

    private async void MoveAttendantClipUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int clipId })
        {
            await MoveAttendantClipAsync(clipId, up: true);
        }
    }

    private async void MoveAttendantClipDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int clipId })
        {
            await MoveAttendantClipAsync(clipId, up: false);
        }
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
