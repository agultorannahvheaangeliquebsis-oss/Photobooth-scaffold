namespace Photobooth.Core;

/// <summary>
/// Second IPaymentService implementation: a simulated card-reader flow
/// (tap/insert/swipe) instead of a QR scan. Proves the interface actually
/// generalizes beyond "generate a QR code" -- Initiate returns a null
/// QrCodePng here since there's nothing to scan. Still fully mocked: no
/// real card-reader hardware or payment gateway integration exists yet,
/// same status the QR gateway has (see the "Cashless payments" roadmap
/// item).
/// </summary>
public class MockCardReaderPaymentService : IPaymentService
{
    /// <summary>When true, the next WaitForConfirmationAsync call reports a declined
    /// card instead of an approved one. Resets itself after firing once, same
    /// pattern as MockCameraService.FailNextCapture.</summary>
    public bool DeclineNext { get; set; } = false;

    public PaymentPrompt Initiate(decimal amount, string reference) =>
        new("Tap, insert, or swipe your card to pay.", QrCodePng: null);

    public async Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default)
    {
        // A card-present transaction authorizes in a couple seconds -- no
        // phone/app round trip for the guest, so this is faster than the QR
        // gateway's simulated scan-and-confirm wait.
        await Task.Delay(1200, ct);

        if (DeclineNext)
        {
            DeclineNext = false;
            return new PaymentResult(Success: false, Method: "card", TransactionRef: null);
        }

        return new PaymentResult(Success: true, Method: "card", TransactionRef: Guid.NewGuid().ToString("N"));
    }
}
