namespace Photobooth.Core;

/// <summary>
/// Fake email delivery for development and tests. Exposes what it "sent"
/// so tests and the console demo can assert against it, same pattern as
/// MockSessionRepository.
/// </summary>
public class MockEmailDeliveryService : IEmailDeliveryService
{
    public List<(string ToEmail, Uri PhotoUrl)> SentEmails { get; } = new();

    public async Task SendPhotoLinkAsync(string toEmail, Uri photoUrl, CancellationToken ct = default)
    {
        // Real SMTP delivery has network latency; simulate it so this
        // doesn't behave suspiciously differently from a real send.
        await Task.Delay(300, ct);
        SentEmails.Add((toEmail, photoUrl));
    }
}
