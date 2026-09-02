using Photobooth.Core;

namespace Photobooth.Tests;

public class BoothStateMachineTests
{
    [Fact]
    public async Task RunSessionAsync_HappyPath_RecordsSessionPrintAndPayment()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Equal(
            new[]
            {
                BoothState.Consent, BoothState.Countdown, BoothState.Capturing, BoothState.Reviewing,
                BoothState.Printing, BoothState.Complete, BoothState.Guestbook, BoothState.Feedback, BoothState.Idle,
            },
            states);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.NotNull(machine.LastCapturedImagePath);
        Assert.True(File.Exists(machine.LastCapturedImagePath));
        // Reviewing/printing/uploading all see the branded path, not the
        // camera's raw output -- confirms branding runs before anything
        // downstream reads LastCapturedImagePath.
        Assert.Contains("_branded", machine.LastCapturedImagePath);
        // Glam Booth mode is off by default (MockBoothSettingsProvider's
        // default matches the schema's own column default), so the filter
        // step shouldn't have run at all here.
        Assert.DoesNotContain("_glam", machine.LastCapturedImagePath);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal("event", createdSession.Mode);
        int sessionId = createdSession.SessionId;

        Assert.Equal(sessionId, Assert.Single(sessions.CompletedSessionIds));
        Assert.Empty(sessions.FailedSessionIds);
        Assert.Empty(sessions.AbandonedSessionIds);

        var print = Assert.Single(sessions.RecordedPrints);
        Assert.Equal(sessionId, print.SessionId);
        Assert.Equal(machine.LastCapturedImagePath, print.FilePath);

        var payment = Assert.Single(sessions.RecordedPayments);
        Assert.Equal(sessionId, payment.SessionId);
        Assert.Equal(0m, payment.Amount);
        Assert.Equal("free_event", payment.Method);

        var recordedConsent = Assert.Single(sessions.RecordedConsents);
        Assert.Equal(sessionId, recordedConsent.SessionId);
        Assert.True(recordedConsent.DisclaimerAccepted);
        Assert.True(recordedConsent.EmailOptIn);
        Assert.Equal("guest@example.com", recordedConsent.Email);
        Assert.NotNull(machine.LastConsent);
        Assert.True(machine.LastConsent!.DisclaimerAccepted);

        // The guest opted in during Consent (MockConsentService's default),
        // so the background upload finishing should have emailed them --
        // same generous timing margin as the upload-completes-in-time
        // comment on the offline-queue test below.
        var sentEmail = Assert.Single(email.SentEmails);
        Assert.Equal("guest@example.com", sentEmail.ToEmail);
        Assert.Equal(machine.LastPhotoUrl, sentEmail.PhotoUrl);

