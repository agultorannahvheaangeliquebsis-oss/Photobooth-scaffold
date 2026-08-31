namespace Photobooth.Core;

/// <summary>One admin-authored survey question, see AdminWindow's Survey section
/// question-builder.</summary>
public record SurveyQuestion(int SurveyQuestionId, string Text);

/// <summary>One guest's answer to a single question, collected during BoothState.Survey.</summary>
public record SurveyAnswer(int SurveyQuestionId, string Answer);

/// <summary>
/// Abstracts reading the booth's active survey questions and recording a
/// guest's responses. Same interface-plus-mock seam as IFeedbackService --
/// BoothStateMachine reads active questions fresh at the start of every
/// session (not cached), so a question an admin just added/removed takes
/// effect for the very next guest.
/// </summary>
public interface ISurveyService
{
    Task<IReadOnlyList<SurveyQuestion>> GetActiveQuestionsAsync(CancellationToken ct = default);

    /// <summary>Waits for the guest to answer (or skip) the given questions during
    /// BoothState.Survey -- same "state waits on a service call" shape
    /// IFeedbackService.CollectAsync/IFrameSelectionService.SelectFrameAsync already
    /// use. Not in the Phase 6 scope text's two-method list verbatim, but required for
    /// BoothStateMachine to have anything to pass to RecordResponsesAsync below.</summary>
    Task<IReadOnlyList<SurveyAnswer>> CollectAnswersAsync(IReadOnlyList<SurveyQuestion> questions, CancellationToken ct = default);

    Task RecordResponsesAsync(int sessionId, IReadOnlyList<SurveyAnswer> answers, CancellationToken ct = default);
}
