using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

public partial class MainWindow : Window
{
    private BoothStateMachine _stateMachine = null!;
    private ILiveViewService _liveView = null!;
    private UiFrameSelectionService _frameSelection = null!;
    private UiFeedbackService _feedback = null!;
    private UiGuestbookPromptService _guestbookPrompt = null!;
    private IBoothSettingsProvider _settingsProvider = null!;
    private int _selectedFeedbackRating;
    private readonly DispatcherTimer _liveViewTimer;
    private bool _sessionRunning;
    private bool _liveViewFetchInProgress;
    private Process? _cameraBridgeProcess;

    public MainWindow()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += KillCameraBridgeIfOwned;

        // Blocking at startup is acceptable here -- this runs once, before
        // the window is shown, and every session after it depends on the
        // seeded LocationId/PrinterId anyway. A missing/stopped LocalDB
        // instance used to hang here indefinitely (SqlConnectionFactory now
        // has a bounded Connect Timeout) -- show a real message and exit
        // cleanly instead of leaving the booth on a black screen or crashing
        // with a raw stack trace a guest or attendant can't act on.
        //
        // Task.Run is required, not just .GetAwaiter().GetResult() directly:
        // this runs on the WPF Dispatcher thread, which installs a
        // SynchronizationContext. Without Task.Run, the awaited continuations
        // inside InitializeAsync try to resume on that same thread, which is
        // blocked waiting on GetResult() -- a deadlock that hangs forever
        // (confirmed: identical code completes in <1s from a console app,
        // which has no SynchronizationContext to deadlock on).
        DatabaseInitializer.SeedIds seedIds;
        try
        {
            seedIds = Task.Run(() => DatabaseInitializer.InitializeAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't reach the booth database and can't start.\n\n{ex.Message}\n\n" +
                "Check that SQL Server LocalDB is installed and the MSSQLLocalDB instance is running, then restart the app.",
                "Focus & Snap -- startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }

        // Service constructors below can throw synchronously (e.g.
        // CloudinaryCloudUploadService requires CLOUDINARY_URL to be set) --
        // same reasoning as the DatabaseInitializer try/catch above: fail
        // with a clear message instead of an uncaught exception before the
        // window is shown, which previously manifested as the app silently
        // never appearing rather than a raw crash or a hang.
        try
        {
            var sessionRepository = new SqlSessionRepository(seedIds.LocationId, seedIds.PrinterId);

            // Real, not mocked, unlike Consent/Payment below -- a frame pick
            // is just a button tap with no external hardware/gateway to
            // integrate, so there's no "mock only for now" gap here. See
            // UiFrameSelectionService.
            _frameSelection = new UiFrameSelectionService();
            _frameSelection.SelectionRequested += options => Dispatcher.Invoke(() => ShowFrameOptions(options));

            // Real, not mocked, same reasoning as _frameSelection above -- a
            // star rating and a comment box is just button taps and text
            // input, no external gateway to integrate.
            _feedback = new UiFeedbackService();
            _feedback.FeedbackRequested += () => Dispatcher.Invoke(ShowFeedbackPrompt);

            // Real, not mocked, same reasoning as _frameSelection/_feedback
            // above for the ask/stop taps -- IVideoGuestbookService (the
            // actual ffmpeg-backed capture) is the part still unverified
            // against real hardware, not this UI handoff.
            _guestbookPrompt = new UiGuestbookPromptService();
            _guestbookPrompt.RecordDecisionRequested += () => Dispatcher.Invoke(ShowGuestbookAskPrompt);
            _guestbookPrompt.StopRequested += () => Dispatcher.Invoke(ShowGuestbookRecordingPrompt);

            EnsureCameraBridgeRunning();

            var services = new BoothServices(
                Camera: new PtpCameraService(),
                Printer: new SpoolerPrinterService(),
                CloudUpload: new CloudinaryCloudUploadService(),
                Sessions: sessionRepository,
                Payment: new MockQrPaymentService(),
                UploadQueue: new FileSystemPendingUploadQueue(),
                Consent: new MockConsentService(),
                Email: new MockEmailDeliveryService(),
                Branding: new GdiPhotoBrandingService(),
                Filter: new GdiPhotoFilterService(),
                Settings: new SqlBoothSettingsProvider(seedIds.LocationId),
                FrameLibrary: new SqlFrameLibraryService(seedIds.LocationId),
                FrameSelection: _frameSelection,
                FrameOverlay: new GdiFrameOverlayService(),
                Feedback: _feedback,
                GuestbookPrompt: _guestbookPrompt,
                VideoGuestbook: new FfmpegVideoGuestbookService());
            _settingsProvider = services.Settings;
            _stateMachine = new BoothStateMachine(services, mode: seedIds.LocationType);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Couldn't start the booth services.\n\n{ex.Message}",
                "Focus & Snap -- startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }
        _stateMachine.StateChanged += state => Dispatcher.Invoke(() => ShowState(state));
        _stateMachine.StateChanged += state =>
        {
            // Re-read/re-apply only at Idle -- same "next guest, no restart"
            // semantics every other setting already has. A theme change
            // saved mid-session won't repaint until the booth returns here.
            if (state == BoothState.Idle)
            {
                _ = ApplyThemeAsync();
            }
        };
        _stateMachine.CountdownTick += number => Dispatcher.Invoke(() => CountdownNumber.Text = number.ToString());
        _stateMachine.ErrorOccurred += message => Dispatcher.Invoke(() => ErrorMessage.Text = message);
        _stateMachine.PhotoUploaded += url => Dispatcher.Invoke(() => LoadQrCode(url));

        // Also flush any backlog left over from last time the app ran (e.g.
        // the venue's WiFi was down at closing time last night) -- the
        // per-session opportunistic retry inside RunSessionAsync only helps
        // once a guest actually walks up, which could be a while after open.
        _ = _stateMachine.RetryQueuedUploadsAsync();

        _liveView = new PtpLiveViewService();
        // ~7fps: fast enough to feel live, slow enough that a pipe round trip
        // per frame doesn't pile up (see _liveViewFetchInProgress below).
        _liveViewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _liveViewTimer.Tick += async (_, _) => await PollLiveViewFrameAsync();

        ShowState(_stateMachine.CurrentState);
        _ = ApplyThemeAsync();

    }

    // ProcessExit rather than Window.Closing: the startup try/catch blocks
    // above call Environment.Exit(1) directly on failure, which skips Closing
    // entirely -- confirmed via a real run against this dev machine (no
    // CLOUDINARY_URL set) that a Closing-only handler leaves the bridge
    // process orphaned when that happens. ProcessExit fires on every exit
    // path, including Environment.Exit.
    private void KillCameraBridgeIfOwned(object? sender, EventArgs e)
    {
        // Only tear down the bridge process if we're the one who launched it
        // -- if it was already running (started manually, or by a prior
        // instance of this app), leave it for whatever's still using it.
        if (_cameraBridgeProcess is { HasExited: false } process)
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
    }

    /// <summary>Launches Photobooth.CameraBridge.Host (the out-of-process pipe
    /// server that drives the camera -- see PtpCameraService and the README's
    /// "Camera: Nikon D3500" section) if it isn't already listening, so an
    /// attendant/dev doesn't have to start it by hand before opening this app.
    /// The bridge auto-detects whatever camera the device actually has (a
    /// tethered D3500 if one's attached, otherwise a laptop webcam) -- set
    /// PHOTOBOOTH_REQUIRE_DSLR=1 on real booth hardware to disable the webcam
    /// fallback instead (see Program.cs in that project for why that matters).</summary>
    private void EnsureCameraBridgeRunning()
    {
        if (PtpCameraService.IsBridgeHostRunning())
        {
            return;
        }

        string? exePath = ResolveCameraBridgeHostPath();
        if (exePath is null)
        {
            // Not found -- PtpCameraService will surface a clear "is the bridge
            // process running?" error on first capture instead of failing here.
            return;
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (Environment.GetEnvironmentVariable("PHOTOBOOTH_REQUIRE_DSLR") is "1" or "true")
        {
            startInfo.ArgumentList.Add("--require-dslr");
        }

        try
        {
            _cameraBridgeProcess = Process.Start(startInfo);
        }
        catch
        {
            // Swallow -- same reasoning as the exePath-not-found case above.
            return;
        }

        // The bridge doesn't start listening on its pipe until after it
        // finishes scanning for a camera (DSLR pass, then a webcam fallback
        // pass -- see Program.cs), which can take a few seconds. Without this
        // wait, the window shows and is tappable before that finishes, so an
        // attendant/guest who taps Start immediately hits "is the bridge
        // process running?" even though the bridge comes up moments later --
        // confirmed by reproducing that exact race against a real run. Block
        // here, before the window is shown, same reasoning as the
        // DatabaseInitializer wait above -- an extra few seconds once at
        // startup beats a confusing false error on the guest's first tap.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!PtpCameraService.IsBridgeHostRunning() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
        }
    }

    private static string? ResolveCameraBridgeHostPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("PHOTOBOOTH_CAMERA_BRIDGE_EXE");
        if (overridePath is { Length: > 0 } && System.IO.File.Exists(overridePath))
        {
            return overridePath;
        }

        // Dev layout: walk up from this app's own build output to the solution
        // root, then into the bridge host project's build output for the same
        // configuration. Deployed installs should set
        // PHOTOBOOTH_CAMERA_BRIDGE_EXE instead of relying on this.
        var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Photobooth.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return null;
        }

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var candidate = System.IO.Path.Combine(
            dir.FullName, "Photobooth.CameraBridge.Host", "bin", configuration, "net48", "Photobooth.CameraBridge.Host.exe");
        return System.IO.File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Reads the current BoothTheme and repaints the window's colors,
    /// title, brand name, and logo. Called once at startup and again every time
    /// the machine returns to Idle, so an admin's saved theme change reaches the
    /// very next guest without an app restart.</summary>
    private async Task ApplyThemeAsync()
    {
        BoothSettings settings;
        try
        {
            settings = await _settingsProvider.GetSettingsAsync();
        }
        catch (Exception)
        {
            // Best-effort: a failed settings read shouldn't crash the idle
            // screen -- it just keeps whatever theme was last applied.
            return;
        }

        BoothTheme theme = settings.Theme;

        // Every screen binds these via {StaticResource ...}, which resolves
        // to a direct reference to the brush object at XAML load time --
        // replacing the dictionary entry wouldn't repaint anything already
        // on screen. Mutating the existing (unfrozen) SolidColorBrush's
        // Color in place does, since every StaticResource reference shares
        // that same object.
        SetBrushColor("AccentBrush", theme.AccentColorHex);
        SetBrushColor("CanvasBrush", theme.CanvasColorHex);
        SetBrushColor("InkBrush", theme.InkColorHex);

        Title = theme.EventName;
        BrandNameText.Text = theme.EventName.ToUpperInvariant();

        if (theme.LogoImagePath is string logoPath && System.IO.File.Exists(logoPath))
        {
            BrandLogoImage.Source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(logoPath)));
            BrandLogoImage.Visibility = Visibility.Visible;
        }
        else
        {
            BrandLogoImage.Visibility = Visibility.Collapsed;
        }
    }

    private static void SetBrushColor(string resourceKey, string colorHex)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(colorHex);
        }
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_stateMachine.CurrentState == BoothState.Idle && !_sessionRunning)
        {
            _sessionRunning = true;
            _ = RunSessionAsync();
        }
    }

    // F12 rather than an on-screen button: guests shouldn't stumble into the
    // admin dashboard on a touchscreen kiosk, but staff with a keyboard can
    // still reach it. Only from Idle, so it can't interrupt a guest session.
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 && _stateMachine.CurrentState == BoothState.Idle)
        {
            new AdminWindow { Owner = this }.ShowDialog();
        }
    }

    private async Task RunSessionAsync()
    {
        try
        {
            await _stateMachine.RunSessionAsync();
        }
        finally
        {
            _sessionRunning = false;
        }
    }

    private void ShowState(BoothState state)
    {
        IdleView.Visibility = state == BoothState.Idle ? Visibility.Visible : Visibility.Collapsed;
        ConsentView.Visibility = state == BoothState.Consent ? Visibility.Visible : Visibility.Collapsed;
        CountdownView.Visibility = state == BoothState.Countdown ? Visibility.Visible : Visibility.Collapsed;
        CapturingView.Visibility = state == BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        ReviewingView.Visibility = state == BoothState.Reviewing ? Visibility.Visible : Visibility.Collapsed;
        FramePickerView.Visibility = state == BoothState.FramePicker ? Visibility.Visible : Visibility.Collapsed;
        PaymentView.Visibility = state == BoothState.Payment ? Visibility.Visible : Visibility.Collapsed;
        PrintingView.Visibility = state == BoothState.Printing ? Visibility.Visible : Visibility.Collapsed;
        CompleteView.Visibility = state == BoothState.Complete ? Visibility.Visible : Visibility.Collapsed;
        GuestbookView.Visibility = state == BoothState.Guestbook ? Visibility.Visible : Visibility.Collapsed;
        FeedbackView.Visibility = state == BoothState.Feedback ? Visibility.Visible : Visibility.Collapsed;
        ErrorView.Visibility = state == BoothState.Error ? Visibility.Visible : Visibility.Collapsed;

        if (state == BoothState.Payment)
        {
            PaymentInstructionsText.Text = _stateMachine.PaymentInstructions ?? string.Empty;
            if (_stateMachine.PaymentQrPng is byte[] qrPng)
            {
                PaymentQrCodeImage.Source = LoadImage(qrPng);
                PaymentQrBorder.Visibility = Visibility.Visible;
            }
            else
            {
                PaymentQrBorder.Visibility = Visibility.Collapsed;
            }
        }

        if (state == BoothState.Countdown)
        {
            _liveViewTimer.Start();
        }
        else if (_liveViewTimer.IsEnabled)
        {
            _liveViewTimer.Stop();
            _ = _liveView.StopAsync();
        }

        if (state == BoothState.Reviewing)
        {
            LoadCapturedImage(_stateMachine.LastCapturedImagePath);
        }

        bool qrEligibleScreen = state == BoothState.Printing || state == BoothState.Complete
            || state == BoothState.Guestbook || state == BoothState.Feedback;
        QrPanel.Visibility = qrEligibleScreen && _stateMachine.LastPhotoUrl != null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>Resets GuestbookView to its "ask" sub-panel, called when
    /// UiGuestbookPromptService raises RecordDecisionRequested (i.e. right as
    /// BoothStateMachine enters Guestbook and starts waiting for a tap).</summary>
    private void ShowGuestbookAskPrompt()
    {
        GuestbookAskView.Visibility = Visibility.Visible;
        GuestbookRecordingView.Visibility = Visibility.Collapsed;
    }

    /// <summary>Switches GuestbookView to its "recording" sub-panel, called when
    /// UiGuestbookPromptService raises StopRequested (i.e. right as the guest said
    /// yes and BoothStateMachine starts waiting for a Stop tap).</summary>
    private void ShowGuestbookRecordingPrompt()
    {
        GuestbookAskView.Visibility = Visibility.Collapsed;
        GuestbookRecordingView.Visibility = Visibility.Visible;
    }

    private void RecordGuestbookMessageButton_Click(object sender, RoutedEventArgs e)
    {
        _guestbookPrompt.SubmitRecordDecision(true);
    }

    private void SkipGuestbookMessageButton_Click(object sender, RoutedEventArgs e)
    {
        _guestbookPrompt.SubmitRecordDecision(false);
    }

    private void StopGuestbookRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        _guestbookPrompt.SubmitStop();
    }

    /// <summary>Populates FramePickerView with one button per active frame plus a
    /// "No frame" option, called when UiFrameSelectionService raises SelectionRequested
    /// (i.e. right as BoothStateMachine enters FramePicker and starts waiting for a tap).</summary>
    private void ShowFrameOptions(IReadOnlyList<FrameOption> options)
    {
        FrameOptionsPanel.Children.Clear();

        foreach (FrameOption option in options)
        {
            var thumbnail = new Image { Width = 130, Height = 90, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) };
            if (System.IO.File.Exists(option.ImagePath))
            {
                thumbnail.Source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(option.ImagePath)));
            }

            var content = new StackPanel();
            content.Children.Add(thumbnail);
            content.Children.Add(new TextBlock { Text = option.Name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14 });

            var button = new Button { Content = content, Tag = option, Padding = new Thickness(12), Margin = new Thickness(8) };
            button.Click += FrameOptionButton_Click;
            FrameOptionsPanel.Children.Add(button);
        }

        var skipButton = new Button { Content = "No frame", Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(8) };
        skipButton.Click += (_, _) => _frameSelection.SubmitSelection(null);
        FrameOptionsPanel.Children.Add(skipButton);
    }

    private void FrameOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FrameOption option })
        {
            _frameSelection.SubmitSelection(option);
        }
    }

    /// <summary>Resets FeedbackView's star buttons and comment box, called when
    /// UiFeedbackService raises FeedbackRequested (i.e. right as BoothStateMachine
    /// enters Feedback and starts waiting for a tap).</summary>
    private void ShowFeedbackPrompt()
    {
        _selectedFeedbackRating = 0;
        FeedbackCommentBox.Text = string.Empty;
        UpdateStarButtons();
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int rating))
        {
            _selectedFeedbackRating = rating;
            UpdateStarButtons();
        }
    }

    private void UpdateStarButtons()
    {
        foreach (Button star in FeedbackStarsPanel.Children.OfType<Button>())
        {
            if (star.Tag is string tag && int.TryParse(tag, out int rating))
            {
                star.Content = rating <= _selectedFeedbackRating ? "★" : "☆"; // filled / outline star
            }
        }
    }

    private void SubmitFeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        int? rating = _selectedFeedbackRating > 0 ? _selectedFeedbackRating : null;
        string? comment = string.IsNullOrWhiteSpace(FeedbackCommentBox.Text) ? null : FeedbackCommentBox.Text.Trim();
        _feedback.SubmitFeedback(new FeedbackResult(rating, comment));
    }

    private void SkipFeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        _feedback.SubmitFeedback(new FeedbackResult(null, null));
    }

    private void LoadQrCode(Uri photoUrl)
    {
        QrCodeImage.Source = LoadImage(QrCodeGenerator.GeneratePng(photoUrl.ToString()));

        bool qrEligibleScreen = _stateMachine.CurrentState == BoothState.Printing || _stateMachine.CurrentState == BoothState.Complete || _stateMachine.CurrentState == BoothState.Feedback;
        QrPanel.Visibility = qrEligibleScreen ? Visibility.Visible : Visibility.Collapsed;
    }

    private static BitmapImage LoadImage(byte[] png)
    {
        using var stream = new System.IO.MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task PollLiveViewFrameAsync()
    {
        // A pipe round trip can take longer than the timer interval; skip
        // this tick rather than let requests queue up behind each other.
        if (_liveViewFetchInProgress)
        {
            return;
        }

        _liveViewFetchInProgress = true;
        try
        {
            byte[]? frame = await _liveView.GetFrameAsync();
            if (frame is null)
            {
                return; // best-effort preview: keep showing the last frame
            }

            using var stream = new System.IO.MemoryStream(frame);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            LiveViewImage.Source = image;
        }
        catch
        {
            // best-effort preview: a missed frame just means the last one stays on screen
        }
        finally
        {
            _liveViewFetchInProgress = false;
        }
    }

    private void LoadCapturedImage(string? imagePath)
    {
        CapturedImage.Source = null;
        PhotoFallback.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
        {
            return;
        }

        CapturedImage.Source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(imagePath)));
        PhotoFallback.Visibility = Visibility.Collapsed;
    }
}
