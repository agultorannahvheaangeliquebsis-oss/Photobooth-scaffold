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

    /// <summary>How long CollectAsync simulates the guest taking. Settable (not a
    /// hardcoded constant) so a test can push it past BoothStateMachine's shared
    /// guest idle timeout to exercise the "walked away" path deterministically,
    /// without needing a dedicated never-responds flag.</summary>
    public TimeSpan SimulatedDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    public async Task<FeedbackResult> CollectAsync(CancellationToken ct = default)
    {
        // Real guests take a moment to tap a star and maybe type a comment;
        // simulate that so the UI's Feedback state has something realistic
        // to sit in.
        await Task.Delay(SimulatedDelay, ct);

        if (SkipNext)
        {
            SkipNext = false;
            return new FeedbackResult(null, null);
        }

        return new FeedbackResult(SimulateRating, SimulateComment);
    }
}
