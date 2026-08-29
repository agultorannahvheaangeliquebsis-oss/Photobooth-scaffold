namespace Photobooth.Core;

/// <summary>
/// In-memory stand-in for ISessionRepository, used by the console demo and
/// tests so they can exercise BoothStateMachine without a database. Exposes
/// what it recorded so tests can assert against it.
/// </summary>
public class MockSessionRepository : ISessionRepository
{
    private int _nextSessionId = 1;

    public List<(int SessionId, string Mode)> CreatedSessions { get; } = new();
    public List<int> CompletedSessionIds { get; } = new();
    public List<int> FailedSessionIds { get; } = new();
    public List<(int SessionId, string FilePath)> RecordedPrints { get; } = new();
    public List<(int SessionId, decimal Amount, string Method)> RecordedPayments { get; } = new();

    public Task<int> CreateAsync(string mode, CancellationToken ct = default)
    {
        int sessionId = _nextSessionId++;
        CreatedSessions.Add((sessionId, mode));
        return Task.FromResult(sessionId);
    }

    public Task CompleteAsync(int sessionId, CancellationToken ct = default)
    {
        CompletedSessionIds.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task FailAsync(int sessionId, CancellationToken ct = default)
    {
        FailedSessionIds.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task RecordPrintAsync(int sessionId, string filePath, CancellationToken ct = default)
    {
        RecordedPrints.Add((sessionId, filePath));
        return Task.CompletedTask;
    }

    public Task RecordPaymentAsync(int sessionId, decimal amount, string method, CancellationToken ct = default)
    {
        RecordedPayments.Add((sessionId, amount, method));
        return Task.CompletedTask;
    }
}
