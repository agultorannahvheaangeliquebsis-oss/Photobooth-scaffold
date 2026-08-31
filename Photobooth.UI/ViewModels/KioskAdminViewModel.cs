using Photobooth.Core;

namespace Photobooth.UI.ViewModels;

/// <summary>
/// Backs the passcode-gated admin overlay (hidden top-right corner gesture, or
/// F12). Read-only by design: it reports what the booth is currently doing --
/// camera, printer, cloud sync, print counters -- so an attendant can triage a
/// problem mid-event without walking the guest-facing screen out of its
/// session. Editing and saving settings stays in AdminWindow, which already
/// owns every write path (LocationRepository.UpdateSettingsAsync and friends);
/// duplicating those forms here would mean two places to keep in sync and two
/// chances to write conflicting values while a session is mid-flight.
///
/// The one action it does take is RetryUploadsCommand, because "the venue WiFi
/// just came back, flush the backlog now" is the request an attendant actually
/// has during an event, and it is idempotent (see
/// BoothStateMachine.RetryQueuedUploadsAsync).
/// </summary>
public class KioskAdminViewModel : ObservableObject
{
    private readonly IBoothSettingsProvider _settings;
    private readonly IPendingUploadQueue _uploadQueue;
    private readonly Func<Task> _retryUploads;

    public KioskAdminViewModel(
        IBoothSettingsProvider settings,
        IPendingUploadQueue uploadQueue,
        Func<Task> retryUploads)
    {
        _settings = settings;
        _uploadQueue = uploadQueue;
        _retryUploads = retryUploads;

        UnlockCommand = new AsyncRelayCommand(UnlockAsync);
        CloseCommand = new RelayCommand(Close);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RetryUploadsCommand = new AsyncRelayCommand(RetryUploadsAsync);
        RetryUploadsCommand.ExceptionHandler = ex => CloudStatus = $"Retry failed: {ex.Message}";
        OpenFullSettingsCommand = new RelayCommand(() => OpenFullSettings?.Invoke());
    }

    /// <summary>Set by KioskWindow right after construction (e.g.
    /// <c>() =&gt; new AdminWindow { Owner = this }.ShowDialog()</c>) -- a
    /// settable delegate rather than a constructor parameter because this
    /// ViewModel is built before its owning Window exists, so the Window
    /// can't be captured yet at construction time. Left null in mock/designer
    /// mode, in which case OpenFullSettingsCommand no-ops.</summary>
    public Action? OpenFullSettings { get; set; }

    public RelayCommand OpenFullSettingsCommand { get; }

    // ----------------------------------------------------------- gate ----

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    private bool _isUnlocked;
    public bool IsUnlocked
    {
        get => _isUnlocked;
        private set => SetProperty(ref _isUnlocked, value);
    }

    private string _pinInput = string.Empty;
    public string PinInput
    {
        get => _pinInput;
        set => SetProperty(ref _pinInput, value);
    }

    private string? _pinError;
    public string? PinError
    {
        get => _pinError;
        private set => SetProperty(ref _pinError, value);
    }

    public AsyncRelayCommand UnlockCommand { get; }
    public RelayCommand CloseCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RetryUploadsCommand { get; }

    /// <summary>Opens the overlay locked. Re-locking on every open (rather than
    /// remembering the unlock for the app's lifetime) is the point of the PIN on
    /// an unattended kiosk -- an attendant who walks away mid-event doesn't leave
    /// the settings panel one gesture away for the next guest.</summary>
    public void Open()
    {
        PinInput = string.Empty;
        PinError = null;
        IsUnlocked = false;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        IsUnlocked = false;
        PinInput = string.Empty;
        PinError = null;
    }

