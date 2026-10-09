using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>
/// Reads, for each seeded subscription account, its provider row, its admin drain flag and its
/// sidecar's <c>GET /usage</c> (owner directive 2026-10-10). Scoped: the singleton
/// <see cref="SubscriptionAccountPool"/> never touches a DbContext.
///
/// The switch percentage has an audited admin override. It is stored on the
/// <c>subscription_pool_switch_percent</c> FeatureFlag row (its <c>RolloutPercentage</c> field, 50-100):
/// an absent or disabled row keeps the built-in 95 % threshold from configuration.
/// </summary>
public sealed class SubscriptionAccountStateProvider(
    LearnerDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<SubscriptionAccountStateProvider> logger) : ISubscriptionAccountStateProvider
{
    public const string SwitchPercentFlagKey = "subscription_pool_switch_percent";
    public const string DrainFlagPrefix = "subscription_account_drain:";

    private const double DefaultSwitchPercent = 95;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<SubscriptionAccountSample> SampleAsync(CancellationToken ct)
    {
        var codes = WritingSubscriptionProviderDefaults.ClaudeGroupCodes
            .Concat(WritingSubscriptionProviderDefaults.CodexGroupCodes)
            .ToArray();

        var rows = await db.AiProviders.AsNoTracking()
            .Where(p => codes.Contains(p.Code))
            .Select(p => new { p.Code, p.Name, p.BaseUrl })
            .ToListAsync(ct);

        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Key == SwitchPercentFlagKey || f.Key.StartsWith(DrainFlagPrefix))
            .Select(f => new { f.Key, f.Enabled, f.RolloutPercentage, f.UpdatedAt })
            .ToListAsync(ct);

        var drained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in flags
            .Where(f => f.Key.StartsWith(DrainFlagPrefix, StringComparison.Ordinal))
            .GroupBy(f => f.Key, StringComparer.Ordinal))
        {
            // Newest row wins per key, read uncached (the reviewer's own flag convention).
            var latest = group.OrderByDescending(f => f.UpdatedAt).First();
            if (latest.Enabled) drained.Add(group.Key[DrainFlagPrefix.Length..]);
        }

        var switchFlag = flags
            .Where(f => f.Key == SwitchPercentFlagKey)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefault();
        var switchPercent = switchFlag is { Enabled: true } && switchFlag.RolloutPercentage is >= 50 and <= 100
            ? switchFlag.RolloutPercentage
            : DefaultSwitchPercent;

        var accounts = new List<SubscriptionAccountInfo>(rows.Count);
        foreach (var row in rows)
        {
            accounts.Add(await ReadAccountAsync(row.Code, row.Name, row.BaseUrl, drained.Contains(row.Code), ct));
        }

        // An account with no provider row yet (fresh deploy) is still polled, so the pool has state
        // for it the moment the seeder inserts the row.
        foreach (var code in codes.Where(code => rows.All(r => r.Code != code)))
        {
            accounts.Add(Unreachable(code, drained.Contains(code)));
        }

        return new SubscriptionAccountSample(switchPercent, TimeSpan.FromMinutes(5), accounts);
    }

    private async Task<SubscriptionAccountInfo> ReadAccountAsync(
        string code, string? name, string? baseUrl, bool drained, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Unreachable(code, drained);

        try
        {
            var client = httpClientFactory.CreateClient("WritingAiSidecar");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            var dto = await client.GetFromJsonAsync<SidecarUsageDto>(
                $"{baseUrl.TrimEnd('/')}/usage", timeout.Token);

            return new SubscriptionAccountInfo(
                code,
                name,
                baseUrl,
                drained,
                Reachable: dto is not null,
                Windows: WindowsOf(dto),
                LastQuotaErrorAt: Parse(dto?.lastQuotaErrorAt),
                LastQuotaErrorKind: dto?.lastQuotaErrorKind,
                LastQuotaErrorResetsAt: Parse(dto?.lastQuotaErrorResetsAt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreachable sidecar keeps its saved position (unknown is never "healthy" nor "dead"):
            // the pool treats it as no information and the reactive failover covers a real outage.
            logger.LogDebug(ex, "Subscription account {ProviderCode} /usage probe failed.", code);
            return Unreachable(code, drained);
        }
    }

    private static IReadOnlyList<SubscriptionAccountWindow> WindowsOf(SidecarUsageDto? dto)
    {
        if (dto?.windows is null) return [];
        var windows = new List<SubscriptionAccountWindow>(dto.windows.Count);
        foreach (var window in dto.windows)
        {
            if (window is null || string.IsNullOrWhiteSpace(window.label)) continue;
            windows.Add(new SubscriptionAccountWindow(
                window.label!,
                window.usedTokens,
                window.cap is > 0 ? window.cap : null,
                window.usedPct is { } pct ? Math.Clamp(pct, 0, 1) : null,
                Parse(window.resetsAt),
                window.windowStartedAt));
        }

        return windows;
    }

    private static SubscriptionAccountInfo Unreachable(string code, bool drained) => new(
        code, null, null, drained, Reachable: false, [], null, null, null);

    private static DateTimeOffset? Parse(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    // Matches writing-ai-sidecars/{claude,codex}/server.mjs GET /usage.
    private sealed class SidecarUsageDto
    {
        public List<UsageWindowDto?>? windows { get; set; }
        public string? lastQuotaErrorAt { get; set; }
        public string? lastQuotaErrorKind { get; set; }
        public string? lastQuotaErrorResetsAt { get; set; }
    }

    private sealed class UsageWindowDto
    {
        public string? label { get; set; }
        public long usedTokens { get; set; }
        public long? cap { get; set; }
        public double? usedPct { get; set; }
        public string? resetsAt { get; set; }
        public string? windowStartedAt { get; set; }
    }
}
