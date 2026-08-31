using Photobooth.Core;

var camera = new MockCameraService();
var printer = new MockPrinterService();
var cloudUpload = new MockCloudUploadService();
var payment = new MockQrPaymentService();
var sessions = new MockSessionRepository();
var uploadQueue = new MockPendingUploadQueue();
var consent = new MockConsentService();
var email = new MockEmailDeliveryService();
var branding = new MockPhotoBrandingService();
var filter = new MockPhotoFilterService();
var settings = new MockBoothSettingsProvider();
var frameLibrary = new MockFrameLibraryService();
var frameSelection = new MockFrameSelectionService();
var frameOverlay = new MockFrameOverlayService();
var feedback = new MockFeedbackService();
var guestbookPrompt = new MockGuestbookPromptService();
var videoGuestbook = new MockVideoGuestbookService();
var gifComposer = new MockGifComposerService();
int emailsPrinted = 0;
void PrintNewEmails(MockEmailDeliveryService service)
{
    for (; emailsPrinted < service.SentEmails.Count; emailsPrinted++)
    {
        var sent = service.SentEmails[emailsPrinted];
        Console.WriteLine($"  [EMAILED]   {sent.ToEmail} -> {sent.PhotoUrl}");
    }
}

var services = new BoothServices(camera, printer, cloudUpload, sessions, payment, uploadQueue, consent, email, branding, filter, settings, frameLibrary, frameSelection, frameOverlay, feedback, guestbookPrompt, videoGuestbook, gifComposer);
var eventMachine = new BoothStateMachine(services, mode: "event");

eventMachine.StateChanged += state => Console.WriteLine($"  [STATE]     {state}");
eventMachine.CountdownTick += n => Console.WriteLine($"  [COUNTDOWN] {n}");
eventMachine.ErrorOccurred += msg => Console.WriteLine($"  [ERROR]     {msg}");
eventMachine.PhotoUploaded += url => Console.WriteLine($"  [UPLOADED]  {url}");

Console.WriteLine("=== Focus & Snap state machine simulation ===");
Console.WriteLine("Running 5 event-mode sessions against the mock camera and printer.\n");

for (int i = 1; i <= 3; i++)
{
    Console.WriteLine($"--- Session {i} (event) ---");

    // Force session 2 to hit a capture failure, to prove the error path
    // actually recovers back to Idle instead of hanging.
    if (i == 2)
    {
        camera.FailNextCapture = true;
        Console.WriteLine("  (forcing a simulated capture failure this session)");
    }

    // Force session 3's upload to fail (dropped venue WiFi), to prove the
    // photo gets queued instead of just silently lost -- the print and the
    // guest-facing flow don't even notice.
    if (i == 3)
    {
        cloudUpload.FailNextUpload = true;
        Console.WriteLine("  (forcing a simulated upload failure this session)");
    }

    await eventMachine.RunSessionAsync();
    PrintNewEmails(email);
    Console.WriteLine();
}

var pendingAfterSession3 = await uploadQueue.GetPendingAsync();
Console.WriteLine($"Pending uploads after session 3: {pendingAfterSession3.Count} (queued while offline)\n");

Console.WriteLine("--- Session 4 (event) ---");
Console.WriteLine("  (guest declines the liability disclaimer -- no countdown/capture/print at all)");
consent.DeclineNext = true;
await eventMachine.RunSessionAsync();
PrintNewEmails(email);
Console.WriteLine();

// Same camera/printer/cloudUpload/sessions/uploadQueue, but a separate
// machine instance in vendo mode -- mode is fixed per machine (one booth
// serves one location), so a vendo-mode run needs its own instance, same as
// a real vendo-location deployment would construct. The queue is shared
// across both, same as it would be for one physical booth machine.
var vendoMachine = new BoothStateMachine(services, mode: "vendo");
vendoMachine.StateChanged += state => Console.WriteLine($"  [STATE]     {state}");
vendoMachine.CountdownTick += n => Console.WriteLine($"  [COUNTDOWN] {n}");
vendoMachine.ErrorOccurred += msg => Console.WriteLine($"  [ERROR]     {msg}");
vendoMachine.PhotoUploaded += url => Console.WriteLine($"  [UPLOADED]  {url}");

Console.WriteLine("--- Session 5 (vendo) ---");
Console.WriteLine("  (proving the Payment state runs before Printing in vendo mode;");
Console.WriteLine("   also, network's back, so session 3's queued upload should flush here too)");
await vendoMachine.RunSessionAsync();
PrintNewEmails(email);
Console.WriteLine();

