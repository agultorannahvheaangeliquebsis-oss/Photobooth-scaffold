using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Photobooth.Core;

/// <summary>
/// Real IEmailDeliveryService: sends over SMTP via MailKit (System.Net.Mail's
/// SmtpClient is officially discouraged by Microsoft and lacks modern TLS/
/// auth support). Settings come from IBoothSettingsProvider, fetched fresh on
/// every send rather than captured once at construction -- same "admin's
/// change takes effect for the next guest, not after an app restart"
/// reasoning SqlBoothSettingsProvider itself documents.
/// </summary>
public class SmtpEmailDeliveryService : IEmailDeliveryService
{
    private readonly IBoothSettingsProvider _settings;

    public SmtpEmailDeliveryService(IBoothSettingsProvider settings)
    {
        _settings = settings;
    }

    public async Task SendPhotoLinkAsync(string toEmail, Uri photoUrl, CancellationToken ct = default)
    {
        SharingSettings sharing = (await _settings.GetSettingsAsync(ct)).Sharing;
        if (string.IsNullOrWhiteSpace(sharing.EmailSmtpHost) || string.IsNullOrWhiteSpace(sharing.EmailFromAddress))
        {
            throw new InvalidOperationException(
                "Email sharing isn't fully configured yet -- set the SMTP host and From address in Sharing Settings.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sharing.EmailFromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = sharing.EmailSubject;
        message.Body = new TextPart("html")
        {
            Text = $"<h2>{System.Net.WebUtility.HtmlEncode(sharing.EmailSubject)}</h2>" +
                   $"<p><a href=\"{photoUrl}\">{photoUrl}</a></p>",
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            sharing.EmailSmtpHost,
            sharing.EmailSmtpPort,
            sharing.EmailUseSsl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None,
            ct);
        try
        {
            if (!string.IsNullOrEmpty(sharing.EmailSmtpUsername))
            {
                // Decrypted only for the life of this call -- never cached,
                // never logged.
                string password = SecretProtector.Unprotect(sharing.EmailSmtpPasswordProtected);
                await client.AuthenticateAsync(sharing.EmailSmtpUsername, password, ct);
            }

            await client.SendAsync(message, ct);
        }
        finally
        {
            await client.DisconnectAsync(quit: true, ct);
        }
    }
}
