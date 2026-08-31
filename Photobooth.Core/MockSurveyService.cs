using System.Linq;

namespace Photobooth.Core;

/// <summary>
/// Fake survey service for development and tests. Defaults to no active
/// questions configured (matching a fresh SurveyQuestion table), so
/// BoothStateMachine's Survey state is skipped entirely unless a test
/// explicitly populates Questions -- same "off until configured" default as
/// MockFrameLibraryService.
/// </summary>
public class MockSurveyService : ISurveyService
{
    public List<SurveyQuestion> Questions { get; set; } = new();

    /// <summary>Every RecordResponsesAsync call, in order -- for tests to assert against.</summary>
    public List<(int SessionId, IReadOnlyList<SurveyAnswer> Answers)> RecordedResponses { get; } = new();

    /// <summary>Simulated guest answers, keyed by question id -- returned by
    /// CollectAnswersAsync for whichever questions are asked. A question with no
    /// entry here is simulated as skipped (no SurveyAnswer produced for it).</summary>
    public Dictionary<int, string> SimulatedAnswers { get; set; } = new();

    /// <summary>When true, the next CollectAnswersAsync call reports no answers at all,
    /// same "walked away" simulation as MockFeedbackService.SkipNext. Resets itself
    /// after firing once.</summary>
    public bool SkipNext { get; set; } = false;

    public Task<IReadOnlyList<SurveyQuestion>> GetActiveQuestionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SurveyQuestion>>(Questions);

    public Task<IReadOnlyList<SurveyAnswer>> CollectAnswersAsync(IReadOnlyList<SurveyQuestion> questions, CancellationToken ct = default)
    {
        if (SkipNext)
        {
            SkipNext = false;
            return Task.FromResult<IReadOnlyList<SurveyAnswer>>(Array.Empty<SurveyAnswer>());
        }

        var answers = questions
            .Where(q => SimulatedAnswers.ContainsKey(q.SurveyQuestionId))
            .Select(q => new SurveyAnswer(q.SurveyQuestionId, SimulatedAnswers[q.SurveyQuestionId]))
            .ToList();
        return Task.FromResult<IReadOnlyList<SurveyAnswer>>(answers);
    }

    public Task RecordResponsesAsync(int sessionId, IReadOnlyList<SurveyAnswer> answers, CancellationToken ct = default)
    {
        RecordedResponses.Add((sessionId, answers));
        return Task.CompletedTask;
    }
}
