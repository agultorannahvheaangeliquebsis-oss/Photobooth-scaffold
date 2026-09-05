using System.Diagnostics;
using Photobooth.Core;

namespace Photobooth.Tests;

/// <summary>
/// Regression cover for the failure paths a booth actually hits at an event,
/// as opposed to the happy paths BoothStateMachineTests already walks: an
/// admin setting that was never read, a guest who cancels mid-recording, a
/// printer that's out of paper, a guest who walks away from the payment
/// screen, and a queue file left half-written by a power cut. Every one of
/// these shipped green -- they're all "the code did something reasonable-
/// looking and nothing ever checked", which is exactly what a test suite is
/// for.
/// </summary>
public class SessionReliabilityTests
{
    /// <summary>Every mock BoothServices needs, with the pieces a given test
    /// wants to assert against passed in. Saves repeating the 21-argument
    /// positional constructor in every test below.</summary>
    private static BoothServices BuildServices(
        IBoothSettingsProvider settings,
        MockSessionRepository sessions,
        IPrinterService? printer = null,
        IPaymentService? payment = null,
        IBoothVideoService? boothVideo = null) =>
        new(
            Camera: new MockCameraService(),
            Printer: printer ?? new MockPrinterService(),
            CloudUpload: new MockCloudUploadService(),
            Sessions: sessions,
            Payment: payment ?? new MockQrPaymentService(),
            UploadQueue: new MockPendingUploadQueue(),
            Consent: new MockConsentService(),
            Email: new MockEmailDeliveryService(),
            Branding: new MockPhotoBrandingService(),
            Filter: new MockPhotoFilterService(),
            Settings: settings,
            FrameLibrary: new MockFrameLibraryService(),
            FrameSelection: new MockFrameSelectionService(),
            FrameOverlay: new MockFrameOverlayService(),
            Feedback: new MockFeedbackService(),
            GuestbookPrompt: new MockGuestbookPromptService(),
            VideoGuestbook: new MockVideoGuestbookService(),
            GifComposer: new MockGifComposerService(),
            BoothVideo: boothVideo ?? new MockBoothVideoService(),
            AttendantCue: new MockVirtualAttendantService(),
            Survey: new MockSurveyService());

    /// <summary>A settings provider tuned so a whole session runs in well under a
    /// second: no countdown, no review dwell, no sharing dwell.</summary>
    private static MockBoothSettingsProvider FastSettings(
        Func<BoothSettings, BoothSettings>? customize = null)
    {
        var baseSettings = new BoothSettings(CountdownSeconds: 0, GlamFilterEnabled: false, PrintTemplate: PrintTemplate.Default)
        {
            Screen = ScreenSettings.Default with { ReviewSeconds = 0, FinalScreenTimeoutSeconds = 1 },
        };

        return new MockBoothSettingsProvider { Settings = customize is null ? baseSettings : customize(baseSettings) };
    }

    // ======================================================== printing ==

