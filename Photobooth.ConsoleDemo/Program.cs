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
int emailsPrinted = 0;
void PrintNewEmails(MockEmailDeliveryService service)
{
    for (; emailsPrinted < service.SentEmails.Count; emailsPrinted++)
    {
        var sent = service.SentEmails[emailsPrinted];
        Console.WriteLine($"  [EMAILED]   {sent.ToEmail} -> {sent.PhotoUrl}");
    }
}

var services = new BoothServices(camera, printer, cloudUpload, sessions, payment, uploadQueue, consent, email, branding, filter, settings);
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
settings.Settings = new BoothSettings(CountdownSeconds: 5, GlamFilterEnabled: true);
await eventMachine.RunSessionAsync();
Console.WriteLine($"  Final photo path: {eventMachine.LastCapturedImagePath}");
PrintNewEmails(email);
Console.WriteLine();

Console.WriteLine("Demo complete. Final state: " + vendoMachine.CurrentState);
Console.WriteLine($"Sessions recorded: {sessions.CreatedSessions.Count} " +
    $"({sessions.CompletedSessionIds.Count} completed, {sessions.FailedSessionIds.Count} failed, " +
    $"{sessions.AbandonedSessionIds.Count} abandoned), " +
    $"{sessions.RecordedPrints.Count} prints, {sessions.RecordedPayments.Count} payments, " +
    $"{sessions.RecordedConsents.Count} consent records, {email.SentEmails.Count} emails sent.");
foreach (var p in sessions.RecordedPayments)
{
    Console.WriteLine($"  Payment: session {p.SessionId}, {p.Amount:C}, {p.Method}");
}
