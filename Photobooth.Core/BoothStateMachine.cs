namespace Photobooth.Core;

/// <summary>
/// Drives a single guest session end to end. This is the piece your WPF UI
/// binds to: subscribe to StateChanged to swap screens, CountdownTick to
/// update the on-screen number, and ErrorOccurred to show a friendly message.
///
/// Camera and printer are injected as interfaces (not created here), so the
/// same state machine runs identically whether it's driving MockCameraService
/// during development or a real PTP-backed service driving the Nikon D3500
/// at an actual event.
/// </summary>
public class BoothStateMachine
{
    /// <summary>Flat per-print price charged in vendo mode. Event mode is a flat fee paid outside the app (the booking), so sessions there stay free_event.</summary>
    private const decimal VendoPricePerPrint = 150m;

    private readonly BoothServices _services;
    private readonly string _mode;

    public BoothState CurrentState { get; private set; } = BoothState.Idle;
    public string? LastCapturedImagePath { get; private set; }
    public Uri? LastPhotoUrl { get; private set; }

    /// <summary>QR code the guest scans to pay, set right before the Payment state shows. Null for a gateway with nothing to scan (e.g. a card reader). Only meaningful in vendo mode.</summary>
    public byte[]? PaymentQrPng { get; private set; }

    /// <summary>What to tell the guest on the Payment screen -- gateway-specific (e.g. "Scan to pay" vs "Tap your card"), set right before the Payment state shows. Only meaningful in vendo mode.</summary>
    public string? PaymentInstructions { get; private set; }

    /// <summary>Outcome of the current/most recent session's disclaimer+opt-in prompt, set right after the Consent state shows.</summary>
    public ConsentResult? LastConsent { get; private set; }

    /// <summary>The frame the guest picked during FramePicker, or null if they skipped it (or no active frames were configured, in which case FramePicker never shows at all).</summary>
    public FrameOption? LastSelectedFrame { get; private set; }

    public event Action<BoothState>? StateChanged;
    public event Action<int>? CountdownTick;
    public event Action<string>? ErrorOccurred;

    /// <summary>Fires when the background upload for the current session's photo finishes -- may land during Reviewing, Printing, or Complete, whichever is showing when the network call happens to finish.</summary>
    public event Action<Uri>? PhotoUploaded;

    /// <param name="mode">'event' or 'vendo', matching the booth's Location.Type -- fixed for the life of this state machine since one booth machine serves one location. Event-mode sessions skip straight through as a free_event Payment row; vendo-mode sessions run the Payment state before Printing.</param>
    public BoothStateMachine(BoothServices services, string mode = "event")
    {
        _services = services;
        _mode = mode;
    }

