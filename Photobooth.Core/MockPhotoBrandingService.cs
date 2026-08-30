namespace Photobooth.Core;

/// <summary>
/// Fake branding for development and tests -- copies the file to a new
/// path with a "_branded" suffix rather than compositing anything, so
/// Photobooth.Tests and Photobooth.ConsoleDemo don't need
/// System.Drawing.Common (Windows-only) just to exercise this seam.
/// </summary>
public class MockPhotoBrandingService : IPhotoBrandingService
{
    public async Task<string> ApplyBrandingAsync(string photoPath, CancellationToken ct = default)
    {
        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_branded{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
