using System.Text.Json;
using Serilog;

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

        try
        {
            await using FileStream stream = File.OpenRead(_queueFilePath);
            List<PendingUpload>? pending = await JsonSerializer.DeserializeAsync<List<PendingUpload>>(stream, cancellationToken: ct);
            return pending ?? new List<PendingUpload>();
        }
        catch (JsonException ex)
        {
            // A corrupt queue file used to be permanent: every read threw, so
            // Enqueue, DequeueAll and the app-startup flush all failed from
            // then on, and no guest whose upload had failed ever got their
            // photo again. One bad file should cost one backlog, not the
            // feature -- so move it aside (keeping it for diagnosis rather
            // than deleting evidence) and carry on with an empty queue.
            string quarantine = _queueFilePath + ".corrupt";
            Log.Warning(ex, "Pending upload queue at {QueueFile} was unreadable; moving it to {Quarantine} and starting a fresh queue", _queueFilePath, quarantine);
            try
            {
                File.Move(_queueFilePath, quarantine, overwrite: true);
            }
            catch (Exception moveEx)
            {
                Log.Warning(moveEx, "Couldn't quarantine the corrupt upload queue at {QueueFile}", _queueFilePath);
            }

            return new List<PendingUpload>();
        }
    }

    private async Task WriteAsync(List<PendingUpload> pending, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_queueFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-then-replace rather than writing over the live file. File.Create
        // truncates in place, so a power cut mid-write -- the exact thing a
        // kiosk faces, and the exact thing this queue exists to survive -- left
        // a half-written JSON file behind. See ReadAsync for what that used to
        // cost. A temp file that's fully written and only then moved into place
        // means the live file is always either the old contents or the new ones.
        string temporaryPath = _queueFilePath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, pending, cancellationToken: ct);
        }

        File.Move(temporaryPath, _queueFilePath, overwrite: true);
    }
}
