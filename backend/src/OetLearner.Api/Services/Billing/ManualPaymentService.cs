using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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

    public ManualPaymentService(LearnerDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<ManualPaymentRequest> SubmitAsync(string userId, ManualPaymentSubmitRequest request, byte[] proofBytes, CancellationToken ct)
    {
        if (proofBytes.Length == 0)
        {
            throw new InvalidOperationException("Payment proof is required.");
        }
        if (string.IsNullOrWhiteSpace(request.CandidateFullName)
            || string.IsNullOrWhiteSpace(request.CandidateEmail)
            || string.IsNullOrWhiteSpace(request.CandidateWhatsApp)
            || string.IsNullOrWhiteSpace(request.CourseName)
            || request.AmountAmount <= 0
            || string.IsNullOrWhiteSpace(request.Method)
            || string.IsNullOrWhiteSpace(request.Reference))
        {
            throw new InvalidOperationException("Full name, email, WhatsApp number, course name, amount, payment method, transaction reference, and proof are required.");
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
            CandidateWhatsApp = request.CandidateWhatsApp.Trim(),
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
        return row;
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
