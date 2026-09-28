using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    // ── Payment Webhooks ──

    // PAY-17 (audit §4.6): gateway selection only validates support + enablement.
    // The amount pin itself happens at intent creation — every checkout intent is
    // minted from the server-calculated BillingQuote (quoteEntity.TotalAmount/
    // Currency, never a client-supplied figure) with quote_id in the metadata —
    // and settlement re-validates provider-vs-order on both the webhook path
    // (EnsureWebhookMatchesAuthoritativeOrder, fail-closed) and the capture path
    // (FindCaptureOrderMismatch, unconditional). A client cannot substitute a
    // cheaper provider order: any contradiction refuses the grant.
    private async Task EnsureCheckoutGatewayAsync(string gatewayLabel, CancellationToken cancellationToken)
    {
        if (!paymentGateways.SupportedGateways.Contains(gatewayLabel, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "unsupported_gateway",
                $"Payment gateway '{gatewayLabel}' is not supported.",
                [new ApiFieldError("gateway", "unsupported", "Choose an available payment method.")]);
        }

        if (paymentGatewayCatalog is not null && !await paymentGatewayCatalog.IsEnabledAsync(gatewayLabel, cancellationToken))
        {
            throw ApiException.Validation(
                "gateway_disabled",
                "This payment method is currently unavailable. Please choose another option.",
                [new ApiFieldError("gateway", "disabled", "Choose an available payment method.")]);
        }
    }

    public Task<object> HandleStripeWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("stripe", payload, headers, ct);

    public Task<object> HandlePayPalWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paypal", payload, headers, ct);

    // Regional gateways. Each gateway's HandleWebhookAsync verifies its own
    // signature/HMAC; HandlePaymentWebhookAsync then runs the same idempotent
    // dedup + fulfillment as Stripe/PayPal. These only receive traffic once an
    // admin configures the gateway's credentials in Runtime Settings.
    public Task<object> HandleCheckoutComWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("checkoutcom", payload, headers, ct);

    public Task<object> HandlePaymobWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paymob", payload, headers, ct);

    public Task<object> HandlePayTabsWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("paytabs", payload, headers, ct);

    public Task<object> HandleEasyKashWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync("easykash", payload, headers, ct);

    public Task<object> HandleWhopWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync(PaymentGatewayNames.Whop, payload, headers, ct);

    public Task<object> HandleFawaterakWebhookAsync(string payload, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        => HandlePaymentWebhookAsync(PaymentGatewayNames.Fawaterak, payload, headers, ct);

    public static bool IsRejectedWebhookOutcome(object outcome)
        => outcome.GetType().GetProperty("received")?.GetValue(outcome) is false;

    /// <summary>
    /// True when the delivery was accepted and verified but local fulfilment failed
    /// in a way that is still worth retrying.
    ///
    /// We used to answer HTTP 200 to every post-ingestion failure, so a provider was
    /// told "handled" for an order we had not fulfilled and never redelivered it —
    /// the only route back was an admin pressing Retry. docs/BILLING.md §6.2 is
    /// explicit that a 5xx is what asks the provider to retry with backoff, so a
    /// retryable failure now answers 5xx.
    ///
    /// <c>dead_letter</c> (the existing terminal state, reached after repeated
    /// attempts) deliberately still answers 200: past that point redelivery cannot
    /// help and a retry storm is worse than a quiet queue entry an admin can see.
    /// Ingestion rejections are handled separately by
    /// <see cref="IsRejectedWebhookOutcome"/> and answer 400.
    /// </summary>
    public static bool IsRetryableWebhookOutcome(object outcome)
    {
        if (IsRejectedWebhookOutcome(outcome))
        {
            return false;
        }

        var state = outcome.GetType().GetProperty("state")?.GetValue(outcome) as string;
        return string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetPaymentWebhookRetryBlockedReason(PaymentWebhookEvent evt)
    {
        if (!string.Equals(evt.ProcessingStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Only failed local webhook processing attempts can be retried.";
        }

        if (!string.Equals(evt.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase) || evt.VerifiedAt is null)
        {
            return "This webhook was not signature-verified at ingestion. Ask the payment provider to redeliver it through the live webhook endpoint.";
        }

        if (string.IsNullOrWhiteSpace(evt.GatewayTransactionId))
        {
            return "This webhook does not have a trusted parsed payment transaction id.";
        }

        if (string.IsNullOrWhiteSpace(evt.NormalizedStatus))
        {
            return "This webhook does not have a trusted parsed payment status.";
        }

        if (!string.Equals(evt.ParserVersion, PaymentWebhookParserVersion, StringComparison.Ordinal))
        {
            return "This webhook was parsed by an unsupported parser version. Ask the payment provider to redeliver it through the live webhook endpoint.";
        }

        if (!IsValidSha256(evt.PayloadSha256))
        {
            return "This webhook does not have durable payload hash evidence from ingestion.";
        }

        if (!string.Equals(evt.NormalizedStatus, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(evt.NormalizedStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(evt.NormalizedStatus, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                return "Only completed, failed, or refunded payment status webhooks can be retried by admin.";
            }
        }

        return null;
    }

    public async Task<PaymentWebhookRetryResult> RetryVerifiedPaymentWebhookAsync(
        Guid eventId,
        string actorId,
        string actorName,
        CancellationToken ct)
    {
        var existing = await db.PaymentWebhookEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw ApiException.NotFound("webhook_not_found", "Webhook event not found.");

        var blockedReason = GetPaymentWebhookRetryBlockedReason(existing);
        if (blockedReason is not null)
        {
            throw ApiException.Conflict("webhook_not_retryable", blockedReason);
        }

        var now = DateTimeOffset.UtcNow;
        var adminId = TruncateForColumn(actorId, 64);
        var adminName = TruncateForColumn(actorName, 128);

        if (db.Database.IsInMemory())
        {
            var tracked = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            if (!string.Equals(tracked.ProcessingStatus, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Conflict("webhook_already_processing", "This webhook is no longer in a failed retryable state.");
            }

            tracked.ProcessingStatus = "processing";
            tracked.ErrorMessage = null;
            tracked.ProcessedAt = null;
            tracked.AttemptCount += 1;
            tracked.RetryCount += 1;
            tracked.LastAttemptedAt = now;
            tracked.LastRetriedAt = now;
            tracked.LastRetriedByAdminId = adminId;
            tracked.LastRetriedByAdminName = adminName;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            var claimed = await db.PaymentWebhookEvents
                .Where(e => e.Id == eventId && e.ProcessingStatus == "failed")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ProcessingStatus, "processing")
                    .SetProperty(e => e.ErrorMessage, (string?)null)
                    .SetProperty(e => e.ProcessedAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.AttemptCount, e => e.AttemptCount + 1)
                    .SetProperty(e => e.RetryCount, e => e.RetryCount + 1)
                    .SetProperty(e => e.LastAttemptedAt, now)
                    .SetProperty(e => e.LastRetriedAt, now)
                    .SetProperty(e => e.LastRetriedByAdminId, adminId)
                    .SetProperty(e => e.LastRetriedByAdminName, adminName), ct);

            if (claimed == 0)
            {
                throw ApiException.Conflict("webhook_already_processing", "This webhook is no longer in a failed retryable state.");
            }

            db.ChangeTracker.Clear();
        }

        var retryEvent = await db.PaymentWebhookEvents.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);
        var result = await ApplyVerifiedPaymentWebhookEventAsync(
            retryEvent.Id,
            retryEvent.GatewayTransactionId,
            retryEvent.NormalizedStatus,
            InferWebhookCategory(retryEvent.EventType),
            retryEvent.GatewayEventId,
            ct);

        return new PaymentWebhookRetryResult(
            retryEvent.Id.ToString(),
            BuildWebhookRetryStatus(result.ProcessingStatus),
            result.ProcessingStatus,
            result.ErrorMessage,
            result.AttemptCount,
            result.RetryCount,
            result.GatewayTransactionId,
            result.NormalizedStatus);
    }

    private async Task<object> HandlePaymentWebhookAsync(
        string gatewayName,
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        WebhookProcessResult result;
        try
        {
            result = await paymentGateways.GetGateway(gatewayName).HandleWebhookAsync(payload, headers, ct);
        }
        catch (Exception ex)
        {
            result = new WebhookProcessResult(
                EventId: $"{gatewayName}-error-{Guid.NewGuid():N}",
                EventType: "handler_exception",
                Processed: false,
                Error: ex.Message);
        }

        // A REJECTED delivery used to return here without persisting anything, so
        // a bad signature, an unconfigured gateway, an unparseable body or a failed
        // server-side payment probe left no trace at all — the admin webhook backlog
        // showed nothing and support could not tell "the gateway never called us"
        // from "we threw the call away". That blind spot is what made the 15 Sep 2026
        // Whop P0 untraceable. Rejections now fall through to the same persistence
        // block below, which already knows how to record an unverified event
        // (VerificationStatus = "failed" + ErrorMessage), and we return the rejected
        // response after saving instead of before.
        //
        // Bounded on purpose: these endpoints are deliberately unauthenticated and
        // unthrottled (providers retry from rotating IPs), so an attacker could
        // otherwise grow this table with varied junk payloads. Replays of the SAME
        // payload collapse onto one row via the unique (Gateway, GatewayEventId)
        // index; beyond RejectedWebhookAuditCapPerHour distinct rejections in an hour
        // we log and drop, which still leaves ample evidence that something is wrong.
        if (!result.Processed)
        {
            var rejectionCutoff = receivedAt.AddHours(-1);
            var recentRejections = await db.PaymentWebhookEvents
                .AsNoTracking()
                .CountAsync(
                    x => x.Gateway == gatewayName
                        && x.VerificationStatus == "failed"
                        && x.ReceivedAt >= rejectionCutoff,
                    ct);

            if (recentRejections >= RejectedWebhookAuditCapPerHour)
            {
                logger?.LogWarning(
                    "Dropping the audit row for a rejected {Gateway} webhook: more than {Cap} rejections in the last hour. Reason: {Reason}",
                    gatewayName,
                    RejectedWebhookAuditCapPerHour,
                    result.Error ?? "Webhook verification failed.");

                return new
                {
                    received = false,
                    gateway = gatewayName,
                    eventId = result.EventId,
                    eventType = result.EventType,
                    error = result.Error ?? "Webhook verification failed.",
                    state = "rejected"
                };
            }
        }

        var webhookEvent = await db.PaymentWebhookEvents
            .FirstOrDefaultAsync(x => x.Gateway == gatewayName && x.GatewayEventId == result.EventId, ct);

        if (webhookEvent is not null && webhookEvent.ProcessingStatus is "completed" or "ignored")
        {
            return new
            {
                received = true,
                duplicate = true,
                gateway = gatewayName,
                eventId = webhookEvent.GatewayEventId,
                eventType = webhookEvent.EventType,
                state = webhookEvent.ProcessingStatus
            };
        }

        if (webhookEvent is not null && webhookEvent.ProcessingStatus == "processing")
        {
            var lastAttemptedAt = webhookEvent.LastAttemptedAt ?? webhookEvent.ReceivedAt;
            if (lastAttemptedAt > receivedAt.Subtract(PaymentWebhookProcessingLease))
            {
                return new
                {
                    received = true,
                    duplicate = true,
                    gateway = gatewayName,
                    eventId = webhookEvent.GatewayEventId,
                    eventType = webhookEvent.EventType,
                    state = webhookEvent.ProcessingStatus
                };
            }
        }

        var isNewWebhookEvent = webhookEvent is null;
        webhookEvent ??= new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = gatewayName,
            GatewayEventId = result.EventId,
            ReceivedAt = receivedAt
        };

        webhookEvent.EventType = result.EventType;
        webhookEvent.PayloadJson = result.Processed ? result.SafePayloadJson ?? "{}" : "{}";
        webhookEvent.PayloadSha256 = ComputePayloadSha256(payload);
        webhookEvent.ParserVersion = PaymentWebhookParserVersion;
        webhookEvent.VerificationStatus = result.Processed ? "verified" : "failed";
        webhookEvent.VerifiedAt = result.Processed ? receivedAt : null;
        webhookEvent.GatewayTransactionId = result.GatewayTransactionId;
        webhookEvent.NormalizedStatus = result.NormalizedStatus;
        webhookEvent.AttemptCount += 1;
        webhookEvent.LastAttemptedAt = receivedAt;
        webhookEvent.ErrorMessage = result.Processed ? null : result.Error ?? "Webhook verification failed.";
        webhookEvent.ProcessingStatus = result.Processed ? "processing" : ResolveWebhookFailureStatus(webhookEvent.AttemptCount);
        webhookEvent.ProcessedAt = result.Processed ? null : DateTimeOffset.UtcNow;
        if (db.Entry(webhookEvent).State == EntityState.Detached)
        {
            db.PaymentWebhookEvents.Add(webhookEvent);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNewWebhookEvent)
        {
            db.ChangeTracker.Clear();
            var existingWebhookEvent = await db.PaymentWebhookEvents
                .FirstOrDefaultAsync(x => x.Gateway == gatewayName && x.GatewayEventId == result.EventId, ct);
            if (existingWebhookEvent is null)
            {
                throw;
            }

            return new
            {
                received = true,
                duplicate = true,
                gateway = gatewayName,
                eventId = existingWebhookEvent.GatewayEventId,
                eventType = existingWebhookEvent.EventType,
                state = existingWebhookEvent.ProcessingStatus
            };
        }

        if (!result.Processed)
        {
            // Persisted above with VerificationStatus "failed" — now refuse it. HTTP
            // 400 is still the answer, so a provider that signs correctly next time
            // retries, but the attempt is on record either way.
            return new
            {
                received = false,
                gateway = gatewayName,
                eventId = result.EventId,
                eventType = result.EventType,
                error = result.Error ?? "Webhook verification failed.",
                state = "rejected"
            };
        }

        var applied = await ApplyVerifiedPaymentWebhookEventAsync(
            webhookEvent.Id,
            result.GatewayTransactionId,
            result.NormalizedStatus,
            result.EventCategory,
            result.GatewayObjectId,
            ct);

        return new
        {
            received = true,
            gateway = gatewayName,
            eventId = result.EventId,
            eventType = result.EventType,
            gatewayTransactionId = applied.GatewayTransactionId,
            normalizedStatus = applied.NormalizedStatus,
            error = applied.ErrorMessage,
            state = applied.ProcessingStatus
        };
    }

    internal async Task<PaymentWebhookRetryResult> ApplyVerifiedPaymentWebhookEventAsync(
        Guid eventId,
        string? gatewayTransactionId,
        string? normalizedStatus,
        string? eventCategory,
        string? gatewayObjectId,
        CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            var webhookEvent = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            var now = DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(gatewayTransactionId))
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "No checkout or payment transaction id was included in the webhook payload.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var privateSpeakingTargetStatus = string.IsNullOrWhiteSpace(normalizedStatus)
                ? "pending"
                : normalizedStatus.Trim().ToLowerInvariant();
            if (await ApplyPrivateSpeakingWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (await ApplyLegacyBillingWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (await ApplyCartCheckoutWebhookIfMatchedAsync(webhookEvent, gatewayTransactionId, privateSpeakingTargetStatus, ct))
            {
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var paymentTransaction = await db.PaymentTransactions
                .FirstOrDefaultAsync(x => x.GatewayTransactionId == gatewayTransactionId
                    || (x.MetadataJson != null && x.MetadataJson.Contains(gatewayTransactionId)), ct);

            paymentTransaction ??= await db.PaymentTransactions
                .Where(x => x.QuoteId == gatewayTransactionId)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (paymentTransaction is null)
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = $"Payment transaction '{gatewayTransactionId}' was not found.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            var targetStatus = string.IsNullOrWhiteSpace(normalizedStatus)
                ? paymentTransaction.Status
                : normalizedStatus.Trim().ToLowerInvariant();

            if (RequiresWebhookOrderBinding(targetStatus, eventCategory))
            {
                var authoritativeQuote = await GetQuoteForTransactionAsync(paymentTransaction, ct);
                if (authoritativeQuote is not null)
                {
                    EnsureWebhookMatchesAuthoritativeOrder(webhookEvent, authoritativeQuote, paymentTransaction);
                }
                else if (IsWalletTopUpTransaction(paymentTransaction))
                {
                    // Wallet top-ups carry no BillingQuote: the server-validated tier
                    // amount is bound into the PaymentTransaction row itself at
                    // creation (WalletService), so the order-binding that matters is
                    // provider-reported amount/currency vs that row. Fail closed on
                    // contradiction; a payload without any amount blocks nothing here
                    // but stays covered by PAY-14-style checks on capture and by
                    // reconciliation.
                    var providerMismatch = FindWebhookProviderOrderMismatch(
                        webhookEvent.PayloadJson,
                        paymentTransaction.Amount,
                        paymentTransaction.Currency,
                        webhookEvent.EventType);
                    if (providerMismatch is not null)
                    {
                        throw ApiException.Conflict("payment_amount_mismatch", providerMismatch);
                    }
                }
                else
                {
                    // Security standard PAY-07/08/09 (audit §4.5): the absence of a
                    // resolvable authoritative order is a binding failure, not a pass.
                    // Quote-less fulfilment is removed entirely: the event is parked
                    // as failed and grants nothing — an admin can retry it through
                    // RetryVerifiedPaymentWebhookAsync once the order is readable,
                    // or quarantine it for reviewed resolution.
                    throw ApiException.Conflict(
                        "payment_order_unresolvable",
                        $"Payment transaction '{paymentTransaction.GatewayTransactionId}' has no resolvable authoritative order. Fulfilment was not applied.");
                }
            }

            if (string.Equals(eventCategory, PaymentWebhookCategories.Refund, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetStatus, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(targetStatus, "refunded", StringComparison.OrdinalIgnoreCase)
                    && IsFullRefundWebhook(webhookEvent.PayloadJson, paymentTransaction.Amount))
                {
                    await ApplyCheckoutRefundAsync(paymentTransaction, webhookEvent.Id.ToString("N"), ct);
                    paymentTransaction.Status = "refunded";
                    paymentTransaction.UpdatedAt = now;
                }
                else
                {
                    await ApplyPartialCheckoutRefundAsync(paymentTransaction, webhookEvent, gatewayObjectId, ct);
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (string.Equals(eventCategory, PaymentWebhookCategories.Dispute, StringComparison.OrdinalIgnoreCase)
                && disputeService is not null
                && targetStatus.StartsWith("dispute_", StringComparison.OrdinalIgnoreCase))
            {
                await disputeService.RecordSignalAsync(new DisputeWebhookSignal(
                    paymentTransaction.Gateway,
                    string.IsNullOrWhiteSpace(gatewayObjectId) ? webhookEvent.GatewayEventId : gatewayObjectId,
                    paymentTransaction.GatewayTransactionId,
                    targetStatus,
                    paymentTransaction.Amount,
                    paymentTransaction.Currency,
                    webhookEvent.EventType), ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (string.Equals(targetStatus, "completed", StringComparison.OrdinalIgnoreCase)
                && IsTerminalNonRestorableTransactionStatus(paymentTransaction.Status))
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = $"Payment transaction is already {paymentTransaction.Status}; a later completion cannot restore access.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            if (string.Equals(paymentTransaction.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(targetStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Payment transaction is already completed; webhook status was not downgraded.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
                return MapWebhookRetryResult(webhookEvent);
            }

            paymentTransaction.Status = targetStatus;
            paymentTransaction.UpdatedAt = now;

            switch (targetStatus)
            {
                case "completed" when string.Equals(paymentTransaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase):
                    await ApplyWalletTopUpCompletionAsync(paymentTransaction, ct);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                case "completed":
                    await ApplyCheckoutCompletionAsync(paymentTransaction, ct, webhookEvent);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                case "failed":
                    await MarkCheckoutFailedAsync(paymentTransaction, ct);
                    webhookEvent.ProcessingStatus = "completed";
                    break;

                default:
                    webhookEvent.ProcessingStatus = "completed";
                    break;
            }

            webhookEvent.ErrorMessage = null;
            webhookEvent.ProcessedAt = now;
            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
            return MapWebhookRetryResult(webhookEvent);
        }
        catch (Exception ex)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            db.ChangeTracker.Clear();
            var webhookEvent = await db.PaymentWebhookEvents.FirstAsync(e => e.Id == eventId, ct);
            webhookEvent.ProcessingStatus = ResolveWebhookFailureStatus(webhookEvent.AttemptCount);
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return MapWebhookRetryResult(webhookEvent);
        }
    }

    /// <summary>
    /// Fawaterak reconciliation: when a fawaterak payment transaction is still pending,
    /// query the provider's getInvoiceData endpoint server-to-server. If the provider
    /// reports the invoice paid, record an idempotent verified webhook event and run the
    /// SAME fulfilment path a genuine callback uses. Returns true when the transaction
    /// was (or already had been) completed by this verification.
    /// </summary>
    private async Task<bool> TryReconcilePendingFawaterakPaymentAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        try
        {
            if (paymentGateways.GetGateway(PaymentGatewayNames.Fawaterak) is not OetLearner.Api.Services.Billing.Gateways.FawaterakGateway gateway)
            {
                return false;
            }

            // Throttle provider checks so rapid learner polls don't hammer Fawaterak.
            var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(transaction.MetadataJson ?? "{}", new Dictionary<string, object?>());
            var lastVerifyRaw = metadata.TryGetValue("lastFawaterakVerifyAt", out var lastVerifyObj) ? lastVerifyObj?.ToString() : null;
            if (DateTimeOffset.TryParse(lastVerifyRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lastVerify)
                && DateTimeOffset.UtcNow - lastVerify < TimeSpan.FromSeconds(15))
            {
                return false;
            }

            var invoiceStatus = await gateway.GetInvoiceStatusAsync(transaction.GatewayTransactionId!, ct);

            var now = DateTimeOffset.UtcNow;
            metadata["lastFawaterakVerifyAt"] = now.ToString("O", CultureInfo.InvariantCulture);
            var tracked = await db.PaymentTransactions.FirstAsync(x => x.Id == transaction.Id, ct);
            tracked.MetadataJson = JsonSerializer.Serialize(metadata);
            tracked.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            if (invoiceStatus is null || !invoiceStatus.Paid)
            {
                return false;
            }

            var eventId = $"fawaterak-verify-{transaction.GatewayTransactionId}";
            var webhookEvent = await db.PaymentWebhookEvents
                .FirstOrDefaultAsync(x => x.Gateway == PaymentGatewayNames.Fawaterak && x.GatewayEventId == eventId, ct);
            if (webhookEvent is not null && webhookEvent.ProcessingStatus is "completed" or "ignored")
            {
                return string.Equals(webhookEvent.NormalizedStatus, "completed", StringComparison.OrdinalIgnoreCase);
            }

            webhookEvent ??= new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = PaymentGatewayNames.Fawaterak,
                GatewayEventId = eventId,
                ReceivedAt = now
            };
            webhookEvent.EventType = "payment.succeeded";
            webhookEvent.PayloadJson = JsonSerializer.Serialize(new
            {
                invoiceId = transaction.GatewayTransactionId,
                source = "status-poll-verification",
                paidAt = invoiceStatus.PaidAt,
            });
            webhookEvent.PayloadSha256 = ComputePayloadSha256(webhookEvent.PayloadJson);
            webhookEvent.ParserVersion = PaymentWebhookParserVersion;
            webhookEvent.VerificationStatus = "verified";
            webhookEvent.VerifiedAt = now;
            webhookEvent.GatewayTransactionId = transaction.GatewayTransactionId;
            webhookEvent.NormalizedStatus = "completed";
            webhookEvent.AttemptCount += 1;
            webhookEvent.LastAttemptedAt = now;
            webhookEvent.ErrorMessage = null;
            webhookEvent.ProcessingStatus = "processing";
            webhookEvent.ProcessedAt = null;
            if (db.Entry(webhookEvent).State == EntityState.Detached)
            {
                db.PaymentWebhookEvents.Add(webhookEvent);
            }
            await db.SaveChangesAsync(ct);

            await ApplyVerifiedPaymentWebhookEventAsync(
                webhookEvent.Id,
                webhookEvent.GatewayTransactionId,
                webhookEvent.NormalizedStatus,
                PaymentWebhookCategories.Payment,
                webhookEvent.GatewayTransactionId,
                ct);
            return true;
        }
        catch (Exception)
        {
            // Reconciliation is best-effort; never fail the status endpoint over it.
            return false;
        }
    }

    /// <summary>
    /// Generalises <see cref="TryReconcilePendingFawaterakPaymentAsync"/> to every other
    /// gateway that exposes a provider status lookup, by delegating to
    /// <see cref="OetLearner.Api.Services.Billing.BillingReconciliationWorker.RecoverPaymentAsync"/>
    /// — the same admin on-demand recovery path, and the same idempotent fulfilment the
    /// nightly sweep and the webhook endpoint both use. Reusing it here means a learner who
    /// simply returns to check their payment status gets the missed-webhook recovery
    /// immediately instead of waiting for the next daily sweep.
    ///
    /// Whop in particular can only be queried by its own payment id (pay_...), never by our
    /// checkout_configuration_id, so this cannot recover a Whop payment for which literally
    /// no webhook-ish event has ever been recorded (see WhopGateway.GetTransactionConfirmationAsync).
    /// That residual gap is closed by the nightly BillingReconciliationWorker sweep and by a
    /// later webhook retry, both of which fulfil independently of whatever this endpoint
    /// answered — never a lost payment, only a delayed confirmation on this one polled read.
    /// </summary>
    private async Task<bool> TryReconcilePendingPaymentLiveAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        if (billingReconciliation is null) return false;
        if (!LivePollReconciliationGateways.Contains(transaction.Gateway, StringComparer.OrdinalIgnoreCase)) return false;

        try
        {
            // Throttle so rapid learner polling does not hammer the provider.
            var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(transaction.MetadataJson ?? "{}", new Dictionary<string, object?>());
            var lastVerifyRaw = metadata.TryGetValue("lastLivePollVerifyAt", out var lastVerifyObj) ? lastVerifyObj?.ToString() : null;
            if (DateTimeOffset.TryParse(lastVerifyRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lastVerify)
                && DateTimeOffset.UtcNow - lastVerify < TimeSpan.FromSeconds(15))
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            metadata["lastLivePollVerifyAt"] = now.ToString("O", CultureInfo.InvariantCulture);
            var tracked = await db.PaymentTransactions.FirstAsync(x => x.Id == transaction.Id, ct);
            tracked.MetadataJson = JsonSerializer.Serialize(metadata);
            tracked.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            var recovery = await billingReconciliation.RecoverPaymentAsync(transaction.Gateway, transaction.GatewayTransactionId!, ct);
            return recovery.ProviderPaid == true;
        }
        catch (Exception)
        {
            // Reconciliation is best-effort; never fail the status endpoint over it.
            return false;
        }
    }

    /// <summary>
    /// Synchronous fulfilment entry for PayPal Expanded (embedded) checkout. The browser
    /// SDK approves an order and posts its id here; we capture server-side and run the SAME
    /// idempotent grant logic the webhook uses, so the capture and a later
    /// PAYMENT.CAPTURE.COMPLETED webhook converge without double-granting. Owner-scoped:
    /// the caller must own the underlying payment transaction or speaking booking. Cart
    /// checkouts capture through the cart service, not here.
    /// </summary>
    internal async Task<PaymentCaptureResult> FulfillCapturedOrderAsync(
        string gateway,
        string userId,
        string orderId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw ApiException.Validation("order_required", "An order id is required to complete the payment.");
        }

        var transaction = await db.PaymentTransactions.FirstOrDefaultAsync(
            x => x.LearnerUserId == userId
                && (x.GatewayTransactionId == orderId || (x.MetadataJson != null && x.MetadataJson.Contains(orderId))),
            ct);

        var speakingBooking = transaction is null
            ? await db.PrivateSpeakingBookings.FirstOrDefaultAsync(
                b => (b.StripeCheckoutSessionId == orderId || b.PaymentGatewayOrderId == orderId)
                    && b.LearnerUserId == userId, ct)
            : null;

        // Cart (embedded PayPal) checkout: a paypal-gateway CheckoutSession keyed on the
        // approved order id. Same owner scope so a guessed order id can't be confirmed
        // against another learner's cart.
        var cartSession = (transaction is null && speakingBooking is null)
            ? await db.CheckoutSessions.FirstOrDefaultAsync(
                s => s.GatewayOrderId == orderId && s.UserId == userId && s.Gateway == "paypal", ct)
            : null;

        if (transaction is null && speakingBooking is null && cartSession is null)
        {
            // Unknown order, or it belongs to another learner — same opaque 404 either way
            // so a guessed order id can never be confirmed against someone else's purchase.
            throw ApiException.NotFound("order_not_found", "We couldn't find that payment to complete.");
        }

        // Idempotent short-circuit: capture or the webhook already fulfilled this order.
        if (transaction is not null && string.Equals(transaction.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentCaptureResult("completed", orderId, transaction.CaptureId, ComputeCaptureRedirect(transaction), null);
        }
        if (speakingBooking is not null && speakingBooking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded)
        {
            return new PaymentCaptureResult("completed", orderId, speakingBooking.StripePaymentIntentId, "/speaking/private", null);
        }
        if (cartSession is not null && string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentCaptureResult("completed", orderId, null, "/dashboard?purchase=success", null);
        }

        // Capture against the gateway OUTSIDE any DB transaction (never hold a db lock across
        // a network round-trip). PayPal-Request-Id keyed on the order id makes this idempotent,
        // so a retried onApprove or a webhook racing the capture cannot double-charge.
        CaptureResult capture;
        try
        {
            capture = await paymentGateways.GetGateway(gateway).CaptureOrderAsync(orderId, $"capture-{orderId}", ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "gateway_unavailable",
                "This payment method is temporarily unavailable. Please pay by card instead.",
                [new ApiFieldError("gateway", "unavailable", "Choose a different payment method.")]);
        }
        catch (PaymentGatewayApiException)
        {
            throw ApiException.ServiceUnavailable(
                "payment_gateway_error",
                "We couldn't complete your payment right now. Please try again in a moment or choose another payment method.",
                retryable: true);
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(capture.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            // Declined / not-yet-settled capture. Mark the txn failed so the learner can retry,
            // and surface a clean, retryable error to the embedded UI.
            if (transaction is not null)
            {
                transaction.Status = "failed";
                transaction.UpdatedAt = now;
                await MarkCheckoutFailedAsync(transaction, ct);
                await db.SaveChangesAsync(ct);
            }
            else if (cartSession is not null && !string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
            {
                cartSession.Status = "failed";
                cartSession.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return new PaymentCaptureResult("failed", orderId, capture.CaptureId, null, "payment_not_completed");
        }

        // A capture may report completed while having taken less than the order — or in
        // another currency. Verify the amount and currency the provider actually captured
        // against the authoritative order before granting anything (PAY-14). The check is
        // unconditional: no order, no reported amount, or a blank currency all refuse.
        var captureMismatch = FindCaptureOrderMismatch(capture, transaction, cartSession, speakingBooking);
        if (captureMismatch is not null)
        {
            if (cartSession is not null && !string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
            {
                cartSession.Status = "failed";
                cartSession.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return new PaymentCaptureResult("failed", orderId, capture.CaptureId, null, captureMismatch);
        }

        // Capture succeeded — run the idempotent grant inside a transaction.
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        try
        {
            if (speakingBooking is not null)
            {
                var confirmed = await privateSpeakingService!.ConfirmBookingPaymentAsync(orderId, capture.CaptureId, ct);
                if (!confirmed)
                {
                    throw new InvalidOperationException($"No private-speaking booking found for order {orderId}.");
                }

                await CommitIfOwnedAsync(tx, ct);
                return new PaymentCaptureResult("completed", orderId, capture.CaptureId, "/speaking/private", null);
            }

            if (cartSession is not null)
            {
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                // Same idempotent grant the PAYMENT.CAPTURE.COMPLETED webhook runs, keyed on
                // the order id, so capture and the webhook converge without double-granting.
                await fulfillmentService.FulfillCartByGatewayOrderAsync(orderId, ct);
                await CommitIfOwnedAsync(tx, ct);
                return new PaymentCaptureResult("completed", orderId, capture.CaptureId, "/dashboard?purchase=success", null);
            }

            transaction!.Status = "completed";
            transaction.CaptureId = capture.CaptureId;
            transaction.UpdatedAt = now;

            if (string.Equals(transaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase))
            {
                await ApplyWalletTopUpCompletionAsync(transaction, ct);
            }
            else
            {
                await ApplyCheckoutCompletionAsync(transaction, ct);
            }

            await db.SaveChangesAsync(ct);
            await CommitIfOwnedAsync(tx, ct);
            return new PaymentCaptureResult("completed", orderId, capture.CaptureId, ComputeCaptureRedirect(transaction), null);
        }
        catch
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }
            throw;
        }
    }

    /// <summary>Best-effort post-purchase destination mirroring the payment-return routing.</summary>
    private static string ComputeCaptureRedirect(PaymentTransaction transaction)
    {
        if (string.Equals(transaction.TransactionType, "wallet_top_up", StringComparison.OrdinalIgnoreCase))
        {
            return "/billing?tab=credits";
        }

        if (string.Equals(transaction.ProductType, "addon", StringComparison.OrdinalIgnoreCase))
        {
            // pkg_* add-ons are AI-credit bundles; route to that tab.
            var isAiPackage = transaction.MetadataJson?.Contains("pkg_", StringComparison.OrdinalIgnoreCase) ?? false;
            return isAiPackage ? "/billing?tab=ai-credits" : "/billing?tab=credits";
        }

        return "/dashboard?purchase=success";
    }

    private async Task<bool> ApplyPrivateSpeakingWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string checkoutSessionId,
        string targetStatus,
        CancellationToken ct)
    {
        // Match a Stripe checkout session (hosted) or a PayPal order id (embedded) — both
        // uniquely identify one booking and route through the same ConfirmBookingPaymentAsync.
        var bookingExists = await db.PrivateSpeakingBookings
            .AnyAsync(booking => booking.StripeCheckoutSessionId == checkoutSessionId
                || booking.PaymentGatewayOrderId == checkoutSessionId, ct);
        if (!bookingExists)
        {
            return false;
        }

        if (privateSpeakingService is null)
        {
            throw new InvalidOperationException("Private speaking payment service is not available.");
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
            {
                var confirmed = await privateSpeakingService.ConfirmBookingPaymentAsync(
                    checkoutSessionId,
                    ExtractStripePaymentIntentId(webhookEvent.PayloadJson),
                    ct);
                if (!confirmed)
                {
                    throw new InvalidOperationException($"No private-speaking booking found for Stripe checkout session {checkoutSessionId}.");
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
            }

            case "failed":
                if (string.Equals(webhookEvent.EventType, "checkout.session.expired", StringComparison.OrdinalIgnoreCase))
                {
                    await privateSpeakingService.HandleCheckoutExpiredAsync(checkoutSessionId, ct);
                }
                else
                {
                    await privateSpeakingService.HandlePaymentFailureAsync(checkoutSessionId, ct);
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Private speaking checkout payment is not settled yet; waiting for a terminal Stripe event.";
                webhookEvent.ProcessedAt = now;
                await db.SaveChangesAsync(ct);
                return true;
        }
    }

    private async Task<bool> ApplyLegacyBillingWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string gatewayObjectId,
        string targetStatus,
        CancellationToken ct)
    {
        if (IsStripeInvoiceWebhook(webhookEvent.EventType))
        {
            if (targetStatus == "completed")
            {
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillRenewalAsync(gatewayObjectId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
            }
            else
            {
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Stripe invoice webhook is not a paid renewal event.";
            }

            webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (!IsStripeCheckoutSessionWebhook(webhookEvent.EventType))
        {
            return false;
        }

        var checkoutSession = await db.CheckoutSessions
            .FirstOrDefaultAsync(session => session.StripeSessionId == gatewayObjectId, ct);
        if (checkoutSession is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillCheckoutAsync(gatewayObjectId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            case "failed":
                if (!string.Equals(checkoutSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
                {
                    checkoutSession.Status = string.Equals(webhookEvent.EventType, "checkout.session.expired", StringComparison.OrdinalIgnoreCase)
                        ? "expired"
                        : "failed";
                    checkoutSession.UpdatedAt = now;
                    checkoutSession.ExpiresAt ??= now;
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "Stripe checkout session payment is not settled yet; waiting for a terminal Stripe event.";
                break;
        }

        webhookEvent.ProcessedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Webhook backstop for PayPal (embedded) cart checkouts. A PAYMENT.CAPTURE.COMPLETED
    /// event carries the order id as the transaction id; we map it to the paypal-gateway
    /// CheckoutSession and run the SAME idempotent fulfilment the capture endpoint runs, so
    /// the two converge without double-granting. Returns false when no cart session matches
    /// (so the caller falls through to the PaymentTransaction path).
    /// </summary>
    private async Task<bool> ApplyCartCheckoutWebhookIfMatchedAsync(
        PaymentWebhookEvent webhookEvent,
        string gatewayOrderId,
        string targetStatus,
        CancellationToken ct)
    {
        var cartSession = await db.CheckoutSessions
            .FirstOrDefaultAsync(s => s.GatewayOrderId == gatewayOrderId && s.Gateway == "paypal", ct);
        if (cartSession is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cartSession.UserId)
            || FindWebhookProviderOrderMismatch(webhookEvent.PayloadJson, cartSession.TotalAmount, cartSession.Currency, webhookEvent.EventType) is not null)
        {
            webhookEvent.ProcessingStatus = "ignored";
            webhookEvent.ErrorMessage = "PayPal cart webhook could not be bound to the checkout session it names.";
            webhookEvent.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        switch (targetStatus)
        {
            case "completed":
                if (fulfillmentService is null)
                {
                    throw new InvalidOperationException("Billing fulfillment service is not available.");
                }

                await fulfillmentService.FulfillCartByGatewayOrderAsync(gatewayOrderId, ct);
                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            case "failed":
                if (!string.Equals(cartSession.Status, "fulfilled", StringComparison.OrdinalIgnoreCase))
                {
                    cartSession.Status = "failed";
                    cartSession.UpdatedAt = now;
                }

                webhookEvent.ProcessingStatus = "completed";
                webhookEvent.ErrorMessage = null;
                break;

            default:
                webhookEvent.ProcessingStatus = "ignored";
                webhookEvent.ErrorMessage = "PayPal cart order is not settled yet; waiting for a capture event.";
                break;
        }

        webhookEvent.ProcessedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool IsStripeCheckoutSessionWebhook(string? eventType)
        => eventType is not null
           && eventType.StartsWith("checkout.session.", StringComparison.OrdinalIgnoreCase);

    private static bool IsStripeInvoiceWebhook(string? eventType)
        => eventType is not null
           && eventType.StartsWith("invoice.", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractStripePaymentIntentId(string? safePayloadJson)
    {
        if (string.IsNullOrWhiteSpace(safePayloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(safePayloadJson);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("object", out var obj))
            {
                return PaymentGatewayJson.GetString(obj, "payment_intent");
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (db.Database.IsInMemory()) return null;
        return await db.Database.BeginTransactionAsync(ct);
    }

    private static async Task CommitIfOwnedAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private static PaymentWebhookRetryResult MapWebhookRetryResult(PaymentWebhookEvent evt)
        => new(
            evt.Id.ToString(),
            BuildWebhookRetryStatus(evt.ProcessingStatus),
            evt.ProcessingStatus,
            evt.ErrorMessage,
            evt.AttemptCount,
            evt.RetryCount,
            evt.GatewayTransactionId,
            evt.NormalizedStatus);

    private static string BuildWebhookRetryStatus(string processingStatus)
        => processingStatus switch
        {
            "completed" => "reprocessed",
            "ignored" => "no_effect",
            "failed" => "still_failed",
            _ => processingStatus
        };

    private string ResolveWebhookFailureStatus(int attemptCount)
        => attemptCount >= Math.Max(1, billingOptions?.Value.WebhookMaxAttempts ?? 5)
            ? "dead_letter"
            : "failed";

    private static string InferWebhookCategory(string eventType)
    {
        if (eventType.Contains("dispute", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentWebhookCategories.Dispute;
        }

        if (eventType.Contains("refund", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentWebhookCategories.Refund;
        }

        return PaymentWebhookCategories.Payment;
    }

    private static string ComputePayloadSha256(string payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static bool IsValidSha256(string? value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);
}
