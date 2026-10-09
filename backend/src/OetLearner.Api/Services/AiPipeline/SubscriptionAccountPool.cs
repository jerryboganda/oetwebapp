using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>
/// Which subscription engine an account belongs to. The pool only ever permutes hops INSIDE one
/// group (owner directive 2026-10-10), so the class order of the saved plan — Max first, the paid
/// API and Codex behind it — is preserved exactly and a Claude Max subscription is always the first
/// thing a run tries.
/// </summary>
public static class SubscriptionAccountGroups
{
    public const string ClaudeMax = "claude-max";
    public const string Codex = "codex";

    /// <summary>The group a provider code belongs to, or null for a non-subscription row
    /// (the paid Anthropic API, an openai/gemini live-voice row, ...).</summary>
    public static string? GroupOf(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return null;
        if (providerCode.StartsWith(WritingSubscriptionProviderDefaults.ClaudeCode, StringComparison.OrdinalIgnoreCase))
            return ClaudeMax;
        if (providerCode.StartsWith(WritingSubscriptionProviderDefaults.CodexCode, StringComparison.OrdinalIgnoreCase))
            return Codex;
        return null;
    }

    /// <summary>Every seeded account code of one group, primary first.</summary>
    public static IReadOnlyList<string> CodesFor(string group) => group switch
    {
        ClaudeMax => WritingSubscriptionProviderDefaults.ClaudeGroupCodes,
        Codex => WritingSubscriptionProviderDefaults.CodexGroupCodes,
        _ => [],
    };
}

/// <summary>One usage window of one account as its sidecar reported it.</summary>
public sealed record SubscriptionAccountWindow(
    string Label,
    long UsedTokens,
    long? Cap,
    double? UsedPct,
    DateTimeOffset? ResetsAt,
    string? WindowStartedAt);

/// <summary>Why an account is where it is in its group's order.</summary>
public enum SubscriptionAccountStanding
{
    /// <summary>Healthy: first in its group's order.</summary>
    Serving = 0,
    /// <summary>Estimated over the switch threshold: parked at the back of the healthy accounts.</summary>
    Parked = 1,
    /// <summary>A real quota/auth failure: parked until the account's reset time.</summary>
    Cooldown = 2,
    /// <summary>An admin drained it: last of its group (still attempted, never removed).</summary>
    Drained = 3,
}

/// <summary>One account's live state, for the admin panel and for the ordering.</summary>
public sealed record SubscriptionAccountStatus(
    string ProviderCode,
    string Group,
    string? Name,
    string? BaseUrl,
    SubscriptionAccountStanding Standing,
    string? StandingReason,
    IReadOnlyList<SubscriptionAccountWindow> Windows,
    DateTimeOffset? LastQuotaErrorAt,
    string? LastQuotaErrorKind,
    DateTimeOffset? CooldownUntil,
    DateTimeOffset SampledAt,
    bool Reachable);

/// <summary>The pool's current policy, so the admin panel shows what the runtime is using.</summary>
public sealed record SubscriptionPoolPolicy(double SwitchPercent, TimeSpan CooldownFloor, TimeSpan RefreshTtl);

/// <summary>The scoped side of the pool: reads provider rows, drain flags and each account's
/// <c>GET /usage</c>. Kept scoped so the singleton pool never touches a DbContext.</summary>
public interface ISubscriptionAccountStateProvider
{
    Task<SubscriptionAccountSample> SampleAsync(CancellationToken ct);
}

/// <summary>One refresh: the effective policy plus every seeded account's state.</summary>
public sealed record SubscriptionAccountSample(
    double SwitchPercent,
    TimeSpan CooldownFloor,
    IReadOnlyList<SubscriptionAccountInfo> Accounts);

/// <summary>One account as read from the database and its sidecar.</summary>
public sealed record SubscriptionAccountInfo(
    string ProviderCode,
    string? Name,
    string? BaseUrl,
    bool Drained,
    bool Reachable,
    IReadOnlyList<SubscriptionAccountWindow> Windows,
    DateTimeOffset? LastQuotaErrorAt,
    string? LastQuotaErrorKind,
    DateTimeOffset? LastQuotaErrorResetsAt);

