using Photobooth.Core;

namespace Photobooth.UI.Services;

/// <summary>
/// Wraps the booth's real <see cref="IBoothSettingsProvider"/> and swaps in the
/// capture mode the guest picked on the idle screen (Photo / GIF / Boomerang /
/// Video), leaving every other setting exactly as configured.
///
/// Why a decorator rather than a settings write: BoothStateMachine reads
/// settings fresh at the top of every session and branches on
/// <see cref="CaptureSettings.Mode"/>, so a per-guest choice has to arrive
/// through that same read. The alternatives were both worse -- writing the
/// guest's pick back to the Location row would persist a transient choice as
/// booth configuration (and race an admin editing settings in AdminWindow),
/// and passing a mode argument into RunSessionAsync would put a UI concern in
/// the state machine's signature. This keeps the guest's pick in the UI layer
/// and Core untouched.
///
/// <see cref="Mode"/> is null until a guest taps a tile, in which case the
/// underlying settings pass through completely unmodified -- so a booth whose
/// kiosk never shows the mode tiles behaves exactly as it did before.
/// </summary>
public class CaptureModeOverrideSettingsProvider : IBoothSettingsProvider
{
    private readonly IBoothSettingsProvider _inner;

    public CaptureModeOverrideSettingsProvider(IBoothSettingsProvider inner) => _inner = inner;

    /// <summary>The mode the guest picked, or null to use the configured one.
    /// Written from the UI thread when a tile is tapped and read on whatever
    /// thread runs the session; both are single reference-sized writes of an
    /// immutable string, and a stale read only ever means one session uses the
    /// previous pick -- volatile keeps that bounded without a lock on the
    /// session's hot path.</summary>
    private volatile string? _mode;

    public string? Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public async Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        BoothSettings settings = await _inner.GetSettingsAsync(ct);

        if (_mode is not string mode || mode == settings.Capture.Mode)
        {
            return settings;
        }

        // `with` on both records, not a mutation: BoothSettings and
        // CaptureSettings are records the state machine may hold onto for the
        // length of a session, and the provider is shared across sessions.
        return settings with { Capture = settings.Capture with { Mode = mode } };
    }
}
