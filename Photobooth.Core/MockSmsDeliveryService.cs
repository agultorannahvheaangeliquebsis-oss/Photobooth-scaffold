namespace Photobooth.Core;

/// <summary>
/// Fake SMS delivery for development and tests. Exposes what it "sent" so
/// tests can assert against it, same pattern as MockEmailDeliveryService.
/// </summary>
public class MockSmsDeliveryService : ISmsDeliveryService
{
    public List<(string ToPhone, Uri PhotoUrl)> SentMessages { get; } = new();

    public async Task SendPhotoLinkAsync(string toPhone, Uri photoUrl, CancellationToken ct = default)
    {
        // Real SMS delivery has network latency; simulate it so this doesn't
        // behave suspiciously differently from a real send.
        await Task.Delay(300, ct);
        SentMessages.Add((toPhone, photoUrl));
    }
}
