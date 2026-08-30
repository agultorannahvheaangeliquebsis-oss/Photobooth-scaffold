namespace Photobooth.Core;

/// <summary>Outcome of a guest's post-session feedback prompt. Rating is 1-5, or null
/// if the guest skipped rating; Comment is optional free text, independent of whether
/// a rating was given.</summary>
public record FeedbackResult(int? Rating, string? Comment)
{
    /// <summary>True when the guest gave neither a rating nor a comment -- nothing worth
    /// a Feedback row for. See BoothStateMachine's Feedback state.</summary>
    public bool IsEmpty => Rating is null && Comment is null;
}

/// <summary>
/// Abstracts collecting a guest's rating/comment right after their session
/// completes. Same interface-plus-mock seam as everything else. Unlike
/// IConsentService/IPaymentService (both still mock-only, since a real
/// disclaimer/gateway needs external integration work), a star rating and a
/// comment box is just button taps and text input with no hardware or
/// network dependency -- same reasoning that made UiFrameSelectionService a
/// real implementation instead of a mock, so UiFeedbackService is too.
/// </summary>
public interface IFeedbackService
{
    Task<FeedbackResult> CollectAsync(CancellationToken ct = default);
}
