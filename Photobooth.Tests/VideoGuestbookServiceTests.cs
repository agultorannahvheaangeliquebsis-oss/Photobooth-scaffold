using Photobooth.Core;

namespace Photobooth.Tests;

public class VideoGuestbookServiceTests
{
    [Fact]
    public async Task StartThenStop_RoundTrips_RecordsAFileWithAPositiveDuration()
    {
        var service = new MockVideoGuestbookService();

        await service.StartRecordingAsync();
        await Task.Delay(20);
        GuestbookRecording recording = await service.StopRecordingAsync();

        Assert.True(File.Exists(recording.FilePath));
        Assert.True(recording.Duration > TimeSpan.Zero);
        Assert.Single(service.RecordedFiles);
        Assert.Equal(recording.FilePath, service.RecordedFiles[0]);
    }

    [Fact]
    public async Task StopRecordingAsync_NothingInProgress_Throws()
    {
        var service = new MockVideoGuestbookService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopRecordingAsync());
    }

    [Fact]
    public async Task FailNextStart_ThrowsOnceThenResets()
    {
        var service = new MockVideoGuestbookService { FailNextStart = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartRecordingAsync());
        Assert.False(service.FailNextStart);

        // Resets after firing, so the booth doesn't get stuck failing every
        // guestbook attempt after one simulated failure.
        await service.StartRecordingAsync();
        GuestbookRecording recording = await service.StopRecordingAsync();
        Assert.True(File.Exists(recording.FilePath));
    }

    [Fact]
    public async Task FailNextStop_ThrowsOnceThenResets()
    {
        var service = new MockVideoGuestbookService { FailNextStop = true };

        await service.StartRecordingAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopRecordingAsync());
        Assert.False(service.FailNextStop);
    }
}

public class MockGuestbookPromptServiceTests
{
    [Fact]
    public async Task AskToRecordAsync_Default_ReturnsTrue()
    {
        var service = new MockGuestbookPromptService();
        Assert.True(await service.AskToRecordAsync());
    }

    [Fact]
    public async Task AskToRecordAsync_SkipNext_ReturnsFalseThenResets()
    {
        var service = new MockGuestbookPromptService { SkipNext = true };

        Assert.False(await service.AskToRecordAsync());
        Assert.False(service.SkipNext);

        // Resets after firing -- the next ask goes back to the default.
        Assert.True(await service.AskToRecordAsync());
    }

    [Fact]
    public async Task AskToRecordAsync_SimulateWantsToRecordFalse_ReturnsFalse()
    {
        var service = new MockGuestbookPromptService { SimulateWantsToRecord = false };
        Assert.False(await service.AskToRecordAsync());
    }
}

public class UiGuestbookPromptServiceTests
{
    [Fact]
    public async Task AskToRecordAsync_DoesNotCompleteUntilSubmitRecordDecisionCalled()
    {
        var service = new UiGuestbookPromptService();
        bool requested = false;
        service.RecordDecisionRequested += () => requested = true;

        Task<bool> task = service.AskToRecordAsync();

        Assert.True(requested);
        Assert.False(task.IsCompleted);

        service.SubmitRecordDecision(true);

        Assert.True(await task);
    }

    [Fact]
    public async Task WaitForStopAsync_DoesNotCompleteUntilSubmitStopCalled()
    {
        var service = new UiGuestbookPromptService();
        bool requested = false;
        service.StopRequested += () => requested = true;

        Task task = service.WaitForStopAsync();

        Assert.True(requested);
        Assert.False(task.IsCompleted);

        service.SubmitStop();
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }
}
