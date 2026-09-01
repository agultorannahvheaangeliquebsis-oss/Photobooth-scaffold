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

    /// <summary>Guest picks a filter (or "Original") from previews of their own
    /// just-captured photo -- see IFilterSelectionService, EffectsSettings.FiltersMode.
    /// Only reached when Filters is enabled and FiltersMode is "Ask"; Auto mode applies
    /// the first enabled preset silently instead, same "empty pool/disabled = feature
    /// invisible" reasoning FramePicker already established.</summary>
    FilterPicker,

    Reviewing,
    FramePicker,
    Payment,
    Printing,
    Complete,
    Guestbook,
    Feedback,

    /// <summary>Post-Feedback question-builder prompt (see BUILD_PLAN.md's Phase 6
    /// scope text, ISurveyService) -- shown only when SurveySettings.Enabled is on
    /// and there's at least one active SurveyQuestion, same "empty table = feature
    /// invisible" reasoning FramePicker already established for Frame.</summary>
    Survey,
    Error
}
