using System.IO;
using System.Linq;
using IoPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

/// <summary>
/// Drag-and-drop visual editor for the Welcome/Capture/Sharing guest-facing
/// screens' text/image/shape overlays -- the Visual Screen Editor from
/// BUILD_PLAN.md's Phase 6. Reuses PrintTemplateEditorWindow's percent-of-canvas
/// drag/resize math (ElementsCanvas maps 1:1 to XPercent/YPercent/WidthPercent/
/// HeightPercent), but with one shared ElementsCanvas whose contents swap
/// per-tab rather than three separate canvases -- factoring the canvas/list/
/// property-panel state into a reusable per-tab structure keeps this
/// maintainable without either tripling ~700 lines of drag/resize code three
/// times over, or inventing a bigger abstraction layer than three tabs over one
/// element model actually needs. Unlike the print editor, there's no
/// PrintCompositor-rendered preview underneath: ElementsCanvas itself is the
/// live view, since these are placed WPF elements, not composited onto a
/// captured photo -- the one exception is a read-only branding layer (see
/// UpdateBrandingLayer) showing the event's real logo/name behind it, so the
/// canvas at least isn't visually disconnected from KioskWindow. Not yet seen
/// rendered or clicked through -- same interactive-desktop gap every WPF
/// screen in this project has.
/// </summary>
public partial class ScreenTemplateEditorWindow : UserControl
{
    /// <summary>Raised when the admin is done with this editor -- true if Save
    /// succeeded, false on Cancel or a breadcrumb navigation (see
    /// RequestedNavigation). AdminWindow (which now hosts this as an embedded
    /// child view, not a separate Window/ShowDialog) subscribes to swap back
    /// to whatever section should show next, mirroring exactly what it used
    /// to do with ShowDialog's own return value.</summary>
    public event Action<bool>? RequestClose;

    private const int CanvasWidth = 640;
    private const int CanvasHeight = 400;
    private const double HandleSize = 12;
    private const double MinElementSizePx = 20;

    private readonly int _locationId;

    /// <summary>The event's current logo/colors/name -- rendered read-only
    /// underneath ElementsCanvas (see UpdateBrandingLayer) so this editor's
    /// canvas isn't a dead rectangle disconnected from what guests actually
    /// see on KioskWindow. Passed in from AdminWindow's own already-loaded
    /// copy rather than re-fetched, same reasoning _screenSettings below
    /// already follows.</summary>
    private readonly BoothTheme _theme;

    /// <summary>Read-only context for the Sharing chrome mockup's QR/Email/
    /// SMS/Print visibility (see UpdateScreenChrome) -- these live in
    /// SharingSettings/PrintOptions, not ScreenSettings, and are edited via
    /// AdminWindow's separate Sharing Settings section (see
    /// SharingSettingsLink_MouseLeftButtonDown), never written back by this
    /// editor. Same "read-only context passed in" precedent _theme above
    /// already sets.</summary>
    private readonly SharingSettings _sharing;
    private readonly PrintOptions _printOptions;

    /// <summary>Working copy of the screen-chrome toggles edited by the
    /// SETTINGS tab (see ScreenSettingsCheckBox_Click/RotationRadio_Click/
    /// PoseStripRadio_Click) -- booth-wide, not per-tab, same as the underlying
    /// Location columns (see LocationRepository.UpdateScreenSettingsAsync).</summary>
    private ScreenSettings _screenSettings;

    /// <summary>Starts true, not false: a Slider with Minimum/Maximum set in
    /// XAML (UnlockButtonOpacitySlider, FinalScreenTimeoutSlider) coerces and
    /// raises ValueChanged during InitializeComponent itself -- before this
    /// constructor's own body (and LoadScreenSettingsControls) ever runs --
    /// which previously called into e.g. FinalScreenTimeoutSlider_ValueChanged
    /// while sibling controls like FinalScreenTimeoutBox were still null,
    /// crashing the window on every open. Defaulting true suppresses that
    /// premature firing the same way LoadScreenSettingsControls's own
    /// true/finally-false bracket suppresses its later, intentional one.</summary>
    private bool _suppressScreenSettingsEvents = true;

    /// <summary>Set when a breadcrumb link (Print Layout / Virtual Attendant /
    /// Countdown settings / Sharing Settings) is clicked -- AdminWindow checks
    /// this after ShowDialog() returns and either opens the separate
    /// PrintTemplateEditorWindow ("PrintLayout") or flips to the named
    /// AdminWindow section (any other value, passed straight to ShowSection),
    /// since none of these live inside this editor window itself.</summary>
    public string? RequestedNavigation { get; private set; }

    /// <summary>Working element lists, one per screen -- populated from the
    /// existing rows at load, mutated in place as the admin edits, and flattened
    /// back into one list on Save.</summary>
    private readonly Dictionary<ScreenTemplateScreen, List<ScreenTemplateElement>> _elementsByScreen = new()
    {
        [ScreenTemplateScreen.Welcome] = new(),
        [ScreenTemplateScreen.Capture] = new(),
        [ScreenTemplateScreen.Sharing] = new(),
    };

    private ScreenTemplateScreen _activeScreen = ScreenTemplateScreen.Welcome;
    private List<ScreenTemplateElement> _elements => _elementsByScreen[_activeScreen];
    private readonly List<Border> _containers = new();
    private readonly List<Rectangle> _handles = new();

