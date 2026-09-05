using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Photobooth.Core;

/// <summary>
/// Real IPaymentService: charges through PayMongo, the payment aggregator
/// fronting GCash/Maya for this booth. A direct GCash- or Maya-issued
/// merchant integration needs a formal partnership neither realistic nor
/// necessary for a small booth business -- PayMongo (or a similar
/// aggregator) already holds those partnerships and exposes both wallets
/// behind one API, the same reason most small PH merchants go through one.
///
/// Uses PayMongo's Payment Intents flow: create a Payment Intent (the
/// charge), create a Payment Method of one fixed wallet type (see
/// SharingSettings.PayMongoWalletType -- PayMongo takes one type per
/// attempt, there's no single "either" option), attach the two (PayMongo
/// hands back a checkout URL at that point), then poll the intent's status
/// until the guest approves or declines in their own wallet app. Polling,
/// not a webhook -- this booth runs on a kiosk machine with no public HTTPS
/// endpoint to receive one, and IPaymentService.WaitForConfirmationAsync's
/// shape (a single awaited call) fits polling directly.
///
/// Also routes to ManualConfirmPaymentService when SharingSettings.
/// PaymentProvider is "Manual" -- a booth with no registered business (no
/// TIN) can't get live keys from PayMongo or any other aggregator at all
/// (BSP AML/KYB rules, not a PayMongo-specific limit), so a staffed event
/// where the attendant collects cash or a personal GCash/Maya scan and taps
/// Confirm on the booth itself is the only real option until that's sorted.
///
/// Falls back to MockQrPaymentService whenever neither of the above is
/// actually configured (SharingSettings.PaymentProvider isn't "PayMongo" or
/// "Manual", or PayMongo is selected with no secret key on file) -- same
/// "admin's setting takes effect for the next guest, read fresh every time"
/// reasoning SmtpEmailDeliveryService/TwilioSmsDeliveryService already
/// follow, but with a graceful fallback instead of a thrown "not
/// configured" error: unlike a missing email/SMS config (which just means
/// one guest doesn't get a text), a payment gateway that throws here fails
/// the entire vendo session, so a booth mid-setup needs to keep working
/// exactly as it did before this class existed.
/// </summary>
public class GatewayPaymentService : IPaymentService
{
    private static readonly Uri PayMongoBaseAddress = new("https://api.paymongo.com/v1/");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Caps the polling loop below -- BoothStateMachine's guest-idle
    /// timeout (WithGuestIdleTimeoutAsync) doesn't cancel an already-started
    /// WaitForConfirmationAsync call on a genuine timeout, it just stops
    /// awaiting it (see that method's own doc comment), so an abandoned poll
    /// against a guest who walked away needs its own hard stop rather than
    /// running until the intent finally expires on PayMongo's side.</summary>
    private static readonly TimeSpan MaxPollDuration = TimeSpan.FromMinutes(5);

    private readonly IBoothSettingsProvider _settings;
    private readonly ManualConfirmPaymentService _manual;
    private readonly IPaymentService _mockFallback = new MockQrPaymentService();
    private readonly HttpClient _http = new() { BaseAddress = PayMongoBaseAddress };

    /// <summary>BoothStateMachine's own per-attempt reference (a fresh GUID)
    /// -> the PayMongo payment_intent id InitiateAsync created for it.
    /// IPaymentService.WaitForConfirmationAsync only gets handed that
    /// reference back, not anything InitiateAsync returned, so this is the
    /// only way it knows which intent to poll. Entries are removed once
    /// resolved (or on this instance's teardown, implicitly -- it lives for
    /// one kiosk run, same lifetime as BoothServices itself).</summary>
    private readonly ConcurrentDictionary<string, string> _intentIdsByReference = new();

    public GatewayPaymentService(IBoothSettingsProvider settings, ManualConfirmPaymentService manual)
    {
        _settings = settings;
        _manual = manual;
    }

    public async Task<PaymentPrompt> InitiateAsync(decimal amount, string reference, CancellationToken ct = default)
    {
        SharingSettings sharing = (await _settings.GetSettingsAsync(ct)).Sharing;
        if (sharing.PaymentProvider == "Manual")
        {
            return await _manual.InitiateAsync(amount, reference, ct);
        }
        if (sharing.PaymentProvider != "PayMongo" || string.IsNullOrWhiteSpace(sharing.PayMongoSecretKeyProtected))
        {
            return await _mockFallback.InitiateAsync(amount, reference, ct);
        }

        // Decrypted only for the life of this call -- never cached, never
        // logged, same discipline SmtpEmailDeliveryService's password
        // handling already establishes.
        string secretKey = SecretProtector.Unprotect(sharing.PayMongoSecretKeyProtected);
        string walletType = sharing.PayMongoWalletType is "gcash" or "paymaya" ? sharing.PayMongoWalletType : "gcash";

        string intentId = await CreatePaymentIntentAsync(secretKey, amount, ct);
        string paymentMethodId = await CreatePaymentMethodAsync(secretKey, walletType, ct);
        Uri checkoutUrl = await AttachPaymentMethodAsync(secretKey, intentId, paymentMethodId, ct);

        _intentIdsByReference[reference] = intentId;

        string walletName = walletType == "paymaya" ? "Maya" : "GCash";
        return new PaymentPrompt(
            $"Scan with your phone's camera, then approve the charge in {walletName}.",
            QrCodeGenerator.GeneratePng(checkoutUrl.ToString()));
    }

