using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

/// <summary>Per-feature AI quality numbers for the ops dashboard (F-126/127).</summary>
public sealed record CompanionFeatureQualityRow(
    string FeatureCode,
    int TotalCalls,
    int Failures,
    decimal FailureRatePct,
    int P50LatencyMs,
    int P95LatencyMs,
    decimal CostUsd,
    IReadOnlyList<CompanionErrorCount> TopErrors);

public sealed record CompanionErrorCount(string ErrorCode, int Count);

/// <summary>Source-coverage state for one profession/subtest slice (F-128).</summary>
public sealed record CompanionCoverageRow(
    string? ProfessionId,
    string? SubtestCode,
    int Approved,
    int Draft,
    int Retired,
    int TotalChunks);

/// <summary>AI programme cost by day (F-132 companion slice).</summary>
public sealed record CompanionCostDayRow(string Day, decimal CostUsd, int Calls, int FailureCount);

/// <summary>
/// Companion operations reporting (SAMI §13.2, F-126..F-129/F-132 companion slice):
/// quality from real <see cref="AiUsageRecord"/> outcomes (failures, error classes,
/// latency, cost), knowledge coverage from <see cref="CompanionSource"/> states, and
/// the handoff queue depth. Numbers are measured, never asserted: the report states
/// what it counts and does not pretend to detect semantic hallucination.
/// </summary>
public interface ICompanionOpsService
{
    Task<IReadOnlyList<CompanionFeatureQualityRow>> QualityAsync(int days, int take, CancellationToken ct);

    Task<IReadOnlyList<CompanionCoverageRow>> CoverageAsync(CancellationToken ct);

    Task<IReadOnlyList<CompanionCostDayRow>> CostByDayAsync(int days, CancellationToken ct);

    Task<object> SnapshotAsync(CancellationToken ct);
}

public sealed class CompanionOpsService(LearnerDbContext db, TimeProvider clock) : ICompanionOpsService
{
    public async Task<IReadOnlyList<CompanionFeatureQualityRow>> QualityAsync(int days, int take, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 90));
        var rows = await db.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAt >= since && r.FeatureCode.StartsWith("companion") || r.FeatureCode.StartsWith("ai_assistant"))
            .GroupBy(r => r.FeatureCode)
            .Select(g => new
            {
                FeatureCode = g.Key,
                Total = g.Count(),
                Failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                Cost = g.Sum(x => x.CostEstimateUsd),
                Latencies = g.Select(x => x.LatencyMs).ToList(),
                Errors = g.Where(x => x.Outcome != AiCallOutcome.Success && x.ErrorCode != null)
                    .GroupBy(x => x.ErrorCode!)
                    .Select(eg => new { Code = eg.Key, Count = eg.Count() })
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var sorted = r.Latencies.OrderBy(x => x).ToList();
                int P(int pct) => sorted.Count == 0
                    ? 0
                    : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * pct / 100.0) - 1)];
                return new CompanionFeatureQualityRow(
                    r.FeatureCode,
                    r.Total,
                    r.Failures,
                    r.Total == 0 ? 0 : Math.Round(r.Failures * 100m / r.Total, 2),
                    P(50),
                    P(95),
                    Math.Round(r.Cost, 4),
                    r.Errors.OrderByDescending(e => e.Count).Take(5)
                        .Select(e => new CompanionErrorCount(e.Code, e.Count)).ToList());
            })
            .OrderByDescending(r => r.Failures)
            .ThenByDescending(r => r.TotalCalls)
            .Take(Math.Clamp(take, 1, 50))
            .ToList();
    }

    public async Task<IReadOnlyList<CompanionCoverageRow>> CoverageAsync(CancellationToken ct)
        => await db.CompanionSources.AsNoTracking()
            .GroupBy(s => new { s.ProfessionId, s.SubtestCode })
            .Select(g => new CompanionCoverageRow(
                g.Key.ProfessionId,
                g.Key.SubtestCode,
                g.Count(x => x.State == CompanionSourceState.Approved),
                g.Count(x => x.State == CompanionSourceState.Draft),
                g.Count(x => x.State == CompanionSourceState.Retired),
                g.Count()))
            .OrderBy(r => r.ProfessionId)
            .ThenBy(r => r.SubtestCode)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompanionCostDayRow>> CostByDayAsync(int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 90));
        var rows = await db.AiUsageRecords.AsNoTracking()
            .Where(r => r.CreatedAt >= since)
            .GroupBy(r => r.PeriodDayKey)
            .Select(g => new
            {
                Day = g.Key,
                Cost = g.Sum(x => x.CostEstimateUsd),
                Calls = g.Count(),
                Failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
            })
            .ToListAsync(ct);
        return rows
            .OrderBy(r => r.Day)
            .Select(r => new CompanionCostDayRow(r.Day, Math.Round(r.Cost, 4), r.Calls, r.Failures))
            .ToList();
    }

    public async Task<object> SnapshotAsync(CancellationToken ct)
    {
        var quality = await QualityAsync(30, 15, ct);
        var coverage = await CoverageAsync(ct);
        var since = clock.GetUtcNow().AddDays(-30);
        var handoffDepth = await db.CompanionHandoffs.AsNoTracking()
            .Where(h => h.Status != "resolved" && h.CreatedAt >= since)
            .CountAsync(ct);
        var handoffByRoute = await db.CompanionHandoffs.AsNoTracking()
            .Where(h => h.Status != "resolved" && h.CreatedAt >= since)
            .GroupBy(h => h.Route)
            .Select(g => new { Route = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var totalCalls = quality.Sum(q => q.TotalCalls);
        var totalFailures = quality.Sum(q => q.Failures);
        return new
        {
            generatedAt = clock.GetUtcNow(),
            windowDays = 30,
            totalCalls,
            totalFailures,
            failureRatePct = totalCalls == 0 ? 0 : Math.Round(totalFailures * 100m / totalCalls, 2),
            quality,
            coverage = new
            {
                slices = coverage,
                approvedTotal = coverage.Sum(c => c.Approved),
                draftTotal = coverage.Sum(c => c.Draft),
                professionsWithoutApprovedSources = coverage
                    .GroupBy(c => c.ProfessionId)
                    .Count(g => g.Sum(x => x.Approved) == 0),
            },
            handoffs = new
            {
                openLast30Days = handoffDepth,
                byRoute = handoffByRoute.Select(h => new { route = h.Route, count = h.Count }),
            },
            note = "Counts are measured from AiUsageRecord outcomes and CompanionSource states. Semantic answer quality (hallucination) is not claimed here; the calibration programme owns that.",
        };
    }
}
