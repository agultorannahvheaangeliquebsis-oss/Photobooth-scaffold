namespace Photobooth.Core;

/// <summary>
/// Abstracts texting a guest their photo link, the SMS counterpart to
/// IEmailDeliveryService. Same interface-plus-mock seam as everything else --
/// KioskViewModel.SendSmsAsync only ever talks to this interface. No real SMS
/// gateway yet (needs a real vendor account, e.g. Twilio/Semaphore) -- same
/// "mock now, real integration is future work" status IEmailDeliveryService
/// itself started with. This closes the interface gap flagged in
/// BUILD_PLAN.md's Day 3 (the guest sharing screen's phone field and Send
/// button previously did nothing but say so), not the vendor/gateway one.
/// </summary>
public interface ISmsDeliveryService
{
    Task SendPhotoLinkAsync(string toPhone, Uri photoUrl, CancellationToken ct = default);
}
