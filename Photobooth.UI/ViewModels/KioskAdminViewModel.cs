using Photobooth.Core;

namespace Photobooth.UI.ViewModels;

/// <summary>
/// Backs the passcode-gated admin overlay (hidden top-right corner gesture, or
/// F12): a PIN prompt only. A correct PIN opens AdminWindow directly (see
/// OpenFullSettings) instead of showing a read-only status screen first --
/// AdminWindow already owns every write path (LocationRepository.
/// UpdateSettingsAsync and friends) and now surfaces every section through its
/// own settings dropdown, so a separate mid-event status/diagnostics screen
/// here would just be a second, out-of-sync place showing the same thing.
/// </summary>
public class KioskAdminViewModel : ObservableObject
{
    private readonly IBoothSettingsProvider _settings;

    public KioskAdminViewModel(IBoothSettingsProvider settings)
    {
        _settings = settings;

        UnlockCommand = new AsyncRelayCommand(UnlockAsync);
        CloseCommand = new RelayCommand(Close);
    }

    /// <summary>Set by KioskWindow right after construction (e.g.
    /// <c>() =&gt; new AdminWindow(_viewModel.LocationId) { Owner = this }.ShowDialog()</c>) -- a
    /// settable delegate rather than a constructor parameter because this
    /// ViewModel is built before its owning Window exists, so the Window
    /// can't be captured yet at construction time. Left null in mock/designer
    /// mode, in which case UnlockAsync just closes the overlay.</summary>
    public Action? OpenFullSettings { get; set; }

    // ----------------------------------------------------------- gate ----

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
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

    /// <summary>Opens the overlay locked. Re-locking on every open (rather than
    /// remembering the unlock for the app's lifetime) is the point of the PIN on
    /// an unattended kiosk -- an attendant who walks away mid-event doesn't leave
    /// the settings panel one gesture away for the next guest.</summary>
    public void Open()
    {
        PinInput = string.Empty;
        PinError = null;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
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

        Close();
        OpenFullSettings?.Invoke();
    }

    // ------------------------------------------------------ counters ----
    // Written by KioskViewModel as the session pipeline advances. No longer
    // displayed anywhere (the overlay that showed them is gone -- see this
    // class's own summary), but KioskViewModel still tracks them here rather
    // than needing its own separate counter fields.

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

    private string? _lastUploadUrl;
    public string? LastUploadUrl
    {
        get => _lastUploadUrl;
        set => SetProperty(ref _lastUploadUrl, value);
    }
}
