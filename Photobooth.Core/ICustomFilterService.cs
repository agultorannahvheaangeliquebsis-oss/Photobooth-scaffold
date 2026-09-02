namespace Photobooth.Core;

/// <summary>
/// Applies an admin-uploaded .CUBE LUT to a photo -- the custom-filter sibling
/// of IFilterPresetService, kept as its own interface (rather than widening
/// IFilterPresetService's enum-typed ApplyPresetAsync) since a LUT is
/// identified by file path, not a PhotoFilterPreset value.
/// </summary>
public interface ICustomFilterService
{
    /// <summary>Composites the LUT onto the photo and returns the path to the
    /// filtered file (the original is left untouched).</summary>
    Task<string> ApplyCustomFilterAsync(string photoPath, string cubeFilePath, CancellationToken ct = default);
}
