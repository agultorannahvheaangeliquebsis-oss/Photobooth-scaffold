using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IFrameOverlayService: draws the frame's PNG (expected to carry a
/// transparent cutout over the photo area) on top of the captured photo via
/// GDI+, stretched to the photo's exact dimensions so it lines up regardless
/// of the frame asset's native resolution. Windows-only, same rationale as
/// GdiPhotoBrandingService/GdiPhotoFilterService.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiFrameOverlayService : IFrameOverlayService
{
    public Task<string> ApplyFrameAsync(string photoPath, string frameImagePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);
            using Bitmap frame = GdiImageHelpers.LoadIndependentCopy(frameImagePath);

            using var framed = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(framed))
            {
                g.DrawImage(original, 0, 0, original.Width, original.Height);
                // PNG alpha in the frame asset composites naturally here --
                // GDI+ respects it when drawing, even though the final JPEG
                // save below flattens to fully opaque.
                g.DrawImage(frame, 0, 0, original.Width, original.Height);
            }

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, "_framed");
            framed.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }
}
