namespace Photobooth.Core;

/// <summary>In-memory stand-in for tests and the console demo -- no disk I/O, so nothing to clean up between runs.</summary>
public class MockPendingUploadQueue : IPendingUploadQueue
{
    private readonly List<PendingUpload> _pending = new();
    private readonly object _lock = new();

    public Task EnqueueAsync(string filePath, string? email, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_pending.Any(p => p.FilePath == filePath))
            {
                _pending.Add(new PendingUpload(filePath, email));
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingUpload>> GetPendingAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PendingUpload>>(_pending.ToList());
        }
    }

    public Task RemoveAsync(string filePath, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _pending.RemoveAll(p => p.FilePath == filePath);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingUpload>> DequeueAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<PendingUpload> claimed = _pending.ToList();
            _pending.Clear();
            return Task.FromResult(claimed);
        }
    }
}
