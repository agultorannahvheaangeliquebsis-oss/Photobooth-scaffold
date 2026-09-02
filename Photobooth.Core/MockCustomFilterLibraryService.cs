namespace Photobooth.Core;

/// <summary>
/// Fake custom filter library for development and tests. Defaults to no
/// custom filters configured (matching a fresh CustomFilter table with
/// nothing uploaded into it), same "off until configured" default as
/// MockFrameLibraryService.
/// </summary>
public class MockCustomFilterLibraryService : ICustomFilterLibraryService
{
    public List<CustomFilterOption> CustomFilters { get; set; } = new();

    public Task<IReadOnlyList<CustomFilterOption>> GetActiveCustomFiltersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CustomFilterOption>>(CustomFilters);
}
