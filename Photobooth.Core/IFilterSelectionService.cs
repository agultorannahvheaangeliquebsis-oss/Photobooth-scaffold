namespace Photobooth.Core;

/// <summary>One filter choice offered on the guest-facing FilterPicker screen --
/// PreviewImagePath is a fully-rendered candidate (the actual captured photo
/// already run through Preset/CustomFilterId via IFilterPresetService or
/// ICustomFilterService), not a generic stock thumbnail, so the guest sees
/// exactly what picking it will produce. Exactly one of Preset/CustomFilterId
/// is set -- a built-in PhotoFilterPreset tile leaves CustomFilterId null, an
/// admin-uploaded LUT tile leaves Preset null (Preset became nullable, and
/// CustomFilterId was added as a trailing optional parameter, specifically so
/// every existing positional `new FilterOption(preset, name, path)` call
/// keeps compiling unchanged).</summary>
public record FilterOption(PhotoFilterPreset? Preset, string DisplayName, string PreviewImagePath, int? CustomFilterId = null);

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
