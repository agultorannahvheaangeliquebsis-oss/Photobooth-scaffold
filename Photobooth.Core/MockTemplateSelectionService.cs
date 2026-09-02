namespace Photobooth.Core;

/// <summary>
/// Fake template selection for development and tests -- simulates a guest
/// tapping through the layout picker. Defaults to picking the first offered
/// template, since "guest picks a layout" is the common case; set SkipNext to
/// simulate a guest tapping "use default layout" instead. Same shape as
/// MockFrameSelectionService.
/// </summary>
public class MockTemplateSelectionService : ITemplateSelectionService
{
    /// <summary>When true, the next SelectTemplateAsync call reports no template chosen
    /// instead of picking the first option. Resets itself after firing once, same
    /// pattern as MockFrameSelectionService.SkipNext.</summary>
    public bool SkipNext { get; set; } = false;

    public async Task<PrintTemplate?> SelectTemplateAsync(IReadOnlyList<PrintTemplate> options, CancellationToken ct = default)
    {
        // Real guests take a moment to browse the options and tap one;
        // simulate that so the UI's FramePicker state has something
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
