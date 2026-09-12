using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Billing.Gateways;

/// <summary>
/// Checkout.com adapter (premium MENA + global cards). 3DS2, Apple/Google Pay, mada.
/// Webhook signature: HMAC-SHA256 over raw body using webhook secret.
///
/// Reads its credentials from <see cref="IRuntimeSettingsProvider"/> (admin-UI
/// DB overrides with env-var fallback) so keys rotate without a redeploy.
/// Webhook verification reads the SAME effective source as payment creation, so
/// an admin key rotation can never desync the signature check.
/// </summary>
public sealed class CheckoutComGateway : IPaymentGateway
{
    public string GatewayName => "checkoutcom";

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;

    public CheckoutComGateway(HttpClient http, IOptions<BillingOptions> billing, IRuntimeSettingsProvider runtimeSettings)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).CheckoutCom;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            if (billing.AllowSandboxFallbacks)
            {
                return new PaymentIntentResult(
                    GatewayTransactionId: $"cko_sandbox_{Guid.NewGuid():N}",
                    ClientSecret: string.Empty,
                    Status: "open",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/cko?ref={request.ProductId}");
            }
            throw new InvalidOperationException("Checkout.com is not configured and sandbox fallbacks are disabled.");
        }

        int minorUnits = (int)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = minorUnits,
            ["currency"] = request.Currency.ToUpperInvariant(),
            ["reference"] = request.ProductId,
            ["description"] = request.Description ?? request.ProductType,
            ["processing_channel_id"] = opts.ProcessingChannelId,
            ["success_url"] = request.SuccessUrl ?? opts.SuccessUrl,
            ["failure_url"] = request.CancelUrl ?? opts.CancelUrl,
            ["customer"] = new Dictionary<string, object?>
            {
                ["email"] = request.Metadata != null && request.Metadata.TryGetValue("email", out var em) ? em : $"{request.UserId}@example.com",
            },
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "hosted-payments"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.SecretKey);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Cko-Idempotency-Key", request.IdempotencyKey);
        }
        message.Content = JsonContent.Create(payload);

        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new PaymentIntentResult(
            GatewayTransactionId: root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Checkout.com response missing id."),
            ClientSecret: string.Empty,
            Status: "open",
            CheckoutUrl: root.GetProperty("_links").GetProperty("redirect").GetProperty("href").GetString()
                ?? throw new InvalidOperationException("Checkout.com response missing redirect href."));
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).CheckoutCom;
        if (string.IsNullOrWhiteSpace(opts.WebhookSecret))
        {
            return new WebhookProcessResult("cko_unconfigured", "signature_missing", false, "Checkout.com webhook secret not configured");
        }

        if (!headers.TryGetValue("Cko-Signature", out var signature) || string.IsNullOrEmpty(signature))
        {
            return new WebhookProcessResult("cko_no_sig", "signature_missing", false, "Missing Cko-Signature header");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(opts.WebhookSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            return new WebhookProcessResult("cko_bad_sig", "signature_invalid", false, "Signature mismatch");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // Replay-attack protection: Cko-Signature is an HMAC over the whole raw body, so
        // the event created_on (or the payment data processed_on) is a signed value.
        var eventData = root.TryGetProperty("data", out var dataRoot) ? dataRoot : default;
        var createdOn = ReadScalar(root, "created_on")
            ?? (eventData.ValueKind == JsonValueKind.Object
                ? ReadScalar(eventData, "processed_on") ?? ReadScalar(eventData, "created_on")
                : null);
        if (IsOutsideReplayWindow(createdOn))
        {
            return new WebhookProcessResult(
                EventId: "cko_stale_callback",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: "Checkout.com callback created_on is outside the accepted replay window.",
                SafePayloadJson: "{}",
                EventCategory: PaymentWebhookCategories.Other);
        }

        var eventId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var eventType = root.TryGetProperty("type", out var t) ? t.GetString() : "payment.unknown";
        var data = root.TryGetProperty("data", out var d) ? d : default;
        var paymentId = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var pid) ? pid.GetString() : null;
        var approved = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("approved", out var ap) && ap.ValueKind == JsonValueKind.True;

        return new WebhookProcessResult(
            EventId: eventId ?? Guid.NewGuid().ToString("N"),
            EventType: eventType ?? "payment.unknown",
            Processed: true,
            Error: null,
            GatewayTransactionId: paymentId,
            NormalizedStatus: approved ? "completed" : (eventType?.Contains("refunded", StringComparison.OrdinalIgnoreCase) == true ? "refunded" : "failed"),
            SafePayloadJson: ProjectCheckoutComSafePayload(root),
            EventCategory: eventType?.Contains("refund", StringComparison.OrdinalIgnoreCase) == true ? PaymentWebhookCategories.Refund : PaymentWebhookCategories.Payment,
            GatewayObjectId: paymentId);
    }

    /// <summary>
    /// Server-to-server payment lookup (GET /payments/{id}). Route to this for
    /// reconciliation and to confirm a callback before fulfilment. Returns null when
    /// Checkout.com is unconfigured, unreachable, or the response is unusable —
    /// callers must treat null as "unknown", never as "unpaid".
    /// </summary>
    public async Task<PaymentConfirmation?> GetTransactionConfirmationAsync(string paymentId, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).CheckoutCom;
        if (string.IsNullOrWhiteSpace(opts.SecretKey) || string.IsNullOrWhiteSpace(paymentId))
        {
            return null;
        }

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), $"payments/{Uri.EscapeDataString(paymentId)}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.SecretKey);

            using var response = await _http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
                ? statusEl.GetString()
                : null;
            var amountMinor = root.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Number && amountEl.TryGetInt64(out var minor)
                ? minor
                : (long?)null;
            var currency = root.TryGetProperty("currency", out var currencyEl) && currencyEl.ValueKind == JsonValueKind.String
                ? currencyEl.GetString()
                : null;
            var providerTransactionId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : paymentId;

            var paid = string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "authorized", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);

            return new PaymentConfirmation(
                Paid: paid,
                Amount: (amountMinor ?? 0) / 100m,
                Currency: currency ?? string.Empty,
                ProviderTransactionId: providerTransactionId,
                RawStatus: status);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).CheckoutCom;
        if (string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            if (_billing.Value.AllowSandboxFallbacks)
            {
                return new RefundResult($"cko_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount);
            }
            throw new InvalidOperationException("Checkout.com refunds not configured.");
        }

        int minorUnits = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), $"payments/{transactionId}/refunds"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.SecretKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Cko-Idempotency-Key", idempotencyKey);
        }
        message.Content = JsonContent.Create(new
        {
            amount = minorUnits,
            reference = reason,
        });

        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new RefundResult(
            RefundId: root.GetProperty("action_id").GetString() ?? Guid.NewGuid().ToString("N"),
            Status: "succeeded",
            AmountRefunded: amount);
    }

    private static string ProjectCheckoutComSafePayload(JsonElement root)
    {
        var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
        var safe = new Dictionary<string, object?>
        {
            ["id"] = ReadScalar(root, "id"),
            ["type"] = ReadScalar(root, "type"),
            ["payment_id"] = ReadScalar(data, "id"),
            ["payment_status"] = ReadScalar(data, "status"),
            ["amount"] = ReadScalar(data, "amount"),
            ["currency"] = ReadScalar(data, "currency"),
            ["reference"] = ReadScalar(data, "reference"),
            ["approved"] = ReadScalar(data, "approved"),
            ["response_code"] = ReadScalar(data, "response_code"),
        };
        return JsonSerializer.Serialize(safe);
    }

    private static string? ReadScalar(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.GetRawText(),
            _ => null,
        };
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private bool IsOutsideReplayWindow(string? timestamp)
    {
        if (!TryParseTimestamp(timestamp, out var parsed))
        {
            return false;
        }

        // Checkout.com retries on a schedule whose first step already exceeds the
        // default window, so this gateway resolves its own (wider) window; a rejected
        // retry would otherwise make a lost first delivery unrecoverable.
        var maxAge = Math.Max(1, _billing.Value.ResolveWebhookMaxAgeSeconds(GatewayName));
        return Math.Abs((DateTimeOffset.UtcNow - parsed).TotalSeconds) > maxAge;
    }

    private static bool TryParseTimestamp(string? raw, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim().Trim('"');
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            if (unix > 99_999_999_999)
            {
                unix /= 1000;
            }

            if (unix is < -62_135_596_800 or > 253_402_300_799)
            {
                return false;
            }

            timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
    }
}
