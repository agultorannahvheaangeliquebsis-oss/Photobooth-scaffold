using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same reasoning as GdiFrameOverlayServiceTests: the one test class allowed
// to touch System.Drawing.Common directly, marked windows-only since the
// whole solution only ever runs on the Windows booth machine.
[SupportedOSPlatform("windows")]
public class GdiGreenScreenServiceTests
{
    /// <summary>Renders a photo split top/bottom: pure green on top (the
    /// backdrop a real chroma-key photo would have) and solid red on bottom
    /// (standing in for the subject), so a test can tell whether keying
    /// actually distinguished the two rather than transforming the whole
    /// image uniformly.</summary>
    private static Bitmap RenderGreenBackdropFrame(int width, int height)
    {
        var photo = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(photo);
        g.FillRectangle(Brushes.Lime, 0, 0, width, height / 2);
        g.FillRectangle(Brushes.Red, 0, height / 2, width, height - (height / 2));
        return photo;
    }

    /// <summary>Writes a 100x100 photo split top/bottom, same layout as
    /// RenderGreenBackdropFrame, but to disk -- for the file-based
    /// ApplyGreenScreenAsync path below.</summary>
    private static string WriteGreenBackdropPhoto()
    {
        using Bitmap photo = RenderGreenBackdropFrame(100, 100);
        string path = Path.Combine(Path.GetTempPath(), $"greenscreen_photo_{Guid.NewGuid():N}.png");
        photo.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>Writes a solid blue background, distinct from both the
    /// photo's green and red regions so a test can tell it actually landed
    /// where the green was keyed out.</summary>
    private static string WriteBlueBackground()
    {
        using var background = new Bitmap(40, 40, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(background))
        {
            g.Clear(Color.Blue);
        }

        string path = Path.Combine(Path.GetTempPath(), $"greenscreen_bg_{Guid.NewGuid():N}.png");
        background.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task ApplyGreenScreenAsync_ReturnsPathToARealJpegAndLeavesOriginalUntouched()
    {
        string photoPath = WriteGreenBackdropPhoto();
        string backgroundPath = WriteBlueBackground();
        var greenScreen = new GdiGreenScreenService();

        string compositedPath = await greenScreen.ApplyGreenScreenAsync(photoPath, backgroundPath);

        Assert.NotEqual(photoPath, compositedPath);
        Assert.True(File.Exists(compositedPath));
        Assert.True(File.Exists(photoPath));

        byte[] header = new byte[2];
        using (var stream = File.OpenRead(compositedPath))
        {
            _ = stream.Read(header, 0, header.Length);
        }
        // JPEG files start with the 0xFF 0xD8 magic bytes.
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
    }

    [Fact]
    public async Task ApplyGreenScreenAsync_ReplacesGreenWithBackgroundAndLeavesSubjectAlone()
    {
        string photoPath = WriteGreenBackdropPhoto();
        string backgroundPath = WriteBlueBackground();
        var greenScreen = new GdiGreenScreenService();

        string compositedPath = await greenScreen.ApplyGreenScreenAsync(photoPath, backgroundPath);

        using var composited = new Bitmap(compositedPath);

        // Stretched to the photo's own dimensions, same reasoning
        // GdiFrameOverlayService uses for frame assets.
        Assert.Equal(100, composited.Width);
        Assert.Equal(100, composited.Height);

        // Top half (was pure green) should now read as the background's
        // blue -- JPEG re-encoding can shift channels a little, so allow slack.
        Color keyedPixel = composited.GetPixel(50, 25);
        Assert.True(keyedPixel.B > 150 && keyedPixel.G < 100 && keyedPixel.R < 100);

        // Bottom half (was solid red, nowhere near "green dominant") should
        // still read as red -- the subject was left alone.
        Color subjectPixel = composited.GetPixel(50, 75);
        Assert.True(subjectPixel.R > 150 && subjectPixel.G < 100 && subjectPixel.B < 100);
    }

    [Fact]
    public async Task ApplyToLiveFrameAsync_ReplacesGreenWithBackgroundAndLeavesSubjectAlone()
    {
        using Bitmap frameBitmap = RenderGreenBackdropFrame(100, 100);
        using var frameStream = new MemoryStream();
        frameBitmap.Save(frameStream, ImageFormat.Png);
        byte[] frameBytes = frameStream.ToArray();
        string backgroundPath = WriteBlueBackground();
        var greenScreen = new GdiGreenScreenService();

        byte[] compositedBytes = await greenScreen.ApplyToLiveFrameAsync(frameBytes, backgroundPath);

        using var compositedStream = new MemoryStream(compositedBytes);
        using var composited = new Bitmap(compositedStream);
        Assert.Equal(100, composited.Width);
        Assert.Equal(100, composited.Height);

        // Same keyed-vs-subject check as the file-based path above -- proves
        // the in-memory live-frame path runs the identical chroma-key logic,
        // not a simplified stand-in.
        Color keyedPixel = composited.GetPixel(50, 25);
        Assert.True(keyedPixel.B > 150 && keyedPixel.G < 100 && keyedPixel.R < 100);
        Color subjectPixel = composited.GetPixel(50, 75);
        Assert.True(subjectPixel.R > 150 && subjectPixel.G < 100 && subjectPixel.B < 100);
    }

    [Fact]
    public async Task ApplyToLiveFrameAsync_CompletesWellWithinTheLiveViewPollBudget()
    {
        // 1280x720: at or above what the webcam fallback path actually
        // produces (Week 1's live-view work measured ~130ms for a full
        // capture-cycle round trip at that ballpark resolution). KioskViewModel
        // polls live view every 150ms (LiveViewInterval) -- if a single
        // composite regularly ate a meaningful chunk of that, frames would
        // visibly lag behind the guest moving. 1000ms is a generous ceiling
        // for "clearly fine," not a tight perf target -- this is a regression
        // guard against something pathological, not a benchmark.
        using Bitmap frameBitmap = RenderGreenBackdropFrame(1280, 720);
        using var frameStream = new MemoryStream();
        frameBitmap.Save(frameStream, ImageFormat.Jpeg);
        byte[] frameBytes = frameStream.ToArray();
        string backgroundPath = WriteBlueBackground();
        var greenScreen = new GdiGreenScreenService();

        var stopwatch = Stopwatch.StartNew();
        byte[] compositedBytes = await greenScreen.ApplyToLiveFrameAsync(frameBytes, backgroundPath);
        stopwatch.Stop();

        Assert.NotEmpty(compositedBytes);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Live-frame chroma-key composite took {stopwatch.ElapsedMilliseconds}ms for a 1280x720 frame -- " +
            "too slow to keep up with KioskViewModel's 150ms live-view poll interval.");
    }
}
