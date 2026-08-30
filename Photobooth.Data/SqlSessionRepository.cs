using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real ISessionRepository backed by LocalDB. Writes to Session, Print,
/// Payment, and Consent -- the tables a running session touches -- against
/// a fixed LocationId/PrinterId, since one booth machine has one location
/// and one printer attached. See DatabaseInitializer for how those get seeded.
/// </summary>
public class SqlSessionRepository : ISessionRepository
{
    private readonly int _locationId;
    private readonly int _printerId;

    public SqlSessionRepository(int locationId, int printerId)
    {
        _locationId = locationId;
        _printerId = printerId;
    }

    public async Task<int> CreateAsync(string mode, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Session (LocationId, Mode) OUTPUT INSERTED.SessionId VALUES (@LocationId, @Mode);",
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);
        command.Parameters.AddWithValue("@Mode", mode);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task CompleteAsync(int sessionId, CancellationToken ct = default) =>
        await SetStatusAsync(sessionId, "completed", ct);

    public async Task FailAsync(int sessionId, CancellationToken ct = default) =>
        await SetStatusAsync(sessionId, "error", ct);

    public async Task AbandonAsync(int sessionId, CancellationToken ct = default) =>
        await SetStatusAsync(sessionId, "abandoned", ct);

    private async Task SetStatusAsync(int sessionId, string status, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "UPDATE Session SET Status = @Status, EndedAt = SYSUTCDATETIME() WHERE SessionId = @SessionId;",
            connection);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordPrintAsync(int sessionId, string filePath, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO [Print] (SessionId, PrinterId, FilePath) VALUES (@SessionId, @PrinterId, @FilePath);",
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@PrinterId", _printerId);
        command.Parameters.AddWithValue("@FilePath", filePath);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordPaymentAsync(int sessionId, decimal amount, string method, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            INSERT INTO Payment (SessionId, Amount, Method, Status, PaidAt)
            VALUES (@SessionId, @Amount, @Method, 'paid', SYSUTCDATETIME());
            """,
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@Amount", amount);
        command.Parameters.AddWithValue("@Method", method);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordConsentAsync(int sessionId, bool disclaimerAccepted, bool emailOptIn, string? email, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            INSERT INTO Consent (SessionId, DisclaimerAccepted, EmailOptIn, Email)
            VALUES (@SessionId, @DisclaimerAccepted, @EmailOptIn, @Email);
            """,
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@DisclaimerAccepted", disclaimerAccepted);
        command.Parameters.AddWithValue("@EmailOptIn", emailOptIn);
        command.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordFeedbackAsync(int sessionId, int? rating, string? comment, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Feedback (SessionId, Rating, Comment) VALUES (@SessionId, @Rating, @Comment);",
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@Rating", (object?)rating ?? DBNull.Value);
        command.Parameters.AddWithValue("@Comment", (object?)comment ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordGuestbookVideoAsync(int sessionId, string filePath, TimeSpan duration, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO GuestbookVideo (SessionId, FilePath, DurationSeconds) VALUES (@SessionId, @FilePath, @DurationSeconds);",
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@FilePath", filePath);
        command.Parameters.AddWithValue("@DurationSeconds", (int)duration.TotalSeconds);
        await command.ExecuteNonQueryAsync(ct);
    }
}