var pendingAfterSession5 = await uploadQueue.GetPendingAsync();
Console.WriteLine($"Pending uploads after session 5: {pendingAfterSession5.Count} (retried opportunistically at session 5's start)\n");

// A second IPaymentService implementation -- a simulated card reader instead
// of a QR scan -- proving the interface actually generalizes beyond
// "generate a QR code". Its own machine instance, since a gateway (like
// mode) is fixed per booth.
var cardPayment = new MockCardReaderPaymentService();
var cardMachine = new BoothStateMachine(services with { Payment = cardPayment }, mode: "vendo");
cardMachine.StateChanged += state => Console.WriteLine($"  [STATE]     {state}");
cardMachine.CountdownTick += n => Console.WriteLine($"  [COUNTDOWN] {n}");
cardMachine.ErrorOccurred += msg => Console.WriteLine($"  [ERROR]     {msg}");
cardMachine.PhotoUploaded += url => Console.WriteLine($"  [UPLOADED]  {url}");

Console.WriteLine("--- Session 6 (vendo, card reader) ---");
Console.WriteLine("  (second IPaymentService implementation -- no QR code, guest taps a card)");
await cardMachine.RunSessionAsync();
Console.WriteLine($"  Payment prompt shown: \"{cardMachine.PaymentInstructions}\" (QR code: {(cardMachine.PaymentQrPng is null ? "none" : "present")})");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("--- Session 7 (vendo, card reader) ---");
Console.WriteLine("  (card declined -- proves the payment-declined path, never exercised before");
Console.WriteLine("   since the QR mock always used to succeed)");
cardPayment.DeclineNext = true;
await cardMachine.RunSessionAsync();
PrintNewEmails(email);
Console.WriteLine();

// Glam Booth mode and countdown duration are admin-editable settings now
// (see AdminWindow's Settings section), not constructor flags -- flip them
// on the shared MockBoothSettingsProvider, same as an admin saving a
// change, and prove BoothStateMachine picks it up on the very next session
// without needing a new instance or a restart.
Console.WriteLine("--- Session 8 (event, Glam Booth mode + 5s countdown) ---");
Console.WriteLine("  (simulating an admin turning on Glam Booth mode and lengthening the countdown)");
settings.Settings = new BoothSettings(CountdownSeconds: 5, GlamFilterEnabled: true, PrintTemplate: PrintTemplate.Default);
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Final photo path: {eventMachine.LastCapturedImagePath}");
PrintNewEmails(email);
Console.WriteLine();

// Print template is the same kind of admin-editable setting (see
// AdminWindow's Settings section) -- switching to a 2x6 strip here proves
// IPrinterService actually receives the current template, not just a
// hardcoded 4x6.
Console.WriteLine("--- Session 9 (event, admin switches to a 2x6 strip template) ---");
Console.WriteLine("  (simulating an admin switching the print layout from the default single 4x6)");
settings.Settings = settings.Settings with { PrintTemplate = new PrintTemplate("Strip", WidthInches: 2, HeightInches: 6, StripCopies: 2) };
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Printed with: {printer.PrintedTemplates[^1]}");
PrintNewEmails(email);
Console.WriteLine();

// Frame library is admin-managed and off by default (an empty Frame table,
// same as this MockFrameLibraryService's default) -- simulating an admin
// adding two frames here, same as flipping settings.Settings above.
Console.WriteLine("--- Session 10 (event, frame library has two frames) ---");
Console.WriteLine("  (simulating an admin adding frame overlays -- guest picks the first one)");
frameLibrary.Frames = new List<FrameOption>
{
    new(1, "Classic Gold Border", "./frames/gold_border.png"),
    new(2, "Confetti", "./frames/confetti.png"),
};
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Frame picked: {eventMachine.LastSelectedFrame?.Name ?? "(none)"}");
Console.WriteLine($"  Final photo path: {eventMachine.LastCapturedImagePath}");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("--- Session 11 (event, guest skips the frame) ---");
frameSelection.SkipNext = true;
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Frame picked: {eventMachine.LastSelectedFrame?.Name ?? "(none)"}");
Console.WriteLine($"  Final photo path: {eventMachine.LastCapturedImagePath}");
PrintNewEmails(email);
Console.WriteLine();

