namespace Photobooth.Core;

/// <summary>Fake filter preset application for development and tests -- copies
/// the file to a new path with a "_<preset>" suffix rather than compositing
/// anything, same reasoning MockPhotoFilterService already gives.</summary>
public class MockFilterPresetService : IFilterPresetService
{
    public async Task<string> ApplyPresetAsync(string photoPath, PhotoFilterPreset preset, CancellationToken ct = default)
    {
        if (preset == PhotoFilterPreset.Original)
        {
            return photoPath;
        }

        // Real compositing takes a moment; simulate it.
        await Task.Delay(20, ct);

        string directory = Path.GetDirectoryName(photoPath) is { Length: > 0 } dir ? dir : ".";
        string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(photoPath)}_{preset}{Path.GetExtension(photoPath)}");
        File.Copy(photoPath, outputPath, overwrite: true);
        return outputPath;
    }
}
