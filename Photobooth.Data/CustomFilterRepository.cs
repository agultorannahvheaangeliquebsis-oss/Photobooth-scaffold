using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record CustomFilterRecord(int CustomFilterId, string Name, string CubeFilePath, int SortOrder, bool IsActive);

/// <summary>Admin-facing CRUD over the CustomFilter table -- same plain-repository
/// shape as FrameRepository (no interface/mock, since only AdminWindow/
/// FilterLibraryWindow and SqlCustomFilterLibraryService ever talk to this
/// directly, not BoothStateMachine).</summary>
public class CustomFilterRepository
{
    public async Task<int> InsertAsync(int locationId, string name, string cubeFilePath, int sortOrder, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO CustomFilter (LocationId, Name, CubeFilePath, SortOrder) OUTPUT INSERTED.CustomFilterId VALUES (@LocationId, @Name, @CubeFilePath, @SortOrder);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@CubeFilePath", cubeFilePath);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Every custom filter for this location, active or not -- for the admin list.</summary>
    public Task<List<CustomFilterRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: false, ct);

    /// <summary>Only active custom filters -- what the guest-facing FilterPicker offers.</summary>
    public Task<List<CustomFilterRecord>> GetActiveByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: true, ct);

    private async Task<List<CustomFilterRecord>> GetByLocationAsync(int locationId, bool activeOnly, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            $"""
            SELECT CustomFilterId, Name, CubeFilePath, SortOrder, IsActive FROM CustomFilter
            WHERE LocationId = @LocationId {(activeOnly ? "AND IsActive = 1" : "")}
            ORDER BY SortOrder, CustomFilterId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<CustomFilterRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new CustomFilterRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4)));
        }
        return results;
    }

    public async Task SetActiveAsync(int customFilterId, bool isActive, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "UPDATE CustomFilter SET IsActive = @IsActive WHERE CustomFilterId = @CustomFilterId;",
            connection);
        command.Parameters.AddWithValue("@IsActive", isActive);
        command.Parameters.AddWithValue("@CustomFilterId", customFilterId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(int customFilterId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM CustomFilter WHERE CustomFilterId = @CustomFilterId;", connection);
        command.Parameters.AddWithValue("@CustomFilterId", customFilterId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
