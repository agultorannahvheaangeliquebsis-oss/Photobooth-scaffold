namespace Photobooth.Core;

/// <summary>
/// Fake frame library for development and tests. Defaults to no frames
/// configured (matching a fresh Frame table with nothing seeded into it),
/// so BoothStateMachine's FramePicker state is skipped entirely unless a
/// test explicitly populates Frames -- same "off until configured" default
/// as MockBoothSettingsProvider's GlamFilterEnabled.
/// </summary>
public class MockFrameLibraryService : IFrameLibraryService
{
    public List<FrameOption> Frames { get; set; } = new();

    public Task<IReadOnlyList<FrameOption>> GetActiveFramesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FrameOption>>(Frames);
}
