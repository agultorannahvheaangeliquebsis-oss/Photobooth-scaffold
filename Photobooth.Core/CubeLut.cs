using System.Globalization;
using System.Numerics;

namespace Photobooth.Core;

/// <summary>
/// A parsed .CUBE 3D LUT (the Adobe/Iridas format most color-grading tools
/// export) -- a Size x Size x Size grid of RGB output triples, sampled with
/// trilinear interpolation for any input color that falls between grid
/// points. Unlike PhotoFilterPreset's ColorMatrix recipes (a single affine
/// transform), a LUT is an arbitrary lookup table -- it can express color
/// grades a matrix can't (independent curves per region of the color cube),
/// which is the whole reason custom LUT uploads are a separate code path
/// from GdiFilterPresetService rather than one more Recipe() case.
/// </summary>
public sealed class CubeLut
{
    private readonly float[] _domainMin;
    private readonly float[] _domainMax;
    private readonly float[] _table;

    public int Size { get; }

    private CubeLut(int size, float[] domainMin, float[] domainMax, float[] table)
    {
        Size = size;
        _domainMin = domainMin;
        _domainMax = domainMax;
        _table = table;
    }

    /// <summary>Parses a .cube file. Throws <see cref="FormatException"/> with a
    /// guest/admin-readable message on anything that isn't a standard 3D LUT --
    /// the admin "Add Custom Filter" dialog shows that message verbatim as the
    /// validation error, so keep them describing the actual problem.</summary>
    public static CubeLut Parse(string path)
    {
        int? size = null;
        float[] domainMin = [0f, 0f, 0f];
        float[] domainMax = [1f, 1f, 1f];
        var values = new List<float>();

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("This is a 1D LUT (LUT_1D_SIZE) -- only 3D LUT .cube files (LUT_3D_SIZE) are supported.");
            }

            if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSize) || parsedSize < 2)
                {
                    throw new FormatException("Invalid LUT_3D_SIZE value.");
                }
                size = parsedSize;
                continue;
            }

            if (line.StartsWith("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            {
                domainMin = ParseTriple(line);
                continue;
            }

            if (line.StartsWith("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                domainMax = ParseTriple(line);
                continue;
            }

            // Any other keyword line this parser doesn't know about (LUT_1D_INPUT_RANGE,
            // vendor-specific metadata, ...) is skipped rather than rejected, same
            // "ignore what you don't recognize" forward-compatibility PhotoFilterPresets.Parse
            // already uses -- only a genuine 3-number data row is captured below.
            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 3 &&
                float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
            {
                values.Add(r);
                values.Add(g);
                values.Add(b);
            }
        }

        if (size is not { } n)
        {
            throw new FormatException("Couldn't read this file -- expecting a standard .CUBE 3D LUT (LUT_3D_SIZE header).");
        }

        int expectedRows = n * n * n;
        if (values.Count != expectedRows * 3)
        {
            throw new FormatException($"Expected {expectedRows} data rows for a {n}x{n}x{n} LUT, found {values.Count / 3}.");
        }

        return new CubeLut(n, domainMin, domainMax, values.ToArray());
    }

    private static float[] ParseTriple(string line)
    {
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            throw new FormatException($"Malformed line: '{line}'.");
        }
        return
        [
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture),
            float.Parse(parts[3], CultureInfo.InvariantCulture),
        ];
    }

    /// <summary>Trilinearly interpolates the LUT at an 8-bit RGB input, returning
    /// the graded 8-bit RGB output. The workhorse GdiCubeLutFilterService calls
    /// once per pixel.</summary>
    public (byte R, byte G, byte B) SampleTrilinear(byte r, byte g, byte b)
    {
        float rf = ScaledCoordinate(r, 0);
        float gf = ScaledCoordinate(g, 1);
        float bf = ScaledCoordinate(b, 2);

        int r0 = (int)MathF.Floor(rf);
        int g0 = (int)MathF.Floor(gf);
        int b0 = (int)MathF.Floor(bf);
        int r1 = Math.Min(r0 + 1, Size - 1);
        int g1 = Math.Min(g0 + 1, Size - 1);
        int b1 = Math.Min(b0 + 1, Size - 1);

        float fr = rf - r0;
        float fg = gf - g0;
        float fb = bf - b0;

        Vector3 c00 = Vector3.Lerp(At(r0, g0, b0), At(r1, g0, b0), fr);
        Vector3 c10 = Vector3.Lerp(At(r0, g1, b0), At(r1, g1, b0), fr);
        Vector3 c01 = Vector3.Lerp(At(r0, g0, b1), At(r1, g0, b1), fr);
        Vector3 c11 = Vector3.Lerp(At(r0, g1, b1), At(r1, g1, b1), fr);

        Vector3 c0 = Vector3.Lerp(c00, c10, fg);
        Vector3 c1 = Vector3.Lerp(c01, c11, fg);

        Vector3 c = Vector3.Lerp(c0, c1, fb);

        return (ToByte(c.X), ToByte(c.Y), ToByte(c.Z));
    }

    /// <summary>Maps an 8-bit channel value into this LUT's [0, Size-1] grid
    /// coordinate space, respecting DOMAIN_MIN/DOMAIN_MAX (almost always 0..1,
    /// but the format allows otherwise).</summary>
    private float ScaledCoordinate(byte channelValue, int axis)
    {
        float normalized = channelValue / 255f;
        float min = _domainMin[axis];
        float max = _domainMax[axis];
        float t = max > min ? (normalized - min) / (max - min) : 0f;
        t = Math.Clamp(t, 0f, 1f);
        return t * (Size - 1);
    }

    /// <summary>Reads one grid cell. Cube data is laid out with red fastest-varying,
    /// then green, then blue -- so the file's row order already matches this index
    /// formula and Parse can append rows in the order it reads them.</summary>
    private Vector3 At(int r, int g, int b)
    {
        int index = ((b * Size) + g) * Size + r;
        int offset = index * 3;
        return new Vector3(_table[offset], _table[offset + 1], _table[offset + 2]);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
