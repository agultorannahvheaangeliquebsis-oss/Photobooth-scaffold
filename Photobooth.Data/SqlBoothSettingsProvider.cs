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
                   CaptureMode, AlsoCreateGif, GifFrameCount, GifFrameDelayMs, VideoDurationSeconds,
                   BoothIconsEnabled, ShowLiveView, MirrorLiveView, LiveViewRotation,
                   EnableWebcams, WebcamResolutionQuality, AudioInputDeviceName,
                   BeautyFilterEnabled, FiltersMode, WatermarkImagePath,
                   BeautyFilterAlsoDuringCountdown, FiltersEnabled, EnabledFilterPresetIds,
                   PostProcessingEnabled, PostProcessingApplicationPath,
                   StickersEnabled, WatermarkEnabled,
                   GreenScreenEnabled, GreenScreenBackgroundPath,
                   SurveyEnabled,
                   DisclaimerHeader, DisclaimerText,
                   PrintAutomatically, ShowPrintButton, PrintLimitPerEvent, PrintLimitPerSession, PrintSharpening,
                   EmailEnabled, SmsEnabled, QrEnabled,
                   AttendantEnabled, AttendantStyle, AttendantRandomizeConsent, AttendantRandomizeCountdown,
                   AttendantRandomizeCapturing, AttendantRandomizeReviewing, AttendantRandomizePrinting, AttendantRandomizeComplete
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
        var virtualAttendant = default(VirtualAttendantSettings)!;
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
            capture = new CaptureSettings(reader.GetString(12), reader.GetBoolean(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16));
            screen = new ScreenSettings(
                reader.GetBoolean(17), reader.GetBoolean(18), reader.GetBoolean(19), reader.GetInt32(20),
                reader.GetBoolean(21), reader.GetInt32(22), reader.IsDBNull(23) ? null : reader.GetString(23));
            effects = new EffectsSettings(
                reader.GetBoolean(24), reader.GetString(25), reader.IsDBNull(26) ? null : reader.GetString(26),
                reader.GetBoolean(27), reader.GetBoolean(28), reader.GetBoolean(30),
                reader.IsDBNull(31) ? null : reader.GetString(31), reader.GetBoolean(32), reader.GetBoolean(33))
            {
                EnabledFilterPresetIds = reader.GetString(29),
            };
            greenScreen = new GreenScreenSettings(reader.GetBoolean(34), reader.IsDBNull(35) ? null : reader.GetString(35));
            survey = new SurveySettings(reader.GetBoolean(36));
            disclaimer = new DisclaimerSettings(reader.GetString(37), reader.GetString(38));
            printOptions = new PrintOptions(reader.GetBoolean(39), reader.GetBoolean(40), reader.GetInt32(41), reader.GetInt32(42), reader.GetString(43));
            sharing = new SharingSettings(reader.GetBoolean(44), reader.GetBoolean(45), reader.GetBoolean(46));
            virtualAttendant = new VirtualAttendantSettings(
                reader.GetBoolean(47), reader.GetString(48), reader.GetBoolean(49), reader.GetBoolean(50),
                reader.GetBoolean(51), reader.GetBoolean(52), reader.GetBoolean(53), reader.GetBoolean(54));
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
            VirtualAttendant = virtualAttendant,
        };
    }
}