    [Fact]
    public async Task RunSessionAsync_PrintAutomaticallyOff_DoesNotPrintButStillCompletes()
    {
        // PrintOptions.PrintAutomatically round-tripped through AdminWindow's
        // save/load and was validated there, but nothing downstream ever read
        // it -- so a share-only event configured "don't print automatically"
        // printed on every single session anyway, burning a full roll of media.
        var sessions = new MockSessionRepository();
        var printer = new MockPrinterService();
        var settings = FastSettings(s => s with
        {
            PrintOptions = PrintOptions.Default with { PrintAutomatically = false },
        });
        var machine = new BoothStateMachine(BuildServices(settings, sessions, printer), mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.DoesNotContain(BoothState.Printing, states);
        Assert.Empty(printer.PrintedTemplates);
        Assert.Empty(sessions.RecordedPrints);

        // Skipping the print must not skip the session: the guest still gets
        // their photo, the QR code and the email -- same shape GIF/Boomerang/
        // Video already established for a non-printable capture.
        Assert.Contains(BoothState.Complete, states);
        Assert.Single(sessions.CompletedSessionIds);
        Assert.Empty(sessions.FailedSessionIds);
    }

    [Fact]
    public async Task RunSessionAsync_PrintAutomaticallyOn_StillPrints()
    {
        // The other half of the test above: the new gate must not have turned
        // the default (true) into "never prints".
        var sessions = new MockSessionRepository();
        var printer = new MockPrinterService();
        var machine = new BoothStateMachine(BuildServices(FastSettings(), sessions, printer), mode: "event");

        await machine.RunSessionAsync();

        Assert.Single(printer.PrintedTemplates);
        Assert.Single(sessions.RecordedPrints);
    }

    [Fact]
    public async Task TryCountManualPrint_SharesOneEventBudgetWithTheAutomaticPrint()
    {
        // Guest reprints used to bypass _printsThisEvent entirely, so
        // PrintLimitPerEvent undercounted real media use by however many
        // reprints an event's guests asked for.
        var sessions = new MockSessionRepository();
        var settings = FastSettings(s => s with
        {
            PrintOptions = PrintOptions.Default with { PrintLimitPerEvent = 2 },
        });
        var machine = new BoothStateMachine(BuildServices(settings, sessions), mode: "event");

        // One automatic print spends the first of the two.
        await machine.RunSessionAsync();
        Assert.Single(sessions.RecordedPrints);

        // A reprint spends the second...
        Assert.True(machine.TryCountManualPrint(2));
        // ...and the budget is now genuinely gone, for reprints and for the
        // next guest's automatic print alike.
        Assert.False(machine.TryCountManualPrint(2));

        await machine.RunSessionAsync();
        Assert.Single(sessions.RecordedPrints);
    }

    [Fact]
    public async Task RunSessionAsync_PrinterOutOfPaper_ReportsItButStillCompletesTheSession()
    {
        // A printer that can't print is an attendant problem, not a failed
        // session: the photo was captured, composited and uploaded, so the
        // guest still has a working way to get it. Dropping to the Error
        // screen here would throw away a perfectly good session -- and, before
        // this, PrintDocument.Print() never surfaced the condition at all.
        var sessions = new MockSessionRepository();
        var machine = new BoothStateMachine(
            BuildServices(FastSettings(), sessions, new OutOfPaperPrinterService()), mode: "event");

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);
        var problems = new List<string>();
        machine.PrinterProblemDetected += problem => problems.Add(problem);

        await machine.RunSessionAsync();

        Assert.Equal("The printer is out of paper.", Assert.Single(problems));
        Assert.DoesNotContain(BoothState.Error, states);
        Assert.Contains(BoothState.Complete, states);
        Assert.Single(sessions.CompletedSessionIds);
        Assert.Empty(sessions.FailedSessionIds);

        // Nothing came out on paper, so nothing should be recorded as printed
        // or counted against the event's print budget.
        Assert.Empty(sessions.RecordedPrints);
        Assert.True(machine.TryCountManualPrint(1));
    }

    private sealed class OutOfPaperPrinterService : IPrinterService
    {
        public Task PrintAsync(IReadOnlyList<string> imagePaths, PrintTemplate template, PrintRenderContext? context = null, CancellationToken ct = default) =>
            throw new PrinterUnavailableException("The printer is out of paper.");
    }

    // =========================================================== dwells ==

    [Fact]
    public async Task SkipCurrentDwell_OnTheSharingScreen_EndsTheSessionEarly()
    {
        // The "I'm done" button used to only reset a decorative countdown bar;
        // the state machine's own Task.Delay kept the booth (and the queue
        // behind it) waiting out the full FinalScreenTimeoutSeconds anyway.
        var sessions = new MockSessionRepository();
        var settings = FastSettings(s => s with
        {
            Screen = ScreenSettings.Default with { ReviewSeconds = 0, FinalScreenTimeoutSeconds = 30 },
        });
        var machine = new BoothStateMachine(BuildServices(settings, sessions), mode: "event");

        var reachedComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.StateChanged += state =>
        {
            if (state == BoothState.Complete)
            {
                reachedComplete.TrySetResult();
            }
        };

        var stopwatch = Stopwatch.StartNew();
        Task session = machine.RunSessionAsync();
        await reachedComplete.Task;

        machine.SkipCurrentDwell();
        await session;
        stopwatch.Stop();

        // Generous margin -- the assertion is "it didn't sit through 30
        // seconds", not a precise timing.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"Expected the sharing dwell to end on the guest's tap, but the session took {stopwatch.Elapsed}.");
        Assert.Single(sessions.CompletedSessionIds);
    }

    [Fact]
    public async Task SkipCurrentDwell_OutsideADwell_IsANoOp()
    {
        // A stray tap (or a remote-control call landing a beat late) must not
        // skip a step the guest isn't on, or arm the *next* dwell to end
        // instantly.
        var sessions = new MockSessionRepository();
        var machine = new BoothStateMachine(BuildServices(FastSettings(), sessions), mode: "event");

        machine.SkipCurrentDwell();
        machine.SkipCurrentDwell();

        await machine.RunSessionAsync();

        Assert.Single(sessions.CompletedSessionIds);
        Assert.Empty(sessions.FailedSessionIds);
    }

    // ======================================================== recording ==

