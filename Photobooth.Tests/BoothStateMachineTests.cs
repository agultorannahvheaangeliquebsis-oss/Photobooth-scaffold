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
        var sessions = new MockSessionRepository();
        var machine = new BoothStateMachine(camera, printer, cloudUpload, sessions);

        var states = new List<BoothState>();
        machine.StateChanged += state => states.Add(state);

        await machine.RunSessionAsync();

        Assert.Equal(
            new[]
            {
                BoothState.Countdown, BoothState.Capturing, BoothState.Reviewing,
                BoothState.Printing, BoothState.Complete, BoothState.Idle,
            },
            states);
        Assert.Equal(BoothState.Idle, machine.CurrentState);
        Assert.NotNull(machine.LastCapturedImagePath);
        Assert.True(File.Exists(machine.LastCapturedImagePath));

        var createdSession = Assert.Single(sessions.CreatedSessions);
        Assert.Equal("event", createdSession.Mode);
        int sessionId = createdSession.SessionId;

        Assert.Equal(sessionId, Assert.Single(sessions.CompletedSessionIds));
        Assert.Empty(sessions.FailedSessionIds);

        var print = Assert.Single(sessions.RecordedPrints);
        Assert.Equal(sessionId, print.SessionId);
        Assert.Equal(machine.LastCapturedImagePath, print.FilePath);

        var payment = Assert.Single(sessions.RecordedPayments);
        Assert.Equal(sessionId, payment.SessionId);
        Assert.Equal(0m, payment.Amount);
        Assert.Equal("free_event", payment.Method);
    }

    [Fact]
    public async Task RunSessionAsync_CaptureFails_RecordsFailureAndSkipsPrintAndPayment()
    {
        var camera = new MockCameraService { FailNextCapture = true };
        var printer = new MockPrinterService();
        var cloudUpload = new MockCloudUploadService();
        var sessions = new MockSessionRepository();
        var machine = new BoothStateMachine(camera, printer, cloudUpload, sessions);

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

        // FailNextCapture resets itself after firing, so a booth doesn't get
        // stuck failing every session after one simulated failure.
        Assert.False(camera.FailNextCapture);
    }
}
