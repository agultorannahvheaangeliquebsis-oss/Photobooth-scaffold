namespace Photobooth.Core;

/// <summary>
/// Fake Virtual Attendant for development and tests. Defaults to disabled
/// with no clips configured (matching a fresh VirtualAttendantClip table),
/// so BoothStateMachine's per-SetState cue lookup is a no-op unless a
/// test/demo explicitly populates Settings/Clips -- same "off until
/// configured" default as MockFrameLibraryService.
/// </summary>
public class MockVirtualAttendantService : IVirtualAttendantService
{
    public VirtualAttendantSettings Settings { get; set; } = VirtualAttendantSettings.Default;

    /// <summary>Pool of clips per stage, ordered as the admin arranged them (SortOrder) --
    /// picked in order unless this stage's Randomize flag is on, in which case one is
    /// picked at random from the pool.</summary>
    public Dictionary<BoothState, List<AttendantClip>> ClipsByStage { get; set; } = new();

    /// <summary>Injectable for deterministic tests -- defaults to a real Random.</summary>
    public Random Random { get; set; } = new();

    public Task<AttendantClip?> GetCueAsync(BoothState state, CancellationToken ct = default)
    {
        if (!Settings.Enabled || !ClipsByStage.TryGetValue(state, out List<AttendantClip>? clips) || clips.Count == 0)
        {
            return Task.FromResult<AttendantClip?>(null);
        }

        AttendantClip clip = ShouldRandomize(state) ? clips[Random.Next(clips.Count)] : clips[0];
        return Task.FromResult<AttendantClip?>(clip);
    }

    private bool ShouldRandomize(BoothState state) => state switch
    {
        BoothState.Consent => Settings.RandomizeConsent,
        BoothState.Countdown => Settings.RandomizeCountdown,
        BoothState.Capturing => Settings.RandomizeCapturing,
        BoothState.Reviewing => Settings.RandomizeReviewing,
        BoothState.Printing => Settings.RandomizePrinting,
        BoothState.Complete => Settings.RandomizeComplete,
        _ => false,
    };
}
