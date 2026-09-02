namespace Photobooth.Core;

/// <summary>
/// Bundles every service BoothStateMachine depends on. Introduced after the
/// constructor grew from 5 positional service parameters (Day 2) to 9
/// (after offline queueing, consent, email delivery, and photo branding
/// were each added one at a time) -- past a certain size, a positional
/// parameter list stops being self-documenting at the call site (nothing
/// stops two same-typed arguments from silently swapping), and adding a
/// 10th seam later only needs a new property here, not a signature change
/// at every constructor call across MainWindow, Photobooth.ConsoleDemo, and
/// every test.
/// </summary>
public record BoothServices(
    ICameraService Camera,
    IPrinterService Printer,
    ICloudUploadService CloudUpload,
    ISessionRepository Sessions,
    IPaymentService Payment,
    IPendingUploadQueue UploadQueue,
    IConsentService Consent,
    IEmailDeliveryService Email,
    IPhotoBrandingService Branding,
    IPhotoFilterService Filter,
    IBoothSettingsProvider Settings,
    IFrameLibraryService FrameLibrary,
    IFrameSelectionService FrameSelection,
    IFrameOverlayService FrameOverlay,
    IFeedbackService Feedback,
    IGuestbookPromptService GuestbookPrompt,
    IVideoGuestbookService VideoGuestbook,
    IGifComposerService GifComposer,
    IBoothVideoService BoothVideo,
    IVirtualAttendantService AttendantCue,
    ISurveyService Survey)
{
    /// <summary>SMS delivery for the guest sharing screen (see
    /// KioskViewModel.SendSmsAsync) -- an init property here, not a 22nd
    /// positional parameter, so every existing `new BoothServices(...)` call
    /// site (BoothStateMachineTests alone has 30+) keeps compiling unchanged
    /// with this silently defaulting to the mock, same reasoning
    /// BoothSettings.Theme/Capture/etc. already established for the same
    /// problem. No real gateway yet -- same "mock now, real vendor later"
    /// status IPaymentService/IConsentService have.</summary>
    public ISmsDeliveryService Sms { get; init; } = new MockSmsDeliveryService();

    /// <summary>Chroma-key compositing for the green screen feature (see
    /// BoothStateMachine's capture step) -- an init property here for the
    /// same reason Sms is: avoids a 23rd positional constructor parameter
    /// every existing call site would otherwise need to learn about.</summary>
    public IGreenScreenService GreenScreen { get; init; } = new MockGreenScreenService();

    /// <summary>Post-Processing hook for the Effects &amp; Stickers screen
    /// (see BoothStateMachine's capture step) -- an init property here for
    /// the same reason Sms/GreenScreen are.</summary>
    public IPostProcessingService PostProcessing { get; init; } = new MockPostProcessingService();

    /// <summary>Applies a PhotoFilterPreset's color grade (see BoothStateMachine's
    /// FilterPicker step) -- an init property here for the same reason Sms/
    /// GreenScreen/PostProcessing are.</summary>
    public IFilterPresetService FilterPreset { get; init; } = new MockFilterPresetService();

    /// <summary>Collects the guest's filter pick during FilterPicker -- an init
    /// property here for the same reason Sms/GreenScreen/PostProcessing are.</summary>
    public IFilterSelectionService FilterSelection { get; init; } = new MockFilterSelectionService();

    /// <summary>Reads the admin's uploaded custom .CUBE LUT filters (see
    /// FilterLibraryWindow's "Add Custom Filter" tile) -- an init property here
    /// for the same reason Sms/GreenScreen/PostProcessing are.</summary>
    public ICustomFilterLibraryService CustomFilterLibrary { get; init; } = new MockCustomFilterLibraryService();

    /// <summary>Applies a custom LUT's color grade (see BoothStateMachine's
    /// FilterPicker step) -- the CustomFilter/CustomFilterLibrary split mirrors
    /// FilterPreset/FilterSelection's own "apply the effect" vs "know what's
    /// offered" split.</summary>
    public ICustomFilterService CustomFilter { get; init; } = new MockCustomFilterService();

    /// <summary>Reads the booth's favorited saved print-layout templates (see
    /// BoothStateMachine's now-first FramePicker step) -- an init property here
    /// for the same reason Sms/GreenScreen/PostProcessing are. Replaces
    /// FrameLibrary as the source of the guest-facing "frame/layout" picker;
    /// FrameLibrary/FrameSelection stay above (still required, and still used
    /// by FrameOverlay's watermark compositing path) so every existing
    /// `new BoothServices(...)` call site keeps compiling unchanged.</summary>
    public IPrintTemplateLibraryService TemplateLibrary { get; init; } = new MockPrintTemplateLibraryService();

    /// <summary>Collects the guest's layout pick during FramePicker -- an init
    /// property here for the same reason TemplateLibrary is.</summary>
    public ITemplateSelectionService TemplateSelection { get; init; } = new MockTemplateSelectionService();
}
