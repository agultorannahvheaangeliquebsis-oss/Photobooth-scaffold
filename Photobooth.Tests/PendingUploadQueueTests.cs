using Photobooth.Core;

namespace Photobooth.Tests;

public class MockPendingUploadQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ThenGetPendingAsync_ReturnsTheFileAndEmail()
    {
        var queue = new MockPendingUploadQueue();

        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        var pending = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal("./captures/one.bmp", pending.FilePath);
        Assert.Equal("guest@example.com", pending.Email);
    }

    [Fact]
    public async Task EnqueueAsync_WithNoEmail_ReturnsNullEmail()
    {
        var queue = new MockPendingUploadQueue();

        await queue.EnqueueAsync("./captures/one.bmp", email: null);

        var pending = Assert.Single(await queue.GetPendingAsync());
        Assert.Null(pending.Email);
    }

    [Fact]
    public async Task EnqueueAsync_SamePathTwice_OnlyAppearsOnce()
    {
        var queue = new MockPendingUploadQueue();

        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        Assert.Single(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task RemoveAsync_TakesTheFileOutOfThePendingList()
    {
        var queue = new MockPendingUploadQueue();
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        await queue.RemoveAsync("./captures/one.bmp");

        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task DequeueAllAsync_ReturnsEverythingAndEmptiesTheQueue()
    {
        var queue = new MockPendingUploadQueue();
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");
        await queue.EnqueueAsync("./captures/two.bmp", email: null);

        var claimed = await queue.DequeueAllAsync();

        Assert.Equal(2, claimed.Count);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task DequeueAllAsync_CalledTwiceConcurrently_OnlyOneCallerGetsTheItem()
    {
        var queue = new MockPendingUploadQueue();
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        var results = await Task.WhenAll(queue.DequeueAllAsync(), queue.DequeueAllAsync());

        int totalClaimed = results[0].Count + results[1].Count;
        Assert.Equal(1, totalClaimed);
    }
}

public class FileSystemPendingUploadQueueTests : IDisposable
{
    private readonly string _queueFilePath = Path.Combine(Path.GetTempPath(), $"pending_uploads_test_{Guid.NewGuid():N}.json");

    [Fact]
    public async Task EnqueueAsync_ThenGetPendingAsync_ReturnsTheFileAndEmail()
    {
        var queue = new FileSystemPendingUploadQueue(_queueFilePath);

        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        var pending = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal("./captures/one.bmp", pending.FilePath);
        Assert.Equal("guest@example.com", pending.Email);
    }

    [Fact]
    public async Task EnqueueAsync_SamePathTwice_OnlyAppearsOnce()
    {
        var queue = new FileSystemPendingUploadQueue(_queueFilePath);

        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        Assert.Single(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task RemoveAsync_TakesTheFileOutOfThePendingList()
    {
        var queue = new FileSystemPendingUploadQueue(_queueFilePath);
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");

        await queue.RemoveAsync("./captures/one.bmp");

        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task GetPendingAsync_SurvivesACreatingANewInstance_LikeAnAppRestart()
    {
        var firstInstance = new FileSystemPendingUploadQueue(_queueFilePath);
        await firstInstance.EnqueueAsync("./captures/left_over.bmp", "guest@example.com");

        var secondInstance = new FileSystemPendingUploadQueue(_queueFilePath);

        var pending = Assert.Single(await secondInstance.GetPendingAsync());
        Assert.Equal("./captures/left_over.bmp", pending.FilePath);
        Assert.Equal("guest@example.com", pending.Email);
    }

    [Fact]
    public async Task GetPendingAsync_WhenNoQueueFileExistsYet_ReturnsEmpty()
    {
        var queue = new FileSystemPendingUploadQueue(_queueFilePath);

        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task DequeueAllAsync_ReturnsEverythingAndEmptiesTheQueue()
    {
        var queue = new FileSystemPendingUploadQueue(_queueFilePath);
        await queue.EnqueueAsync("./captures/one.bmp", "guest@example.com");
        await queue.EnqueueAsync("./captures/two.bmp", email: null);

        var claimed = await queue.DequeueAllAsync();

        Assert.Equal(2, claimed.Count);
        Assert.Empty(await queue.GetPendingAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_queueFilePath))
        {
            File.Delete(_queueFilePath);
        }
    }
}

/// <summary>
/// The on-disk queue's durability, which is the whole reason it's a file and
/// not a database table: it has to work when the venue's network (and, on a
/// kiosk, sometimes the power) is what failed.
/// </summary>
public class FileSystemPendingUploadQueueDurabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "photobooth-queue-tests", Guid.NewGuid().ToString("N"));

    private string QueuePath => Path.Combine(_directory, "pending_uploads.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only -- a locked temp file isn't worth failing over.
        }
    }

    [Fact]
    public async Task ReadAsync_HalfWrittenQueueFile_QuarantinesItAndKeepsWorking()
    {
        // File.Create truncated the live file in place, so a power cut
        // mid-write left invalid JSON behind -- and every later read threw,
        // which permanently disabled Enqueue, DequeueAll and the app-startup
        // flush alike. One bad file must cost one backlog, not the feature.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(QueuePath, "[{\"FilePath\":\"./captures/one.bmp\",\"Ema");

        var queue = new FileSystemPendingUploadQueue(QueuePath);

        Assert.Empty(await queue.GetPendingAsync());

        // The corrupt file is kept for diagnosis rather than silently deleted.
        Assert.True(File.Exists(QueuePath + ".corrupt"));

        // And the queue is usable again, which is the part that was broken.
        await queue.EnqueueAsync("./captures/two.bmp", "guest@example.com");
        var pending = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal("./captures/two.bmp", pending.FilePath);
    }

    [Fact]
    public async Task EnqueueAsync_LeavesNoTemporaryFileBehind()
    {
        // The write-then-replace that makes the above impossible in the first
        // place: the live file is always either the old contents or the new
        // ones, and the temp file it goes through is never left lying around.
        var queue = new FileSystemPendingUploadQueue(QueuePath);

        await queue.EnqueueAsync("./captures/one.bmp", null);
        await queue.EnqueueAsync("./captures/two.bmp", null);

        Assert.False(File.Exists(QueuePath + ".tmp"));
        Assert.Equal(2, (await queue.GetPendingAsync()).Count);

        // And it round-trips as valid JSON after repeated rewrites.
        var reopened = new FileSystemPendingUploadQueue(QueuePath);
        Assert.Equal(2, (await reopened.GetPendingAsync()).Count);
    }
}
