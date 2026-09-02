namespace Photobooth.Core;

/// <summary>
/// Real IGuestbookPromptService: bridges BoothStateMachine's async waits for
/// the guest's record/skip choice and their Stop tap to WPF button clicks,
/// same TaskCompletionSource handoff UiFrameSelectionService and
/// UiFeedbackService already established. Two independent pending waits
/// (one per method, each with its own lock/token pair below) rather than
/// one, since "does the guest want to record" and "are they done recording"
/// happen at different points and KioskWindow shows a different sub-panel
/// for each. Also carries the same lock+token staleness guard
/// UiFeedbackService/UiFrameSelectionService/UiFilterSelectionService
/// already use, so a tap meant for an old, already-abandoned prompt (e.g.
/// one orphaned by BoothStateMachine's guest-idle timeout on
/// AskToRecordAsync) can't resolve a newer one that replaced it.
/// </summary>
public class UiGuestbookPromptService : IGuestbookPromptService
{
    private readonly object _askSync = new();
    private readonly object _stopSync = new();
    private PendingAskRequest? _pendingAsk;
    private PendingStopRequest? _pendingStop;

    /// <summary>Raised when BoothStateMachine starts waiting for the guest's record/skip choice -- the UI shows the "leave a message?" sub-panel.</summary>
    public event Action? RecordDecisionRequested;
    public event Action<Guid>? RecordDecisionRequestedWithToken;

    /// <summary>Raised when BoothStateMachine starts waiting for the guest to tap Stop -- the UI shows the "recording..." sub-panel.</summary>
    public event Action? StopRequested;
    public event Action<Guid>? StopRequestedWithToken;

    public Guid? CurrentAskRequestToken
    {
        get
        {
            lock (_askSync)
            {
                return _pendingAsk?.Token;
            }
        }
    }

    public Guid? CurrentStopRequestToken
    {
        get
        {
            lock (_stopSync)
            {
                return _pendingStop?.Token;
            }
        }
    }

    public Task<bool> AskToRecordAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingAskRequest(Guid.NewGuid(), tcs);
        PendingAskRequest? previous;
        lock (_askSync)
        {
            previous = _pendingAsk;
            _pendingAsk = request;

            // Registered while still holding _askSync -- see
            // UiFeedbackService's CollectAsync for why: a synchronously-firing
            // Register (ct already cancelled) needs _pendingAsk already
            // pointing at `request` for CancelAsk's own staleness check to
            // resolve it correctly.
            request.Cancellation = ct.Register(() => CancelAsk(request.Token, ct));
        }
        CancelAskRequest(previous);

        // If the registration above already resolved (and cleared _pendingAsk)
        // -- e.g. ct was already cancelled -- don't announce a prompt nothing
        // will ever answer.
        if (!tcs.Task.IsCompleted)
        {
            RecordDecisionRequested?.Invoke();
            RecordDecisionRequestedWithToken?.Invoke(request.Token);
        }

        return tcs.Task;
    }

    public Task WaitForStopAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingStopRequest(Guid.NewGuid(), tcs);
        PendingStopRequest? previous;
        lock (_stopSync)
        {
            previous = _pendingStop;
            _pendingStop = request;

            // Registered while still holding _stopSync -- same reasoning as
            // AskToRecordAsync above.
            request.Cancellation = ct.Register(() => CancelStop(request.Token, ct));
        }
        CancelStopRequest(previous);

        // If the registration above already resolved (and cleared
        // _pendingStop) -- e.g. ct was already cancelled -- don't announce a
        // prompt nothing will ever answer.
        if (!tcs.Task.IsCompleted)
        {
            StopRequested?.Invoke();
            StopRequestedWithToken?.Invoke(request.Token);
        }

        return tcs.Task;
    }

    /// <summary>Called by KioskWindow when the guest taps Yes/No in response to the record prompt.</summary>
    public void SubmitRecordDecision(bool wantsToRecord)
    {
        PendingAskRequest? request;
        lock (_askSync)
        {
            request = _pendingAsk;
        }

        if (request is not null)
        {
            SubmitRecordDecision(wantsToRecord, request.Token);
        }
    }

    public void SubmitRecordDecision(bool wantsToRecord, Guid requestToken)
    {
        PendingAskRequest? request;
        lock (_askSync)
        {
            if (_pendingAsk?.Token != requestToken)
            {
                return;
            }

            request = _pendingAsk;
            _pendingAsk = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetResult(wantsToRecord);
    }

    /// <summary>Called by KioskWindow when the guest taps Stop.</summary>
    public void SubmitStop()
    {
        PendingStopRequest? request;
        lock (_stopSync)
        {
            request = _pendingStop;
        }

        if (request is not null)
        {
            SubmitStop(request.Token);
        }
    }

    public void SubmitStop(Guid requestToken)
    {
        PendingStopRequest? request;
        lock (_stopSync)
        {
            if (_pendingStop?.Token != requestToken)
            {
                return;
            }

            request = _pendingStop;
            _pendingStop = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetResult();
    }

    /// <summary>Cancels whichever wait(s) are outstanding -- called when
    /// BoothStateMachine's guest-idle timeout fires on AskToRecordAsync (a
    /// guest who walked away without tapping Record or Skip), and on every
    /// return to Idle (see KioskViewModel.ResetForNextGuest) as a
    /// belt-and-suspenders cleanup. Same "orphaned wait gets explicitly
    /// cancelled, not left to resolve into a future guest's tap" reasoning
    /// UiFeedbackService/UiFrameSelectionService/UiFilterSelectionService's
    /// own CancelPending already establishes.</summary>
    public void CancelPending()
    {
        PendingAskRequest? ask;
        lock (_askSync)
        {
            ask = _pendingAsk;
            _pendingAsk = null;
        }
        CancelAskRequest(ask);

        PendingStopRequest? stop;
        lock (_stopSync)
        {
            stop = _pendingStop;
            _pendingStop = null;
        }
        CancelStopRequest(stop);
    }

    private void CancelAsk(Guid requestToken, CancellationToken cancellationToken)
    {
        PendingAskRequest? request;
        lock (_askSync)
        {
            if (_pendingAsk?.Token != requestToken)
            {
                return;
            }

            request = _pendingAsk;
            _pendingAsk = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetCanceled(cancellationToken);
    }

    private void CancelStop(Guid requestToken, CancellationToken cancellationToken)
    {
        PendingStopRequest? request;
        lock (_stopSync)
        {
            if (_pendingStop?.Token != requestToken)
            {
                return;
            }

            request = _pendingStop;
            _pendingStop = null;
        }

        request!.Cancellation.Dispose();
        request.Source.TrySetCanceled(cancellationToken);
    }

    private static void CancelAskRequest(PendingAskRequest? request)
    {
        if (request is not null)
        {
            request.Cancellation.Dispose();
            request.Source.TrySetCanceled();
        }
    }

    private static void CancelStopRequest(PendingStopRequest? request)
    {
        if (request is not null)
        {
            request.Cancellation.Dispose();
            request.Source.TrySetCanceled();
        }
    }

    private sealed class PendingAskRequest(Guid token, TaskCompletionSource<bool> source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource<bool> Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }

    private sealed class PendingStopRequest(Guid token, TaskCompletionSource source)
    {
        public Guid Token { get; } = token;
        public TaskCompletionSource Source { get; } = source;
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
