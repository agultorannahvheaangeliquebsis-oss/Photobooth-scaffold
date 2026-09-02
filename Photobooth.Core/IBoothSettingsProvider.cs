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

    // Phase 6 -- see BUILD_PLAN.md's Phase 6 scope text.
    public VirtualAttendantSettings VirtualAttendant { get; init; } = VirtualAttendantSettings.Default;

    // Admin Dashboard sections added after the dslrBooth-parity pass (see
    // BUILD_PLAN.md's "Admin Dashboard stub sections" writeup) -- same
    // init-property reasoning as Theme/Capture/etc. above.

    /// <summary>Show Lock Screen: blocks a new guest session from starting
    /// (see KioskViewModel.CanStartSession) without interrupting one already
    /// in progress. Re-read at every return to Idle, same cadence Theme/
    /// Screen already use -- plus applied immediately by AdminWindow's own
    /// Lock Now/Unlock buttons when they're reached from a live kiosk
    /// session (see KioskAdminViewModel.OnLockChanged), since waiting for
    /// the next Idle re-read would leave the booth briefly unlocked to a
    /// guest tapping the screen right now.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Whether the loopback Remote Control HTTP listener should be
    /// running for this event (see RemoteControlServer in Photobooth.UI).</summary>
    public bool RemoteControlEnabled { get; init; }

    public SlideshowSettings Slideshow { get; init; } = SlideshowSettings.Default;
}

/// <summary>See AdminWindow's Slideshow section. Cycles this event's captured
/// photos on an idle screen or second monitor between guests (see
/// SlideshowWindow in Photobooth.UI). Transition only has one real
/// implementation so far (Fade -- see SlideshowWindow); "Slide" and "Ken
/// Burns" round-trip through Save but render as a fade too, same honest
/// "saved but not yet applied" status BeautyFilterEnabled already has for
/// its own unbuilt effect. ShowQrOverlay is likewise saved but not yet
/// rendered -- a slideshow spans many photos, and there's no single "the"
/// QR to show for all of them without a real per-photo overlay pass this
/// build doesn't attempt yet.</summary>
public record SlideshowSettings(
    bool Enabled = true,
    int IntervalSeconds = 4,
    string Transition = "Fade",
    bool ShowLogoOverlay = true,
    bool ShowQrOverlay = true)
{
    public static SlideshowSettings Default { get; } = new();
}

/// <summary>Capture mode + related timing, see dslrBooth's Capture Settings screen.
/// FrameCount/FrameDelayMs drive the GIF/Boomerang capture loop (see
/// BoothStateMachine, IGifComposerService); VideoDurationSeconds drives the
/// Video-mode recording length (see IBoothVideoService). All three are
/// unused when Mode is "Photo".</summary>
public record CaptureSettings(string Mode = "Photo", bool AlsoCreateGif = false, int FrameCount = 4, int FrameDelayMs = 500, int VideoDurationSeconds = 10)
{
    public static CaptureSettings Default { get; } = new();
}

/// <summary>Welcome/Capture screen behavior plus camera hardware selection, see
/// dslrBooth's Screen Editor settings panels and its Camera Settings screen.
/// EnableWebcams mirrors dslrBooth's "if disabled, only Canon/Nikon are used" --
/// it gates the camera bridge's DSLR-then-webcam fallback (see
/// BoothCompositionRoot.EnsureCameraBridgeRunning), same effect as the existing
/// PHOTOBOOTH_REQUIRE_DSLR env var but per-event instead of machine-wide.
/// WebcamResolutionQuality is a 0-100 slider value (0 = fastest framerate, 100 =
/// highest quality) -- interpretation is left to whichever webcam capture path
/// reads it, same "store the admin's choice, let the consuming service decide
/// what to do with it" pattern PrintSharpening already uses. AudioInputDeviceName
/// is null for "use the system default device" (see AudioInputDevices.EnumerateNames).
/// PoseStripPosition is one of "Top"/"Bottom"/"Left"/"Right" -- which edge of the
/// Capture screen shows the strip of already-taken shots while a guest poses for
/// more (see ScreenTemplateEditorWindow's Capture settings), only ever seen for a
/// true multi-pose template (PrintTemplate.RequiredPhotoCount > 1), same gate
/// KioskViewModel.ShowPoseProgress already uses.</summary>
public record ScreenSettings(
    bool BoothIconsEnabled = false, bool ShowLiveView = true, bool MirrorLiveView = true, int LiveViewRotation = 0,
    bool EnableWebcams = true, int WebcamResolutionQuality = 70, string? AudioInputDeviceName = null,
    string PoseStripPosition = "Bottom")
{
    public static ScreenSettings Default { get; } = new();
}

