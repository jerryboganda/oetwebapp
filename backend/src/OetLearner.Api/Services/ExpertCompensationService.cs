using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ExpertCompensationService(LearnerDbContext db, ILogger<ExpertCompensationService> logger)
{
    private const int MaxPageSize = 100;

    public async Task<ExpertCompensationSummaryResponse> GetCompensationSummaryAsync(string expertId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertId, ct);

        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

        var allEarnings = await db.Set<ExpertEarning>()
            .AsNoTracking()
            .Where(e => e.ExpertId == expertId)
            .ToListAsync(ct);

        var pending = (long)allEarnings.Where(e => e.Status == "pending").Sum(e => (decimal)e.AmountMinorUnits);
        var paidThisMonth = (long)allEarnings.Where(e => e.Status == "paid" && e.PaidOutAt >= monthStart).Sum(e => (decimal)e.AmountMinorUnits);
        var lifetime = (long)allEarnings.Where(e => e.Status == "paid").Sum(e => (decimal)e.AmountMinorUnits);
        var completedThisMonth = allEarnings.Count(e => e.EarnedAt >= monthStart);

        var pendingPayouts = await db.Set<ExpertPayout>()
            .AsNoTracking()
            .CountAsync(p => p.ExpertId == expertId && p.Status == "pending", ct);

        var currency = allEarnings.FirstOrDefault()?.Currency ?? "GBP";

        return new ExpertCompensationSummaryResponse(
            pending,
            paidThisMonth,
            lifetime,
            currency,
            completedThisMonth,
            pendingPayouts);
    }

    public async Task<ExpertEarningsHistoryResponse> GetEarningsHistoryAsync(string expertId, int page, int pageSize, CancellationToken ct)
    {
        await EnsureExpertAsync(expertId, ct);

        page = page > 0 ? page : 1;
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Set<ExpertEarning>()
            .AsNoTracking()
            .Where(e => e.ExpertId == expertId)
            .OrderByDescending(e => e.EarnedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExpertEarningItemResponse(
                e.Id,
                e.ReviewRequestId,
                "unknown",
                e.AmountMinorUnits,
                e.Currency,
                e.Status,
                e.EarnedAt))
            .ToListAsync(ct);

        return new ExpertEarningsHistoryResponse(items, totalCount, page, pageSize);
    }

    public async Task<ExpertPayoutsResponse> GetPayoutsAsync(string expertId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertId, ct);

        var payouts = await db.Set<ExpertPayout>()
            .AsNoTracking()
            .Where(p => p.ExpertId == expertId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ExpertPayoutItemResponse(
                p.Id,
                p.TotalAmountMinorUnits,
                p.Currency,
                p.Status,
                p.CreatedAt,
                p.ApprovedAt))
            .ToListAsync(ct);

        return new ExpertPayoutsResponse(payouts, payouts.Count);
    }

    public async Task RecordEarningAsync(string expertId, string reviewRequestId, long amountMinorUnits, string currency, CancellationToken ct)
    {
        var earning = new ExpertEarning
        {
            Id = Guid.NewGuid().ToString("N")[..32],
            ExpertId = expertId,
            ReviewRequestId = reviewRequestId,
            AmountMinorUnits = amountMinorUnits,
            Currency = currency,
            Status = "pending",
            EarnedAt = DateTimeOffset.UtcNow
        };
        db.Set<ExpertEarning>().Add(earning);
        logger.LogInformation("Recorded earning for expert {ExpertId}, review {ReviewRequestId}: {Amount} {Currency}",
            expertId, reviewRequestId, amountMinorUnits, currency);
    }

    private async Task EnsureExpertAsync(string expertId, CancellationToken ct)
    {
        var exists = await db.ExpertUsers.AsNoTracking()
            .AnyAsync(e => e.Id == expertId && e.IsActive, ct);
        if (!exists)
            throw ApiException.Forbidden("expert_not_active", "Expert profile not found or inactive.");
    }
}
