using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

public partial class MainWindow : Window
{
    private BoothStateMachine _stateMachine = null!;
    private ILiveViewService _liveView = null!;
    private readonly DispatcherTimer _liveViewTimer;
    private bool _sessionRunning;
    private bool _liveViewFetchInProgress;

    public MainWindow()
    {
        InitializeComponent();

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
                Settings: new SqlBoothSettingsProvider(seedIds.LocationId));
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
        PaymentView.Visibility = state == BoothState.Payment ? Visibility.Visible : Visibility.Collapsed;
        PrintingView.Visibility = state == BoothState.Printing ? Visibility.Visible : Visibility.Collapsed;
        CompleteView.Visibility = state == BoothState.Complete ? Visibility.Visible : Visibility.Collapsed;
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

        bool qrEligibleScreen = state == BoothState.Printing || state == BoothState.Complete;
        QrPanel.Visibility = qrEligibleScreen && _stateMachine.LastPhotoUrl != null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void LoadQrCode(Uri photoUrl)
    {
        QrCodeImage.Source = LoadImage(QrCodeGenerator.GeneratePng(photoUrl.ToString()));

        bool qrEligibleScreen = _stateMachine.CurrentState == BoothState.Printing || _stateMachine.CurrentState == BoothState.Complete;
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