    /// <summary>One row in LayerListBox -- Kind drives which icon chip the
    /// LayerRowTemplate DataTemplate shows, Name is the same display text
    /// RefreshLayerList always computed, just no longer baked into a plain
    /// string so the icon can live alongside it instead of a "Text: " prefix.</summary>
    private sealed record LayerRow(ScreenTemplateElementKind Kind, string Name)
    {
        public Geometry IconData => Kind switch
        {
            ScreenTemplateElementKind.Text => Geometry.Parse("M5,5 L19,5 M12,5 L12,19"),
            ScreenTemplateElementKind.Image => Geometry.Parse("M3.5,4.5 L20.5,4.5 L20.5,19.5 L3.5,19.5 Z M4,17 L9.5,11.5 L14,16 L16.5,13.5 L20,17"),
            _ => Geometry.Parse("M4,4 L20,4 L20,20 L4,20 Z"),
        };
    }

    private static string DisplayName(ScreenTemplateElement element) => element.Kind switch
    {
        ScreenTemplateElementKind.Text => string.IsNullOrWhiteSpace(element.Text) ? "Text" : element.Text!,
        ScreenTemplateElementKind.Image => "Image",
        _ => "Shape",
    };

    private int _selectedIndex = -1;
    private int _draggingIndex = -1;
    private bool _resizing;
    private Point _dragStartPoint;
    private double _dragStartLeft, _dragStartTop, _dragStartWidth, _dragStartHeight;
    private bool _suppressPropertyEvents;
    private bool _suppressLayerListEvents;

    public ScreenTemplateEditorWindow(IReadOnlyList<ScreenTemplateElement> existingElements, int locationId,
        ScreenSettings screenSettings, BoothTheme theme, SharingSettings sharing, PrintOptions printOptions)
    {
        InitializeComponent();

        _locationId = locationId;
        _screenSettings = screenSettings;
        _theme = theme;
        _sharing = sharing;
        _printOptions = printOptions;
        foreach (ScreenTemplateElement element in existingElements)
        {
            _elementsByScreen[element.Screen].Add(element);
        }

        ElementsCanvas.Width = CanvasWidth;
        ElementsCanvas.Height = CanvasHeight;

        LoadActiveScreen();
        LoadScreenSettingsControls();
    }

    private void LoadScreenSettingsControls()
    {
        _suppressScreenSettingsEvents = true;
        try
        {
            // Welcome
            WelcomeBackgroundColorBox.Text = _screenSettings.WelcomeBackgroundColorHex;
            WelcomeBackgroundColorSwatch.Background = HexToBrush(_screenSettings.WelcomeBackgroundColorHex);
            WelcomeBackgroundImageNameText.Text = _screenSettings.WelcomeBackgroundImagePath is string welcomeBgPath
                ? IoPath.GetFileName(welcomeBgPath)
                : "No image selected.";
            BoothIconsEnabledCheckBox.IsChecked = _screenSettings.BoothIconsEnabled;
            BoothIconLabelsEnabledCheckBox.IsChecked = _screenSettings.BoothIconLabelsEnabled;
            WelcomeShowLiveViewCheckBox.IsChecked = _screenSettings.WelcomeShowLiveView;
            LiveTemplatePreviewCheckBox.IsChecked = _screenSettings.LiveTemplatePreview;
            StretchLiveViewCombo.SelectedIndex = _screenSettings.StretchLiveView switch
            {
                "Fit Screen" => 1,
                "Stretch To Fill" => 2,
                _ => 0,
            };
            BrowseButtonEnabledCheckBox.IsChecked = _screenSettings.BrowseButtonEnabled;
            ChooseTemplateEnabledCheckBox.IsChecked = _screenSettings.ChooseTemplateEnabled;
            StartScreenVideoNameText.Text = _screenSettings.StartScreenVideoPath is string videoPath
                ? IoPath.GetFileName(videoPath)
                : "No video selected.";
            UnlockButtonOpacitySlider.Value = _screenSettings.UnlockButtonOpacityPercent;
            UnlockButtonOpacityBox.Text = _screenSettings.UnlockButtonOpacityPercent.ToString();
            SessionTriggerTouchScreenCheckBox.IsChecked = _screenSettings.SessionTriggerTouchScreen;
            SessionTriggerF13CheckBox.IsChecked = _screenSettings.SessionTriggerF13;
            SessionTriggerKeysCheckBox.IsChecked = _screenSettings.SessionTriggerKeys;
            GuestQrCodeEnabledCheckBox.IsChecked = _screenSettings.GuestQrCodeEnabled;

            // Capture
            CaptureBackgroundColorBox.Text = _screenSettings.CaptureBackgroundColorHex;
            CaptureBackgroundColorSwatch.Background = HexToBrush(_screenSettings.CaptureBackgroundColorHex);
            CaptureBackgroundImageNameText.Text = _screenSettings.CaptureBackgroundImagePath is string captureBgPath
                ? IoPath.GetFileName(captureBgPath)
                : "No image selected.";
            ShowLiveViewCheckBox.IsChecked = _screenSettings.ShowLiveView;
            CropLiveViewCheckBox.IsChecked = _screenSettings.CropLiveView;
            MirrorLiveViewCheckBox.IsChecked = _screenSettings.MirrorLiveView;
            AutoTriggerCameraCheckBox.IsChecked = _screenSettings.AutoTriggerCamera;
            FlashScreenWhiteCheckBox.IsChecked = _screenSettings.FlashScreenWhite;
            ShowCancelButtonCheckBox.IsChecked = _screenSettings.ShowCancelButton;
            CountdownColorBox.Text = _screenSettings.CountdownColorHex;
            CountdownColorSwatch.Background = HexToBrush(_screenSettings.CountdownColorHex);
            PhotoThumbnailsEnabledCheckBox.IsChecked = _screenSettings.PhotoThumbnailsEnabled;
            SayCheeseImageNameText.Text = _screenSettings.SayCheeseImagePath is string sayCheesePath
                ? IoPath.GetFileName(sayCheesePath)
                : "No image selected.";

            RadioButton rotationRadio = _screenSettings.LiveViewRotation switch
            {
                90 => Rotation90Radio,
                180 => Rotation180Radio,
                270 => Rotation270Radio,
                _ => Rotation0Radio,
            };
            rotationRadio.IsChecked = true;

            RadioButton poseStripRadio = _screenSettings.PoseStripPosition switch
            {
                "Top" => PoseTopRadio,
                "Left" => PoseLeftRadio,
                "Right" => PoseRightRadio,
                _ => PoseBottomRadio,
            };
            poseStripRadio.IsChecked = true;

            // Sharing
            SharingBackgroundColorBox.Text = _screenSettings.SharingBackgroundColorHex;
            SharingBackgroundColorSwatch.Background = HexToBrush(_screenSettings.SharingBackgroundColorHex);
            SharingBackgroundImageNameText.Text = _screenSettings.SharingBackgroundImagePath is string sharingBgPath
                ? IoPath.GetFileName(sharingBgPath)
                : "No image selected.";
            SkipSharingScreenCheckBox.IsChecked = _screenSettings.SkipSharingScreen;
            ShowDoneButtonCheckBox.IsChecked = _screenSettings.ShowDoneButton;
            SharingIconsLocationCombo.SelectedIndex = _screenSettings.SharingIconsLocation switch
            {
                "Bottom Row" => 1,
                "Grid" => 2,
                _ => 0,
            };
            SharingTextLabelsEnabledCheckBox.IsChecked = _screenSettings.SharingTextLabelsEnabled;
            FinalScreenTimeoutSlider.Value = _screenSettings.FinalScreenTimeoutSeconds;
            FinalScreenTimeoutBox.Text = _screenSettings.FinalScreenTimeoutSeconds.ToString();
            ShowOriginalPhotosCheckBox.IsChecked = _screenSettings.ShowOriginalPhotos;
            ShowRetakeButtonCheckBox.IsChecked = _screenSettings.ShowRetakeButton;
        }
        finally
        {
            _suppressScreenSettingsEvents = false;
        }
    }

