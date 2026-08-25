namespace Photobooth.Core;

/// <summary>
/// Writes a simple, self-describing placeholder photo to disk so the mock
/// camera hands the UI a real image file instead of a path to nothing.
///
/// This deliberately hand-rolls an uncompressed 24-bit BMP rather than pulling
/// in System.Drawing.Common: Photobooth.Core targets plain net8.0 and is shared
/// with the console demo, and System.Drawing.Common is a Windows-only NuGet
/// dependency. A few dozen lines of byte writing keeps this project dependency
/// free and the core library honestly cross-platform. BMP is what WPF's
/// BitmapImage (and every other decoder) reads without complaint.
/// </summary>
internal static class PlaceholderImage
{
    private const int Width = 1200;
    private const int Height = 800;
    private const int BytesPerPixel = 3;

    /// <summary>Background colours cycle per frame so consecutive mock captures
    /// are visibly different -- otherwise it's impossible to tell at a glance
    /// whether the Reviewing screen actually reloaded the new shot.</summary>
    private static readonly (byte R, byte G, byte B)[] Backgrounds =
    {
        (0x2B, 0x3A, 0x55),
        (0x4A, 0x2E, 0x3C),
        (0x27, 0x44, 0x3C),
        (0x50, 0x3D, 0x28),
        (0x38, 0x30, 0x50),
    };

    private static readonly (byte R, byte G, byte B) Ink = (0xF5, 0xF2, 0xEC);
    private static readonly (byte R, byte G, byte B) Accent = (0xD9, 0x7A, 0x5B);

    /// <summary>
    /// Renders the placeholder for <paramref name="frameNumber"/> and saves it
    /// to <paramref name="path"/>, creating the containing directory if needed.
    /// </summary>
    public static void Write(string path, int frameNumber, DateTime timestamp)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Top-down BGR buffer; the row order is flipped when it's written out.
        var pixels = new byte[Width * Height * BytesPerPixel];

        var background = Backgrounds[Math.Abs(frameNumber) % Backgrounds.Length];
        FillRect(pixels, 0, 0, Width, Height, background);

        // A thin inset frame plus accent rules, so the placeholder reads as a
        // deliberate stand-in rather than a broken or blank image.
        DrawRectOutline(pixels, 40, 40, Width - 80, Height - 80, 3, Ink);
        FillRect(pixels, 0, 0, Width, 12, Accent);
        FillRect(pixels, 0, Height - 12, Width, 12, Accent);

        DrawTextCentered(pixels, "MOCK CAPTURE", 210, 9, Ink);
        DrawTextCentered(pixels, $"FRAME {frameNumber:D4}", 380, 12, Accent);
        DrawTextCentered(pixels, timestamp.ToString("yyyy-MM-dd HH:mm:ss"), 540, 5, Ink);
        DrawTextCentered(pixels, "DEVELOPMENT PLACEHOLDER - NOT A REAL PHOTO", 640, 3, Ink);

