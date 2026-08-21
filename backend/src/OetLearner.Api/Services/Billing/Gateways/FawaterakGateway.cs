using System.Globalization;
using System.Net;
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
/// Fawaterak adapter — secondary Global / Outside Egypt gateway.
/// Creates an invoice and returns an iframe/hosted URL. Fulfilment requires a
/// verified HASH callback; browser success/fail/pending redirects never mark paid.
/// </summary>
public sealed class FawaterakGateway : IPaymentGateway
{
    public string GatewayName => PaymentGatewayNames.Fawaterak;

    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "GBP", "EGP", "EUR", "AED", "SAR",
    };

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IFxRateService? _fx;
    private readonly ILogger<FawaterakGateway>? _logger;

    public FawaterakGateway(
        HttpClient http,
        IOptions<BillingOptions> billing,
        IRuntimeSettingsProvider runtimeSettings,
        IFxRateService? fx = null,
        ILogger<FawaterakGateway>? logger = null)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
        _fx = fx;
        _logger = logger;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Fawaterak;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.HashApiKey))
        {
            if (billing.AllowSandboxFallbacks)
            {
                var sandboxId = $"fawaterak_sandbox_{Guid.NewGuid():N}";
                return new PaymentIntentResult(
                    GatewayTransactionId: sandboxId,
                    ClientSecret: sandboxId,
                    Status: "open",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/fawaterak?ref={request.ProductId}");
            }

            throw new InvalidOperationException("Fawaterak is not configured and sandbox fallbacks are disabled.");
        }

        var quoteId = request.ProductId ?? Guid.NewGuid().ToString("N");
        var (amountValue, currency) = await ResolveChargeCurrencyAsync(request, ct);
        var amount = amountValue.ToString("F2", CultureInfo.InvariantCulture);
        var payload = new Dictionary<string, object?>
        {
            ["cartTotal"] = amount,
            ["currency"] = currency,
            ["invoice_number"] = quoteId,
            ["customer"] = new Dictionary<string, string>
            {
                ["first_name"] = "Learner",
                ["last_name"] = request.UserId.Length > 24 ? request.UserId[..24] : request.UserId,
                ["email"] = request.Metadata is not null && request.Metadata.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email)
                    ? email
                    : "noreply@oetwithdrhesham.co.uk",
                ["phone"] = "0000000000",
                ["address"] = "NA",
            },
            ["redirectionUrls"] = new Dictionary<string, string?>
            {
                ["successUrl"] = StripStripeSessionPlaceholder(request.SuccessUrl) ?? opts.SuccessUrl,
                ["failUrl"] = StripStripeSessionPlaceholder(request.CancelUrl) ?? opts.FailUrl,
                ["pendingUrl"] = opts.PendingUrl
                    ?? StripStripeSessionPlaceholder(request.SuccessUrl)
                    ?? opts.SuccessUrl,
            },
            ["cartItems"] = new object[]
            {
                new Dictionary<string, string>
                {
                    ["name"] = string.IsNullOrWhiteSpace(request.Description) ? "OET With Dr Hesham" : request.Description,
                    ["price"] = amount,
                    ["quantity"] = "1",
                },
            },
            ["payLoad"] = quoteId,
        };
        if (!string.IsNullOrWhiteSpace(opts.ProviderKey))
        {
            payload["vendorKey"] = opts.ProviderKey;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, Combine(opts.ApiBaseUrl, "api/v2/invoiceInitPay"))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + opts.HashApiKey.Trim());
        message.Headers.TryAddWithoutValidation("User-Agent", "OetWithDrHesham/1.0");

        using var response = await _http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Fawaterak invoiceInitPay failed with HTTP {Status}", (int)response.StatusCode);
            throw new PaymentGatewayApiException(
                GatewayName,
                (int)response.StatusCode,
                $"Fawaterak invoiceInitPay failed: {(int)response.StatusCode} {Truncate(body, 400)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        if (root.TryGetProperty("status", out var statusEl))
        {
            var status = statusEl.ValueKind == JsonValueKind.String ? statusEl.GetString() : statusEl.GetRawText();
            if (!string.IsNullOrWhiteSpace(status)
                && status.IndexOf("success", StringComparison.OrdinalIgnoreCase) < 0
                && !string.Equals(status, "true", StringComparison.OrdinalIgnoreCase)
                && status != "200")
            {
                throw new PaymentGatewayApiException(
                    GatewayName,
                    (int)response.StatusCode,
                    $"Fawaterak invoiceInitPay rejected: {Truncate(body, 400)}");
            }
        }
        var data = root.TryGetProperty("data", out var nested) ? nested : root;
        var invoiceId = ReadString(data, "invoice_id")
            ?? ReadString(data, "invoiceId")
            ?? throw new InvalidOperationException("Fawaterak response missing invoice_id.");
        var invoiceKey = ReadString(data, "invoice_key") ?? ReadString(data, "invoiceKey") ?? invoiceId;
        var paymentData = data.TryGetProperty("payment_data", out var pd) ? pd : data;
        var iframeUrl = ReadString(paymentData, "iframeURL")
            ?? ReadString(paymentData, "iframeUrl")
            ?? ReadString(paymentData, "iframe")
            ?? ReadString(paymentData, "redirectTo")
            ?? ReadString(paymentData, "payment_url")
            ?? ReadString(data, "url");
        if (string.IsNullOrWhiteSpace(iframeUrl))
        {
            iframeUrl = $"{opts.ApiBaseUrl.TrimEnd('/')}/invoice/{Uri.EscapeDataString(invoiceKey)}";
        }

        return new PaymentIntentResult(
            GatewayTransactionId: invoiceId,
            ClientSecret: invoiceKey,
            Status: "open",
            CheckoutUrl: iframeUrl);
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Fawaterak;
        if (string.IsNullOrWhiteSpace(opts.HashApiKey))
        {
            return new WebhookProcessResult("fawaterak_unconfigured", "signature_missing", false, "Fawaterak HASH key is not configured");
        }

        Dictionary<string, string> fields;
        try
        {
            fields = FlattenPayload(payload);
        }
        catch (JsonException)
        {
            return new WebhookProcessResult("fawaterak_bad_payload", "payload_invalid", false, "Callback payload was not valid JSON");
        }

        var invoiceId = First(fields, "invoice_id", "invoiceId", "invoiceid") ?? string.Empty;
        var invoiceKey = First(fields, "invoice_key", "invoiceKey", "invoicekey") ?? string.Empty;
        var providedHash = First(fields, "hashKey", "hash_key", "hash", "hmac") ?? string.Empty;
        var statusRaw = First(fields, "invoice_status", "payment_status", "status", "paid") ?? string.Empty;
        var quoteId = First(fields, "payLoad", "payload", "pay_load", "order_id", "quote_id") ?? invoiceId;

        if (!PaymentCallbackHmac.FawaterakHashMatches(opts.HashApiKey, invoiceId, invoiceKey, providedHash))
        {
            return new WebhookProcessResult("fawaterak_bad_sig", "signature_invalid", false, "HASH mismatch");
        }

        var succeeded = statusRaw.Contains("paid", StringComparison.OrdinalIgnoreCase)
            || statusRaw.Contains("success", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusRaw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusRaw, "1", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(statusRaw);

        var eventId = string.IsNullOrWhiteSpace(invoiceId) ? Guid.NewGuid().ToString("N") : invoiceId;
        return new WebhookProcessResult(
            EventId: eventId,
            EventType: succeeded ? "payment.succeeded" : "payment.pending",
            Processed: true,
            Error: null,
            GatewayTransactionId: string.IsNullOrWhiteSpace(invoiceId) ? quoteId : invoiceId,
            NormalizedStatus: succeeded ? "completed" : "pending",
            SafePayloadJson: JsonSerializer.Serialize(new
            {
                invoiceId,
                quoteId,
                status = statusRaw,
            }),
            EventCategory: PaymentWebhookCategories.Payment,
            GatewayObjectId: invoiceId);
    }

    public Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        if (_billing.Value.AllowSandboxFallbacks)
        {
            return Task.FromResult(new RefundResult($"fawaterak_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount));
        }

        throw new InvalidOperationException("Fawaterak refunds are processed from the Fawaterak dashboard.");
    }

    private static Dictionary<string, string> FlattenPayload(string payload)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return fields;
        }

        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText().Trim('"');
        }

        return fields;
    }

    private static string? First(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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
                $"Fawaterak does not accept {currency} and FX conversion is unavailable.");
        }

        var converted = decimal.Round(await _fx.ConvertAsync(amount, currency, "USD", ct), 2, MidpointRounding.AwayFromZero);
        if (converted <= 0)
        {
            throw new PaymentGatewayApiException(
                GatewayName,
                422,
                $"Fawaterak FX conversion produced an invalid {currency}->USD amount.");
        }

        return (converted, "USD");
    }

    private static string? StripStripeSessionPlaceholder(string? url)
        => url?.Replace("{CHECKOUT_SESSION_ID}", string.Empty, StringComparison.Ordinal);

    private static Uri Combine(string baseUrl, string path)
        => new(new Uri(EnsureTrailingSlash(baseUrl)), path);

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

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
}