public interface ISubscriptionAccountPool
{
    /// <summary>
    /// The same plan with the hops of each subscription engine group permuted by remaining quota.
    /// Never removes a hop, never re-enables a disabled one and never touches the saved order.
    /// </summary>
    Task<AiPipelinePlan> OrderAsync(ISubscriptionAccountStateProvider state, AiPipelinePlan plan, CancellationToken ct);

    /// <summary>Force a refresh now (admin panel), ignoring the cache TTL.</summary>
    Task RefreshAsync(ISubscriptionAccountStateProvider state, CancellationToken ct);

    /// <summary>A real quota/auth failure on one account: park it until its reset, or the floor.</summary>
    void NoteAccountFailure(string providerCode, DateTimeOffset now, DateTimeOffset? resetsAt = null);

    IReadOnlyList<SubscriptionAccountStatus> Accounts { get; }

    SubscriptionPoolPolicy Policy { get; }

    DateTimeOffset? LastRefreshedAt { get; }
}

/// <summary>Rotation knobs. Env/appsettings only (`SubscriptionPool:*`); the switch percentage has an
/// audited admin override on /admin/ai-pipelines.</summary>
public sealed class SubscriptionAccountPoolOptions
{
    public double SwitchPercent { get; set; } = 95;

    public int CooldownFloorMinutes { get; set; } = 5;

    public int RefreshSeconds { get; set; } = 45;
}

/// <summary>
/// Subscription-account rotation (owner directive 2026-10-10). Holds the last sampled state of every
/// seeded subscription account and, per run, permutes the hops of one engine group so the account
/// with the most quota left is tried first; a real quota failure parks that account until its reset.
///
/// What it deliberately does NOT do: it never removes a hop (so a Max subscription is always the
/// first thing tried and the plan the run executes always contains every saved, usable hop), never
/// re-enables a hop an admin disabled, and never writes <c>AiPipelineStages</c> — the only writer of
/// the saved order stays <see cref="AiPipelineStore"/>. An account whose state is unknown keeps its
/// saved position: the existing reactive failover (a typed 429/401 moves to the next hop inside the
/// run) is the safety net for every estimate miss.
/// </summary>
public sealed class SubscriptionAccountPool : ISubscriptionAccountPool
{
    private readonly ILogger<SubscriptionAccountPool> _logger;
    private readonly IOptions<SubscriptionAccountPoolOptions> _options;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, AccountState> _accounts = new(StringComparer.Ordinal);
    private double _switchPercent;
    private TimeSpan _cooldownFloor;
    private DateTimeOffset? _lastRefreshedAt;

    public SubscriptionAccountPool(
        IOptions<SubscriptionAccountPoolOptions> options,
        ILogger<SubscriptionAccountPool> logger)
    {
        _options = options;
        _logger = logger;
        _switchPercent = NormalisePercent(options.Value.SwitchPercent);
        _cooldownFloor = TimeSpan.FromMinutes(Math.Max(1, options.Value.CooldownFloorMinutes));
    }