        WriteBmp(path, pixels);
    }

    private static void FillRect(byte[] pixels, int x, int y, int w, int h, (byte R, byte G, byte B) color)
    {
        int xEnd = Math.Min(x + w, Width);
        int yEnd = Math.Min(y + h, Height);

        for (int py = Math.Max(y, 0); py < yEnd; py++)
        {
            for (int px = Math.Max(x, 0); px < xEnd; px++)
            {
                SetPixel(pixels, px, py, color);
            }
        }
    }

    private static void DrawRectOutline(byte[] pixels, int x, int y, int w, int h, int thickness, (byte R, byte G, byte B) color)
    {
        FillRect(pixels, x, y, w, thickness, color);
        FillRect(pixels, x, y + h - thickness, w, thickness, color);
        FillRect(pixels, x, y, thickness, h, color);
        FillRect(pixels, x + w - thickness, y, thickness, h, color);
    }

    private static void SetPixel(byte[] pixels, int x, int y, (byte R, byte G, byte B) color)
    {
        int offset = ((y * Width) + x) * BytesPerPixel;
        pixels[offset] = color.B;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.R;
    }

    private static void DrawTextCentered(byte[] pixels, string text, int y, int scale, (byte R, byte G, byte B) color)
    {
        // Glyphs are 5 columns wide with a 1 column gap; the trailing gap after
        // the final glyph doesn't count toward the measured width.
        int width = text.Length == 0 ? 0 : (text.Length * 6 * scale) - scale;
        DrawText(pixels, text, (Width - width) / 2, y, scale, color);
    }

    private static void DrawText(byte[] pixels, string text, int x, int y, int scale, (byte R, byte G, byte B) color)
    {
        int cursor = x;

        foreach (char character in text.ToUpperInvariant())
        {
            byte[] glyph = Font.TryGetValue(character, out byte[]? found) ? found : Blank;

            for (int col = 0; col < glyph.Length; col++)
            {
                for (int row = 0; row < 7; row++)
                {
                    if ((glyph[col] & (1 << row)) != 0)
                    {
                        FillRect(pixels, cursor + (col * scale), y + (row * scale), scale, scale, color);
                    }
                }
            }

            cursor += 6 * scale;
        }
    }

    private static void WriteBmp(string path, byte[] pixels)
    {
        int stride = Width * BytesPerPixel;
        int padding = (4 - (stride % 4)) % 4;
        int rowSize = stride + padding;
        int imageSize = rowSize * Height;
        const int headerSize = 54;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // BITMAPFILEHEADER
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(headerSize + imageSize);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(headerSize);

        // BITMAPINFOHEADER
        writer.Write(40);
        writer.Write(Width);
        writer.Write(Height);   // positive height means rows are stored bottom-up
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);        // BI_RGB, no compression
        writer.Write(imageSize);
        writer.Write(2835);     // ~72 DPI, expressed in pixels per metre
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        var pad = new byte[padding];
        for (int y = Height - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
            if (padding > 0)
            {
                writer.Write(pad);
            }
        }
    }

    private static readonly byte[] Blank = { 0x00, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>
    /// Classic 5x7 bitmap font, one byte per column, bit 0 = top row. Only the
    /// characters this placeholder actually prints are included; anything else
    /// renders as a blank space.
    /// </summary>
    private static readonly Dictionary<char, byte[]> Font = new()
    {
        [' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 },
        ['-'] = new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 },
        ['.'] = new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 },
        ['/'] = new byte[] { 0x20, 0x10, 0x08, 0x04, 0x02 },
        [':'] = new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 },
        ['#'] = new byte[] { 0x14, 0x7F, 0x14, 0x7F, 0x14 },
        ['0'] = new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E },
        ['1'] = new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 },
        ['2'] = new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 },
        ['3'] = new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 },
        ['4'] = new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 },
        ['5'] = new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 },
        ['6'] = new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 },
        ['7'] = new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 },
        ['8'] = new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 },
        ['9'] = new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E },
        ['A'] = new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E },
        ['B'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 },
        ['C'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 },
        ['D'] = new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C },
        ['E'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 },
        ['F'] = new byte[] { 0x7F, 0x09, 0x09, 0x01, 0x01 },
        ['G'] = new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A },
        ['H'] = new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F },
        ['I'] = new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 },
        ['J'] = new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 },
        ['K'] = new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 },
        ['L'] = new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 },
        ['M'] = new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F },
        ['N'] = new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F },
        ['O'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E },
        ['P'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 },
        ['Q'] = new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E },
        ['R'] = new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 },
        ['S'] = new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 },
        ['T'] = new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 },
        ['U'] = new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F },
        ['V'] = new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F },
        ['W'] = new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F },
        ['X'] = new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 },
        ['Y'] = new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 },
        ['Z'] = new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 },
    };
}
