using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IGreenScreenService: per-pixel chroma-key composite of a photo (or a
/// single live-view frame) over an admin-configured background, via GDI+
/// LockBits (a ColorMatrix pass, as GdiPhotoFilterService uses, can only
/// re-weight existing channels -- it can't substitute a different image's
/// pixels in for the keyed-out ones). Windows-only, same rationale as
/// GdiPhotoFilterService/GdiFrameOverlayService.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiGreenScreenService : IGreenScreenService
{
    // "Green dominance" = G - max(R, B): how far a pixel's green channel
    // outweighs both red and blue. Below LowThreshold the pixel is kept as
    // the original photo (not green); above HighThreshold it's fully
    // replaced by the background; the band between the two is alpha-blended
    // rather than a hard cutoff, which both softens jagged matte edges and
    // suppresses green spill on the subject's hair/skin at the silhouette.
    private const int LowThreshold = 30;
    private const int HighThreshold = 90;

    public Task<string> ApplyGreenScreenAsync(string photoPath, string backgroundImagePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using Bitmap originalSource = GdiImageHelpers.LoadIndependentCopy(photoPath);
            using Bitmap backgroundSource = GdiImageHelpers.LoadIndependentCopy(backgroundImagePath);

            using Bitmap original = StretchTo(originalSource, originalSource.Width, originalSource.Height);
            // Stretched to the photo's own dimensions, same "line up
            // regardless of the asset's native resolution" reasoning
            // GdiFrameOverlayService already uses for frame overlays.
            using Bitmap background = StretchTo(backgroundSource, original.Width, original.Height);

            using Bitmap composited = Composite(original, background);
            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, "_greenscreen");
            composited.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }

    public Task<byte[]> ApplyToLiveFrameAsync(byte[] frameBytes, string backgroundImagePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using Bitmap frameSource = GdiImageHelpers.LoadIndependentCopyFromBytes(frameBytes);
            using Bitmap backgroundSource = GdiImageHelpers.LoadIndependentCopy(backgroundImagePath);

            using Bitmap frame = StretchTo(frameSource, frameSource.Width, frameSource.Height);
            using Bitmap background = StretchTo(backgroundSource, frame.Width, frame.Height);

            using Bitmap composited = Composite(frame, background);
            using var buffer = new MemoryStream();
            composited.Save(buffer, ImageFormat.Jpeg);
            return buffer.ToArray();
        }, ct);
    }

    /// <summary>Draws source into a new independent Format32bppArgb bitmap at the
    /// given size (stretching if it differs from source's own), so the pixel loop
    /// in Composite below can always assume both inputs share one pixel format
    /// and one set of dimensions.</summary>
    private static Bitmap StretchTo(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics canvas = Graphics.FromImage(result);
        canvas.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }

    /// <summary>The actual chroma-key blend, shared by the file-based and
    /// live-frame paths above. original and background must already be the same
    /// size and Format32bppArgb (see StretchTo).</summary>
    private static Bitmap Composite(Bitmap original, Bitmap background)
    {
        int width = original.Width;
        int height = original.Height;
        Rectangle bounds = new(0, 0, width, height);

        var composited = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData originalData = original.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData backgroundData = background.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData outData = composited.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Format32bppArgb always packs 4 bytes/pixel with no row padding,
            // so stride == width * 4 here.
            int byteCount = originalData.Stride * height;
            byte[] originalBuffer = new byte[byteCount];
            byte[] backgroundBuffer = new byte[byteCount];
            byte[] outBuffer = new byte[byteCount];
            Marshal.Copy(originalData.Scan0, originalBuffer, 0, byteCount);
            Marshal.Copy(backgroundData.Scan0, backgroundBuffer, 0, byteCount);

            for (int i = 0; i < byteCount; i += 4)
            {
                // GDI+ stores Format32bppArgb pixels as B, G, R, A.
                byte blue = originalBuffer[i];
                byte green = originalBuffer[i + 1];
                byte red = originalBuffer[i + 2];

                int dominance = green - Math.Max(red, blue);
                double alpha = dominance <= LowThreshold ? 0.0
                    : dominance >= HighThreshold ? 1.0
                    : (dominance - LowThreshold) / (double)(HighThreshold - LowThreshold);

                outBuffer[i] = (byte)(blue * (1 - alpha) + backgroundBuffer[i] * alpha);
                outBuffer[i + 1] = (byte)(green * (1 - alpha) + backgroundBuffer[i + 1] * alpha);
                outBuffer[i + 2] = (byte)(red * (1 - alpha) + backgroundBuffer[i + 2] * alpha);
                outBuffer[i + 3] = 255;
            }

            Marshal.Copy(outBuffer, 0, outData.Scan0, byteCount);
        }
        finally
        {
            original.UnlockBits(originalData);
            background.UnlockBits(backgroundData);
            composited.UnlockBits(outData);
        }

        return composited;
    }
}
