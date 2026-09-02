namespace Photobooth.Core;

/// <summary>
/// Real IFrameSelectionService: bridges BoothStateMachine's async wait for a
/// guest's frame pick to a WPF button click. Raises SelectionRequested (the
/// UI shows the offered thumbnails) and completes the pending task once
/// MainWindow calls SubmitSelection in response to a tap -- a plain
/// TaskCompletionSource handoff, no polling.
/// </summary>
public class UiFrameSelectionService : IFrameSelectionService
{
    private readonly object _sync = new();
    private PendingRequest? _pending;

    public event Action<IReadOnlyList<FrameOption>>? SelectionRequested;
    public event Action<IReadOnlyList<FrameOption>, Guid>? SelectionRequestedWithToken;

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

    public Task<FrameOption?> SelectFrameAsync(IReadOnlyList<FrameOption> options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FrameOption?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingRequest(Guid.NewGuid(), tcs);
        PendingRequest? previous;
        lock (_sync)
        {
            previous = _pending;
            _pending = request;
        }
        CancelRequest(previous);

        request.Cancellation = ct.Register(() => Cancel(request.Token, ct));

        SelectionRequested?.Invoke(options);
        SelectionRequestedWithToken?.Invoke(options, request.Token);
        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest taps a frame thumbnail, or "no frame" (null).</summary>
    public void SubmitSelection(FrameOption? chosen)
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

    public void SubmitSelection(FrameOption? chosen, Guid requestToken)
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

    private sealed class PendingRequest(Guid token, TaskCompletionSource<FrameOption?> source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource<FrameOption?> Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
