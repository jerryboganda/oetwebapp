using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.Review;
using OetLearner.Api.Services.Seeding;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Services.AiPipeline;

public sealed record PipelineBucketUsage(string Bucket, long Calls, long Successes, decimal CostUsd);

public sealed record PipelineProviderUsage(
    string ProviderId,
    long Calls,
    long Successes,
    long Failures,
    long PromptTokens,
    long CompletionTokens,
    decimal CostUsd,
    IReadOnlyList<PipelineBucketUsage> Stages);

public sealed record PipelineCostPerUnit(long Count, decimal TotalUsd, decimal? AvgUsd);

public sealed record PipelineLiveVoiceUsage(string Provider, long Sessions);

public sealed record PipelineClaudeMaxSnapshot(
    double? UtilizationPct,
    DateTimeOffset? ResetsAt,
    string Source,
    long WeeklyTokensUsed,
    long WeeklyTokenCap,
    long WritingTokens7d,
    long SpeakingTokens7d);

public sealed record PipelineReviewerQueueItem(
    string AssessmentType,
    long Arrived,
    long InFlight,
    long CodexTotal,
    long CodexSuccess,
    long CodexQuota,
    long CodexTimeout,
    long ApiFallbacks,
    long Completed,
    string? LastFallbackReason);

public sealed record PipelineCodexReviewerSnapshot(
    string Source,
    long RequestsThisWeek,
    long InputTokensThisWeek,
    long OutputTokensThisWeek,
    double? UtilizationPct,
    DateTimeOffset? WeekStartedAt,
    IReadOnlyList<PipelineReviewerQueueItem> Queue);

