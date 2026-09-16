using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Billing;
using OetLearner.Api.Services.Billing.Gateways;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// PAY-19 / MON-07 — daily billing reconciliation sweep.
///
/// The only pre-existing reconciliation was the inline, learner-poll driven
/// <c>LearnerService.TryReconcilePendingFawaterakPaymentAsync</c> (Fawaterak only,
/// only while a learner happens to poll). There was no scheduled, provider-agnostic
/// sweep that answers "did a payment settle at the provider but not locally, or did
/// we mark something paid locally that the provider never settled?".
///
/// This worker closes that gap. Once per day (leader-independent — see below) it
/// sweeps three classes of divergence and records each as a
/// <see cref="BillingEvent"/> of type <c>reconciliation.mismatch</c>.
///
/// It also REPAIRS the two "provider paid, we did not grant" directions rather than
/// only reporting them (owner P0, 15 Sep 2026: "a missed/rejected webhook is
/// automatically recovered by reconciliation without manual fulfilment"). Recovery
/// re-drives the same verified-fulfilment path the webhook endpoint uses, so every
/// idempotency layer it carries applies unchanged and a recovered payment can never
/// double-grant, double-invoice or re-gift credits. Where no webhook was ever
/// delivered, a verified event is synthesised under a deterministic id from the
/// provider's own server-to-server answer. The finding row is still written either
/// way and records the outcome:
///
///   1. <b>Stale pending</b> — <see cref="PaymentTransaction"/> rows still
///      <c>pending</c> past the configured age. The provider is queried
///      server-to-server through the existing
///      <see cref="IPaymentGatewayProvider"/> abstraction (never hand-rolled HTTP).
///      A paid provider response with a pending local row is the
///      "provider-paid, no local fulfilment" direction; an unpaid/unknown response
///      is recorded as an informational mismatch for manual review.
///   2. <b>Provider-paid event without fulfilment</b> — verified
///      <see cref="PaymentWebhookEvent"/> rows whose <c>NormalizedStatus</c> is
///      <c>completed</c> but whose local processing landed in <c>ignored</c> /
///      <c>dead_letter</c>, i.e. the provider told us it was paid and we never granted.
///   3. <b>Locally completed without provider evidence</b> — <see cref="PaymentTransaction"/>
///      rows marked <c>completed</c> that have no verified provider completion
///      event; where the provider exposes a status lookup it is queried and a
///      not-paid answer is a hard mismatch. Stuck <see cref="CheckoutSession"/> and
///      <see cref="BillingQuote"/> rows that already have a completed provider
///      event are swept the same way.
///
/// Idempotency: every finding is written under a deterministic
/// <see cref="BillingEvent"/> primary key derived from
/// (day, direction, gateway, transaction key), so a worker restart — or a second
/// replica racing the same day — re-uses the same row instead of double-reporting.
/// Because sibling Billing workers (<see cref="SubscriptionExpiryWorker"/>,
/// <c>DunningWorker</c>, <c>FxRateRefreshWorker</c>, <c>BillingMetricsRollupWorker</c>)
/// do NOT take a leader lock, this worker follows suit and relies on that per-day
/// key for exactly-once-per-day reporting instead of an advisory lock.
///
/// Robustness: the loop never throws — every provider call and every DB write is
/// individually guarded, and a mismatch is logged as a warning.
///
/// Configuration keys (read from <see cref="IConfiguration"/>, under <c>Billing:</c>,
/// matching how sibling Billing services read env/appsettings):
///   • <c>Billing:Reconciliation:Enabled</c>            (bool,   default true)
///   • <c>Billing:Reconciliation:SweepIntervalHours</c>  (int,    default 24)
///   • <c>Billing:Reconciliation:PendingAgeMinutes</c>   (int,    default 60)
///   • <c>Billing:Reconciliation:LookbackDays</c>        (int,    default 30)
///   • <c>Billing:Reconciliation:BatchSize</c>           (int,    default 200)
/// </summary>
public sealed class BillingReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<BillingReconciliationWorker> logger) : BackgroundService
{
    /// <summary>BillingEvent.EventType stamped on every reconciliation finding.</summary>
    public const string MismatchEventType = "reconciliation.mismatch";

