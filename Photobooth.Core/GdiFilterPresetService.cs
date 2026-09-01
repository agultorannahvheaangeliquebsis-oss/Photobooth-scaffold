using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IFilterPresetService: each PhotoFilterPreset is a short recipe of
/// GDI+ ColorMatrix passes (tint/saturation/contrast), applied in sequence via
/// ApplyMatrix -- the same "one ColorMatrix pass per Graphics.DrawImage call"
/// approach GdiPhotoFilterService.ApplyGlamFilterAsync already established,
/// generalized into small composable building blocks instead of one bespoke
/// two-pass method. These are original approximations of the Instagram-style
/// looks dslrBooth's own filter names reference (1977/Brannan/Gotham/Hefe/
/// Lord Kelvin/Nashville), not licensed clones of any proprietary curve --
/// same "approximation, not a licensed algorithm" status the existing Glam
/// filter already has.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiFilterPresetService : IFilterPresetService
{
    public Task<string> ApplyPresetAsync(string photoPath, PhotoFilterPreset preset, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (preset == PhotoFilterPreset.Original)
        {
            return Task.FromResult(photoPath);
        }

        return Task.Run(() =>
        {
            using Bitmap original = GdiImageHelpers.LoadIndependentCopy(photoPath);

            Bitmap current = original;
            bool ownsCurrent = false;
            foreach (ColorMatrix pass in Recipe(preset))
            {
                Bitmap next = ApplyMatrix(current, pass);
                if (ownsCurrent)
                {
                    current.Dispose();
                }
                current = next;
                ownsCurrent = true;
            }

            string outputPath = GdiImageHelpers.DerivedJpegPath(photoPath, $"_{preset}");
            current.Save(outputPath, ImageFormat.Jpeg);
            if (ownsCurrent)
            {
                current.Dispose();
            }
            return outputPath;
        }, ct);
    }

    /// <summary>Runs one ColorMatrix pass over source, returning a new Bitmap --
    /// the caller owns disposing both the input it passed in and the result.</summary>
    private static Bitmap ApplyMatrix(Bitmap source, ColorMatrix matrix)
    {
        var result = new Bitmap(source.Width, source.Height);
        using (Graphics g = Graphics.FromImage(result))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(matrix);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        return result;
    }

    /// <summary>Each preset's ordered list of passes -- read top to bottom as
    /// "first do this, then this". Kept as small named recipes rather than one
    /// hand-combined 5x5 matrix per preset, same "fiddlier to get right than
    /// two straightforward passes are to verify" reasoning the original Glam
    /// filter's own two-pass split already gives.</summary>
    private static IEnumerable<ColorMatrix> Recipe(PhotoFilterPreset preset) => preset switch
    {
        // Exactly GdiPhotoFilterService.ApplyGlamFilterAsync's own recipe --
        // same effect, offered here as one tile among the Filters grid instead
        // of only reachable through the separate General-section Glam toggle.
        PhotoFilterPreset.BlackAndWhiteGlam => [Saturation(0f), Contrast(1.6f)],

        PhotoFilterPreset.BlackAndWhite => [Saturation(0f)],

        // Warm/faded look with lifted blacks and slightly reduced contrast.
        PhotoFilterPreset.Filter1977 => [Tint(1.10f, 1.00f, 0.90f, 0.05f, 0.02f, 0.00f), Contrast(0.90f)],

        // Cool metallic tint, punchier contrast.
        PhotoFilterPreset.Brannan => [Tint(0.90f, 1.00f, 1.05f, 0.00f, 0.00f, 0.05f), Contrast(1.25f)],

        // Moody, desaturated, cool blue-black shadows.
        PhotoFilterPreset.Gotham => [Saturation(0.4f), Tint(0.85f, 0.90f, 1.05f, 0.00f, 0.00f, 0.03f), Contrast(1.15f)],

        // Punchy warm saturation boost.
        PhotoFilterPreset.Hefe => [Saturation(1.35f), Tint(1.05f, 1.00f, 0.95f, 0.02f, 0.00f, 0.00f), Contrast(1.10f)],

        // Strong warm/orange cast.
        PhotoFilterPreset.LordKelvin => [Tint(1.25f, 1.05f, 0.75f, 0.08f, 0.03f, -0.05f)],

        // Warm pink/cream wash, softened contrast.
        PhotoFilterPreset.Nashville => [Tint(1.10f, 0.95f, 1.05f, 0.08f, 0.03f, 0.08f), Contrast(0.85f)],

        _ => [],
    };

    /// <summary>Standard luminance-preserving saturation matrix -- s=0 is full
    /// grayscale, s=1 is unchanged, s&gt;1 boosts saturation.</summary>
    private static ColorMatrix Saturation(float s) => new(new float[][]
    {
        new float[] { 0.213f + (0.787f * s), 0.213f - (0.213f * s), 0.213f - (0.213f * s), 0, 0 },
        new float[] { 0.715f - (0.715f * s), 0.715f + (0.285f * s), 0.715f - (0.715f * s), 0, 0 },
        new float[] { 0.072f - (0.072f * s), 0.072f - (0.072f * s), 0.072f + (0.928f * s), 0, 0 },
        new float[] { 0, 0, 0, 1, 0 },
        new float[] { 0, 0, 0, 0, 1 },
    });

    /// <summary>Same contrast-boost matrix GdiPhotoFilterService's second pass
    /// uses -- scales each channel around mid-gray by c, c&lt;1 flattens contrast.</summary>
    private static ColorMatrix Contrast(float c)
    {
        float translate = (1f - c) / 2f;
        return new ColorMatrix(new float[][]
        {
            new float[] { c, 0, 0, 0, 0 },
            new float[] { 0, c, 0, 0, 0 },
            new float[] { 0, 0, c, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { translate, translate, translate, 0, 1 },
        });
    }

    /// <summary>Per-channel scale plus offset -- the workhorse for a color cast
    /// (warm/cool tint).</summary>
    private static ColorMatrix Tint(float rMul, float gMul, float bMul, float rOffset, float gOffset, float bOffset) => new(new float[][]
    {
        new float[] { rMul, 0, 0, 0, 0 },
        new float[] { 0, gMul, 0, 0, 0 },
        new float[] { 0, 0, bMul, 0, 0 },
        new float[] { 0, 0, 0, 1, 0 },
        new float[] { rOffset, gOffset, bOffset, 0, 1 },
    });
}
