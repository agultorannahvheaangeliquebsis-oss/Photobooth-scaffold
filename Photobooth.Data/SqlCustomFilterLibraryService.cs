using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real ICustomFilterLibraryService: reads active CustomFilter rows for this
/// location fresh on every call (no caching), same reasoning as
/// SqlFrameLibraryService -- a filter an admin just added or retired takes
/// effect for the very next guest session.
/// </summary>
public class SqlCustomFilterLibraryService : ICustomFilterLibraryService
{
    private readonly int _locationId;
    private readonly CustomFilterRepository _customFilters = new();

    public SqlCustomFilterLibraryService(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<IReadOnlyList<CustomFilterOption>> GetActiveCustomFiltersAsync(CancellationToken ct = default)
    {
        List<CustomFilterRecord> records = await _customFilters.GetActiveByLocationAsync(_locationId, ct);
        return records.Select(r => new CustomFilterOption(r.CustomFilterId, r.Name, r.CubeFilePath)).ToList();
    }
}
