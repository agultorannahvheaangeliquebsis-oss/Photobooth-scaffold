namespace Photobooth.Core;

/// <summary>
/// Real IGuestbookPromptService: bridges BoothStateMachine's async waits for
/// the guest's record/skip choice and their Stop tap to WPF button clicks,
/// same TaskCompletionSource handoff UiFrameSelectionService and
/// UiFeedbackService already established. Two independent pending waits
/// (one per method) rather than one, since "does the guest want to record"
/// and "are they done recording" happen at different points and MainWindow
/// shows a different sub-panel for each.
/// </summary>
public class UiGuestbookPromptService : IGuestbookPromptService
{
    private TaskCompletionSource<bool>? _pendingAsk;
    private TaskCompletionSource? _pendingStop;

    /// <summary>Raised when BoothStateMachine starts waiting for the guest's record/skip choice -- the UI shows the "leave a message?" sub-panel.</summary>
    public event Action? RecordDecisionRequested;

    /// <summary>Raised when BoothStateMachine starts waiting for the guest to tap Stop -- the UI shows the "recording..." sub-panel.</summary>
    public event Action? StopRequested;

    public Task<bool> AskToRecordAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAsk = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        RecordDecisionRequested?.Invoke();
        return tcs.Task;
    }

    public Task WaitForStopAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingStop = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        StopRequested?.Invoke();
        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest taps Yes/No in response to the record prompt.</summary>
    public void SubmitRecordDecision(bool wantsToRecord)
    {
        _pendingAsk?.TrySetResult(wantsToRecord);
        _pendingAsk = null;
    }

    /// <summary>Called by MainWindow when the guest taps Stop.</summary>
    public void SubmitStop()
    {
        _pendingStop?.TrySetResult();
        _pendingStop = null;
    }
}
