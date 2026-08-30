using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record RevenueByMode(string Mode, decimal Revenue);
public record InventoryAlert(int PrinterId, string Model, string ItemType, int QuantityRemaining);

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
}
