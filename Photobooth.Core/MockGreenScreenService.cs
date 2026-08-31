namespace Photobooth.Core;

/// <summary>
/// Fake chroma-key compositing for development and tests -- copies the file
/// to a new path with a "_greenscreen" suffix rather than compositing
/// anything, same reasoning as MockPhotoFilterService/MockFrameOverlayService.
/// </summary>
public class MockGreenScreenService : IGreenScreenService
{
    public async Task<string> ApplyGreenScreenAsync(string photoPath, string backgroundImagePath, CancellationToken ct = default)
    {
        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_greenscreen{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }

    /// <summary>Returns frameBytes unchanged -- there's no dev-machine stand-in
    /// image worth faking a composite onto for a per-frame preview the way the
    /// suffixed-file convention works for the other Mock* methods, and a live
    /// preview's whole point is being seen on screen, which a mock can't fake
    /// meaningfully anyway.</summary>
    public Task<byte[]> ApplyToLiveFrameAsync(byte[] frameBytes, string backgroundImagePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(frameBytes);
    }
}
