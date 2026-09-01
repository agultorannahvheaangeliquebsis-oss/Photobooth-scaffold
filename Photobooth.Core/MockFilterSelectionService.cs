namespace Photobooth.Core;

/// <summary>
/// Fake filter selection for development and tests -- simulates a guest
/// tapping through the picker. Defaults to picking the first offered option
/// (matches MockFrameSelectionService's own default reasoning); set SkipNext
/// to simulate a guest tapping past the picker instead.
/// </summary>
public class MockFilterSelectionService : IFilterSelectionService
{
    public bool SkipNext { get; set; } = false;

    public async Task<FilterOption?> SelectFilterAsync(IReadOnlyList<FilterOption> options, CancellationToken ct = default)
    {
        // Real guests take a moment to browse the previews and tap one;
        // simulate that so the UI's FilterPicker state has something
        // realistic to sit in.
        await Task.Delay(500, ct);

        if (SkipNext)
        {
            SkipNext = false;
            return null;
        }

        return options.Count > 0 ? options[0] : null;
    }
}