    private async Task UnlockAsync()
    {
        BoothSettings settings;
        try
        {
            // Fetched fresh, not cached: a PIN changed in AdminWindow during
            // this same run has to take effect immediately, same reasoning
            // MainWindow's SetupUnlockButton_Click already applies.
            settings = await _settings.GetSettingsAsync();
        }
        catch (Exception ex)
        {
            PinError = $"Couldn't check the PIN: {ex.Message}";
            return;
        }

        if (PinInput != settings.AdminPin)
        {
            PinError = "Incorrect PIN.";
            PinInput = string.Empty;
            return;
        }

        PinError = null;
        PinInput = string.Empty;
        IsUnlocked = true;
        await RefreshAsync();
    }

    // ------------------------------------------------------ camera tab ----

    private string _cameraBridgeStatus = "Not checked";
    public string CameraBridgeStatus
    {
        get => _cameraBridgeStatus;
        private set => SetProperty(ref _cameraBridgeStatus, value);
    }

    private string _captureMode = CaptureSettings.Default.Mode;
    public string CaptureMode
    {
        get => _captureMode;
        private set => SetProperty(ref _captureMode, value);
    }

    private int _countdownSeconds = 3;
    public int CountdownSeconds
    {
        get => _countdownSeconds;
        private set => SetProperty(ref _countdownSeconds, value);
    }

    private bool _showLiveView = true;
    public bool ShowLiveView
    {
        get => _showLiveView;
        private set => SetProperty(ref _showLiveView, value);
    }

    private bool _mirrorLiveView = true;
    public bool MirrorLiveView
    {
        get => _mirrorLiveView;
        private set => SetProperty(ref _mirrorLiveView, value);
    }

    private int _liveViewRotation;
    public int LiveViewRotation
    {
        get => _liveViewRotation;
        private set => SetProperty(ref _liveViewRotation, value);
    }

    private bool _glamFilterEnabled;
    public bool GlamFilterEnabled
    {
        get => _glamFilterEnabled;
        private set => SetProperty(ref _glamFilterEnabled, value);
    }

    // ----------------------------------------------------- printer tab ----

    private string _printLayout = "Single";
    public string PrintLayout
    {
        get => _printLayout;
        private set => SetProperty(ref _printLayout, value);
    }

    private string _paperSize = "4 x 6 in";
    public string PaperSize
    {
        get => _paperSize;
        private set => SetProperty(ref _paperSize, value);
    }

    private int _stripCopies = 1;
    public int StripCopies
    {
        get => _stripCopies;
        private set => SetProperty(ref _stripCopies, value);
    }

    private bool _printAutomatically = true;
    public bool PrintAutomatically
    {
        get => _printAutomatically;
        private set => SetProperty(ref _printAutomatically, value);
    }

    private string _printSharpening = "Medium";
    public string PrintSharpening
    {
        get => _printSharpening;
        private set => SetProperty(ref _printSharpening, value);
    }

    private string _systemPrinter = "Unknown";
    public string SystemPrinter
    {
        get => _systemPrinter;
        private set => SetProperty(ref _systemPrinter, value);
    }

    // ------------------------------------------------------- cloud tab ----

    private int _pendingUploadCount;
    public int PendingUploadCount
    {
        get => _pendingUploadCount;
        private set => SetProperty(ref _pendingUploadCount, value);
    }

    private string _cloudStatus = "Not checked";
    public string CloudStatus
    {
        get => _cloudStatus;
        private set => SetProperty(ref _cloudStatus, value);
    }

    private string? _lastUploadUrl;
    public string? LastUploadUrl
    {
        get => _lastUploadUrl;
        set => SetProperty(ref _lastUploadUrl, value);
    }

    private bool _emailEnabled = true;
    public bool EmailEnabled
    {
        get => _emailEnabled;
        private set => SetProperty(ref _emailEnabled, value);
    }

    private bool _smsEnabled;
    public bool SmsEnabled
    {
        get => _smsEnabled;
        private set => SetProperty(ref _smsEnabled, value);
    }

    private bool _qrEnabled = true;
    public bool QrEnabled
    {
        get => _qrEnabled;
        private set => SetProperty(ref _qrEnabled, value);
    }

    // ---------------------------------------------------- counters tab ----
    // Written by KioskViewModel as the session pipeline advances; the overlay
    // only ever reads them, hence the public setters here and nowhere else.

