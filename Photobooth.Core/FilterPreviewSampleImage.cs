namespace Photobooth.Core;

/// <summary>
/// Provides a stand-in sample photo for the admin Filter library screen's
/// thumbnails, in place of dslrBooth's own bundled/branded test image (nothing
/// to source that from here). Reuses PlaceholderImage's synthetic BMP renderer
/// (already colorful/high-contrast enough to show each preset's tint clearly)
/// rather than adding a new binary image asset -- cached to disk on first use
/// so thumbnail generation is a one-time cost, not regenerated on every open
/// of the Filter library window.
/// </summary>
public static class FilterPreviewSampleImage
{
    /// <summary>Returns the path to the cached sample image, writing it first
    /// if this is the first time it's been needed.</summary>
    public static string EnsurePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "FilterPreviewSample.bmp");
        if (!File.Exists(path))
        {
            PlaceholderImage.Write(path, frameNumber: 0, DateTime.Now);
        }
        return path;
    }
}