    /// <summary>
    /// The gateways that own a real provider-side payment state. "manual" / wallet
    /// admin grants and other synthetic identifiers are deliberately excluded so a
    /// manual approval is never reported as a missing provider counterpart.
    /// </summary>
    private static readonly string[] ProviderGateways =
    {
        PaymentGatewayNames.Stripe,
        PaymentGatewayNames.PayPal,
        PaymentGatewayNames.Whop,
        PaymentGatewayNames.Fawaterak,
        PaymentGatewayNames.Paymob,
        PaymentGatewayNames.PayTabs,
        PaymentGatewayNames.EasyKash,
        PaymentGatewayNames.CheckoutCom,
    };

    private readonly bool _enabled =
        configuration.GetValue("Billing:Reconciliation:Enabled", true);

    private readonly TimeSpan _sweepInterval = TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("Billing:Reconciliation:SweepIntervalHours", 24)));

    private readonly TimeSpan _pendingAge = TimeSpan.FromMinutes(
        Math.Max(1, configuration.GetValue("Billing:Reconciliation:PendingAgeMinutes", 60)));

    private readonly TimeSpan _lookback = TimeSpan.FromDays(
        Math.Max(1, configuration.GetValue("Billing:Reconciliation:LookbackDays", 30)));

    private readonly int _batchSize =
        Math.Max(1, configuration.GetValue("Billing:Reconciliation:BatchSize", 200));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            logger.LogInformation(
                "BillingReconciliationWorker disabled via Billing:Reconciliation:Enabled=false; no sweeps scheduled.");
            return;
        }

        // Small startup jitter so blue/green replicas do not all sweep the same instant.
        try { await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(_sweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    /// <summary>
    /// Recover ONE named provider payment on demand, for an admin working a live
    /// incident instead of waiting for the daily sweep. Same path the sweep uses, so
    /// it is idempotent: running it twice grants once.
    ///
    /// Returns a human-readable trace for the admin response. Never throws.
    /// </summary>
    public async Task<string> RecoverPaymentAsync(string gatewayName, string paymentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gatewayName) || string.IsNullOrWhiteSpace(paymentId))
        {
            return "no_action (gateway and payment id are both required)";
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var gateways = scope.ServiceProvider.GetRequiredService<IPaymentGatewayProvider>();
            var fulfilment = scope.ServiceProvider.GetRequiredService<LearnerService>();
            var now = clock.GetUtcNow();

            // 1. An event may already exist for this payment (Whop stamps the payment
            //    id as the event id) — re-drive it rather than inventing a new one.
            var recorded = await db.PaymentWebhookEvents
                .Where(e => e.Gateway == gatewayName && e.GatewayEventId == paymentId)
                .FirstOrDefaultAsync(ct);

            if (recorded is not null && string.Equals(recorded.ProcessingStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return $"already_fulfilled (event {recorded.GatewayEventId} processed at {recorded.ProcessedAt:O})";
            }

            // 2. Confirm with the provider server-side before anything is granted —
            //    an admin asking nicely is not evidence of payment.
            var status = await QueryProviderStatusAsync(gateways, db, gatewayName, paymentId, ct);
            if (status is null)
            {
                return "unverified (the provider could not confirm this payment; nothing was granted)";
            }

            if (!status.Paid)
            {
                return $"not_paid (provider status: {status.RawStatus ?? "unpaid"}; nothing was granted)";
            }

            if (recorded is not null)
            {
                recorded.NormalizedStatus = "completed";
                recorded.VerificationStatus = "verified";
                recorded.VerifiedAt ??= now;
                await db.SaveChangesAsync(ct);
                return await TryReapplyWebhookEventAsync(db, fulfilment, recorded, ct);
            }

            // 3. No event at all — the webhook was never delivered. Find the local
            //    order this payment belongs to and recover through it.
            var txn = await db.PaymentTransactions
                .AsNoTracking()
                .Where(t => t.Gateway == gatewayName
                    && (t.GatewayTransactionId == paymentId
                        || (t.MetadataJson != null && t.MetadataJson.Contains(paymentId))))
                .OrderByDescending(t => t.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (txn is null)
            {
                return "unmatched (the provider confirms this payment but no local order references it; "
                    + "resolve the order manually before granting)";
            }

            return await TryRecoverPaidTransactionAsync(db, fulfilment, txn, status, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "On-demand reconciliation failed for {Gateway} payment {PaymentId}.", gatewayName, paymentId);
            return $"recovery_failed ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Single sweep, exposed for deterministic tests. Returns the number of NEW
    /// mismatch findings recorded (findings already reported earlier the same day
    /// count as zero). Never throws.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (!_enabled)
        {
            return 0;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var gateways = scope.ServiceProvider.GetRequiredService<IPaymentGatewayProvider>();
            // Recovery re-uses the ONE verified-fulfilment path the webhook uses, so
            // every idempotency layer it already carries (event dedupe, order
            // binding, quote-completed early return, terminal-state downgrade
            // guards) applies unchanged. It runs request-free here exactly as it
            // does from the unauthenticated webhook endpoint.
            var fulfilment = scope.ServiceProvider.GetRequiredService<LearnerService>();
            var now = clock.GetUtcNow();

            var findings = 0;
            findings += await ReconcileStalePendingTransactionsAsync(db, gateways, fulfilment, now, ct);
            findings += await ReconcileProviderPaidEventsWithoutFulfilmentAsync(db, fulfilment, now, ct);
            findings += await ReconcileLocallyCompletedWithoutProviderEvidenceAsync(db, gateways, now, ct);
            findings += await ReconcileUnfulfilledSessionsAndQuotesAsync(db, now, ct);

            if (findings > 0)
            {
                logger.LogWarning("Billing reconciliation recorded {Count} mismatch finding(s).", findings);
            }
            else
            {
                logger.LogInformation("Billing reconciliation sweep complete: no mismatches.");
            }

            return findings;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Billing reconciliation sweep failed.");
            return 0;
        }
    }

    /// <summary>
    /// Direction 1 + "provider-paid, no local fulfilment": pending transactions
    /// older than <see cref="_pendingAge"/>, with a server-to-server provider query.
    /// </summary>
    private async Task<int> ReconcileStalePendingTransactionsAsync(
        LearnerDbContext db,
        IPaymentGatewayProvider gateways,
        LearnerService fulfilment,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ageCutoff = now - _pendingAge;

        // Server-side narrow on the translatable predicates only; the timestamp
        // comparison runs in memory so the query is valid on Postgres, the bundled
        // SQLite desktop backend and the InMemory test provider alike (same
        // limitation SubscriptionExpiryWorker works around).
        var candidates = await db.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Status == "pending" && ProviderGateways.Contains(t.Gateway))
            .OrderBy(t => t.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        var findings = 0;
        foreach (var txn in candidates)
        {
            if (txn.CreatedAt > ageCutoff)
            {
                continue; // not old enough to reconcile yet
            }

            try
            {
                var status = await QueryProviderStatusAsync(gateways, db, txn.Gateway, txn.GatewayTransactionId, ct);

                if (status is not null)
                {
                    findings += await RecordAmountDivergenceAsync(db, txn, status, ct);
                }

                if (status is null)
                {
                    findings += await RecordFindingAsync(
                        db,
                        direction: "stale_pending_unverified",
                        entityType: "PaymentTransaction",
                        entityKey: txn.GatewayTransactionId,
                        userId: txn.LearnerUserId,
                        gateway: txn.Gateway,
                        amount: txn.Amount,
                        currency: txn.Currency,
                        detail: "Pending payment older than the reconciliation age; the provider could not "
                            + "confirm a settled payment (server-to-server lookup unsupported or unreachable).",
                        ct: ct);
                }
                else if (status.Paid)
                {
                    // Do not just report it — recover it. The brief this closes is
                    // explicit: "a missed/rejected webhook is automatically recovered
                    // by reconciliation without manual fulfilment."
                    var recovery = await TryRecoverPaidTransactionAsync(db, fulfilment, txn, status, now, ct);

                    findings += await RecordFindingAsync(
                        db,
                        direction: "provider_paid_not_fulfilled",
                        entityType: "PaymentTransaction",
                        entityKey: txn.GatewayTransactionId,
                        userId: txn.LearnerUserId,
                        gateway: txn.Gateway,
                        amount: txn.Amount,
                        currency: txn.Currency,
                        detail: $"Provider reports this payment settled (provider status: {status.RawStatus ?? "paid"}) "
                            + $"but the local transaction was still pending. Automatic recovery: {recovery}.",
                        ct: ct);
                }
                else
                {
                    findings += await RecordFindingAsync(
                        db,
                        direction: "stale_pending_unpaid",
                        entityType: "PaymentTransaction",
                        entityKey: txn.GatewayTransactionId,
                        userId: txn.LearnerUserId,
                        gateway: txn.Gateway,
                        amount: txn.Amount,
                        currency: txn.Currency,
                        detail: $"Pending payment older than the reconciliation age; provider reports it not paid "
                            + $"(provider status: {status.RawStatus ?? "unpaid"}).",
                        ct: ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Reconciliation provider lookup failed for {Gateway} transaction {TransactionId}.",
                    txn.Gateway,
                    txn.GatewayTransactionId);
            }
        }

        return findings;
    }

    /// <summary>
    /// PAY-17: compares the provider-reported charge against the ledger row. A gateway
    /// may FX-convert the charge out of the quote currency (Whop/EasyKash convert to a
    /// fixed target currency and Fawaterak to USD), but the effective converted amount
    /// is not persisted on <see cref="PaymentTransaction"/> yet. A differing provider
    /// currency is therefore recorded as <c>amount_unverifiable_fx</c> — informational,
    /// because a conversion would legitimately explain it and equivalence cannot be
    /// proven. Only a same-currency difference is a hard <c>amount_mismatch</c>.
    /// </summary>
    private async Task<int> RecordAmountDivergenceAsync(
        LearnerDbContext db,
        PaymentTransaction txn,
        ProviderStatus status,
        CancellationToken ct)
    {
        if (status.Amount is not { } providerAmount || providerAmount <= 0 || string.IsNullOrWhiteSpace(status.Currency))
        {
            return 0;
        }

        var recorded = decimal.Round(txn.Amount, 2, MidpointRounding.AwayFromZero);
        var reported = decimal.Round(providerAmount, 2, MidpointRounding.AwayFromZero);
        var recordedCurrency = txn.Currency.Trim().ToUpperInvariant();
        var reportedCurrency = status.Currency.Trim().ToUpperInvariant();

        if (!string.Equals(recordedCurrency, reportedCurrency, StringComparison.Ordinal))
        {
            return await RecordFindingAsync(
                db,
                direction: "amount_unverifiable_fx",
                entityType: "PaymentTransaction",
                entityKey: txn.GatewayTransactionId,
                userId: txn.LearnerUserId,
                gateway: txn.Gateway,
                amount: txn.Amount,
                currency: txn.Currency,
                detail: $"Provider reports a {reportedCurrency} charge of {reported} for a ledger row held as "
                    + $"{recorded} {recordedCurrency}. A gateway FX conversion into {reportedCurrency} would explain "
                    + "the difference, but the effective charge is not persisted, so equivalence cannot be proven.",
                ct: ct,
                providerAmount: providerAmount,
                providerCurrency: status.Currency);
        }

        if (recorded == reported)
        {
            return 0;
        }

        return await RecordFindingAsync(
            db,
            direction: "amount_mismatch",
            entityType: "PaymentTransaction",
            entityKey: txn.GatewayTransactionId,
            userId: txn.LearnerUserId,
            gateway: txn.Gateway,
            amount: txn.Amount,
            currency: txn.Currency,
            detail: $"Provider reports {reported} {reportedCurrency} but the ledger row records {recorded} "
                + $"{recordedCurrency} in the same currency — the amount the provider took does not match the ledger.",
            ct: ct,
            providerAmount: providerAmount,
            providerCurrency: status.Currency);
    }

    /// <summary>
    /// Direction 2: the provider told us a payment completed but local processing
    /// never fulfilled it (ignored / dead-lettered webhook event).
    /// </summary>
    private async Task<int> ReconcileProviderPaidEventsWithoutFulfilmentAsync(
        LearnerDbContext db,
        LearnerService fulfilment,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var candidates = await db.PaymentWebhookEvents
            .AsNoTracking()
            .Where(e => e.NormalizedStatus == "completed"
                && ProviderGateways.Contains(e.Gateway)
                && (e.ProcessingStatus == "ignored" || e.ProcessingStatus == "dead_letter"))
            .OrderByDescending(e => e.ReceivedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        var findings = 0;
        foreach (var evt in candidates)
        {
            // "ignored" is TERMINAL to the webhook dedupe, so once an event lands
            // there — which is what happens when the transaction lookup misses, e.g.
            // the event arrived before the local row existed — every provider retry
            // short-circuits as a duplicate and a real payment stays buried forever.
            // Re-driving it here is the only thing that un-buries it, and it is safe:
            // the fulfilment path is idempotent end to end.
            var recovery = await TryReapplyWebhookEventAsync(db, fulfilment, evt, ct);

            findings += await RecordFindingAsync(
                db,
                direction: "provider_paid_event_unfulfilled",
                entityType: "PaymentWebhookEvent",
                entityKey: evt.GatewayEventId,
                userId: null,
                gateway: evt.Gateway,
                amount: null,
                currency: null,
                detail: "Provider reported a completed payment event that local fulfilment did not apply "
                    + $"(processing status: {evt.ProcessingStatus}; reason: {evt.ErrorMessage ?? "none recorded"}). "
                    + $"Automatic recovery: {recovery}.",
                ct: ct);
        }

        return findings;
    }

    /// <summary>
    /// Re-drive one already-recorded, provider-verified completion through the normal
    /// fulfilment path. Returns a short outcome string for the audit row; never throws.
    /// </summary>
    private async Task<string> TryReapplyWebhookEventAsync(
        LearnerDbContext db,
        LearnerService fulfilment,
        PaymentWebhookEvent evt,
        CancellationToken ct)
    {
        try
        {
            var applied = await fulfilment.ApplyVerifiedPaymentWebhookEventAsync(
                evt.Id,
                evt.GatewayTransactionId,
                evt.NormalizedStatus,
                PaymentWebhookCategories.Payment,
                evt.GatewayEventId,
                ct);

            return applied.ProcessingStatus == "completed"
                ? "recovered"
                : $"not_recovered ({applied.ProcessingStatus})";
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Reconciliation could not re-apply {Gateway} webhook event {EventId}.",
                evt.Gateway,
                evt.GatewayEventId);
            db.ChangeTracker.Clear();
            return $"recovery_failed ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Recover a payment the provider says is settled but that never got fulfilled
    /// locally. Prefers an existing verified webhook event; when the webhook was never
    /// delivered at all there is nothing to re-drive, so a verified event is
    /// synthesised from the provider's own server-to-server answer.
    ///
    /// The synthesised event id is deterministic, so the unique
    /// (Gateway, GatewayEventId) index makes repeat sweeps a no-op, and a real webhook
    /// arriving later dedupes against the completed quote rather than granting twice.
    /// </summary>
    private async Task<string> TryRecoverPaidTransactionAsync(
        LearnerDbContext db,
        LearnerService fulfilment,
        PaymentTransaction txn,
        ProviderStatus status,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var existing = await db.PaymentWebhookEvents
                .Where(e => e.Gateway == txn.Gateway
                    && e.GatewayTransactionId == txn.GatewayTransactionId
                    && e.NormalizedStatus == "completed")
                .OrderByDescending(e => e.ReceivedAt)
                .FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                var syntheticId = $"reconciliation:{txn.Gateway}:{txn.GatewayTransactionId}";
                existing = await db.PaymentWebhookEvents
                    .FirstOrDefaultAsync(e => e.Gateway == txn.Gateway && e.GatewayEventId == syntheticId, ct);

                if (existing is null)
                {
                    existing = new PaymentWebhookEvent
                    {
                        Id = Guid.NewGuid(),
                        Gateway = txn.Gateway,
                        GatewayEventId = syntheticId,
                        EventType = "reconciliation.provider_confirmed",
                        GatewayTransactionId = txn.GatewayTransactionId,
                        NormalizedStatus = "completed",
                        ProcessingStatus = "processing",
                        // The provider was queried server-to-server, which is stronger
                        // proof than a signed callback body.
                        VerificationStatus = "verified",
                        VerifiedAt = now,
                        ReceivedAt = now,
                        LastAttemptedAt = now,
                        AttemptCount = 1,
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            source = "billing_reconciliation",
                            gateway = txn.Gateway,
                            gatewayTransactionId = txn.GatewayTransactionId,
                            providerStatus = status.RawStatus,
                            providerAmount = status.Amount,
                            providerCurrency = status.Currency,
                        }),
                    };
                    db.PaymentWebhookEvents.Add(existing);
                    await db.SaveChangesAsync(ct);
                }
            }

            return await TryReapplyWebhookEventAsync(db, fulfilment, existing, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Reconciliation could not recover {Gateway} transaction {TransactionId}.",
                txn.Gateway,
                txn.GatewayTransactionId);
            db.ChangeTracker.Clear();
            return $"recovery_failed ({ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Direction 3: a locally <c>completed</c> transaction with no verified provider
    /// completion event. Where the provider exposes a status lookup, a not-paid (or
    /// unconfirmable) answer is surfaced.
    /// </summary>
    private async Task<int> ReconcileLocallyCompletedWithoutProviderEvidenceAsync(
        LearnerDbContext db,
        IPaymentGatewayProvider gateways,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var lookbackCutoff = now - _lookback;

        var candidates = await db.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Status == "completed" && ProviderGateways.Contains(t.Gateway))
            .OrderByDescending(t => t.UpdatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        // Preload (gateway|transaction) pairs already evidenced by a verified
        // completed webhook to avoid an N+1 query per candidate.
        var completedEvidence = await db.PaymentWebhookEvents
            .AsNoTracking()
            .Where(e => e.NormalizedStatus == "completed" && ProviderGateways.Contains(e.Gateway))
            .Select(e => new { e.Gateway, e.GatewayTransactionId })
            .ToListAsync(ct);

        var evidencedKeys = completedEvidence
            .Where(e => !string.IsNullOrWhiteSpace(e.GatewayTransactionId))
            .Select(e => EvidenceKey(e.Gateway, e.GatewayTransactionId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = 0;
        foreach (var txn in candidates)
        {
            if (txn.UpdatedAt < lookbackCutoff)
            {
                continue;
            }

            if (evidencedKeys.Contains(EvidenceKey(txn.Gateway, txn.GatewayTransactionId)))
            {
                continue; // a verified provider completion event exists — consistent
            }

            try
            {
                var status = await QueryProviderStatusAsync(gateways, db, txn.Gateway, txn.GatewayTransactionId, ct);

                if (status is { Paid: true })
                {
                    continue; // provider confirms settlement — not a mismatch
                }

                if (status is null)
                {
                    findings += await RecordFindingAsync(
                        db,
                        direction: "local_completed_unverified",
                        entityType: "PaymentTransaction",
                        entityKey: txn.GatewayTransactionId,
                        userId: txn.LearnerUserId,
                        gateway: txn.Gateway,
                        amount: txn.Amount,
                        currency: txn.Currency,
                        detail: "Transaction is locally completed but has no verified provider completion event "
                            + "and the provider could not confirm settlement.",
                        ct: ct);
                }
                else
                {
                    findings += await RecordFindingAsync(
                        db,
                        direction: "local_completed_no_provider_payment",
                        entityType: "PaymentTransaction",
                        entityKey: txn.GatewayTransactionId,
                        userId: txn.LearnerUserId,
                        gateway: txn.Gateway,
                        amount: txn.Amount,
                        currency: txn.Currency,
                        detail: $"Transaction is locally completed but the provider reports it NOT paid "
                            + $"(provider status: {status.RawStatus ?? "unpaid"}).",
                        ct: ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Reconciliation provider lookup failed for completed {Gateway} transaction {TransactionId}.",
                    txn.Gateway,
                    txn.GatewayTransactionId);
            }
        }

        return findings;
    }

    /// <summary>
    /// Direction 2 (continued): stuck <see cref="CheckoutSession"/> and
    /// <see cref="BillingQuote"/> rows that already carry a completed provider event
    /// but were never fulfilled / consumed.
    /// </summary>
    private async Task<int> ReconcileUnfulfilledSessionsAndQuotesAsync(
        LearnerDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var staleCutoff = now - _pendingAge;

        var sessions = await db.CheckoutSessions
            .AsNoTracking()
            .Where(s => ProviderGateways.Contains(s.Gateway)
                && s.Status != "fulfilled"
                && s.Status != "failed")
            .OrderBy(s => s.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        var findings = 0;
        foreach (var session in sessions)
        {
            if (session.UpdatedAt > staleCutoff)
            {
                continue;
            }

            var key = !string.IsNullOrWhiteSpace(session.GatewayOrderId)
                ? session.GatewayOrderId
                : session.StripeSessionId;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var providerPaid = await db.PaymentWebhookEvents
                .AsNoTracking()
                .AnyAsync(e => e.Gateway == session.Gateway
                    && e.GatewayTransactionId == key
                    && e.NormalizedStatus == "completed", ct);
            if (!providerPaid)
            {
                continue;
            }

            findings += await RecordFindingAsync(
                db,
                direction: "checkout_session_paid_not_fulfilled",
                entityType: "CheckoutSession",
                entityKey: session.Id.ToString(),
                userId: session.UserId,
                gateway: session.Gateway,
                amount: session.TotalAmount,
                currency: session.Currency,
                detail: $"Checkout session is still '{session.Status}' although the provider reported a "
                    + "completed payment; the grant was never fulfilled.",
                ct: ct);
        }

        var quotes = await db.BillingQuotes
            .AsNoTracking()
            .Where(q => q.Status == BillingQuoteStatus.Created && q.CheckoutSessionId != null)
            .OrderBy(q => q.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var quote in quotes)
        {
            if (quote.CreatedAt > staleCutoff)
            {
                continue;
            }

            var key = quote.CheckoutSessionId!;
            var providerPaid = await db.PaymentWebhookEvents
                .AsNoTracking()
                .AnyAsync(e => e.GatewayTransactionId == key && e.NormalizedStatus == "completed", ct);
            if (!providerPaid)
            {
                continue;
            }

            findings += await RecordFindingAsync(
                db,
                direction: "billing_quote_paid_not_consumed",
                entityType: "BillingQuote",
                entityKey: quote.Id,
                userId: quote.UserId,
                gateway: "unknown",
                amount: quote.TotalAmount,
                currency: quote.Currency,
                detail: $"Billing quote is still '{quote.Status}' although a completed provider payment event "
                    + "exists for its checkout session.",
                ct: ct);
        }

        return findings;
    }

    /// <summary>
    /// Server-to-server provider status lookup, reached through the shared
    /// <see cref="IPaymentGatewayProvider"/> abstraction (no hand-rolled HTTP).
    /// Returns <c>null</c> when the gateway exposes no lookup on the shared contract
    /// (Stripe / PayPal / Whop) or the lookup could not confirm state — callers treat
    /// null as "unknown", never as "unpaid".
    /// </summary>
    private static async Task<ProviderStatus?> QueryProviderStatusAsync(
        IPaymentGatewayProvider gateways,
        LearnerDbContext db,
        string gatewayName,
        string transactionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        var gateway = gateways.GetGateway(gatewayName);
        switch (gateway)
        {
            case WhopGateway whop:
            {
                // Whop's payments resource is addressed by its own payment id
                // (pay_...), but PaymentTransaction.GatewayTransactionId holds the
                // checkout_configuration_id (ch_...) or our quote id. Whop stamps the
                // payment id as the webhook event id, so recover it from there when
                // the ledger does not already hold one.
                var paymentId = transactionId.StartsWith("pay_", StringComparison.OrdinalIgnoreCase)
                    ? transactionId
                    : await db.PaymentWebhookEvents
                        .AsNoTracking()
                        .Where(e => e.Gateway == gatewayName
                            && e.GatewayTransactionId == transactionId
                            && e.GatewayEventId.StartsWith("pay_"))
                        .OrderByDescending(e => e.ReceivedAt)
                        .Select(e => e.GatewayEventId)
                        .FirstOrDefaultAsync(ct);

                if (string.IsNullOrWhiteSpace(paymentId))
                {
                    // No addressable payment id: UNKNOWN, never "unpaid".
                    return null;
                }

                var confirmation = await whop.GetTransactionConfirmationAsync(paymentId, ct);
                return confirmation is null
                    ? null
                    : new ProviderStatus(confirmation.Paid, confirmation.Amount, confirmation.Currency, confirmation.RawStatus);
            }

            case FawaterakGateway fawaterak:
            {
                var status = await fawaterak.GetInvoiceStatusAsync(transactionId, ct);
                return status is null
                    ? null
                    : new ProviderStatus(status.Paid, null, null, status.RawStatus);
            }

            case PaymobGateway paymob:
            {
                var confirmation = await paymob.GetTransactionConfirmationAsync(transactionId, ct);
                return confirmation is null
                    ? null
                    : new ProviderStatus(confirmation.Paid, confirmation.Amount, confirmation.Currency, confirmation.RawStatus);
            }

            case PayTabsGateway payTabs:
            {
                var confirmation = await payTabs.GetTransactionConfirmationAsync(transactionId, ct);
                return confirmation is null
                    ? null
                    : new ProviderStatus(confirmation.Paid, confirmation.Amount, confirmation.Currency, confirmation.RawStatus);
            }

            case CheckoutComGateway checkoutCom:
            {
                var confirmation = await checkoutCom.GetTransactionConfirmationAsync(transactionId, ct);
                return confirmation is null
                    ? null
                    : new ProviderStatus(confirmation.Paid, confirmation.Amount, confirmation.Currency, confirmation.RawStatus);
            }

            case EasyKashGateway easyKash:
            {
                var confirmation = await easyKash.GetTransactionConfirmationAsync(transactionId, ct);
                return confirmation is null
                    ? null
                    : new ProviderStatus(confirmation.Paid, confirmation.Amount, confirmation.Currency, confirmation.RawStatus);
            }

            default:
                // Stripe / PayPal expose no server-to-server status lookup on the shared
                // IPaymentGateway contract — state is "unknown" for reconciliation.
                return null;
        }
    }

    /// <summary>
    /// Writes one audit finding to <see cref="BillingEvent"/> under a deterministic
    /// per-day primary key so restarts and concurrent replicas never double-report.
    /// Returns 1 when a new finding row was inserted, 0 when it already existed.
    /// </summary>
    private async Task<int> RecordFindingAsync(
        LearnerDbContext db,
        string direction,
        string entityType,
        string? entityKey,
        string? userId,
        string gateway,
        decimal? amount,
        string? currency,
        string detail,
        CancellationToken ct,
        decimal? providerAmount = null,
        string? providerCurrency = null)
    {
        var now = clock.GetUtcNow();
        var dayKey = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var eventId = BuildFindingEventId(direction, gateway, entityKey, dayKey);

        var alreadyRecorded = await db.BillingEvents
            .AsNoTracking()
            .AnyAsync(e => e.Id == eventId, ct);
        if (alreadyRecorded)
        {
            return 0;
        }

        logger.LogWarning(
            "Billing reconciliation mismatch [{Direction}]: gateway={Gateway} transaction={Transaction} "
            + "amount={Amount} {Currency}. {Detail}",
            direction,
            gateway,
            entityKey,
            amount,
            currency,
            detail);

        db.BillingEvents.Add(new BillingEvent
        {
            Id = eventId,
            UserId = Truncate(userId, 64),
            EventType = MismatchEventType,
            EntityType = Truncate(entityType, 64) ?? entityType,
            EntityId = Truncate(entityKey, 256),
            PayloadJson = JsonSerializer.Serialize(new
            {
                direction,
                gateway,
                transactionKey = entityKey,
                amount,
                currency,
                providerAmount,
                providerCurrency,
                detail,
                day = dayKey,
                detectedAt = now,
            }),
            OccurredAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return 1;
        }
        catch (DbUpdateException)
        {
            // A concurrent replica inserted the same deterministic per-day row first;
            // it has already reported the finding, so this remains a single report.
            db.ChangeTracker.Clear();
            return 0;
        }
    }

    private static string EvidenceKey(string gateway, string transactionId)
        => $"{gateway}|{transactionId}";

    private static string BuildFindingEventId(string direction, string gateway, string? entityKey, string dayKey)
    {
        var seed = $"{direction}|{gateway}|{entityKey}|{dayKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        // "recon-" (6) + day (8) + "-" (1) + 32 hex = 47 chars, within BillingEvent.Id's 64-char column.
        return $"recon-{dayKey}-{hash[..32]}";
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record ProviderStatus(bool Paid, decimal? Amount, string? Currency, string? RawStatus);
}
