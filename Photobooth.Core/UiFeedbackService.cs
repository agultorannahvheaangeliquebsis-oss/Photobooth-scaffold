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
    private readonly object _sync = new();
    private PendingRequest? _pending;

    public event Action? FeedbackRequested;
    public event Action<Guid>? FeedbackRequestedWithToken;

    public Guid? CurrentRequestToken
    {
        get
        {
            lock (_sync)
            {
                return _pending?.Token;
            }
        }
    }

    public Task<FeedbackResult> CollectAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FeedbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingRequest(Guid.NewGuid(), tcs);
        PendingRequest? previous;
        lock (_sync)
        {
            previous = _pending;
            _pending = request;

            // Registered while still holding _sync, not after releasing it: if
            // ct is already cancelled, Register invokes Cancel(...)
            // synchronously on this same thread -- lock is re-entrant
            // per-thread, so Cancel's own lock acquisition below succeeds
            // immediately, and _pending already equals `request` (assigned
            // just above), so it resolves tcs.Task as cancelled right here
            // instead of leaving request.Cancellation pointing at a
            // not-yet-assigned field, or (if Register instead ran before this
            // assignment) silently dropping the cancellation because Cancel's
            // own staleness check would never match a request nothing has
            // published yet.
            request.Cancellation = ct.Register(() => Cancel(request.Token, ct));
        }
        CancelRequest(previous);

        // If the registration above already resolved (and cleared _pending) --
        // e.g. ct was already cancelled -- don't announce a prompt nothing
        // will ever answer.
        if (!tcs.Task.IsCompleted)
        {
            FeedbackRequested?.Invoke();
            FeedbackRequestedWithToken?.Invoke(request.Token);
        }

        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest taps a star rating and/or Submit,
    /// or Skip (an empty FeedbackResult).</summary>
    public void SubmitFeedback(FeedbackResult result)
    {
        PendingRequest? request;
        lock (_sync)
        {
            request = _pending;
        }

        if (request is not null)
        {
            SubmitFeedback(result, request.Token);
        }
    }

    public void SubmitFeedback(FeedbackResult result, Guid requestToken)
    {
        PendingRequest? request;
        lock (_sync)
        {
            if (_pending?.Token != requestToken)
            {
                return;
            }

            request = _pending;
            _pending = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetResult(result);
    }

    public void CancelPending()
    {
        PendingRequest? request;
        lock (_sync)
        {
            request = _pending;
            _pending = null;
        }

        CancelRequest(request);
    }

    private void Cancel(Guid requestToken, CancellationToken cancellationToken)
    {
        PendingRequest? request;
        lock (_sync)
        {
            if (_pending?.Token != requestToken)
            {
                return;
            }

            request = _pending;
            _pending = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetCanceled(cancellationToken);
    }

    private static void CancelRequest(PendingRequest? request)
    {
        if (request is not null)
        {
            request.Cancellation.Dispose();
            request.Source.TrySetCanceled();
        }
    }

    private sealed class PendingRequest(Guid token, TaskCompletionSource<FeedbackResult> source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource<FeedbackResult> Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
