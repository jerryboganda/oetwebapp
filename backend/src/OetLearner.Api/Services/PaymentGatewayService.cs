using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;
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
    Task<RefundResult> ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Synchronously captures a previously-created order, server-side. Used by PayPal
    /// Expanded (embedded) checkout where the browser SDK approves an order and we must
    /// capture + fulfil in the same request rather than waiting on a webhook. Redirect /
    /// hosted gateways that fulfil purely via webhook do not support this and throw.
    /// </summary>
    Task<CaptureResult> CaptureOrderAsync(string orderId, string idempotencyKey, CancellationToken ct)
        => throw new NotSupportedException($"Gateway '{GatewayName}' does not support server-side order capture.");
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
    string? CancelUrl = null,
    string? IdempotencyKey = null);

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
    string? NormalizedStatus = null,
    string? SafePayloadJson = null,
    string? EventCategory = null,
    string? GatewayObjectId = null);

/// <summary>Categorises a webhook event for routing into refund / dispute pipelines.</summary>
public static class PaymentWebhookCategories
{
    public const string Payment = "payment";
    public const string Refund = "refund";
    public const string Dispute = "dispute";
    public const string Other = "other";
}

public record RefundResult(
    string RefundId,
    string Status,
    decimal AmountRefunded);

/// <summary>
/// Result of a synchronous server-side order capture (PayPal Expanded checkout).
/// <see cref="Status"/> is normalised to lowercase ("completed" on success).
/// </summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal AmountCaptured,
    string Currency);

/// <summary>
/// Raised when a payment gateway's HTTP API rejects a request (non-2xx response).
/// Carries the upstream status code plus the gateway's machine-readable error code/type
/// so callers can translate it into a clean, actionable API error instead of letting a
/// raw <see cref="HttpRequestException"/> bubble up as an opaque 500. The
/// <see cref="Exception.Message"/> is safe to log but MUST NOT be surfaced verbatim to
/// end users (it may contain provider internals).
/// </summary>
public sealed class PaymentGatewayApiException : Exception
{
    public PaymentGatewayApiException(
        string gateway,
        int upstreamStatusCode,
        string message,
        string? upstreamErrorCode = null,
        string? upstreamErrorType = null)
        : base(message)
    {
        Gateway = gateway;
        UpstreamStatusCode = upstreamStatusCode;
        UpstreamErrorCode = upstreamErrorCode;
        UpstreamErrorType = upstreamErrorType;
    }

    public string Gateway { get; }
    public int UpstreamStatusCode { get; }
    public string? UpstreamErrorCode { get; }
    public string? UpstreamErrorType { get; }
}

