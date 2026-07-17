using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Billing.Gateways;

/// <summary>
/// Paymob adapter (Egypt). Cards, Meeza, Fawry (cash voucher), Vodafone/Orange/Etisalat
/// Cash, Aman. Two-phase flow: auth → order → payment-key → iframe URL.
/// Webhook HMAC-SHA512.
///
/// Reads credentials from <see cref="IRuntimeSettingsProvider"/> (admin-UI DB
/// overrides with env fallback); webhook verification uses the same effective
/// HMAC secret as payment creation.
/// </summary>
public sealed class PaymobGateway : IPaymentGateway
{
    public string GatewayName => "paymob";

    private readonly HttpClient _http;
    private readonly IOptions<BillingOptions> _billing;
    private readonly IRuntimeSettingsProvider _runtimeSettings;

    public PaymobGateway(HttpClient http, IOptions<BillingOptions> billing, IRuntimeSettingsProvider runtimeSettings)
    {
        _http = http;
        _billing = billing;
        _runtimeSettings = runtimeSettings;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Paymob;
        var billing = _billing.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey) || string.IsNullOrWhiteSpace(opts.MerchantId))
        {
            if (billing.AllowSandboxFallbacks)
            {
                return new PaymentIntentResult(
                    GatewayTransactionId: $"paymob_sandbox_{Guid.NewGuid():N}",
                    ClientSecret: string.Empty,
                    Status: "open",
                    CheckoutUrl: $"{billing.CheckoutBaseUrl?.TrimEnd('/')}/sandbox/paymob?ref={request.ProductId}");
            }
            throw new InvalidOperationException("Paymob is not configured and sandbox fallbacks are disabled.");
        }

        // Method selection: cart hint via metadata["paymob_method"] (card | fawry | vodafone_cash | aman). Default = card.
        var method = (request.Metadata != null && request.Metadata.TryGetValue("paymob_method", out var m)) ? m : "card";
        if (!opts.IntegrationIds.TryGetValue(method, out var integrationId))
        {
            throw new InvalidOperationException($"Paymob integration id not configured for method '{method}'.");
        }

        // 1) Auth token.
        using (var authMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/auth/tokens")))
        {
            authMsg.Content = JsonContent.Create(new { api_key = opts.ApiKey });
            using var authResp = await _http.SendAsync(authMsg, ct);
            authResp.EnsureSuccessStatusCode();
            using var authJson = JsonDocument.Parse(await authResp.Content.ReadAsStringAsync(ct));
            var authToken = authJson.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Paymob auth token missing in response.");

            // 2) Order.
            int amountCents = (int)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);
            using var orderMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/ecommerce/orders"));
            orderMsg.Content = JsonContent.Create(new
            {
                auth_token = authToken,
                delivery_needed = false,
                amount_cents = amountCents.ToString(CultureInfo.InvariantCulture),
                currency = request.Currency.ToUpperInvariant(),
                merchant_order_id = request.ProductId,
                items = Array.Empty<object>(),
            });
            using var orderResp = await _http.SendAsync(orderMsg, ct);
            orderResp.EnsureSuccessStatusCode();
            using var orderJson = JsonDocument.Parse(await orderResp.Content.ReadAsStringAsync(ct));
            var orderId = orderJson.RootElement.GetProperty("id").GetRawText();

            // 3) Payment key.
            using var keyMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/acceptance/payment_keys"));
            keyMsg.Content = JsonContent.Create(new
            {
                auth_token = authToken,
                amount_cents = amountCents,
                currency = request.Currency.ToUpperInvariant(),
                order_id = orderId,
                integration_id = integrationId,
                billing_data = new
                {
                    first_name = "Customer",
                    last_name = request.UserId,
                    email = request.Metadata != null && request.Metadata.TryGetValue("email", out var em) ? em : "noreply@example.com",
                    phone_number = "+201000000000",
                    country = "EG",
                    apartment = "NA", floor = "NA", street = "NA", building = "NA", shipping_method = "NA",
                    postal_code = "NA", city = "NA", state = "NA",
                },
                expiration = 3600,
            });
            using var keyResp = await _http.SendAsync(keyMsg, ct);
            keyResp.EnsureSuccessStatusCode();
            using var keyJson = JsonDocument.Parse(await keyResp.Content.ReadAsStringAsync(ct));
            var paymentKey = keyJson.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Paymob payment key missing in response.");

            var iframeUrl = $"{opts.ApiBaseUrl.TrimEnd('/')}/api/acceptance/iframes/{opts.IframeId}?payment_token={paymentKey}";

            return new PaymentIntentResult(
                GatewayTransactionId: orderId,
                ClientSecret: paymentKey,
                Status: method is "fawry" or "aman" ? "pending_offline" : "open",
                CheckoutUrl: iframeUrl);
        }
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Paymob;
        if (string.IsNullOrWhiteSpace(opts.HmacSecret))
        {
            return new WebhookProcessResult("paymob_unconfigured", "signature_missing", false, "Paymob HMAC secret not configured");
        }

        if (!headers.TryGetValue("HMAC", out var signature) && !headers.TryGetValue("X-Paymob-HMAC", out signature))
        {
            return new WebhookProcessResult("paymob_no_sig", "signature_missing", false, "Missing HMAC header");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // Paymob HMAC formula: SHA512(concat(selected_fields)). For correctness we
        // compute over the canonical payload — production must mirror Paymob's
        // documented field-list ordering.
        var concatenatedFields = ExtractPaymobHashableFields(root);
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(opts.HmacSecret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(concatenatedFields))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature.ToLowerInvariant())))
        {
            return new WebhookProcessResult("paymob_bad_sig", "signature_invalid", false, "Signature mismatch");
        }

        var orderId = root.TryGetProperty("obj", out var obj) && obj.TryGetProperty("order", out var order)
            ? order.TryGetProperty("id", out var oid) ? oid.GetRawText() : null
            : null;
        var success = root.TryGetProperty("obj", out var obj2) && obj2.TryGetProperty("success", out var s) && s.GetBoolean();

        return new WebhookProcessResult(
            EventId: orderId ?? Guid.NewGuid().ToString("N"),
            EventType: success ? "payment.succeeded" : "payment.failed",
            Processed: true,
            Error: null,
            GatewayTransactionId: orderId,
            NormalizedStatus: success ? "succeeded" : "failed",
            SafePayloadJson: payload,
            EventCategory: PaymentWebhookCategories.Payment,
            GatewayObjectId: orderId);
    }

    public async Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Paymob;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            if (_billing.Value.AllowSandboxFallbacks)
            {
                return new RefundResult($"paymob_refund_sandbox_{Guid.NewGuid():N}", "succeeded", amount);
            }
            throw new InvalidOperationException("Paymob refunds not configured.");
        }

        // 1) Auth token (refunds require a fresh token).
        string authToken;
        using (var authMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/auth/tokens")))
        {
            authMsg.Content = JsonContent.Create(new { api_key = opts.ApiKey });
            using var authResp = await _http.SendAsync(authMsg, ct);
            authResp.EnsureSuccessStatusCode();
            using var authJson = JsonDocument.Parse(await authResp.Content.ReadAsStringAsync(ct));
            authToken = authJson.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Paymob auth token missing.");
        }

        // 2) Refund the transaction. Paymob: POST /api/acceptance/void_refund/refund with body
        //    { auth_token, transaction_id, amount_cents }.
        int amountCents = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        using var refundMsg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(EnsureTrailingSlash(opts.ApiBaseUrl)), "api/acceptance/void_refund/refund"));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            refundMsg.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        refundMsg.Content = JsonContent.Create(new
        {
            auth_token = authToken,
            transaction_id = transactionId,
            amount_cents = amountCents,
        });

        using var refundResp = await _http.SendAsync(refundMsg, ct);
        if (!refundResp.IsSuccessStatusCode)
        {
            var body = await refundResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Paymob refund failed: {(int)refundResp.StatusCode} {body}");
        }

        await using var stream = await refundResp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var refundId = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : Guid.NewGuid().ToString("N");
        var success = root.TryGetProperty("success", out var sEl) && sEl.ValueKind == JsonValueKind.True;
        return new RefundResult(refundId.Trim('"'), success ? "succeeded" : "pending", amount);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private static string ExtractPaymobHashableFields(JsonElement root)
    {
        // Concatenate the fields Paymob lists in their HMAC docs (amount_cents, created_at, currency,
        // error_occured, has_parent_transaction, id, integration_id, is_3d_secure, is_auth, is_capture,
        // is_refunded, is_standalone_payment, is_voided, order.id, owner, pending, source_data.pan,
        // source_data.sub_type, source_data.type, success).
        var sb = new StringBuilder();
        void Append(JsonElement el)
        {
            sb.Append(el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => el.ToString(),
            });
        }

        if (!root.TryGetProperty("obj", out var obj)) return string.Empty;
        string[] flat = { "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id",
                          "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded",
                          "is_standalone_payment", "is_voided" };
        foreach (var f in flat)
        {
            if (obj.TryGetProperty(f, out var v)) Append(v);
        }
        if (obj.TryGetProperty("order", out var order) && order.TryGetProperty("id", out var oid)) Append(oid);
        if (obj.TryGetProperty("owner", out var owner)) Append(owner);
        if (obj.TryGetProperty("pending", out var pending)) Append(pending);
        if (obj.TryGetProperty("source_data", out var src))
        {
            foreach (var f in new[] { "pan", "sub_type", "type" })
            {
                if (src.TryGetProperty(f, out var v)) Append(v);
            }
        }
        if (obj.TryGetProperty("success", out var success)) Append(success);
        return sb.ToString();
    }
}