    private void ScreenSettingsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with
        {
            BoothIconsEnabled = BoothIconsEnabledCheckBox.IsChecked == true,
            BoothIconLabelsEnabled = BoothIconLabelsEnabledCheckBox.IsChecked == true,
            WelcomeShowLiveView = WelcomeShowLiveViewCheckBox.IsChecked == true,
            LiveTemplatePreview = LiveTemplatePreviewCheckBox.IsChecked == true,
            BrowseButtonEnabled = BrowseButtonEnabledCheckBox.IsChecked == true,
            ChooseTemplateEnabled = ChooseTemplateEnabledCheckBox.IsChecked == true,
            SessionTriggerTouchScreen = SessionTriggerTouchScreenCheckBox.IsChecked == true,
            SessionTriggerF13 = SessionTriggerF13CheckBox.IsChecked == true,
            SessionTriggerKeys = SessionTriggerKeysCheckBox.IsChecked == true,
            GuestQrCodeEnabled = GuestQrCodeEnabledCheckBox.IsChecked == true,

            ShowLiveView = ShowLiveViewCheckBox.IsChecked == true,
            CropLiveView = CropLiveViewCheckBox.IsChecked == true,
            MirrorLiveView = MirrorLiveViewCheckBox.IsChecked == true,
            AutoTriggerCamera = AutoTriggerCameraCheckBox.IsChecked == true,
            FlashScreenWhite = FlashScreenWhiteCheckBox.IsChecked == true,
            ShowCancelButton = ShowCancelButtonCheckBox.IsChecked == true,
            PhotoThumbnailsEnabled = PhotoThumbnailsEnabledCheckBox.IsChecked == true,

            SkipSharingScreen = SkipSharingScreenCheckBox.IsChecked == true,
            ShowDoneButton = ShowDoneButtonCheckBox.IsChecked == true,
            SharingTextLabelsEnabled = SharingTextLabelsEnabledCheckBox.IsChecked == true,
            ShowOriginalPhotos = ShowOriginalPhotosCheckBox.IsChecked == true,
            ShowRetakeButton = ShowRetakeButtonCheckBox.IsChecked == true,
        };
        UpdateScreenChrome();
    }

    private void StretchLiveViewCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || StretchLiveViewCombo.SelectedItem is not ComboBoxItem { Content: string text })
        {
            return;
        }

        _screenSettings = _screenSettings with { StretchLiveView = text };
    }

    private void SharingIconsLocationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || SharingIconsLocationCombo.SelectedItem is not ComboBoxItem { Content: string text })
        {
            return;
        }

        _screenSettings = _screenSettings with { SharingIconsLocation = text };
    }

    private void UnlockButtonOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        int percent = (int)Math.Round(UnlockButtonOpacitySlider.Value);
        _screenSettings = _screenSettings with { UnlockButtonOpacityPercent = percent };

        _suppressScreenSettingsEvents = true;
        UnlockButtonOpacityBox.Text = percent.ToString();
        _suppressScreenSettingsEvents = false;
    }

    private void UnlockButtonOpacityBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || !int.TryParse(UnlockButtonOpacityBox.Text, out int percent))
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        _screenSettings = _screenSettings with { UnlockButtonOpacityPercent = percent };

        _suppressScreenSettingsEvents = true;
        UnlockButtonOpacitySlider.Value = percent;
        _suppressScreenSettingsEvents = false;
    }

    private void FinalScreenTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        int seconds = (int)Math.Round(FinalScreenTimeoutSlider.Value);
        _screenSettings = _screenSettings with { FinalScreenTimeoutSeconds = seconds };

        _suppressScreenSettingsEvents = true;
        FinalScreenTimeoutBox.Text = seconds.ToString();
        _suppressScreenSettingsEvents = false;
    }

    private void FinalScreenTimeoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || !int.TryParse(FinalScreenTimeoutBox.Text, out int seconds))
        {
            return;
        }

        seconds = Math.Clamp(seconds, 5, 120);
        _screenSettings = _screenSettings with { FinalScreenTimeoutSeconds = seconds };

        _suppressScreenSettingsEvents = true;
        FinalScreenTimeoutSlider.Value = seconds;
        _suppressScreenSettingsEvents = false;
    }

    private void CountdownColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with { CountdownColorHex = CountdownColorBox.Text };
        CountdownColorSwatch.Background = HexToBrush(CountdownColorBox.Text);
        UpdateScreenChrome();
    }

    private void WelcomeBackgroundColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with { WelcomeBackgroundColorHex = WelcomeBackgroundColorBox.Text };
        WelcomeBackgroundColorSwatch.Background = HexToBrush(WelcomeBackgroundColorBox.Text);
        UpdateScreenChrome();
    }

    private void ChooseWelcomeBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _screenSettings = _screenSettings with { WelcomeBackgroundImagePath = storedPath };
        WelcomeBackgroundImageNameText.Text = IoPath.GetFileName(storedPath);
        UpdateScreenChrome();
    }

    private void CaptureBackgroundColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with { CaptureBackgroundColorHex = CaptureBackgroundColorBox.Text };
        CaptureBackgroundColorSwatch.Background = HexToBrush(CaptureBackgroundColorBox.Text);
        UpdateScreenChrome();
    }

    private void ChooseCaptureBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _screenSettings = _screenSettings with { CaptureBackgroundImagePath = storedPath };
        CaptureBackgroundImageNameText.Text = IoPath.GetFileName(storedPath);
        UpdateScreenChrome();
    }

    private void SharingBackgroundColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressScreenSettingsEvents)
        {
            return;
        }

        _screenSettings = _screenSettings with { SharingBackgroundColorHex = SharingBackgroundColorBox.Text };
        SharingBackgroundColorSwatch.Background = HexToBrush(SharingBackgroundColorBox.Text);
        UpdateScreenChrome();
    }

    private void ChooseSharingBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _screenSettings = _screenSettings with { SharingBackgroundImagePath = storedPath };
        SharingBackgroundImageNameText.Text = IoPath.GetFileName(storedPath);
        UpdateScreenChrome();
    }

    private void ChooseStartScreenVideoButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreFile("Video files (*.mp4;*.wmv;*.avi)|*.mp4;*.wmv;*.avi", "Choose a start screen video", "ScreenVideos");
        if (storedPath is null)
        {
            return;
        }

        _screenSettings = _screenSettings with { StartScreenVideoPath = storedPath };
        StartScreenVideoNameText.Text = IoPath.GetFileName(storedPath);
    }

    private void ChooseSayCheeseImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _screenSettings = _screenSettings with { SayCheeseImagePath = storedPath };
        SayCheeseImageNameText.Text = IoPath.GetFileName(storedPath);
    }

    /// <summary>Closes this editor and tells AdminWindow to open the separate
    /// Print Layout editor (PrintTemplateEditorWindow) right after -- the two
    /// stay distinct windows/pages, this just chains them the way dslrBooth's
    /// own breadcrumb does. In-progress design/settings edits are discarded,
    /// same as clicking Cancel, since there's no way to save mid-navigation
    /// without also changing the Save button's own meaning.</summary>
    private void PrintLayoutLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseAndNavigate("PrintLayout");

    /// <summary>Chains to AdminWindow's own Virtual Attendant section, same
    /// pattern as PrintLayoutLink above -- that section already has the real
    /// Enabled/Style/Randomize-by-stage controls, this editor doesn't
    /// duplicate them.</summary>
    private void VirtualAttendantLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseAndNavigate("VirtualAttendant");

    /// <summary>Chains to AdminWindow's Capture Settings section, which owns
    /// the countdown duration (CountdownSecondsBox) -- this editor's own
    /// Countdown color field is the only countdown-related setting that
    /// belongs to screen chrome rather than capture behavior.</summary>
    private void CountdownSettingsLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseAndNavigate("CaptureSettings");

    /// <summary>Chains to AdminWindow's Sharing Settings section, which owns
    /// the actual Email/SMS/Twitter/QR/Print channel toggles and delivery
    /// config -- this editor's own Sharing tab only has screen-chrome
    /// settings (Skip/Done/labels/timeout/etc).</summary>
    private void SharingSettingsLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseAndNavigate("SharingSettings");

    private void CloseAndNavigate(string target)
    {
        RequestedNavigation = target;
        RequestClose?.Invoke(false);
    }

    private void RotationRadio_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int degrees))
        {
            return;
        }

        _screenSettings = _screenSettings with { LiveViewRotation = degrees };
    }

    private void PoseStripRadio_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressScreenSettingsEvents || sender is not RadioButton { Tag: string position })
        {
            return;
        }

        _screenSettings = _screenSettings with { PoseStripPosition = position };
    }

    private void ScreenTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenTabControl.SelectedItem is not TabItem { Tag: string tag } || !Enum.TryParse(tag, out ScreenTemplateScreen screen))
        {
            return;
        }

        _activeScreen = screen;
        LoadActiveScreen();

        EditingSubtitleText.Text = $"Editing the {screen} screen";
        SettingsHeaderText.Text = $"Settings · {screen}";
        WelcomeSettingsPanel.Visibility = screen == ScreenTemplateScreen.Welcome ? Visibility.Visible : Visibility.Collapsed;
        CaptureSettingsPanel.Visibility = screen == ScreenTemplateScreen.Capture ? Visibility.Visible : Visibility.Collapsed;
        SharingSettingsPanel.Visibility = screen == ScreenTemplateScreen.Sharing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Rebuilds ElementsCanvas's visuals for whichever screen is now
    /// active -- same "clear and re-add" approach MoveSelectedLayerTo already
    /// uses in PrintTemplateEditorWindow for a re-order, just for a full tab
    /// switch instead.</summary>
    private void LoadActiveScreen()
    {
        ElementsCanvas.Children.Clear();
        _containers.Clear();
        _handles.Clear();
        _selectedIndex = -1;

        for (int i = 0; i < _elements.Count; i++)
        {
            AddVisualForElement(i);
        }

        RefreshLayerList();
        SelectElement(-1);
        UpdateBrandingLayer();
        UpdateScreenChrome();
    }

    /// <summary>Applies current state to the three non-interactive chrome
    /// mockups (WelcomeChromeLayer/CaptureChromeLayer/SharingChromeLayer) and
    /// shows only the active screen's layer -- this is what makes the canvas
    /// an actual WYSIWYG preview of KioskWindow instead of a dead box: called
    /// from LoadActiveScreen (tab switch) and from every settings handler
    /// that touches a field the chrome reflects (background color/image,
    /// ShowCancelButton, CountdownColorHex, ShowRetakeButton, ShowDoneButton,
    /// SharingTextLabelsEnabled).</summary>
    private void UpdateScreenChrome()
    {
        WelcomeChromeLayer.Visibility = _activeScreen == ScreenTemplateScreen.Welcome ? Visibility.Visible : Visibility.Collapsed;
        CaptureChromeLayer.Visibility = _activeScreen == ScreenTemplateScreen.Capture ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeLayer.Visibility = _activeScreen == ScreenTemplateScreen.Sharing ? Visibility.Visible : Visibility.Collapsed;

        WelcomeBackgroundFill.Background = HexToBrush(_screenSettings.WelcomeBackgroundColorHex);
        WelcomeBackgroundImageElement.Source = LoadImageOrNull(_screenSettings.WelcomeBackgroundImagePath);
        CaptureBackgroundFill.Background = HexToBrush(_screenSettings.CaptureBackgroundColorHex);
        CaptureBackgroundImageElement.Source = LoadImageOrNull(_screenSettings.CaptureBackgroundImagePath);
        SharingBackgroundFill.Background = HexToBrush(_screenSettings.SharingBackgroundColorHex);
        SharingBackgroundImageElement.Source = LoadImageOrNull(_screenSettings.SharingBackgroundImagePath);

        CaptureChromeCountdownText.Foreground = HexToBrush(_screenSettings.CountdownColorHex);
        CaptureChromeCancelPill.Visibility = _screenSettings.ShowCancelButton ? Visibility.Visible : Visibility.Collapsed;

        // Same sources KioskViewModel itself reads for IsQrEnabled/IsEmailEnabled/
        // IsSmsEnabled/IsPrintButtonVisible -- SharingSettings/PrintOptions, not
        // ScreenSettings (see _sharing/_printOptions doc comment above).
        SharingChromeQrBox.Visibility = _sharing.QrEnabled ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeEmailRow.Visibility = _sharing.EmailEnabled ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeSmsRow.Visibility = _sharing.SmsEnabled ? Visibility.Visible : Visibility.Collapsed;
        SharingChromePrintPill.Visibility = _printOptions.ShowPrintButton ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeRetakePill.Visibility = _screenSettings.ShowRetakeButton ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeDonePill.Visibility = _screenSettings.ShowDoneButton ? Visibility.Visible : Visibility.Collapsed;

        // ScreenSettings.SharingTextLabelsEnabled hides just the captions,
        // same as KioskWindow.xaml's own IsSharingTextLabelsEnabled bindings --
        // the QR box/email+SMS fields themselves stay up.
        Visibility labelVisibility = _screenSettings.SharingTextLabelsEnabled ? Visibility.Visible : Visibility.Collapsed;
        SharingChromeQrLabel.Visibility = labelVisibility;
        SharingChromeEmailLabel.Visibility = labelVisibility;
        SharingChromeSmsLabel.Visibility = labelVisibility;
    }

    private static ImageSource? LoadImageOrNull(string? path) => path is string p && File.Exists(p)
        ? new BitmapImage(new Uri(IoPath.GetFullPath(p)))
        : null;

    /// <summary>Shows the event's real logo/name over the chrome mockup --
    /// Welcome gets the big centered logo+name inside WelcomeChromeLayer
    /// (WelcomeChromeLayer's own Visibility, set by UpdateScreenChrome, already
    /// gates these), Capture/Sharing get the small top-center "brand bar"
    /// KioskWindow shows during Countdown/Capture/Processing/Review (see
    /// KioskWindow.xaml's own CHROME: BRAND BAR block) -- one shared element
    /// since both chrome layers use the same position.</summary>
    private void UpdateBrandingLayer()
    {
        ImageSource? logo = _theme.LogoImagePath is string path && File.Exists(path)
            ? new BitmapImage(new Uri(IoPath.GetFullPath(path)))
            : null;

        bool isWelcome = _activeScreen == ScreenTemplateScreen.Welcome;
        BrandBarBrandingLayer.Visibility = isWelcome ? Visibility.Collapsed : Visibility.Visible;

        WelcomeBrandingLogo.Source = logo;
        WelcomeBrandingLogo.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        WelcomeBrandingEventName.Text = _theme.EventName;

        BrandBarLogo.Source = logo;
        BrandBarLogo.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        BrandBarEventName.Text = _theme.EventName;
    }

    private void AddVisualForElement(int index)
    {
        ScreenTemplateElement element = _elements[index];

        FrameworkElement content = element.Kind switch
        {
            ScreenTemplateElementKind.Text => new TextBlock
            {
                Text = element.Text,
                FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = HexToBrush(element.ColorHex),
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
            ScreenTemplateElementKind.Image => new Image
            {
                Source = element.ImagePath is string path && File.Exists(path)
                    ? new BitmapImage(new Uri(IoPath.GetFullPath(path)))
                    : null,
                Stretch = Stretch.Uniform,
            },
            _ => new System.Windows.Shapes.Rectangle { Fill = HexToBrush(element.ColorHex) },
        };

        var container = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent, // hit-test visible, same reasoning MainWindow's surface background needs one
            Child = content,
        };
        container.MouseLeftButtonDown += ElementContainer_MouseLeftButtonDown;
        container.MouseMove += ElementContainer_MouseMove;
        container.MouseLeftButtonUp += ElementContainer_MouseLeftButtonUp;

        var handle = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = Brushes.SteelBlue,
            Cursor = Cursors.SizeNWSE,
        };
        handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
        handle.MouseMove += Handle_MouseMove;
        handle.MouseLeftButtonUp += Handle_MouseLeftButtonUp;

        _containers.Insert(index, container);
        _handles.Insert(index, handle);
        ElementsCanvas.Children.Add(container);
        ElementsCanvas.Children.Add(handle);

        PositionVisual(index);
    }

    private void PositionVisual(int index)
    {
        ScreenTemplateElement element = _elements[index];
        Border container = _containers[index];
        Rectangle handle = _handles[index];

        double left = element.XPercent * CanvasWidth;
        double top = element.YPercent * CanvasHeight;
        double width = element.WidthPercent * CanvasWidth;
        double height = element.HeightPercent * CanvasHeight;

        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        container.Width = width;
        container.Height = height;

        Canvas.SetLeft(handle, left + width - HandleSize / 2);
        Canvas.SetTop(handle, top + height - HandleSize / 2);
    }

    private void ElementContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border container)
        {
            return;
        }

        int index = _containers.IndexOf(container);
        if (index < 0)
        {
            return;
        }

        _draggingIndex = index;
        _dragStartPoint = e.GetPosition(ElementsCanvas);
        _dragStartLeft = Canvas.GetLeft(container);
        _dragStartTop = Canvas.GetTop(container);
        container.CaptureMouse();
        e.Handled = true;
    }

    private void ElementContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0 || sender is not Border container || !container.IsMouseCaptured)
        {
            return;
        }

        Point current = e.GetPosition(ElementsCanvas);
        double deltaX = current.X - _dragStartPoint.X;
        double deltaY = current.Y - _dragStartPoint.Y;

        double newLeft = Math.Clamp(_dragStartLeft + deltaX, 0, Math.Max(0, CanvasWidth - container.Width));
        double newTop = Math.Clamp(_dragStartTop + deltaY, 0, Math.Max(0, CanvasHeight - container.Height));

        Canvas.SetLeft(container, newLeft);
        Canvas.SetTop(container, newTop);

        Rectangle handle = _handles[_draggingIndex];
        Canvas.SetLeft(handle, newLeft + container.Width - HandleSize / 2);
        Canvas.SetTop(handle, newTop + container.Height - HandleSize / 2);
    }

    private void ElementContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingIndex < 0 || sender is not Border container)
        {
            return;
        }

        container.ReleaseMouseCapture();
        int index = _draggingIndex;
        _draggingIndex = -1;

        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);
        _elements[index] = _elements[index] with
        {
            XPercent = left / CanvasWidth,
            YPercent = top / CanvasHeight,
        };

        SelectElement(index);
        e.Handled = true;
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle handle)
        {
            return;
        }

        int index = _handles.IndexOf(handle);
        if (index < 0)
        {
            return;
        }

        _draggingIndex = index;
        _resizing = true;
        _dragStartPoint = e.GetPosition(ElementsCanvas);
        _dragStartWidth = _containers[index].Width;
        _dragStartHeight = _containers[index].Height;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || _draggingIndex < 0 || sender is not Rectangle handle || !handle.IsMouseCaptured)
        {
            return;
        }

        Border container = _containers[_draggingIndex];
        Point current = e.GetPosition(ElementsCanvas);
        double deltaX = current.X - _dragStartPoint.X;
        double deltaY = current.Y - _dragStartPoint.Y;

        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);
        double newWidth = Math.Clamp(_dragStartWidth + deltaX, MinElementSizePx, Math.Max(MinElementSizePx, CanvasWidth - left));
        double newHeight = Math.Clamp(_dragStartHeight + deltaY, MinElementSizePx, Math.Max(MinElementSizePx, CanvasHeight - top));

        container.Width = newWidth;
        container.Height = newHeight;
        Canvas.SetLeft(handle, left + newWidth - HandleSize / 2);
        Canvas.SetTop(handle, top + newHeight - HandleSize / 2);
    }

    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing || _draggingIndex < 0 || sender is not Rectangle handle)
        {
            return;
        }

        handle.ReleaseMouseCapture();
        int index = _draggingIndex;
        _draggingIndex = -1;
        _resizing = false;

        Border container = _containers[index];
        _elements[index] = _elements[index] with
        {
            WidthPercent = container.Width / CanvasWidth,
            HeightPercent = container.Height / CanvasHeight,
        };

        SelectElement(index);
        e.Handled = true;
    }

    private void SelectElement(int index)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            _containers[i].BorderBrush = i == index ? Brushes.SteelBlue : Brushes.Gray;
            _containers[i].BorderThickness = new Thickness(i == index ? 2 : 1);
        }

        _selectedIndex = index;
        DeleteSelectedButton.IsEnabled = index >= 0;
        NoSelectionText.Visibility = index >= 0 ? Visibility.Collapsed : Visibility.Visible;

        _suppressLayerListEvents = true;
        try
        {
            LayerListBox.SelectedIndex = index;
        }
        finally
        {
            _suppressLayerListEvents = false;
        }

        if (index < 0)
        {
            SelectedElementHeaderText.Text = "Selected element";
            TextPropertiesPanel.Visibility = Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ScreenTemplateElement element = _elements[index];
        SelectedElementHeaderText.Text = "Editing · " + DisplayName(element);
        _suppressPropertyEvents = true;
        try
        {
            TextPropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Text ? Visibility.Visible : Visibility.Collapsed;
            ImagePropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Image ? Visibility.Visible : Visibility.Collapsed;
            ShapePropertiesPanel.Visibility = element.Kind == ScreenTemplateElementKind.Shape ? Visibility.Visible : Visibility.Collapsed;

            if (element.Kind == ScreenTemplateElementKind.Text)
            {
                ElementTextBox.Text = element.Text ?? string.Empty;
                FontSizeSlider.Value = element.FontSizePercent;
                BoldCheckBox.IsChecked = element.Bold;
                ElementColorBox.Text = element.ColorHex;
                TextColorSwatch.Background = HexToBrush(element.ColorHex);
            }
            else if (element.Kind == ScreenTemplateElementKind.Image)
            {
                SelectedImagePreview.Source = element.ImagePath is string path && File.Exists(path)
                    ? new BitmapImage(new Uri(IoPath.GetFullPath(path)))
                    : null;
            }
            else if (element.Kind == ScreenTemplateElementKind.Shape)
            {
                ShapeColorBox.Text = element.ColorHex;
                ShapeColorSwatch.Background = HexToBrush(element.ColorHex);
            }
        }
        finally
        {
            _suppressPropertyEvents = false;
        }
    }

    private void RefreshVisualContent(int index)
    {
        ScreenTemplateElement element = _elements[index];
        if (_containers[index].Child is TextBlock textBlock)
        {
            textBlock.Text = element.Text;
            textBlock.FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.Foreground = HexToBrush(element.ColorHex);
        }
        else if (_containers[index].Child is System.Windows.Shapes.Rectangle rectangle)
        {
            rectangle.Fill = HexToBrush(element.ColorHex);
        }
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (Exception)
        {
            return Brushes.Black;
        }
    }

    private void ElementTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { Text = ElementTextBox.Text };
        RefreshVisualContent(_selectedIndex);
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { FontSizePercent = FontSizeSlider.Value };
    }

    private void BoldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { Bold = BoldCheckBox.IsChecked == true };
        RefreshVisualContent(_selectedIndex);
    }

    private void ElementColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPropertyEvents || _selectedIndex < 0)
        {
            return;
        }

        bool isShape = _elements[_selectedIndex].Kind == ScreenTemplateElementKind.Shape;
        string hex = isShape ? ShapeColorBox.Text : ElementColorBox.Text;
        _elements[_selectedIndex] = _elements[_selectedIndex] with { ColorHex = hex };
        RefreshVisualContent(_selectedIndex);

        SolidColorBrush brush = HexToBrush(hex);
        if (isShape)
        {
            ShapeColorSwatch.Background = brush;
        }
        else
        {
            TextColorSwatch.Background = brush;
        }
    }

    private void AddTextButton_Click(object sender, RoutedEventArgs e)
    {
        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Text,
            XPercent: 0.1, YPercent: 0.4, WidthPercent: 0.8, HeightPercent: 0.2,
            Text: "Your text here");
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void AddImageButton_Click(object sender, RoutedEventArgs e)
    {
        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Image,
            XPercent: 0.35, YPercent: 0.05, WidthPercent: 0.3, HeightPercent: 0.2,
            ImagePath: storedPath);
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void AddShapeButton_Click(object sender, RoutedEventArgs e)
    {
        var element = new ScreenTemplateElement(
            _activeScreen, ScreenTemplateElementKind.Shape,
            XPercent: 0.05, YPercent: 0.05, WidthPercent: 0.2, HeightPercent: 0.1,
            ColorHex: "#365C58");
        _elements.Add(element);
        AddVisualForElement(_elements.Count - 1);
        RefreshLayerList();
        SelectElement(_elements.Count - 1);
    }

    private void ChangeImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        string? storedPath = PickAndStoreImage();
        if (storedPath is null)
        {
            return;
        }

        _elements[_selectedIndex] = _elements[_selectedIndex] with { ImagePath = storedPath };
        if (_containers[_selectedIndex].Child is Image image)
        {
            image.Source = new BitmapImage(new Uri(IoPath.GetFullPath(storedPath)));
        }

        SelectedImagePreview.Source = new BitmapImage(new Uri(IoPath.GetFullPath(storedPath)));
    }

    /// <summary>Copies the chosen image into a local Assets/ScreenElements folder,
    /// same "own local copy, not a reference to wherever the admin picked it from"
    /// pattern PrintTemplateEditorWindow.PickAndStoreLogoImage already established.</summary>
    private static string? PickAndStoreImage() => PickAndStoreFile("Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", "Choose an image", "ScreenElements");

    /// <summary>Shared by PickAndStoreImage above and the Welcome/Capture
    /// settings' own Start screen video / Say Cheese image pickers -- same
    /// "copy into a local Assets subfolder, don't just reference wherever the
    /// admin picked it from" pattern, generalized past images-only.</summary>
    private static string? PickAndStoreFile(string filter, string title, string subfolder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            Title = title,
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string directory = IoPath.Combine(AppContext.BaseDirectory, "Assets", subfolder);
        Directory.CreateDirectory(directory);
        string storedFileName = $"{Guid.NewGuid():N}{IoPath.GetExtension(dialog.FileName)}";
        string storedPath = IoPath.Combine(directory, storedFileName);
        File.Copy(dialog.FileName, storedPath, overwrite: true);
        return storedPath;
    }

    /// <summary>Aligns the selected element against the canvas bounds --
    /// left/right/top/bottom snap the corresponding edge to 0 or the
    /// canvas's own width/height, CenterHorizontal/CenterVertical center it
    /// within the canvas. Only ever touches XPercent/YPercent (size is
    /// untouched), same as a drag-move does.</summary>
    private void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || sender is not Button { Tag: string alignment })
        {
            return;
        }

        Border container = _containers[_selectedIndex];
        double left = Canvas.GetLeft(container);
        double top = Canvas.GetTop(container);

        switch (alignment)
        {
            case "Left": left = 0; break;
            case "Right": left = CanvasWidth - container.Width; break;
            case "CenterHorizontal": left = (CanvasWidth - container.Width) / 2; break;
            case "Top": top = 0; break;
            case "Bottom": top = CanvasHeight - container.Height; break;
            case "CenterVertical": top = (CanvasHeight - container.Height) / 2; break;
        }

        Canvas.SetLeft(container, left);
        Canvas.SetTop(container, top);
        Rectangle handle = _handles[_selectedIndex];
        Canvas.SetLeft(handle, left + container.Width - HandleSize / 2);
        Canvas.SetTop(handle, top + container.Height - HandleSize / 2);

        _elements[_selectedIndex] = _elements[_selectedIndex] with
        {
            XPercent = left / CanvasWidth,
            YPercent = top / CanvasHeight,
        };
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        ElementsCanvas.Children.Remove(_containers[_selectedIndex]);
        ElementsCanvas.Children.Remove(_handles[_selectedIndex]);
        _containers.RemoveAt(_selectedIndex);
        _handles.RemoveAt(_selectedIndex);
        _elements.RemoveAt(_selectedIndex);

        RefreshLayerList();
        SelectElement(-1);
    }

    private void RefreshLayerList()
    {
        _suppressLayerListEvents = true;
        try
        {
            LayerListBox.Items.Clear();
            foreach (ScreenTemplateElement element in _elements)
            {
                LayerListBox.Items.Add(new LayerRow(element.Kind, DisplayName(element)));
            }
        }
        finally
        {
            _suppressLayerListEvents = false;
        }
    }

    private void LayerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLayerListEvents)
        {
            return;
        }

        SelectElement(LayerListBox.SelectedIndex);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        List<ScreenTemplateElement> all = _elementsByScreen.Values.SelectMany(list => list).ToList();
        if (all.Any(element => !element.IsValid))
        {
            EditorStatusText.Text = "Every element needs valid bounds and either text or an image.";
            EditorStatusText.Foreground = Brushes.Firebrick;
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await new ScreenTemplateElementRepository().ReplaceAllAsync(_locationId, all);
            await new LocationRepository().UpdateScreenSettingsAsync(_locationId, _screenSettings);
            BoothSettingsChanged.Publish(_locationId);
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            EditorStatusText.Text = $"Couldn't save: {ex.Message}";
            EditorStatusText.Foreground = Brushes.Firebrick;
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RequestClose?.Invoke(false);
    }
}
