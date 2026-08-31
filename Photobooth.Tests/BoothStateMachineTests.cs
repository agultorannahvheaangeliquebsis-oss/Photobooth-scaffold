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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), feedbackService, new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: true, PrintTemplate: PrintTemplate.Default) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 5, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var settings = new MockBoothSettingsProvider { Settings = new BoothSettings(CountdownSeconds: 3, GlamFilterEnabled: false, PrintTemplate: stripTemplate) };
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
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
            new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
        var machine = new BoothStateMachine(services);

        await machine.RetryQueuedUploadsAsync();

        PendingUpload stillQueued = Assert.Single(await uploadQueue.GetPendingAsync());
        Assert.Equal("./captures/still_offline.bmp", stillQueued.FilePath);
    }

    [Fact]
    public async Task RunSessionAsync_ActiveFramesConfigured_ShowsFramePickerAndAppliesChosenFrameBeforePrintingAndUpload()
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
        var frameLibrary = new MockFrameLibraryService { Frames = new List<FrameOption> { new(1, "Gold Border", "./frames/gold.png") } };
        var frameSelection = new MockFrameSelectionService();
        var frameOverlay = new MockFrameOverlayService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, frameLibrary, frameSelection, frameOverlay, new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Contains(BoothState.FramePicker, states);
        // FramePicker comes after Reviewing (the guest sees the raw shot
        // first) and before Printing (the frame has to be in the file that
        // actually gets printed).
        Assert.True(states.IndexOf(BoothState.Reviewing) < states.IndexOf(BoothState.FramePicker));
        Assert.True(states.IndexOf(BoothState.FramePicker) < states.IndexOf(BoothState.Printing));

        Assert.NotNull(machine.LastSelectedFrame);
        Assert.Equal("Gold Border", machine.LastSelectedFrame!.Name);
        Assert.Contains("_framed", machine.LastCapturedImagePath);

        // The framed (not pre-frame) path is what got printed and uploaded --
        // same "everything downstream sees the same final photo" invariant
        // branding/filter already established.
        var print = Assert.Single(sessions.RecordedPrints);
        Assert.Equal(machine.LastCapturedImagePath, print.FilePath);
        Assert.Contains("_framed", machine.LastPhotoUrl!.ToString());
    }

    [Fact]
    public async Task RunSessionAsync_GuestSkipsTheFrame_NoFrameApplied()
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
        var frameLibrary = new MockFrameLibraryService { Frames = new List<FrameOption> { new(1, "Gold Border", "./frames/gold.png") } };
        var frameSelection = new MockFrameSelectionService { SkipNext = true };
        var frameOverlay = new MockFrameOverlayService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, frameLibrary, frameSelection, frameOverlay, new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        // FramePicker still shows (there were frames to offer), but the
        // guest declined all of them.
        Assert.Contains(BoothState.FramePicker, states);
        Assert.Null(machine.LastSelectedFrame);
        Assert.DoesNotContain("_framed", machine.LastCapturedImagePath);
    }

    [Fact]
    public async Task RunSessionAsync_NoActiveFrames_SkipsFramePickerEntirely()
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
        // Default MockFrameLibraryService.Frames is empty, matching a fresh
        // Frame table nothing's been added to yet.
        var frameLibrary = new MockFrameLibraryService();
        var frameSelection = new MockFrameSelectionService();
        var frameOverlay = new MockFrameOverlayService();
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, frameLibrary, frameSelection, frameOverlay, new MockFeedbackService(), new MockGuestbookPromptService(), new MockVideoGuestbookService(), new MockGifComposerService());
        var machine = new BoothStateMachine(services, mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.DoesNotContain(BoothState.FramePicker, states);
        Assert.Null(machine.LastSelectedFrame);
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService());
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
        var services = new BoothServices(camera, printer, cloudUpload, sessions, paymentService, uploadQueue, consent, email, branding, filter, settings, new MockFrameLibraryService(), new MockFrameSelectionService(), new MockFrameOverlayService(), new MockFeedbackService(), guestbookPrompt, videoGuestbook, new MockGifComposerService());
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
}
