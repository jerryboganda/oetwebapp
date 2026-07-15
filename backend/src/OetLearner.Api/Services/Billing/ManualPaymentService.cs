using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Universal proof-of-payment workflow — every order carries exactly one
/// <see cref="ManualPaymentRequest"/> row. Learners upload proof for offline
/// methods (bank transfer / Vodafone Cash / InstaPay / Fawry / Wise / …) and an
/// admin approves it to grant the same access an online payment would; card
/// gateways get a system-minted receipt instead (<see cref="CreateGatewayReceiptAsync"/>).
/// </summary>
public interface IManualPaymentService
{
    Task<ManualPaymentRequest> SubmitAsync(string userId, ManualPaymentSubmitRequest request, byte[] proofBytes, CancellationToken ct);
    Task<ManualPaymentRequest> ApproveAsync(string requestId, string adminId, string? notes, CancellationToken ct);
    Task<ManualPaymentRequest> RejectAsync(string requestId, string adminId, string reason, CancellationToken ct);

    /// <summary>Move a request between the non-terminal states (<c>pending</c> ↔
    /// <c>needs_review</c>), or re-open a <c>rejected</c> one back to <c>pending</c>
    /// (a mis-clicked Reject must be recoverable). <c>approved</c>/<c>paid</c> stay
    /// terminal — money has already been recognised and access granted.</summary>
    Task<ManualPaymentRequest> SetStatusAsync(string requestId, string adminId, string targetStatus, string? notes, CancellationToken ct);

    /// <summary>Mint the proof row for a completed card-gateway payment, so the admin
    /// dashboard shows one record per order regardless of how it was paid. Idempotent on
    /// <see cref="PaymentTransaction.Id"/>.
    ///
    /// Does NOT call SaveChanges — the row is added to the caller's unit of work
    /// (the checkout-completion path mutates the subscription in the same transaction
    /// and must commit the receipt atomically with it).</summary>
    Task<ManualPaymentRequest> CreateGatewayReceiptAsync(
        string userId,
        PaymentTransaction transaction,
        string courseName,
        string? courseId,
        CancellationToken ct);

    /// <summary>Release a pending offline order whose payment was confirmed out-of-band,
    /// without an uploaded file. Records who waived it, when, and why, plus an AuditEvent.
    /// The request stays pending — the admin still has to approve it.</summary>
    Task<ManualPaymentRequest> WaiveProofAsync(string requestId, string adminId, string adminName, string reason, CancellationToken ct);
}

public sealed record ManualPaymentSubmitRequest(
    string? QuoteId,
    decimal AmountAmount,
    string Currency,
    string Method,
    string Reference,
    string ProofUrl,
    string CandidateFullName,
    string CandidateEmail,
    string CandidateWhatsApp,
    string CourseName,
    string? CourseId,
    string PaymentCategory);

public sealed class ManualPaymentService : IManualPaymentService
{
    private readonly LearnerDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IBillingNotificationDispatcher? _notifier;
    private readonly ILogger<ManualPaymentService>? _logger;

    /// <summary>Fallback method allowlist used only when the PaymentMethodConfigs
    /// table is empty. Mirrors the seed in 20260620120000_AddPaymentMethodConfig.</summary>
    private static readonly string[] KnownMethodKeys =
    {
        "instapay_qr_link",
        "vodafone_cash_fawry",
        "qnb_egypt",
        "stripe_card",
        "paypal_business",
        "uk_monzo_transfer",
        "international_monzo_transfer",
    };

