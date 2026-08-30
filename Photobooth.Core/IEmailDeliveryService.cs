namespace Photobooth.Core;

/// <summary>
/// Abstracts emailing a guest their photo link once they've opted in during
/// Consent. Same interface-plus-mock seam as everything else --
/// BoothStateMachine only ever talks to this interface. No real SMTP
/// delivery yet: that needs a real mail account/credentials, same "mock
/// only, real integration is future work" status IPaymentService and
/// IConsentService have today.
/// </summary>
public interface IEmailDeliveryService
{
    Task SendPhotoLinkAsync(string toEmail, Uri photoUrl, CancellationToken ct = default);
}
