using System.Drawing;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// This is the one test class in the suite allowed to touch System.Drawing.Common
// directly -- everything else in Photobooth.Tests stays platform-agnostic via
// MockPhotoBrandingService, same reasoning as SpoolerPrinterService staying
// untested here. Marked windows-only for the same reason the real service is:
// the whole solution only ever runs on the Windows booth machine.
[SupportedOSPlatform("windows")]
public class GdiPhotoBrandingServiceTests
{
    [Fact]
    public async Task ApplyBrandingAsync_ReturnsPathToARealBrandedJpegAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var branding = new GdiPhotoBrandingService();

        string brandedPath = await branding.ApplyBrandingAsync(originalPath, "Focus & Snap");

        Assert.NotEqual(originalPath, brandedPath);
        Assert.True(File.Exists(brandedPath));
        Assert.True(File.Exists(originalPath));

        byte[] header = new byte[2];
        using (var stream = File.OpenRead(brandedPath))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // JPEG files start with the 0xFF 0xD8 magic bytes.
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
    }

    [Fact]
    public async Task ApplyBrandingAsync_AddsACaptionBannerSoTheImageGetsTaller()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        var branding = new GdiPhotoBrandingService();

        string brandedPath = await branding.ApplyBrandingAsync(originalPath, "Focus & Snap");

        int originalHeight, originalWidth;
        using (var originalImage = Image.FromFile(originalPath))
        {
            originalHeight = originalImage.Height;
            originalWidth = originalImage.Width;
        }

        using var brandedImage = Image.FromFile(brandedPath);
        Assert.Equal(originalWidth, brandedImage.Width);
        Assert.True(brandedImage.Height > originalHeight);
    }
}