    public ManualPaymentService(
        LearnerDbContext db,
        IFileStorage storage,
        IBillingNotificationDispatcher? notifier = null,
        ILogger<ManualPaymentService>? logger = null)
    {
        _db = db;
        _storage = storage;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<ManualPaymentRequest> SubmitAsync(string userId, ManualPaymentSubmitRequest request, byte[] proofBytes, CancellationToken ct)
    {
        if (proofBytes.Length == 0)
        {
            throw new InvalidOperationException("Payment proof is required.");
        }
        if (proofBytes.Length > ManualPaymentProof.MaxProofBytes)
        {
            throw new InvalidOperationException("Payment proof must be 10 MB or smaller.");
        }
        if (!ManualPaymentProof.IsAllowedProof(proofBytes))
        {
            throw new InvalidOperationException("Payment proof must be an image (JPG, PNG, GIF, WEBP) or a PDF.");
        }
        if (string.IsNullOrWhiteSpace(request.CandidateFullName)
            || string.IsNullOrWhiteSpace(request.CandidateEmail)
            || string.IsNullOrWhiteSpace(request.CourseName)
            || request.AmountAmount <= 0
            || string.IsNullOrWhiteSpace(request.Method)
            || string.IsNullOrWhiteSpace(request.Reference))
        {
            throw new InvalidOperationException("Full name, email, course name, amount, payment method, transaction reference, and proof are required.");
        }

        // Reject unknown payment methods. The set of accepted methods is the active
        // PaymentMethodConfig keys; when that table is empty (deploy-order race or
        // test fixtures) we fall back to the original seeded keys so a valid request
        // is never rejected before the seed migration runs.
        // NOTE: keep KnownMethodKeys in sync with the seed in
        // 20260620120000_AddPaymentMethodConfig until the table is guaranteed seeded
        // in every environment, then this fallback can be removed.
        var activeKeys = await _db.PaymentMethodConfigs
            .Where(m => m.IsActive)
            .Select(m => m.Key)
            .ToListAsync(ct);
        IReadOnlyCollection<string> acceptedKeys = activeKeys.Count > 0 ? activeKeys : KnownMethodKeys;
        if (!acceptedKeys.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown payment method '{request.Method}'.");
        }

        var hashHex = Convert.ToHexString(SHA256.HashData(proofBytes)).ToLowerInvariant();
        var courseId = string.IsNullOrWhiteSpace(request.CourseId) ? null : request.CourseId.Trim();

        // A proof file is evidence for exactly one order. Reject it when it has already
        // been used by anyone else, or by this learner against a *different* order —
        // one receipt must not pay for two packages. Re-uploading against the same order
        // (fixing a typo, an admin asked for a resubmit) stays allowed.
        var priorUses = await _db.ManualPaymentRequests
            .Where(r => r.ProofHashHex == hashHex)
            .Select(r => new { r.Id, r.UserId, r.QuoteId, r.CourseId })
            .ToListAsync(ct);

        var otherUserUse = priorUses.FirstOrDefault(u => !string.Equals(u.UserId, userId, StringComparison.Ordinal));
        if (otherUserUse is not null)
        {
            throw new InvalidOperationException($"Duplicate proof detected (already submitted by another user as {otherUserUse.Id}).");
        }

        var otherOrderUse = priorUses.FirstOrDefault(u => !IsSameOrder(u.QuoteId, u.CourseId, request.QuoteId, courseId));
        if (otherOrderUse is not null)
        {
            throw new InvalidOperationException($"Duplicate proof detected (already submitted for a different order as {otherOrderUse.Id}).");
        }

        var now = DateTimeOffset.UtcNow;
        var proofKey = $"billing/manual-payments/{now:yyyy/MM}/{Guid.NewGuid():N}-{hashHex[..12]}.bin";
        await using (var stream = new MemoryStream(proofBytes))
        {
            await _storage.WriteAsync(proofKey, stream, ct);
        }

        var professionId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.ActiveProfessionId)
            .FirstOrDefaultAsync(ct);

        var row = new ManualPaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            QuoteId = request.QuoteId,
            Kind = PaymentProofKinds.LearnerUpload,
            ProfessionId = professionId,
            AmountAmount = request.AmountAmount,
            Currency = request.Currency.ToUpperInvariant(),
            Method = request.Method,
            Reference = request.Reference,
            ProofUrl = proofKey,
            ProofHashHex = hashHex,
            CandidateFullName = request.CandidateFullName.Trim(),
            CandidateEmail = request.CandidateEmail.Trim(),
            CandidateWhatsApp = (request.CandidateWhatsApp ?? string.Empty).Trim(),
            CourseName = request.CourseName.Trim(),
            CourseId = courseId,
            PaymentCategory = NormalizePaymentCategory(request.PaymentCategory),
            Status = "pending",
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.ManualPaymentRequests.Add(row);
        await _db.SaveChangesAsync(ct);

        await DispatchSafeAsync("manual_payment_received", row, reason: null, ct);
        return row;
    }

