using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;
using Photobooth.Core;

namespace Photobooth.Tests;

// Same "one test class allowed to touch System.Drawing.Common directly"
// exception GdiPhotoBrandingServiceTests/GdiPhotoFilterServiceTests already
// have, for the same reason: MockGifComposerService is what everything else
// in the suite exercises instead.
[SupportedOSPlatform("windows")]
public class GdiGifComposerServiceTests
{
    [Fact]
    public async Task ComposeAsync_Forward_ProducesARealAnimatedGifWithOneFramePerInput()
    {
        var camera = new MockCameraService();
        var frames = new List<string> { await camera.CaptureAsync(), await camera.CaptureAsync(), await camera.CaptureAsync() };
        var composer = new GdiGifComposerService();

        string gifPath = await composer.ComposeAsync(frames, reversed: false, frameDelayMs: 100);

        Assert.True(File.Exists(gifPath));

        byte[] signature = new byte[6];
        using (var stream = File.OpenRead(gifPath))
        {
            _ = stream.Read(signature, 0, signature.Length);
        }
        Assert.Equal("GIF89a", Encoding.ASCII.GetString(signature));

        using var image = Image.FromFile(gifPath);
        int frameCount = image.GetFrameCount(FrameDimension.Time);
        Assert.Equal(3, frameCount);
    }

    [Fact]
    public async Task ComposeAsync_Reversed_PlaysForwardThenBackwardWithoutRepeatingTheEndFrames()
    {
        var camera = new MockCameraService();
        var frames = new List<string> { await camera.CaptureAsync(), await camera.CaptureAsync(), await camera.CaptureAsync(), await camera.CaptureAsync() };
        var composer = new GdiGifComposerService();

        string gifPath = await composer.ComposeAsync(frames, reversed: true, frameDelayMs: 100);

        using var image = Image.FromFile(gifPath);
        int frameCount = image.GetFrameCount(FrameDimension.Time);
        // 4 forward + 2 backward (frames 3,2 -- skipping the repeated last
        // and first frame) = 6, matching BoothStateMachine's isBurstMode
        // comment on the "forward then backward, without repeating the two
        // end frames" splice.
        Assert.Equal(6, frameCount);
    }
}