/// <summary>Beauty filter / filters / post-processing / stickers / watermark,
/// see dslrBooth's Effects &amp; Stickers screen. BeautyFilterEnabled and
/// BeautyFilterAlsoDuringCountdown are stored but not yet consumed anywhere --
/// real skin smoothing needs face detection (IPhotoFilterService's own doc
/// comment already flags this as separate, unbuilt work); nothing here
/// regresses that, it just lets the admin's choice round-trip instead of
/// being silently dropped. Filters itself IS real now (see PhotoFilterPreset/
/// GdiFilterPresetService, BoothStateMachine's FilterPicker step): FiltersMode
/// picks Ask (guest chooses, see BoothState.FilterPicker) vs Auto (first
/// enabled preset applied silently), and EnabledFilterPresetIds (below) is
/// which presets are offered. PostProcessing/Stickers/Watermark are also real:
/// see BoothStateMachine's capture step (post-processing hook, FramePicker
/// gate, watermark composite via IFrameOverlayService -- the same
/// transparent-PNG-overlay operation a frame/sticker already is).
/// FiltersEnabled defaults to false, unlike StickersEnabled's true default --
/// Stickers is harmless-by-emptiness on a fresh booth (no frames added yet, so
/// FramePicker never shows regardless), but Filters always has all nine
/// built-in presets ready to go, so leaving it on by default would silently
/// add a brand-new guest-facing screen (FilterPicker) to every fresh install's
/// session. Same "new behavior-changing feature starts off" reasoning
/// PostProcessing/WatermarkEnabled/BeautyFilterEnabled already default to
/// false for.</summary>
public record EffectsSettings(
    bool BeautyFilterEnabled = false,
    string FiltersMode = "Ask",
    string? WatermarkImagePath = null,
    bool BeautyFilterAlsoDuringCountdown = false,
    bool FiltersEnabled = false,
    bool PostProcessingEnabled = false,
    string? PostProcessingApplicationPath = null,
    bool StickersEnabled = true,
    bool WatermarkEnabled = false)
{
    public static EffectsSettings Default { get; } = new();

    /// <summary>Comma-separated PhotoFilterPreset names the Filters grid offers
    /// (see PhotoFilterPresets.Parse). An init property, not a positional
    /// parameter, since its default (every built-in preset) isn't a compile-time
    /// constant -- same reasoning BoothSettings.Theme already established for
    /// the same problem.</summary>
    public string EnabledFilterPresetIds { get; init; } = PhotoFilterPresets.DefaultEnabledIds;
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

/// <summary>See dslrBooth's Sharing Settings screen. The three positional
/// channel toggles are the original fields; SMTP/Twilio delivery config
/// below was added later as init properties, not positional parameters,
/// same reasoning BoothTheme/EnabledFilterPresetIds already established for
/// the same problem -- every existing `new SharingSettings(...)` call site
/// keeps compiling unchanged with these silently defaulting.</summary>
public record SharingSettings(bool EmailEnabled = true, bool SmsEnabled = false, bool QrEnabled = true)
{
    public static SharingSettings Default { get; } = new();

    // Real SMTP delivery config -- see SmtpEmailDeliveryService, which reads
    // these fresh on every send (via IBoothSettingsProvider), same "admin's
    // change takes effect for the next guest" reasoning every other setting
    // in this file already follows.
    public string EmailFromAddress { get; init; } = "";
    public string EmailSubject { get; init; } = "Here is your photo";
    public string EmailSmtpHost { get; init; } = "";
    public int EmailSmtpPort { get; init; } = 587;
    public string EmailSmtpUsername { get; init; } = "";
    public bool EmailUseSsl { get; init; } = true;

    /// <summary>DPAPI-protected at rest (see SecretProtector) -- a real
    /// mail-account password, unlike every other field on this record.
    /// AdminWindow encrypts before saving; SmtpEmailDeliveryService decrypts
    /// right before connecting, never holding the plain value longer than
    /// one send.</summary>
    public string EmailSmtpPasswordProtected { get; init; } = "";

    // Real Twilio SMS delivery config -- see TwilioSmsDeliveryService.
    public string TwilioAccountSid { get; init; } = "";
    public string TwilioFromNumber { get; init; } = "";

    /// <summary>DPAPI-protected at rest, same reasoning as
    /// EmailSmtpPasswordProtected -- a Twilio auth token is a real secret.</summary>
    public string TwilioAuthTokenProtected { get; init; } = "";
}

/// <summary>Per-stage audio/video attendant cues, see dslrBooth's Virtual Attendant screen
/// (BUILD_PLAN.md Phase 6). Randomize is a fixed bool per stage rather than a
/// Dictionary&lt;string,bool&gt; -- BoothState's cue-worthy stages are a small, fixed set
/// (Consent/Countdown/Capturing/Reviewing/Printing/Complete), same reasoning
/// ScreenSettings/EffectsSettings use fixed properties instead of a bag of flags.</summary>
public record VirtualAttendantSettings(
    bool Enabled = false,
    string Style = "Friendly",
    bool RandomizeConsent = false,
    bool RandomizeCountdown = false,
    bool RandomizeCapturing = false,
    bool RandomizeReviewing = false,
    bool RandomizePrinting = false,
    bool RandomizeComplete = false)
{
    public static VirtualAttendantSettings Default { get; } = new();
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
