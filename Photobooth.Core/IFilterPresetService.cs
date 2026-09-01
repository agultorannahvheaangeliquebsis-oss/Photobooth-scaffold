namespace Photobooth.Core;

/// <summary>
/// Applies one of the built-in PhotoFilterPreset color grades to a photo.
/// Same interface-plus-mock seam as IPhotoFilterService/IFrameOverlayService.
/// </summary>
public interface IFilterPresetService
{
    /// <summary>Composites the preset onto the photo and returns the path to the
    /// filtered file (the original is left untouched). PhotoFilterPreset.Original
    /// is a no-op that returns photoPath unchanged -- nothing to composite for
    /// "no filter".</summary>
    Task<string> ApplyPresetAsync(string photoPath, PhotoFilterPreset preset, CancellationToken ct = default);
}
