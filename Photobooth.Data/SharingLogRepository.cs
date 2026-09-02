using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

/// <summary>One row per guest email/SMS share attempt (see KioskViewModel's
/// SendEmailAsync/SendSmsAsync and AdminWindow's Sharing Status section).
/// No Core interface/mock here, unlike most repositories BoothStateMachine
/// depends on -- this is an admin-only read/write path with nothing for
/// BoothStateMachineTests to fake, same reasoning AdminDashboardRepository/
/// FrameRepository already have for staying plain Data-layer classes.</summary>
public record SharingLogRow(
    int SharingLogId, int SessionId, string Method, string Destination, string PhotoUrl,
    string Status, string? ErrorMessage, DateTime SentAt);

public class SharingLogRepository
{
    public async Task InsertAsync(
        int sessionId, string method, string destination, string photoUrl, string status, string? errorMessage,
        CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            INSERT INTO SharingLog (SessionId, Method, Destination, PhotoUrl, Status, ErrorMessage)
            VALUES (@SessionId, @Method, @Destination, @PhotoUrl, @Status, @ErrorMessage);
            """,
            connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@Method", method);
        command.Parameters.AddWithValue("@Destination", destination);
        command.Parameters.AddWithValue("@PhotoUrl", photoUrl);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Most recent share attempts for this location, newest first --
    /// joined through Session since SharingLog itself has no LocationId
    /// column of its own (same join-through-Session pattern
    /// AdminDashboardRepository's revenue-by-mode query already uses).</summary>
    public async Task<List<SharingLogRow>> GetRecentAsync(int locationId, int limit = 50, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT TOP (@Limit) sl.SharingLogId, sl.SessionId, sl.Method, sl.Destination, sl.PhotoUrl,
                   sl.Status, sl.ErrorMessage, sl.SentAt
            FROM SharingLog sl
            JOIN Session s ON s.SessionId = sl.SessionId
            WHERE s.LocationId = @LocationId
            ORDER BY sl.SentAt DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@Limit", limit);
        command.Parameters.AddWithValue("@LocationId", locationId);

        var results = new List<SharingLogRow>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SharingLogRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7)));
        }
        return results;
    }

    public async Task<(int Sent, int Failed)> GetSummaryAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT sl.Status, COUNT(*)
            FROM SharingLog sl
            JOIN Session s ON s.SessionId = sl.SessionId
            WHERE s.LocationId = @LocationId
            GROUP BY sl.Status;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        int sent = 0, failed = 0;
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string status = reader.GetString(0);
            int count = reader.GetInt32(1);
            if (status == "Sent") sent = count;
            else if (status == "Failed") failed = count;
        }
        return (sent, failed);
    }
}
