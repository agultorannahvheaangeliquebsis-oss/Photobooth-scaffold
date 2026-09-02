namespace Photobooth.Core;

/// <summary>
/// Fake ICustomFilterService for development and tests -- same "copy with a
/// suffix, no real transform" shape as MockFilterPresetService, so tests can
/// assert a distinct output path without needing a real .cube file or GDI+.
/// </summary>
public class MockCustomFilterService : ICustomFilterService
{
    public async Task<string> ApplyCustomFilterAsync(string photoPath, string cubeFilePath, CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string suffix = Path.GetFileNameWithoutExtension(cubeFilePath);
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_{suffix}{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
