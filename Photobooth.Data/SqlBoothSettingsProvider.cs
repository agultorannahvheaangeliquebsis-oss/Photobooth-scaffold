using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real IBoothSettingsProvider: reads CountdownSeconds/GlamFilterEnabled
/// straight from the Location row on every call (no caching) -- this is
/// what makes an admin's settings change take effect for the very next
/// guest session instead of needing an app restart.
/// </summary>
public class SqlBoothSettingsProvider : IBoothSettingsProvider
{
    private readonly int _locationId;

    public SqlBoothSettingsProvider(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT CountdownSeconds, GlamFilterEnabled FROM Location WHERE LocationId = @LocationId;",
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException($"Location {_locationId} not found -- can't read booth settings.");
        }

        return new BoothSettings(reader.GetInt32(0), reader.GetBoolean(1));
    }
}