    private void SetState(BoothState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Runs one full guest session: countdown, capture, review, print, then
    /// resets to Idle. Any failure at any step is caught, reported via
    /// ErrorOccurred, and the machine still returns to Idle -- a session
    /// should never leave the booth stuck on a dead screen.
    /// </summary>
    public async Task RunSessionAsync(CancellationToken ct = default)
    {
        // Event mode is recorded as a zero-amount 'free_event' Payment rather
        // than skipping the Payment row entirely, so the admin dashboard's
        // revenue-by-mode query doesn't have to special-case sessions with
        // no Payment at all.
        int? sessionId = null;

        // Best-effort flush of any earlier session's upload that failed
        // (dropped venue WiFi, Cloudinary hiccup) -- fire-and-forget so a
        // backlog doesn't have to wait for a dedicated retry timer, just the
        // next guest walking up. Never allowed to block or fail this session.
        _ = RetryQueuedUploadsAsync(ct);

        try
        {
            // Read fresh at the start of every session, not cached anywhere,
            // so an admin's settings change (see AdminWindow) takes effect
            // for the very next guest instead of needing an app restart.
            BoothSettings settings = await _services.Settings.GetSettingsAsync(ct);

            sessionId = await _services.Sessions.CreateAsync(_mode, ct);

            SetState(BoothState.Consent);
            ConsentResult consent = await _services.Consent.CollectAsync(ct);
            LastConsent = consent;
            await _services.Sessions.RecordConsentAsync(
                sessionId.Value, consent.DisclaimerAccepted, consent.EmailOptIn, consent.Email, ct);

            if (!consent.DisclaimerAccepted)
            {
                // Declining is a legitimate guest choice, not a failure -- no
                // countdown, no capture, and recorded as Abandoned rather
                // than Error so the admin dashboard can tell the two apart.
                await _services.Sessions.AbandonAsync(sessionId.Value, ct);
                return;
            }

            SetState(BoothState.Countdown);
            for (int i = settings.CountdownSeconds; i > 0; i--)
            {
                CountdownTick?.Invoke(i);
                await Task.Delay(1000, ct);
            }

            SetState(BoothState.Capturing);
            LastPhotoUrl = null;
            PaymentQrPng = null;
            PaymentInstructions = null;
            LastSelectedFrame = null;
            LastCapturedImagePath = await _services.Camera.CaptureAsync(ct);

            // Glam filter (if this booth's settings have it on) applies
            // before branding, not after -- the caption bar is always white
            // text on a solid black bar regardless of the photo's colors, so
            // filter order doesn't affect its legibility either way, but
            // doing the color/contrast pass on the plain capture first keeps
            // the two effects independent and easy to reason about.
            if (settings.GlamFilterEnabled)
            {
                LastCapturedImagePath = await _services.Filter.ApplyGlamFilterAsync(LastCapturedImagePath, ct);
            }

            // Branded before anything downstream ever sees the path -- the
            // Reviewing screen, the print, and the upload should all show
            // the guest exactly the same (branded) photo, not three
            // different versions depending on which step ran first.
            LastCapturedImagePath = await _services.Branding.ApplyBrandingAsync(LastCapturedImagePath, settings.Theme.EventName, ct);

            SetState(BoothState.Reviewing);
            await Task.Delay(2000, ct); // guest sees the shot before it prints

            // Skipped entirely when no admin-configured frames are active --
            // a fresh booth with an empty Frame table behaves exactly as it
            // did before this feature existed.
            IReadOnlyList<FrameOption> frames = await _services.FrameLibrary.GetActiveFramesAsync(ct);
            if (frames.Count > 0)
            {
                SetState(BoothState.FramePicker);
                LastSelectedFrame = await _services.FrameSelection.SelectFrameAsync(frames, ct);
                if (LastSelectedFrame is not null)
                {
                    LastCapturedImagePath = await _services.FrameOverlay.ApplyFrameAsync(
                        LastCapturedImagePath, LastSelectedFrame.ImagePath, ct);
                }
            }

            // Fire-and-forget, and deliberately started only now (after any
            // frame choice), not right after branding -- the QR code and the
            // print both need to show the same final composited photo, same
            // reasoning branding/filter ordering already established. A
            // failed or slow upload just means no QR code shows this
            // session -- it never holds up the print.
            Task uploadTask = UploadInBackgroundAsync(LastCapturedImagePath, ct);

            if (_mode == "vendo")
            {
                string reference = Guid.NewGuid().ToString("N");
                PaymentPrompt prompt = _services.Payment.Initiate(VendoPricePerPrint, reference);
                PaymentInstructions = prompt.Instructions;
                PaymentQrPng = prompt.QrCodePng;
                SetState(BoothState.Payment);
                PaymentResult result = await _services.Payment.WaitForConfirmationAsync(reference, VendoPricePerPrint, ct);
                if (!result.Success)
                {
                    throw new InvalidOperationException("Payment was not completed.");
                }
                await _services.Sessions.RecordPaymentAsync(sessionId.Value, VendoPricePerPrint, result.Method, ct);
            }

            // The guest has definitely earned their photo by this point --
            // event mode is free by design, and vendo mode just cleared
            // payment above (a thrown/declined payment never reaches this
            // line, so a guest who didn't pay never gets emailed a free
            // copy, nor a queued retry that would eventually email one).
            // Fire-and-forget so a still-in-flight upload or a slow email
            // send doesn't hold up Printing.
            _ = FinalizeUploadAsync(uploadTask, ct);

            SetState(BoothState.Printing);
            await _services.Printer.PrintAsync(LastCapturedImagePath, settings.PrintTemplate, ct);
            await _services.Sessions.RecordPrintAsync(sessionId.Value, LastCapturedImagePath, ct);

            if (_mode != "vendo")
            {
                await _services.Sessions.RecordPaymentAsync(sessionId.Value, 0m, "free_event", ct);
            }

            SetState(BoothState.Complete);
            await _services.Sessions.CompleteAsync(sessionId.Value, ct);
            await Task.Delay(1500, ct); // "thank you" screen dwell time

            // Best-effort, wrapped in its own try/catch, same reasoning as
            // the Feedback block right below: a guest who walks away without
            // tapping anything should never turn an already-completed
            // session into an Error one.
            try
            {
                SetState(BoothState.Guestbook);
                if (await _services.GuestbookPrompt.AskToRecordAsync(ct))
                {
                    await _services.VideoGuestbook.StartRecordingAsync(ct);
                    try
                    {
                        // Guest taps Stop, or a 60s safety-net timeout elapses --
                        // ffmpeg should never be left recording indefinitely
                        // because a guest walked away without tapping anything.
                        await Task.WhenAny(
                            _services.GuestbookPrompt.WaitForStopAsync(ct),
                            Task.Delay(TimeSpan.FromSeconds(60), ct));
                    }
                    finally
                    {
                        GuestbookRecording recording = await _services.VideoGuestbook.StopRecordingAsync(ct);
                        await _services.Sessions.RecordGuestbookVideoAsync(sessionId.Value, recording.FilePath, recording.Duration, ct);
                    }
                }
            }
            catch (Exception)
            {
            }

            // Best-effort, wrapped in its own try/catch: a guest who walks
            // away without tapping anything, or any other failure collecting
            // feedback, should never turn an already-completed session into
            // an Error one -- the photo's already been captured, paid for
            // (if vendo), and printed by this point.
            try
            {
                SetState(BoothState.Feedback);
                FeedbackResult feedback = await _services.Feedback.CollectAsync(ct);
                if (!feedback.IsEmpty)
                {
                    await _services.Sessions.RecordFeedbackAsync(sessionId.Value, feedback.Rating, feedback.Comment, ct);
                }
            }
            catch (Exception)
            {
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
            SetState(BoothState.Error);
            if (sessionId.HasValue)
            {
                await _services.Sessions.FailAsync(sessionId.Value, ct);
            }
            await Task.Delay(3000, ct); // show the error briefly before resetting
        }
        finally
        {
            SetState(BoothState.Idle);
        }
    }

    private async Task UploadInBackgroundAsync(string imagePath, CancellationToken ct)
    {
        try
        {
            LastPhotoUrl = await _services.CloudUpload.UploadAsync(imagePath, ct);
            PhotoUploaded?.Invoke(LastPhotoUrl);
        }
        catch (Exception)
        {
            // Deliberately doesn't queue the failure here -- see
            // FinalizeUploadAsync for why that decision waits until the
            // payment gate has cleared.
        }
    }

    /// <summary>
    /// Waits for the in-flight upload to settle, then either emails the
    /// guest their photo link (if they opted in and the upload succeeded)
    /// or queues the file for retry (carrying the same email along, so a
    /// later successful retry can still send it). Only ever called once the
    /// guest has definitely earned their photo (see the call site) -- never
    /// for a declined vendo payment, so neither a same-session email nor a
    /// queued retry-with-email can happen for a guest who didn't pay.
    /// </summary>
    private async Task FinalizeUploadAsync(Task uploadTask, CancellationToken ct)
    {
        await uploadTask; // UploadInBackgroundAsync catches its own failures, never throws

        string? email = LastConsent is { EmailOptIn: true, Email: string toEmail } ? toEmail : null;

        if (LastPhotoUrl is Uri photoUrl)
        {
            if (email is not null)
            {
                try
                {
                    await _services.Email.SendPhotoLinkAsync(email, photoUrl, ct);
                }
                catch (Exception)
                {
                    // Best-effort: a failed email isn't the guest's problem
                    // to see -- they still have the QR code as a working way
                    // to get their photo.
                }
            }
        }
        else if (LastCapturedImagePath is string imagePath)
        {
            // Upload failed -- the photo isn't lost, queue it (with the
            // email, if any) so the next session or app startup retries it.
            try
            {
                await _services.UploadQueue.EnqueueAsync(imagePath, email, ct);
            }
            catch (Exception)
            {
                // Best-effort: swallow so a failure to even queue can't
                // surface as an unobserved task exception -- it's not on the
                // guest-facing error path, just a missing QR code (and
                // eventually, email) for this session.
            }
        }
    }

    /// <summary>
    /// Retries every upload that failed in a previous session, emailing
    /// whoever opted in once their retry actually succeeds. Safe to call as
    /// often as convenient (opportunistically at the start of a session, or
    /// once at app startup), including from multiple BoothStateMachine
    /// instances sharing one queue -- DequeueAllAsync atomically claims the
    /// whole backlog, so a second overlapping call (e.g. a still-in-flight
    /// retry from the previous session racing this one) sees nothing left
    /// to do instead of double-processing the same item (confirmed:
    /// reproduced as a duplicate guest email via Photobooth.ConsoleDemo
    /// before this existed).
    /// </summary>
    public async Task RetryQueuedUploadsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PendingUpload> claimed = await _services.UploadQueue.DequeueAllAsync(ct);
        foreach (PendingUpload item in claimed)
        {
            try
            {
                Uri url = await _services.CloudUpload.UploadAsync(item.FilePath, ct);

                if (item.Email is string toEmail)
                {
                    try
                    {
                        await _services.Email.SendPhotoLinkAsync(toEmail, url, ct);
                    }
                    catch (Exception)
                    {
                        // Best-effort: the upload itself already succeeded --
                        // a failed email here isn't worth re-queuing over.
                    }
                }
            }
            catch (Exception)
            {
                // Still offline (or this particular file's upload still
                // fails) -- put it back for next time. Already claimed out
                // of the queue by DequeueAllAsync above, so re-enqueue
                // rather than leave it (as the old Get+Remove version did).
                try
                {
                    await _services.UploadQueue.EnqueueAsync(item.FilePath, item.Email, ct);
                }
                catch (Exception)
                {
                    // Best-effort: swallow so a failure to even re-queue
                    // can't surface as an unobserved task exception.
                }
            }
        }
    }
}
