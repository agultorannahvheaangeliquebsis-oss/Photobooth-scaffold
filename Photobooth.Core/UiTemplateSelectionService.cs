namespace Photobooth.Core;

/// <summary>
/// Real ITemplateSelectionService: bridges BoothStateMachine's async wait for a
/// guest's layout pick to a WPF button click. Raises SelectionRequested (the UI
/// shows the offered templates) and completes the pending task once KioskWindow
/// calls SubmitSelection in response to a tap -- a plain TaskCompletionSource
/// handoff, no polling. Same shape as UiFrameSelectionService, which this
/// replaces as the guest-facing "frame/layout" picker.
/// </summary>
public class UiTemplateSelectionService : ITemplateSelectionService
{
    private readonly object _sync = new();
    private PendingRequest? _pending;

    public event Action<IReadOnlyList<PrintTemplate>>? SelectionRequested;
    public event Action<IReadOnlyList<PrintTemplate>, Guid>? SelectionRequestedWithToken;

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

    public Task<PrintTemplate?> SelectTemplateAsync(IReadOnlyList<PrintTemplate> options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<PrintTemplate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingRequest(Guid.NewGuid(), tcs);
        PendingRequest? previous;
        lock (_sync)
        {
            previous = _pending;
            _pending = request;

            // Registered while still holding _sync -- same reasoning
            // UiFrameSelectionService's SelectFrameAsync already gives.
            request.Cancellation = ct.Register(() => Cancel(request.Token, ct));
        }
        CancelRequest(previous);

        if (!tcs.Task.IsCompleted)
        {
            SelectionRequested?.Invoke(options);
            SelectionRequestedWithToken?.Invoke(options, request.Token);
        }

        return tcs.Task;
    }

    /// <summary>Called by KioskWindow when the guest taps a layout tile, or "use default" (null).</summary>
    public void SubmitSelection(PrintTemplate? chosen)
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

    public void SubmitSelection(PrintTemplate? chosen, Guid requestToken)
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

    private sealed class PendingRequest(Guid token, TaskCompletionSource<PrintTemplate?> source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource<PrintTemplate?> Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
