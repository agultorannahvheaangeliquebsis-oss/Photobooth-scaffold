namespace Photobooth.Core;

/// <summary>Outcome of a guest's disclaimer/opt-in prompt before their session starts.</summary>
public record ConsentResult(bool DisclaimerAccepted, bool EmailOptIn, string? Email);

/// <summary>
/// Abstracts collecting the liability disclaimer acceptance and email
/// opt-in before a session runs. Same interface-plus-mock seam as
/// camera/printer/cloud upload/payment -- BoothStateMachine only ever talks
/// to this interface. No real interactive UI capture yet (no button-driven
/// accept/decline wired up in MainWindow) -- same "mock only, real gateway
/// is future work" status as IPaymentService today.
/// </summary>
public interface IConsentService
{
    Task<ConsentResult> CollectAsync(CancellationToken ct = default);
}
