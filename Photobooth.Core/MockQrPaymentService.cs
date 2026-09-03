namespace Photobooth.Core;

/// <summary>
/// Fake QR payment gateway for development. Delay roughly matches how long
/// a guest actually takes to pull out a phone, scan, and confirm in a real
/// GCash/Maya app, so the Payment state in the UI feels accurate during
/// testing rather than flashing by instantly.
/// </summary>
public class MockQrPaymentService : IPaymentService
{
    public Task<PaymentPrompt> InitiateAsync(decimal amount, string reference, CancellationToken ct = default) => Task.FromResult(new PaymentPrompt(
        "Scan to pay, then hold your phone still for a moment.",
        QrCodeGenerator.GeneratePng($"photobooth-mock-pay://{reference}?amount={amount}")));

    public async Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default)
    {
        await Task.Delay(2500, ct);
        return new PaymentResult(Success: true, Method: "qr_gcash", TransactionRef: reference);
    }
}
