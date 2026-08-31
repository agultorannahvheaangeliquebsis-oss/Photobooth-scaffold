namespace Photobooth.UI.ViewModels;

/// <summary>
/// The screens KioskWindow's state switcher can show. Deliberately coarser
/// than <see cref="Photobooth.Core.BoothState"/>: BoothState tracks every step
/// of the session pipeline (fourteen of them), while this is the guest-facing
/// screen vocabulary -- the five dslrBooth mirrors, plus an error screen.
///
/// KioskViewModel.MapScreen owns the BoothState -> KioskScreen mapping and
/// documents each grouping. Keeping them separate means adding a pipeline step
/// (another consent gate, another upsell) doesn't force a new screen, and
/// re-skinning a screen doesn't touch the state machine.
/// </summary>
public enum KioskScreen
{
    /// <summary>Attract loop: branding, "Touch Screen to Begin", mode tiles.</summary>
    Idle,

    /// <summary>Live camera feed behind a scrim with the big countdown digit.</summary>
    Countdown,

    /// <summary>Shutter moment: flash wash plus "Say Cheese!".</summary>
    Capture,

    /// <summary>Anything the guest waits through with nothing to act on: consent
    /// (mock-only, auto-accepts), template stitching, spooling.</summary>
    Processing,

    /// <summary>Guest picks a photo frame (or none) from the active frame library.</summary>
    FramePicker,

    /// <summary>Vendo-mode payment: instructions plus a QR to scan and pay.</summary>
    Payment,

    /// <summary>Ask/record sub-screens for the video guestbook message.</summary>
    Guestbook,

    /// <summary>Star rating plus an optional comment.</summary>
    Feedback,

    /// <summary>Admin-authored questions, answered or skipped.</summary>
    Survey,

    /// <summary>Review and share: template preview, QR, email/SMS, print, done timer.</summary>
    Review,

    /// <summary>Friendly failure screen; the state machine resets to Idle on its own.</summary>
    Error
}

/// <summary>Which sub-panel KioskScreen.Guestbook shows -- the ask prompt or the
/// in-progress recording view, mirroring MainWindow's GuestbookAskView/
/// GuestbookRecordingView toggle.</summary>
public enum GuestbookSubScreen
{
    Ask,
    Recording
}

/// <summary>
/// The capture modes offered on the idle screen. The values match
/// <see cref="Photobooth.Core.CaptureSettings.Mode"/>'s strings exactly
/// ("Photo"/"GIF"/"Boomerang"/"Video"), because that is what
/// BoothStateMachine switches on -- see CaptureModeOverrideSettingsProvider,
/// which is how a guest's tile choice reaches the state machine.
/// </summary>
public enum CaptureMode
{
    Photo,
    GIF,
    Boomerang,
    Video
}
