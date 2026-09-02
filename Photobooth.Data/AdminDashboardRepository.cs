using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record RevenueByMode(string Mode, decimal Revenue);
public record InventoryAlert(int PrinterId, string Model, string ItemType, int QuantityRemaining);
public record FeedbackSummary(double? AverageRating, int RatingCount);
public record RecentFeedbackComment(int SessionId, string Comment, DateTime RecordedAt);
public record GuestbookVideoRecord(int GuestbookVideoId, int SessionId, string FilePath, int DurationSeconds, DateTime RecordedAt);
public record SessionLogRow(int SessionId, string Mode, DateTime StartedAt, DateTime? EndedAt, string Status);
public record FeedbackExportRow(int SessionId, int? Rating, string? Comment, DateTime RecordedAt);

/// <summary>
/// Read-only queries backing the admin dashboard: sessions today, revenue
/// by mode, and low-inventory alerts. No write methods -- inventory rows
/// are logged elsewhere (currently just the DatabaseInitializer seed; a
/// real decrement-on-print hook is future work).
/// </summary>
public class AdminDashboardRepository
{
    public async Task<int> GetSessionsTodayCountAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT COUNT(*) FROM Session WHERE CAST(StartedAt AS DATE) = CAST(SYSUTCDATETIME() AS DATE);",
            connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<RevenueByMode>> GetRevenueByModeAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT s.Mode, SUM(p.Amount) AS Revenue
            FROM Payment p
            JOIN Session s ON s.SessionId = p.SessionId
            WHERE p.Status = 'paid'
            GROUP BY s.Mode;
            """,
            connection);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RevenueByMode>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RevenueByMode(reader.GetString(0), reader.GetDecimal(1)));
        }
        return results;
    }

    /// <summary>Printers whose most recently logged quantity for any item (paper/ink/ribbon) is at or below the threshold. "Most recent" is per printer+item, not just per printer, since one printer logs both paper and ribbon independently.</summary>
    public async Task<List<InventoryAlert>> GetLowInventoryAlertsAsync(int threshold = 20, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            WITH Latest AS (
                SELECT PrinterId, ItemType, QuantityRemaining,
                       ROW_NUMBER() OVER (PARTITION BY PrinterId, ItemType ORDER BY LoggedAt DESC) AS rn
                FROM InventoryLog
            )
            SELECT pr.PrinterId, pr.Model, l.ItemType, l.QuantityRemaining
            FROM Latest l
            JOIN Printer pr ON pr.PrinterId = l.PrinterId
            WHERE l.rn = 1 AND l.QuantityRemaining <= @Threshold
            ORDER BY l.QuantityRemaining;
            """,
            connection);
        command.Parameters.AddWithValue("@Threshold", threshold);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<InventoryAlert>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new InventoryAlert(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }
        return results;
    }

    /// <summary>Average rating and how many guests actually left one -- Rating is
    /// nullable in Feedback (a guest can leave a comment with no rating, or vice
    /// versa), so AVG/COUNT here only ever see the rows that have one.</summary>
    public async Task<FeedbackSummary> GetFeedbackSummaryAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT AVG(CAST(Rating AS FLOAT)), COUNT(Rating) FROM Feedback WHERE Rating IS NOT NULL;",
            connection);
        using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        return new FeedbackSummary(
            reader.IsDBNull(0) ? null : reader.GetDouble(0),
            reader.GetInt32(1));
    }

    /// <summary>Most recent guest comments, newest first -- gives an admin something to
    /// actually read, not just an average number.</summary>
    public async Task<List<RecentFeedbackComment>> GetRecentCommentsAsync(int limit = 5, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT TOP (@Limit) SessionId, Comment, RecordedAt
            FROM Feedback
            WHERE Comment IS NOT NULL
            ORDER BY RecordedAt DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@Limit", limit);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RecentFeedbackComment>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RecentFeedbackComment(reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2)));
        }
        return results;
    }

    /// <summary>Most recent guestbook recordings, newest first -- see the Guestbook
    /// section of AdminWindow, GuestbookVideoRepository's schema note on why these
    /// aren't uploaded/QR'd/printed like the photo is.</summary>
    public async Task<List<GuestbookVideoRecord>> GetRecentGuestbookVideosAsync(int limit = 20, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT TOP (@Limit) GuestbookVideoId, SessionId, FilePath, DurationSeconds, RecordedAt
            FROM GuestbookVideo
            ORDER BY RecordedAt DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@Limit", limit);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<GuestbookVideoRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new GuestbookVideoRecord(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetDateTime(4)));
        }
        return results;
    }

    /// <summary>Deletes one guestbook recording's row. Leaves the physical file on
    /// disk (same "nothing deletes old print files either" precedent) -- this just
    /// removes it from the admin's list.</summary>
    public async Task DeleteGuestbookVideoAsync(int guestbookVideoId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM GuestbookVideo WHERE GuestbookVideoId = @Id;", connection);
        command.Parameters.AddWithValue("@Id", guestbookVideoId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Every session for one event, newest first -- backs Export
    /// Event's "Session log (CSV)" (see AdminWindow's Export Event section).
    /// Location-scoped (unlike the aggregate queries above, which predate
    /// multi-event support) since an export is specifically about one event,
    /// never "every event this booth machine has ever run".</summary>
    public async Task<List<SessionLogRow>> GetSessionLogAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT SessionId, Mode, StartedAt, EndedAt, Status FROM Session WHERE LocationId = @LocationId ORDER BY StartedAt DESC;",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<SessionLogRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SessionLogRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3), reader.GetString(4)));
        }
        return results;
    }

    /// <summary>Every feedback row for one event, newest first -- backs Export
    /// Event's "Guest feedback (CSV)" (see AdminWindow's Export Event
    /// section). Unlike GetRecentCommentsAsync (capped at a handful, comment-only,
    /// unscoped), this is the complete record for one event -- an export
    /// should never silently drop rows past some display-only limit.</summary>
    public async Task<List<FeedbackExportRow>> GetAllFeedbackAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT f.SessionId, f.Rating, f.Comment, f.RecordedAt
            FROM Feedback f
            JOIN Session s ON s.SessionId = f.SessionId
            WHERE s.LocationId = @LocationId
            ORDER BY f.RecordedAt DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<FeedbackExportRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FeedbackExportRow(
                reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTime(3)));
        }
        return results;
    }
}
