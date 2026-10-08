using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Weekly utilisation snapshot of the dedicated ChatGPT/Codex subscription that backs the shared
/// reviewer (Writing + Speaking) and the Writing fallback (owner directive 2026-10-09, phase 2).
/// Mirrors <see cref="WritingSubscriptionQuotaService"/> but reads the codex sidecar
/// (<c>oet-writing-codex</c>), whose <c>GET /usage</c> counts requests/tokens since the rolling
/// week started — consumer subscriptions expose no official usage API, so this counter plus the
/// <c>AiUsageRecord</c> rows are the only honest sources. Singleton, 5-minute cache, quiet
/// degradation to <c>unknown</c> when the sidecar is unreachable.
/// </summary>
public interface ICodexSubscriptionQuotaService
{
    Task<CodexQuotaSnapshot> GetSnapshotAsync(CancellationToken ct);
}

public sealed record CodexQuotaSnapshot(
    double? UtilizationPct,      // 0..100, null when no weekly cap is configured
    DateTimeOffset? WeekStartedAt,
    string Source,               // "reported" | "unknown"
    long RequestsThisWeek,
    long InputTokensThisWeek,
    long OutputTokensThisWeek,
    DateTimeOffset SampledAt);

public sealed class CodexSubscriptionQuotaService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<CodexSubscriptionQuotaService> logger) : ICodexSubscriptionQuotaService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(8);

    private CodexQuotaSnapshot? _cached;
    private DateTimeOffset _cachedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string SidecarBaseUrl =>
        configuration["WritingAi:CodexSidecarBaseUrl"] ?? "http://oet-writing-codex:8080";

    public async Task<CodexQuotaSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var snapshot = _cached;
        if (snapshot is not null && now - _cachedAt < PollInterval)
            return snapshot;

        await _gate.WaitAsync(ct);
        try
        {
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

    private async Task<CodexQuotaSnapshot> SampleAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("WritingAiSidecar");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PollTimeout);
            var dto = await client.GetFromJsonAsync<CodexSidecarUsageDto>(
                $"{SidecarBaseUrl.TrimEnd('/')}/usage", cts.Token);
            if (dto is null) return Unknown(now);

            long inTok = dto.inputTokensThisWeek;
            long outTok = dto.outputTokensThisWeek;
            double? pct = dto.utilisation is { } u ? Math.Clamp(u, 0, 1) * 100.0 : null;
            return new CodexQuotaSnapshot(
                UtilizationPct: pct,
                WeekStartedAt: DateTimeOffset.TryParse(dto.weekStartedAt, out var w) ? w : null,
                // The sidecar computed these from its own live counters; we surface them verbatim.
                Source: "reported",
                RequestsThisWeek: dto.requestsThisWeek,
                InputTokensThisWeek: inTok,
                OutputTokensThisWeek: outTok,
                SampledAt: now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Codex sidecar /usage probe failed; reviewer gauge reports unknown.");
            return Unknown(now);
        }
    }

    private static CodexQuotaSnapshot Unknown(DateTimeOffset now) =>
        new(null, null, "unknown", 0, 0, 0, now);

    // Matches writing-ai-sidecars/codex/server.mjs GET /usage.
    private sealed class CodexSidecarUsageDto
    {
        public double? utilisation { get; set; }
        public string? weekStartedAt { get; set; }
        public long requestsThisWeek { get; set; }
        public long inputTokensThisWeek { get; set; }
        public long outputTokensThisWeek { get; set; }
        public long? weeklyTokenCap { get; set; }
        public string? source { get; set; }
    }
}
