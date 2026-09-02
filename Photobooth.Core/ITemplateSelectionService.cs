namespace Photobooth.Core;

/// <summary>
/// Abstracts collecting the guest's print-layout pick during the (now first)
/// FramePicker state -- same interface-plus-mock seam as IFrameSelectionService,
/// which this replaces for guest-facing "which layout am I getting" choices.
/// A layout pick is just a button tap with no hardware/network dependency, so
/// UiTemplateSelectionService is a real, WPF-backed implementation, not just a mock.
/// </summary>
public interface ITemplateSelectionService
{
    /// <summary>Returns the guest's chosen template, or null if they picked "use the
    /// default layout". `options` is always non-empty -- BoothStateMachine only calls
    /// this when at least one favorited PrintTemplate library entry exists.</summary>
    Task<PrintTemplate?> SelectTemplateAsync(IReadOnlyList<PrintTemplate> options, CancellationToken ct = default);
}
