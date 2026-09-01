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

    /// <summary>How long a guest-interactive state (Consent, Payment, FramePicker,
    /// Feedback, Survey) waits for the guest before treating them as having walked
    /// away -- see WithGuestIdleTimeoutAsync. One shared mechanism/timeout for all
    /// five states, not five separate timers (see BUILD_PLAN.md's Day 3): a guest
    /// who never taps anything would otherwise block the booth for the next one
    /// forever, since none of those states had any timeout at all before this.</summary>
    private readonly TimeSpan _guestIdleTimeout;

    public BoothState CurrentState { get; private set; } = BoothState.Setup;
    public string? LastCapturedImagePath { get; private set; }

    /// <summary>Every pose captured this session, in PhotoIndex order -- a single-element
    /// list matching LastCapturedImagePath for every template that predates PhotoSlot
    /// elements (the common case). What actually gets handed to IPrinterService.PrintAsync.</summary>
    public IReadOnlyList<string> LastCapturedImagePaths { get; private set; } = Array.Empty<string>();

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

    /// <summary>Fires once per pose, right before that pose's Countdown starts, for a
    /// true multi-pose template (PrintTemplate.RequiredPhotoCount > 1) -- lets the UI show
    /// "Pose 2 of 4". Never fires for a template that predates PhotoSlot elements (the
    /// common case), same as CountdownTick never firing outside Countdown.</summary>
    public event Action<int, int>? PoseChanged;

    /// <summary>Fires when the background upload for the current session's photo finishes -- may land during Reviewing, Printing, or Complete, whichever is showing when the network call happens to finish.</summary>
    public event Action<Uri>? PhotoUploaded;

    /// <summary>Fires once per SetState when the Virtual Attendant has a clip configured
    /// for that stage -- MainWindow plays it alongside whatever screen is already
    /// showing. Purely additive: never gates or delays a state transition, and a
    /// missing/misconfigured clip never raises this event at all (see FireAttendantCueAsync).</summary>
    public event Action<AttendantClip>? AttendantCueChanged;

    /// <param name="mode">'event' or 'vendo', matching the booth's Location.Type -- fixed for the life of this state machine since one booth machine serves one location. Event-mode sessions skip straight through as a free_event Payment row; vendo-mode sessions run the Payment state before Printing.</param>
    /// <param name="guestIdleTimeout">Overrides <see cref="_guestIdleTimeout"/> -- defaults
    /// to 45 seconds, long enough for a real guest to read a disclaimer or tap a star
    /// rating, but exposed here (not a hardcoded constant) so tests can use a timeout
    /// shorter than a Mock service's own simulated response delay to exercise the
    /// "walked away" path deterministically, without needing a dedicated never-responds
    /// hook on every affected mock.</param>
    public BoothStateMachine(BoothServices services, string mode = "event", TimeSpan? guestIdleTimeout = null)
    {
        _services = services;
        _mode = mode;
        _guestIdleTimeout = guestIdleTimeout ?? TimeSpan.FromSeconds(45);
    }

    /// <summary>
    /// Races a guest-interactive call against the shared idle timeout. On a genuine
    /// timeout, returns <paramref name="fallback"/> -- interpreted by each call site
    /// exactly the same way it already treats a guest's explicit skip/decline (e.g.
    /// MockConsentService.DeclineNext, MockFeedbackService.SkipNext), so a guest who
    /// never responds behaves like one who responded empty, not like an error. If
    /// <paramref name="ct"/> is cancelled instead, the cancellation is rethrown rather
    /// than swallowed into a fallback -- Task.Delay completes as Cancelled (not
    /// RanToCompletion) in that case, so awaiting it here surfaces the real exception.
    /// The abandoned guestTask (past a genuine timeout) is left to finish on its own;
    /// any eventual fault is observed and discarded so it can't surface as an
    /// unobserved task exception later.
    /// </summary>
    private async Task<T> WithGuestIdleTimeoutAsync<T>(Task<T> guestTask, T fallback, CancellationToken ct)
    {
        Task delayTask = Task.Delay(_guestIdleTimeout, ct);
        Task winner = await Task.WhenAny(guestTask, delayTask);
        if (winner == guestTask)
        {
            return await guestTask;
        }

        await delayTask; // throws if ct was cancelled instead of the timeout genuinely elapsing
        _ = guestTask.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return fallback;
    }

    private void SetState(BoothState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);

        // Fire-and-forget: the Virtual Attendant is purely decorative (audio/video
        // playing alongside whatever screen is already showing), so a slow or failed
        // cue lookup must never delay or interrupt the state transition itself.
        _ = FireAttendantCueAsync(state);
    }

    private async Task FireAttendantCueAsync(BoothState state)
    {
        try
        {
            AttendantClip? clip = await _services.AttendantCue.GetCueAsync(state);
            if (clip is not null)
            {
                AttendantCueChanged?.Invoke(clip);
            }
        }
        catch (Exception)
        {
            // Best-effort, same reasoning as the Feedback/Guestbook try/catches below --
            // a missing or misconfigured attendant clip should never disrupt a session.
        }
    }

    /// <summary>Admin has confirmed the PIN and settings and wants guests to
    /// start using the booth -- the only transition out of Setup. A no-op once
    /// the event is already running (e.g. a stray double-tap on Launch Event),
    /// so it can never interrupt an in-progress guest session.</summary>
    public void LaunchEvent()
    {
        if (CurrentState == BoothState.Setup)
        {
            SetState(BoothState.Idle);
        }
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
            ConsentResult consent = await WithGuestIdleTimeoutAsync(
                _services.Consent.CollectAsync(ct), new ConsentResult(false, false, null), ct);
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

            LastPhotoUrl = null;
            PaymentQrPng = null;
            PaymentInstructions = null;
            LastSelectedFrame = null;

            // GIF/Boomerang/Video: none of the three produce a printable
            // single still, so none of them go through Printing below --
            // see BUILD_PLAN.md's "dslrBooth feature-parity plan", Phase 2.
            // Photo mode (the default, and the only mode that existed
            // before this feature) is unchanged below. GIF/Boomerang also
            // deliberately skip the green screen/glam filter/branding/
            // frame-overlay pipeline entirely: those are all single-still
            // GDI+ operations (see GdiGreenScreenService/
            // GdiPhotoBrandingService/GdiPhotoFilterService/
            // GdiFrameOverlayService) that would either only touch the
            // first frame or corrupt the animation outright if pointed at a
            // multi-frame GIF -- a real fix means compositing each effect
            // onto every frame before assembly, not attempted here. Neither
            // mode has a notion of multiple print poses either -- PhotoSlot
            // templates only apply to Photo mode below.
            bool isNonPrintableCapture = settings.Capture.Mode is "GIF" or "Boomerang" or "Video";
            if (settings.Capture.Mode is "GIF" or "Boomerang")
            {
                SetState(BoothState.Countdown);
                for (int i = settings.CountdownSeconds; i > 0; i--)
                {
                    CountdownTick?.Invoke(i);
                    await Task.Delay(1000, ct);
                }

                SetState(BoothState.Capturing);
                var framePaths = new List<string>();
                for (int i = 0; i < settings.Capture.FrameCount; i++)
                {
                    framePaths.Add(await _services.Camera.CaptureAsync(ct));
                    if (i < settings.Capture.FrameCount - 1)
                    {
                        await Task.Delay(settings.Capture.FrameDelayMs, ct);
                    }
                }

                LastCapturedImagePath = await _services.GifComposer.ComposeAsync(
                    framePaths, reversed: settings.Capture.Mode == "Boomerang", settings.Capture.FrameDelayMs, ct);
                LastCapturedImagePaths = new[] { LastCapturedImagePath };
            }
            else if (settings.Capture.Mode == "Video")
            {
                SetState(BoothState.Countdown);
                for (int i = settings.CountdownSeconds; i > 0; i--)
                {
                    CountdownTick?.Invoke(i);
                    await Task.Delay(1000, ct);
                }

                SetState(BoothState.Capturing);
                // A fixed-duration recording (no "guest taps stop" UI yet,
                // same simplification the guestbook recording's 60s safety
                // net accepts for a different reason) -- starts, waits out
                // VideoDurationSeconds, then stops. Independent of
                // ICameraService entirely, same as IVideoGuestbookService.
                await _services.BoothVideo.StartRecordingAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(settings.Capture.VideoDurationSeconds), ct);
                BoothVideoRecording recording = await _services.BoothVideo.StopRecordingAsync(ct);
                LastCapturedImagePath = recording.FilePath;
                LastCapturedImagePaths = new[] { LastCapturedImagePath };
            }
            else
            {
                // One Countdown/Capturing/effects cycle per required pose.
                // PrintTemplate.RequiredPhotoCount is 1 for every template
                // that predates PhotoSlot elements, so this loop runs
                // exactly once -- identical to the single-capture behavior
                // that existed before true multi-pose templates -- for
                // every template in use before this feature.
                int requiredPhotoCount = settings.PrintTemplate.RequiredPhotoCount;
                var poses = new List<string>();
                for (int poseIndex = 0; poseIndex < requiredPhotoCount; poseIndex++)
                {
                    if (requiredPhotoCount > 1)
                    {
                        PoseChanged?.Invoke(poseIndex + 1, requiredPhotoCount);
                    }

                    SetState(BoothState.Countdown);
                    for (int i = settings.CountdownSeconds; i > 0; i--)
                    {
                        CountdownTick?.Invoke(i);
                        await Task.Delay(1000, ct);
                    }

                    SetState(BoothState.Capturing);
                    LastCapturedImagePath = await _services.Camera.CaptureAsync(ct);

                    // Filters (see PhotoFilterPreset/GdiFilterPresetService,
                    // BoothState.FilterPicker) run first in the effects chain -- a
                    // foundational photographic treatment the rest (green screen
                    // background swap, branding caption, frame/sticker overlay,
                    // watermark) layer on top of. Skipped entirely when the
                    // Filters toggle is off or no preset is enabled, same
                    // "disabled/empty pool = feature invisible" reasoning
                    // FramePicker/Stickers already established.
                    if (settings.Effects.FiltersEnabled)
                    {
                        List<PhotoFilterPreset> enabledPresets = PhotoFilterPresets.Parse(settings.Effects.EnabledFilterPresetIds);
                        if (enabledPresets.Count > 0)
                        {
                            if (settings.Effects.FiltersMode == "Auto")
                            {
                                // Silent -- no guest interaction, no FilterPicker state.
                                LastCapturedImagePath = await _services.FilterPreset.ApplyPresetAsync(LastCapturedImagePath, enabledPresets[0], ct);
                            }
                            else
                            {
                                // Ask: render every enabled preset against the actual
                                // capture up front -- each candidate is a real,
                                // fully-rendered result (not a generic stock
                                // thumbnail), so whichever one the guest taps is
                                // already the final file, no second apply pass needed.
                                var filterOptions = new List<FilterOption>();
                                foreach (PhotoFilterPreset preset in enabledPresets)
                                {
                                    string previewPath = await _services.FilterPreset.ApplyPresetAsync(LastCapturedImagePath, preset, ct);
                                    filterOptions.Add(new FilterOption(preset, PhotoFilterPresets.DisplayName(preset), previewPath));
                                }

                                SetState(BoothState.FilterPicker);
                                FilterOption? chosenFilter = await WithGuestIdleTimeoutAsync(
                                    _services.FilterSelection.SelectFilterAsync(filterOptions, ct), (FilterOption?)null, ct);
                                if (chosenFilter is not null)
                                {
                                    LastCapturedImagePath = chosenFilter.PreviewImagePath;
                                }
                            }
                        }
                    }

                    // Green screen composites first, before the glam filter --
                    // its green-dominance threshold needs the plain camera
                    // colors, and a background composited after a B&W pass would
                    // itself need to be desaturated to match, which isn't
                    // attempted here. Skipped with no background configured:
                    // nothing to composite against yet, even if the toggle is on.
                    if (settings.GreenScreen is { Enabled: true, BackgroundImagePath: not null } greenScreen)
                    {
                        LastCapturedImagePath = await _services.GreenScreen.ApplyGreenScreenAsync(
                            LastCapturedImagePath, greenScreen.BackgroundImagePath, ct);
                    }

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

                    poses.Add(LastCapturedImagePath);
                }

                LastCapturedImagePaths = poses;
            }

            SetState(BoothState.Reviewing);
            await Task.Delay(2000, ct); // guest sees the shot before it prints

            // Frame/sticker picker is a single-still GDI+ overlay too (see the
            // isNonPrintableCapture comment above) -- skipped for GIF/
            // Boomerang/Video for the same reason. Also skipped entirely when
            // no admin-configured frames are active, or when the Effects &
            // Stickers screen's Stickers toggle is off -- either way, a fresh/
            // stickers-disabled booth behaves exactly as it did before this
            // feature existed.
            // Applied to every captured pose (usually just the one), not only the
            // last -- every pose in the final print gets the same treatment.
            List<string> processedPoses = LastCapturedImagePaths.ToList();

            IReadOnlyList<FrameOption> frames = isNonPrintableCapture || !settings.Effects.StickersEnabled
                ? []
                : await _services.FrameLibrary.GetActiveFramesAsync(ct);
            if (frames.Count > 0)
            {
                SetState(BoothState.FramePicker);
                LastSelectedFrame = await WithGuestIdleTimeoutAsync(
                    _services.FrameSelection.SelectFrameAsync(frames, ct), (FrameOption?)null, ct);
                if (LastSelectedFrame is not null)
                {
                    for (int i = 0; i < processedPoses.Count; i++)
                    {
                        processedPoses[i] = await _services.FrameOverlay.ApplyFrameAsync(
                            processedPoses[i], LastSelectedFrame.ImagePath, ct);
                    }
                }
            }

            // Watermark stamps last, on top of everything else (branding,
            // filter, frame/sticker) -- same "a logo overlay sits above
            // everything" semantics dslrBooth's own Watermark setting
            // describes ("a full overlay ... to each individual photo").
            // Reuses IFrameOverlayService rather than a dedicated watermark
            // service -- compositing a transparent PNG over the photo is the
            // exact same GDI+ operation a frame/sticker already is. Skipped
            // for GIF/Boomerang/Video, same single-still limitation as the
            // rest of this pipeline.
            if (!isNonPrintableCapture && settings.Effects is { WatermarkEnabled: true, WatermarkImagePath: not null })
            {
                for (int i = 0; i < processedPoses.Count; i++)
                {
                    processedPoses[i] = await _services.FrameOverlay.ApplyFrameAsync(
                        processedPoses[i], settings.Effects.WatermarkImagePath, ct);
                }
            }

            LastCapturedImagePaths = processedPoses;
            LastCapturedImagePath = processedPoses[^1];

            // Post-Processing hook -- see IPostProcessingService's doc for why
            // this is fire-and-forget (doesn't await or gate the guest flow on
            // an arbitrary external app). Runs against the final composited
            // path, same "final version" reasoning the upload/print below
            // already need. Skipped for GIF/Boomerang/Video, same single-still
            // framing "post-processing on each photo" implies.
            if (!isNonPrintableCapture && settings.Effects is { PostProcessingEnabled: true, PostProcessingApplicationPath: { Length: > 0 } postProcessingApp })
            {
                _services.PostProcessing.Run(postProcessingApp, LastCapturedImagePath);
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
                PaymentResult result = await WithGuestIdleTimeoutAsync(
                    _services.Payment.WaitForConfirmationAsync(reference, VendoPricePerPrint, ct),
                    new PaymentResult(false, "timeout", null), ct);
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

            // GIF/Boomerang/Video have nothing printable -- dslrBooth's own
            // Video/GIF modes are share-only too (see the Sharing Screen
            // toggles surveyed in BUILD_PLAN.md's dslrBooth feature list).
            // Skips the Printing state and Print row entirely rather than
            // handing SpoolerPrinterService a .gif/.mp4 it has no way to
            // rasterize onto paper.
            if (!isNonPrintableCapture)
            {
                // A template with a QrCode element needs a real upload URL to
                // encode -- give the still-in-flight upload a bounded chance to
                // finish before printing (never indefinitely; the booth must
                // never stall on a slow or dead network). A template without
                // one keeps today's fully fire-and-forget upload -- printing
                // never waits on it at all.
                if (settings.PrintTemplate.Elements.Any(e => e.Kind == PrintTemplateElementKind.QrCode))
                {
                    await Task.WhenAny(uploadTask, Task.Delay(TimeSpan.FromSeconds(10), ct));
                }

                SetState(BoothState.Printing);
                var printContext = new PrintRenderContext(LastPhotoUrl, settings.Theme.EventName, DateTime.Now);
                await _services.Printer.PrintAsync(LastCapturedImagePaths, settings.PrintTemplate, printContext, ct);
                await _services.Sessions.RecordPrintAsync(sessionId.Value, LastCapturedImagePath, ct);
            }

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
                FeedbackResult feedback = await WithGuestIdleTimeoutAsync(
                    _services.Feedback.CollectAsync(ct), new FeedbackResult(null, null), ct);
                if (!feedback.IsEmpty)
                {
                    await _services.Sessions.RecordFeedbackAsync(sessionId.Value, feedback.Rating, feedback.Comment, ct);
                }
            }
            catch (Exception)
            {
            }

            // Best-effort, wrapped in its own try/catch, same reasoning as the
            // Feedback block just above: a guest who walks away without tapping
            // anything, or any other failure collecting survey answers, should
            // never turn an already-completed session into an Error one. Skipped
            // entirely when the admin has it off, or there are no active
            // questions configured -- same "empty table = feature invisible"
            // reasoning FramePicker already established for an empty Frame table.
            try
            {
                if (settings.Survey.Enabled)
                {
                    IReadOnlyList<SurveyQuestion> questions = await _services.Survey.GetActiveQuestionsAsync(ct);
                    if (questions.Count > 0)
                    {
                        SetState(BoothState.Survey);
                        IReadOnlyList<SurveyAnswer> answers = await WithGuestIdleTimeoutAsync(
                            _services.Survey.CollectAnswersAsync(questions, ct), (IReadOnlyList<SurveyAnswer>)Array.Empty<SurveyAnswer>(), ct);
                        if (answers.Count > 0)
                        {
                            await _services.Survey.RecordResponsesAsync(sessionId.Value, answers, ct);
                        }
                    }
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