/// <summary>
/// Stripe payment gateway adapter. Uses the hosted checkout session API when credentials are configured
/// and falls back to a sandbox-style response for local development.
/// </summary>
public sealed class StripeGateway(HttpClient httpClient, IOptions<BillingOptions> billingOptions, IRuntimeSettingsProvider runtimeSettings, ILogger<StripeGateway> logger) : IPaymentGateway
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IOptions<BillingOptions> _billingOptions = billingOptions;
    private readonly IRuntimeSettingsProvider _runtimeSettings = runtimeSettings;
    private readonly ILogger<StripeGateway> _logger = logger;

    public string GatewayName => "stripe";

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var billingOptionsSnapshot = _billingOptions.Value;
        var stripeOptions = billingOptionsSnapshot.Stripe;
        var runtimeBilling = (await _runtimeSettings.GetAsync(ct)).Billing;
        // Promote relative return URLs (when Platform:PublicWebBaseUrl is unset) to absolute
        // using the admin-configured public app base URL, so checkout works with no env vars.
        var successUrl = AbsolutizeReturnUrl(request.SuccessUrl ?? runtimeBilling.StripeSuccessUrl, runtimeBilling.PublicAppBaseUrl);
        var cancelUrl = AbsolutizeReturnUrl(request.CancelUrl ?? runtimeBilling.StripeCancelUrl, runtimeBilling.PublicAppBaseUrl);
        if (string.IsNullOrWhiteSpace(runtimeBilling.StripeSecretKey)
            || string.IsNullOrWhiteSpace(successUrl)
            || string.IsNullOrWhiteSpace(cancelUrl))
        {
            if (billingOptionsSnapshot.AllowSandboxFallbacks)
            {
                return BuildSandboxCheckout(request);
            }

            throw new InvalidOperationException("Stripe billing is not fully configured and sandbox fallbacks are disabled.");
        }

        // Stripe rejects relative success_url/cancel_url with an HTTP 400. That would
        // otherwise surface as an opaque 500. Treat non-absolute URLs (e.g. when no
        // public app/web base URL is configured) as a configuration problem so the
        // caller maps it to a clean gateway_unavailable response.
        if (!Uri.TryCreate(successUrl, UriKind.Absolute, out _) || !Uri.TryCreate(cancelUrl, UriKind.Absolute, out _))
        {
            if (billingOptionsSnapshot.AllowSandboxFallbacks)
            {
                return BuildSandboxCheckout(request);
            }

            throw new InvalidOperationException(
                "Stripe billing is not fully configured: success_url and cancel_url must be absolute URLs. "
                + "Set the billing public app base URL (admin Settings) or Platform:PublicWebBaseUrl.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(stripeOptions.ApiBaseUrl)), "v1/checkout/sessions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtimeBilling.StripeSecretKey);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
        }

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
        await EnsureStripeSuccessAsync(response, "checkout session creation", ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        return new PaymentIntentResult(
            GatewayTransactionId: GetString(root, "id") ?? $"cs_local_{Guid.NewGuid():N}",
            ClientSecret: GetString(root, "payment_intent") ?? GetString(root, "id") ?? string.Empty,
            Status: GetString(root, "status") ?? "open",
            CheckoutUrl: GetString(root, "url") ?? string.Empty);
    }

    async Task<RefundResult> IPaymentGateway.ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        var billingOptionsSnapshot = _billingOptions.Value;
        var stripeOptions = billingOptionsSnapshot.Stripe;
        var runtimeBilling = (await _runtimeSettings.GetAsync(ct)).Billing;
        if (string.IsNullOrWhiteSpace(runtimeBilling.StripeSecretKey))
        {
            if (billingOptionsSnapshot.AllowSandboxFallbacks)
            {
                return new RefundResult($"re_local_{Guid.NewGuid():N}", "succeeded", amount);
            }

            throw new InvalidOperationException("Stripe refunds are not configured and sandbox fallbacks are disabled.");
        }

        var refundableId = transactionId;
        if (transactionId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
        {
            using var sessionRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(EnsureTrailingSlash(stripeOptions.ApiBaseUrl)), $"v1/checkout/sessions/{Uri.EscapeDataString(transactionId)}"));
            sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtimeBilling.StripeSecretKey);
            using var sessionResponse = await _httpClient.SendAsync(sessionRequest, ct);
            await EnsureStripeSuccessAsync(sessionResponse, "refund session lookup", ct);
            await using var sessionStream = await sessionResponse.Content.ReadAsStreamAsync(ct);
            using var sessionDocument = await JsonDocument.ParseAsync(sessionStream, cancellationToken: ct);
            refundableId = GetString(sessionDocument.RootElement, "payment_intent")
                ?? throw new InvalidOperationException("Stripe checkout session does not expose a payment_intent for refunding.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(stripeOptions.ApiBaseUrl)), "v1/refunds"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtimeBilling.StripeSecretKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        var form = new Dictionary<string, string>
        {
            [refundableId.StartsWith("ch_", StringComparison.OrdinalIgnoreCase) ? "charge" : "payment_intent"] = refundableId,
            ["amount"] = ConvertToMinorUnits(amount).ToString(CultureInfo.InvariantCulture)
        };
        var refundReason = NormalizeStripeRefundReason(reason);
        if (refundReason is not null)
        {
            form["reason"] = refundReason;
        }

        message.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(message, ct);
        await EnsureStripeSuccessAsync(response, "refund", ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var status = GetString(root, "status") ?? "pending";
        return new RefundResult(GetString(root, "id") ?? $"re_{Guid.NewGuid():N}", NormalizeRefundStatus(status), amount);
    }

    private static string? NormalizeStripeRefundReason(string? reason)
        => reason?.Trim().ToLowerInvariant() switch
        {
            "duplicate" => "duplicate",
            "fraudulent" => "fraudulent",
            "requested_by_customer" => "requested_by_customer",
            _ => null
        };

    private static string NormalizeRefundStatus(string status)
        => string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase)
            ? "succeeded"
            : status.ToLowerInvariant();

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var runtimeBilling = (await _runtimeSettings.GetAsync(ct)).Billing;
        if (!VerifyStripeWebhook(payload, headers, runtimeBilling.StripeWebhookSecret, out var verificationError))
        {
            return new WebhookProcessResult(
                EventId: $"stripe-invalid-{Guid.NewGuid():N}",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: verificationError,
                SafePayloadJson: "{}",
                EventCategory: PaymentWebhookCategories.Other);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventId = GetString(root, "id") ?? $"stripe-{Guid.NewGuid():N}";
        var eventType = GetString(root, "type") ?? "unknown";
        var dataObject = root.TryGetProperty("data", out var dataElement) && dataElement.TryGetProperty("object", out var objectElement)
            ? objectElement
            : default;

        var category = eventType switch
        {
            "charge.refunded" or "refund.created" or "refund.updated" => PaymentWebhookCategories.Refund,
            "charge.dispute.created" or "charge.dispute.funds_withdrawn" or "charge.dispute.funds_reinstated" or "charge.dispute.closed" or "charge.dispute.updated" => PaymentWebhookCategories.Dispute,
            "checkout.session.completed" or "checkout.session.expired" or "checkout.session.async_payment_failed" or "checkout.session.async_payment_succeeded" => PaymentWebhookCategories.Payment,
            "invoice.paid" or "invoice.payment_succeeded" or "invoice.payment_failed" => PaymentWebhookCategories.Payment,
            _ => PaymentWebhookCategories.Other
        };

        var transactionId = category switch
        {
            PaymentWebhookCategories.Refund => GetString(dataObject, "payment_intent")
                ?? GetString(dataObject, "charge")
                ?? GetString(dataObject, "id"),
            PaymentWebhookCategories.Dispute => GetString(dataObject, "payment_intent")
                ?? GetString(dataObject, "charge")
                ?? GetString(dataObject, "id"),
            _ => GetString(dataObject, "id")
        };

        var normalizedStatus = eventType switch
        {
            "checkout.session.completed" when string.Equals(GetString(dataObject, "payment_status"), "paid", StringComparison.OrdinalIgnoreCase) => "completed",
            "checkout.session.async_payment_succeeded" => "completed",
            "checkout.session.expired" => "failed",
            "checkout.session.async_payment_failed" => "failed",
            "invoice.paid" or "invoice.payment_succeeded" => "completed",
            "invoice.payment_failed" => "failed",
            "charge.refunded" => "refunded",
            "refund.created" => "refund_pending",
            "refund.updated" => GetString(dataObject, "status") ?? "refund_updated",
            "charge.dispute.created" => "dispute_opened",
            "charge.dispute.funds_withdrawn" => "dispute_funds_withdrawn",
            "charge.dispute.funds_reinstated" => "dispute_funds_reinstated",
            "charge.dispute.closed" => string.Equals(GetString(dataObject, "status"), "won", StringComparison.OrdinalIgnoreCase)
                ? "dispute_won"
                : "dispute_lost",
            _ => "pending"
        };

        return new WebhookProcessResult(
            EventId: eventId,
            EventType: eventType,
            Processed: true,
            Error: null,
            GatewayTransactionId: transactionId,
            NormalizedStatus: normalizedStatus,
            SafePayloadJson: PaymentWebhookPiiRedactor.RedactStripe(root),
            EventCategory: category,
            GatewayObjectId: GetString(dataObject, "id"));
    }

    /// <summary>
    /// If <paramref name="url"/> is a relative path (e.g. the caller built it without a
    /// configured <c>Platform:PublicWebBaseUrl</c>) and an absolute
    /// <paramref name="appBaseUrl"/> is available, combine them into an absolute URL.
    /// Already-absolute or null URLs are returned unchanged. The literal Stripe
    /// <c>{CHECKOUT_SESSION_ID}</c> template token is preserved (Uri.ToString does not
    /// percent-encode braces in the query).
    /// </summary>
    private static string? AbsolutizeReturnUrl(string? url, string? appBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url;
        }

        if (string.IsNullOrWhiteSpace(appBaseUrl)
            || !Uri.TryCreate(EnsureTrailingSlash(appBaseUrl), UriKind.Absolute, out var baseUri))
        {
            return url;
        }

        return new Uri(baseUri, url.TrimStart('/')).ToString();
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

    /// <summary>
    /// Replacement for <c>HttpResponseMessage.EnsureSuccessStatusCode()</c> that, on a
    /// non-2xx Stripe response, reads and parses Stripe's <c>{ "error": { ... } }</c>
    /// envelope, logs the detail server-side, and throws a <see cref="PaymentGatewayApiException"/>
    /// carrying the upstream status/code/type. This keeps Stripe's real failure reason out
    /// of the generic 500 path while never leaking it verbatim to the learner.
    /// </summary>
    private async Task EnsureStripeSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        string? stripeMessage = null;
        string? stripeCode = null;
        string? stripeType = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    stripeMessage = GetString(error, "message");
                    stripeCode = GetString(error, "code");
                    stripeType = GetString(error, "type");
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. an HTML proxy/gateway page). Fall back to the status line.
        }

        var detail = string.IsNullOrWhiteSpace(stripeMessage) ? (response.ReasonPhrase ?? "Unknown Stripe error") : stripeMessage;
        _logger.LogWarning(
            "Stripe {Operation} failed: HTTP {Status} type={Type} code={Code} message={Message}",
            operation,
            status,
            stripeType,
            stripeCode,
            detail);

        throw new PaymentGatewayApiException(
            gateway: GatewayName,
            upstreamStatusCode: status,
            message: $"Stripe {operation} failed (HTTP {status}): {detail}",
            upstreamErrorCode: stripeCode,
            upstreamErrorType: stripeType);
    }

    private bool VerifyStripeWebhook(string payload, IReadOnlyDictionary<string, string> headers, string? secret, out string? error)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            error = "Stripe webhook secret is not configured.";
            return false;
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

        // Replay-attack protection: reject events whose embedded timestamp is
        // outside the configured tolerance window. This must run BEFORE the
        // expensive HMAC compare so we cheaply discard stale redeliveries.
        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            error = "Stripe signature timestamp is malformed.";
            return false;
        }

        var maxAge = Math.Max(1, _billingOptions.Value.WebhookMaxAgeSeconds);
        var ageSeconds = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixSeconds);
        if (ageSeconds > maxAge)
        {
            // Intentionally generic to avoid leaking server clock to attackers.
            error = "Stripe signature timestamp is outside the accepted window.";
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
public sealed class PayPalGateway(
    HttpClient httpClient,
    IOptions<BillingOptions> billingOptions,
    IRuntimeSettingsProvider? runtimeSettings = null) : IPaymentGateway
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly BillingOptions _billing = billingOptions.Value;
    private readonly IRuntimeSettingsProvider? _runtimeSettings = runtimeSettings;

    public string GatewayName => "paypal";

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        var options = await GetEffectivePayPalOptionsAsync(ct);
        var successUrl = request.SuccessUrl ?? options.SuccessUrl;
        var cancelUrl = request.CancelUrl ?? options.CancelUrl;
        if (string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret)
            || string.IsNullOrWhiteSpace(successUrl)
            || string.IsNullOrWhiteSpace(cancelUrl))
        {
            if (_billing.AllowSandboxFallbacks)
            {
                return BuildSandboxOrder(request);
            }

            throw new InvalidOperationException("PayPal billing is not fully configured and sandbox fallbacks are disabled.");
        }

        var accessToken = await GetAccessTokenAsync(options, ct);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), "v2/checkout/orders"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.IdempotencyKey);
        }

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

    public async Task<CaptureResult> CaptureOrderAsync(string orderId, string idempotencyKey, CancellationToken ct)
    {
        var options = await GetEffectivePayPalOptionsAsync(ct);
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            if (_billing.AllowSandboxFallbacks)
            {
                // No live creds + sandbox fallbacks on: the order id is a synthesised
                // sandbox token, so mirror it with a synthesised completed capture.
                return new CaptureResult($"CAPTURE-{Guid.NewGuid():N}", "completed", 0m, "GBP");
            }

            throw new InvalidOperationException("PayPal capture is not configured and sandbox fallbacks are disabled.");
        }

        var accessToken = await GetAccessTokenAsync(options, ct);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Idempotent capture: a retried onApprove (or a webhook racing the capture)
            // re-uses the same request id so PayPal returns the original capture instead
            // of charging twice.
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        // PayPal's capture endpoint requires a JSON content-type even with an empty body.
        message.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        var orderStatus = GetString(root, "status") ?? "COMPLETED";
        var capture = FindFirstCapture(root);
        var captureId = capture is { } c ? GetString(c, "id") : null;
        var captureStatus = capture is { } c2 ? GetString(c2, "status") : null;
        var (amountValue, currencyCode) = ReadCaptureAmount(capture);

        var normalized = string.Equals(captureStatus ?? orderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? "completed"
            : (captureStatus ?? orderStatus).ToLowerInvariant();

        return new CaptureResult(
            CaptureId: captureId ?? orderId,
            Status: normalized,
            AmountCaptured: amountValue,
            Currency: currencyCode ?? "GBP");
    }

    async Task<RefundResult> IPaymentGateway.ProcessRefundAsync(string transactionId, decimal amount, string currency, string reason, string idempotencyKey, CancellationToken ct)
    {
        var options = await GetEffectivePayPalOptionsAsync(ct);
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            if (_billing.AllowSandboxFallbacks)
            {
                return new RefundResult($"PAYPAL-REFUND-{Guid.NewGuid():N}", "succeeded", amount);
            }

            throw new InvalidOperationException("PayPal refunds are not configured and sandbox fallbacks are disabled.");
        }

        var accessToken = await GetAccessTokenAsync(options, ct);
        var captureId = transactionId;
        if (!transactionId.StartsWith("CAPTURE-", StringComparison.OrdinalIgnoreCase))
        {
            using var orderRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), $"v2/checkout/orders/{Uri.EscapeDataString(transactionId)}"));
            orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var orderResponse = await _httpClient.SendAsync(orderRequest, ct);
            orderResponse.EnsureSuccessStatusCode();
            await using var orderStream = await orderResponse.Content.ReadAsStreamAsync(ct);
            using var orderDocument = await JsonDocument.ParseAsync(orderStream, cancellationToken: ct);
            captureId = FindFirstCaptureId(orderDocument.RootElement)
                ?? throw new InvalidOperationException("PayPal order does not expose a capture id for refunding.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(EnsureTrailingSlash(GetPayPalApiBaseUrl(options))), $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        message.Content = new StringContent(JsonSerializer.Serialize(new
        {
            amount = new
            {
                value = amount.ToString("0.00", CultureInfo.InvariantCulture),
                currency_code = string.IsNullOrWhiteSpace(currency) ? "GBP" : currency.ToUpperInvariant()
            },
            note_to_payer = string.IsNullOrWhiteSpace(reason) ? null : reason
        }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var status = GetString(document.RootElement, "status") ?? "PENDING";
        return new RefundResult(
            GetString(document.RootElement, "id") ?? $"PAYPAL-REFUND-{Guid.NewGuid():N}",
            string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ? "succeeded" : status.ToLowerInvariant(),
            amount);
    }

    private static string? FindFirstCaptureId(JsonElement order)
        => FindFirstCapture(order) is { } capture ? GetString(capture, "id") : null;

    /// <summary>Returns the first capture object inside a PayPal order's purchase_units, or null.</summary>
    private static JsonElement? FindFirstCapture(JsonElement order)
    {
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("captures", out var captures)
                && captures.ValueKind == JsonValueKind.Array)
            {
                foreach (var capture in captures.EnumerateArray())
                {
                    if (!string.IsNullOrWhiteSpace(GetString(capture, "id")))
                    {
                        return capture;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Reads the (value, currency_code) from a PayPal capture object's amount.</summary>
    private static (decimal Amount, string? Currency) ReadCaptureAmount(JsonElement? capture)
    {
        if (capture is not { } element
            || !element.TryGetProperty("amount", out var amount)
            || amount.ValueKind != JsonValueKind.Object)
        {
            return (0m, null);
        }

        var currency = GetString(amount, "currency_code");
        return decimal.TryParse(GetString(amount, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? (parsed, currency)
            : (0m, currency);
    }

    public async Task<WebhookProcessResult> HandleWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        // Replay-attack protection — PayPal `paypal-transmission-time` header.
        if (TryGetHeader(headers, "paypal-transmission-time", out var ttRaw)
            && DateTimeOffset.TryParse(ttRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var transmissionTime))
        {
            var maxAge = Math.Max(1, _billing.WebhookMaxAgeSeconds);
            if (Math.Abs((DateTimeOffset.UtcNow - transmissionTime).TotalSeconds) > maxAge)
            {
                return new WebhookProcessResult(
                    EventId: $"paypal-stale-{Guid.NewGuid():N}",
                    EventType: "signature_verification_failed",
                    Processed: false,
                    Error: "PayPal transmission timestamp is outside the accepted window.",
                    SafePayloadJson: "{}",
                    EventCategory: PaymentWebhookCategories.Other);
            }
        }

        var options = await GetEffectivePayPalOptionsAsync(ct);
        if (!await VerifyWebhookAsync(options, payload, headers, ct))
        {
            return new WebhookProcessResult(
                EventId: $"paypal-invalid-{Guid.NewGuid():N}",
                EventType: "signature_verification_failed",
                Processed: false,
                Error: "PayPal webhook verification failed.",
                SafePayloadJson: "{}",
                EventCategory: PaymentWebhookCategories.Other);
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

        var category = eventType switch
        {
            "PAYMENT.CAPTURE.REFUNDED" or "PAYMENT.SALE.REFUNDED" => PaymentWebhookCategories.Refund,
            "CUSTOMER.DISPUTE.CREATED" or "CUSTOMER.DISPUTE.UPDATED" or "CUSTOMER.DISPUTE.RESOLVED" or "RISK.DISPUTE.CREATED" => PaymentWebhookCategories.Dispute,
            "PAYMENT.CAPTURE.COMPLETED" or "CHECKOUT.ORDER.APPROVED" or "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED" or "CHECKOUT.ORDER.CANCELLED" => PaymentWebhookCategories.Payment,
            _ => PaymentWebhookCategories.Other
        };

        var normalizedStatus = eventType switch
        {
            "PAYMENT.CAPTURE.COMPLETED" => "completed",
            // Approval is NOT payment — an order with intent=CAPTURE is only paid once it
            // is captured (synchronously by our capture endpoint, confirmed by
            // PAYMENT.CAPTURE.COMPLETED). Treating APPROVED as "completed" would grant
            // entitlements/credits for an order whose funds were never taken. Keep it
            // informational ("pending") so it is recorded but never drives fulfilment.
            "CHECKOUT.ORDER.APPROVED" => "pending",
            "PAYMENT.CAPTURE.DENIED" => "failed",
            "PAYMENT.CAPTURE.DECLINED" => "failed",
            "CHECKOUT.ORDER.CANCELLED" => "failed",
            "PAYMENT.CAPTURE.REFUNDED" or "PAYMENT.SALE.REFUNDED" => "refunded",
            "CUSTOMER.DISPUTE.CREATED" => "dispute_opened",
            "CUSTOMER.DISPUTE.UPDATED" => "dispute_updated",
            "CUSTOMER.DISPUTE.RESOLVED" => string.Equals(GetString(resource, "dispute_outcome"), "RESOLVED_BUYER_FAVOUR", StringComparison.OrdinalIgnoreCase)
                ? "dispute_lost"
                : "dispute_won",
            _ => "pending"
        };

        return new WebhookProcessResult(
            EventId: eventId,
            EventType: eventType,
            Processed: true,
            Error: null,
            GatewayTransactionId: transactionId,
            NormalizedStatus: normalizedStatus,
            SafePayloadJson: PaymentWebhookPiiRedactor.RedactPayPal(root),
                EventCategory: category,
                GatewayObjectId: GetString(resource, "id"));
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

    private async Task<string> GetAccessTokenAsync(EffectivePayPalOptions options, CancellationToken ct)
    {
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

    private async Task<bool> VerifyWebhookAsync(
        EffectivePayPalOptions options,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
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

        var accessToken = await GetAccessTokenAsync(options, ct);
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

    private async Task<EffectivePayPalOptions> GetEffectivePayPalOptionsAsync(CancellationToken ct)
    {
        var configured = _billing.PayPal;
        if (_runtimeSettings is null)
        {
            return new EffectivePayPalOptions(
                configured.ApiBaseUrl,
                configured.UseSandbox,
                configured.ClientId,
                configured.ClientSecret,
                configured.WebhookId,
                configured.SuccessUrl,
                configured.CancelUrl);
        }

        var billing = (await _runtimeSettings.GetAsync(ct)).Billing;
        return new EffectivePayPalOptions(
            // Host selection (sandbox vs live + custom base URL) MUST come from the
            // effective runtime settings, not the env/appsettings defaults — otherwise
            // an admin who configures LIVE credentials and unchecks "Use PayPal Sandbox"
            // would still hit api-m.sandbox.paypal.com and every call fails 401.
            billing.PayPalApiBaseUrl,
            billing.PayPalUseSandbox,
            billing.PayPalClientId,
            billing.PayPalClientSecret,
            billing.PayPalWebhookId,
            billing.PayPalSuccessUrl,
            billing.PayPalCancelUrl);
    }

    private sealed record EffectivePayPalOptions(
        string ApiBaseUrl,
        bool UseSandbox,
        string? ClientId,
        string? ClientSecret,
        string? WebhookId,
        string? SuccessUrl,
        string? CancelUrl);

    private static string GetPayPalApiBaseUrl(EffectivePayPalOptions options)
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
/// Lookup contract for payment gateways. Implemented by <see cref="PaymentGatewayService"/>
/// in production; tests can substitute a fake to avoid spinning up HTTP clients.
/// </summary>
public interface IPaymentGatewayProvider
{
    IPaymentGateway GetGateway(string name);
    IReadOnlyList<string> SupportedGateways { get; }
}

/// <summary>
/// Unified payment gateway service that routes to Stripe or PayPal based on gateway name.
/// Registered as a scoped service to keep gateway configuration and HTTP clients aligned with request scope.
/// </summary>
public sealed class PaymentGatewayService : IPaymentGatewayProvider
{
    private readonly Dictionary<string, IPaymentGateway> _gateways;

    public PaymentGatewayService(
        StripeGateway stripe,
        PayPalGateway paypal,
        OetLearner.Api.Services.Billing.Gateways.PayTabsGateway payTabs,
        OetLearner.Api.Services.Billing.Gateways.PaymobGateway paymob,
        OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway checkoutCom)
    {
        _gateways = new Dictionary<string, IPaymentGateway>(StringComparer.OrdinalIgnoreCase)
        {
            ["stripe"] = stripe,
            ["paypal"] = paypal,
            ["paytabs"] = payTabs,
            ["paymob"] = paymob,
            ["checkoutcom"] = checkoutCom,
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

/// <summary>
/// Strict allowlist projector that strips PII (emails, names, addresses, phones,
/// IPs, raw card identifiers) from webhook payloads before persistence. Anything
/// not on the allowlist is dropped — never redacted in place — so we cannot
/// accidentally retain a payer email by misnaming a field.
/// </summary>
public static class PaymentWebhookPiiRedactor
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    public static string RedactStripe(JsonElement root)
    {
        var dataObject = root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("object", out var obj)
            && obj.ValueKind == JsonValueKind.Object
                ? obj
                : default;

        var safe = new Dictionary<string, object?>
        {
            ["id"] = PaymentGatewayJson.GetString(root, "id"),
            ["type"] = PaymentGatewayJson.GetString(root, "type"),
            ["livemode"] = TryGetBool(root, "livemode"),
            ["created"] = TryGetLong(root, "created"),
            ["api_version"] = PaymentGatewayJson.GetString(root, "api_version"),
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = ProjectStripeObject(dataObject)
            }
        };
        return JsonSerializer.Serialize(safe, Options);
    }

    public static string RedactPayPal(JsonElement root)
    {
        var resource = root.TryGetProperty("resource", out var r) && r.ValueKind == JsonValueKind.Object
            ? r
            : default;

        var safe = new Dictionary<string, object?>
        {
            ["id"] = PaymentGatewayJson.GetString(root, "id"),
            ["event_type"] = PaymentGatewayJson.GetString(root, "event_type"),
            ["create_time"] = PaymentGatewayJson.GetString(root, "create_time"),
            ["resource_type"] = PaymentGatewayJson.GetString(root, "resource_type"),
            ["resource"] = ProjectPayPalResource(resource)
        };
        return JsonSerializer.Serialize(safe, Options);
    }

    private static Dictionary<string, object?>? ProjectStripeObject(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        return new Dictionary<string, object?>
        {
            ["id"] = PaymentGatewayJson.GetString(obj, "id"),
            ["object"] = PaymentGatewayJson.GetString(obj, "object"),
            ["amount"] = TryGetLong(obj, "amount"),
            ["amount_total"] = TryGetLong(obj, "amount_total"),
            ["amount_refunded"] = TryGetLong(obj, "amount_refunded"),
            ["currency"] = PaymentGatewayJson.GetString(obj, "currency"),
            ["status"] = PaymentGatewayJson.GetString(obj, "status"),
            ["payment_status"] = PaymentGatewayJson.GetString(obj, "payment_status"),
            ["payment_intent"] = PaymentGatewayJson.GetString(obj, "payment_intent"),
            ["charge"] = PaymentGatewayJson.GetString(obj, "charge"),
            ["client_reference_id"] = PaymentGatewayJson.GetString(obj, "client_reference_id"),
            ["reason"] = PaymentGatewayJson.GetString(obj, "reason"),
            ["created"] = TryGetLong(obj, "created"),
            ["livemode"] = TryGetBool(obj, "livemode")
        };
    }

    private static Dictionary<string, object?>? ProjectPayPalResource(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        return new Dictionary<string, object?>
        {
            ["id"] = PaymentGatewayJson.GetString(r, "id"),
            ["status"] = PaymentGatewayJson.GetString(r, "status"),
            ["dispute_outcome"] = PaymentGatewayJson.GetString(r, "dispute_outcome"),
            ["amount"] = ProjectPayPalAmount(r, "amount"),
            ["gross_amount"] = ProjectPayPalAmount(r, "gross_amount"),
            ["create_time"] = PaymentGatewayJson.GetString(r, "create_time"),
            ["update_time"] = PaymentGatewayJson.GetString(r, "update_time"),
            ["custom_id"] = PaymentGatewayJson.GetString(r, "custom_id"),
            ["invoice_id"] = PaymentGatewayJson.GetString(r, "invoice_id"),
            ["supplementary_data"] = ProjectSupplementary(r)
        };
    }

    private static Dictionary<string, object?>? ProjectPayPalAmount(JsonElement r, string field)
    {
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty(field, out var amt) || amt.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new Dictionary<string, object?>
        {
            ["currency_code"] = PaymentGatewayJson.GetString(amt, "currency_code"),
            ["value"] = PaymentGatewayJson.GetString(amt, "value")
        };
    }

    private static Dictionary<string, object?>? ProjectSupplementary(JsonElement r)
    {
        if (!r.TryGetProperty("supplementary_data", out var sup) || sup.ValueKind != JsonValueKind.Object) return null;
        if (!sup.TryGetProperty("related_ids", out var related) || related.ValueKind != JsonValueKind.Object) return null;
        return new Dictionary<string, object?>
        {
            ["related_ids"] = new Dictionary<string, object?>
            {
                ["order_id"] = PaymentGatewayJson.GetString(related, "order_id"),
                ["capture_id"] = PaymentGatewayJson.GetString(related, "capture_id")
            }
        };
    }

    private static long? TryGetLong(JsonElement element, string field)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(field, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt64(out var l)
            ? l
            : null;

    private static bool? TryGetBool(JsonElement element, string field)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(field, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => (bool?)null
            }
            : null;
}
