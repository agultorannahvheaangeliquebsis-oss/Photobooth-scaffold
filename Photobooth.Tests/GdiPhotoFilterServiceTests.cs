using System.Drawing;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same reasoning as GdiPhotoBrandingServiceTests: the one test class allowed
// to touch System.Drawing.Common directly, marked windows-only since the
// whole solution only ever runs on the Windows booth machine.
[SupportedOSPlatform("windows")]
public class GdiPhotoFilterServiceTests
{
    [Fact]
    public async Task ApplyGlamFilterAsync_ReturnsPathToARealJpegAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var filter = new GdiPhotoFilterService();

        string filteredPath = await filter.ApplyGlamFilterAsync(originalPath);

        Assert.NotEqual(originalPath, filteredPath);
        Assert.True(File.Exists(filteredPath));
        Assert.True(File.Exists(originalPath));

        byte[] header = new byte[2];
        using (var stream = File.OpenRead(filteredPath))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // JPEG files start with the 0xFF 0xD8 magic bytes.
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
    }

    [Fact]
    public async Task ApplyGlamFilterAsync_ProducesAGrayscaleImageSameSizeAsOriginal()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var filter = new GdiPhotoFilterService();

        string filteredPath = await filter.ApplyGlamFilterAsync(originalPath);

        int originalWidth, originalHeight;
        using (var originalImage = new Bitmap(originalPath))
        {
            originalWidth = originalImage.Width;
            originalHeight = originalImage.Height;
        }

        using var filteredImage = new Bitmap(filteredPath);
        Assert.Equal(originalWidth, filteredImage.Width);
        Assert.Equal(originalHeight, filteredImage.Height);

        // Sample a handful of pixels across the image and confirm each is
        // genuinely grayscale (R == G == B) -- proves the color matrix
        // actually desaturated the photo, not just that a file got written.
        var samplePoints = new (int X, int Y)[]
        {
            (10, 10),
            (filteredImage.Width / 2, filteredImage.Height / 2),
            (filteredImage.Width - 10, filteredImage.Height - 10),
        };
        foreach (var (x, y) in samplePoints)
        {
            Color pixel = filteredImage.GetPixel(x, y);
            Assert.Equal(pixel.R, pixel.G);
            Assert.Equal(pixel.G, pixel.B);
        }
    }
}
