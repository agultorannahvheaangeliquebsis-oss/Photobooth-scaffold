using Photobooth.Core;
using System.Linq;

namespace Photobooth.Data;

/// <summary>
/// Real ISurveyService: reads/writes SurveyQuestion/SurveyResponse via SQL,
/// and bridges CollectAnswersAsync's wait for guest answers to WPF taps via
/// a TaskCompletionSource -- same handoff UiFeedbackService/UiFrameSelectionService
/// already established, just living here (not Photobooth.Core) since this
/// implementation also needs direct SQL access unlike those two.
/// </summary>
public class SqlSurveyService : ISurveyService
{
    private readonly int _locationId;
    private readonly SurveyRepository _repo = new();
    private TaskCompletionSource<IReadOnlyList<SurveyAnswer>>? _pending;

    /// <summary>Raised when BoothStateMachine enters Survey and starts waiting for
    /// answers -- MainWindow shows SurveyView in response.</summary>
    public event Action<IReadOnlyList<SurveyQuestion>>? AnswersRequested;

    public SqlSurveyService(int locationId)
    {
        _locationId = locationId;
    }

    public async Task<IReadOnlyList<SurveyQuestion>> GetActiveQuestionsAsync(CancellationToken ct = default)
    {
        List<SurveyQuestionRecord> records = await _repo.GetActiveByLocationAsync(_locationId, ct);
        return records.Select(r => new SurveyQuestion(r.SurveyQuestionId, r.Text)).ToList();
    }

    public Task<IReadOnlyList<SurveyAnswer>> CollectAnswersAsync(IReadOnlyList<SurveyQuestion> questions, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<SurveyAnswer>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        AnswersRequested?.Invoke(questions);
        return tcs.Task;
    }

    /// <summary>Called by MainWindow when the guest submits (or skips) SurveyView.</summary>
    public void SubmitAnswers(IReadOnlyList<SurveyAnswer> answers)
    {
        _pending?.TrySetResult(answers);
        _pending = null;
    }

    public Task RecordResponsesAsync(int sessionId, IReadOnlyList<SurveyAnswer> answers, CancellationToken ct = default) =>
        _repo.InsertResponsesAsync(sessionId, answers, ct);
}
