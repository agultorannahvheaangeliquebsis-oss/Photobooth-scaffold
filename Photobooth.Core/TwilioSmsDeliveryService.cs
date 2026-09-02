using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Photobooth.Core;

/// <summary>
/// Real ISmsDeliveryService: sends over Twilio's REST API. Settings come
/// from IBoothSettingsProvider, fetched fresh on every send rather than
/// captured once at construction, same reasoning SmtpEmailDeliveryService
/// documents. Builds its own TwilioRestClient per send (rather than the
/// static TwilioClient.Init(...) style Twilio's own docs lead with) so a
/// credentials change in Sharing Settings takes effect immediately and two
/// concurrent sends can never race over shared global state.
/// </summary>
public class TwilioSmsDeliveryService : ISmsDeliveryService
{
    private readonly IBoothSettingsProvider _settings;

    public TwilioSmsDeliveryService(IBoothSettingsProvider settings)
    {
        _settings = settings;
    }

    public async Task SendPhotoLinkAsync(string toPhone, Uri photoUrl, CancellationToken ct = default)
    {
        SharingSettings sharing = (await _settings.GetSettingsAsync(ct)).Sharing;
        if (string.IsNullOrWhiteSpace(sharing.TwilioAccountSid) || string.IsNullOrWhiteSpace(sharing.TwilioFromNumber))
        {
            throw new InvalidOperationException(
                "SMS sharing isn't fully configured yet -- set the Twilio Account SID and From Number in Sharing Settings.");
        }

        // Decrypted only for the life of this call -- never cached, never
        // logged.
        string authToken = SecretProtector.Unprotect(sharing.TwilioAuthTokenProtected);
        ITwilioRestClient client = new TwilioRestClient(sharing.TwilioAccountSid, authToken);

        // The installed Twilio SDK's MessageResource.CreateAsync has no
        // CancellationToken overload -- ct is still accepted on this method
        // for interface conformance and so a future SDK version that adds
        // one doesn't need a signature change here.
        await MessageResource.CreateAsync(
            body: $"Here is your photo: {photoUrl}",
            from: new PhoneNumber(sharing.TwilioFromNumber),
            to: new PhoneNumber(toPhone),
            client: client);
    }
}
