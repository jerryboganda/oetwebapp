using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using static OetLearner.Api.Services.PaymentGatewayJson;

namespace OetLearner.Api.Services;

/// <summary>
/// Abstraction for hosted payment gateway operations supporting Stripe and PayPal.
/// </summary>
public interface IPaymentGateway
{
    string GatewayName { get; }
    Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct);
    Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
    Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string reason, CancellationToken ct);
}

public record CreatePaymentIntentRequest(
    string UserId,
    decimal Amount,
    string Currency,
    string ProductType,
    string? ProductId,
    string? Description,
    Dictionary<string, string>? Metadata,
    string? SuccessUrl = null,
    string? CancelUrl = null);

public record PaymentIntentResult(
    string GatewayTransactionId,
    string ClientSecret,
    string Status,
    string CheckoutUrl);

public record WebhookProcessResult(
    string EventId,
    string EventType,
    bool Processed,
    string? Error,
    string? GatewayTransactionId = null,
    string? NormalizedStatus = null);

public record RefundResult(
    string RefundId,
    string Status,
    decimal AmountRefunded);

/// <summary>
/// Stripe payment gateway adapter. Uses the hosted checkout session API when credentials are configured
/// and falls back to a sandbox-style response for local development.
/// </summary>
public sealed class StripeGateway(HttpClient httpClient, IOptions<BillingOptions> billingOptions) : IPaymentGateway
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly BillingOptions _billing = billingOptions.Value;

    public string GatewayName => "stripe";

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var options = _billing.Stripe;
        var successUrl = request.SuccessUrl ?? options.SuccessUrl;
        var cancelUrl = request.CancelUrl ?? options.CancelUrl;
        if (string.IsNullOrWhiteSpace(options.SecretKey)
            || string.IsNullOrWhiteSpace(successUrl)
            || string.IsNullOrWhiteSpace(cancelUrl))
        {
            return BuildSandboxCheckout(request);
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(options.ApiBaseUrl)), "v1/checkout/sessions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);

        var form = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = request.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][unit_amount]"] = ConvertToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"] = request.Description ?? $"{request.ProductType} purchase",
        };

        if (!string.IsNullOrWhiteSpace(request.ProductId))
        {
            form["client_reference_id"] = request.ProductId;
        }

        foreach (var (key, value) in request.Metadata ?? [])
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                form[$"metadata[{key}]"] = value;
            }
        }

        message.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        return new PaymentIntentResult(
            GatewayTransactionId: GetString(root, "id") ?? $"cs_local_{Guid.NewGuid():N}",
            ClientSecret: GetString(root, "payment_intent") ?? GetString(root, "id") ?? string.Empty,
            Status: GetString(root, "status") ?? "open",
            CheckoutUrl: GetString(root, "url") ?? string.Empty);
    }

    Task<RefundResult> IPaymentGateway.ProcessRefundAsync(string transactionId, decimal amount, string reason, CancellationToken ct)
        => Task.FromResult(new RefundResult(
            RefundId: $"re_local_{Guid.NewGuid():N}",
            Status: "pending",
            AmountRefunded: amount));

    public Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        if (!VerifyStripeWebhook(payload, headers, out var verificationError))
        {
            return Task.FromResult(new WebhookProcessResult(
                EventId: $"stripe-invalid-{Guid.NewGuid():N}",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: verificationError));
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventId = GetString(root, "id") ?? $"stripe-{Guid.NewGuid():N}";
        var eventType = GetString(root, "type") ?? "unknown";
        var dataObject = root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("object", out var objectElement)
            ? objectElement
            : default;

        var transactionId = eventType switch
        {
            "checkout.session.completed" => GetString(dataObject, "id"),
            "checkout.session.expired" => GetString(dataObject, "id"),
            "checkout.session.async_payment_failed" => GetString(dataObject, "id"),
            "checkout.session.async_payment_succeeded" => GetString(dataObject, "id"),
            _ => GetString(dataObject, "id")
        };

        var normalizedStatus = eventType switch
        {
            "checkout.session.completed" when string.Equals(GetString(dataObject, "payment_status"), "paid", StringComparison.OrdinalIgnoreCase) => "completed",
            "checkout.session.async_payment_succeeded" => "completed",
            "checkout.session.expired" => "failed",
            "checkout.session.async_payment_failed" => "failed",
            _ => "pending"
        };

        return Task.FromResult(new WebhookProcessResult(
            EventId: eventId,
            EventType: eventType,
            Processed: true,
            Error: null,
            GatewayTransactionId: transactionId,
            NormalizedStatus: normalizedStatus));
    }

    private PaymentIntentResult BuildSandboxCheckout(CreatePaymentIntentRequest request)
    {
        var sessionId = $"cs_local_{Guid.NewGuid():N}";
        return new PaymentIntentResult(
            GatewayTransactionId: sessionId,
            ClientSecret: sessionId,
            Status: "sandbox_pending",
            CheckoutUrl: $"https://checkout.stripe.com/pay/{sessionId}");
    }

    private bool VerifyStripeWebhook(string payload, IReadOnlyDictionary<string, string> headers, out string? error)
    {
        var secret = _billing.Stripe.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            error = _billing.AllowSandboxFallbacks ? null : "Stripe webhook secret is not configured.";
            return _billing.AllowSandboxFallbacks;
        }

        if (!TryGetHeader(headers, "Stripe-Signature", out var signatureHeader) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            error = "Stripe-Signature header is missing.";
            return false;
        }

        var pairs = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var timestamp = pairs
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0] == "t")?[1];
        var signatures = pairs
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "v1")
            .Select(parts => parts[1])
            .ToList();

        if (string.IsNullOrWhiteSpace(timestamp) || signatures.Count == 0)
        {
            error = "Stripe signature header is malformed.";
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        var matched = signatures.Any(signature =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expected)));

        error = matched ? null : "Stripe signature verification failed.";
        return matched;
    }
}

