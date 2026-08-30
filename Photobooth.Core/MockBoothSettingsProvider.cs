namespace Photobooth.Core;

/// <summary>
/// Fake settings provider for development and tests. Defaults match the
/// schema's own column defaults (3-second countdown, Glam filter off).
/// Settable so a test/demo can simulate an admin changing settings between
/// sessions and confirm the next session actually picks them up.
/// </summary>
public class MockBoothSettingsProvider : IBoothSettingsProvider
{
    public BoothSettings Settings { get; set; } = new(CountdownSeconds: 3, GlamFilterEnabled: false);

    public Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);
}
