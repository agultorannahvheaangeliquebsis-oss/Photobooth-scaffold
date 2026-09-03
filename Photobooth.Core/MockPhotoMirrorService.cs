namespace Photobooth.Core;

/// <summary>
/// Fake mirror flip for development and tests -- copies the file to a new
/// path with a "_mirrored" suffix rather than actually flipping any pixels,
/// same reasoning MockPhotoFilterService gives for not needing
/// System.Drawing.Common (Windows-only) just to exercise this seam.
/// </summary>
public class MockPhotoMirrorService : IPhotoMirrorService
{
    public async Task<string> FlipHorizontallyAsync(string photoPath, CancellationToken ct = default)
    {
        // Real flipping takes a moment; simulate it.
        await Task.Delay(50, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_mirrored{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
