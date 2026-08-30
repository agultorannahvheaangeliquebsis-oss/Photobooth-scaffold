namespace Photobooth.Core;

/// <summary>
/// Fake frame compositing for development and tests -- copies the file to a
/// new path with a "_framed" suffix rather than compositing anything, same
/// reasoning as MockPhotoBrandingService/MockPhotoFilterService.
/// </summary>
public class MockFrameOverlayService : IFrameOverlayService
{
    public async Task<string> ApplyFrameAsync(string photoPath, string frameImagePath, CancellationToken ct = default)
    {
        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_framed{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
