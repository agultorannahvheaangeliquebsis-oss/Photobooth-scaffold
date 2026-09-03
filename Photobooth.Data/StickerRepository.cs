using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record StickerRecord(int StickerId, string Name, string ImagePath, int SortOrder, bool IsActive);

/// <summary>Admin-facing CRUD over the Sticker table -- same plain-repository
/// shape as FrameRepository/CustomFilterRepository (no interface/mock, since
/// only AdminWindow/StickerLibraryWindow ever talk to this directly). Unlike
/// Frame/CustomFilter, nothing at guest-session time reads this yet -- see
/// Sticker's own doc comment in schema.sql -- so IsActive is carried for
/// schema consistency but every insert is active and DeleteAsync is a hard
/// delete, same as CustomFilterRepository's "x" button.</summary>
public class StickerRepository
{
    public async Task<int> InsertAsync(int locationId, string name, string imagePath, int sortOrder, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO Sticker (LocationId, Name, ImagePath, SortOrder) OUTPUT INSERTED.StickerId VALUES (@LocationId, @Name, @ImagePath, @SortOrder);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@ImagePath", imagePath);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Every sticker for this location, active or not -- for the admin library.</summary>
    public Task<List<StickerRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: false, ct);

    /// <summary>Only active stickers -- what a future guest-facing picker would offer.</summary>
    public Task<List<StickerRecord>> GetActiveByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: true, ct);

    private async Task<List<StickerRecord>> GetByLocationAsync(int locationId, bool activeOnly, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            $"""
            SELECT StickerId, Name, ImagePath, SortOrder, IsActive FROM Sticker
            WHERE LocationId = @LocationId {(activeOnly ? "AND IsActive = 1" : "")}
            ORDER BY SortOrder, StickerId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<StickerRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new StickerRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }
        return results;
    }

    public async Task DeleteAsync(int stickerId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM Sticker WHERE StickerId = @StickerId;", connection);
        command.Parameters.AddWithValue("@StickerId", stickerId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
