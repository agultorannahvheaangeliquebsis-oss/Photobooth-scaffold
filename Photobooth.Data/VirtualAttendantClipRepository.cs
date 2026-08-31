using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

public record VirtualAttendantClipRecord(int ClipId, string Stage, string FilePath, int SortOrder);

/// <summary>Admin-facing CRUD over the VirtualAttendantClip table -- same plain-repository
/// shape as FrameRepository (no interface/mock, since only AdminWindow and
/// SqlVirtualAttendantService ever talk to this directly, not BoothStateMachine).</summary>
public class VirtualAttendantClipRepository
{
    public async Task<int> InsertAsync(int locationId, BoothState stage, string filePath, int sortOrder, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO VirtualAttendantClip (LocationId, Stage, FilePath, SortOrder) OUTPUT INSERTED.ClipId VALUES (@LocationId, @Stage, @FilePath, @SortOrder);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Stage", stage.ToString());
        command.Parameters.AddWithValue("@FilePath", filePath);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<VirtualAttendantClipRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "SELECT ClipId, Stage, FilePath, SortOrder FROM VirtualAttendantClip WHERE LocationId = @LocationId ORDER BY Stage, SortOrder, ClipId;",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<VirtualAttendantClipRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new VirtualAttendantClipRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }
        return results;
    }

    public async Task DeleteAsync(int clipId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM VirtualAttendantClip WHERE ClipId = @ClipId;", connection);
        command.Parameters.AddWithValue("@ClipId", clipId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
