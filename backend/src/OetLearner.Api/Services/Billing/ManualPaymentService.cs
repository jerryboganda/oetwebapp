using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Phase 4 manual-payment workflow. Learners upload proof of bank/Fawry/cash
/// payment; admins approve to grant the same access an online payment would.
/// </summary>
public interface IManualPaymentService
{
    Task<ManualPaymentRequest> SubmitAsync(string userId, ManualPaymentSubmitRequest request, byte[] proofBytes, CancellationToken ct);
    Task<ManualPaymentRequest> ApproveAsync(string requestId, string adminId, string? notes, CancellationToken ct);
    Task<ManualPaymentRequest> RejectAsync(string requestId, string adminId, string reason, CancellationToken ct);

    /// <summary>Move a request between the two non-terminal states
    /// (<c>pending</c> ↔ <c>needs_review</c>). Approved/paid/rejected requests
    /// are terminal and cannot be re-opened here.</summary>
    Task<ManualPaymentRequest> SetStatusAsync(string requestId, string adminId, string targetStatus, string? notes, CancellationToken ct);
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

        // Reject if the same proof file has been used for another user previously.
        var existingForOtherUser = await _db.ManualPaymentRequests
            .Where(r => r.ProofHashHex == hashHex && r.UserId != userId)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (existingForOtherUser is not null)
        {
            throw new InvalidOperationException($"Duplicate proof detected (already submitted by another user as {existingForOtherUser}).");
        }

        var now = DateTimeOffset.UtcNow;
        var proofKey = $"billing/manual-payments/{now:yyyy/MM}/{Guid.NewGuid():N}-{hashHex[..12]}.bin";
        await using (var stream = new MemoryStream(proofBytes))
        {
            await _storage.WriteAsync(proofKey, stream, ct);
        }

        var row = new ManualPaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            QuoteId = request.QuoteId,
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
            CourseId = string.IsNullOrWhiteSpace(request.CourseId) ? null : request.CourseId.Trim(),
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
        subscription.Status = SubscriptionStatus.Active;
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

        if (row.Status is "approved" or "paid" or "rejected")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status} and cannot be re-opened.");
        }
        if (row.Status == normalized)
        {
            return row;
        }

        row.Status = normalized;
        row.ReviewedByAdminId = adminId;
        // Leave ReviewedAt null — needs_review / pending are not terminal review outcomes.
        if (!string.IsNullOrWhiteSpace(notes))
        {
            row.AdminNotes = notes;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
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
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(
            s => s.UserId == row.UserId && s.PlanId == planCode, ct)
            ?? await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == row.UserId, ct);

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
            Status = SubscriptionStatus.Active,
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
