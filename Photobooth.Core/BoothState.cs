namespace Photobooth.Core;

/// <summary>
/// The states a single guest session moves through. The idle screen loops
/// back here after every session, whether it finished normally or errored out.
/// </summary>
public enum BoothState
{
    /// <summary>Initial state on every app launch. Gated behind the admin PIN
    /// in MainWindow's SetupView until an admin reviews/adjusts settings and
    /// taps Launch Event -- see BoothStateMachine.LaunchEvent, the only
    /// transition out of this state. Guests can't reach Idle's tap-to-start
    /// until that happens, so nothing here can take a picture beforehand.</summary>
    Setup,
    Idle,
    Consent,
    Countdown,
    Capturing,
    Reviewing,
    FramePicker,
    Payment,
    Printing,
    Complete,
    Guestbook,
    Feedback,
    Error
}
