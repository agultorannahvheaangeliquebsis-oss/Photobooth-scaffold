using System.Text.Json;

namespace Photobooth.Core;

/// <summary>
/// Real backlog for failed uploads: a small JSON file next to the app so a
/// queued photo survives an app restart, not just a network blip that
/// resolves before the next retry. Deliberately not a database table --
/// this needs to work even if the venue's network is down, which is the
/// exact scenario it exists for, and a flat file has nothing else to be
/// unavailable.
/// </summary>
public class FileSystemPendingUploadQueue : IPendingUploadQueue
{
    private readonly string _queueFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileSystemPendingUploadQueue(string? queueFilePath = null)
    {
        _queueFilePath = queueFilePath ?? "./pending_uploads.json";
    }

    public async Task EnqueueAsync(string filePath, string? email, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            List<PendingUpload> pending = await ReadAsync(ct);
            if (!pending.Any(p => p.FilePath == filePath))
            {
                pending.Add(new PendingUpload(filePath, email));
                await WriteAsync(pending, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<PendingUpload>> GetPendingAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await ReadAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string filePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            List<PendingUpload> pending = await ReadAsync(ct);
            if (pending.RemoveAll(p => p.FilePath == filePath) > 0)
            {
                await WriteAsync(pending, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<PendingUpload>> DequeueAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            List<PendingUpload> claimed = await ReadAsync(ct);
            if (claimed.Count > 0)
            {
                await WriteAsync(new List<PendingUpload>(), ct);
            }
            return claimed;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<PendingUpload>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_queueFilePath))
        {
            return new List<PendingUpload>();
        }

        await using FileStream stream = File.OpenRead(_queueFilePath);
        List<PendingUpload>? pending = await JsonSerializer.DeserializeAsync<List<PendingUpload>>(stream, cancellationToken: ct);
        return pending ?? new List<PendingUpload>();
    }

    private async Task WriteAsync(List<PendingUpload> pending, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_queueFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(_queueFilePath);
        await JsonSerializer.SerializeAsync(stream, pending, cancellationToken: ct);
    }
}
