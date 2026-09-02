using Photobooth.Core;

namespace Photobooth.Tests;

/// <summary>
/// SmtpEmailDeliveryService/TwilioSmsDeliveryService both hit a real network
/// endpoint, so this suite only covers what's testable without one: that an
/// unconfigured Sharing Settings section fails fast with a clear message
/// (via SendPhotoLinkAsync) instead of MailKit/Twilio throwing something
/// opaque three layers down. Real send behavior is exercised by AdminWindow's
/// own Send Test Email/SMS buttons against real credentials, not here.
/// </summary>
public class RealDeliveryServiceValidationTests
{
    [Fact]
    public async Task SmtpEmailDeliveryService_NoSmtpHostConfigured_ThrowsClearError()
    {
        var settings = new MockBoothSettingsProvider();
        var service = new SmtpEmailDeliveryService(settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendPhotoLinkAsync("guest@example.com", new Uri("https://example.com/photo.jpg")));
        Assert.Contains("Sharing Settings", ex.Message);
    }

    [Fact]
    public async Task SmtpEmailDeliveryService_HostSetButNoFromAddress_ThrowsClearError()
    {
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(3, false, PrintTemplate.Default)
            {
                Sharing = new SharingSettings { EmailSmtpHost = "smtp.example.com" },
            },
        };
        var service = new SmtpEmailDeliveryService(settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendPhotoLinkAsync("guest@example.com", new Uri("https://example.com/photo.jpg")));
        Assert.Contains("From address", ex.Message);
    }

    [Fact]
    public async Task TwilioSmsDeliveryService_NoAccountSidConfigured_ThrowsClearError()
    {
        var settings = new MockBoothSettingsProvider();
        var service = new TwilioSmsDeliveryService(settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendPhotoLinkAsync("+15551234567", new Uri("https://example.com/photo.jpg")));
        Assert.Contains("Sharing Settings", ex.Message);
    }

    [Fact]
    public async Task TwilioSmsDeliveryService_SidSetButNoFromNumber_ThrowsClearError()
    {
        var settings = new MockBoothSettingsProvider
        {
            Settings = new BoothSettings(3, false, PrintTemplate.Default)
            {
                Sharing = new SharingSettings { TwilioAccountSid = "AC123" },
            },
        };
        var service = new TwilioSmsDeliveryService(settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendPhotoLinkAsync("+15551234567", new Uri("https://example.com/photo.jpg")));
        Assert.Contains("From Number", ex.Message);
    }
}
