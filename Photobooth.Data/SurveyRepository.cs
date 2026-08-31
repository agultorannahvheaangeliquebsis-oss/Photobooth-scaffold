using Microsoft.Data.SqlClient;
using Photobooth.Core;

namespace Photobooth.Data;

public record SurveyQuestionRecord(int SurveyQuestionId, string Text, int SortOrder, bool IsActive);

/// <summary>Admin-facing CRUD over SurveyQuestion/SurveyResponse -- same plain-repository
/// shape as FrameRepository (no interface/mock, since only AdminWindow and
/// SqlSurveyService ever talk to this directly, not BoothStateMachine).</summary>
public class SurveyRepository
{
    public async Task<int> InsertQuestionAsync(int locationId, string text, int sortOrder, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            "INSERT INTO SurveyQuestion (LocationId, Text, SortOrder) OUTPUT INSERTED.SurveyQuestionId VALUES (@LocationId, @Text, @SortOrder);",
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);
        command.Parameters.AddWithValue("@Text", text);
        command.Parameters.AddWithValue("@SortOrder", sortOrder);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public Task<List<SurveyQuestionRecord>> GetAllByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: false, ct);

    public Task<List<SurveyQuestionRecord>> GetActiveByLocationAsync(int locationId, CancellationToken ct = default) =>
        GetByLocationAsync(locationId, activeOnly: true, ct);

    private async Task<List<SurveyQuestionRecord>> GetByLocationAsync(int locationId, bool activeOnly, CancellationToken ct)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            $"""
            SELECT SurveyQuestionId, Text, SortOrder, IsActive FROM SurveyQuestion
            WHERE LocationId = @LocationId {(activeOnly ? "AND IsActive = 1" : "")}
            ORDER BY SortOrder, SurveyQuestionId;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<SurveyQuestionRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SurveyQuestionRecord(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3)));
        }
        return results;
    }

    public async Task DeleteQuestionAsync(int surveyQuestionId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand("DELETE FROM SurveyQuestion WHERE SurveyQuestionId = @Id;", connection);
        command.Parameters.AddWithValue("@Id", surveyQuestionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertResponsesAsync(int sessionId, IReadOnlyList<SurveyAnswer> answers, CancellationToken ct = default)
    {
        if (answers.Count == 0)
        {
            return;
        }

        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        foreach (SurveyAnswer answer in answers)
        {
            using var command = new SqlCommand(
                "INSERT INTO SurveyResponse (SessionId, SurveyQuestionId, Answer) VALUES (@SessionId, @QuestionId, @Answer);",
                connection);
            command.Parameters.AddWithValue("@SessionId", sessionId);
            command.Parameters.AddWithValue("@QuestionId", answer.SurveyQuestionId);
            command.Parameters.AddWithValue("@Answer", answer.Answer);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Every recorded response for this location, joined to its question text --
    /// for AdminWindow's View Responses list.</summary>
    public async Task<List<(int SessionId, string QuestionText, string Answer, DateTime RecordedAt)>> GetResponsesByLocationAsync(int locationId, CancellationToken ct = default)
    {
        using var connection = await SqlConnectionFactory.OpenAsync(ct);
        using var command = new SqlCommand(
            """
            SELECT r.SessionId, q.Text, r.Answer, r.RecordedAt
            FROM SurveyResponse r
            JOIN SurveyQuestion q ON q.SurveyQuestionId = r.SurveyQuestionId
            WHERE q.LocationId = @LocationId
            ORDER BY r.RecordedAt DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@LocationId", locationId);

        using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<(int, string, string, DateTime)>();
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
        }
        return results;
    }
}
