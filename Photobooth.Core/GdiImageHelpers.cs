using System.Drawing;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>Shared helpers for the GDI+-backed photo services (GdiPhotoBrandingService, GdiPhotoFilterService).</summary>
[SupportedOSPlatform("windows")]
internal static class GdiImageHelpers
{
    /// <summary>Loads the photo into a Bitmap that owns its own memory, independent
    /// of the source file -- GDI+'s stream-backed Bitmap constructor requires the
    /// stream to stay open for the image's lifetime, and callers don't want to
    /// think about that.</summary>
    public static Bitmap LoadIndependentCopy(string path) => LoadIndependentCopyFromBytes(File.ReadAllBytes(path));

    /// <summary>Same independent-memory guarantee as <see cref="LoadIndependentCopy"/>,
    /// for callers that already have the image bytes in memory (e.g. a live-view
    /// frame straight off the camera bridge pipe) and don't want a round trip
    /// through disk just to decode them.</summary>
    public static Bitmap LoadIndependentCopyFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded);
    }

    /// <summary>Builds the output path for a derived photo (e.g. "foo.jpg" -> "foo_glam.jpg"), always as .jpg regardless of the source extension.</summary>
    public static string DerivedJpegPath(string sourcePath, string suffix)
    {
        string directory = Path.GetDirectoryName(sourcePath) is { Length: > 0 } dir ? dir : ".";
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(sourcePath)}{suffix}.jpg");
    }
}
