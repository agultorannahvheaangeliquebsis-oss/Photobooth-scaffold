namespace Photobooth.Core;

/// <summary>Admin-editable per-booth settings. Countdown duration, whether Glam
/// Booth mode is on, and the print layout (paper size / single vs. strip) -- see
/// AdminWindow's Settings section.</summary>
public record BoothSettings(int CountdownSeconds, bool GlamFilterEnabled, PrintTemplate PrintTemplate)
{
    /// <summary>Brand identity (colors/logo/event name). An init-only property
    /// outside the primary constructor, not a 4th positional parameter -- a
    /// record's positional parameters can't default to another type's static
    /// field (not a compile-time constant), but an init property can, which
    /// means every existing `new BoothSettings(...)` call site (mocks,
    /// SqlBoothSettingsProvider, AdminWindow, tests, ConsoleDemo) keeps
    /// compiling unchanged with Theme silently defaulting.</summary>
    public BoothTheme Theme { get; init; } = BoothTheme.Default;
}

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