    public async Task<ManualPaymentRequest> ApproveAsync(string requestId, string adminId, string? notes, CancellationToken ct)
    {
        var row = await _db.ManualPaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Manual payment request not found.");

        if (row.Status is "approved" or "paid" or "rejected")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status}.");
        }

        var now = DateTimeOffset.UtcNow;
        var gatewayTransactionId = $"manual_{row.Id}";
        var quote = string.IsNullOrWhiteSpace(row.QuoteId)
            ? null
            : await _db.BillingQuotes.FirstOrDefaultAsync(q => q.Id == row.QuoteId && q.UserId == row.UserId, ct);

        var planCode = quote?.PlanCode ?? row.CourseId ?? row.CourseName;
        var plan = await _db.BillingPlans.FirstOrDefaultAsync(p => p.Code == planCode || p.Id == planCode, ct);
        BillingPlanVersion? planVersion = null;
        if (!string.IsNullOrWhiteSpace(quote?.PlanVersionId))
        {
            planVersion = await _db.BillingPlanVersions.FirstOrDefaultAsync(v => v.Id == quote.PlanVersionId, ct);
        }
        if (plan is null && planVersion is not null)
        {
            plan = await _db.BillingPlans.FirstOrDefaultAsync(p => p.Id == planVersion.PlanId || p.Code == planVersion.Code, ct);
        }
        if (plan is null && planVersion is null)
        {
            throw new InvalidOperationException("Manual payment cannot be approved because the selected course/plan was not found.");
        }

        var existingTxn = await _db.PaymentTransactions.FirstOrDefaultAsync(t => t.GatewayTransactionId == gatewayTransactionId, ct);
        Guid txnId;
        if (existingTxn is null)
        {
            var txn = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                LearnerUserId = row.UserId,
                Gateway = "manual",
                GatewayTransactionId = gatewayTransactionId,
                TransactionType = "subscription_payment",
                Status = "completed",
                Amount = row.AmountAmount,
                Currency = row.Currency,
                ProductType = "subscription",
                ProductId = plan?.Code ?? planVersion!.Code,
                QuoteId = row.QuoteId,
                PlanVersionId = quote?.PlanVersionId ?? planVersion?.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.PaymentTransactions.Add(txn);
            txnId = txn.Id;
        }
        else
        {
            existingTxn.Status = "completed";
            existingTxn.UpdatedAt = now;
            txnId = existingTxn.Id;
        }

        var subscription = await ResolveSubscriptionForApprovalAsync(row, plan, planVersion, now, ct);
        if (planVersion is not null)
        {
            SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, planVersion, now);
        }
        else if (plan is not null)
        {
            SubscriptionBundleInitializer.ApplyPlanEntitlements(subscription, plan, now);
        }
        // Delivery method decides whether payment alone unlocks access. Telegram /
        // manual packages are paid but not delivered: the entitlement resolver grants
        // nothing on Pending, so access stays shut until an admin marks the order
        // fulfilled. The Active guard means re-approving an order never revokes access
        // a learner already has.
        var deliveryMethod = planVersion?.DeliveryMethod ?? plan?.DeliveryMethod ?? DeliveryMethods.AutomaticWeb;
        if (DeliveryMethods.RequiresManualFulfilment(deliveryMethod)
            && subscription.FulfilmentStatus != FulfilmentStatuses.Fulfilled
            && subscription.Status != SubscriptionStatus.Active)
        {
            subscription.FulfilmentStatus = FulfilmentStatuses.PendingManual;
            subscription.Status = SubscriptionStatus.Pending;
        }
        else
        {
            subscription.Status = SubscriptionStatus.Active;
        }
        subscription.ChangedAt = now;

        // AI-credit parity with the Stripe webhook fulfillment path: the
        // ApplyPlanEntitlements calls above deliberately leave AiCreditsRemaining
        // untouched (the AI-credit ledger is the single source of truth), so the
        // bundled AI credits must be granted here, with the same idempotency
        // guard the webhook uses (see CreditAiLedgerForPlanPaymentAsync). Keyed
        // on the manual request id so re-approval never double-grants.
        var aiCredits = planVersion?.BundledAiCredits ?? plan?.BundledAiCredits ?? 0;
        if (aiCredits > 0)
        {
            var planCodeForCredit = plan?.Code ?? planVersion!.Code;
            var creditReferenceId = $"manual:{row.Id}:{planCodeForCredit}";
            var alreadyGranted = await _db.AiCreditLedger.AnyAsync(
                e => e.UserId == row.UserId
                     && e.Source == AiCreditSource.Purchase
                     && e.ReferenceId == creditReferenceId, ct);
            if (!alreadyGranted)
            {
                var durationMonths = plan?.DurationMonths ?? planVersion?.DurationMonths ?? 0;
                _db.AiCreditLedger.Add(new AiCreditLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = row.UserId,
                    TokensDelta = aiCredits,
                    CostDeltaUsd = 0m,
                    Source = AiCreditSource.Purchase,
                    Description = $"{plan?.Name ?? row.CourseName} bundled AI grading credits (manual payment)",
                    ReferenceId = creditReferenceId,
                    ExpiresAt = durationMonths > 0 ? now.AddMonths(durationMonths) : null,
                    CreatedAt = now,
                });
                subscription.AiCreditsRemaining = checked(subscription.AiCreditsRemaining + aiCredits);
            }
        }

        if (quote is not null)
        {
            quote.Status = BillingQuoteStatus.Completed;
            quote.CheckoutSessionId = gatewayTransactionId;
        }

        row.Status = "paid";
        row.ReviewedAt = now;
        row.ReviewedByAdminId = adminId;
        row.AdminNotes = notes;
        row.UpdatedAt = now;
        row.AccessGrantedSubscriptionId = subscription.Id;

        await _db.SaveChangesAsync(ct);

        await DispatchSafeAsync("manual_payment_approved", row, reason: null, ct);
        return row;
    }

    public async Task<ManualPaymentRequest> RejectAsync(string requestId, string adminId, string reason, CancellationToken ct)
    {
        var row = await _db.ManualPaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Manual payment request not found.");

        if (row.Status is "approved" or "paid" or "rejected")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status}.");
        }

        row.Status = "rejected";
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewedByAdminId = adminId;
        row.AdminNotes = reason;
        row.UpdatedAt = row.ReviewedAt.Value;
        await _db.SaveChangesAsync(ct);

        await DispatchSafeAsync("manual_payment_rejected", row, reason, ct);
        return row;
    }

    public async Task<ManualPaymentRequest> SetStatusAsync(string requestId, string adminId, string targetStatus, string? notes, CancellationToken ct)
    {
        var normalized = (targetStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is not ("pending" or "needs_review"))
        {
            throw new InvalidOperationException("Status can only be set to 'pending' or 'needs_review'. Use approve or reject for terminal decisions.");
        }

        var row = await _db.ManualPaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Manual payment request not found.");

        // approved/paid are terminal — the payment is recognised and access granted, so
        // re-opening would leave the two records disagreeing. 'rejected' is reversible:
        // a mis-clicked Reject must not permanently strand a genuine payment.
        if (row.Status is "approved" or "paid")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status} and cannot be re-opened.");
        }
        if (row.Status == "rejected" && normalized != "pending")
        {
            throw new InvalidOperationException("A rejected payment can only be re-opened to 'pending'.");
        }
        if (row.Status == normalized)
        {
            return row;
        }

        var wasRejected = row.Status == "rejected";
        row.Status = normalized;
        row.ReviewedByAdminId = adminId;
        // Leave ReviewedAt null — needs_review / pending are not terminal review outcomes.
        if (wasRejected)
        {
            row.ReviewedAt = null;
        }
        if (!string.IsNullOrWhiteSpace(notes))
        {
            row.AdminNotes = notes;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<ManualPaymentRequest> CreateGatewayReceiptAsync(
        string userId,
        PaymentTransaction transaction,
        string courseName,
        string? courseId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Idempotent on the transaction: a webhook replay (or a webhook racing the
        // synchronous capture path) must not mint a second receipt for one payment.
        // Checks the change tracker too — the caller batches its writes, so an
        // already-added row is not yet visible to a query.
        var pending = _db.ChangeTracker.Entries<ManualPaymentRequest>()
            .Select(e => e.Entity)
            .FirstOrDefault(e => e.PaymentTransactionId == transaction.Id);
        if (pending is not null)
        {
            return pending;
        }
        var existing = await _db.ManualPaymentRequests
            .FirstOrDefaultAsync(r => r.PaymentTransactionId == transaction.Id, ct);
        if (existing is not null)
        {
            return existing;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        var now = DateTimeOffset.UtcNow;
        var row = new ManualPaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            QuoteId = transaction.QuoteId,
            Kind = PaymentProofKinds.GatewayReceipt,
            Gateway = Truncate(transaction.Gateway, 32),
            PaymentTransactionId = transaction.Id,
            ProfessionId = user?.ActiveProfessionId,
            AmountAmount = transaction.Amount,
            Currency = transaction.Currency.ToUpperInvariant(),
            Method = Truncate(transaction.Gateway, 32),
            Reference = Truncate(transaction.GatewayTransactionId, 128),
            // No file to store, and nothing to dedupe against — the gateway's own
            // transaction id is the evidence.
            ProofUrl = null,
            ProofHashHex = string.Empty,
            CandidateFullName = Truncate(user?.DisplayName ?? string.Empty, 128),
            CandidateEmail = Truncate(user?.Email ?? string.Empty, 256),
            CandidateWhatsApp = string.Empty,
            CourseName = Truncate(courseName ?? string.Empty, 128),
            CourseId = string.IsNullOrWhiteSpace(courseId) ? null : Truncate(courseId.Trim(), 64),
            PaymentCategory = "international",
            // Card money has already cleared — there is nothing for an admin to verify.
            Status = "paid",
            SubmittedAt = transaction.CreatedAt == default ? now : transaction.CreatedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.ManualPaymentRequests.Add(row);
        return row;
    }

    public async Task<ManualPaymentRequest> WaiveProofAsync(string requestId, string adminId, string adminName, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("A reason is required when waiving the proof requirement.");
        }

        var row = await _db.ManualPaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Manual payment request not found.");

        if (row.Status is "approved" or "paid" or "rejected")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status}.");
        }
        if (row.Kind != PaymentProofKinds.LearnerUpload)
        {
            throw new InvalidOperationException("Only a learner-upload proof can be waived; gateway receipts have no upload requirement.");
        }

        var now = DateTimeOffset.UtcNow;
        row.ProofWaivedByAdminId = adminId;
        row.ProofWaivedAt = now;
        row.ProofWaiverReason = Truncate(reason.Trim(), 512);
        row.UpdatedAt = now;

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = adminId,
            ActorName = adminName,
            Action = "manual_payment.waive_proof",
            ResourceType = "ManualPaymentRequest",
            ResourceId = row.Id,
            Details = $"Waived the proof-upload requirement for {row.CandidateEmail} / {row.CourseName} ({row.AmountAmount:0.00} {row.Currency}). Reason: {row.ProofWaiverReason}",
        });

        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Best-effort billing notification dispatch. A notification
    /// failure must never roll back the already-persisted payment mutation, so
    /// this swallows and logs any error (matching the webhook fulfillment path).</summary>
    private async Task DispatchSafeAsync(string eventCode, ManualPaymentRequest row, string? reason, CancellationToken ct)
    {
        if (_notifier is null)
        {
            return;
        }
        try
        {
            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fullName"] = row.CandidateFullName,
                ["courseName"] = row.CourseName,
                ["amount"] = $"{row.AmountAmount:0.00} {row.Currency}",
                ["method"] = row.Method,
            };
            if (!string.IsNullOrWhiteSpace(reason))
            {
                vars["reason"] = reason;
            }
            await _notifier.DispatchAsync(new BillingNotificationEvent(eventCode, row.Id, row.UserId, vars), ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Manual payment notification '{EventCode}' failed for request {RequestId}.", eventCode, row.Id);
        }
    }

    private async Task<Subscription> ResolveSubscriptionForApprovalAsync(
        ManualPaymentRequest row,
        BillingPlan? plan,
        BillingPlanVersion? planVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var planCode = plan?.Code ?? planVersion?.Code ?? row.CourseId ?? row.CourseName;

        // Match on the plan being approved ONLY. Subscriptions are many-per-user and
        // additive: falling back to "any subscription this user happens to have" would
        // overwrite an unrelated package in place, silently deleting access the learner
        // already paid for. No match ⇒ this is a new package ⇒ new row.
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(
            s => s.UserId == row.UserId && s.PlanId == planCode, ct);

        if (subscription is not null)
        {
            subscription.PlanId = planCode;
            subscription.PlanVersionId = planVersion?.Id;
            subscription.PriceAmount = plan?.Price ?? planVersion?.Price ?? row.AmountAmount;
            subscription.Currency = plan?.Currency ?? planVersion?.Currency ?? row.Currency;
            subscription.Interval = plan?.Interval ?? planVersion?.Interval ?? "one_time";
            if (subscription.StartedAt == default) subscription.StartedAt = now;
            if (subscription.NextRenewalAt <= now)
            {
                subscription.NextRenewalAt = now.AddMonths(Math.Max(1, plan?.DurationMonths ?? planVersion?.DurationMonths ?? 6));
            }
            return subscription;
        }

        subscription = new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = row.UserId,
            PlanId = planCode,
            PlanVersionId = planVersion?.Id,
            // Pending on creation; ApproveAsync decides Active vs pending-manual from
            // the plan's delivery method.
            Status = SubscriptionStatus.Pending,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddMonths(Math.Max(1, plan?.DurationMonths ?? planVersion?.DurationMonths ?? 6)),
            PriceAmount = plan?.Price ?? planVersion?.Price ?? row.AmountAmount,
            Currency = plan?.Currency ?? planVersion?.Currency ?? row.Currency,
            Interval = plan?.Interval ?? planVersion?.Interval ?? "one_time",
            AccessDurationDays = Math.Max(1, plan?.AccessDurationDays ?? planVersion?.AccessDurationDays ?? 180),
        };
        _db.Subscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>Two proof submissions belong to the same order when they carry the same
    /// quote. Quote-less orders (direct course purchases) fall back to the course id; a
    /// quoted and a quote-less submission are never the same order.</summary>
    private static bool IsSameOrder(string? quoteIdA, string? courseIdA, string? quoteIdB, string? courseIdB)
    {
        var hasQuoteA = !string.IsNullOrWhiteSpace(quoteIdA);
        var hasQuoteB = !string.IsNullOrWhiteSpace(quoteIdB);
        if (hasQuoteA || hasQuoteB)
        {
            return hasQuoteA && hasQuoteB && string.Equals(quoteIdA, quoteIdB, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(courseIdA ?? string.Empty, courseIdB ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Clamp to the destination column width. Gateway-supplied values (transaction
    /// ids especially) are wider than the proof row's columns, and a receipt must never be
    /// lost to a length overflow on save.</summary>
    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string NormalizePaymentCategory(string? value)
        => string.Equals(value, "egypt", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "inside_egypt", StringComparison.OrdinalIgnoreCase)
            ? "inside_egypt"
            : "international";
}

/// <summary>
/// Content-type sniffing for manual-payment proof files. Proofs are stored as
/// opaque <c>.bin</c> blobs via <see cref="IFileStorage"/> with no recorded MIME,
/// so the admin proof-viewer endpoint detects the type from magic bytes, and the
/// submit path uses the same logic to reject anything that is not an image or PDF.
/// </summary>
internal static class ManualPaymentProof
{
    /// <summary>Upper bound on proof file size — mirrors the media upload cap.</summary>
    internal const long MaxProofBytes = 10L * 1024 * 1024;

    /// <summary>Detect the content type of a proof file from its leading bytes.
    /// Returns <c>application/octet-stream</c> when the header matches no allowed
    /// image/PDF signature.</summary>
    internal static string SniffContentType(ReadOnlySpan<byte> bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }
        // GIF: "GIF87a" / "GIF89a"
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38
            && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return "image/gif";
        }
        // WEBP: "RIFF"...."WEBP"
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }
        // PDF: "%PDF-"
        if (bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D)
        {
            return "application/pdf";
        }
        return "application/octet-stream";
    }

    /// <summary>True when the bytes start with an allowed image or PDF signature.</summary>
    internal static bool IsAllowedProof(ReadOnlySpan<byte> bytes)
        => SniffContentType(bytes) != "application/octet-stream";
}
