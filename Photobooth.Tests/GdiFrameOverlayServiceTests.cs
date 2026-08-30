using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same reasoning as GdiPhotoBrandingServiceTests/GdiPhotoFilterServiceTests:
// the one test class allowed to touch System.Drawing.Common directly, marked
// windows-only since the whole solution only ever runs on the Windows booth
// machine.
[SupportedOSPlatform("windows")]
public class GdiFrameOverlayServiceTests
{
    /// <summary>Writes a small frame PNG with an opaque red border and a fully
    /// transparent center, so a test can tell whether compositing actually
    /// respected the frame's alpha channel rather than just overwriting the
    /// whole photo.</summary>
    private static string WriteTestFramePng()
    {
        using var frame = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.Red, 0, 0, 100, 10);
            g.FillRectangle(Brushes.Red, 0, 90, 100, 10);
        }

        string path = Path.Combine(Path.GetTempPath(), $"frame_test_{Guid.NewGuid():N}.png");
        frame.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task ApplyFrameAsync_ReturnsPathToARealFramedJpegAndLeavesOriginalUntouched()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        string framePath = WriteTestFramePng();
        var overlay = new GdiFrameOverlayService();

        string framedPath = await overlay.ApplyFrameAsync(originalPath, framePath);

        Assert.NotEqual(originalPath, framedPath);
        Assert.True(File.Exists(framedPath));
        Assert.True(File.Exists(originalPath));

        byte[] header = new byte[2];
        using (var stream = File.OpenRead(framedPath))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // JPEG files start with the 0xFF 0xD8 magic bytes.
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
    }

    [Fact]
    public async Task ApplyFrameAsync_StretchesFrameToPhotoSizeAndRespectsTransparency()
    {
        var camera = new MockCameraService();
        string originalPath = await camera.CaptureAsync();
        string framePath = WriteTestFramePng();
        var overlay = new GdiFrameOverlayService();

        string framedPath = await overlay.ApplyFrameAsync(originalPath, framePath);

        using Bitmap original = new(originalPath);
        using Bitmap framed = new(framedPath);

        // Stretched to the photo's own dimensions, not the frame asset's
        // native 100x100 -- so a guest's picked frame lines up regardless of
        // what resolution the admin uploaded it at.
        Assert.Equal(original.Width, framed.Width);
        Assert.Equal(original.Height, framed.Height);

        // Top strip is the frame's opaque red border -- should now read as
        // red (JPEG re-encoding can shift channels a little, so allow slack)
        // rather than the original photo's background color.
        Color topPixel = framed.GetPixel(framed.Width / 2, 2);
        Assert.True(topPixel.R > 150 && topPixel.G < 100 && topPixel.B < 100);

        // Center is the frame's fully transparent cutout -- should still
        // show the original photo's own color there, not red.
        Color centerPixel = framed.GetPixel(framed.Width / 2, framed.Height / 2);
        Color originalCenterPixel = original.GetPixel(original.Width / 2, original.Height / 2);
        Assert.True(Math.Abs(centerPixel.R - originalCenterPixel.R) < 40);
        Assert.True(Math.Abs(centerPixel.G - originalCenterPixel.G) < 40);
        Assert.True(Math.Abs(centerPixel.B - originalCenterPixel.B) < 40);
    }
}
