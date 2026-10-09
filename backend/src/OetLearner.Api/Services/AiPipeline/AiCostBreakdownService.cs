using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>One provider+model line of a cost component (owner directive 2026-10-10).</summary>
public sealed record CostRow(
    string ProviderId,
    string ProviderName,
    string Model,
    // Kind: subscription | api | live_voice
    string Kind,
    long Requests,
    long Successes,
    long FailedAttempts,
    long Retries,
    long PromptTokens,
    long CompletionTokens,
    long CacheTokens,
    // CostUsd: incremental API cost. Always 0 for a subscription route (it is never an API charge).
    decimal CostUsd,
    // ApiEquivalentUsd: what a subscription route's calls would have cost on the paid API; null when
    // unknown or not applicable.
    decimal? ApiEquivalentUsd,
    // Basis: subscription | rate_card | stored_estimate | duration_estimate | duration_assumed
    string Basis,
    // Minutes: metered connected minutes (live voice only).
    double? Minutes);

/// <summary>One pipeline component (e.g. Writing grading, Speaking live voice) with its provider/model lines.</summary>
public sealed record CostComponent(
    string Key,
    string Label,
    long Requests,
    long FailedAttempts,
    long Retries,
    long PromptTokens,
    long CompletionTokens,
    long CacheTokens,
    long SubscriptionRequests,
    long ApiRequests,
    decimal ApiUsd,
    decimal SubscriptionApiEquivalentUsd,
    IReadOnlyList<CostRow> Rows);

public sealed record WritingCostBlock(
    CostComponent Grading,
    CostComponent Reviewer,
    decimal TotalUsd,
    long Letters,
    decimal? AvgPerLetterUsd);

public sealed record SpeakingCostBlock(
    CostComponent LiveVoice,
    CostComponent Grading,
    CostComponent Reviewer,
    CostComponent AudioModel,
    decimal TotalUsd,
    long SingleCards,
    long FullMocks,
    decimal? AvgPerSingleCardUsd,
    decimal? AvgPerFullMockUsd,
    decimal? AvgPerAssessmentUsd,
    long LiveVoiceSessions,
    double LiveVoiceMinutes);

public sealed record PromoGrantCost(
    string GrantId,
    string ProviderCode,
    decimal GrantUsd,
    DateTimeOffset StartsAt,
    string? Note,
    decimal ConsumedInWindowUsd,
    decimal ConsumedTotalUsd,
    decimal RemainingUsd,
    decimal OverflowUsd);

public sealed record MoneyBlock(
    decimal GrossApiUsd,
    decimal PipelineApiUsd,
    decimal OtherFeaturesApiUsd,
    decimal SubscriptionApiEquivalentUsd,
    decimal PromoConsumedUsd,
    decimal PromoRemainingUsd,
    decimal OutOfPocketUsd,
    IReadOnlyList<PromoGrantCost> Grants);

public sealed record LiveVoiceRateView(
    string Provider,
    string Name,
    string Model,
    decimal PerMinuteUsd,
    bool OwnerSet,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt);

public sealed record CostBreakdown(
    string Window,
    DateTimeOffset? WindowStart,
    DateTimeOffset GeneratedAt,
    WritingCostBlock Writing,
    SpeakingCostBlock Speaking,
    MoneyBlock Money);

public sealed record CostWindowSummary(
    string Window,
    string Label,
    decimal WritingGradingUsd,
    decimal WritingReviewerUsd,
    decimal WritingTotalUsd,
    long Letters,
    decimal? WritingAvgPerLetterUsd,
    decimal SpeakingLiveVoiceUsd,
    decimal SpeakingGradingUsd,
    decimal SpeakingReviewerUsd,
    decimal SpeakingAudioUsd,
    decimal SpeakingTotalUsd,
    long SingleCards,
    long FullMocks,
    decimal? SpeakingAvgPerAssessmentUsd,
    decimal? SpeakingAvgPerFullMockUsd,
    decimal GrossApiUsd,
    decimal PromoConsumedUsd,
    decimal OutOfPocketUsd);

public sealed record CostRunLeg(string ProviderId, string ProviderName, string Model, string Kind, long Calls, long Failed, decimal CostUsd);

public sealed record CostRunPart(decimal CostUsd, long Calls, long Failed, long Retries, IReadOnlyList<CostRunLeg> Legs);

