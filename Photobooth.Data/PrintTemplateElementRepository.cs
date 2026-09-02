using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

/// <summary>Admin-facing CRUD over the PrintTemplateElement table -- same plain-repository
/// shape as FrameRepository (no interface/mock, since only SqlBoothSettingsProvider and
/// PrintTemplateEditorWindow ever talk to this directly, not BoothStateMachine).
///
/// Every row belongs to either a location's one "live" print setup (PrintTemplateId IS
/// NULL, keyed by LocationId alone -- everything this type did before the PrintTemplate
/// library table existed) or one saved PrintTemplate library entry (PrintTemplateId set --
/// see PrintTemplateRepository). GetAllByLocationAsync/ReplaceAllAsync only ever touch the
/// live rows, so SqlBoothSettingsProvider's read path is untouched by the library feature;
/// GetAllByTemplateAsync/ReplaceAllForTemplateAsync are the library-side equivalents.</summary>
public class PrintTemplateElementRepository
{
    public Task<List<PrintTemplateElement>> GetAllByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetAllAsync("LocationId = @LocationId AND PrintTemplateId IS NULL", ("@LocationId", locationId), ct);

    public Task<List<PrintTemplateElement>> GetAllByTemplateAsync(int printTemplateId, CancellationToken ct = default) =>
        GetAllAsync("PrintTemplateId = @PrintTemplateId", ("@PrintTemplateId", printTemplateId), ct);

    private async Task<List<PrintTemplateElement>> GetAllAsync(string whereClause, (string Name, object Value) parameter, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            $"""
            SELECT Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath,
                   FontFamily, FontSizePercent, Bold, ColorHex, ShapeType, PhotoIndex
            FROM PrintTemplateElement
            WHERE {whereClause}
            ORDER BY SortOrder, ElementId;
            """,
            connection);
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<PrintTemplateElement>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new PrintTemplateElement(
                Enum.Parse<PrintTemplateElementKind>(reader.GetString(0)),
                (double)reader.GetDecimal(1),
                (double)reader.GetDecimal(2),
                (double)reader.GetDecimal(3),
                (double)reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? "Segoe UI" : reader.GetString(7),
                reader.IsDBNull(8) ? 0.05 : (double)reader.GetDecimal(8),
                !reader.IsDBNull(9) && reader.GetBoolean(9),
                reader.IsDBNull(10) ? "#202124" : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12)));
        }
        return results;
    }

    /// <summary>Replaces every live element for this location (PrintTemplateId IS NULL)
    /// in one delete-then-reinsert -- PrintTemplateEditorWindow always saves its whole
    /// working list at once, so there's no need for per-row update/delete tracking. Never
    /// touches this location's saved library-template rows (PrintTemplateId IS NOT NULL).</summary>
    public Task ReplaceAllAsync(int locationId, IReadOnlyList<PrintTemplateElement> elements, CancellationToken ct = default) =>
        ReplaceAllAsync("LocationId = @LocationId AND PrintTemplateId IS NULL", locationId, printTemplateId: null, elements, ct);

    /// <summary>Same replace-in-place shape as ReplaceAllAsync, for one saved library
    /// entry's own elements instead of a location's live ones.</summary>
    public Task ReplaceAllForTemplateAsync(int printTemplateId, int locationId, IReadOnlyList<PrintTemplateElement> elements, CancellationToken ct = default) =>
        ReplaceAllAsync("PrintTemplateId = @PrintTemplateId", locationId, printTemplateId, elements, ct);

    private async Task ReplaceAllAsync(string deleteWhereClause, int locationId, int? printTemplateId, IReadOnlyList<PrintTemplateElement> elements, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = new SqlCommand($"DELETE FROM PrintTemplateElement WHERE {deleteWhereClause};", connection, transaction))
        {
            deleteCommand.Parameters.AddWithValue("@LocationId", locationId);
            if (printTemplateId is int templateId)
            {
                deleteCommand.Parameters.AddWithValue("@PrintTemplateId", templateId);
            }
            await deleteCommand.ExecuteNonQueryAsync(ct);
        }

        for (int i = 0; i < elements.Count; i++)
        {
            PrintTemplateElement element = elements[i];
            using var insertCommand = new SqlCommand(
                """
                INSERT INTO PrintTemplateElement
                    (LocationId, PrintTemplateId, Kind, XPercent, YPercent, WidthPercent, HeightPercent, Text, ImagePath, FontFamily, FontSizePercent, Bold, ColorHex, ShapeType, PhotoIndex, SortOrder)
                VALUES
                    (@LocationId, @PrintTemplateId, @Kind, @XPercent, @YPercent, @WidthPercent, @HeightPercent, @Text, @ImagePath, @FontFamily, @FontSizePercent, @Bold, @ColorHex, @ShapeType, @PhotoIndex, @SortOrder);
                """,
                connection, transaction);
            insertCommand.Parameters.AddWithValue("@LocationId", locationId);
            insertCommand.Parameters.AddWithValue("@PrintTemplateId", (object?)printTemplateId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@Kind", element.Kind.ToString());
            insertCommand.Parameters.AddWithValue("@XPercent", element.XPercent);
            insertCommand.Parameters.AddWithValue("@YPercent", element.YPercent);
            insertCommand.Parameters.AddWithValue("@WidthPercent", element.WidthPercent);
            insertCommand.Parameters.AddWithValue("@HeightPercent", element.HeightPercent);
            insertCommand.Parameters.AddWithValue("@Text", (object?)element.Text ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@ImagePath", (object?)element.ImagePath ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@FontFamily", element.FontFamily);
            insertCommand.Parameters.AddWithValue("@FontSizePercent", element.FontSizePercent);
            insertCommand.Parameters.AddWithValue("@Bold", element.Bold);
            insertCommand.Parameters.AddWithValue("@ColorHex", element.ColorHex);
            insertCommand.Parameters.AddWithValue("@ShapeType", (object?)element.ShapeType ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@PhotoIndex", (object?)element.PhotoIndex ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@SortOrder", i);
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
