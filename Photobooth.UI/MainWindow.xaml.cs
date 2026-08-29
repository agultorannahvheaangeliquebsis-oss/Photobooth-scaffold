using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Core;
using Photobooth.Data;

namespace Photobooth.UI;

public partial class MainWindow : Window
{
    private readonly BoothStateMachine _stateMachine;
    private readonly ILiveViewService _liveView;
    private readonly DispatcherTimer _liveViewTimer;
    private bool _sessionRunning;
    private bool _liveViewFetchInProgress;

    public MainWindow()
    {
        InitializeComponent();

        // Blocking at startup is acceptable here -- this runs once, before
        // the window is shown, and every session after it depends on the
        // seeded LocationId/PrinterId anyway.
        var seedIds = DatabaseInitializer.InitializeAsync().GetAwaiter().GetResult();
        var sessionRepository = new SqlSessionRepository(seedIds.LocationId, seedIds.PrinterId);

        _stateMachine = new BoothStateMachine(new PtpCameraService(), new MockPrinterService(), new MockCloudUploadService(), sessionRepository);
        _stateMachine.StateChanged += state => Dispatcher.Invoke(() => ShowState(state));
        _stateMachine.CountdownTick += number => Dispatcher.Invoke(() => CountdownNumber.Text = number.ToString());
        _stateMachine.ErrorOccurred += message => Dispatcher.Invoke(() => ErrorMessage.Text = message);
        _stateMachine.PhotoUploaded += url => Dispatcher.Invoke(() => LoadQrCode(url));

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
        CountdownView.Visibility = state == BoothState.Countdown ? Visibility.Visible : Visibility.Collapsed;
        CapturingView.Visibility = state == BoothState.Capturing ? Visibility.Visible : Visibility.Collapsed;
        ReviewingView.Visibility = state == BoothState.Reviewing ? Visibility.Visible : Visibility.Collapsed;
        PrintingView.Visibility = state == BoothState.Printing ? Visibility.Visible : Visibility.Collapsed;
        CompleteView.Visibility = state == BoothState.Complete ? Visibility.Visible : Visibility.Collapsed;
        ErrorView.Visibility = state == BoothState.Error ? Visibility.Visible : Visibility.Collapsed;

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
        byte[] png = QrCodeGenerator.GeneratePng(photoUrl.ToString());
        using var stream = new System.IO.MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        QrCodeImage.Source = image;

        bool qrEligibleScreen = _stateMachine.CurrentState == BoothState.Printing || _stateMachine.CurrentState == BoothState.Complete;
        QrPanel.Visibility = qrEligibleScreen ? Visibility.Visible : Visibility.Collapsed;
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
