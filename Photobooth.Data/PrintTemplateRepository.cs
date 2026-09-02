using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

public record PrintTemplateRecord(
    int PrintTemplateId, string Name, string Layout, double WidthInches, double HeightInches, int StripCopies, bool IsFavorite, int SortOrder);

/// <summary>Admin-facing CRUD over the PrintTemplate library table (see
/// Script0021_PrintTemplateLibrary.sql) -- same plain-repository shape as
/// FrameRepository (no interface/mock, since only AdminWindow and
/// PrintTemplateEditorWindow ever talk to this directly, not BoothStateMachine).
/// A row here is a saved template an admin can switch to, favorite, rename or
/// delete; it's distinct from a location's one "live" print setup (the Location
/// row's own PrintLayout/PrintWidthInches/... columns), which this type never
/// touches -- switching the switcher's selection is a separate explicit step
/// (see PrintTemplateEditorWindow's ActivateTemplate) that copies a row's
/// Layout/dimensions/Elements onto the live setup.</summary>
public class PrintTemplateRepository
{
    public async Task<int> InsertAsync(
        int locationId, string name, string layout, double widthInches, double heightInches, int stripCopies, bool isFavorite, int sortOrder,
        CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            INSERT INTO PrintTemplate (LocationId, Name, Layout, WidthInches, HeightInches, StripCopies, IsFavorite, SortOrder)
            OUTPUT INSERTED.PrintTemplateId
            VALUES (@LocationId, @Name, @Layout, @WidthInches, @HeightInches, @StripCopies, @IsFavorite, @SortOrder);
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Layout", layout);
        command.Parameters.AddWithValue("@WidthInches", widthInches);
        command.Parameters.AddWithValue("@HeightInches", heightInches);
        command.Parameters.AddWithValue("@StripCopies", stripCopies);
        command.Parameters.AddWithValue("@IsFavorite", isFavorite);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);

        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<PrintTemplateRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT PrintTemplateId, Name, Layout, WidthInches, HeightInches, StripCopies, IsFavorite, SortOrder
            FROM PrintTemplate
            WHERE LocationId = @LocationId
            ORDER BY SortOrder, PrintTemplateId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<PrintTemplateRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PrintTemplateRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                reader.GetInt32(5),
                reader.GetBoolean(6),
                reader.GetInt32(7)));
        }
        return results;
    }

    public async Task RenameAsync(int printTemplateId, string name, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("UPDATE PrintTemplate SET Name = @Name WHERE PrintTemplateId = @PrintTemplateId;", connection);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@PrintTemplateId", printTemplateId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetFavoriteAsync(int printTemplateId, bool isFavorite, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("UPDATE PrintTemplate SET IsFavorite = @IsFavorite WHERE PrintTemplateId = @PrintTemplateId;", connection);
        command.Parameters.AddWithValue("@IsFavorite", isFavorite);
        command.Parameters.AddWithValue("@PrintTemplateId", printTemplateId);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Deletes the template row and its own saved elements together (one
    /// transaction) -- PrintTemplateElement.PrintTemplateId has no ON DELETE CASCADE,
    /// so the element rows would otherwise dangle.</summary>
    public async Task DeleteAsync(int printTemplateId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        using (var deleteElementsCommand = new SqlCommand("DELETE FROM PrintTemplateElement WHERE PrintTemplateId = @PrintTemplateId;", connection, transaction))
        {
            deleteElementsCommand.Parameters.AddWithValue("@PrintTemplateId", printTemplateId);
            await deleteElementsCommand.ExecuteNonQueryAsync(ct);
        }

        using (var deleteTemplateCommand = new SqlCommand("DELETE FROM PrintTemplate WHERE PrintTemplateId = @PrintTemplateId;", connection, transaction))
        {
            deleteTemplateCommand.Parameters.AddWithValue("@PrintTemplateId", printTemplateId);
            await deleteTemplateCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
