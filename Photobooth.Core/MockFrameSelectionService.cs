namespace Photobooth.Core;

/// <summary>
/// Fake frame selection for development and tests -- simulates a guest
/// tapping through the picker. Defaults to picking the first offered frame,
/// since "guest picks a frame" is the common case; set SkipNext to simulate
/// a guest tapping "no frame" instead.
/// </summary>
public class MockFrameSelectionService : IFrameSelectionService
{
    /// <summary>When true, the next SelectFrameAsync call reports no frame chosen instead
    /// of picking the first option. Resets itself after firing once, same pattern as
    /// MockCameraService.FailNextCapture.</summary>
    public bool SkipNext { get; set; } = false;

    public async Task<FrameOption?> SelectFrameAsync(IReadOnlyList<FrameOption> options, CancellationToken ct = default)
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
