using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>
/// Real IBoothSettingsProvider: reads CountdownSeconds/GlamFilterEnabled/the
/// print template straight from the Location row on every call (no caching)
/// -- this is what makes an admin's settings change take effect for the very
/// next guest session instead of needing an app restart.
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
            """
            SELECT CountdownSeconds, GlamFilterEnabled, PrintLayout, PrintWidthInches, PrintHeightInches, PrintStripCopies,
                   AccentColorHex, CanvasColorHex, InkColorHex, LogoImagePath, EventName
            FROM Location WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);

        int countdownSeconds;
        bool glamFilterEnabled;
        var printTemplate = default(PrintTemplate)!;
        var theme = default(BoothTheme)!;
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException($"Location {_locationId} not found -- can't read booth settings.");
            }

            countdownSeconds = reader.GetInt32(0);
            glamFilterEnabled = reader.GetBoolean(1);
            printTemplate = new PrintTemplate(
                reader.GetString(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                reader.GetInt32(5));
            theme = new BoothTheme(
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10));
        }

        // A separate connection (not this method's `connection` above), so
        // this can only run once the reader above has finished either way --
        // fetched after, not interleaved with, the Location row read.
        printTemplate = printTemplate with
        {
            Elements = await new PrintTemplateElementRepository().GetAllByLocationAsync(_locationId, ct),
        };

        return new BoothSettings(countdownSeconds, glamFilterEnabled, printTemplate) { Theme = theme };
    }
}
