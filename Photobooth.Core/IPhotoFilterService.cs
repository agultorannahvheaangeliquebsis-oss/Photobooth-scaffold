namespace Photobooth.Core;

/// <summary>
/// Applies the "Glam Booth" high-contrast black &amp; white look to a
/// captured photo, ahead of branding. Same interface-plus-mock seam as
/// IPhotoBrandingService, and for the same reason -- no hardware or
/// network to fake, just keeping System.Drawing.Common (Windows-only) out
/// of Photobooth.Tests/Photobooth.ConsoleDemo. Skin smoothing (the other
/// half of the "Glam Booth mode" roadmap item) needs face detection and is
/// separate, unbuilt work.
/// </summary>
public interface IPhotoFilterService
{
    /// <summary>Composites the glam filter onto the photo and returns the path to the filtered file (the original is left untouched).</summary>
    Task<string> ApplyGlamFilterAsync(string photoPath, CancellationToken ct = default);
}
