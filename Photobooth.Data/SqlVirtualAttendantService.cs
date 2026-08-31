using Microsoft.Data.SqlClient;
using Photobooth.Core;
using System.Linq;

namespace Photobooth.Data;

/// <summary>
/// Real IVirtualAttendantService: reads VirtualAttendantSettings and the clip
/// pool for this location fresh on every call (no caching), same reasoning
/// as SqlFrameLibraryService -- a clip an admin just added/removed takes
/// effect for the very next SetState call.
/// </summary>
public class SqlVirtualAttendantService : IVirtualAttendantService
{
    private readonly int _locationId;
    private readonly VirtualAttendantClipRepository _clips = new();
    private readonly Random _random = new();

    public SqlVirtualAttendantService(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<AttendantClip?> GetCueAsync(BoothState state, CancellationToken ct = default)
    {
        VirtualAttendantSettings settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
        {
            return null;
        }

        List<VirtualAttendantClipRecord> clips = (await _clips.GetAllByLocationAsync(_locationId, ct))
            .Where(c => c.Stage == state.ToString())
            .ToList();
        if (clips.Count == 0)
        {
            return null;
        }

        VirtualAttendantClipRecord chosen = ShouldRandomize(state, settings) ? clips[_random.Next(clips.Count)] : clips[0];
        return new AttendantClip(chosen.FilePath, state);
    }

    private static bool ShouldRandomize(BoothState state, VirtualAttendantSettings settings) => state switch
    {
        BoothState.Consent => settings.RandomizeConsent,
        BoothState.Countdown => settings.RandomizeCountdown,
        BoothState.Capturing => settings.RandomizeCapturing,
        BoothState.Reviewing => settings.RandomizeReviewing,
        BoothState.Printing => settings.RandomizePrinting,
        BoothState.Complete => settings.RandomizeComplete,
        _ => false,
    };

    private async Task<VirtualAttendantSettings> GetSettingsAsync(CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT AttendantEnabled, AttendantStyle, AttendantRandomizeConsent, AttendantRandomizeCountdown,
                   AttendantRandomizeCapturing, AttendantRandomizeReviewing, AttendantRandomizePrinting, AttendantRandomizeComplete
            FROM Location WHERE LocationId = @LocationId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", _locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return VirtualAttendantSettings.Default;
        }

        return new VirtualAttendantSettings(
            reader.GetBoolean(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7));
    }
}