/// <summary>
/// PayPal payment gateway adapter. Creates hosted orders when credentials are configured and falls back to
/// sandbox-style responses when running locally without provider credentials.
/// </summary>
public sealed class PayPalGateway(HttpClient httpClient, IOptions<BillingOptions> billingOptions) : IPaymentGateway
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly BillingOptions _billing = billingOptions.Value;

    public string GatewayName => "paypal";

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var options = _billing.PayPal;
        var successUrl = request.SuccessUrl ?? options.SuccessUrl;
        var cancelUrl = request.CancelUrl ?? options.CancelUrl;
        if (string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret)
            || string.IsNullOrWhiteSpace(successUrl)
            || string.IsNullOrWhiteSpace(cancelUrl))
        {
            return BuildSandboxOrder(request);
        }

        var accessToken = await GetAccessTokenAsync(ct);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), "v2/checkout/orders"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = request.ProductType,
                    custom_id = request.ProductId ?? request.UserId,
                    description = request.Description ?? $"{request.ProductType} purchase",
                    amount = new
                    {
                        currency_code = request.Currency.ToUpperInvariant(),
                        value = request.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                }
            },
            application_context = new
            {
                return_url = successUrl,
                cancel_url = cancelUrl,
                brand_name = "OET Prep",
                shipping_preference = "NO_SHIPPING",
                user_action = "PAY_NOW"
            }
        };

        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var orderId = GetString(root, "id") ?? $"PAYPAL-{Guid.NewGuid():N}";
        var checkoutUrl = FindLink(root, "approve") ?? FindLink(root, "payer-action") ?? string.Empty;

        return new PaymentIntentResult(
            GatewayTransactionId: orderId,
            ClientSecret: orderId,
            Status: GetString(root, "status") ?? "CREATED",
            CheckoutUrl: checkoutUrl);
    }

    Task<RefundResult> IPaymentGateway.ProcessRefundAsync(string transactionId, decimal amount, string reason, CancellationToken ct)
        => Task.FromResult(new RefundResult(
            RefundId: $"PAYPAL-REFUND-{Guid.NewGuid():N}",
            Status: "pending",
            AmountRefunded: amount));

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        if (!await VerifyWebhookAsync(payload, headers, ct))
        {
            return new WebhookProcessResult(
                EventId: $"paypal-invalid-{Guid.NewGuid():N}",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: "PayPal webhook verification failed.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventId = GetString(root, "id") ?? $"paypal-{Guid.NewGuid():N}";
        var eventType = GetString(root, "event_type") ?? "unknown";
        var resource = root.TryGetProperty("resource", out var resourceElement) ? resourceElement : default;
        var relatedIds = resource.TryGetProperty("supplementary_data", out var supplementary)
            && supplementary.TryGetProperty("related_ids", out var related)
            ? related
            : default;

        var transactionId = GetString(relatedIds, "order_id")
            ?? GetString(resource, "id");

        var normalizedStatus = eventType switch
        {
            "PAYMENT.CAPTURE.COMPLETED" => "completed",
            "CHECKOUT.ORDER.APPROVED" => "completed",
            "PAYMENT.CAPTURE.DENIED" => "failed",
            "PAYMENT.CAPTURE.DECLINED" => "failed",
            "CHECKOUT.ORDER.CANCELLED" => "failed",
            _ => "pending"
        };

        return new WebhookProcessResult(
            EventId: eventId,
            EventType: eventType,
            Processed: true,
            Error: null,
            GatewayTransactionId: transactionId,
            NormalizedStatus: normalizedStatus);
    }

    private PaymentIntentResult BuildSandboxOrder(CreatePaymentIntentRequest request)
    {
        var orderId = $"PAYPAL-{Guid.NewGuid():N}";
        return new PaymentIntentResult(
            GatewayTransactionId: orderId,
            ClientSecret: orderId,
            Status: "sandbox_pending",
            CheckoutUrl: $"https://www.sandbox.paypal.com/checkoutnow?token={orderId}");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var options = _billing.PayPal;
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), "v1/oauth2/token"));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return GetString(document.RootElement, "access_token")
            ?? throw new InvalidOperationException("PayPal access token response did not contain an access token.");
    }

    private async Task<bool> VerifyWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var options = _billing.PayPal;
        if (string.IsNullOrWhiteSpace(options.WebhookId)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            return _billing.AllowSandboxFallbacks;
        }

        if (!TryGetHeader(headers, "paypal-transmission-id", out var transmissionId)
            || !TryGetHeader(headers, "paypal-transmission-time", out var transmissionTime)
            || !TryGetHeader(headers, "paypal-transmission-sig", out var transmissionSig)
            || !TryGetHeader(headers, "paypal-cert-url", out var certUrl)
            || !TryGetHeader(headers, "paypal-auth-algo", out var authAlgo))
        {
            return false;
        }

        var accessToken = await GetAccessTokenAsync(ct);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), "v1/notifications/verify-webhook-signature"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var payloadDocument = JsonDocument.Parse(payload);
        var body = new
        {
            auth_algo = authAlgo,
            cert_url = certUrl,
            transmission_id = transmissionId,
            transmission_sig = transmissionSig,
            transmission_time = transmissionTime,
            webhook_id = options.WebhookId,
            webhook_event = JsonSerializer.Deserialize<object>(payloadDocument.RootElement.GetRawText())
        };
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return string.Equals(GetString(document.RootElement, "verification_status"), "SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPayPalApiBaseUrl(PayPalBillingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl)
            && !string.Equals(options.ApiBaseUrl, "https://api-m.paypal.com", StringComparison.OrdinalIgnoreCase))
        {
            return options.ApiBaseUrl;
        }

        return options.UseSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";
    }
}