        // MockFeedbackService defaults to a 5-star rating with no comment --
        // confirms the Feedback state's outcome actually gets recorded.
        var recordedFeedback = Assert.Single(sessions.RecordedFeedback);
        Assert.Equal(sessionId, recordedFeedback.SessionId);
        Assert.Equal(5, recordedFeedback.Rating);
        Assert.Null(recordedFeedback.Comment);
    }

    [Fact]
    public async Task RunSessionAsync_GuestSkipsFeedback_RecordsNoFeedbackRow()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var feedbackService = new MockFeedbackService { SkipNext = true };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), feedbackService, new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // The Feedback state still shows -- the guest just gave nothing
        // worth a row for, same "state runs either way, recording is
        // conditional" shape Consent's decline path established.
        Assert.Contains(BoothState.Feedback, states);
        Assert.Empty(sessions.RecordedFeedback);
    }

    [Fact]
    public async Task RunSessionAsync_GlamModeEnabledInSettings_AppliesFilterBeforeBranding()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: true, PrintTemplate: PrintTemplate.Default) { Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 } } };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        Assert.NotNull(machine.LastCapturedImagePath);
        // Filter runs first, so "_glam" appears before "_branded" in the
        // final filename -- proves the ordering, not just that both ran.
        int glamIndex = machine.LastCapturedImagePath!.IndexOf("_glam", StringComparison.Ordinal);
        int brandedIndex = machine.LastCapturedImagePath.IndexOf("_branded", StringComparison.Ordinal);
        Assert.True(glamIndex >= 0 && brandedIndex >= 0 && glamIndex < brandedIndex);
    }

    [Fact]
    public async Task RunSessionAsync_GreenScreenEnabledWithBackground_AppliesItBeforeGlamFilterAndBranding()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: true, PrintTemplate: PrintTemplate.Default)
            {
                GreenScreen = new GreenScreenSettings(Enabled: true, BackgroundImagePath: "./backgrounds/beach.jpg"),
                Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 },
            },
        };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        Assert.NotNull(machine.LastCapturedImagePath);
        // Green screen runs first, so "_greenscreen" appears before both
        // "_glam" and "_branded" in the final filename -- proves the
        // ordering, not just that all three ran.
        int greenScreenIndex = machine.LastCapturedImagePath!.IndexOf("_greenscreen", StringComparison.Ordinal);
        int glamIndex = machine.LastCapturedImagePath.IndexOf("_glam", StringComparison.Ordinal);
        int brandedIndex = machine.LastCapturedImagePath.IndexOf("_branded", StringComparison.Ordinal);
        Assert.True(greenScreenIndex >= 0 && glamIndex >= 0 && brandedIndex >= 0
            && greenScreenIndex < glamIndex && glamIndex < brandedIndex);
    }

    [Fact]
    public async Task RunSessionAsync_GreenScreenEnabledButNoBackgroundConfigured_SkipsGreenScreen()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default)
            {
                // Enabled with nothing to composite against yet -- e.g. an
                // admin turned the toggle on before picking a background.
                GreenScreen = new GreenScreenSettings(Enabled: true, BackgroundImagePath: null),
                Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 },
            },
        };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        Assert.NotNull(machine.LastCapturedImagePath);
        Assert.DoesNotContain("_greenscreen", machine.LastCapturedImagePath);
    }

    [Fact]
    public async Task RunSessionAsync_CustomCountdownInSettings_FiresThatManyCountdownTicks()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 5, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default) { Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 } } };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var countdownTicks = new List<int>();
        machine.CountdownTick += tick => countdownTicks.Add(tick);

        await machine.RunSessionAsync();

        // An admin set this booth's countdown to 5 seconds instead of the
        // schema default of 3 -- confirms BoothStateMachine actually reads
        // that value rather than a hardcoded constant.
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, countdownTicks);
    }

    [Fact]
    public async Task RunSessionAsync_CustomPrintTemplateInSettings_PassesItToThePrinter()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var stripTemplate = new PrintTemplate("Strip", WidthInches: 2, HeightInches: 6, StripCopies: 2);
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: stripTemplate) { Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 } } };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        // An admin switched this booth to a 2x6 strip template instead of the
        // default single 4x6 -- confirms BoothStateMachine reads the current
        // settings' PrintTemplate and actually hands it to IPrinterService,
        // not just a hardcoded default.
        Assert.Equal(stripTemplate, Assert.Single(printer.PrintedTemplates));
    }

    [Fact]
    public async Task RunSessionAsync_EmailOptInFalse_NoEmailSent()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService { SimulateEmailOptIn = false };
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        Assert.NotNull(machine.LastPhotoUrl);
        Assert.Empty(email.SentEmails);
    }

    [Fact]
    public async Task RunSessionAsync_ConsentDeclined_AbandonsSessionWithoutCaptureOrPrint()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService { DeclineNext = true };
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // Declining is a legitimate choice, not an error -- no Countdown/
        // Capturing/etc., and the machine goes straight back to Idle.
        Assert.Equal(new[] { BoothState.Consent, BoothState.Idle }, states);
        Assert.Null(machine.LastCapturedImagePath);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.AbandonedSessionIds));
        Assert.Empty(sessions.CompletedSessionIds);
        Assert.Empty(sessions.FailedSessionIds);
        Assert.Empty(sessions.RecordedPrints);
        Assert.Empty(sessions.RecordedPayments);
        Assert.Empty(email.SentEmails);

        var recordedConsent = Assert.Single(sessions.RecordedConsents);
        Assert.False(recordedConsent.DisclaimerAccepted);
        Assert.False(recordedConsent.EmailOptIn);
        Assert.Null(recordedConsent.Email);

        // DeclineNext resets itself after firing, so a booth doesn't get
        // stuck abandoning every session after one guest declines.
        Assert.False(consent.DeclineNext);
    }

    [Fact]
    public async Task RunSessionAsync_CaptureFails_RecordsFailureAndSkipsPrintAndPayment()
    {
        var camera = new MockCameraService { FailNextCapture = true };
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        string? error = null;
        machine.ErrorOccurred += message => error = message;

        await machine.RunSessionAsync();

        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.NotNull(error);
        Assert.Null(machine.LastCapturedImagePath);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.FailedSessionIds));
        Assert.Empty(sessions.CompletedSessionIds);
        Assert.Empty(sessions.RecordedPrints);
        Assert.Empty(sessions.RecordedPayments);
        Assert.Empty(email.SentEmails);

        // FailNextCapture resets itself after firing, so a booth doesn't get
        // stuck failing every session after one simulated failure.
        Assert.False(camera.FailNextCapture);
    }

    [Fact]
    public async Task RunSessionAsync_CanceledAfterSessionCreation_AbandonsSessionAndClearsLastSession()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(CountdownSeconds: 0, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default)
            {
                Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 },
            },
        };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");
        using var cancellation = new CancellationTokenSource();
        machine.StateChanged += state =>
        {
            if (state == BoothState.Capturing)
            {
                cancellation.Cancel();
            }
        };

        await machine.RunSessionAsync(cancellation.Token);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.AbandonedSessionIds));
        Assert.Empty(sessions.CompletedSessionIds);
        Assert.Empty(sessions.FailedSessionIds);
        Assert.Null(machine.LastSessionId);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
    }

    [Fact]
    public async Task RunSessionAsync_VendoMode_RunsPaymentBeforePrintingAndRecordsPaidAmount()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "vendo");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Equal(
            new[]
            {
                BoothState.Consent, BoothState.Countdown, BoothState.Capturing, BoothState.Reviewing,
                BoothState.Payment, BoothState.Printing, BoothState.Complete, BoothState.Guestbook, BoothState.Feedback, BoothState.Idle,
            },
            states);
        Assert.NotNull(machine.PaymentQrPng);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal("vendo", createdSession.Mode);

        var recordedPayment = Assert.Single(sessions.RecordedPayments);
        Assert.Equal(createdSession.SessionId, recordedPayment.SessionId);
        Assert.Equal(150m, recordedPayment.Amount);
        Assert.Equal("qr_gcash", recordedPayment.Method);
    }

    [Fact]
    public async Task RunSessionAsync_VendoModeWithCardReader_HasNoQrCodeAndRecordsCardPayment()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockCardReaderPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "vendo");

        await machine.RunSessionAsync();

        // Proves IPaymentService actually generalizes beyond "generate a QR
        // code" -- a card reader has nothing to scan.
        Assert.Null(machine.PaymentQrPng);
        Assert.NotNull(machine.PaymentInstructions);
        Assert.Contains("card", machine.PaymentInstructions!, StringComparison.OrdinalIgnoreCase);

        var recordedPayment = Assert.Single(sessions.RecordedPayments);
        Assert.Equal(150m, recordedPayment.Amount);
        Assert.Equal("card", recordedPayment.Method);
    }

    [Fact]
    public async Task RunSessionAsync_VendoPaymentDeclined_RecordsFailureAndSkipsPrint()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockCardReaderPaymentService { DeclineNext = true };
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "vendo");

        string? error = null;
        machine.ErrorOccurred += message => error = message;

        await machine.RunSessionAsync();

        // A declined payment is a failure, not a clean opt-out like a
        // declined disclaimer -- the guest already went through Countdown
        // and Capturing expecting to get a print out of this.
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.NotNull(error);

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.FailedSessionIds));
        Assert.Empty(sessions.CompletedSessionIds);
        Assert.Empty(sessions.RecordedPrints);
        Assert.Empty(sessions.RecordedPayments);

        // The guest didn't pay, so they shouldn't get a free digital copy
        // by email either -- even though the photo was already captured
        // and uploaded before the decline happened.
        Assert.Empty(email.SentEmails);
    }

    [Fact]
    public async Task RunSessionAsync_UploadFails_QueuesFileWithEmailInsteadOfLosingIt()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService { FailNextUpload = true };
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        // The full session (countdown + capture + review + print + complete
        // delays) takes far longer than the mock upload's simulated 1.5s
        // latency, so by the time RunSessionAsync returns the background
        // upload attempt has already failed and queued -- no extra wait needed.
        Assert.Null(machine.LastPhotoUrl);
        PendingUpload queued = Assert.Single(await uploadQueue.GetPendingAsync());
        Assert.Equal(machine.LastCapturedImagePath, queued.FilePath);
        // The guest opted in (MockConsentService's default) -- that email
        // rides along in the queue entry so a later successful retry can
        // still send it (see RetryQueuedUploadsAsync_UploadNowSucceeds... below).
        Assert.Equal("guest@example.com", queued.Email);
        // No email yet, though -- the upload hasn't actually succeeded.
        Assert.Empty(email.SentEmails);
    }

    [Fact]
    public async Task RunSessionAsync_VendoPaymentDeclinedAndUploadFails_QueuesFileWithoutEmail()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService { FailNextUpload = true };
        var paymentService = new MockCardReaderPaymentService { DeclineNext = true };
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "vendo");

        await machine.RunSessionAsync();

        // A declined payment throws before FinalizeUploadAsync's call site,
        // so the failed upload never gets queued at all here -- not queued
        // "without an email" by omission, just never reached. Guards against
        // reintroducing the payment-declined-still-gets-emailed bug via the
        // retry path instead of the direct path it was originally fixed on.
        Assert.Empty(await uploadQueue.GetPendingAsync());
    }

    [Fact]
    public async Task RetryQueuedUploadsAsync_UploadNowSucceeds_RemovesFileFromQueueAndEmailsTheGuest()
    {
        var uploadQueue = new MockPendingUploadQueue();
        await uploadQueue.EnqueueAsync("./captures/leftover_from_last_night.bmp", "guest@example.com");
        var email = new MockEmailDeliveryService();
        var services = new BoothServices(
            new MockCameraService(), new MockPrinterService(), new MockCloudUploadService(),
            new MockSessionRepository(), new MockQrPaymentService(), uploadQueue,
            new MockConsentService(), email, new MockPhotoBrandingService(),
            new MockPhotoFilterService(), new MockBoothSettingsProvider(),
            new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(),
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services);

        await machine.RetryQueuedUploadsAsync();

        Assert.Empty(await uploadQueue.GetPendingAsync());
        // This is the fix: a retry that succeeds now actually emails whoever
        // opted in when the original upload failed, instead of silently
        // never sending it.
        var sentEmail = Assert.Single(email.SentEmails);
        Assert.Equal("guest@example.com", sentEmail.ToEmail);
        Assert.Contains("leftover_from_last_night.bmp", sentEmail.PhotoUrl.ToString());
    }

    [Fact]
    public async Task RetryQueuedUploadsAsync_CalledConcurrentlyOnSameInstance_OnlyEmailsOnce()
    {
        // Reproduces a real bug found via Photobooth.ConsoleDemo: a
        // still-in-flight opportunistic retry from one session and a new
        // one fired by the next session both saw the same pending item
        // before either had claimed it, so the guest got emailed twice for
        // one photo. MockCloudUploadService's simulated 1.5s upload latency
        // gives both concurrent calls plenty of time to race before either
        // finishes.
        var uploadQueue = new MockPendingUploadQueue();
        await uploadQueue.EnqueueAsync("./captures/leftover.bmp", "guest@example.com");
        var email = new MockEmailDeliveryService();
        var services = new BoothServices(
            new MockCameraService(), new MockPrinterService(), new MockCloudUploadService(),
            new MockSessionRepository(), new MockQrPaymentService(), uploadQueue,
            new MockConsentService(), email, new MockPhotoBrandingService(),
            new MockPhotoFilterService(), new MockBoothSettingsProvider(),
            new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(),
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services);

        Task first = machine.RetryQueuedUploadsAsync();
        Task second = machine.RetryQueuedUploadsAsync();
        await Task.WhenAll(first, second);

        Assert.Single(email.SentEmails);
    }

    [Fact]
    public async Task RetryQueuedUploadsAsync_CalledConcurrentlyFromTwoInstancesSharingAQueue_OnlyEmailsOnce()
    {
        // The demo's actual failure mode, more precisely than the
        // same-instance test above: Photobooth.ConsoleDemo runs several
        // BoothStateMachine instances (one per gateway/mode combination)
        // that all share one BoothServices.UploadQueue, same as
        // `services with { Payment = cardPayment }` only overrides Payment.
        // A per-BoothStateMachine-instance lock wouldn't catch two
        // *different* instances racing on the same shared queue -- this is
        // why the fix lives in the queue's DequeueAllAsync, not in
        // BoothStateMachine itself.
        var uploadQueue = new MockPendingUploadQueue();
        await uploadQueue.EnqueueAsync("./captures/leftover.bmp", "guest@example.com");
        var email = new MockEmailDeliveryService();
        var services = new BoothServices(
            new MockCameraService(), new MockPrinterService(), new MockCloudUploadService(),
            new MockSessionRepository(), new MockQrPaymentService(), uploadQueue,
            new MockConsentService(), email, new MockPhotoBrandingService(),
            new MockPhotoFilterService(), new MockBoothSettingsProvider(),
            new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(),
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var firstMachine = new BoothStateMachine(services, mode: "event");
        var secondMachine = new BoothStateMachine(services, mode: "vendo");

        Task first = firstMachine.RetryQueuedUploadsAsync();
        Task second = secondMachine.RetryQueuedUploadsAsync();
        await Task.WhenAll(first, second);

        Assert.Single(email.SentEmails);
    }

    [Fact]
    public async Task RetryQueuedUploadsAsync_UploadNowSucceedsWithNoEmailOnFile_SendsNoEmail()
    {
        var uploadQueue = new MockPendingUploadQueue();
        await uploadQueue.EnqueueAsync("./captures/leftover_from_last_night.bmp", email: null);
        var email = new MockEmailDeliveryService();
        var services = new BoothServices(
            new MockCameraService(), new MockPrinterService(), new MockCloudUploadService(),
            new MockSessionRepository(), new MockQrPaymentService(), uploadQueue,
            new MockConsentService(), email, new MockPhotoBrandingService(),
            new MockPhotoFilterService(), new MockBoothSettingsProvider(),
            new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(),
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services);

        await machine.RetryQueuedUploadsAsync();

        Assert.Empty(await uploadQueue.GetPendingAsync());
        Assert.Empty(email.SentEmails);
    }

    [Fact]
    public async Task RetryQueuedUploadsAsync_StillFailing_LeavesFileQueued()
    {
        var uploadQueue = new MockPendingUploadQueue();
        await uploadQueue.EnqueueAsync("./captures/still_offline.bmp", "guest@example.com");
        var cloudUpload = new MockCloudUploadService { FailNextUpload = true };
        var services = new BoothServices(
            new MockCameraService(), new MockPrinterService(), cloudUpload,
            new MockSessionRepository(), new MockQrPaymentService(), uploadQueue,
            new MockConsentService(), new MockEmailDeliveryService(), new MockPhotoBrandingService(),
            new MockPhotoFilterService(), new MockBoothSettingsProvider(),
            new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(),
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services);

        await machine.RetryQueuedUploadsAsync();

        PendingUpload stillQueued = Assert.Single(await uploadQueue.GetPendingAsync());
        Assert.Equal("./captures/still_offline.bmp", stillQueued.FilePath);
    }

    [Fact]
    public async Task RunSessionAsync_FavoritedTemplatesConfigured_ShowsFramePickerBeforeConsentAndPrintsChosenTemplate()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Screen = settings.Settings.Screen with { ChooseTemplateEnabled = true } };
        var favoriteTemplate = new PrintTemplate("Single", 5, 7, 1) { Id = 1, Name = "Gold Border", IsFavorite = true };
        var templateLibrary = new MockPrintTemplateLibraryService { Templates = new List<PrintTemplate> { favoriteTemplate } };
        var templateSelection = new MockTemplateSelectionService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService())
        {
            TemplateLibrary = templateLibrary,
            TemplateSelection = templateSelection,
        };
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Contains(BoothState.FramePicker, states);
        // FramePicker is now the guest's very first interactive step -- before
        // Consent, well before Printing -- not a post-capture overlay pick.
        Assert.True(states.IndexOf(BoothState.FramePicker) < states.IndexOf(BoothState.Consent));
        Assert.True(states.IndexOf(BoothState.FramePicker) < states.IndexOf(BoothState.Printing));

        Assert.NotNull(machine.LastSelectedTemplate);
        Assert.Equal("Gold Border", machine.LastSelectedTemplate!.Name);

        // The guest's chosen template (5x7), not the location's default
        // (PrintTemplate.Default, 4x6), is what actually got printed.
        PrintTemplate printedTemplate = Assert.Single(printer.PrintedTemplates);
        Assert.Equal(5, printedTemplate.WidthInches);
        Assert.Equal(7, printedTemplate.HeightInches);
    }

    [Fact]
    public async Task RunSessionAsync_GuestSkipsTheLayoutChoice_DefaultTemplatePrinted()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Screen = settings.Settings.Screen with { ChooseTemplateEnabled = true } };
        var favoriteTemplate = new PrintTemplate("Single", 5, 7, 1) { Id = 1, Name = "Gold Border", IsFavorite = true };
        var templateLibrary = new MockPrintTemplateLibraryService { Templates = new List<PrintTemplate> { favoriteTemplate } };
        var templateSelection = new MockTemplateSelectionService { SkipNext = true };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService())
        {
            TemplateLibrary = templateLibrary,
            TemplateSelection = templateSelection,
        };
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // FramePicker still shows (there was a favorite to offer), but the
        // guest declined it, so the location's default template governs.
        Assert.Contains(BoothState.FramePicker, states);
        Assert.Null(machine.LastSelectedTemplate);
        PrintTemplate printedTemplate = Assert.Single(printer.PrintedTemplates);
        Assert.Equal(PrintTemplate.Default.WidthInches, printedTemplate.WidthInches);
    }

    [Fact]
    public async Task RunSessionAsync_NoFavoritedTemplates_SkipsFramePickerEntirely()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Screen = settings.Settings.Screen with { ChooseTemplateEnabled = true } };
        // Default MockPrintTemplateLibraryService.Templates is empty, matching
        // a fresh booth where nothing's been favorited yet.
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.DoesNotContain(BoothState.FramePicker, states);
        Assert.Null(machine.LastSelectedTemplate);
    }

    [Fact]
    public async Task RunSessionAsync_GuestRecordsAGuestbookMessage_RecordsAVideoRow()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var guestbookPrompt = new MockGuestbookPromptService();
        var videoGuestbook = new MockVideoGuestbookService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Contains(BoothState.Guestbook, states);
        // Guestbook comes after Complete (the print/upload/payment are all
        // already settled) and before Feedback.
        Assert.True(states.IndexOf(BoothState.Complete) < states.IndexOf(BoothState.Guestbook));
        Assert.True(states.IndexOf(BoothState.Guestbook) < states.IndexOf(BoothState.Feedback));

        var video = Assert.Single(sessions.RecordedGuestbookVideos);
        Assert.Equal(Assert.Single(sessions.CreatedSessions).SessionId, video.SessionId);
        Assert.True(video.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RunSessionAsync_GuestDeclinesTheGuestbookPrompt_RecordsNoVideoRow()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var guestbookPrompt = new MockGuestbookPromptService { SkipNext = true };
        var videoGuestbook = new MockVideoGuestbookService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // The Guestbook state still shows -- the guest just declined, same
        // "state runs either way, recording is conditional" shape Feedback's
        // skip path already established.
        Assert.Contains(BoothState.Guestbook, states);
        Assert.Empty(sessions.RecordedGuestbookVideos);
    }

    [Fact]
    public async Task RunSessionAsync_GuestbookRecordingFails_SessionStillCompletesNormally()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var guestbookPrompt = new MockGuestbookPromptService();
        var videoGuestbook = new MockVideoGuestbookService { FailNextStart = true };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // A guestbook capture failure is wrapped in its own try/catch, same
        // as Feedback -- the session (already printed and paid for) should
        // never turn into an Error just because the optional guestbook
        // message failed to record.
        Assert.DoesNotContain(BoothState.Error, states);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.Empty(sessions.RecordedGuestbookVideos);
        Assert.Contains(BoothState.Feedback, states);
    }

    [Fact]
    public async Task RunSessionAsync_GifMode_CapturesConfiguredFrameCountAndComposesForward()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var gifComposer = new MockGifComposerService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Capture = new CaptureSettings(Mode: "GIF", FrameCount: 3, FrameDelayMs: 10) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), gifComposer, new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);
        var frameCaptures = new List<(int Frame, int Total, string Path)>();
        machine.FrameCaptured += (frame, total, path) => frameCaptures.Add((frame, total, path));

        await machine.RunSessionAsync();

        Assert.Equal(3, gifComposer.LastFrameCount);
        Assert.False(gifComposer.LastReversed);
        // Playback speed is independent of the capture-time FrameDelayMs (10ms above) --
        // GIF mode's sequence is forward-only, so 3000ms / 3 frames = 1000ms/frame, a fixed
        // ~3s total loop matching a standard Boomerang-style clip length regardless of how
        // fast the burst itself ran.
        Assert.Equal(1000, gifComposer.LastFrameDelayMs);
        Assert.NotNull(machine.LastCapturedImagePath);
        Assert.Contains("_gif", machine.LastCapturedImagePath);
        // FrameCaptured lets the UI show each just-captured frame the instant it
        // lands instead of freezing after the loop's single Capturing state
        // change -- see KioskViewModel.OnFrameCaptured.
        Assert.Equal(3, frameCaptures.Count);
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, frameCaptures.Select(f => (f.Frame, f.Total)));
        Assert.All(frameCaptures, f => Assert.False(string.IsNullOrEmpty(f.Path)));
        // GIF mode skips the single-still GDI+ pipeline entirely (see
        // BoothStateMachine's isBurstMode branch) -- confirms branding/
        // filter/frame-picker didn't run against the composed animation.
        Assert.DoesNotContain("_branded", machine.LastCapturedImagePath);
        Assert.DoesNotContain(BoothState.FramePicker, states);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.DoesNotContain(BoothState.Error, states);
    }

    [Fact]
    public async Task RunSessionAsync_BoomerangMode_ComposesReversed()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var gifComposer = new MockGifComposerService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Capture = new CaptureSettings(Mode: "Boomerang", FrameCount: 4, FrameDelayMs: 10) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), gifComposer, new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        await machine.RunSessionAsync();

        Assert.Equal(4, gifComposer.LastFrameCount);
        Assert.True(gifComposer.LastReversed);
        // Boomerang's sequence is forward + reversed-minus-both-ends: 2*4-2 = 6 frames, so
        // 3000ms / 6 = 500ms/frame -- same fixed ~3s total loop target as GIF mode, despite
        // this test's much faster 10ms capture-time FrameDelayMs.
        Assert.Equal(500, gifComposer.LastFrameDelayMs);
        Assert.NotNull(machine.LastCapturedImagePath);
        Assert.Contains("_boomerang", machine.LastCapturedImagePath);
    }

    [Fact]
    public async Task RunSessionAsync_VideoMode_RecordsAndSkipsPrintingEntirely()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var boothVideo = new MockBoothVideoService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Capture = new CaptureSettings(Mode: "Video", VideoDurationSeconds: 1) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), boothVideo, new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.NotNull(machine.LastCapturedImagePath);
        var recordedFile = Assert.Single(boothVideo.RecordedFiles);
        Assert.Equal(recordedFile, machine.LastCapturedImagePath);

        // No printable still exists for Video mode -- confirms the
        // Printing state, the printer call, and the Print row are all
        // skipped, not just that a print happened to succeed against a
        // video file.
        Assert.DoesNotContain(BoothState.Printing, states);
        Assert.DoesNotContain(BoothState.FramePicker, states);
        Assert.Empty(printer.PrintedTemplates);
        Assert.Empty(sessions.RecordedPrints);

        // Still earns the guest a "session completed" outcome and the
        // free_event payment row, same as Photo mode -- not printing isn't
        // the same as the session failing.
        Assert.Contains(BoothState.Complete, states);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.DoesNotContain(BoothState.Error, states);
        var payment = Assert.Single(sessions.RecordedPayments);
        Assert.Equal("free_event", payment.Method);
    }

    [Fact]
    public async Task RunSessionAsync_VirtualAttendantConfiguredForCountdownOnly_FiresCueOnlyForThatStage()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var attendant = new MockVirtualAttendantService { Settings = new VirtualAttendantSettings(Enabled: true) };
        attendant.ClipsByStage[BoothState.Countdown] = new List<AttendantClip> { new("./attendant/countdown.mp3", BoothState.Countdown) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), attendant, new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var cues = new List<AttendantClip>();
        machine.AttendantCueChanged += cues.Add;

        await machine.RunSessionAsync();

        // Cue firing is fire-and-forget inside SetState -- give the last one a
        // moment to complete before asserting, same generous timing margin the
        // upload-completes-in-time tests elsewhere in this file already use.
        await Task.Delay(200);

        var cue = Assert.Single(cues);
        Assert.Equal(BoothState.Countdown, cue.Stage);
        Assert.Equal("./attendant/countdown.mp3", cue.FilePath);
    }

    [Fact]
    public async Task RunSessionAsync_VirtualAttendantDisabled_NeverFiresACue()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var attendant = new MockVirtualAttendantService(); // Enabled defaults to false
        attendant.ClipsByStage[BoothState.Countdown] = new List<AttendantClip> { new("./attendant/countdown.mp3", BoothState.Countdown) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), attendant, new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var cues = new List<AttendantClip>();
        machine.AttendantCueChanged += cues.Add;

        await machine.RunSessionAsync();
        await Task.Delay(200);

        Assert.Empty(cues);
    }

    [Fact]
    public async Task RunSessionAsync_SurveyEnabledWithActiveQuestions_ShowsSurveyAndRecordsAnswers()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Survey = new SurveySettings(Enabled: true) };
        var survey = new MockSurveyService();
        survey.Questions.Add(new SurveyQuestion(1, "How did you hear about us?"));
        survey.SimulatedAnswers[1] = "Instagram";
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), survey);
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Contains(BoothState.Survey, states);
        // Survey runs after Feedback, before the machine returns to Idle.
        Assert.True(states.IndexOf(BoothState.Survey) > states.IndexOf(BoothState.Feedback));
        var recorded = Assert.Single(survey.RecordedResponses);
        var answer = Assert.Single(recorded.Answers);
        Assert.Equal(1, answer.SurveyQuestionId);
        Assert.Equal("Instagram", answer.Answer);
    }

    [Fact]
    public async Task RunSessionAsync_SurveyEnabledButNoActiveQuestions_SkipsSurveyEntirely()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Survey = new SurveySettings(Enabled: true) };
        var survey = new MockSurveyService(); // no questions configured -- empty table
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), survey);
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.DoesNotContain(BoothState.Survey, states);
        Assert.Empty(survey.RecordedResponses);
    }

    [Fact]
    public async Task RunSessionAsync_SurveyDisabledInSettings_SkipsSurveyEvenWithActiveQuestions()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider(); // Survey.Enabled defaults to false
        var survey = new MockSurveyService();
        survey.Questions.Add(new SurveyQuestion(1, "How did you hear about us?"));
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), survey);
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.DoesNotContain(BoothState.Survey, states);
        Assert.Empty(survey.RecordedResponses);
    }

    [Fact]
    public async Task RunSessionAsync_GuestSkipsSurvey_RecordsNoResponses()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        settings.Settings = settings.Settings with { Survey = new SurveySettings(Enabled: true) };
        var survey = new MockSurveyService { SkipNext = true };
        survey.Questions.Add(new SurveyQuestion(1, "How did you hear about us?"));
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), survey);
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // The Survey state still shows -- the guest just gave nothing worth
        // recording, same "state runs either way, recording is conditional"
        // shape Feedback's skip path already established.
        Assert.Contains(BoothState.Survey, states);
        Assert.Empty(survey.RecordedResponses);
    }

    // ---- Guest idle timeout (BUILD_PLAN.md Day 3) -------------------------
    // Every Mock*Service above simulates a realistic guest response delay
    // (300ms-2500ms, see each mock's own comment) rather than resolving
    // instantly. Passing a guestIdleTimeout shorter than that delay (instead
    // of adding a dedicated "never responds" hook to five different mocks)
    // deterministically exercises WithGuestIdleTimeoutAsync's timeout branch:
    // the guest's actual response still arrives later, but the state machine
    // has already moved on by the time it does.

    [Fact]
    public async Task RunSessionAsync_GuestWalksAwayDuringConsent_TimesOutAndAbandonsSession()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService(); // 500ms simulated delay
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event", guestIdleTimeout: TimeSpan.FromMilliseconds(50));

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // Same outcome as an explicit decline (MockConsentService.DeclineNext)
        // -- no countdown/capture/print, marked Abandoned not Error, since a
        // guest who never responds isn't a booth malfunction.
        Assert.Equal(new[] { BoothState.Consent, BoothState.Idle }, states);
        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.AbandonedSessionIds));
        Assert.Empty(sessions.FailedSessionIds);
        Assert.Empty(sessions.RecordedPrints);
        Assert.Empty(sessions.RecordedPayments);
    }

    [Fact]
    public async Task RunSessionAsync_GuestWalksAwayDuringVendoPayment_TimesOutAndFailsSession()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService(); // 2500ms simulated delay
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        // Between Consent's own 500ms simulated delay (must resolve normally --
        // this test is about Payment, not Consent) and the QR mock's 2500ms
        // simulated delay (must NOT resolve in time -- that's the timeout being
        // tested), so Consent passes through untouched and only Payment times out.
        var machine = new BoothStateMachine(services, mode: "vendo", guestIdleTimeout: TimeSpan.FromMilliseconds(800));

        string? error = null;
        machine.ErrorOccurred += message => error = message;

        await machine.RunSessionAsync();

        // Same outcome as an explicit decline (MockCardReaderPaymentService.DeclineNext)
        // -- a guest who never confirms payment shouldn't tie up the booth (or
        // get a free digital copy) any more than one who explicitly declined.
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.NotNull(error);
        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.FailedSessionIds));
        Assert.Empty(sessions.RecordedPrints);
        Assert.Empty(sessions.RecordedPayments);
        Assert.Empty(email.SentEmails);
    }

    [Fact]
    public async Task RunSessionAsync_GuestWalksAwayDuringFeedback_TimesOutAndRecordsNoFeedbackButSessionCompletes()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var settings = new MockBoothSettingsProvider();
        // Bumped well past the guestIdleTimeout below -- Consent's own fixed 500ms
        // delay is longer than Feedback's normal 300ms one, so a single shared
        // timeout can't sit strictly between them without also pushing Feedback's
        // own delay up first (otherwise a timeout short enough to catch Feedback
        // would catch Consent too, and long enough to spare Consent would never
        // catch Feedback).
        var feedback = new MockFeedbackService { SimulatedDelay = TimeSpan.FromSeconds(5) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), feedback, new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event", guestIdleTimeout: TimeSpan.FromMilliseconds(700));

        await machine.RunSessionAsync();

        // Unlike Consent/Payment, a stalled Feedback shouldn't fail or abandon
        // the session -- the photo's already captured, paid for, and printed
        // by this point (same reasoning the existing SkipNext path already
        // established), so the session should still complete normally.
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal(createdSession.SessionId, Assert.Single(sessions.CompletedSessionIds));
        Assert.Empty(sessions.RecordedFeedback);
    }

    [Fact]
    public async Task RunSessionAsync_TemplateWithThreePhotoSlots_CapturesThreeDistinctPosesAndPrintsAllThree()
    {
        var camera = new MockCameraService();
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var paymentService = new MockQrPaymentService();
        var sessions = new MockSessionRepository();
        var uploadQueue = new MockPendingUploadQueue();
        var consent = new MockConsentService();
        var email = new MockEmailDeliveryService();
        var branding = new MockPhotoBrandingService();
        var filter = new MockPhotoFilterService();
        var multiPoseTemplate = PrintTemplate.Default with
        {
            Elements = new[]
            {
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0, 0, 0.33, 1, PhotoIndex: 0),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0.33, 0, 0.33, 1, PhotoIndex: 1),
                new PrintTemplateElement(PrintTemplateElementKind.PhotoSlot, 0.66, 0, 0.34, 1, PhotoIndex: 2),
            },
        };
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: multiPoseTemplate)
            {
                Screen = ScreenSettings.Default with { FinalScreenTimeoutSeconds = 1 },
            },
        };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService(), new MockBoothVideoService(), new MockVirtualAttendantService(), new MockSurveyService());
        var machine = new BoothStateMachine(services, mode: "event");

        var poseChanges = new List<(int Pose, int Total)>();
        machine.PoseChanged += (pose, total) => poseChanges.Add((pose, total));

        await machine.RunSessionAsync();

        // Fired once per pose, before that pose's own Countdown -- the UI's
        // "Pose 2 of 4" progress relies on this sequence.
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, poseChanges);

        // Three distinct captured/processed poses, not the same one photo
        // reused three times -- the actual "true multi-pose" behavior.
        Assert.Equal(3, machine.LastCapturedImagePaths.Count);
        Assert.Equal(3, machine.LastCapturedImagePaths.Distinct().Count());
        Assert.All(machine.LastCapturedImagePaths, path => Assert.True(File.Exists(path)));

        // Every pose handed to the printer, in order, not just the last one.
        Assert.Equal(machine.LastCapturedImagePaths, Assert.Single(printer.PrintedImagePaths));
    }
}