    public IReadOnlyList<SubscriptionAccountStatus> Accounts
    {
        get
        {
            lock (_stateGate)
            {
                return _accounts.Values
                    .Select(account => ToStatus(this, account))
                    .OrderBy(a => a.Group, StringComparer.Ordinal)
                    .ThenBy(a => a.ProviderCode, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    public SubscriptionPoolPolicy Policy
    {
        get
        {
            lock (_stateGate)
            {
                return new SubscriptionPoolPolicy(
                    _switchPercent, _cooldownFloor, TimeSpan.FromSeconds(Math.Max(5, _options.Value.RefreshSeconds)));
            }
        }
    }

    public DateTimeOffset? LastRefreshedAt
    {
        get { lock (_stateGate) return _lastRefreshedAt; }
    }

    public async Task<AiPipelinePlan> OrderAsync(
        ISubscriptionAccountStateProvider state, AiPipelinePlan plan, CancellationToken ct)
    {
        await RefreshIfStaleAsync(state, ct);
        return Rotate(plan);
    }

    public async Task RefreshAsync(ISubscriptionAccountStateProvider state, CancellationToken ct)
    {
        await RefreshIfStaleAsync(state, ct, force: true);
    }

    public void NoteAccountFailure(string providerCode, DateTimeOffset now, DateTimeOffset? resetsAt = null)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return;
        var group = SubscriptionAccountGroups.GroupOf(providerCode);
        if (group is null) return;

        lock (_stateGate)
        {
            if (!_accounts.TryGetValue(providerCode, out var account))
            {
                account = new AccountState { ProviderCode = providerCode, Group = group };
                _accounts[providerCode] = account;
            }

            if (account.LastQuotaErrorAt is null || now >= account.LastQuotaErrorAt.Value)
            {
                account.LastQuotaErrorAt = now;
                account.LastQuotaErrorKind = "quota";
            }

            var until = resetsAt is { } reset && reset > now ? reset : now + _cooldownFloor;
            if (account.CooldownUntil is null || until > account.CooldownUntil) account.CooldownUntil = until;
        }

        _logger.LogInformation(
            "Subscription account {ProviderCode} parked after a quota/auth failure until {Until}.",
            providerCode, resetsAt?.ToString("u") ?? (now + _cooldownFloor).ToString("u"));
    }

    private async Task RefreshIfStaleAsync(
        ISubscriptionAccountStateProvider state, CancellationToken ct, bool force = false)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(5, _options.Value.RefreshSeconds));
        var now = DateTimeOffset.UtcNow;
        lock (_stateGate)
        {
            if (!force && _lastRefreshedAt is { } last && now - last < ttl) return;
        }

        await _refreshGate.WaitAsync(ct);
        try
        {
            lock (_stateGate)
            {
                now = DateTimeOffset.UtcNow;
                if (!force && _lastRefreshedAt is { } last && now - last < ttl) return;
            }

            var sample = await state.SampleAsync(ct);

            lock (_stateGate)
            {
                _switchPercent = NormalisePercent(sample.SwitchPercent);
                _cooldownFloor = sample.CooldownFloor > TimeSpan.Zero
                    ? sample.CooldownFloor
                    : TimeSpan.FromMinutes(Math.Max(1, _options.Value.CooldownFloorMinutes));

                foreach (var info in sample.Accounts)
                {
                    var group = SubscriptionAccountGroups.GroupOf(info.ProviderCode) ?? info.ProviderCode;
                    if (!_accounts.TryGetValue(info.ProviderCode, out var account))
                    {
                        account = new AccountState { ProviderCode = info.ProviderCode, Group = group };
                        _accounts[info.ProviderCode] = account;
                    }

                    account.Group = group;
                    account.Name = info.Name;
                    account.BaseUrl = info.BaseUrl;
                    account.Drained = info.Drained;
                    account.Reachable = info.Reachable;
                    account.Known = true;
                    account.Windows = info.Windows.ToList();
                    account.SampledAt = now;

                    // A sidecar-reported quota error that is newer than the last one we saw extends
                    // the cooldown to the reset the CLI actually promised.
                    if (info.LastQuotaErrorAt is { } errorAt && (account.LastQuotaErrorAt is null || errorAt > account.LastQuotaErrorAt))
                    {
                        account.LastQuotaErrorAt = errorAt;
                        account.LastQuotaErrorKind = info.LastQuotaErrorKind ?? "quota";
                        if (info.LastQuotaErrorResetsAt is { } reset)
                        {
                            account.CooldownUntil = reset > errorAt ? reset : errorAt + _cooldownFloor;
                        }
                        else if (account.CooldownUntil is null || account.CooldownUntil < errorAt + _cooldownFloor)
                        {
                            account.CooldownUntil = errorAt + _cooldownFloor;
                        }
                    }
                }

                _lastRefreshedAt = now;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private AiPipelinePlan Rotate(AiPipelinePlan plan)
    {
        if (plan.Hops.Count < 2) return plan;

        var now = DateTimeOffset.UtcNow;
        var groups = new Dictionary<string, List<AiPipelineResolvedHop>>(StringComparer.Ordinal);
        foreach (var hop in plan.Hops)
        {
            var group = SubscriptionAccountGroups.GroupOf(hop.Provider);
            if (group is null) continue;
            if (!groups.TryGetValue(group, out var hops)) groups[group] = hops = new List<AiPipelineResolvedHop>();
            hops.Add(hop);
        }

        if (groups.Count == 0) return plan;

        var ordered = new Dictionary<string, List<AiPipelineResolvedHop>>(StringComparer.Ordinal);
        foreach (var (group, hops) in groups)
        {
            var rotated = hops
                .Select(hop => (Hop: hop, State: StateOf(hop.Provider, now)))
                .OrderBy(x => (int)x.State.Standing)
                .ThenBy(x => x.State.WorstUsedPct ?? -1)
                .ThenBy(x => x.Hop.Index)
                .Select(x => x.Hop)
                .ToList();
            ordered[group] = rotated;

            var changed = !rotated.Select(h => h.Provider).SequenceEqual(hops.Select(h => h.Provider));
            if (changed)
            {
                _logger.LogInformation(
                    "Subscription account rotation for {Stage}: {Group} order {Saved} -> {Chosen} (threshold {Threshold}%).",
                    plan.StageKey, group,
                    string.Join(" > ", hops.Select(h => h.Provider)),
                    string.Join(" > ", rotated.Select(h => h.Provider)),
                    _switchPercent);
            }
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AiPipelineResolvedHop>(plan.Hops.Count);
        foreach (var hop in plan.Hops)
        {
            var group = SubscriptionAccountGroups.GroupOf(hop.Provider);
            if (group is null)
            {
                result.Add(hop);
                continue;
            }

            // The group is emitted once, at the position of its first saved hop: the class order of
            // the saved plan (Max, then the API, then Codex) survives the rotation untouched.
            if (emitted.Add(group)) result.AddRange(ordered[group]);
        }

        return result.Count == plan.Hops.Count && result.SequenceEqual(plan.Hops)
            ? plan
            : plan with { Hops = result };
    }

    private (SubscriptionAccountStanding Standing, string? Reason, double? WorstUsedPct) StateOf(
        string providerCode, DateTimeOffset now)
    {
        lock (_stateGate)
        {
            if (!_accounts.TryGetValue(providerCode, out var account))
                return (SubscriptionAccountStanding.Serving, null, null);

            var worst = WorstUsedPctOf(account);
            if (account.Drained) return (SubscriptionAccountStanding.Drained, "drained_by_admin", worst);
            if (account.CooldownUntil is { } until && now < until)
                return (SubscriptionAccountStanding.Cooldown, $"quota_cooldown_until_{until:u}", worst);
            if (IsOverThreshold(account)) return (SubscriptionAccountStanding.Parked, "over_switch_threshold", worst);
            return (SubscriptionAccountStanding.Serving, null, worst);
        }
    }

    private bool IsOverThreshold(AccountState account)
        => account.Windows.Any(w => w.Cap is > 0 && w.UsedPct is { } pct && pct * 100.0 >= _switchPercent);

    private static double? WorstUsedPctOf(AccountState account)
    {
        var values = account.Windows
            .Where(w => w.UsedPct is not null)
            .Select(w => w.UsedPct!.Value * 100.0)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    private static SubscriptionAccountStatus ToStatus(SubscriptionAccountPool pool, AccountState account)
    {
        var worst = WorstUsedPctOf(account);
        var standing = account.Drained
            ? SubscriptionAccountStanding.Drained
            : account.CooldownUntil is { } until && DateTimeOffset.UtcNow < until
                ? SubscriptionAccountStanding.Cooldown
                : pool.IsOverThreshold(account)
                    ? SubscriptionAccountStanding.Parked
                    : SubscriptionAccountStanding.Serving;
        return new SubscriptionAccountStatus(
            account.ProviderCode,
            account.Group,
            account.Name,
            account.BaseUrl,
            standing,
            standing switch
            {
                SubscriptionAccountStanding.Drained => "drained_by_admin",
                SubscriptionAccountStanding.Cooldown => "quota_cooldown",
                SubscriptionAccountStanding.Parked => "over_switch_threshold",
                _ => null,
            },
            account.Windows,
            account.LastQuotaErrorAt,
            account.LastQuotaErrorKind,
            account.CooldownUntil,
            account.SampledAt,
            account.Reachable);
    }

    private static double NormalisePercent(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? 95 : Math.Clamp(value, 50, 100);

    private sealed class AccountState
    {
        public string ProviderCode { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? BaseUrl { get; set; }
        public bool Known { get; set; }
        public bool Reachable { get; set; }
        public bool Drained { get; set; }
        public List<SubscriptionAccountWindow> Windows { get; set; } = new();
        public DateTimeOffset? LastQuotaErrorAt { get; set; }
        public string? LastQuotaErrorKind { get; set; }
        public DateTimeOffset? CooldownUntil { get; set; }
        public DateTimeOffset SampledAt { get; set; }
    }
}
