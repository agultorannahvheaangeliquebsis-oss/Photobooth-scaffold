namespace Photobooth.Core;

/// <summary>
/// Fake glam filter for development and tests -- copies the file to a new
/// path with a "_glam" suffix rather than compositing anything, so
/// Photobooth.Tests and Photobooth.ConsoleDemo don't need
/// System.Drawing.Common (Windows-only) just to exercise this seam.
/// </summary>
public class MockPhotoFilterService : IPhotoFilterService
{
    public async Task<string> ApplyGlamFilterAsync(string photoPath, CancellationToken ct = default)
    {
        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_glam{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
