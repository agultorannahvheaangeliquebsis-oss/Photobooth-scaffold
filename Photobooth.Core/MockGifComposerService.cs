namespace Photobooth.Core;

/// <summary>
/// Fake GIF composer for development and tests -- copies the first frame to
/// a new path with a "_gif"/"_boomerang" suffix rather than actually
/// encoding an animated file, same reasoning MockPhotoBrandingService gives
/// for not depending on the real imaging work here.
/// </summary>
public class MockGifComposerService : IGifComposerService
{
    /// <summary>How many frames the most recent ComposeAsync call received -- lets
    /// Photobooth.ConsoleDemo/tests prove the right number of frames actually
    /// reached this seam, not just that the code ran.</summary>
    public int LastFrameCount { get; private set; }

    /// <summary>The reversed flag passed to the most recent ComposeAsync call.</summary>
    public bool LastReversed { get; private set; }

    /// <summary>The frameDelayMs passed to the most recent ComposeAsync call -- lets tests
    /// confirm BoothStateMachine's playback-duration computation (independent of capture
    /// cadence) actually reaches the composer.</summary>
    public int LastFrameDelayMs { get; private set; }

    public async Task<string> ComposeAsync(IReadOnlyList<string> framePaths, bool reversed, int frameDelayMs, CancellationToken ct = default)
    {
        if (framePaths.Count == 0)
        {
            throw new ArgumentException("Need at least one frame to compose.", nameof(framePaths));
        }

        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        LastFrameCount = framePaths.Count;
        LastReversed = reversed;
        LastFrameDelayMs = frameDelayMs;

        string firstFrame = framePaths[0];
        string directory = Path.GetDirectoryName(firstFrame) is { Length: > 0 } dir ? dir : ".";
        string suffix = reversed ? "_boomerang" : "_gif";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(firstFrame)}{suffix}.gif");
        File.Copy(firstFrame, outputPath, overwrite: true);
        return outputPath;
    }
}
