using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record LocationRecord(int LocationId, string Name, string Type, string? Address, int CountdownSeconds, bool GlamFilterEnabled);

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
        using var command = new SqlCommand(
            "SELECT LocationId, Name, Type, Address, CountdownSeconds, GlamFilterEnabled FROM Location ORDER BY LocationId;",
            connection);
        using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<LocationRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new LocationRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetBoolean(5)));
        }
        return results;
    }

    /// <summary>Updates the admin-editable booth settings for a location -- countdown
    /// duration and whether Glam Booth mode is on. Read fresh by SqlBoothSettingsProvider
    /// at the start of every session, so a change here takes effect for the very next
    /// guest without needing to restart the app.</summary>
    public async Task UpdateSettingsAsync(int locationId, int countdownSeconds, bool glamFilterEnabled, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "UPDATE Location SET CountdownSeconds = @CountdownSeconds, GlamFilterEnabled = @GlamFilterEnabled WHERE LocationId = @LocationId;",
            connection);
        command.Parameters.AddWithValue("@CountdownSeconds", countdownSeconds);
        command.Parameters.AddWithValue("@GlamFilterEnabled", glamFilterEnabled);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
