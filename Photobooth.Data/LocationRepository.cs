using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

public record LocationRecord(
    int LocationId, string Name, string Type, string? Address, int CountdownSeconds, bool GlamFilterEnabled,
    PrintTemplate PrintTemplate, BoothTheme Theme, string AdminPin,
    CaptureSettings Capture, ScreenSettings Screen, EffectsSettings Effects, GreenScreenSettings GreenScreen,
    SurveySettings Survey, DisclaimerSettings Disclaimer, PrintOptions PrintOptions, SharingSettings Sharing);

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
                   AccentColorHex, CanvasColorHex, InkColorHex, LogoImagePath, EventName, AdminPin,
                   CaptureMode, AlsoCreateGif, GifFrameCount, GifFrameDelayMs, VideoDurationSeconds,
                   BoothIconsEnabled, ShowLiveView, MirrorLiveView, LiveViewRotation,
                   BeautyFilterEnabled, FiltersMode, WatermarkImagePath,
                   GreenScreenEnabled, GreenScreenBackgroundPath,
                   SurveyEnabled,
                   DisclaimerHeader, DisclaimerText,
                   PrintAutomatically, ShowPrintButton, PrintLimitPerEvent, PrintLimitPerSession, PrintSharpening,
                   EmailEnabled, SmsEnabled, QrEnabled
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
                    reader.GetString(14)),
                reader.GetString(15),
                new CaptureSettings(reader.GetString(16), reader.GetBoolean(17), reader.GetInt32(18), reader.GetInt32(19), reader.GetInt32(20)),
                new ScreenSettings(reader.GetBoolean(21), reader.GetBoolean(22), reader.GetBoolean(23), reader.GetInt32(24)),
                new EffectsSettings(reader.GetBoolean(25), reader.GetString(26), reader.IsDBNull(27) ? null : reader.GetString(27)),
                new GreenScreenSettings(reader.GetBoolean(28), reader.IsDBNull(29) ? null : reader.GetString(29)),
                new SurveySettings(reader.GetBoolean(30)),
                new DisclaimerSettings(reader.GetString(31), reader.GetString(32)),
                new PrintOptions(reader.GetBoolean(33), reader.GetBoolean(34), reader.GetInt32(35), reader.GetInt32(36), reader.GetString(37)),
                new SharingSettings(reader.GetBoolean(38), reader.GetBoolean(39), reader.GetBoolean(40))));
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
    /// duration, whether Glam Booth mode is on, the print template, and the PIN that
    /// gates MainWindow's Setup/launch screen. Read fresh by SqlBoothSettingsProvider
    /// at the start of every session, so a change here takes effect for the very next
    /// guest without needing to restart the app.</summary>
    public async Task UpdateSettingsAsync(int locationId, int countdownSeconds, bool glamFilterEnabled, PrintTemplate printTemplate, string adminPin, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET CountdownSeconds = @CountdownSeconds, GlamFilterEnabled = @GlamFilterEnabled,
                                 PrintLayout = @PrintLayout, PrintWidthInches = @PrintWidthInches,
                                 PrintHeightInches = @PrintHeightInches, PrintStripCopies = @PrintStripCopies,
                                 AdminPin = @AdminPin
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@CountdownSeconds", countdownSeconds);
        command.Parameters.AddWithValue("@GlamFilterEnabled", glamFilterEnabled);
        command.Parameters.AddWithValue("@PrintLayout", printTemplate.Layout);
        command.Parameters.AddWithValue("@PrintWidthInches", printTemplate.WidthInches);
        command.Parameters.AddWithValue("@PrintHeightInches", printTemplate.HeightInches);
        command.Parameters.AddWithValue("@PrintStripCopies", printTemplate.StripCopies);
        command.Parameters.AddWithValue("@AdminPin", adminPin);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Updates the dslrBooth feature-parity settings added in Phase 1 --
    /// Capture, Screen, Effects, Green Screen, Survey, Disclaimer, Print Options,
    /// and Sharing. Kept separate from UpdateSettingsAsync so saving one of these
    /// sections doesn't force the countdown/print-template fields to also
    /// validate, same reasoning as UpdateThemeAsync above.</summary>
    public async Task UpdateDslrBoothParitySettingsAsync(
        int locationId,
        CaptureSettings capture,
        ScreenSettings screen,
        EffectsSettings effects,
        GreenScreenSettings greenScreen,
        SurveySettings survey,
        DisclaimerSettings disclaimer,
        PrintOptions printOptions,
        SharingSettings sharing,
        CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            UPDATE Location SET
                CaptureMode = @CaptureMode, AlsoCreateGif = @AlsoCreateGif, GifFrameCount = @GifFrameCount,
                GifFrameDelayMs = @GifFrameDelayMs, VideoDurationSeconds = @VideoDurationSeconds,
                BoothIconsEnabled = @BoothIconsEnabled, ShowLiveView = @ShowLiveView,
                MirrorLiveView = @MirrorLiveView, LiveViewRotation = @LiveViewRotation,
                BeautyFilterEnabled = @BeautyFilterEnabled, FiltersMode = @FiltersMode, WatermarkImagePath = @WatermarkImagePath,
                GreenScreenEnabled = @GreenScreenEnabled, GreenScreenBackgroundPath = @GreenScreenBackgroundPath,
                SurveyEnabled = @SurveyEnabled,
                DisclaimerHeader = @DisclaimerHeader, DisclaimerText = @DisclaimerText,
                PrintAutomatically = @PrintAutomatically, ShowPrintButton = @ShowPrintButton,
                PrintLimitPerEvent = @PrintLimitPerEvent, PrintLimitPerSession = @PrintLimitPerSession, PrintSharpening = @PrintSharpening,
                EmailEnabled = @EmailEnabled, SmsEnabled = @SmsEnabled, QrEnabled = @QrEnabled
            WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@CaptureMode", capture.Mode);
        command.Parameters.AddWithValue("@AlsoCreateGif", capture.AlsoCreateGif);
        command.Parameters.AddWithValue("@GifFrameCount", capture.FrameCount);
        command.Parameters.AddWithValue("@GifFrameDelayMs", capture.FrameDelayMs);
        command.Parameters.AddWithValue("@VideoDurationSeconds", capture.VideoDurationSeconds);
        command.Parameters.AddWithValue("@BoothIconsEnabled", screen.BoothIconsEnabled);
        command.Parameters.AddWithValue("@ShowLiveView", screen.ShowLiveView);
        command.Parameters.AddWithValue("@MirrorLiveView", screen.MirrorLiveView);
        command.Parameters.AddWithValue("@LiveViewRotation", screen.LiveViewRotation);
        command.Parameters.AddWithValue("@BeautyFilterEnabled", effects.BeautyFilterEnabled);
        command.Parameters.AddWithValue("@FiltersMode", effects.FiltersMode);
        command.Parameters.AddWithValue("@WatermarkImagePath", (object?)effects.WatermarkImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@GreenScreenEnabled", greenScreen.Enabled);
        command.Parameters.AddWithValue("@GreenScreenBackgroundPath", (object?)greenScreen.BackgroundImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@SurveyEnabled", survey.Enabled);
        command.Parameters.AddWithValue("@DisclaimerHeader", disclaimer.Header);
        command.Parameters.AddWithValue("@DisclaimerText", disclaimer.Text);
        command.Parameters.AddWithValue("@PrintAutomatically", printOptions.PrintAutomatically);
        command.Parameters.AddWithValue("@ShowPrintButton", printOptions.ShowPrintButton);
        command.Parameters.AddWithValue("@PrintLimitPerEvent", printOptions.PrintLimitPerEvent);
        command.Parameters.AddWithValue("@PrintLimitPerSession", printOptions.PrintLimitPerSession);
        command.Parameters.AddWithValue("@PrintSharpening", printOptions.PrintSharpening);
        command.Parameters.AddWithValue("@EmailEnabled", sharing.EmailEnabled);
        command.Parameters.AddWithValue("@SmsEnabled", sharing.SmsEnabled);
        command.Parameters.AddWithValue("@QrEnabled", sharing.QrEnabled);
        command.Parameters.AddWithValue("@LocationId", locationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
