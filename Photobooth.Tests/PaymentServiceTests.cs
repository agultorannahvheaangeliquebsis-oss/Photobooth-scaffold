using Photobooth.Core;

namespace Photobooth.Tests;

public class MockQrPaymentServiceTests
{
    [Fact]
    public async Task InitiateAsync_ReturnsScanInstructionsWithAQrCode()
    {
        var payment = new MockQrPaymentService();

        PaymentPrompt prompt = await payment.InitiateAsync(150m, "ref-1");

        Assert.Contains("Scan", prompt.Instructions);
        Assert.NotNull(prompt.QrCodePng);
        // PNG files start with the 0x89 'P' 'N' 'G' magic bytes.
        Assert.Equal(0x89, prompt.QrCodePng![0]);
    }

    [Fact]
    public async Task WaitForConfirmationAsync_ReportsSuccessAsQrGcash()
    {
        var payment = new MockQrPaymentService();

        PaymentResult result = await payment.WaitForConfirmationAsync("ref-1", 150m);

        Assert.True(result.Success);
        Assert.Equal("qr_gcash", result.Method);
        Assert.Equal("ref-1", result.TransactionRef);
    }
}

public class MockCardReaderPaymentServiceTests
{
    [Fact]
    public async Task InitiateAsync_ReturnsCardInstructionsWithNoQrCode()
    {
        var payment = new MockCardReaderPaymentService();

        PaymentPrompt prompt = await payment.InitiateAsync(150m, "ref-1");

        Assert.Contains("card", prompt.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Null(prompt.QrCodePng);
    }

    [Fact]
    public async Task WaitForConfirmationAsync_DefaultsToApprovedAsCard()
    {
        var payment = new MockCardReaderPaymentService();

        PaymentResult result = await payment.WaitForConfirmationAsync("ref-1", 150m);

        Assert.True(result.Success);
        Assert.Equal("card", result.Method);
        Assert.NotNull(result.TransactionRef);
    }

    [Fact]
    public async Task WaitForConfirmationAsync_WhenDeclineNextSet_ReportsDeclinedOnceThenResets()
    {
        var payment = new MockCardReaderPaymentService { DeclineNext = true };

        PaymentResult declined = await payment.WaitForConfirmationAsync("ref-1", 150m);
        Assert.False(declined.Success);
        Assert.Equal("card", declined.Method);
        Assert.Null(declined.TransactionRef);
        Assert.False(payment.DeclineNext);

        PaymentResult next = await payment.WaitForConfirmationAsync("ref-2", 150m);
        Assert.True(next.Success);
    }
}
