namespace Photobooth.Core;

/// <summary>
/// Abstracts persisting a guest session's lifecycle to the database. Same
/// seam as ICameraService/IPrinterService/ICloudUploadService: the state
/// machine only knows about this interface, never about SQL directly, so
/// the console demo and tests can run against MockSessionRepository while
/// the real WPF app runs against a LocalDB-backed implementation.
/// </summary>
public interface ISessionRepository
{
    /// <summary>Inserts a new Session row and returns its SessionId.</summary>
    Task<int> CreateAsync(string mode, CancellationToken ct = default);

    /// <summary>Marks a session Completed and stamps EndedAt.</summary>
    Task CompleteAsync(int sessionId, CancellationToken ct = default);

    /// <summary>Marks a session Error and stamps EndedAt.</summary>
    Task FailAsync(int sessionId, CancellationToken ct = default);

    /// <summary>Marks a session Abandoned and stamps EndedAt -- for a guest who declined the disclaimer, not an error.</summary>
    Task AbandonAsync(int sessionId, CancellationToken ct = default);

    /// <summary>Inserts a Print row for a successful print in this session.</summary>
    Task RecordPrintAsync(int sessionId, string filePath, CancellationToken ct = default);

    /// <summary>Inserts a paid Payment row for this session (e.g. 'free_event' for event mode, 'qr_gcash'/'qr_maya'/'card' for vendo).</summary>
    Task RecordPaymentAsync(int sessionId, decimal amount, string method, CancellationToken ct = default);

    /// <summary>Inserts a Consent row recording the disclaimer/email-opt-in outcome for this session, whether accepted or declined.</summary>
    Task RecordConsentAsync(int sessionId, bool disclaimerAccepted, bool emailOptIn, string? email, CancellationToken ct = default);
}
