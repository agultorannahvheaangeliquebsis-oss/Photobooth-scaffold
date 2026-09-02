namespace Photobooth.Core;

/// <summary>
/// Fake settings provider for development and tests. Defaults match the
/// schema's own column defaults (3-second countdown, Glam filter off).
/// Settable so a test/demo can simulate an admin changing settings between
/// sessions and confirm the next session actually picks them up.
/// </summary>
public class MockBoothSettingsProvider : IBoothSettingsProvider
{
    /// <summary>Screen.FinalScreenTimeoutSeconds overridden down from
    /// ScreenSettings.Default's real-world 30s -- BoothStateMachine now
    /// blocks on that value for its Complete-state dwell (see
    /// RunSessionAsync), and a dev/test run needs that dwell to be near-
    /// instant, not a real 30-second wait on every single session it runs.</summary>
    public BoothSettings Settings { get; set; } = new(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default)
    {
        Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 },
    };

    public Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);
}
