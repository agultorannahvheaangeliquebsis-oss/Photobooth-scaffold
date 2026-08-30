namespace Photobooth.Core;

/// <summary>
/// Fake branding for development and tests -- copies the file to a new
/// path with a "_branded" suffix rather than compositing anything, so
/// Photobooth.Tests and Photobooth.ConsoleDemo don't need
/// System.Drawing.Common (Windows-only) just to exercise this seam.
/// </summary>
public class MockPhotoBrandingService : IPhotoBrandingService
{
    /// <summary>The studioName passed to the most recent ApplyBrandingAsync call --
    /// lets Photobooth.ConsoleDemo prove a theme change actually reached branding,
    /// not just that the code ran.</summary>
    public string? LastStudioName { get; private set; }

    public async Task<string> ApplyBrandingAsync(string photoPath, string studioName, CancellationToken ct = default)
    {
        // Real compositing takes a moment; simulate it.
        await Task.Delay(50, ct);

        LastStudioName = studioName;

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_branded{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