/// <summary>
/// One reconstructed graded run. Usage rows carry the learner and the time but not a submission id, so a run is
/// every pipeline row of one learner joined by time (a gap over 45 minutes starts a new run). The numbers are exact
/// per row; only the grouping of rows into one submission is inferred.
/// </summary>
public sealed record CostRun(
    string Kind,
    DateTimeOffset At,
    string Learner,
    bool Succeeded,
    CostRunPart Grading,
    CostRunPart Reviewer,
    CostRunPart Audio,
    CostRunPart LiveVoice,
    decimal TotalUsd);

public sealed record CostBreakdownResponse(
    CostBreakdown Selected,
    IReadOnlyList<CostWindowSummary> Summary,
    IReadOnlyList<CostRun> Runs,
    IReadOnlyList<LiveVoiceRateView> LiveVoiceRates,
    string RateCardVerifiedOn);

public interface ICostBreakdownService
{
    Task<CostBreakdownResponse> BuildAsync(string? window, CancellationToken ct);

    /// <summary>Drops cached payloads after a rate or credit-grant write.</summary>
    void InvalidateCache();
}

/// <summary>
/// Builds the Writing / Speaking cost breakdown of the AI Pipelines page (owner directive 2026-10-10).
///
/// Honesty rules, all mechanical:
///  - every figure is an internally tracked estimate (provider-reported tokens x list prices); none is read from a
///    provider invoice, and the payload says which basis produced each row;
///  - a subscription route is never a charge: its cost is 0 and its API-equivalent value is reported separately;
///  - live voice is metered by connected minutes (mint to last saved turn) x the operator's per-minute rate. The
///    realtime audio never passes through this API, so token-exact figures are not available server-side; the
///    method is stated on the row and an unset rate falls back to a visibly-labelled assumption, never to a silent 0;
///  - totals include every call (failures, retries, paid reviewer fallbacks) — they are sums of the same rows.
/// </summary>
public sealed class AiCostBreakdownService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<AiCostBreakdownService> logger) : ICostBreakdownService
{
    public const string LiveVoiceRateFlagPrefix = "ai_live_voice_rate_per_min:";

