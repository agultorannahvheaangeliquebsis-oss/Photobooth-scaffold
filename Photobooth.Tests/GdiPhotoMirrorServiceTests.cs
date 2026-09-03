using System.Drawing;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same reasoning as GdiPhotoFilterServiceTests: the one test class allowed
// to touch System.Drawing.Common directly, marked windows-only since the
// whole solution only ever runs on the Windows booth machine.
[SupportedOSPlatform("windows")]
public class GdiPhotoMirrorServiceTests
{
    [Fact]
    public async Task FlipHorizontallyAsync_ReturnsPathToARealJpegAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var mirror = new GdiPhotoMirrorService();

        string flippedPath = await mirror.FlipHorizontallyAsync(originalPath);

        Assert.NotEqual(originalPath, flippedPath);
        Assert.True(File.Exists(flippedPath));
        Assert.True(File.Exists(originalPath));

        byte[] header = new byte[2];
        using (var stream = File.OpenRead(flippedPath))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // JPEG files start with the 0xFF 0xD8 magic bytes.
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
    }

    [Fact]
    public async Task FlipHorizontallyAsync_MirrorsPixelsLeftToRightSameSizeAsOriginal()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var mirror = new GdiPhotoMirrorService();

        string flippedPath = await mirror.FlipHorizontallyAsync(originalPath);

        using var original = new Bitmap(originalPath);
        using var flipped = new Bitmap(flippedPath);
        Assert.Equal(original.Width, flipped.Width);
        Assert.Equal(original.Height, flipped.Height);

        // Sample a handful of columns and confirm each one now shows up
        // mirrored to the opposite edge -- proves pixels actually moved, not
        // just that a same-size file got written. Compares with a tolerance
        // (not exact equality) since the flipped file round-trips through
        // JPEG, which is lossy.
        var sampleYs = new[] { 10, original.Height / 2, original.Height - 10 };
        foreach (int y in sampleYs)
        {
            for (int x = 0; x < original.Width; x += Math.Max(original.Width / 8, 1))
            {
                Color originalPixel = original.GetPixel(x, y);
                Color flippedPixel = flipped.GetPixel(original.Width - 1 - x, y);
                Assert.True(Math.Abs(originalPixel.R - flippedPixel.R) < 60);
                Assert.True(Math.Abs(originalPixel.G - flippedPixel.G) < 60);
                Assert.True(Math.Abs(originalPixel.B - flippedPixel.B) < 60);
            }
        }
    }
}
