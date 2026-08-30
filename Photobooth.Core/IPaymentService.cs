namespace Photobooth.Core;

/// <summary>What the Payment screen shows the guest right after a payment attempt starts.
/// QrCodePng is null for a gateway with nothing to scan (e.g. a card reader) --
/// this is why the interface can't just be "generate a QR code" (see
/// MockCardReaderPaymentService).</summary>
public record PaymentPrompt(string Instructions, byte[]? QrCodePng);

/// <summary>Result of a completed (or failed) vendo-mode payment attempt.</summary>
public record PaymentResult(bool Success, string Method, string? TransactionRef);

/// <summary>
/// Abstracts collecting payment for a vendo-mode session, before printing.
/// Same interface-plus-mock seam as camera/printer/cloud upload/session
/// repository -- BoothStateMachine only ever talks to this interface. Two
/// mocks exist: MockQrPaymentService (GCash/Maya-style scan-to-pay) and
/// MockCardReaderPaymentService (tap/insert/swipe, no QR at all). No real
/// gateway or card-reader hardware integration yet -- that's still future
/// work (see the "Cashless payments" roadmap item).
/// </summary>
public interface IPaymentService
{
    /// <summary>Starts a payment attempt and returns what to show the guest. Synchronous since neither mock has a network call to make here; a real gateway would likely need this to be async instead (e.g. creating a payment intent).</summary>
    PaymentPrompt Initiate(decimal amount, string reference);

    /// <summary>Waits for the payment started by Initiate to be confirmed, declined, or time out. In production this would poll the gateway or wait on a webhook; the mocks just simulate guest confirmation time.</summary>
    Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default);
}
