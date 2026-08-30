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
    private TaskCompletionSource<FrameOption?>? _pending;

    public event Action<IReadOnlyList<FrameOption>>? SelectionRequested;

    public Task<FrameOption?> SelectFrameAsync(IReadOnlyList<FrameOption> options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FrameOption?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        SelectionRequested?.Invoke(options);
        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest taps a frame thumbnail, or "no frame" (null).</summary>
    public void SubmitSelection(FrameOption? chosen)
    {
        _pending?.TrySetResult(chosen);
        _pending = null;
    }
}
