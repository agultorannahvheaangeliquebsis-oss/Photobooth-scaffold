namespace Photobooth.Core;

/// <summary>One filter choice offered on the guest-facing FilterPicker screen --
/// PreviewImagePath is a fully-rendered candidate (the actual captured photo
/// already run through Preset via IFilterPresetService), not a generic stock
/// thumbnail, so the guest sees exactly what picking it will produce.</summary>
public record FilterOption(PhotoFilterPreset Preset, string DisplayName, string PreviewImagePath);

/// <summary>
/// Abstracts collecting the guest's filter pick during the FilterPicker state.
/// Same interface-plus-mock seam as IFrameSelectionService, for the same reason.
/// </summary>
public interface IFilterSelectionService
{
    /// <summary>Waits for the guest to tap one of the offered previews (or "Original"/skip),
    /// returning the chosen option or null if they skipped.</summary>
    Task<FilterOption?> SelectFilterAsync(IReadOnlyList<FilterOption> options, CancellationToken ct = default);
}
