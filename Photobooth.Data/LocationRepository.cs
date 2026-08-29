using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record LocationRecord(int LocationId, string Name, string Type, string? Address);

public class LocationRepository
{
    public async Task<int> InsertAsync(string name, string type, string? address, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Location (Name, Type, Address) OUTPUT INSERTED.LocationId VALUES (@Name, @Type, @Address);",
            connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<LocationRecord>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("SELECT LocationId, Name, Type, Address FROM Location ORDER BY LocationId;", connection);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<LocationRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new LocationRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return results;
    }
}