    [Fact]
    public async Task RunSessionAsync_VideoModeCancelledMidClip_StillStopsTheRecording()
    {
        // The Video branch had no try/finally: cancelling during the clip
        // cancelled the delay, so StopRecordingAsync was never reached at all.
        // ffmpeg kept recording with nobody left to stop it -- holding the
        // webcam and mic for the rest of the app run, leaving an unplayable
        // file, and wedging every later Video session with "a recording is
        // already in progress".
        var sessions = new MockSessionRepository();
        var boothVideo = new MockBoothVideoService();
        var settings = FastSettings(s => s with
        {
            Capture = new CaptureSettings("Video")
            {
                Video = VideoCaptureSettings.Default with
                {
                    CountdownBeforeClip1Seconds = 0,
                    ClipDurationSeconds = 30,
                },
            },
        });
        var machine = new BoothStateMachine(BuildServices(settings, sessions, boothVideo: boothVideo), mode: "event");

        using var cts = new CancellationTokenSource();
        var recordingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.StateChanged += state =>
        {
            if (state == BoothState.Capturing)
            {
                recordingStarted.TrySetResult();
            }
        };

        Task session = machine.RunSessionAsync(cts.Token);
        await recordingStarted.Task;
        await Task.Delay(50, CancellationToken.None); // let StartRecordingAsync land
        cts.Cancel();
        await session;

        Assert.Single(boothVideo.RecordedFiles);
        Assert.Single(sessions.AbandonedSessionIds);

        // The real proof that nothing is wedged: the service takes another
        // recording. Before the fix this threw "already in progress".
        await boothVideo.StartRecordingAsync();
        await boothVideo.StopRecordingAsync();
    }

    // ========================================================== payment ==

    [Fact]
    public async Task RunPaymentGate_GuestWalksAway_DropsThePendingAttempt()
    {
        // WithGuestIdleTimeoutAsync stops awaiting on a timeout but can't
        // cancel the call already in flight, and this was the one call site
        // that passed no cancelGuest callback. The parked attempt stayed in
        // the dictionary for the life of the process -- and stayed
        // confirmable, so an attendant tapping "Payment Received" a beat too
        // late resolved a session that had already failed, with no Payment row
        // for money they had actually collected.
        var sessions = new MockSessionRepository();
        var payment = new ManualConfirmPaymentService();
        var machine = new BoothStateMachine(
            BuildServices(FastSettings(), sessions, payment: payment),
            mode: "vendo",
            // Long enough to clear MockConsentService's own 500ms simulated
            // read-the-disclaimer delay (a shorter timeout makes Consent itself
            // time out, and the session never reaches the payment gate at all),
            // short enough that the guest-never-pays wait is still quick.
            guestIdleTimeout: TimeSpan.FromMilliseconds(1200));

        await machine.RunSessionAsync();

        string reference = Assert.IsType<string>(machine.PaymentReference);
        Assert.False(payment.HasPending(reference));

        // A guest who never paid still doesn't get a photo recorded as paid.
        Assert.Empty(sessions.RecordedPayments);
        Assert.Single(sessions.FailedSessionIds);
    }

    [Fact]
    public async Task RunSessionAsync_ClearsThePaymentReferenceAtTheStartOfEachSession()
    {
        // A stale reference left the attendant's "Payment Received" button
        // pointing at the previous session's attempt.
        var sessions = new MockSessionRepository();
        var payment = new ManualConfirmPaymentService();
        var machine = new BoothStateMachine(
            BuildServices(FastSettings(), sessions, payment: payment),
            mode: "vendo",
            // Long enough to clear MockConsentService's own 500ms simulated
            // read-the-disclaimer delay (a shorter timeout makes Consent itself
            // time out, and the session never reaches the payment gate at all),
            // short enough that the guest-never-pays wait is still quick.
            guestIdleTimeout: TimeSpan.FromMilliseconds(1200));

        await machine.RunSessionAsync();
        string firstReference = Assert.IsType<string>(machine.PaymentReference);

        await machine.RunSessionAsync();
        string secondReference = Assert.IsType<string>(machine.PaymentReference);

        Assert.NotEqual(firstReference, secondReference);
        Assert.False(payment.HasPending(firstReference));
    }

    [Fact]
    public async Task ManualConfirmPayment_ConfirmingACancelledAttempt_IsANoOp()
    {
        var payment = new ManualConfirmPaymentService();

        await payment.InitiateAsync(150m, "ref-1");
        payment.CancelPending("ref-1");

        Assert.False(payment.HasPending("ref-1"));

        // The late tap the fix exists for: it must find nothing to confirm
        // rather than resolving a dead attempt.
        payment.ConfirmPayment("ref-1");
        Assert.False(payment.HasPending("ref-1"));
    }
}