public sealed record PipelineCreditGrantView(
    string Id,
    string ProviderCode,
    decimal GrantUsd,
    DateTimeOffset StartsAt,
    string? Note,
    decimal SpentSinceStartUsd,
    decimal? RemainingUsd,
    string CreatedByAdminName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Everything the AI Pipeline Control Center usage dashboard needs for ONE window, in one payload.
/// All numbers are internally tracked — the exact call counts from <see cref="AiUsageRecord"/>, USD
/// from the stored rate-card estimates, subscription figures from the sidecars' own counters. The
/// UI labels them accordingly; nothing here claims provider verification.
/// </summary>
public sealed record PipelineOverview(
    string Window,
    DateTimeOffset? WindowStart,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PipelineProviderUsage> Providers,
    IReadOnlyList<PipelineBucketUsage> StageTotals,
    PipelineCostPerUnit WritingLetter,
    PipelineCostPerUnit SpeakingAssessment,
    PipelineCostPerUnit ReviewerRun,
    IReadOnlyList<PipelineLiveVoiceUsage> LiveVoiceSessions,
    PipelineClaudeMaxSnapshot ClaudeMax,
    PipelineCodexReviewerSnapshot CodexReviewer,
    IReadOnlyList<PipelineCreditGrantView> Credits);

public interface IAiPipelineOverviewService
{
    Task<PipelineOverview> BuildAsync(string? window, CancellationToken ct);

    /// <summary>Drops the cached payloads after a credit-grant write so the next read recomputes.</summary>
    void InvalidateCache();
}

/// <summary>
/// Aggregates <see cref="AiUsageRecord"/> rows, the subscription sidecar gauges, the shared reviewer
/// queue counters, the live-voice session audit and the operator credit grants into the dashboard
/// payload. Singleton with a 60-second cache per window: the dashboard is a monitoring surface, not
/// the run path, and every figure it shows already lags at most a write.
/// </summary>
public sealed class AiPipelineOverviewService(
    IServiceScopeFactory scopeFactory,
    IWritingSubscriptionQuotaService claudeQuota,
    ICodexSubscriptionQuotaService codexQuota,
    TimeProvider clock,
    ILogger<AiPipelineOverviewService> logger) : IAiPipelineOverviewService
{
    public static readonly string[] Windows = ["today", "7d", "30d", "all"];

    private static readonly ConcurrentDictionary<string, (PipelineOverview Overview, DateTimeOffset At)> Cache = new();

    public async Task<PipelineOverview> BuildAsync(string? window, CancellationToken ct)
    {
        var key = Normalize(window);
        var now = clock.GetUtcNow();
        if (Cache.TryGetValue(key, out var hit) && now - hit.At < TimeSpan.FromSeconds(60))
            return hit.Overview;

        var overview = await BuildUncachedAsync(key, now, ct);
        Cache[key] = (overview, now);
        return overview;
    }

    private static string Normalize(string? window) =>
        Windows.Contains(window, StringComparer.OrdinalIgnoreCase) ? window!.ToLowerInvariant() : "7d";

    private static int OrderOf(string bucket)
    {
        for (var i = 0; i < AiUsageStageBuckets.All.Count; i++)
        {
            if (string.Equals(AiUsageStageBuckets.All[i], bucket, StringComparison.Ordinal)) return i;
        }
        return int.MaxValue;
    }

    /// <summary>Drops the cached payloads after a credit-grant write so the next read recomputes.</summary>
    public void InvalidateCache() => Cache.Clear();

    private async Task<PipelineOverview> BuildUncachedAsync(string window, DateTimeOffset now, CancellationToken ct)
    {
        var start = window switch
        {
            "today" => new DateTimeOffset(now.Date, TimeSpan.Zero),
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            _ => (DateTimeOffset?)null,
        };

        var providers = await ProviderUsageAsync(start, ct);
        var stageTotals = TotalPerBucket(providers);
        var (writing, speaking, reviewer) = CostPerUnit(providers);
        var liveVoice = await LiveVoiceAsync(start, now, ct);
        var claudeMax = await ClaudeMaxAsync(providers, now, ct);
        var codex = await CodexAsync(ct);
        var credits = await CreditsAsync(ct);

        logger.LogDebug(
            "AI pipeline overview built (window={Window}): {Providers} providers, {Calls} calls, ${Cost}.",
            window, providers.Count, providers.Sum(p => p.Calls), providers.Sum(p => p.CostUsd));

        return new PipelineOverview(
            Window: window,
            WindowStart: start,
            GeneratedAt: now,
            Providers: providers,
            StageTotals: stageTotals,
            WritingLetter: writing,
            SpeakingAssessment: speaking,
            ReviewerRun: reviewer,
            LiveVoiceSessions: liveVoice,
            ClaudeMax: claudeMax,
            CodexReviewer: codex,
            Credits: credits);
    }

    // One grouped query over the window: (provider, feature) → calls/tokens/cost. Everything else
    // (per-provider rows, per-stage splits, cost per unit) aggregates in memory via the bucket map.
    private async Task<List<PipelineProviderUsage>> ProviderUsageAsync(DateTimeOffset? start, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var q = db.AiUsageRecords.AsNoTracking();
        if (start is { } s) q = q.Where(r => r.CreatedAt >= s);

        var rows = await q
            .Where(r => r.ProviderId != null)
            .GroupBy(r => new { r.ProviderId, r.FeatureCode })
            .Select(g => new
            {
                ProviderId = g.Key.ProviderId!,
                g.Key.FeatureCode,
                Calls = (long)g.Count(),
                Successes = (long)g.Count(x => x.Outcome == AiCallOutcome.Success),
                PromptTokens = (long)g.Sum(x => x.PromptTokens),
                CompletionTokens = (long)g.Sum(x => x.CompletionTokens),
                CostUsd = g.Sum(x => x.CalculatedCostUsd ?? x.CostEstimateUsd),
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ProviderId, StringComparer.Ordinal)
            .Select(g =>
            {
                var stages = g
                    .GroupBy(r => AiUsageStageBuckets.For(r.FeatureCode), StringComparer.Ordinal)
                    .Select(b => new PipelineBucketUsage(
                        Bucket: b.Key,
                        Calls: b.Sum(x => x.Calls),
                        Successes: b.Sum(x => x.Successes),
                        CostUsd: b.Sum(x => x.CostUsd)))
                    .OrderBy(b => OrderOf(b.Bucket))
                    .ToList();
                return new PipelineProviderUsage(
                    ProviderId: g.Key,
                    Calls: g.Sum(x => x.Calls),
                    Successes: g.Sum(x => x.Successes),
                    Failures: g.Sum(x => x.Calls - x.Successes),
                    PromptTokens: g.Sum(x => x.PromptTokens),
                    CompletionTokens: g.Sum(x => x.CompletionTokens),
                    CostUsd: g.Sum(x => x.CostUsd),
                    Stages: stages);
            })
            .OrderByDescending(p => p.CostUsd)
            .ThenByDescending(p => p.Calls)
            .ToList();
    }

    private static List<PipelineBucketUsage> TotalPerBucket(List<PipelineProviderUsage> providers) =>
        providers
            .SelectMany(p => p.Stages)
            .GroupBy(s => s.Bucket, StringComparer.Ordinal)
            .Select(g => new PipelineBucketUsage(g.Key, g.Sum(s => s.Calls), g.Sum(s => s.Successes), g.Sum(s => s.CostUsd)))
            .OrderBy(s => OrderOf(s.Bucket))
            .ToList();

    private static (PipelineCostPerUnit Writing, PipelineCostPerUnit Speaking, PipelineCostPerUnit Reviewer) CostPerUnit(
        List<PipelineProviderUsage> providers)
    {
        static PipelineBucketUsage? Bucket(List<PipelineProviderUsage> ps, string bucket) =>
            ps.SelectMany(p => p.Stages).Where(s => s.Bucket == bucket)
                .Aggregate((PipelineBucketUsage?)null, (acc, s) => acc is null
                    ? s
                    : new PipelineBucketUsage(bucket, acc!.Calls + s.Calls, acc.Successes + s.Successes, acc.CostUsd + s.CostUsd));

        static PipelineCostPerUnit Per(PipelineBucketUsage? costBuckets, long successCount)
        {
            var total = costBuckets?.CostUsd ?? 0m;
            return new PipelineCostPerUnit(successCount, total, successCount > 0 ? Math.Round(total / successCount, 4) : null);
        }

        var writingGrade = Bucket(providers, AiUsageStageBuckets.WritingGrade);
        var speakingGrade = Bucket(providers, AiUsageStageBuckets.SpeakingGrade);
        var speakingAudio = Bucket(providers, AiUsageStageBuckets.SpeakingAudio);
        var review = Sum(Bucket(providers, AiUsageStageBuckets.WritingReview), Bucket(providers, AiUsageStageBuckets.SpeakingReview), AiUsageStageBuckets.WritingReview);

        // One letter's cost = everything the grading chain spent on the writing-grade bucket;
        // one assessment = the text grade plus its acoustic half; one review = both reviewers.
        var writing = Per(writingGrade, writingGrade?.Successes ?? 0);
        var speakingCost = Sum(speakingGrade, speakingAudio, AiUsageStageBuckets.SpeakingGrade);
        var speaking = Per(speakingCost, speakingGrade?.Successes ?? 0);
        var reviewer = Per(review, review?.Successes ?? 0);
        return (writing, speaking, reviewer);
    }

    private static PipelineBucketUsage? Sum(PipelineBucketUsage? a, PipelineBucketUsage? b, string bucket) =>
        (a, b) switch
        {
            (null, null) => null,
            (_, null) => a,
            (null, _) => b,
            (_, _) => new PipelineBucketUsage(bucket, a!.Calls + b!.Calls, a.Successes + b.Successes, a.CostUsd + b.CostUsd),
        };

    // Live voice sessions are the provider-session audit rows the mint path persists
    // (Role == live_session, Text == "{provider}:{model}"); the realtime audio itself never
    // flows through AiUsageRecord, so token/USD figures would be fabricated — sessions only.
    private async Task<List<PipelineLiveVoiceUsage>> LiveVoiceAsync(DateTimeOffset? start, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var q = db.SpeakingPatientTurns.AsNoTracking()
            .Where(t => t.Role == LiveVoiceService.LiveVoiceSessionRole);
        // "all" gets a sane cap: the session audit is rolling-history, not an accounting ledger.
        var effectiveStart = start ?? now.AddDays(-365);
        var rows = await q
            .Where(t => t.CreatedAt >= effectiveStart)
            .OrderByDescending(t => t.CreatedAt)
            .Take(20000)
            .Select(t => new { t.SessionId, t.Text })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var sep = r.Text.IndexOf(':');
                return new { SessionId = r.SessionId, Provider = sep > 0 ? r.Text[..sep] : r.Text };
            })
            .GroupBy(x => x.Provider, StringComparer.Ordinal)
            .Select(g => new PipelineLiveVoiceUsage(g.Key, g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).LongCount()))
            .OrderByDescending(x => x.Sessions)
            .ToList();
    }

    private async Task<PipelineClaudeMaxSnapshot> ClaudeMaxAsync(List<PipelineProviderUsage> providers, DateTimeOffset now, CancellationToken ct)
    {
        var snapshot = await claudeQuota.GetSnapshotAsync(ct);
        if (snapshot.Source == "unknown")
        {
            return new PipelineClaudeMaxSnapshot(null, null, snapshot.Source, snapshot.WeeklyTokensUsed, snapshot.WeeklyTokenCap, 0, 0);
        }

        // Writing-vs-Speaking split is always the trailing 7 days, regardless of the selected window.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var weekAgo = now.AddDays(-7);
        var split = await db.AiUsageRecords.AsNoTracking()
            .Where(r => r.ProviderId == WritingSubscriptionProviderDefaults.ClaudeCode && r.CreatedAt >= weekAgo)
            .GroupBy(r => r.FeatureCode)
            .Select(g => new { g.Key, Tokens = (long)g.Sum(x => x.PromptTokens) + g.Sum(x => x.CompletionTokens) })
            .ToListAsync(ct);
        long writing = 0, speaking = 0;
        foreach (var row in split)
        {
            switch (AiUsageStageBuckets.For(row.Key))
            {
                case AiUsageStageBuckets.WritingGrade: writing += row.Tokens; break;
                case AiUsageStageBuckets.SpeakingGrade: speaking += row.Tokens; break;
            }
        }

        return new PipelineClaudeMaxSnapshot(
            snapshot.UtilizationPct,
            snapshot.ResetsAt,
            snapshot.Source,
            snapshot.WeeklyTokensUsed,
            snapshot.WeeklyTokenCap,
            writing,
            speaking);
    }

    private async Task<PipelineCodexReviewerSnapshot> CodexAsync(CancellationToken ct)
    {
        var snapshot = await codexQuota.GetSnapshotAsync(ct);
        var queue = ReviewerQueueMetrics.Instance.Snapshot().Items
            .Select(i => new PipelineReviewerQueueItem(
                i.AssessmentType, i.Arrived, i.InFlight, i.CodexTotal, i.CodexSuccess,
                i.CodexQuota, i.CodexTimeout, i.ApiFallbacks, i.TotalCompleted, i.LastFallbackReason))
            .ToList();
        return new PipelineCodexReviewerSnapshot(
            snapshot.Source,
            snapshot.RequestsThisWeek,
            snapshot.InputTokensThisWeek,
            snapshot.OutputTokensThisWeek,
            snapshot.UtilizationPct,
            snapshot.WeekStartedAt,
            queue);
    }

    private async Task<List<PipelineCreditGrantView>> CreditsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var grants = await db.AiCreditGrants.AsNoTracking()
            .OrderByDescending(g => g.StartsAt)
            .ToListAsync(ct);

        var views = new List<PipelineCreditGrantView>(grants.Count);
        foreach (var grant in grants)
        {
            // One indexed sum per grant; grants are operator-entered and stay in the dozens at most.
            var spent = await db.AiUsageRecords.AsNoTracking()
                .Where(r => r.ProviderId == grant.ProviderCode && r.CreatedAt >= grant.StartsAt)
                .SumAsync(r => (decimal?)(r.CalculatedCostUsd ?? r.CostEstimateUsd), ct) ?? 0m;
            var remaining = grant.GrantUsd - spent;
            views.Add(new PipelineCreditGrantView(
                grant.Id,
                grant.ProviderCode,
                grant.GrantUsd,
                grant.StartsAt,
                grant.Note,
                Math.Round(spent, 4),
                remaining,
                grant.CreatedByAdminName ?? string.Empty,
                grant.CreatedAt));
        }

        return views;
    }
}
