namespace Photobooth.Core;

/// <summary>
/// Bridges BoothStateMachine's wait for the guest's guestbook choice/stop
/// tap to WPF button clicks -- same TaskCompletionSource handoff
/// UiFrameSelectionService and UiFeedbackService already established. Split
/// from IVideoGuestbookService because "does the guest want to, and when
/// are they done" is a pure UI interaction with no hardware dependency,
/// same reasoning that already split IFrameSelectionService (UI wait) from
/// IFrameOverlayService (GDI+ compositing).
/// </summary>
public interface IGuestbookPromptService
{
    /// <summary>Waits for the guest to choose whether to record a message.</summary>
    Task<bool> AskToRecordAsync(CancellationToken ct = default);

    /// <summary>Waits for the guest to tap Stop.</summary>
    Task WaitForStopAsync(CancellationToken ct = default);
}
