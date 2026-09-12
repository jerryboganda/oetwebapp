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
/// PayTabs adapter (UAE/Saudi/Oman/Qatar/Kuwait/Bahrain/Egypt/Jordan).
/// Hosted Payment Page API. Webhook HMAC-SHA256.
///
/// Reads credentials from <see cref="IRuntimeSettingsProvider"/> (admin-UI DB
/// overrides with env fallback); webhook verification uses the same effective
/// secret as payment creation.
/// </summary>
public sealed class PayTabsGateway : IPaymentGateway
{
    public string GatewayName => "paytabs";

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;

    public PayTabsGateway(HttpClient http, IOptions<BillingOptions> billing, IRuntimeSettingsProvider runtimeSettings)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).PayTabs;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.ServerKey) || string.IsNullOrWhiteSpace(opts.ProfileId))
        {
            if (billing.AllowSandboxFallbacks)
            {
                return new PaymentIntentResult(
                    GatewayTransactionId: $"paytabs_sandbox_{Guid.NewGuid():N}",
                    ClientSecret: string.Empty,
                    Status: "open",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/paytabs?ref={request.ProductId}");
            }
            throw new InvalidOperationException("PayTabs is not configured and sandbox fallbacks are disabled.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["profile_id"] = opts.ProfileId,
            ["tran_type"] = "sale",
            ["tran_class"] = "ecom",
            ["cart_id"] = request.ProductId ?? Guid.NewGuid().ToString("N"),
            ["cart_currency"] = request.Currency.ToUpperInvariant(),
            ["cart_amount"] = request.Amount.ToString("F2", CultureInfo.InvariantCulture),
            ["cart_description"] = request.Description ?? request.ProductType,
            ["return"] = request.SuccessUrl ?? opts.SuccessUrl,
            ["callback"] = request.CancelUrl ?? opts.CancelUrl,
            ["customer_ref"] = request.UserId,
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "payment/request"));
        message.Headers.TryAddWithoutValidation("Authorization", opts.ServerKey);
        message.Content = JsonContent.Create(payload);

        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new PaymentIntentResult(
            GatewayTransactionId: GetStringOrThrow(root, "tran_ref"),
            ClientSecret: string.Empty,
            Status: "open",
            CheckoutUrl: GetStringOrThrow(root, "redirect_url"));
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).PayTabs;
        if (string.IsNullOrWhiteSpace(opts.WebhookSecret))
        {
            return new WebhookProcessResult("paytabs_unconfigured", "signature_missing", false, "PayTabs webhook secret not configured");
        }

        if (!headers.TryGetValue("Signature", out var signature) || string.IsNullOrEmpty(signature))
        {
            return new WebhookProcessResult("paytabs_no_sig", "signature_missing", false, "Missing Signature header");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(opts.WebhookSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            return new WebhookProcessResult("paytabs_bad_sig", "signature_invalid", false, "Signature mismatch");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // Replay-attack protection: the Signature header is an HMAC over the whole raw
        // body, so payment_result.transaction_time is a signed value.
        if (IsOutsideReplayWindow(GetStringOrNull(root, "payment_result.transaction_time")))
        {
            return new WebhookProcessResult(
                EventId: "paytabs_stale_callback",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: "PayTabs callback transaction_time is outside the accepted replay window.",
                SafePayloadJson: "{}",
                EventCategory: PaymentWebhookCategories.Other);
        }

        var tranRef = GetStringOrNull(root, "tran_ref");
        var paymentStatus = GetStringOrNull(root, "payment_result.response_status");

        return new WebhookProcessResult(
            EventId: tranRef ?? Guid.NewGuid().ToString("N"),
            EventType: paymentStatus ?? "payment.unknown",
            Processed: true,
            Error: null,
            GatewayTransactionId: tranRef,
            NormalizedStatus: paymentStatus switch { "A" => "completed", "H" => "pending", "P" => "pending", _ => "failed" },
            SafePayloadJson: ProjectPayTabsSafePayload(root),
            EventCategory: PaymentWebhookCategories.Payment,
            GatewayObjectId: tranRef);
    }

    /// <summary>
    /// Server-to-server transaction lookup (POST /payment/query). Route to this for
    /// reconciliation and to confirm a callback before fulfilment. Returns null when
    /// PayTabs is unconfigured, unreachable, or the response is unusable — callers
    /// must treat null as "unknown", never as "unpaid".
    /// </summary>
    public async Task<PaymentConfirmation?> GetTransactionConfirmationAsync(string tranRef, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).PayTabs;
        if (string.IsNullOrWhiteSpace(opts.ServerKey) || string.IsNullOrWhiteSpace(opts.ProfileId) || string.IsNullOrWhiteSpace(tranRef))
        {
            return null;
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["profile_id"] = opts.ProfileId,
                ["tran_ref"] = tranRef,
            };

            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "payment/query"));
            message.Headers.TryAddWithoutValidation("Authorization", opts.ServerKey);
            message.Content = JsonContent.Create(payload);

            using var response = await _http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var responseStatus = GetStringOrNull(root, "payment_result.response_status");
            var amount = decimal.TryParse(GetStringOrNull(root, "cart_amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;

            return new PaymentConfirmation(
                Paid: string.Equals(responseStatus, "A", StringComparison.OrdinalIgnoreCase),
                Amount: amount,
                Currency: GetStringOrNull(root, "cart_currency") ?? string.Empty,
                ProviderTransactionId: GetStringOrNull(root, "tran_ref") ?? tranRef,
                RawStatus: responseStatus);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).PayTabs;
        if (string.IsNullOrWhiteSpace(opts.ServerKey))
        {
            if (_billing.Value.AllowSandboxFallbacks)
            {
                return new RefundResult($"paytabs_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount);
            }
            throw new InvalidOperationException("PayTabs refunds not configured.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["profile_id"] = opts.ProfileId,
            ["tran_type"] = "refund",
            ["tran_class"] = "ecom",
            ["cart_id"] = transactionId,
            ["cart_currency"] = currency.ToUpperInvariant(),
            ["cart_amount"] = amount.ToString("F2", CultureInfo.InvariantCulture),
            ["cart_description"] = reason,
            ["tran_ref"] = transactionId,
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "payment/request"));
        message.Headers.TryAddWithoutValidation("Authorization", opts.ServerKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        message.Content = JsonContent.Create(payload);

        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new RefundResult(
            RefundId: GetStringOrThrow(root, "tran_ref"),
            Status: GetStringOrNull(root, "payment_result.response_status") == "A" ? "succeeded" : "pending",
            AmountRefunded: amount);
    }

    private static string ProjectPayTabsSafePayload(JsonElement root)
    {
        var safe = new Dictionary<string, object?>
        {
            ["tran_ref"] = GetStringOrNull(root, "tran_ref"),
            ["cart_id"] = GetStringOrNull(root, "cart_id"),
            ["cart_amount"] = GetStringOrNull(root, "cart_amount"),
            ["cart_currency"] = GetStringOrNull(root, "cart_currency"),
            ["response_status"] = GetStringOrNull(root, "payment_result.response_status"),
            ["response_code"] = GetStringOrNull(root, "payment_result.response_code"),
            ["transaction_time"] = GetStringOrNull(root, "payment_result.transaction_time"),
        };
        return JsonSerializer.Serialize(safe);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private bool IsOutsideReplayWindow(string? timestamp)
    {
        if (!TryParseTimestamp(timestamp, out var parsed))
        {
            return false;
        }

        var maxAge = Math.Max(1, _billing.Value.WebhookMaxAgeSeconds);
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

    private static string GetStringOrThrow(JsonElement root, string property)
        => GetStringOrNull(root, property) ?? throw new InvalidOperationException($"PayTabs response missing {property}.");

    private static string? GetStringOrNull(JsonElement root, string dottedPath)
    {
        var element = root;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
            {
                return null;
            }
            element = next;
        }
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }
}