/// <summary>
/// Unified payment gateway service that routes to Stripe or PayPal based on gateway name.
/// Registered as a scoped service to keep gateway configuration and HTTP clients aligned with request scope.
/// </summary>
public sealed class PaymentGatewayService
{
    private readonly Dictionary<string, IPaymentGateway> _gateways;

    public PaymentGatewayService(StripeGateway stripe, PayPalGateway paypal)
    {
        _gateways = new Dictionary<string, IPaymentGateway>(StringComparer.OrdinalIgnoreCase)
        {
            ["stripe"] = stripe,
            ["paypal"] = paypal
        };
    }

    public IPaymentGateway GetGateway(string name)
    {
        if (_gateways.TryGetValue(name, out var gateway))
        {
            return gateway;
        }

        throw new ArgumentException($"Unsupported payment gateway: '{name}'. Supported: stripe, paypal.");
    }

    public IReadOnlyList<string> SupportedGateways => _gateways.Keys.ToList().AsReadOnly();
}

internal static class PaymentGatewayJson
{
    public static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static string? FindLink(JsonElement element, string relation)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (string.Equals(GetString(link, "rel"), relation, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(link, "href");
            }
        }

        return null;
    }

    public static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
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

    public static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : $"{url}/";

    public static long ConvertToMinorUnits(decimal amount) => decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
