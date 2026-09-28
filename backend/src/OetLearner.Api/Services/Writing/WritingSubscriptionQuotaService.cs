using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Tracks weekly utilisation of the dedicated Claude Max 5x subscription that
/// backs Writing grading. The subscription CLI cannot report a raw "allowance
/// remaining" counter, so utilisation is derived two ways, preferring the
/// sidecar's own counter and falling back to a local estimate from
/// <see cref="AiUsageRecord"/>:
///
/// <list type="bullet">
///   <item><b>reported</b> — the claude sidecar's <c>GET /usage</c> returns a
///   utilisation fraction computed against an operator-set weekly token cap
///   (<c>WRITING_CLAUDE_WEEKLY_TOKEN_CAP</c>). Used verbatim when non-null.</item>
///   <item><b>estimated</b> — sum of tokens billed to the
///   <c>writing-claude-sub</c> provider over the trailing 7 days divided by a
///   conservative weekly token budget. The floor never blocks on its own when
///   no cap is configured anywhere; it only feeds the admin gauge.</item>
/// </list>
///
/// Quota <i>exhaustion</i> failover never depends on this estimate: when Claude
/// actually refuses, the sidecar returns a machine-readable <c>quota_exceeded</c>
/// signal and the selector fails over on that hard signal regardless of the
/// percentage. The percentage only drives the 80%/90% warn/pre-emptive-failover
/// bands the owner asked for.
/// </summary>
public interface IWritingSubscriptionQuotaService
{
    Task<WritingQuotaSnapshot> GetSnapshotAsync(CancellationToken ct);
}

public sealed record WritingQuotaSnapshot(
    double? UtilizationPct,      // 0..100, null when unknown
    DateTimeOffset? ResetsAt,
    string Source,               // "reported" | "estimated" | "unknown"
    long WeeklyTokensUsed,
    long WeeklyTokenCap,
    DateTimeOffset SampledAt);

public sealed class WritingSubscriptionQuotaService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<WritingSubscriptionQuotaService> logger) : IWritingSubscriptionQuotaService
{
    // Sidecar usage probe cadence. Cheap internal GET; 5 min is fresh enough for
    // a weekly allowance gauge and keeps the sidecar idle.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(8);

    private WritingQuotaSnapshot? _cached;
    private DateTimeOffset _cachedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long WeeklyTokenCap =>
        long.TryParse(configuration["WritingAi:ClaudeWeeklyTokenCap"], out var cap) && cap > 0
            ? cap
            : 0;

    public async Task<WritingQuotaSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var snapshot = _cached;
        if (snapshot is not null && now - _cachedAt < PollInterval)
            return snapshot;

        await _gate.WaitAsync(ct);
        try
        {
            // Double-checked after taking the lock.
            snapshot = _cached;
            now = clock.GetUtcNow();
            if (snapshot is not null && now - _cachedAt < PollInterval)
                return snapshot;

            snapshot = await SampleAsync(now, ct);
            _cached = snapshot;
            _cachedAt = now;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WritingQuotaSnapshot> SampleAsync(DateTimeOffset now, CancellationToken ct)
    {
        // 1) Prefer the sidecar's own counter when it reports a real utilisation.
        var sidecar = await TryReadSidecarUsageAsync(ct);
        if (sidecar?.Utilization is double u)
        {
            return new WritingQuotaSnapshot(
                UtilizationPct: u * 100.0,
                ResetsAt: sidecar.ResetsAt,
                Source: "reported",
                WeeklyTokensUsed: sidecar.WeeklyTokensUsed,
                WeeklyTokenCap: sidecar.WeeklyTokenCap,
                SampledAt: now);
        }

        // 2) Fall back to a local estimate from billed usage rows.
        return await EstimateFromUsageRecordsAsync(now, ct);
    }

    private async Task<SidecarUsage?> TryReadSidecarUsageAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = configuration["WritingAi:ClaudeSidecarBaseUrl"] ?? "http://oet-writing-claude:8080";
            var client = httpClientFactory.CreateClient("WritingAiSidecar");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PollTimeout);
            var dto = await client.GetFromJsonAsync<SidecarUsageDto>(
                $"{baseUrl.TrimEnd('/')}/usage", cts.Token);
            if (dto is null) return null;
            return new SidecarUsage(
                Utilization: dto.utilisation,
                ResetsAt: DateTimeOffset.TryParse(dto.resetsAt, out var r) ? r : null,
                WeeklyTokensUsed: (long)(dto.inputTokensThisWeek + dto.outputTokensThisWeek),
                WeeklyTokenCap: dto.weeklyTokenCap ?? 0);
        }
        catch (Exception ex)
        {
            // Sidecar down / unreachable is normal before first deploy — degrade
            // quietly to the local estimate instead of failing the gauge.
            logger.LogDebug(ex, "Writing Claude sidecar /usage probe failed; falling back to local estimate.");
            return null;
        }
    }

    private async Task<WritingQuotaSnapshot> EstimateFromUsageRecordsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cap = WeeklyTokenCap;
        var weekAgo = now.AddDays(-7);
        long used = 0;
        try
        {
            // Singleton service → resolve the scoped DbContext per sample.
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            used = await db.AiUsageRecords.AsNoTracking()
                .Where(r => r.ProviderId == WritingSubscriptionProviders.Claude && r.CreatedAt >= weekAgo)
                .SumAsync(r => (long)r.PromptTokens + r.CompletionTokens, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Writing quota local estimate query failed.");
        }

        return new WritingQuotaSnapshot(
            UtilizationPct: cap > 0 ? Math.Min(100.0, used * 100.0 / cap) : null,
            ResetsAt: null,
            Source: cap > 0 ? "estimated" : "unknown",
            WeeklyTokensUsed: used,
            WeeklyTokenCap: cap,
            SampledAt: now);
    }

    private sealed record SidecarUsage(double? Utilization, DateTimeOffset? ResetsAt, long WeeklyTokensUsed, long WeeklyTokenCap);

    // Matches writing-ai-sidecars/claude/server.mjs GET /usage.
    private sealed class SidecarUsageDto
    {
        public double? utilisation { get; set; }
        public string? resetsAt { get; set; }
        public long inputTokensThisWeek { get; set; }
        public long outputTokensThisWeek { get; set; }
        public long? weeklyTokenCap { get; set; }
    }
}
