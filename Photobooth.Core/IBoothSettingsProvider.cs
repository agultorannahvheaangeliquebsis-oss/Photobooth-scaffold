namespace Photobooth.Core;

/// <summary>Admin-editable per-booth settings. Countdown duration and whether Glam
/// Booth mode is on -- see AdminWindow's Settings section.</summary>
public record BoothSettings(int CountdownSeconds, bool GlamFilterEnabled);

/// <summary>
/// Abstracts reading the booth's current settings. Same interface-plus-mock
/// seam as everything else -- BoothStateMachine reads this fresh at the
/// start of every session (not just once at startup), so an admin change
/// takes effect for the very next guest without restarting the app.
/// </summary>
public interface IBoothSettingsProvider
{
    Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default);
}
