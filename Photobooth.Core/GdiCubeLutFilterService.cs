using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real ICustomFilterService: samples a parsed CubeLut per pixel via
/// Bitmap.LockBits -- unlike GdiFilterPresetService's ColorMatrix passes
/// (a single affine transform GDI+ applies for you), a 3D LUT has no GDI+
/// primitive, so this walks the raw pixel buffer itself. Windows-only, same
/// rationale as GdiFilterPresetService.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiCubeLutFilterService : ICustomFilterService
{
    /// <summary>Parsing a .cube file (tens of thousands of data rows) is real work
    /// worth not repeating -- the same LUT gets applied once per enabled custom
    /// filter per guest session (FilterPicker renders every candidate up front),
    /// so a booth with one popular filter would otherwise reparse its file on
    /// every single capture. Keyed by path + last-write time so an admin
    /// re-uploading a different file under the same path (shouldn't normally
    /// happen given Guid-named storage, but cheap to guard) doesn't serve a
    /// stale parse.</summary>
    private static readonly ConcurrentDictionary<(string Path, DateTime LastWriteUtc), CubeLut> Cache = new();

    public Task<string> ApplyCustomFilterAsync(string photoPath, string cubeFilePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            CubeLut lut = GetOrParse(cubeFilePath);

            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);
            using var working = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(working))
            {
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height));
            }

            ApplyInPlace(working, lut);

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, $"_custom{Path.GetFileNameWithoutExtension(cubeFilePath)}");
            working.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }, ct);
    }

    private static CubeLut GetOrParse(string cubeFilePath)
    {
        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(cubeFilePath);
        return Cache.GetOrAdd((cubeFilePath, lastWriteUtc), key => CubeLut.Parse(key.Path));
    }

    /// <summary>Walks every pixel of a Format24bppRgb bitmap, replacing it with the
    /// LUT's trilinear-interpolated output. GDI+ stores 24bpp scanlines as
    /// B,G,R byte triples (not R,G,B) -- swapped on the way in and out below.</summary>
    private static void ApplyInPlace(Bitmap bitmap, CubeLut lut)
    {
        Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int stride = data.Stride;
            int byteCount = stride * bitmap.Height;
            byte[] buffer = new byte[byteCount];
            Marshal.Copy(data.Scan0, buffer, 0, byteCount);

            for (int y = 0; y < bitmap.Height; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int offset = rowStart + (x * 3);
                    byte b = buffer[offset];
                    byte g = buffer[offset + 1];
                    byte r = buffer[offset + 2];

                    (byte nr, byte ng, byte nb) = lut.SampleTrilinear(r, g, b);

                    buffer[offset] = nb;
                    buffer[offset + 1] = ng;
                    buffer[offset + 2] = nr;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
