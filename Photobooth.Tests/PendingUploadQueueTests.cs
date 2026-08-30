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
