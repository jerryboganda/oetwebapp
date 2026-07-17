using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// Phase 8 affiliate / agent program. Handles attribution, commission accrual,
/// and refund-driven reversal.
/// </summary>
public interface IAffiliateService
{
    Task<Affiliate?> ResolveByCodeAsync(string code, CancellationToken ct);
    Task AttributeUserAsync(string userId, string affiliateCode, CancellationToken ct);
    Task<AffiliateCommission?> AccrueCommissionAsync(string userId, string paymentTransactionId, decimal amount, string currency, CancellationToken ct);
    Task ReverseCommissionAsync(string paymentTransactionId, CancellationToken ct);
    Task<AffiliatePayoutBatch> GeneratePayoutBatchAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct);
    Task<string> ExportPayoutCsvAsync(string batchId, CancellationToken ct);
}

public sealed record AffiliatePayoutBatch(
    string BatchId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int AffiliatesIncluded,
    int CommissionRows,
    decimal TotalAmount,
    Dictionary<string, decimal> ByCurrency);

public sealed class AffiliateService : IAffiliateService
{
    private readonly LearnerDbContext _db;

    public AffiliateService(LearnerDbContext db) => _db = db;

    public Task<Affiliate?> ResolveByCodeAsync(string code, CancellationToken ct)
        => _db.Affiliates.FirstOrDefaultAsync(a => a.Code == code && a.Status == "active", ct);

    public async Task AttributeUserAsync(string userId, string affiliateCode, CancellationToken ct)
    {
        var affiliate = await ResolveByCodeAsync(affiliateCode, ct);
        if (affiliate is null) return;

        var existing = await _db.AffiliateAttributions.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (existing is not null) return; // First-click wins.

        var now = DateTimeOffset.UtcNow;
        _db.AffiliateAttributions.Add(new AffiliateAttribution
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            AffiliateId = affiliate.Id,
            ClickedAt = now,
            AttributedAt = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AffiliateCommission?> AccrueCommissionAsync(string userId, string paymentTransactionId, decimal amount, string currency, CancellationToken ct)
    {
        var attribution = await _db.AffiliateAttributions.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (attribution is null) return null;

        var affiliate = await _db.Affiliates.FirstOrDefaultAsync(a => a.Id == attribution.AffiliateId, ct);
        if (affiliate is null || affiliate.Status != "active") return null;

        var existing = await _db.AffiliateCommissions.FirstOrDefaultAsync(c => c.PaymentTransactionId == paymentTransactionId, ct);
        if (existing is not null) return existing;

        var commissionAmount = decimal.Round(amount * affiliate.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var commission = new AffiliateCommission
        {
            Id = Guid.NewGuid().ToString("N"),
            AffiliateId = affiliate.Id,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AmountAmount = commissionAmount,
            Currency = currency.ToUpperInvariant(),
            Status = "accrued",
            AccruedAt = DateTimeOffset.UtcNow,
        };
        _db.AffiliateCommissions.Add(commission);

        if (attribution.ConvertedAt is null)
        {
            attribution.ConvertedAt = commission.AccruedAt;
            attribution.FirstPaymentTransactionId = paymentTransactionId;
        }

        await _db.SaveChangesAsync(ct);
        return commission;
    }

    public async Task ReverseCommissionAsync(string paymentTransactionId, CancellationToken ct)
    {
        var commission = await _db.AffiliateCommissions.FirstOrDefaultAsync(c => c.PaymentTransactionId == paymentTransactionId, ct);
        if (commission is null) return;
        if (commission.Status == "paid")
        {
            // Already paid out — admin must collect from affiliate; mark as reversed for audit.
        }
        commission.Status = "reversed";
        commission.ReversedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AffiliatePayoutBatch> GeneratePayoutBatchAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct)
    {
        var batchId = $"payout_{periodStart:yyyyMMdd}_{periodEnd:yyyyMMdd}_{Guid.NewGuid():N}".Substring(0, 64);

        var eligible = await _db.AffiliateCommissions
            .Where(c => c.Status == "accrued" && c.AccruedAt >= periodStart && c.AccruedAt <= periodEnd)
            .ToListAsync(ct);

        // Apply payout threshold per affiliate. Affiliates whose batch total
        // doesn't meet their PayoutThresholdAmount stay in 'accrued' for next cycle.
        var byAffiliate = eligible.GroupBy(c => c.AffiliateId).ToList();
        var affiliateThresholds = await _db.Affiliates
            .Where(a => byAffiliate.Select(g => g.Key).Contains(a.Id))
            .Select(a => new { a.Id, a.PayoutThresholdAmount })
            .ToListAsync(ct);
        var thresholdMap = affiliateThresholds.ToDictionary(a => a.Id, a => a.PayoutThresholdAmount);

        int affiliatesIncluded = 0;
        int rowsFlipped = 0;
        decimal total = 0m;
        var byCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in byAffiliate)
        {
            var groupTotal = group.Sum(c => c.AmountAmount);
            var threshold = thresholdMap.TryGetValue(group.Key, out var t) ? t : 0m;
            if (groupTotal < threshold) continue;

            affiliatesIncluded++;
            foreach (var commission in group)
            {
                commission.Status = "pending_payout";
                commission.PayoutBatchId = batchId;
                rowsFlipped++;
                total += commission.AmountAmount;
                if (byCurrency.ContainsKey(commission.Currency))
                    byCurrency[commission.Currency] += commission.AmountAmount;
                else
                    byCurrency[commission.Currency] = commission.AmountAmount;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new AffiliatePayoutBatch(batchId, periodStart, periodEnd, affiliatesIncluded, rowsFlipped, total, byCurrency);
    }

    public async Task<string> ExportPayoutCsvAsync(string batchId, CancellationToken ct)
    {
        var rows = await (
            from c in _db.AffiliateCommissions
            join a in _db.Affiliates on c.AffiliateId equals a.Id
            where c.PayoutBatchId == batchId
            select new
            {
                AffiliateCode = a.Code,
                OwnerName = a.OwnerName,
                ContactEmail = a.ContactEmail,
                PayoutMethod = a.PayoutMethod,
                CommissionId = c.Id,
                PaymentTransactionId = c.PaymentTransactionId,
                Amount = c.AmountAmount,
                Currency = c.Currency,
                Status = c.Status,
                AccruedAt = c.AccruedAt,
            }).ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("batch_id,affiliate_code,owner_name,contact_email,payout_method,commission_id,payment_transaction_id,amount,currency,status,accrued_at");
        foreach (var r in rows)
        {
            sb.Append(EscapeCsv(batchId)).Append(',')
              .Append(EscapeCsv(r.AffiliateCode)).Append(',')
              .Append(EscapeCsv(r.OwnerName)).Append(',')
              .Append(EscapeCsv(r.ContactEmail)).Append(',')
              .Append(EscapeCsv(r.PayoutMethod)).Append(',')
              .Append(EscapeCsv(r.CommissionId)).Append(',')
              .Append(EscapeCsv(r.PaymentTransactionId)).Append(',')
              .Append(r.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(EscapeCsv(r.Currency)).Append(',')
              .Append(EscapeCsv(r.Status)).Append(',')
              .Append(r.AccruedAt.ToString("O")).AppendLine();
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value is null) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