    /// <summary>Forwards to whichever provider actually owns this reference,
    /// mirroring WaitForConfirmationAsync's own routing: the manual attempt if
    /// there is one, and either way drop the intent mapping so an abandoned
    /// PayMongo attempt doesn't keep its entry in
    /// <see cref="_intentIdsByReference"/>.</summary>
    public void CancelPending(string reference)
    {
        _manual.CancelPending(reference);
        _intentIdsByReference.TryRemove(reference, out _);
    }

    public async Task<PaymentResult> WaitForConfirmationAsync(string reference, decimal amount, CancellationToken ct = default)
    {
        if (_manual.HasPending(reference))
        {
            return await _manual.WaitForConfirmationAsync(reference, amount, ct);
        }
        if (!_intentIdsByReference.TryRemove(reference, out string? intentId))
        {
            // InitiateAsync fell back to the mock for this reference (not
            // configured, or the admin flipped it back mid-attempt) -- match
            // that here too, rather than the two calls disagreeing.
            return await _mockFallback.WaitForConfirmationAsync(reference, amount, ct);
        }

        SharingSettings sharing = (await _settings.GetSettingsAsync(ct)).Sharing;
        string secretKey = SecretProtector.Unprotect(sharing.PayMongoSecretKeyProtected);
        string method = sharing.PayMongoWalletType == "paymaya" ? "paymaya" : "gcash";

        DateTime deadline = DateTime.UtcNow + MaxPollDuration;
        while (DateTime.UtcNow < deadline)
        {
            string status = await GetPaymentIntentStatusAsync(secretKey, intentId, ct);
            if (status == "succeeded")
            {
                return new PaymentResult(Success: true, Method: method, TransactionRef: intentId);
            }
            if (status == "awaiting_payment_method")
            {
                // PayMongo drops a declined/expired e-wallet attempt back to
                // this status (the same one a fresh, unattached intent
                // starts in) rather than a dedicated "failed" status.
                return new PaymentResult(Success: false, Method: method, TransactionRef: null);
            }

            await Task.Delay(PollInterval, ct);
        }

        return new PaymentResult(Success: false, Method: method, TransactionRef: null);
    }

    private async Task<string> CreatePaymentIntentAsync(string secretKey, decimal amount, CancellationToken ct)
    {
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    amount = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero), // PayMongo amounts are in centavos
                    payment_method_allowed = new[] { "gcash", "paymaya" },
                    currency = "PHP",
                    capture_type = "automatic",
                },
            },
        };

        using JsonDocument response = await PostAsync(secretKey, "payment_intents", payload, ct);
        return response.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("PayMongo didn't return a payment_intent id.");
    }

    private async Task<string> CreatePaymentMethodAsync(string secretKey, string walletType, CancellationToken ct)
    {
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    type = walletType,
                    billing = new { name = "Photobooth Guest" },
                },
            },
        };

        using JsonDocument response = await PostAsync(secretKey, "payment_methods", payload, ct);
        return response.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("PayMongo didn't return a payment_method id.");
    }

    private async Task<Uri> AttachPaymentMethodAsync(string secretKey, string intentId, string paymentMethodId, CancellationToken ct)
    {
        var payload = new
        {
            data = new
            {
                attributes = new
                {
                    payment_method = paymentMethodId,
                    // PayMongo requires a return_url even though this kiosk
                    // has no browser flow to return the guest to -- they
                    // never leave their wallet app (scan, approve, done), so
                    // this URL is never actually visited.
                    return_url = "https://paymongo.com",
                },
            },
        };

        using JsonDocument response = await PostAsync(secretKey, $"payment_intents/{intentId}/attach", payload, ct);
        JsonElement attributes = response.RootElement.GetProperty("data").GetProperty("attributes");
        if (!attributes.TryGetProperty("next_action", out JsonElement nextAction) ||
            !nextAction.TryGetProperty("redirect", out JsonElement redirect) ||
            redirect.GetProperty("url").GetString() is not string checkoutUrl)
        {
            throw new InvalidOperationException("PayMongo didn't return a checkout URL for the guest to scan.");
        }

        return new Uri(checkoutUrl);
    }

    private async Task<string> GetPaymentIntentStatusAsync(string secretKey, string intentId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"payment_intents/{intentId}");
        request.Headers.Authorization = BasicAuthHeader(secretKey);
        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return document.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("status").GetString()
            ?? throw new InvalidOperationException("PayMongo didn't return a payment_intent status.");
    }

    private async Task<JsonDocument> PostAsync(string secretKey, string path, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = BasicAuthHeader(secretKey);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PayMongo request to {path} failed ({(int)response.StatusCode}): {body}");
        }

        return JsonDocument.Parse(body);
    }

    /// <summary>PayMongo authenticates with HTTP Basic auth: the secret key
    /// as the username, no password.</summary>
    private static AuthenticationHeaderValue BasicAuthHeader(string secretKey) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:")));
}
