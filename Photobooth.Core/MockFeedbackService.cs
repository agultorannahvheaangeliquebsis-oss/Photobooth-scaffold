namespace Photobooth.Core;

/// <summary>
/// Fake feedback capture for development and tests. Defaults to a guest who
/// leaves a 5-star rating and no comment -- set SkipNext to simulate a guest
/// who walks away without rating anything at all.
/// </summary>
public class MockFeedbackService : IFeedbackService
{
    public int? SimulateRating { get; set; } = 5;
    public string? SimulateComment { get; set; } = null;

    /// <summary>When true, the next CollectAsync call reports no rating and no comment
    /// instead of the simulated values. Resets itself after firing once, same pattern
    /// as MockCameraService.FailNextCapture.</summary>
    public bool SkipNext { get; set; } = false;

    public async Task<FeedbackResult> CollectAsync(CancellationToken ct = default)
    {
        // Real guests take a moment to tap a star and maybe type a comment;
        // simulate that so the UI's Feedback state has something realistic
        // to sit in.
        await Task.Delay(300, ct);

        if (SkipNext)
        {
            SkipNext = false;
            return new FeedbackResult(null, null);
        }

        return new FeedbackResult(SimulateRating, SimulateComment);
    }
}
