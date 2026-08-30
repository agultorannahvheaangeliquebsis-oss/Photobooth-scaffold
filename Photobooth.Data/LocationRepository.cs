using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

public record LocationRecord(int LocationId, string Name, string Type, string? Address, int CountdownSeconds, bool GlamFilterEnabled, PrintTemplate PrintTemplate, BoothTheme Theme);

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
            """
            SELECT LocationId, Name, Type, Address, CountdownSeconds, GlamFilterEnabled,
                   PrintLayout, PrintWidthInches, PrintHeightInches, PrintStripCopies,
                   AccentColorHex, CanvasColorHex, InkColorHex, LogoImagePath, EventName
            FROM Location ORDER BY LocationId;
            """,
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
                reader.GetBoolean(5),
                new PrintTemplate(
                    reader.GetString(6),
                    (double)reader.GetDecimal(7),
                    (double)reader.GetDecimal(8),
                    reader.GetInt32(9)),
                new BoothTheme(
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetString(14))));
        }
        return results;
    }

    /// <summary>Updates the admin-editable brand identity for a location -- colors,
    /// logo, and event name. Kept separate from UpdateSettingsAsync so saving a
    /// theme change doesn't force the countdown/print-template fields to also
    /// validate, and vice versa.</summary>
    public async Task UpdateThemeAsync(int locationId, BoothTheme theme, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET AccentColorHex = @AccentColorHex, CanvasColorHex = @CanvasColorHex,
                                 InkColorHex = @InkColorHex, LogoImagePath = @LogoImagePath, EventName = @EventName
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@AccentColorHex", theme.AccentColorHex);
        command.Parameters.AddWithValue("@CanvasColorHex", theme.CanvasColorHex);
        command.Parameters.AddWithValue("@InkColorHex", theme.InkColorHex);
        command.Parameters.AddWithValue("@LogoImagePath", (object?)theme.LogoImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@EventName", theme.EventName);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the admin-editable booth settings for a location -- countdown
    /// duration, whether Glam Booth mode is on, and the print template. Read fresh by
    /// SqlBoothSettingsProvider at the start of every session, so a change here takes
    /// effect for the very next guest without needing to restart the app.</summary>
    public async Task UpdateSettingsAsync(int locationId, int countdownSeconds, bool glamFilterEnabled, PrintTemplate printTemplate, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET CountdownSeconds = @CountdownSeconds, GlamFilterEnabled = @GlamFilterEnabled,
                                 PrintLayout = @PrintLayout, PrintWidthInches = @PrintWidthInches,
                                 PrintHeightInches = @PrintHeightInches, PrintStripCopies = @PrintStripCopies
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@CountdownSeconds", countdownSeconds);
        command.Parameters.AddWithValue("@GlamFilterEnabled", glamFilterEnabled);
        command.Parameters.AddWithValue("@PrintLayout", printTemplate.Layout);
        command.Parameters.AddWithValue("@PrintWidthInches", printTemplate.WidthInches);
        command.Parameters.AddWithValue("@PrintHeightInches", printTemplate.HeightInches);
        command.Parameters.AddWithValue("@PrintStripCopies", printTemplate.StripCopies);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
