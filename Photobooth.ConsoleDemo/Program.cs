using Photobooth.Core;

var camera = new MockCameraService();
var printer = new MockPrinterService();
var cloudUpload = new MockCloudUploadService();
var sessions = new MockSessionRepository();
var machine = new BoothStateMachine(camera, printer, cloudUpload, sessions);

machine.StateChanged += state => Console.WriteLine($"  [STATE]     {state}");
machine.CountdownTick += n => Console.WriteLine($"  [COUNTDOWN] {n}");
machine.ErrorOccurred += msg => Console.WriteLine($"  [ERROR]     {msg}");
machine.PhotoUploaded += url => Console.WriteLine($"  [UPLOADED]  {url}");

Console.WriteLine("=== Focus & Snap state machine simulation ===");
Console.WriteLine("Running 3 sessions against the mock camera and printer.\n");

for (int i = 1; i <= 3; i++)
{
    Console.WriteLine($"--- Session {i} ---");

    // Force session 2 to hit a capture failure, to prove the error path
    // actually recovers back to Idle instead of hanging.
    if (i == 2)
    {
        camera.FailNextCapture = true;
        Console.WriteLine("  (forcing a simulated capture failure this session)");
    }

    await machine.RunSessionAsync();
    Console.WriteLine();
}

Console.WriteLine("Demo complete. Final state: " + machine.CurrentState);
Console.WriteLine($"Sessions recorded: {sessions.CreatedSessions.Count} " +
    $"({sessions.CompletedSessionIds.Count} completed, {sessions.FailedSessionIds.Count} failed), " +
    $"{sessions.RecordedPrints.Count} prints, {sessions.RecordedPayments.Count} payments.");
