namespace Photobooth.Core;

/// <summary>
/// Fake consent capture for development and tests. Defaults to a guest who
/// accepts the disclaimer and opts in with an email, since that's the
/// common case -- set DeclineNext to true to exercise the decline path.
/// </summary>
public class MockConsentService : IConsentService
{
    /// <summary>When true, the next CollectAsync call reports a declined disclaimer
    /// instead of accepting. Resets itself after firing once, same pattern as
    /// MockCameraService.FailNextCapture.</summary>
    public bool DeclineNext { get; set; } = false;

    public bool SimulateEmailOptIn { get; set; } = true;
    public string? SimulateEmail { get; set; } = "guest@example.com";

    public async Task<ConsentResult> CollectAsync(CancellationToken ct = default)
    {
        // Real guests take a moment to read the disclaimer and tap through;
        // simulate that so the UI's Consent state has something realistic to
        // sit in.
        await Task.Delay(500, ct);

        if (DeclineNext)
        {
            DeclineNext = false;
            return new ConsentResult(false, false, null);
        }

        return new ConsentResult(true, SimulateEmailOptIn, SimulateEmailOptIn ? SimulateEmail : null);
    }
}
