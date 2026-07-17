using System.Globalization;
using System.Linq;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Billing.Gateways;

/// <summary>
/// EasyKash adapter (Egypt hosted Direct-Pay). Single-call flow:
/// POST /api/directpayv1/pay returns a hosted redirect URL; the buyer pays on
/// EasyKash's page; a server-to-server callback (verified here via HMAC-SHA512)
/// confirms the payment. Correlation is by <c>ProductCode</c> (the last segment
/// of the returned pay URL, echoed back in the callback) — stored as the
/// <see cref="PaymentIntentResult.GatewayTransactionId"/> so the shared
/// webhook-fulfilment path matches on it, exactly like Paymob's order id.
///
/// Reads credentials from <see cref="IRuntimeSettingsProvider"/> (admin-UI DB
/// overrides with env fallback); webhook verification uses the same effective
/// HMAC secret as payment creation. Amount is charged in the quote currency, or
/// FX-converted to EGP when the admin selects the "egp" currency mode.
/// </summary>
public sealed class EasyKashGateway : IPaymentGateway
{
    public string GatewayName => "easykash";

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IFxRateService? _fx;
    private readonly ILogger<EasyKashGateway>? _logger;

    // fx + logger are optional so lightweight tests can construct the gateway with
    // just (http, billing, runtimeSettings); DI always supplies the real services.
    public EasyKashGateway(
        HttpClient http,
        IOptions<BillingOptions> billing,
        IRuntimeSettingsProvider runtimeSettings,
        IFxRateService? fx = null,
        ILogger<EasyKashGateway>? logger = null)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
        _fx = fx;
        _logger = logger;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).EasyKash;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            if (billing.AllowSandboxFallbacks)
            {
                return new PaymentIntentResult(
                    GatewayTransactionId: $"easykash_sandbox_{Guid.NewGuid():N}",
                    ClientSecret: string.Empty,
                    Status: "pending",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/easykash?ref={request.ProductId}");
            }
            throw new InvalidOperationException("EasyKash is not configured and sandbox fallbacks are disabled.");
        }

        // Currency mode: charge the quote currency as-is, or FX-convert to EGP.
        decimal amount = request.Amount;
        string currency = request.Currency.ToUpperInvariant();
        if (opts.ConvertToEgp && _fx is not null && currency != "EGP")
        {
            amount = Math.Round(await _fx.ConvertAsync(request.Amount, currency, "EGP", ct), 2, MidpointRounding.AwayFromZero);
            currency = "EGP";
        }

        // Buyer return URL. Strip Stripe's {CHECKOUT_SESSION_ID} token (the shared
        // checkout URL builder embeds it for Stripe; EasyKash would carry it literally).
        var redirectUrl = StripStripeSessionPlaceholder(request.SuccessUrl)
            ?? opts.SuccessUrl
            ?? $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/billing/payment-return?gateway=easykash";

        var email = MetadataValue(request.Metadata, "email") ?? "noreply@oetwithdrhesham.co.uk";
        var name = MetadataValue(request.Metadata, "name") ?? "OET with Dr Hesham";
        var mobile = MetadataValue(request.Metadata, "mobile") ?? "01000000000";

        var body = new Dictionary<string, object?>
        {
            ["amount"] = amount,
            ["currency"] = currency,
            ["name"] = name,
            ["email"] = email,
            ["mobile"] = mobile,
            ["redirectUrl"] = redirectUrl,
            // EasyKash requires a numeric customerReference; correlation is by
            // ProductCode, so this is a benign numeric surrogate.
            ["customerReference"] = DeriveNumericReference(request.ProductId ?? request.IdempotencyKey ?? request.UserId),
        };
        if (opts.PaymentOptions.Count > 0)
        {
            body["paymentOptions"] = opts.PaymentOptions;
        }

        using var payMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/directpayv1/pay"));
        payMsg.Headers.TryAddWithoutValidation("authorization", opts.ApiKey);
        payMsg.Content = JsonContent.Create(body);

        using var resp = await _http.SendAsync(payMsg, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            _logger?.LogWarning("EasyKash pay request failed with status {Status}", (int)resp.StatusCode);
            throw new PaymentGatewayApiException("easykash", (int)resp.StatusCode,
                $"EasyKash pay request failed: {(int)resp.StatusCode} {Truncate(errBody, 500)}");
        }

        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var payUrl = json.RootElement.TryGetProperty("redirectUrl", out var ru) ? ru.GetString() : null;
        if (string.IsNullOrWhiteSpace(payUrl))
        {
            throw new InvalidOperationException("EasyKash response did not contain a redirectUrl.");
        }

        var productCode = ExtractProductCode(payUrl);

        return new PaymentIntentResult(
            GatewayTransactionId: productCode,
            ClientSecret: string.Empty,
            Status: "pending",
            CheckoutUrl: payUrl);
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).EasyKash;
        if (string.IsNullOrWhiteSpace(opts.HmacSecret))
        {
            return new WebhookProcessResult("easykash_unconfigured", "signature_missing", false, "EasyKash HMAC secret not configured");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        }
        catch (JsonException)
        {
            return new WebhookProcessResult("easykash_bad_payload", "payload_invalid", false, "Callback payload was not valid JSON");
        }

        using (doc)
        {
            var root = doc.RootElement;
            string Field(string name) => PaymentGatewayJson.GetString(root, name) ?? string.Empty;

            var productCode = Field("ProductCode");
            var amount = Field("Amount");
            var productType = Field("ProductType");
            var paymentMethod = Field("PaymentMethod");
            var status = Field("status");
            var easykashRef = Field("easykashRef");
            var customerReference = Field("customerReference");
            var signatureHash = Field("signatureHash");

            if (string.IsNullOrEmpty(signatureHash))
            {
                return new WebhookProcessResult("easykash_no_sig", "signature_missing", false, "Missing signatureHash");
            }

            // HMAC-SHA512 over the documented field concatenation (no separators).
            var concatenated = productCode + amount + productType + paymentMethod + status + easykashRef + customerReference;
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(opts.HmacSecret));
            var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(concatenated))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computed),
                    Encoding.UTF8.GetBytes(signatureHash.ToLowerInvariant())))
            {
                return new WebhookProcessResult("easykash_bad_sig", "signature_invalid", false, "Signature mismatch");
            }

            var paid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase);

            // Persist only non-PII fields (drop Buyer name/email/mobile + voucher).
            var safePayload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["ProductCode"] = productCode,
                ["Amount"] = amount,
                ["ProductType"] = productType,
                ["PaymentMethod"] = paymentMethod,
                ["status"] = status,
                ["easykashRef"] = easykashRef,
                ["customerReference"] = customerReference,
            });

            return new WebhookProcessResult(
                EventId: string.IsNullOrEmpty(easykashRef) ? (string.IsNullOrEmpty(productCode) ? Guid.NewGuid().ToString("N") : productCode) : easykashRef,
                EventType: paid ? "payment.paid" : "payment.failed",
                Processed: true,
                Error: null,
                GatewayTransactionId: productCode,
                NormalizedStatus: paid ? "completed" : "failed",
                SafePayloadJson: safePayload,
                EventCategory: PaymentWebhookCategories.Payment,
                GatewayObjectId: easykashRef);
        }
    }

    public Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        if (_billing.Value.AllowSandboxFallbacks)
        {
            return Task.FromResult(new RefundResult($"easykash_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount));
        }

        // EasyKash Direct-Pay exposes no programmatic refund API; refunds are
        // issued from the EasyKash merchant dashboard.
        throw new InvalidOperationException("EasyKash refunds are processed manually from the EasyKash dashboard.");
    }

    private static string? MetadataValue(IReadOnlyDictionary<string, string>? metadata, string key)
        => metadata != null && metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string? StripStripeSessionPlaceholder(string? url)
        => url?.Replace("{CHECKOUT_SESSION_ID}", string.Empty, StringComparison.Ordinal);

    private static string ExtractProductCode(string payUrl)
    {
        if (Uri.TryCreate(payUrl, UriKind.Absolute, out var uri))
        {
            var last = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(last)) return last;
        }
        // Fallback: last non-empty path chunk of the raw string.
        var trimmed = payUrl.Split('?', '#')[0].TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static long DeriveNumericReference(string seed)
    {
        // Stable, non-negative 9-digit numeric reference derived from the seed.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var value = BitConverter.ToUInt64(hash, 0);
        return (long)(value % 1_000_000_000UL);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
