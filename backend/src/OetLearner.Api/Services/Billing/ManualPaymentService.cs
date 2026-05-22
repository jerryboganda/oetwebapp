using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

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
    string ProofUrl);

public sealed class ManualPaymentService : IManualPaymentService
{
    private readonly LearnerDbContext _db;

    public ManualPaymentService(LearnerDbContext db) => _db = db;

    public async Task<ManualPaymentRequest> SubmitAsync(string userId, ManualPaymentSubmitRequest request, byte[] proofBytes, CancellationToken ct)
    {
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
        var row = new ManualPaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            QuoteId = request.QuoteId,
            AmountAmount = request.AmountAmount,
            Currency = request.Currency.ToUpperInvariant(),
            Method = request.Method,
            Reference = request.Reference,
            ProofUrl = request.ProofUrl,
            ProofHashHex = hashHex,
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

        if (row.Status is "approved" or "rejected")
        {
            throw new InvalidOperationException($"Manual payment already {row.Status}.");
        }

        // Create a synthetic PaymentTransaction so the existing post-payment
        // pipeline (subscription activation, credit grant, idempotency) runs
        // exactly like a gateway-completed checkout. The unique
        // GatewayTransactionId is derived from the manual-payment id to keep
        // approval idempotent across retries.
        var now = DateTimeOffset.UtcNow;
        var gatewayTransactionId = $"manual_{row.Id}";

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
                QuoteId = row.QuoteId,
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

        row.Status = "approved";
        row.ReviewedAt = now;
        row.ReviewedByAdminId = adminId;
        row.AdminNotes = notes;
        row.UpdatedAt = now;
        row.AccessGrantedSubscriptionId = await _db.Subscriptions
            .Where(s => s.UserId == row.UserId)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        await _db.SaveChangesAsync(ct);

        // Webhook-driven completion is preferred for production; for manual
        // payments the admin click *is* the completion event, so the existing
        // LearnerService.ApplyCheckoutCompletionAsync wire-up runs against the
        // synthetic transaction on the next webhook poll. The synthetic
        // transaction's QuoteId + Status=completed cause the existing webhook
        // worker to re-apply the subscription activation idempotently.

        return row;
    }

    public async Task<ManualPaymentRequest> RejectAsync(string requestId, string adminId, string reason, CancellationToken ct)
    {
        var row = await _db.ManualPaymentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Manual payment request not found.");

        if (row.Status is "approved" or "rejected")
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
}
