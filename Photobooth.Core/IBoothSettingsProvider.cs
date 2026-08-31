namespace Photobooth.Core;

/// <summary>Admin-editable per-booth settings. Countdown duration, whether Glam
/// Booth mode is on, and the print layout (paper size / single vs. strip) -- see
/// AdminWindow's Settings section. AdminPin gates MainWindow's Setup/launch
/// screen (see BoothState.Setup) -- a positional parameter with a literal
/// default, unlike Theme below, since "1234" (matching the schema column's own
/// default) is a compile-time constant a positional parameter can default to.</summary>
public record BoothSettings(int CountdownSeconds, bool GlamFilterEnabled, PrintTemplate PrintTemplate, string AdminPin = "1234")
{
    /// <summary>Brand identity (colors/logo/event name). An init-only property
    /// outside the primary constructor, not a 4th positional parameter -- a
    /// record's positional parameters can't default to another type's static
    /// field (not a compile-time constant), but an init property can, which
    /// means every existing `new BoothSettings(...)` call site (mocks,
    /// SqlBoothSettingsProvider, AdminWindow, tests, ConsoleDemo) keeps
    /// compiling unchanged with Theme silently defaulting.</summary>
    public BoothTheme Theme { get; init; } = BoothTheme.Default;

    // dslrBooth feature-parity settings (see BUILD_PLAN.md's "dslrBooth
    // feature-parity plan" section, Phase 1) -- nested init-only records,
    // same reasoning as Theme above: grouped by AdminWindow section, and
    // an init property (not a positional parameter) so every existing
    // `new BoothSettings(...)` call site keeps compiling unchanged with
    // these silently defaulting.
    public CaptureSettings Capture { get; init; } = CaptureSettings.Default;
    public ScreenSettings Screen { get; init; } = ScreenSettings.Default;
    public EffectsSettings Effects { get; init; } = EffectsSettings.Default;
    public GreenScreenSettings GreenScreen { get; init; } = GreenScreenSettings.Default;
    public SurveySettings Survey { get; init; } = SurveySettings.Default;
    public DisclaimerSettings Disclaimer { get; init; } = DisclaimerSettings.Default;
    public PrintOptions PrintOptions { get; init; } = PrintOptions.Default;
    public SharingSettings Sharing { get; init; } = SharingSettings.Default;
}

/// <summary>Capture mode + related timing, see dslrBooth's Capture Settings screen.
/// FrameCount/FrameDelayMs drive the GIF/Boomerang capture loop (see
/// BoothStateMachine, IGifComposerService) -- unused when Mode is "Photo".</summary>
public record CaptureSettings(string Mode = "Photo", bool AlsoCreateGif = false, int FrameCount = 4, int FrameDelayMs = 500)
{
    public static CaptureSettings Default { get; } = new();
}

/// <summary>Welcome/Capture screen behavior, see dslrBooth's Screen Editor settings panels.</summary>
public record ScreenSettings(bool BoothIconsEnabled = false, bool ShowLiveView = true, bool MirrorLiveView = true, int LiveViewRotation = 0)
{
    public static ScreenSettings Default { get; } = new();
}

/// <summary>Beauty filter / filter mode / watermark, see dslrBooth's Effects & Stickers screen.</summary>
public record EffectsSettings(bool BeautyFilterEnabled = false, string FiltersMode = "Ask", string? WatermarkImagePath = null)
{
    public static EffectsSettings Default { get; } = new();
}

/// <summary>See dslrBooth's Green Screen screen.</summary>
public record GreenScreenSettings(bool Enabled = false, string? BackgroundImagePath = null)
{
    public static GreenScreenSettings Default { get; } = new();
}

/// <summary>See dslrBooth's Survey screen. Question-builder itself is out of scope for now
/// (see BUILD_PLAN.md's open question) -- this is just the on/off switch.</summary>
public record SurveySettings(bool Enabled = false)
{
    public static SurveySettings Default { get; } = new();
}

/// <summary>See dslrBooth's Disclaimer screen. Distinct from Consent's DisclaimerAccepted
/// outcome -- this is the admin-editable prompt copy shown before that choice is made.</summary>
public record DisclaimerSettings(string Header = "Do you agree with the terms?", string Text = "")
{
    public static DisclaimerSettings Default { get; } = new();
}

/// <summary>Print behavior beyond layout/paper size (already covered by PrintTemplate),
/// see dslrBooth's Print Setup screen.</summary>
public record PrintOptions(
    bool PrintAutomatically = true,
    bool ShowPrintButton = false,
    int PrintLimitPerEvent = 5000,
    int PrintLimitPerSession = 3,
    string PrintSharpening = "Medium")
{
    public static PrintOptions Default { get; } = new();
}

/// <summary>See dslrBooth's Sharing Settings screen. Channel toggles only -- actual
/// delivery is IEmailDeliveryService etc., unaffected by this record.</summary>
public record SharingSettings(bool EmailEnabled = true, bool SmsEnabled = false, bool QrEnabled = true)
{
    public static SharingSettings Default { get; } = new();
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
