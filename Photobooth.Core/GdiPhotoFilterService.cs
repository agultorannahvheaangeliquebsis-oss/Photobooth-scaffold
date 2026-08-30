using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IPhotoFilterService: converts the photo to grayscale then boosts
/// contrast, via two GDI+ ColorMatrix passes -- the "high-contrast B&amp;W
/// filter" half of the Glam Booth roadmap item. Windows-only, same
/// rationale as SpoolerPrinterService/GdiPhotoBrandingService.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiPhotoFilterService : IPhotoFilterService
{
    // Values pushed away from mid-gray, not just desaturated -- a plain
    // grayscale conversion alone reads as "washed out", not "glam".
    private const float ContrastBoost = 1.6f;

    public Task<string> ApplyGlamFilterAsync(string photoPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);

            using Bitmap grayscale = new(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(grayscale))
            using (var attributes = new ImageAttributes())
            {
                // Standard luminance-weighted grayscale matrix.
                attributes.SetColorMatrix(new ColorMatrix(new float[][]
                {
                    new float[] { 0.30f, 0.30f, 0.30f, 0, 0 },
                    new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                    new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 },
                }));
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }

            // Second pass for contrast -- combining both into a single
            // matrix multiplication is fiddlier to get right than two
            // straightforward passes are to verify.
            using var filtered = new Bitmap(grayscale.Width, grayscale.Height);
            using (Graphics g = Graphics.FromImage(filtered))
            using (var attributes = new ImageAttributes())
            {
                float translate = (1f - ContrastBoost) / 2f;
                attributes.SetColorMatrix(new ColorMatrix(new float[][]
                {
                    new float[] { ContrastBoost, 0, 0, 0, 0 },
                    new float[] { 0, ContrastBoost, 0, 0, 0 },
                    new float[] { 0, 0, ContrastBoost, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { translate, translate, translate, 0, 1 },
                }));
                g.DrawImage(grayscale, new Rectangle(0, 0, grayscale.Width, grayscale.Height),
                    0, 0, grayscale.Width, grayscale.Height, GraphicsUnit.Pixel, attributes);
            }

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, "_glam");
            filtered.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }
}
