namespace Photobooth.Core;

/// <summary>
/// Real IFilterSelectionService: bridges BoothStateMachine's async wait for a
/// guest's filter pick to a WPF button click. Same TaskCompletionSource handoff
/// UiFrameSelectionService already established for FramePicker.
/// </summary>
public class UiFilterSelectionService : IFilterSelectionService
{
    private TaskCompletionSource<FilterOption?>? _pending;

    public event Action<IReadOnlyList<FilterOption>>? SelectionRequested;

    public Task<FilterOption?> SelectFilterAsync(IReadOnlyList<FilterOption> options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<FilterOption?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        SelectionRequested?.Invoke(options);
        return tcs.Task;
    }

    /// <summary>Called by KioskWindow when the guest taps a filter preview, or "Original" (null).</summary>
    public void SubmitSelection(FilterOption? chosen)
    {
        _pending?.TrySetResult(chosen);
        _pending = null;
    }
}
