namespace Photobooth.Core;

/// <summary>A file waiting to be uploaded, plus the email (if any) to notify once it finally succeeds.</summary>
public record PendingUpload(string FilePath, string? Email);

/// <summary>
/// Durable backlog for photos whose cloud upload failed (e.g. the venue's
/// WiFi drops mid-event). Same interface-plus-mock seam as everything else
/// in this project -- BoothStateMachine only ever talks to this interface,
/// never to a specific storage mechanism.
/// </summary>
public interface IPendingUploadQueue
{
    /// <summary>Remembers that the file at this path still needs to be uploaded, and
    /// who (if anyone opted in during Consent) to email once it finally succeeds.</summary>
    Task EnqueueAsync(string filePath, string? email, CancellationToken ct = default);

    /// <summary>Every upload currently waiting, oldest first.</summary>
    Task<IReadOnlyList<PendingUpload>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Forgets a file path once its upload finally succeeds.</summary>
    Task RemoveAsync(string filePath, CancellationToken ct = default);

    /// <summary>Atomically returns everything currently queued and empties the
    /// queue in one step, so two callers retrying at the same time (e.g. two
    /// BoothStateMachine instances sharing this queue) can't both claim the
    /// same item -- whichever calls this first gets it all, the other gets
    /// nothing. Callers are expected to re-enqueue (via EnqueueAsync) any
    /// item whose retry still fails.</summary>
    Task<IReadOnlyList<PendingUpload>> DequeueAllAsync(CancellationToken ct = default);
}
