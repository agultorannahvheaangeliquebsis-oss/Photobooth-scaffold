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
    IVideoGuestbookService VideoGuestbook);
