using Photobooth.Core;

namespace Photobooth.Tests;

public class MockSessionRepositoryTests
{
    [Fact]
    public async Task CreateAsync_AssignsIncrementingSessionIds()
    {
        var repo = new MockSessionRepository();

        int first = await repo.CreateAsync("event");
        int second = await repo.CreateAsync("vendo");

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(new[] { (1, "event"), (2, "vendo") }, repo.CreatedSessions);
    }

    [Fact]
    public async Task RecordPrintAndPayment_AppendToTheirLists()
    {
        var repo = new MockSessionRepository();
        int sessionId = await repo.CreateAsync("event");

        await repo.RecordPrintAsync(sessionId, "./captures/mock_0001.bmp");
        await repo.RecordPaymentAsync(sessionId, 150m, "qr_gcash");
        await repo.CompleteAsync(sessionId);

        var print = Assert.Single(repo.RecordedPrints);
        Assert.Equal((sessionId, "./captures/mock_0001.bmp"), print);

        var payment = Assert.Single(repo.RecordedPayments);
        Assert.Equal((sessionId, 150m, "qr_gcash"), payment);

        Assert.Equal(sessionId, Assert.Single(repo.CompletedSessionIds));
    }

    [Fact]
    public async Task FailAsync_RecordsFailureSeparatelyFromCompletion()
    {
        var repo = new MockSessionRepository();
        int sessionId = await repo.CreateAsync("event");

        await repo.FailAsync(sessionId);

        Assert.Equal(sessionId, Assert.Single(repo.FailedSessionIds));
        Assert.Empty(repo.CompletedSessionIds);
    }
}
