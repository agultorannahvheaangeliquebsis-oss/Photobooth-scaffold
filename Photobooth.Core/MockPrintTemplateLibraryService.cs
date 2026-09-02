namespace Photobooth.Core;

/// <summary>
/// Fake favorited-template source for development and tests -- same shape as
/// MockFrameLibraryService. Empty by default, matching a fresh booth where no
/// admin has favorited any saved template yet (the guest-facing layout picker
/// then never shows, same "empty pool = feature invisible" reasoning
/// MockFrameLibraryService already established).
/// </summary>
public class MockPrintTemplateLibraryService : IPrintTemplateLibraryService
{
    public List<PrintTemplate> Templates { get; set; } = new();

    public Task<IReadOnlyList<PrintTemplate>> GetFavoriteTemplatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PrintTemplate>>(Templates);
}
