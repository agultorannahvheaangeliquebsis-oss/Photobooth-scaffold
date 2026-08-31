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
                   AccentColorHex, CanvasColorHex, InkColorHex, LogoImagePath, EventName, AdminPin,
                   CaptureMode, AlsoCreateGif, GifFrameCount, GifFrameDelayMs,
                   BoothIconsEnabled, ShowLiveView, MirrorLiveView, LiveViewRotation,
                   BeautyFilterEnabled, FiltersMode, WatermarkImagePath,
                   GreenScreenEnabled, GreenScreenBackgroundPath,
                   SurveyEnabled,
                   DisclaimerHeader, DisclaimerText,
                   PrintAutomatically, ShowPrintButton, PrintLimitPerEvent, PrintLimitPerSession, PrintSharpening,
                   EmailEnabled, SmsEnabled, QrEnabled
            FROM Location WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);

        int countdownSeconds;
        bool glamFilterEnabled;
        string adminPin;
        var printTemplate = default(PrintTemplate)!;
        var theme = default(BoothTheme)!;
        var capture = default(CaptureSettings)!;
        var screen = default(ScreenSettings)!;
        var effects = default(EffectsSettings)!;
        var greenScreen = default(GreenScreenSettings)!;
        var survey = default(SurveySettings)!;
        var disclaimer = default(DisclaimerSettings)!;
        var printOptions = default(PrintOptions)!;
        var sharing = default(SharingSettings)!;
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
            adminPin = reader.GetString(11);
            capture = new CaptureSettings(reader.GetString(12), reader.GetBoolean(13), reader.GetInt32(14), reader.GetInt32(15));
            screen = new ScreenSettings(reader.GetBoolean(16), reader.GetBoolean(17), reader.GetBoolean(18), reader.GetInt32(19));
            effects = new EffectsSettings(reader.GetBoolean(20), reader.GetString(21), reader.IsDBNull(22) ? null : reader.GetString(22));
            greenScreen = new GreenScreenSettings(reader.GetBoolean(23), reader.IsDBNull(24) ? null : reader.GetString(24));
            survey = new SurveySettings(reader.GetBoolean(25));
            disclaimer = new DisclaimerSettings(reader.GetString(26), reader.GetString(27));
            printOptions = new PrintOptions(reader.GetBoolean(28), reader.GetBoolean(29), reader.GetInt32(30), reader.GetInt32(31), reader.GetString(32));
            sharing = new SharingSettings(reader.GetBoolean(33), reader.GetBoolean(34), reader.GetBoolean(35));
        }

        // A separate connection (not this method's `connection` above), so
        // this can only run once the reader above has finished either way --
        // fetched after, not interleaved with, the Location row read.
        printTemplate = printTemplate with
        {
            Elements = await new PrintTemplateElementRepository().GetAllByLocationAsync(_locationId, ct),
        };

        return new BoothSettings(countdownSeconds, glamFilterEnabled, printTemplate, adminPin)
        {
            Theme = theme,
            Capture = capture,
            Screen = screen,
            Effects = effects,
            GreenScreen = greenScreen,
            Survey = survey,
            Disclaimer = disclaimer,
            PrintOptions = printOptions,
            Sharing = sharing,
        };
    }
}
