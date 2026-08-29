using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record PrinterRecord(int PrinterId, int LocationId, string Model, string? SerialNumber, string Status);

public class PrinterRepository
{
    public async Task<int> InsertAsync(int locationId, string model, string? serialNumber, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Printer (LocationId, Model, SerialNumber) OUTPUT INSERTED.PrinterId VALUES (@LocationId, @Model, @SerialNumber);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Model", model);
        command.Parameters.AddWithValue("@SerialNumber", (object?)serialNumber ?? DBNull.Value);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<PrinterRecord>> GetByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT PrinterId, LocationId, Model, SerialNumber, Status FROM Printer WHERE LocationId = @LocationId ORDER BY PrinterId;",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<PrinterRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PrinterRecord(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }
        return results;
    }
}
