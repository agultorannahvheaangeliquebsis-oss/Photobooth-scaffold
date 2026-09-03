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
    // Default true, not false: this used to have no on-screen icon UI to gate
    // (see BUILD_PLAN.md's "BoothIconsEnabled has no on-screen icon UI to
    // gate" note) so every existing install's stored value -- almost always
    // the schema's own DEFAULT 0 -- was never a real admin choice, just an
    // inert leftover. Now that it actually hides the Welcome mode tiles (see
    // KioskWindow's WelcomeIconsGroup), defaulting a *new* record to true
    // keeps that behavior change from silently hiding mode selection for
    // anyone who never touched this checkbox; Script0023's migration
    // one-time-backfills every existing Location row to match.
    bool BoothIconsEnabled = true, bool ShowLiveView = true, bool MirrorLiveView = true, int LiveViewRotation = 0,
    bool EnableWebcams = true, int WebcamResolutionQuality = 70, string? AudioInputDeviceName = null,
    string PoseStripPosition = "Bottom")
{
    public static ScreenSettings Default { get; } = new();

    // ===================== Welcome screen =====================
    // See ScreenTemplateEditorWindow's Welcome settings panel / dslrBooth's
    // own Welcome Screen Settings panel.
    /// <summary>Exact DeviceName (see PtpCameraDevices.ListAsync/the camera
    /// bridge's LIST_CAMERAS command) of the camera AdminWindow's Camera
    /// Settings picker last had selected -- null means "auto-detect" (the
    /// bridge's original DSLR-then-webcam fallback, see
    /// BoothCompositionRoot.EnsureCameraBridgeRunning). Only takes effect the
    /// next time the bridge process starts fresh or the picker issues a live
    /// SELECT_CAMERA, same "next fresh launch" caveat EnableWebcams' own doc
    /// comment already covers for that same external process.</summary>
    public string? CameraDeviceName { get; init; }

    /// <summary>When MirrorLiveView is on, whether the SAVED photo also gets
    /// flipped to match the mirrored preview the guest actually saw (true, the
    /// default -- "what you see is what you get", same as dslrBooth's "Save
    /// Mirrored Photos"), or whether the saved file is instead flipped back to
    /// the camera's true-to-life orientation on capture (false). No effect
    /// when MirrorLiveView itself is off -- there's no mirrored preview to
    /// match in the first place. See BoothStateMachine's capture step /
    /// GdiPhotoMirrorService.</summary>
    public bool SaveMirroredPhotos { get; init; } = true;

    public bool BoothIconLabelsEnabled { get; init; } = true;

    // Booth Icons group -- the Photo/GIF/Boomerang/Video mode tiles. Each
    // tile's own enabled flag; BoothIconsEnabled/BoothIconLabelsEnabled above
    // are the group-wide enable/show-labels switches IconGroupLayout would
    // otherwise carry (see ScreenTemplateEditorWindow's WelcomeIconsLayout
    // property, which assembles all of these into one IconGroupLayout for the
    // drag/layout/align editor code).
    public bool WelcomePhotoIconEnabled { get; init; } = true;
    public bool WelcomeGifIconEnabled { get; init; } = true;
    public bool WelcomeBoomerangIconEnabled { get; init; } = true;
    public bool WelcomeVideoIconEnabled { get; init; } = true;

    /// <summary>Percent-of-canvas anchor for the Booth Icons group's top-left
    /// corner (see IconGroupLayout). 0.27, not 0.5: XPercent anchors the
    /// group's left edge, not its center, so 0.5 would start the ~45%-wide
    /// four-tile row just right of screen-center rather than centering it
    /// (confirmed visually on a 1920x1080 kiosk render) -- 0.27 approximates
    /// where the old fixed, actually-centered StackPanel placed it. Only ever
    /// a first-run default; an admin can drag the group anywhere from
    /// ScreenTemplateEditorWindow's DESIGN tab.</summary>
    public double WelcomeIconsPositionXPercent { get; init; } = 0.27;
    public double WelcomeIconsPositionYPercent { get; init; } = 0.72;
    public string WelcomeIconsLayout { get; init; } = IconGroupLayout.RowLayout;
    public string WelcomeIconsAlignment { get; init; } = IconGroupLayout.CenterAlignment;

    /// <summary>Camera preview behind the Welcome screen's own elements --
    /// distinct from Capture's ShowLiveView (a different screen), off by
    /// default same as dslrBooth's own Welcome panel.</summary>
    public bool WelcomeShowLiveView { get; init; }

    /// <summary>Renders the live camera feed inside whichever photo
    /// placeholder(s) the active print template defines, instead of a plain
    /// background feed. Stored and round-tripped like BeautyFilterEnabled/
    /// PostProcessingEnabled elsewhere in this file -- actually compositing
    /// the feed into a template placeholder is unbuilt rendering work, not a
    /// settings-plumbing problem, so this just lets the admin's choice
    /// persist rather than being silently dropped.</summary>
    public bool LiveTemplatePreview { get; init; }

    /// <summary>One of "Fill Screen With Cropping" / "Fit Screen" / "Stretch
    /// To Fill" -- how WelcomeShowLiveView's feed is scaled to the screen.
    /// Interpretation is left to whichever view renders it, same "store the
    /// admin's choice, let the consuming service decide" pattern
    /// WebcamResolutionQuality above already uses.</summary>
    public string StretchLiveView { get; init; } = "Fill Screen With Cropping";

    public bool BrowseButtonEnabled { get; init; } = true;
    public bool ChooseTemplateEnabled { get; init; }

    /// <summary>Looping video played before the Welcome screen appears (see
    /// ScreenTemplateEditorWindow's file picker). Null means no video.</summary>
    public string? StartScreenVideoPath { get; init; }

    /// <summary>0-100. How visible the always-present admin-unlock tap
    /// target is over the Welcome screen -- see KioskWindow's own unlock
    /// gesture.</summary>
    public int UnlockButtonOpacityPercent { get; init; } = 10;

    // Session trigger -- which guest inputs start a new session from the
    // Welcome screen (see KioskViewModel.CanStartSession's input handling).
    public bool SessionTriggerTouchScreen { get; init; } = true;
    public bool SessionTriggerF13 { get; init; }
    public bool SessionTriggerKeys { get; init; } = true;

    /// <summary>Lets a guest scan a QR code to drive the session from their
    /// own phone camera app -- a separate, not-yet-built control channel
    /// from RemoteControlEnabled's own loopback HTTP listener (see
    /// BoothSettings.RemoteControlEnabled), so this stores the admin's
    /// choice without yet wiring a phone-facing endpoint.</summary>
    public bool GuestQrCodeEnabled { get; init; }

    /// <summary>#RRGGBB behind the Welcome screen, and an optional image
    /// layered on top of it (see KioskWindow's IdleScreen Background/first
    /// child Image, and ScreenTemplateEditorWindow's WelcomeChromeLayer).
    /// "#17181A" matches KioskDark.xaml's previously-fixed KioskCanvasBrush,
    /// so an existing install renders identically until an admin changes
    /// this.</summary>
    public string WelcomeBackgroundColorHex { get; init; } = "#17181A";
    public string? WelcomeBackgroundImagePath { get; init; }

    // ===================== Capture screen =====================
    // Beyond ShowLiveView/MirrorLiveView/LiveViewRotation/PoseStripPosition
    // above -- see ScreenTemplateEditorWindow's Capture settings panel.
    public bool CropLiveView { get; init; } = true;
    public bool AutoTriggerCamera { get; init; } = true;
    public bool FlashScreenWhite { get; init; } = true;
    public bool ShowCancelButton { get; init; } = true;

    /// <summary>Percent-of-canvas anchor for the Cancel button's top-left
    /// corner (see IconGroupLayout; a single-item group, so Layout/Alignment
    /// don't apply). Defaults to roughly where the old fixed
    /// VerticalAlignment="Bottom" HorizontalAlignment="Center" placement put
    /// it, so an existing install doesn't visibly jump.</summary>
    public double CaptureCancelButtonPositionXPercent { get; init; } = 0.5;
    public double CaptureCancelButtonPositionYPercent { get; init; } = 0.93;

    /// <summary>#RRGGBB. Purely cosmetic -- the countdown overlay's own ring/
    /// number color (see KioskWindow's countdown rendering).</summary>
    public string CountdownColorHex { get; init; } = "#2ED9A0";

    /// <summary>Whether the strip of already-taken photos (positioned by
    /// PoseStripPosition) is shown at all.</summary>
    public bool PhotoThumbnailsEnabled { get; init; } = true;

    /// <summary>0-100 background opacity for the thumbnail strip's backing
    /// panel (see PhotoThumbnailsEnabled/PoseStripPosition above) -- 0 leaves
    /// just the slot images/numbers floating over the live feed, 100 is a
    /// fully opaque panel.</summary>
    public int PoseStripBackgroundOpacityPercent { get; init; } = 45;

    /// <summary>#RRGGBB border drawn around whichever slot is the pose
    /// currently being captured, so a guest mid-session can tell which shot
    /// is "live" among the already-taken thumbnails. Defaults to
    /// CountdownColorHex's own default so the two read as one accent color
    /// out of the box.</summary>
    public string PoseStripActiveBorderColorHex { get; init; } = "#2ED9A0";

    /// <summary>Whether an empty (not-yet-captured) slot shows its pose
    /// number (1, 2, 3...) as a placeholder, or stays blank until filled.</summary>
    public bool PoseStripShowPlaceholderNumbers { get; init; } = true;

    /// <summary>Shown while the camera auto-focuses right after the
    /// countdown ends, before FlashScreenWhite/capture. Null means no
    /// image (blank/last-frame passthrough).</summary>
    public string? SayCheeseImagePath { get; init; }

    /// <summary>Same idea as WelcomeBackgroundColorHex/ImagePath above, for
    /// the Countdown+Capture screens (KioskWindow's CountdownScreen and
    /// CaptureScreen both map to this editor's single "Capture" tab). Only
    /// visible where the live camera feed doesn't already cover it (i.e.
    /// when ShowLiveView is off), same layering as the existing scrim.</summary>
    public string CaptureBackgroundColorHex { get; init; } = "#17181A";
    public string? CaptureBackgroundImagePath { get; init; }

    /// <summary>Seconds BoothStateMachine's Reviewing state dwells before
    /// moving on to Payment/Printing -- "guest sees the shot before it
    /// prints." Was a hardcoded 2 until this setting existed; the default
    /// keeps that exact behavior.</summary>
    public int ReviewSeconds { get; init; } = 2;

    // ===================== Sharing screen =====================
    // See ScreenTemplateEditorWindow's Sharing settings panel, which
    // previously had no settings at all.
    public bool SkipSharingScreen { get; init; }
    public bool ShowDoneButton { get; init; } = true;

    /// <summary>Same idea as WelcomeBackgroundColorHex/ImagePath above, for
    /// KioskWindow's ReviewScreen.</summary>
    public string SharingBackgroundColorHex { get; init; } = "#17181A";
    public string? SharingBackgroundImagePath { get; init; }

    /// <summary>One of "Custom" (icons placed via the Design tab's canvas,
    /// the only layout this build's canvas-based Sharing screen actually
    /// supports) or a named preset layout dslrBooth also offers (e.g.
    /// "Bottom Row", "Grid") that this build doesn't render differently yet
    /// -- stored so the choice round-trips, same "not yet consumed"
    /// reasoning as BeautyFilterEnabled elsewhere in this file.</summary>
    public string SharingIconsLocation { get; init; } = "Custom";

    /// <summary>Group-wide enable for the QR/Email/SMS/Print icon row (see
    /// IconGroupLayout) -- distinct from each channel's own EmailEnabled/
    /// SmsEnabled/QrEnabled/PrintEnabled (SharingSettings, edited via the
    /// Sharing Settings section, not this editor): those gate whether a
    /// channel works at all, this just hides the whole row on the Sharing
    /// screen while leaving every channel's own setting untouched.</summary>
    public bool SharingIconsGroupEnabled { get; init; } = true;

    /// <summary>Percent-of-canvas anchor for the icon row's top-left corner.
    /// Defaults to roughly where SharingChromeQrBox/EmailRow/SmsRow already
    /// sit in the mockup (see ScreenTemplateEditorWindow.xaml's Sharing chrome
    /// mockup), so an existing install doesn't visibly jump.</summary>
    public double SharingIconsPositionXPercent { get; init; } = 0.56;
    public double SharingIconsPositionYPercent { get; init; } = 0.32;
    public string SharingIconsLayout { get; init; } = IconGroupLayout.ColumnLayout;
    public string SharingIconsAlignment { get; init; } = IconGroupLayout.StartAlignment;

    public bool SharingTextLabelsEnabled { get; init; } = true;
    public int FinalScreenTimeoutSeconds { get; init; } = 30;
    public bool ShowOriginalPhotos { get; init; } = true;
    public bool ShowRetakeButton { get; init; }
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

    /// <summary>Posting straight to Twitter/X needs an app registration and
    /// OAuth flow this build doesn't implement -- stored so the admin's
    /// channel choice round-trips (see the Sharing Settings section's own
    /// Email/SMS/QR toggles above), same "not yet consumed" reasoning
    /// EffectsSettings.BeautyFilterEnabled already documents.</summary>
    public bool TwitterEnabled { get; init; }

    /// <summary>Lets a guest trigger a print straight from the Sharing
    /// screen icon row -- distinct from PrintOptions.ShowPrintButton, which
    /// is the always-on-screen print button during the review step.</summary>
    public bool PrintEnabled { get; init; }

    /// <summary>When to run vendo-mode payment -- "SharingScreen" (default,
    /// after the guest sees their photo; BoothState.Payment runs between
    /// Reviewing and Printing, same as before this setting existed) or
    /// "StartScreen" (before the guest can begin a session at all;
    /// BoothState.PrePayment runs before FramePicker/Consent instead). Same
    /// two options dslrBooth's own "Request payment" dropdown offers. Moot
    /// in event mode, which never charges regardless of this setting.</summary>
    public string PaymentTiming { get; init; } = "SharingScreen";

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
