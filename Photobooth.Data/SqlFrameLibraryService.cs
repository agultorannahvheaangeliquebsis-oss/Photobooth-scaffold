using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real IFrameLibraryService: reads active Frame rows for this location
/// fresh on every call (no caching), same reasoning as
/// SqlBoothSettingsProvider -- a frame an admin just added or retired takes
/// effect for the very next guest session.
/// </summary>
public class SqlFrameLibraryService : IFrameLibraryService
{
    private readonly int _locationId;
    private readonly FrameRepository _frames = new();

    public SqlFrameLibraryService(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<IReadOnlyList<FrameOption>> GetActiveFramesAsync(CancellationToken ct = default)
    {
        List<FrameRecord> records = await _frames.GetActiveByLocationAsync(_locationId, ct);
        return records.Select(r => new FrameOption(r.FrameId, r.Name, r.ImagePath)).ToList();
    }
}
