using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record FrameRecord(int FrameId, string Name, string ImagePath, int SortOrder, bool IsActive);

/// <summary>Admin-facing CRUD over the Frame table -- same plain-repository
/// shape as LocationRepository/InventoryLogRepository (no interface/mock,
/// since only AdminWindow and SqlFrameLibraryService ever talk to this
/// directly, not BoothStateMachine).</summary>
public class FrameRepository
{
    public async Task<int> InsertAsync(int locationId, string name, string imagePath, int sortOrder, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Frame (LocationId, Name, ImagePath, SortOrder) OUTPUT INSERTED.FrameId VALUES (@LocationId, @Name, @ImagePath, @SortOrder);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@ImagePath", imagePath);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Every frame for this location, active or not -- for the admin list.</summary>
    public Task<List<FrameRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: false, ct);

    /// <summary>Only active frames -- what the guest-facing picker offers.</summary>
    public Task<List<FrameRecord>> GetActiveByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: true, ct);

    private async Task<List<FrameRecord>> GetByLocationAsync(int locationId, bool activeOnly, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            $"""
            SELECT FrameId, Name, ImagePath, SortOrder, IsActive FROM Frame
            WHERE LocationId = @LocationId {(activeOnly ? "AND IsActive = 1" : "")}
            ORDER BY SortOrder, FrameId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<FrameRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FrameRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }
        return results;
    }

    public async Task SetActiveAsync(int frameId, bool isActive, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "UPDATE Frame SET IsActive = @IsActive WHERE FrameId = @FrameId;",
            connection);
        command.Parameters.AddWithValue("@IsActive", isActive);
        command.Parameters.AddWithValue("@FrameId", frameId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int frameId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM Frame WHERE FrameId = @FrameId;", connection);
        command.Parameters.AddWithValue("@FrameId", frameId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
