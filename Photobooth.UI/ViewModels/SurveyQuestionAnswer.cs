namespace Photobooth.UI.ViewModels;

/// <summary>One row of KioskWindow's Survey screen -- pairs an admin-authored
/// question with the guest's in-progress bindable answer. SubmitSurveyCommand
/// reads these back into SurveyAnswer records for ISurveyService.SubmitAnswers.</summary>
public class SurveyQuestionAnswer : ObservableObject
{
    public SurveyQuestionAnswer(int surveyQuestionId, string questionText)
    {
        SurveyQuestionId = surveyQuestionId;
        QuestionText = questionText;
    }

    public int SurveyQuestionId { get; }

    public string QuestionText { get; }

    private string _answer = string.Empty;
    public string Answer
    {
        get => _answer;
        set => SetProperty(ref _answer, value);
    }
}
