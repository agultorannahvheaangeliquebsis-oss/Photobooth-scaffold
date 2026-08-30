namespace Photobooth.Core;

/// <summary>
/// Fake guestbook prompt for development and tests -- simulates a guest
/// tapping through without any real UI, so Photobooth.Tests and
/// Photobooth.ConsoleDemo can exercise this seam deterministically.
/// </summary>
public class MockGuestbookPromptService : IGuestbookPromptService
{
    /// <summary>Whether AskToRecordAsync should report the guest wants to record. Defaults to true.</summary>
    public bool SimulateWantsToRecord { get; set; } = true;

    /// <summary>When true, the next AskToRecordAsync call reports a decline regardless of SimulateWantsToRecord. Resets itself after firing once.</summary>
    public bool SkipNext { get; set; }

    public async Task<bool> AskToRecordAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);

        if (SkipNext)
        {
            SkipNext = false;
            return false;
        }

        return SimulateWantsToRecord;
    }

    public async Task WaitForStopAsync(CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
    }
}
