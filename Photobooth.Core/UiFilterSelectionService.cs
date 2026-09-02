namespace Photobooth.Core;

/// <summary>
/// Real IFilterSelectionService: bridges BoothStateMachine's async wait for a
/// guest's filter pick to a WPF button click. Same TaskCompletionSource handoff
/// UiFrameSelectionService already established for FramePicker.
/// </summary>
public class UiFilterSelectionService : IFilterSelectionService
{
    private readonly object _sync = new();
    private PendingRequest? _pending;

    public event Action<IReadOnlyList<FilterOption>>? SelectionRequested;
    public event Action<IReadOnlyList<FilterOption>, Guid>? SelectionRequestedWithToken;

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

    public Task<FilterOption?> SelectFilterAsync(IReadOnlyList<FilterOption> options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FilterOption?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingRequest(Guid.NewGuid(), tcs);
        PendingRequest? previous;
        lock (_sync)
        {
            previous = _pending;
            _pending = request;

            // Registered while still holding _sync -- see UiFeedbackService's
            // CollectAsync for why: a synchronously-firing Register (ct
            // already cancelled) needs _pending already pointing at `request`
            // for Cancel's own staleness check to resolve it correctly.
            request.Cancellation = ct.Register(() => Cancel(request.Token, ct));
        }
        CancelRequest(previous);

        // If the registration above already resolved (and cleared _pending) --
        // e.g. ct was already cancelled -- don't announce a prompt nothing
        // will ever answer.
        if (!tcs.Task.IsCompleted)
        {
            SelectionRequested?.Invoke(options);
            SelectionRequestedWithToken?.Invoke(options, request.Token);
        }

        return tcs.Task;
    }

    /// <summary>Called by KioskWindow when the guest taps a filter preview, or "Original" (null).</summary>
    public void SubmitSelection(FilterOption? chosen)
    {
        PendingRequest? request;
        lock (_sync)
        {
            request = _pending;
        }

        if (request is not null)
        {
            SubmitSelection(chosen, request.Token);
        }
    }

    public void SubmitSelection(FilterOption? chosen, Guid requestToken)
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
        request.Source.TrySetResult(chosen);
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

    private sealed class PendingRequest(Guid token, TaskCompletionSource<FilterOption?> source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource<FilterOption?> Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