// General feedback survey -- MockFeedbackService defaults to a 5-star
// rating with no comment, so simulate a guest leaving a comment too.
Console.WriteLine("--- Session 12 (event, guest leaves a 4-star rating and a comment) ---");
feedback.SimulateRating = 4;
feedback.SimulateComment = "Loved the frames, printer was a little slow.";
await eventMachine.RunSessionAsync();
var lastFeedback = sessions.RecordedFeedback[^1];
Console.WriteLine($"  Feedback recorded: {lastFeedback.Rating} stars -- \"{lastFeedback.Comment}\"");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("--- Session 13 (event, guest skips feedback entirely) ---");
feedback.SkipNext = true;
int feedbackCountBefore = sessions.RecordedFeedback.Count;
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Feedback recorded this session: {sessions.RecordedFeedback.Count > feedbackCountBefore} " +
    "(false is correct -- an empty skip leaves no Feedback row)");
PrintNewEmails(email);
Console.WriteLine();

// Video guestbook -- MockGuestbookPromptService defaults to "wants to
// record", so this proves a guest message actually gets captured and
// recorded against the session, distinct from and after Complete/before
// Feedback.
Console.WriteLine("--- Session 14 (event, guest records a guestbook message) ---");
await eventMachine.RunSessionAsync();
var lastVideo = sessions.RecordedGuestbookVideos[^1];
Console.WriteLine($"  Guestbook video recorded: session {lastVideo.SessionId}, {lastVideo.FilePath}, {lastVideo.Duration.TotalMilliseconds:0}ms");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("--- Session 15 (event, guest declines the guestbook prompt) ---");
guestbookPrompt.SkipNext = true;
int guestbookCountBefore = sessions.RecordedGuestbookVideos.Count;
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Guestbook video recorded this session: {sessions.RecordedGuestbookVideos.Count > guestbookCountBefore} " +
    "(false is correct -- a decline leaves no GuestbookVideo row)");
PrintNewEmails(email);
Console.WriteLine();

// Theme (colors/logo/event name) is the same kind of admin-editable setting
// as everything else in BoothSettings -- flipping it here proves
// IPhotoBrandingService's caption actually uses the current event name, not
// a hardcoded "Focus & Snap".
Console.WriteLine("--- Session 16 (event, admin changes the event theme) ---");
Console.WriteLine("  (simulating an admin renaming the event and changing brand colors)");
settings.Settings = settings.Settings with { Theme = new BoothTheme("#B3261E", "#FFFFFF", "#111111", null, "Sunset Social") };
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Branding applied with studio name: {branding.LastStudioName}");
PrintNewEmails(email);
Console.WriteLine();

// Print template elements (logo/text overlays placed via the admin's
// drag-and-drop editor) ride inside PrintTemplate.Elements, already threaded
// through settings.PrintTemplate -> IPrinterService.PrintAsync with zero
// BoothStateMachine changes -- this proves the elements actually reach the
// printer, not just that the code compiles.
Console.WriteLine("--- Session 17 (event, admin adds a logo and a caption to the print template) ---");
Console.WriteLine("  (simulating an admin placing two elements via the print template editor)");
var printElements = new List<PrintTemplateElement>
{
    new(PrintTemplateElementKind.Text, XPercent: 0.1, YPercent: 0.88, WidthPercent: 0.8, HeightPercent: 0.08, Text: "Sunset Social"),
    new(PrintTemplateElementKind.Logo, XPercent: 0.75, YPercent: 0.03, WidthPercent: 0.2, HeightPercent: 0.08, ImagePath: "./assets/studio_logo.png"),
};
settings.Settings = settings.Settings with { PrintTemplate = settings.Settings.PrintTemplate with { Elements = printElements } };
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Printed with {printer.PrintedTemplates[^1].Elements.Count} template element(s) (expected 2)");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("Demo complete. Final state: " + vendoMachine.CurrentState);
Console.WriteLine($"Sessions recorded: {sessions.CreatedSessions.Count} " +
    $"({sessions.CompletedSessionIds.Count} completed, {sessions.FailedSessionIds.Count} failed, " +
    $"{sessions.AbandonedSessionIds.Count} abandoned), " +
    $"{sessions.RecordedPrints.Count} prints, {sessions.RecordedPayments.Count} payments, " +
    $"{sessions.RecordedConsents.Count} consent records, {sessions.RecordedFeedback.Count} feedback records, " +
    $"{sessions.RecordedGuestbookVideos.Count} guestbook videos, {email.SentEmails.Count} emails sent.");
foreach (var p in sessions.RecordedPayments)
{
    Console.WriteLine($"  Payment: session {p.SessionId}, {p.Amount:C}, {p.Method}");
}
