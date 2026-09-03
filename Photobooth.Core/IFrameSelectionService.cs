namespace Photobooth.Core;

/// <summary>
/// Abstracts collecting the guest's frame pick during the FramePicker state.
/// Unlike IConsentService/IPaymentService (both still mock-only, since a
/// real disclaimer/gateway needs external integration work), a frame pick
/// is just a button tap with no hardware or network dependency, so
/// UiFrameSelectionService is a real, WPF-backed implementation, not just a
/// mock.
///
/// Currently unreferenced by BoothStateMachine: FramePicker moved to the
/// guest's very first interactive step and is now driven by
/// ITemplateSelectionService picking a saved PrintTemplate, not this
/// interface picking a FrameOption overlay (see BoothServices.TemplateLibrary's
/// own doc comment). SelectFrameAsync is exercised only by MockServicesTests
/// (direct implementation tests), never by anything that calls it as part
/// of a running session. Kept, with UiFrameSelectionService/
/// MockFrameSelectionService still wired into BoothServices/
/// BoothCompositionRoot/KioskViewModel, purely so every existing
/// `new BoothServices(...)` call site (30+ across Photobooth.Tests alone)
/// keeps compiling -- not deleted here, since removing it would ripple into
/// every one of those call sites plus KioskViewModel's still-present (also
/// dead) FrameOptions/SelectFrameCommand plumbing and whatever XAML binds to
/// it, a wider change than this pass was scoped for.
/// </summary>
public interface IFrameSelectionService
{
    /// <summary>Returns the guest's chosen frame, or null if they picked "no frame". `options` is always non-empty -- BoothStateMachine only calls this when at least one active frame exists.</summary>
    Task<FrameOption?> SelectFrameAsync(IReadOnlyList<FrameOption> options, CancellationToken ct = default);
}