    private int _printsThisSession;
    public int PrintsThisSession
    {
        get => _printsThisSession;
        set => SetProperty(ref _printsThisSession, value);
    }

    private int _printsThisEvent;
    public int PrintsThisEvent
    {
        get => _printsThisEvent;
        set => SetProperty(ref _printsThisEvent, value);
    }

    private int _sessionsThisRun;
    public int SessionsThisRun
    {
        get => _sessionsThisRun;
        set => SetProperty(ref _sessionsThisRun, value);
    }

    private int _errorsThisRun;
    public int ErrorsThisRun
    {
        get => _errorsThisRun;
        set => SetProperty(ref _errorsThisRun, value);
    }

    private int _printLimitPerSession = PrintOptions.Default.PrintLimitPerSession;
    public int PrintLimitPerSession
    {
        get => _printLimitPerSession;
        private set => SetProperty(ref _printLimitPerSession, value);
    }

    private int _printLimitPerEvent = PrintOptions.Default.PrintLimitPerEvent;
    public int PrintLimitPerEvent
    {
        get => _printLimitPerEvent;
        private set => SetProperty(ref _printLimitPerEvent, value);
    }

    public DateTime RunStartedAt { get; } = DateTime.Now;

    // -------------------------------------------------------- refresh ----

    /// <summary>Re-reads everything the four tabs show. Called on unlock and
    /// from the overlay's Refresh button -- not on a timer, since the overlay
    /// is only ever on screen while an attendant is standing there looking at
    /// it, and a background poll would keep hitting the DB and the camera pipe
    /// for the entire event.</summary>
    private async Task RefreshAsync()
    {
        try
        {
            BoothSettings settings = await _settings.GetSettingsAsync();

            CaptureMode = settings.Capture.Mode;
            CountdownSeconds = settings.CountdownSeconds;
            ShowLiveView = settings.Screen.ShowLiveView;
            MirrorLiveView = settings.Screen.MirrorLiveView;
            LiveViewRotation = settings.Screen.LiveViewRotation;
            GlamFilterEnabled = settings.GlamFilterEnabled;

            PrintLayout = settings.PrintTemplate.Layout;
            PaperSize = $"{settings.PrintTemplate.WidthInches:0.##} x {settings.PrintTemplate.HeightInches:0.##} in";
            StripCopies = settings.PrintTemplate.StripCopies;
            PrintAutomatically = settings.PrintOptions.PrintAutomatically;
            PrintSharpening = settings.PrintOptions.PrintSharpening;
            PrintLimitPerSession = settings.PrintOptions.PrintLimitPerSession;
            PrintLimitPerEvent = settings.PrintOptions.PrintLimitPerEvent;

            EmailEnabled = settings.Sharing.EmailEnabled;
            SmsEnabled = settings.Sharing.SmsEnabled;
            QrEnabled = settings.Sharing.QrEnabled;
        }
        catch (Exception ex)
        {
            PinError = $"Couldn't read settings: {ex.Message}";
        }

        CameraBridgeStatus = PtpCameraService.IsBridgeHostRunning()
            ? "Bridge running"
            : "Bridge not responding";

        try
        {
            SystemPrinter = new System.Drawing.Printing.PrinterSettings().PrinterName;
        }
        catch (Exception)
        {
            // No printers installed at all throws rather than returning a
            // blank name; a dev machine without a printer shouldn't blank out
            // the other three tabs.
            SystemPrinter = "No printer installed";
        }

        try
        {
            IReadOnlyList<PendingUpload> pending = await _uploadQueue.GetPendingAsync();
            PendingUploadCount = pending.Count;
            CloudStatus = pending.Count == 0 ? "All photos uploaded" : $"{pending.Count} waiting to upload";
        }
        catch (Exception ex)
        {
            CloudStatus = $"Queue unreadable: {ex.Message}";
        }
    }

    private async Task RetryUploadsAsync()
    {
        CloudStatus = "Retrying...";
        await _retryUploads();
        await RefreshAsync();
    }
}
