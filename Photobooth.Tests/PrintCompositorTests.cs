using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same reasoning as GdiFrameOverlayServiceTests: the one test class allowed
// to touch System.Drawing.Common directly, marked windows-only since the
// whole solution only ever runs on the Windows booth machine.
[SupportedOSPlatform("windows")]
public class PrintCompositorTests
{
    private static string WriteTestPhotoJpg(int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
        }
        string path = Path.Combine(Path.GetTempPath(), $"photo_test_{Guid.NewGuid():N}.jpg");
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static string WriteTestLogoPng(Color color)
    {
        using var bitmap = new Bitmap(50, 50, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(color);
        }
        string path = Path.Combine(Path.GetTempPath(), $"logo_test_{Guid.NewGuid():N}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void DrawTemplate_TextElement_DrawsColoredTextWithinItsBoundsAndLeavesRestUntouched()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.Text, 0.1, 0.85, 0.8, 0.1, Text: "HELLO", ColorHex: "#FF0000", FontSizePercent: 0.05),
            },
        };

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g);
        }

        // Somewhere within the text element's bounds should now show red
        // pixels (the drawn text), not the original white background.
        bool foundRedPixel = false;
        int startX = (int)(0.1 * 400), endX = (int)(0.9 * 400);
        int startY = (int)(0.85 * 600), endY = (int)(0.95 * 600);
        for (int x = startX; x < endX && !foundRedPixel; x += 2)
        {
            for (int y = startY; y < endY && !foundRedPixel; y += 2)
            {
                Color pixel = canvas.GetPixel(x, y);
                if (pixel.R > 150 && pixel.G < 100 && pixel.B < 100)
                {
                    foundRedPixel = true;
                }
            }
        }
        Assert.True(foundRedPixel);

        // A corner well outside the text element's bounds still shows the
        // photo's original white, untouched by the overlay.
        Color untouched = canvas.GetPixel(5, 5);
        Assert.True(untouched.R > 200 && untouched.G > 200 && untouched.B > 200);
    }

    [Fact]
    public void DrawTemplate_LogoElement_DrawsTheLogoWithinItsBounds()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        string logoPath = WriteTestLogoPng(Color.Blue);
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.Logo, 0.7, 0.05, 0.25, 0.1, ImagePath: logoPath),
            },
        };

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g);
        }

        int centerX = (int)((0.7 + 0.25 / 2) * 400);
        int centerY = (int)((0.05 + 0.1 / 2) * 600);
        Color logoPixel = canvas.GetPixel(centerX, centerY);
        Assert.True(logoPixel.B > 150 && logoPixel.R < 100);
    }

    [Fact]
    public void ComputePreviewDimensions_PortraitTemplate_HeightIsTheLongerSide()
    {
        (int width, int height) = PrintCompositor.ComputePreviewDimensions(PrintTemplate.Default, 500);
        Assert.Equal(500, height);
        Assert.True(width < height);
    }

    [Fact]
    public void ComputePreviewDimensions_LandscapeTemplate_WidthIsTheLongerSide()
    {
        var template = new PrintTemplate("Single", 6, 4, 1);
        (int width, int height) = PrintCompositor.ComputePreviewDimensions(template, 500);
        Assert.Equal(500, width);
        Assert.True(height < width);
    }

    [Fact]
    public void RenderPreview_ReturnsABitmapMatchingComputedDimensions()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Bitmap preview = PrintCompositor.RenderPreview(new[] { photoPath }, PrintTemplate.Default, 500);

        (int expectedWidth, int expectedHeight) = PrintCompositor.ComputePreviewDimensions(PrintTemplate.Default, 500);
        Assert.Equal(expectedWidth, preview.Width);
        Assert.Equal(expectedHeight, preview.Height);
    }

    [Fact]
    public void DrawTemplate_ShapeElement_FillsItsBoundsWithTheGivenColor()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.Shape, 0.25, 0.25, 0.5, 0.5, ColorHex: "#FF0000", ShapeType: "Rectangle"),
            },
        };

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g);
        }

        Color centerPixel = canvas.GetPixel(200, 300);
        Assert.True(centerPixel.R > 150 && centerPixel.G < 100 && centerPixel.B < 100);
    }

    [Fact]
    public void DrawTemplate_QrCodeElementWithNoPhotoUrl_DrawsNothing()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[] { new PrintTemplateElement(PrintTemplateElementKind.QrCode, 0.25, 0.25, 0.5, 0.5) },
        };

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g, context: null);
        }

        Color centerPixel = canvas.GetPixel(200, 300);
        Assert.True(centerPixel.R > 200 && centerPixel.G > 200 && centerPixel.B > 200);
    }

    [Fact]
    public void DrawTemplate_QrCodeElementWithPhotoUrl_DrawsAQrCode()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[] { new PrintTemplateElement(PrintTemplateElementKind.QrCode, 0.25, 0.25, 0.5, 0.5) },
        };
        var context = new PrintRenderContext(new Uri("https://example.com/photo123"));

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g, context);
        }

        // A real QR code is a dense pattern of black/white modules -- somewhere
        // within its bounds should now show a dark (non-white) pixel.
        bool foundDarkPixel = false;
        int startX = (int)(0.25 * 400), endX = (int)(0.75 * 400);
        int startY = (int)(0.25 * 600), endY = (int)(0.75 * 600);
        for (int x = startX; x < endX && !foundDarkPixel; x++)
        {
            for (int y = startY; y < endY && !foundDarkPixel; y++)
            {
                Color pixel = canvas.GetPixel(x, y);
                if (pixel.R < 100 && pixel.G < 100 && pixel.B < 100)
                {
                    foundDarkPixel = true;
                }
            }
        }
        Assert.True(foundDarkPixel);
    }

    [Fact]
    public void DrawTemplate_SessionDataElement_DrawsTheResolvedFieldValue()
    {
        string photoPath = WriteTestPhotoJpg(400, 600, Color.White);
        using Image photo = Image.FromFile(photoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.SessionData, 0.1, 0.85, 0.8, 0.1, Text: "EventName", ColorHex: "#FF0000"),
            },
        };
        var context = new PrintRenderContext(EventName: "Sample Event");

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { photo }, template, new Rectangle(0, 0, 400, 600), g, context);
        }

        bool foundRedPixel = false;
        int startX = (int)(0.1 * 400), endX = (int)(0.9 * 400);
        int startY = (int)(0.85 * 600), endY = (int)(0.95 * 600);
        for (int x = startX; x < endX && !foundRedPixel; x += 2)
        {
            for (int y = startY; y < endY && !foundRedPixel; y += 2)
            {
                Color pixel = canvas.GetPixel(x, y);
                if (pixel.R > 150 && pixel.G < 100 && pixel.B < 100)
                {
                    foundRedPixel = true;
                }
            }
        }
        Assert.True(foundRedPixel);
    }

    [Fact]
    public void DrawTemplate_SlotMode_DrawsEachPhotoSlotsOwnDistinctPhoto()
    {
        // The actual "true multi-pose" proof: two differently-colored photos,
        // each assigned to its own PhotoSlot, land in two different regions of
        // the page rather than the same one photo being repeated everywhere.
        string redPhotoPath = WriteTestPhotoJpg(200, 200, Color.Red);
        string bluePhotoPath = WriteTestPhotoJpg(200, 200, Color.Blue);
        using Image redPhoto = Image.FromFile(redPhotoPath);
        using Image bluePhoto = Image.FromFile(bluePhotoPath);
        var template = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0, 0, 0.5, 1, PhotoIndex: 0),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0.5, 0, 0.5, 1, PhotoIndex: 1),
            },
        };

        using var canvas = new Bitmap(400, 600);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);
            PrintCompositor.DrawTemplate(new[] { redPhoto, bluePhoto }, template, new Rectangle(0, 0, 400, 600), g);
        }

        Color leftHalf = canvas.GetPixel(100, 300);
        Color rightHalf = canvas.GetPixel(300, 300);
        Assert.True(leftHalf.R > 150 && leftHalf.G < 100 && leftHalf.B < 100);
        Assert.True(rightHalf.B > 150 && rightHalf.R < 100);
    }
}
