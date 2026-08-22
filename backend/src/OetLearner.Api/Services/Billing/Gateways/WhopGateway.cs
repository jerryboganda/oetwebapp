using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing.Gateways;

/// <summary>
/// Whop adapter — primary Global / Outside Egypt gateway.
/// Creates a checkout configuration with an inline one-time plan, then the
/// website renders Whop Embedded Checkout. Fulfilment is webhook-driven
/// (payment.succeeded) with optional HMAC verification plus API confirmation.
/// </summary>
public sealed class WhopGateway : IPaymentGateway
{
    public string GatewayName => PaymentGatewayNames.Whop;

    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "GBP", "EUR",
    };

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IFxRateService? _fx;
    private readonly ILogger<WhopGateway>? _logger;

    public WhopGateway(
        HttpClient http,
        IOptions<BillingOptions> billing,
        IRuntimeSettingsProvider runtimeSettings,
        IFxRateService? fx = null,
        ILogger<WhopGateway>? logger = null)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
        _fx = fx;
        _logger = logger;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Whop;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            if (billing.AllowSandboxFallbacks)
            {
                var sandboxId = $"whop_sandbox_{Guid.NewGuid():N}";
                return new PaymentIntentResult(
                    GatewayTransactionId: sandboxId,
                    ClientSecret: sandboxId,
                    Status: "open",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/whop?ref={request.ProductId}");
            }

            throw new InvalidOperationException("Whop is not configured and sandbox fallbacks are disabled.");
        }

        var quoteId = SanitizeId(request.ProductId);
        var (amount, currency) = await ResolveChargeCurrencyAsync(request, ct);
        object price = amount == decimal.Truncate(amount)
            ? (int)amount
            : decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

        var plan = new Dictionary<string, object?>
        {
            ["plan_type"] = "one_time",
            ["currency"] = currency.ToLowerInvariant(),
            ["initial_price"] = price,
            ["title"] = "OET With Dr Hesham",
        };

        var payload = new Dictionary<string, object?>
        {
            ["plan"] = plan,
            ["metadata"] = new Dictionary<string, string>
            {
                ["order_id"] = quoteId,
                ["quote_id"] = request.ProductId ?? quoteId,
            },
        };

        var first = await PostCheckoutAsync(opts, payload, ct);
        var status = first.Status;
        var body = first.Body;

        if (status is < 200 or >= 300)
        {
            _logger?.LogWarning("Whop checkout configuration failed with HTTP {Status}", status);
            throw new PaymentGatewayApiException(
                GatewayName,
                status,
                $"Whop checkout configuration failed: {status}",
                upstreamErrorType: ReadErrorType(body));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var nested) ? nested : root;
        var configId = ReadString(data, "id") ?? throw new InvalidOperationException("Whop response missing checkout configuration id.");
        var planId = ReadNestedString(data, "plan", "id") ?? configId;
        var checkoutUrl = ReadString(data, "purchase_url")
            ?? ReadString(data, "url")
            ?? $"https://whop.com/embedded/checkout/{Uri.EscapeDataString(configId)}/";

        return new PaymentIntentResult(
            GatewayTransactionId: configId,
            ClientSecret: planId,
            Status: "open",
            CheckoutUrl: checkoutUrl);
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Whop;
        if (string.IsNullOrWhiteSpace(opts.ApiKey) && string.IsNullOrWhiteSpace(opts.WebhookSecret)
            && !_billing.Value.AllowSandboxFallbacks)
        {
            return new WebhookProcessResult("whop_unconfigured", "signature_missing", false, "Whop is not configured");
        }

        if (!string.IsNullOrWhiteSpace(opts.WebhookSecret))
        {
            if (!TryGetHeader(headers, "webhook-signature", out var signature)
                && !TryGetHeader(headers, "whop-signature", out signature)
                && !TryGetHeader(headers, "Whop-Signature", out signature))
            {
                return new WebhookProcessResult("whop_no_sig", "signature_missing", false, "Missing Whop signature header");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!PaymentCallbackHmac.WhopSignatureMatches(opts.WebhookSecret, payload, signature, now, _billing.Value.WebhookMaxAgeSeconds))
            {
                return new WebhookProcessResult("whop_bad_sig", "signature_invalid", false, "Signature mismatch");
            }
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        }
        catch (JsonException)
        {
            return new WebhookProcessResult("whop_bad_payload", "payload_invalid", false, "Callback payload was not valid JSON");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var type = ReadString(root, "type") ?? ReadString(root, "action") ?? ReadString(root, "event") ?? "payment.unknown";
            var data = root.TryGetProperty("data", out var dataEl) ? dataEl : root;
            var paymentId = ReadString(data, "id")
                ?? ReadNestedString(data, "payment", "id")
                ?? ReadString(root, "id")
                ?? Guid.NewGuid().ToString("N");
            var statusRaw = ReadString(data, "status") ?? type;
            var quoteId = ReadNestedString(data, "metadata", "quote_id")
                ?? ReadNestedString(data, "metadata", "order_id")
                ?? ReadNestedString(root, "metadata", "quote_id")
                ?? ReadNestedString(root, "metadata", "order_id");

            if (!string.IsNullOrWhiteSpace(opts.ApiKey) && !paymentId.StartsWith("whop_sandbox_", StringComparison.OrdinalIgnoreCase))
            {
                var confirmed = await ConfirmPaymentAsync(opts, paymentId, ct);
                if (confirmed is false)
                {
                    return new WebhookProcessResult(paymentId, type, false, "Whop API did not confirm this payment");
                }
            }

            var succeeded = type.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
                || type.Contains("paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusRaw, "paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusRaw, "succeeded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusRaw, "complete", StringComparison.OrdinalIgnoreCase);

            return new WebhookProcessResult(
                EventId: paymentId,
                EventType: type,
                Processed: true,
                Error: null,
                GatewayTransactionId: quoteId ?? paymentId,
                NormalizedStatus: succeeded ? "completed" : "pending",
                SafePayloadJson: JsonSerializer.Serialize(new
                {
                    type,
                    paymentId,
                    quoteId,
                    status = statusRaw,
                }),
                EventCategory: PaymentWebhookCategories.Payment,
                GatewayObjectId: paymentId);
        }
    }

    public Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        if (_billing.Value.AllowSandboxFallbacks)
        {
            return Task.FromResult(new RefundResult($"whop_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount));
        }

        throw new InvalidOperationException("Whop refunds are processed from the Whop dashboard.");
    }

    private async Task<(decimal Amount, string Currency)> ResolveChargeCurrencyAsync(
        CreatePaymentIntentRequest request,
        CancellationToken ct)
    {
        var currency = (request.Currency ?? "USD").Trim().ToUpperInvariant();
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (SupportedCurrencies.Contains(currency))
        {
            return (amount, currency);
        }

        if (_fx is null)
        {
            throw new PaymentGatewayApiException(
                GatewayName,
                422,
                $"Whop does not accept {currency} and FX conversion is unavailable.");
        }

        decimal converted;
        try
        {
            converted = decimal.Round(await _fx.ConvertAsync(amount, currency, "USD", ct), 2, MidpointRounding.AwayFromZero);
        }
        catch (InvalidOperationException ex)
        {
            throw new PaymentGatewayApiException(GatewayName, 422, ex.Message);
        }
        if (converted <= 0)
        {
            throw new PaymentGatewayApiException(
                GatewayName,
                422,
                $"Whop FX conversion produced an invalid {currency}->USD amount.");
        }

        _logger?.LogInformation("Whop converting {From} to USD for checkout", currency);
        return (converted, "USD");
    }

    private async Task<(int Status, string Body)> PostCheckoutAsync(
        WhopSettings opts,
        Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Combine(opts.ApiBaseUrl, "checkout_configurations"))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = JsonContent.Create(payload),
        };
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + opts.ApiKey!.Trim());
        using var response = await _http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return ((int)response.StatusCode, body);
    }

    private static string SanitizeId(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
        var chars = raw.Where(char.IsLetterOrDigit).Take(32).ToArray();
        return chars.Length > 0 ? new string(chars) : Guid.NewGuid().ToString("N")[..32];
    }

    private static string? ReadErrorType(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    return type.GetString();
                }

                if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    return code.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? StripStripeSessionPlaceholder(string? url)
        => url?.Replace("{CHECKOUT_SESSION_ID}", string.Empty, StringComparison.Ordinal);

    private async Task<bool?> ConfirmPaymentAsync(WhopSettings opts, string paymentId, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, Combine(opts.ApiBaseUrl, $"payments/{Uri.EscapeDataString(paymentId)}"))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + opts.ApiKey!.Trim());
            using var response = await _http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
            var status = ReadString(root, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                return true;
            }

            return status.Contains("paid", StringComparison.OrdinalIgnoreCase)
                || status.Contains("succeed", StringComparison.OrdinalIgnoreCase)
                || status.Contains("complete", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Uri Combine(string baseUrl, string path)
        => new(new Uri(EnsureTrailingSlash(baseUrl)), path);

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
    {
        foreach (var (key, headerValue) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = headerValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static string? ReadNestedString(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(parent, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(nested, child);
    }
}