    /// <summary>Starting estimates used until the owner enters a figure. NOT provider billing.</summary>
    internal static readonly IReadOnlyDictionary<string, decimal> AssumedPerMinuteUsd = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["openai"] = 0.12m,
        ["gemini"] = 0.04m,
    };

    private static readonly (string Id, string Label)[] Windows =
    [
        ("today", "Today"),
        ("7d", "Last 7 days"),
        ("30d", "Last 30 days"),
        ("all", "All time"),
    ];

    private static readonly TimeSpan RunGap = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MaxLiveSessionSpan = TimeSpan.FromMinutes(25);
    private const int MaxRunRows = 4000;
    private const int MaxRuns = 25;

    private static readonly ConcurrentDictionary<string, (CostBreakdownResponse Response, DateTimeOffset At)> Cache = new();

    public void InvalidateCache() => Cache.Clear();

    public async Task<CostBreakdownResponse> BuildAsync(string? window, CancellationToken ct)
    {
        var key = Windows.Any(w => string.Equals(w.Id, window, StringComparison.OrdinalIgnoreCase)) ? window!.ToLowerInvariant() : "7d";
        var now = clock.GetUtcNow();
        if (Cache.TryGetValue(key, out var hit) && now - hit.At < TimeSpan.FromSeconds(60))
            return hit.Response;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var names = await db.AiProviders.AsNoTracking()
            .Select(p => new { p.Code, p.Name })
            .ToDictionaryAsync(p => p.Code, p => p.Name, StringComparer.OrdinalIgnoreCase, ct);
        var rates = await LoadRatesAsync(db, ct);
        var liveOptions = scope.ServiceProvider
            .GetService<Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.LiveVoiceOptions>>()?.Value
            ?? new OetLearner.Api.Configuration.LiveVoiceOptions();

        CostBreakdown? selected = null;
        var summary = new List<CostWindowSummary>(Windows.Length);
        foreach (var (id, label) in Windows)
        {
            var breakdown = await BuildWindowAsync(db, id, now, names, rates, ct);
            summary.Add(ToSummary(breakdown, label));
            if (id == key) selected = breakdown;
        }

        var runs = await BuildRunsAsync(db, StartOf(key, now), now, names, rates, ct);
        var response = new CostBreakdownResponse(
            selected!, summary, runs, RateViews(rates, liveOptions), AiModelRateCard.VerifiedOn);
        Cache[key] = (response, now);
        return response;
    }

    private static DateTimeOffset? StartOf(string window, DateTimeOffset now) => window switch
    {
        "today" => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
        "7d" => now.AddDays(-7),
        "30d" => now.AddDays(-30),
        _ => null,
    };

    // ── rates ────────────────────────────────────────────────────────────────

    private sealed record StoredRate(decimal PerMinuteUsd, string? UpdatedBy, DateTimeOffset UpdatedAt);

    private static async Task<Dictionary<string, StoredRate>> LoadRatesAsync(LearnerDbContext db, CancellationToken ct)
    {
        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Key.StartsWith(LiveVoiceRateFlagPrefix))
            .Select(f => new { f.Key, f.Description, f.Owner, f.UpdatedAt, f.Enabled })
            .ToListAsync(ct);

        var map = new Dictionary<string, StoredRate>(StringComparer.Ordinal);
        foreach (var f in flags)
        {
            if (!f.Enabled) continue;
            var provider = f.Key[LiveVoiceRateFlagPrefix.Length..];
            if (TryParseRate(f.Description, out var rate))
                map[provider] = new StoredRate(rate, f.Owner, f.UpdatedAt);
        }

        return map;
    }

    /// <summary>The flag stores the rate as the first token of its description: "0.1200 | text".</summary>
    internal static bool TryParseRate(string? description, out decimal rate)
    {
        rate = 0m;
        if (string.IsNullOrWhiteSpace(description)) return false;
        var head = description.Split('|', 2)[0].Trim();
        return decimal.TryParse(head, NumberStyles.Number, CultureInfo.InvariantCulture, out rate) && rate >= 0m && rate <= 100m;
    }

    internal static string FormatRateDescription(decimal perMinuteUsd, string provider) =>
        $"{perMinuteUsd.ToString("0.0000", CultureInfo.InvariantCulture)} | Estimated USD per connected minute of {provider} live voice (AI Pipelines page).";

    private static decimal RateFor(string provider, Dictionary<string, StoredRate> rates, out bool ownerSet)
    {
        if (rates.TryGetValue(provider, out var stored))
        {
            ownerSet = true;
            return stored.PerMinuteUsd;
        }

        ownerSet = false;
        return AssumedPerMinuteUsd.TryGetValue(provider, out var assumed) ? assumed : 0m;
    }

    private static IReadOnlyList<LiveVoiceRateView> RateViews(
        Dictionary<string, StoredRate> rates,
        OetLearner.Api.Configuration.LiveVoiceOptions options)
    {
        var views = new List<LiveVoiceRateView>();
        foreach (var (provider, name, model) in new[]
                 {
                     ("openai", "OpenAI GPT Live", options.OpenAiModel),
                     ("gemini", "Gemini Live", options.GeminiModel),
                 })
        {
            var perMinute = RateFor(provider, rates, out var ownerSet);
            rates.TryGetValue(provider, out var stored);
            views.Add(new LiveVoiceRateView(provider, name, model, perMinute, ownerSet, stored?.UpdatedBy, stored?.UpdatedAt));
        }

        return views;
    }

    // ── one window ───────────────────────────────────────────────────────────

    private static string BucketOf(AiUsageGroup g) => AiUsageStageBuckets.For(g.FeatureCode);

    private async Task<CostBreakdown> BuildWindowAsync(
        LearnerDbContext db,
        string window,
        DateTimeOffset now,
        Dictionary<string, string> names,
        Dictionary<string, StoredRate> rates,
        CancellationToken ct)
    {
        var start = StartOf(window, now);
        var groups = await AiUsageLedger.LoadGroupsAsync(db, start, null, null, ct);
        var combined = await AiUsageLedger.LoadGroupsAsync(
            db, start, null, null, ct,
            r => r.FeatureCode == AiFeatureCodes.SpeakingGrade
                 && r.PromptTemplateId != null
                 && r.PromptTemplateId.EndsWith("combined"));

        var writingGrade = Component("writing-grading", "Writing grading", groups.Where(g => BucketOf(g) == AiUsageStageBuckets.WritingGrade), names);
        var writingReview = Component("writing-reviewer", "Writing reviewer", groups.Where(g => BucketOf(g) == AiUsageStageBuckets.WritingReview), names);
        var speakingGradeGroups = groups.Where(g => BucketOf(g) == AiUsageStageBuckets.SpeakingGrade).ToList();
        var speakingGrade = Component("speaking-grading", "Speaking grading", speakingGradeGroups, names);
        var speakingReview = Component("speaking-reviewer", "Speaking reviewer", groups.Where(g => BucketOf(g) == AiUsageStageBuckets.SpeakingReview), names);
        var speakingAudio = Component("speaking-audio", "Speaking audio model (acoustic half of grading)", groups.Where(g => BucketOf(g) == AiUsageStageBuckets.SpeakingAudio), names);

        var (live, liveSessions, liveMinutes) = await LiveVoiceAsync(db, start, now, rates, ct);

        // Units. A successful graded letter is one unit; for Speaking, one successful combined (two-card) grade is a
        // full mock and every other successful grade is a single card. Failed attempts add cost but never a unit.
        var letters = writingGrade.Requests - writingGrade.FailedAttempts;
        var mockSuccess = combined.Sum(g => g.Successes);
        var gradeSuccess = speakingGradeGroups.Sum(g => g.Successes);
        var singleCards = Math.Max(0, gradeSuccess - mockSuccess);
        var fullMocks = mockSuccess;

        var writingTotal = writingGrade.ApiUsd + writingReview.ApiUsd;
        var speakingTotal = live.ApiUsd + speakingGrade.ApiUsd + speakingReview.ApiUsd + speakingAudio.ApiUsd;

        // Per-unit averages. Shared costs are counted once: a two-card mock is one combined grade, one review, and
        // two live sessions and two acoustic judgements (one per card).
        var mockGradeUsd = combined.Sum(g => g.IncrementalApiUsd);
        var singleGradeUsd = Math.Max(0m, speakingGrade.ApiUsd - mockGradeUsd);
        var perLiveSession = liveSessions > 0 ? live.ApiUsd / liveSessions : 0m;
        var reviewSuccess = speakingReview.Requests - speakingReview.FailedAttempts;
        var perReview = reviewSuccess > 0 ? speakingReview.ApiUsd / reviewSuccess : 0m;
        var audioSuccess = speakingAudio.Requests - speakingAudio.FailedAttempts;
        var perAudio = audioSuccess > 0 ? speakingAudio.ApiUsd / audioSuccess : 0m;

        decimal? avgSingle = singleCards > 0
            ? Math.Round(singleGradeUsd / singleCards + perReview + perAudio + perLiveSession, 4)
            : null;
        decimal? avgMock = fullMocks > 0
            ? Math.Round(mockGradeUsd / fullMocks + perReview + 2m * perAudio + 2m * perLiveSession, 4)
            : null;
        var units = singleCards + fullMocks;
        decimal? avgAssessment = units > 0 ? Math.Round(speakingTotal / units, 4) : null;

        var writing = new WritingCostBlock(
            writingGrade, writingReview, writingTotal, letters,
            letters > 0 ? Math.Round(writingTotal / letters, 4) : null);
        var speaking = new SpeakingCostBlock(
            live, speakingGrade, speakingReview, speakingAudio, speakingTotal,
            singleCards, fullMocks, avgSingle, avgMock, avgAssessment, liveSessions, liveMinutes);

        var money = await MoneyAsync(db, groups, writingTotal + speakingTotal, start, ct);
        logger.LogDebug("AI cost breakdown built (window={Window}): writing ${Writing}, speaking ${Speaking}.", window, writingTotal, speakingTotal);
        return new CostBreakdown(window, start, now, writing, speaking, money);
    }

    private static CostComponent Component(string key, string label, IEnumerable<AiUsageGroup> groups, Dictionary<string, string> names)
    {
        var rows = groups
            .GroupBy(g => (g.ProviderId, Model: string.IsNullOrWhiteSpace(g.Model) ? "unknown" : g.Model!))
            .Select(g =>
            {
                var subscription = SubscriptionAccountGroups.GroupOf(g.Key.ProviderId) is not null;
                var priced = g.Sum(x => x.PricedUsd);
                var fromCard = g.All(x => x.FromRateCard);
                return new CostRow(
                    ProviderId: g.Key.ProviderId,
                    ProviderName: names.TryGetValue(g.Key.ProviderId, out var n) ? n : g.Key.ProviderId,
                    Model: g.Key.Model,
                    Kind: subscription ? "subscription" : "api",
                    Requests: g.Sum(x => x.Calls),
                    Successes: g.Sum(x => x.Successes),
                    FailedAttempts: g.Sum(x => x.Failures),
                    Retries: g.Sum(x => x.Retries),
                    PromptTokens: g.Sum(x => x.PromptTokens),
                    CompletionTokens: g.Sum(x => x.CompletionTokens),
                    CacheTokens: g.Sum(x => x.CacheWriteTokens + x.CacheReadTokens),
                    CostUsd: subscription ? 0m : priced,
                    ApiEquivalentUsd: subscription && fromCard ? priced : null,
                    Basis: subscription ? "subscription" : fromCard ? "rate_card" : "stored_estimate",
                    Minutes: null);
            })
            .OrderByDescending(r => r.CostUsd)
            .ThenByDescending(r => r.Requests)
            .ToList();
        return Total(key, label, rows);
    }

    private static CostComponent Total(string key, string label, List<CostRow> rows) => new(
        key,
        label,
        rows.Sum(r => r.Requests),
        rows.Sum(r => r.FailedAttempts),
        rows.Sum(r => r.Retries),
        rows.Sum(r => r.PromptTokens),
        rows.Sum(r => r.CompletionTokens),
        rows.Sum(r => r.CacheTokens),
        rows.Where(r => r.Kind == "subscription").Sum(r => r.Requests),
        rows.Where(r => r.Kind != "subscription").Sum(r => r.Requests),
        rows.Sum(r => r.CostUsd),
        rows.Sum(r => r.ApiEquivalentUsd ?? 0m),
        rows);

    // ── live voice (connected-minutes metering) ──────────────────────────────

    private async Task<(CostComponent Component, long Sessions, double Minutes)> LiveVoiceAsync(
        LearnerDbContext db,
        DateTimeOffset? start,
        DateTimeOffset now,
        Dictionary<string, StoredRate> rates,
        CancellationToken ct)
    {
        var since = start ?? now.AddDays(-365);

        // Provider-session mints are the metering anchor: the server created every live session, so its audit row
        // is the one durable, provider-agnostic proof that a session was billed ("{provider}:{model}").
        var mints = await db.SpeakingPatientTurns.AsNoTracking()
            .Where(t => t.Role == LiveVoiceService.LiveVoiceSessionRole && t.CreatedAt >= since)
            .OrderBy(t => t.CreatedAt)
            .Take(20000)
            .Select(t => new { t.SessionId, t.Text, t.CreatedAt })
            .ToListAsync(ct);
        var lastTurns = await db.SpeakingPatientTurns.AsNoTracking()
            .Where(t => t.Role == LiveVoiceService.LiveVoiceTurnRole && t.CreatedAt >= since)
            .GroupBy(t => t.SessionId)
            .Select(g => new { SessionId = g.Key, Last = g.Max(t => t.CreatedAt) })
            .ToListAsync(ct);
        var lastTurnBySession = lastTurns.ToDictionary(x => x.SessionId, x => x.Last, StringComparer.Ordinal);

        // (provider, model) -> minutes and the distinct sessions that used it.
        var minutes = new Dictionary<(string Provider, string Model), double>();
        var sessions = new Dictionary<(string Provider, string Model), HashSet<string>>();

        foreach (var session in mints.GroupBy(m => m.SessionId, StringComparer.Ordinal))
        {
            var ordered = session.OrderBy(m => m.CreatedAt).ToList();
            var lastActivity = ordered[^1].CreatedAt;
            if (lastTurnBySession.TryGetValue(session.Key, out var lastTurn) && lastTurn > lastActivity)
                lastActivity = lastTurn;

            var remaining = MaxLiveSessionSpan;
            for (var i = 0; i < ordered.Count; i++)
            {
                var (provider, model) = ParseMint(ordered[i].Text);
                var until = i + 1 < ordered.Count ? ordered[i + 1].CreatedAt : lastActivity;
                var span = until - ordered[i].CreatedAt;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                if (span > remaining) span = remaining;
                remaining -= span;

                var k = (provider, model);
                minutes[k] = minutes.GetValueOrDefault(k) + span.TotalMinutes;
                if (!sessions.TryGetValue(k, out var sessionSet))
                {
                    sessionSet = new HashSet<string>(StringComparer.Ordinal);
                    sessions[k] = sessionSet;
                }

                sessionSet.Add(session.Key);
            }
        }

        var rows = new List<CostRow>();
        foreach (var (k, mins) in minutes.OrderByDescending(x => x.Value))
        {
            var perMinute = RateFor(k.Provider, rates, out var ownerSet);
            rows.Add(new CostRow(
                ProviderId: k.Provider,
                ProviderName: k.Provider == "openai" ? "OpenAI GPT Live" : k.Provider == "gemini" ? "Gemini Live" : k.Provider,
                Model: k.Model,
                Kind: "live_voice",
                Requests: sessions[k].Count,
                Successes: sessions[k].Count,
                FailedAttempts: 0,
                Retries: 0,
                PromptTokens: 0,
                CompletionTokens: 0,
                CacheTokens: 0,
                CostUsd: Math.Round((decimal)mins * perMinute, 4),
                ApiEquivalentUsd: null,
                Basis: ownerSet ? "duration_estimate" : "duration_assumed",
                Minutes: Math.Round(mins, 2)));
        }

        var totalSessions = mints.Select(m => m.SessionId).Distinct(StringComparer.Ordinal).LongCount();
        return (Total("speaking-live-voice", "Speaking live voice agent", rows), totalSessions, Math.Round(minutes.Values.Sum(), 2));
    }

    private static (string Provider, string Model) ParseMint(string text)
    {
        var sep = text.IndexOf(':');
        return sep > 0
            ? (text[..sep].Trim().ToLowerInvariant(), text[(sep + 1)..].Trim())
            : (text.Trim().ToLowerInvariant(), "unknown");
    }

    // ── promotional credits and out-of-pocket ────────────────────────────────

    private static async Task<MoneyBlock> MoneyAsync(
        LearnerDbContext db,
        List<AiUsageGroup> groups,
        decimal pipelineApi,
        DateTimeOffset? start,
        CancellationToken ct)
    {
        // pipelineApi = Writing and Speaking grading + review + the audio model + live voice (the same rows as the
        // components above); "other" is every other AI feature, so the two always add up to the gross.
        var otherApi = groups
            .Where(g => AiUsageStageBuckets.For(g.FeatureCode) == AiUsageStageBuckets.Other)
            .Sum(g => g.IncrementalApiUsd);
        var gross = pipelineApi + otherApi;

        var subscriptionEquivalent = groups
            .Where(g => g.IsSubscription && g.FromRateCard)
            .Sum(g => g.PricedUsd);

        var grants = await db.AiCreditGrants.AsNoTracking().OrderByDescending(g => g.StartsAt).ToListAsync(ct);
        var views = new List<PromoGrantCost>(grants.Count);
        foreach (var grant in grants)
        {
            var total = await AiUsageLedger.SpendUsdAsync(db, grant.ProviderCode, grant.StartsAt, null, ct);
            var before = start is { } s && s > grant.StartsAt
                ? await AiUsageLedger.SpendUsdAsync(db, grant.ProviderCode, grant.StartsAt, s, ct)
                : 0m;
            var consumedTotal = Math.Min(total, grant.GrantUsd);
            var consumedBefore = Math.Min(before, grant.GrantUsd);
            views.Add(new PromoGrantCost(
                grant.Id,
                grant.ProviderCode,
                grant.GrantUsd,
                grant.StartsAt,
                grant.Note,
                ConsumedInWindowUsd: Math.Max(0m, consumedTotal - consumedBefore),
                ConsumedTotalUsd: consumedTotal,
                RemainingUsd: Math.Max(0m, grant.GrantUsd - total),
                OverflowUsd: Math.Max(0m, total - grant.GrantUsd)));
        }

        var promoConsumed = views.Sum(v => v.ConsumedInWindowUsd);
        return new MoneyBlock(
            GrossApiUsd: gross,
            PipelineApiUsd: pipelineApi,
            OtherFeaturesApiUsd: otherApi,
            SubscriptionApiEquivalentUsd: subscriptionEquivalent,
            PromoConsumedUsd: promoConsumed,
            PromoRemainingUsd: views.Sum(v => v.RemainingUsd),
            OutOfPocketUsd: Math.Max(0m, gross - promoConsumed),
            Grants: views);
    }

    private static CostWindowSummary ToSummary(CostBreakdown b, string label) => new(
        b.Window,
        label,
        b.Writing.Grading.ApiUsd,
        b.Writing.Reviewer.ApiUsd,
        b.Writing.TotalUsd,
        b.Writing.Letters,
        b.Writing.AvgPerLetterUsd,
        b.Speaking.LiveVoice.ApiUsd,
        b.Speaking.Grading.ApiUsd,
        b.Speaking.Reviewer.ApiUsd,
        b.Speaking.AudioModel.ApiUsd,
        b.Speaking.TotalUsd,
        b.Speaking.SingleCards,
        b.Speaking.FullMocks,
        b.Speaking.AvgPerAssessmentUsd,
        b.Speaking.AvgPerFullMockUsd,
        b.Money.GrossApiUsd,
        b.Money.PromoConsumedUsd,
        b.Money.OutOfPocketUsd);

    // ── reconstructed runs ───────────────────────────────────────────────────

    private sealed record RunRow(
        string UserId,
        string Bucket,
        string ProviderId,
        string Model,
        bool Success,
        bool Combined,
        DateTimeOffset At,
        decimal PricedUsd,
        bool Subscription);

    private async Task<IReadOnlyList<CostRun>> BuildRunsAsync(
        LearnerDbContext db,
        DateTimeOffset? start,
        DateTimeOffset now,
        Dictionary<string, string> names,
        Dictionary<string, StoredRate> rates,
        CancellationToken ct)
    {
        try
        {
            var q = db.AiUsageRecords.AsNoTracking()
                .Where(r => r.UserId != null && r.ProviderId != null
                            && (r.FeatureCode == AiFeatureCodes.WritingGrade
                                || r.FeatureCode == AiFeatureCodes.WritingSampleScore
                                || r.FeatureCode == AiFeatureCodes.WritingGradeReview
                                || r.FeatureCode == AiFeatureCodes.SpeakingGrade
                                || r.FeatureCode == AiFeatureCodes.SpeakingGradeReview
                                || r.FeatureCode == AiFeatureCodes.SpeakingAudioAssess));
            if (start is { } s) q = q.Where(r => r.CreatedAt >= s);

            var raw = await q
                .OrderByDescending(r => r.CreatedAt)
                .Take(MaxRunRows)
                .Select(r => new
                {
                    UserId = r.UserId!,
                    r.FeatureCode,
                    ProviderId = r.ProviderId!,
                    r.Model,
                    r.Outcome,
                    r.PromptTemplateId,
                    r.CreatedAt,
                    r.PromptTokens,
                    r.CompletionTokens,
                    r.CacheWriteTokens,
                    r.CacheReadTokens,
                    Stored = r.CalculatedCostUsd ?? r.CostEstimateUsd,
                })
                .ToListAsync(ct);

            var rows = raw.Select(r =>
            {
                var g = new AiUsageGroup(
                    r.ProviderId, r.Model, r.FeatureCode, 1, r.Outcome == AiCallOutcome.Success ? 1 : 0, 0,
                    r.PromptTokens, r.CompletionTokens, r.CacheWriteTokens ?? 0, r.CacheReadTokens ?? 0, r.Stored);
                return new RunRow(
                    r.UserId,
                    AiUsageStageBuckets.For(r.FeatureCode),
                    r.ProviderId,
                    string.IsNullOrWhiteSpace(r.Model) ? "unknown" : r.Model!,
                    r.Outcome == AiCallOutcome.Success,
                    r.PromptTemplateId is not null && r.PromptTemplateId.EndsWith("combined", StringComparison.Ordinal),
                    r.CreatedAt,
                    g.PricedUsd,
                    g.IsSubscription);
            }).ToList();

            // Live voice sessions per learner (mint audit joined to the speaking session owner), for attribution
            // to the speaking runs reconstructed below.
            var since = start ?? now.AddDays(-365);
            var mintRows = await db.SpeakingPatientTurns.AsNoTracking()
                .Where(m => m.Role == LiveVoiceService.LiveVoiceSessionRole && m.CreatedAt >= since)
                .Join(
                    db.SpeakingSessions.AsNoTracking(),
                    m => m.SessionId,
                    s => s.Id,
                    (m, s) => new { s.UserId, m.SessionId, m.Text, m.CreatedAt })
                .Take(20000)
                .ToListAsync(ct);
            var lastTurns = await db.SpeakingPatientTurns.AsNoTracking()
                .Where(t => t.Role == LiveVoiceService.LiveVoiceTurnRole && t.CreatedAt >= since)
                .GroupBy(t => t.SessionId)
                .Select(g => new { SessionId = g.Key, Last = g.Max(t => t.CreatedAt) })
                .ToListAsync(ct);
            var lastTurnBySession = lastTurns.ToDictionary(x => x.SessionId, x => x.Last, StringComparer.Ordinal);

            var liveSessions = mintRows
                .GroupBy(m => m.SessionId, StringComparer.Ordinal)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.CreatedAt).ToList();
                    var first = ordered[0];
                    var end = ordered[^1].CreatedAt;
                    if (lastTurnBySession.TryGetValue(g.Key, out var lt) && lt > end) end = lt;
                    var span = end - first.CreatedAt;
                    if (span > MaxLiveSessionSpan) span = MaxLiveSessionSpan;
                    if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                    var (provider, model) = ParseMint(first.Text);
                    var perMinute = RateFor(provider, rates, out _);
                    return new LiveSession(first.UserId, first.CreatedAt, provider, model, (decimal)span.TotalMinutes * perMinute);
                })
                .ToList();

            var runs = new List<CostRun>();
            foreach (var user in rows.GroupBy(r => r.UserId, StringComparer.Ordinal))
            {
                var ordered = user.OrderBy(r => r.At).ToList();
                var cluster = new List<RunRow>();
                foreach (var row in ordered)
                {
                    if (cluster.Count > 0 && row.At - cluster[^1].At > RunGap)
                    {
                        AddRun(runs, user.Key, cluster, liveSessions, names);
                        cluster = new List<RunRow>();
                    }

                    cluster.Add(row);
                }

                if (cluster.Count > 0) AddRun(runs, user.Key, cluster, liveSessions, names);
            }

            return runs.OrderByDescending(r => r.At).Take(MaxRuns).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The reconstructed list is a convenience view; the totals above never depend on it.
            logger.LogWarning(ex, "Reconstructed run list failed ({Type}); the breakdown totals are unaffected.", ex.GetType().Name);
            return [];
        }
    }

    private sealed record LiveSession(string UserId, DateTimeOffset At, string Provider, string Model, decimal CostUsd);

    private static void AddRun(
        List<CostRun> runs,
        string userId,
        List<RunRow> cluster,
        List<LiveSession> liveSessions,
        Dictionary<string, string> names)
    {
        var grading = cluster.Where(r => r.Bucket is AiUsageStageBuckets.WritingGrade or AiUsageStageBuckets.SpeakingGrade).ToList();
        if (grading.Count == 0) return; // a reviewer-only fragment has no graded unit to price

        var isWriting = grading[0].Bucket == AiUsageStageBuckets.WritingGrade;
        var reviewer = cluster.Where(r => r.Bucket is AiUsageStageBuckets.WritingReview or AiUsageStageBuckets.SpeakingReview).ToList();
        var audio = cluster.Where(r => r.Bucket == AiUsageStageBuckets.SpeakingAudio).ToList();

        var first = cluster[0].At;
        var last = cluster[^1].At;
        var live = isWriting
            ? new List<LiveSession>()
            : liveSessions
                .Where(s => string.Equals(s.UserId, userId, StringComparison.Ordinal)
                            && s.At >= first - RunGap && s.At <= last)
                .ToList();

        var kind = isWriting ? "writing" : grading.Any(r => r.Combined) ? "speaking_mock" : "speaking_card";
        var liveLegs = live
            .GroupBy(s => (s.Provider, s.Model))
            .Select(g => new CostRunLeg(
                g.Key.Provider,
                g.Key.Provider == "openai" ? "OpenAI GPT Live" : g.Key.Provider == "gemini" ? "Gemini Live" : g.Key.Provider,
                g.Key.Model, "live_voice", g.Count(), 0, g.Sum(x => x.CostUsd)))
            .ToList();
        var livePart = new CostRunPart(liveLegs.Sum(l => l.CostUsd), liveLegs.Sum(l => l.Calls), 0, 0, liveLegs);

        var gradingPart = Part(grading, names);
        var reviewerPart = Part(reviewer, names);
        var audioPart = Part(audio, names);
        runs.Add(new CostRun(
            kind,
            last,
            userId.Length > 8 ? userId[..8] : userId,
            grading.Any(r => r.Success),
            gradingPart,
            reviewerPart,
            audioPart,
            livePart,
            gradingPart.CostUsd + reviewerPart.CostUsd + audioPart.CostUsd + livePart.CostUsd));
    }

    private static CostRunPart Part(List<RunRow> rows, Dictionary<string, string> names)
    {
        var legs = rows
            .GroupBy(r => (r.ProviderId, r.Model, r.Subscription))
            .Select(g => new CostRunLeg(
                g.Key.ProviderId,
                names.TryGetValue(g.Key.ProviderId, out var n) ? n : g.Key.ProviderId,
                g.Key.Model,
                g.Key.Subscription ? "subscription" : "api",
                g.Count(),
                g.Count(x => !x.Success),
                g.Key.Subscription ? 0m : g.Sum(x => x.PricedUsd)))
            .OrderByDescending(l => l.Calls)
            .ToList();
        var retries = legs.Sum(l => Math.Max(0, l.Calls - 1));
        return new CostRunPart(legs.Sum(l => l.CostUsd), legs.Sum(l => l.Calls), legs.Sum(l => l.Failed), retries, legs);
    }
}
