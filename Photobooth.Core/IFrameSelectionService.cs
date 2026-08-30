namespace Photobooth.Core;

/// <summary>
/// Abstracts collecting the guest's frame pick during the FramePicker state.
/// Same interface-plus-mock seam as everything else -- BoothStateMachine
/// only ever talks to this interface. Unlike IConsentService/IPaymentService
/// (both still mock-only, since a real disclaimer/gateway needs external
/// integration work), a frame pick is just a button tap with no hardware or
/// network dependency, so UiFrameSelectionService is a real, WPF-backed
/// implementation, not just a mock.
/// </summary>
public interface IFrameSelectionService
{
    /// <summary>Returns the guest's chosen frame, or null if they picked "no frame". `options` is always non-empty -- BoothStateMachine only calls this when at least one active frame exists.</summary>
    Task<FrameOption?> SelectFrameAsync(IReadOnlyList<FrameOption> options, CancellationToken ct = default);
}
