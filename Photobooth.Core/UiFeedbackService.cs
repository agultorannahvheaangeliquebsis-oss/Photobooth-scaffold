namespace Photobooth.Core;

/// <summary>
/// Real IFeedbackService: bridges BoothStateMachine's async wait for a
/// guest's rating/comment to WPF button taps, same TaskCompletionSource
/// handoff UiFrameSelectionService already established. Raises
/// FeedbackRequested (the UI shows the star buttons and comment box) and
/// completes once MainWindow calls SubmitFeedback in response to a tap.
/// </summary>
public class UiFeedbackService : IFeedbackService
{
    private TaskCompletionSource<FeedbackResult>? _pending;

    public event Action? FeedbackRequested;

    public Task<FeedbackResult> CollectAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FeedbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        FeedbackRequested?.Invoke();
        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest taps a star rating and/or Submit,
    /// or Skip (an empty FeedbackResult).</summary>
    public void SubmitFeedback(FeedbackResult result)
    {
        _pending?.TrySetResult(result);
        _pending = null;
    }
}
