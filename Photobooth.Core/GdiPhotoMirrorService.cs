using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IPhotoMirrorService: flips the photo left-right via GDI+'s
/// RotateFlip, same "load independent copy, transform, save to a derived
/// path" shape as GdiPhotoFilterService. Windows-only, same rationale as
/// SpoolerPrinterService/GdiPhotoBrandingService.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiPhotoMirrorService : IPhotoMirrorService
{
    public Task<string> FlipHorizontallyAsync(string photoPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);
            original.RotateFlip(RotateFlipType.RotateNoneFlipX);

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, "_mirrored");
            original.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }
}
