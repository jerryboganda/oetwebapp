using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
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
    double? Minutes,
    // ReportedSessions: live voice sessions whose provider-reported token usage was received.
    long ReportedSessions = 0);

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
    double LiveVoiceMinutes,
    // Minutes of live voice whose provider is no longer recorded (audit expired before it was kept): not priced.
    double LiveVoiceUnpricedMinutes = 0,
    long LiveVoiceReportedSessions = 0);

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
    DateTimeOffset? UpdatedAt,
    // Optional blended USD per 1M tokens; when set, sessions with provider-reported usage are priced from tokens.
    decimal? InputPerMillionUsd = null,
    decimal? OutputPerMillionUsd = null);

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

public sealed record ReconciliationCheck(string Name, bool Ok, string Detail);

public sealed record CostReconciliation(
    string Window,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReconciliationCheck> Checks);

public interface ICostBreakdownService
{
    Task<CostBreakdownResponse> BuildAsync(string? window, CancellationToken ct);

    /// <summary>
    /// On-demand evidence (admin-only, never scheduled, nothing written): recomputes the figures through
    /// independent paths and compares them with each other and with the domain records they describe.
    /// </summary>
    Task<CostReconciliation> ReconcileAsync(string? window, CancellationToken ct);

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

    public async Task<CostReconciliation> ReconcileAsync(string? window, CancellationToken ct)
    {
        var key = Windows.Any(w => string.Equals(w.Id, window, StringComparison.OrdinalIgnoreCase)) ? window!.ToLowerInvariant() : "7d";
        var now = clock.GetUtcNow();
        var start = StartOf(key, now);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var names = await db.AiProviders.AsNoTracking()
            .Select(p => new { p.Code, p.Name })
            .ToDictionaryAsync(p => p.Code, p => p.Name, StringComparer.OrdinalIgnoreCase, ct);
        var rates = await LoadRatesAsync(db, ct);

        var groups = await AiUsageLedger.LoadGroupsAsync(db, start, null, null, ct);
        var b = await BuildWindowAsync(db, key, now, names, rates, ct);
        var checks = new List<ReconciliationCheck>();

        static string Usd(decimal v) => "$" + v.ToString("0.0000", CultureInfo.InvariantCulture);

        // 1. The stage components plus "other features" must add up to everything the ledger priced.
        var ledgerApi = groups.Sum(g => g.IncrementalApiUsd);
        var componentsApi = b.Writing.TotalUsd + b.Speaking.Grading.ApiUsd + b.Speaking.Reviewer.ApiUsd + b.Speaking.AudioModel.ApiUsd
                            + b.Money.OtherFeaturesApiUsd;
        checks.Add(new ReconciliationCheck(
            "components_plus_other_equal_ledger",
            Math.Abs(ledgerApi - componentsApi) < 0.0001m,
            $"ledger {Usd(ledgerApi)} vs Writing + Speaking grading/review/audio + other features {Usd(componentsApi)}."));

        // 2. Gross API consumption = ledger + live voice.
        var expectedGross = ledgerApi + b.Speaking.LiveVoice.ApiUsd;
        checks.Add(new ReconciliationCheck(
            "gross_equals_ledger_plus_live_voice",
            Math.Abs(b.Money.GrossApiUsd - expectedGross) < 0.0001m,
            $"gross {Usd(b.Money.GrossApiUsd)} vs ledger {Usd(ledgerApi)} + live voice {Usd(b.Speaking.LiveVoice.ApiUsd)}."));

        // 3. Call counts: nothing in a stage bucket may be lost or counted twice.
        var ledgerCalls = groups.Sum(g => g.Calls);
        var componentCalls = b.Writing.Grading.Requests + b.Writing.Reviewer.Requests + b.Speaking.Grading.Requests
                             + b.Speaking.Reviewer.Requests + b.Speaking.AudioModel.Requests
                             + groups.Where(g => AiUsageStageBuckets.For(g.FeatureCode) == AiUsageStageBuckets.Other).Sum(g => g.Calls);
        checks.Add(new ReconciliationCheck(
            "call_counts_match",
            ledgerCalls == componentCalls,
            $"{ledgerCalls:N0} recorded calls vs {componentCalls:N0} across the stage components and other features."));

        // 4. Models that are not on the rate card fall back to the stored per-call estimate.
        var unpriced = groups
            .Where(g => g.ProviderId == AiPipelineDefaults.ClaudeApiProvider && !g.FromRateCard && g.PromptTokens + g.CompletionTokens > 0)
            .GroupBy(g => g.Model ?? "unknown")
            .Select(g => $"{g.Key} x{g.Sum(x => x.Calls):N0}")
            .ToList();
        checks.Add(new ReconciliationCheck(
            "claude_models_on_rate_card",
            unpriced.Count == 0,
            unpriced.Count == 0
                ? $"Every Claude API call used a model on the rate card (verified {AiModelRateCard.VerifiedOn})."
                : "Priced from the stored estimate because the model is not on the rate card: " + string.Join(", ", unpriced) + "."));

        // 5. Information: how far re-pricing moved the numbers relative to the stored per-call estimate.
        var stored = groups.Where(g => !g.IsSubscription).Sum(g => g.StoredCostUsd);
        var priced = groups.Where(g => !g.IsSubscription).Sum(g => g.PricedUsd);
        checks.Add(new ReconciliationCheck(
            "repricing_delta_vs_stored_estimate",
            true,
            $"Stored per-call estimates total {Usd(stored)}; list-price re-pricing (model rates + cache tokens) totals {Usd(priced)}. " +
            "The stored estimate is what /admin/ai-usage shows; the difference is expected."));

        // 6. Units: letters counted from usage rows vs completed Writing evaluations in the domain tables.
        var evalQuery = db.Evaluations.AsNoTracking()
            .Where(e => e.SubtestCode == "writing" && e.State == AsyncState.Completed);
        if (start is { } windowStart) evalQuery = evalQuery.Where(e => e.GeneratedAt >= windowStart);
        var evaluations = await evalQuery.LongCountAsync(ct);
        var tolerance = Math.Max(2L, (long)Math.Ceiling(evaluations * 0.10));
        checks.Add(new ReconciliationCheck(
            "writing_letters_vs_completed_evaluations",
            Math.Abs(b.Writing.Letters - evaluations) <= tolerance,
            $"{b.Writing.Letters:N0} graded letters in the usage ledger vs {evaluations:N0} completed Writing evaluations " +
            $"(tolerance {tolerance:N0}: free samples and retries are counted differently)."));

        // 7. Live voice: provider known for every minute, and how many sessions carry provider-reported tokens.
        checks.Add(new ReconciliationCheck(
            "live_voice_fully_attributed",
            b.Speaking.LiveVoiceUnpricedMinutes <= 0.0,
            b.Speaking.LiveVoiceUnpricedMinutes <= 0.0
                ? $"All {b.Speaking.LiveVoiceMinutes:N1} metered minutes belong to a recorded provider."
                : $"{b.Speaking.LiveVoiceUnpricedMinutes:N1} of {b.Speaking.LiveVoiceMinutes:N1} minutes are from sessions whose provider was wiped by retention before it was kept; they are not priced."));
        checks.Add(new ReconciliationCheck(
            "live_voice_reported_tokens",
            true,
            $"{b.Speaking.LiveVoiceReportedSessions:N0} of {b.Speaking.LiveVoiceSessions:N0} live voice sessions carry provider-reported token usage; " +
            "the rest are priced from connected minutes."));

        return new CostReconciliation(key, now, checks);
    }

    private static DateTimeOffset? StartOf(string window, DateTimeOffset now) => window switch
    {
        "today" => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
        "7d" => now.AddDays(-7),
        "30d" => now.AddDays(-30),
        _ => null,
    };

    // ── rates ────────────────────────────────────────────────────────────────

    private sealed record StoredRate(
        decimal PerMinuteUsd,
        decimal? InputPerMillionUsd,
        decimal? OutputPerMillionUsd,
        string? UpdatedBy,
        DateTimeOffset UpdatedAt);

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
            if (TryParseRate(f.Description, out var perMinute, out var inPerM, out var outPerM))
                map[provider] = new StoredRate(perMinute, inPerM, outPerM, f.Owner, f.UpdatedAt);
        }

        return map;
    }

    /// <summary>
    /// The flag stores the rates in its description: <c>"0.1200 | in=40.0000;out=80.0000 | text"</c> — the
    /// per-minute rate first, then optional blended per-million-token rates (blank = not set).
    /// </summary>
    internal static bool TryParseRate(string? description, out decimal perMinute, out decimal? inPerMillion, out decimal? outPerMillion)
    {
        perMinute = 0m;
        inPerMillion = null;
        outPerMillion = null;
        if (string.IsNullOrWhiteSpace(description)) return false;

        var parts = description.Split('|');
        if (!decimal.TryParse(parts[0].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out perMinute)
            || perMinute < 0m || perMinute > 100m)
            return false;

        if (parts.Length > 1)
        {
            foreach (var pair in parts[1].Split(';'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                if (!decimal.TryParse(kv[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                    || value < 0m || value > 100_000m)
                    continue;
                if (kv[0].Trim() == "in") inPerMillion = value;
                else if (kv[0].Trim() == "out") outPerMillion = value;
            }
        }

        return true;
    }

    internal static string FormatRateDescription(decimal perMinuteUsd, string provider, decimal? inPerMillion, decimal? outPerMillion)
    {
        static string Fmt(decimal? v) => v is { } d ? d.ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty;
        return $"{perMinuteUsd.ToString("0.0000", CultureInfo.InvariantCulture)} | in={Fmt(inPerMillion)};out={Fmt(outPerMillion)} | " +
               $"Estimated USD per connected minute / per 1M tokens of {provider} live voice (AI Pipelines page).";
    }

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
            views.Add(new LiveVoiceRateView(
                provider, name, model, perMinute, ownerSet, stored?.UpdatedBy, stored?.UpdatedAt,
                stored?.InputPerMillionUsd, stored?.OutputPerMillionUsd));
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

        var (live, liveSessions, liveMinutes, liveUnpricedMinutes, liveReported) = await LiveVoiceAsync(db, start, now, rates, ct);

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
            singleCards, fullMocks, avgSingle, avgMock, avgAssessment, liveSessions, liveMinutes,
            liveUnpricedMinutes, liveReported);

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

    // ── live voice (connected minutes + provider-reported tokens) ─────────────

    private sealed record LiveUsage(string Provider, string Model, long Input, long Output, long Cached, long InAudio, long OutAudio, string Basis);

    private sealed record LiveLeg(string Provider, string Model, double Minutes);

    private sealed record LiveSessionFacts(string SessionId, List<LiveLeg> Legs, LiveUsage? Usage);

    private sealed record LiveLegCost(string Provider, string Model, double Minutes, decimal? CostUsd, bool TokenPriced, LiveUsage? Usage);

    /// <summary>
    /// Every live voice session in the window as the server knows it: the provider-session mint audit rows give
    /// the connected minutes (first mint to last saved turn, capped), and the browser's usage report, when it
    /// arrived, gives the provider-reported tokens. The server created every session, so the mint row is the one
    /// provider-agnostic proof a session was billed.
    /// </summary>
    private static async Task<List<LiveSessionFacts>> LoadLiveSessionsAsync(LearnerDbContext db, DateTimeOffset since, CancellationToken ct)
    {
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
        var usageRows = await db.SpeakingPatientTurns.AsNoTracking()
            .Where(t => t.Role == LiveVoiceService.LiveVoiceUsageRole && t.CreatedAt >= since)
            .OrderBy(t => t.CreatedAt)
            .Take(20000)
            .Select(t => new { t.SessionId, t.Text, t.ResponseJson })
            .ToListAsync(ct);

        var usageBySession = new Dictionary<string, LiveUsage>(StringComparer.Ordinal);
        foreach (var row in usageRows)
        {
            if (!TryReadUsage(row.Text, row.ResponseJson, out var usage)) continue;
            if (usageBySession.TryGetValue(row.SessionId, out var previous) && previous.Provider == usage.Provider)
            {
                usage = new LiveUsage(
                    usage.Provider, usage.Model,
                    previous.Input + usage.Input, previous.Output + usage.Output, previous.Cached + usage.Cached,
                    previous.InAudio + usage.InAudio, previous.OutAudio + usage.OutAudio,
                    previous.Basis == usage.Basis ? usage.Basis : "mixed");
            }

            usageBySession[row.SessionId] = usage;
        }

        var facts = new List<LiveSessionFacts>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in mints.GroupBy(m => m.SessionId, StringComparer.Ordinal))
        {
            var ordered = session.OrderBy(m => m.CreatedAt).ToList();
            var lastActivity = ordered[^1].CreatedAt;
            if (lastTurnBySession.TryGetValue(session.Key, out var lastTurn) && lastTurn > lastActivity)
                lastActivity = lastTurn;

            usageBySession.TryGetValue(session.Key, out var reported);
            var legs = new List<LiveLeg>();
            var remaining = MaxLiveSessionSpan;
            for (var i = 0; i < ordered.Count; i++)
            {
                var (provider, model) = ParseMint(ordered[i].Text);
                // A wiped audit row lost its provider; the usage report still knows it.
                if (provider == "unknown" && reported is not null)
                {
                    provider = reported.Provider;
                    model = reported.Model;
                }

                var until = i + 1 < ordered.Count ? ordered[i + 1].CreatedAt : lastActivity;
                var span = until - ordered[i].CreatedAt;
                if (span < TimeSpan.Zero) span = TimeSpan.Zero;
                if (span > remaining) span = remaining;
                remaining -= span;
                legs.Add(new LiveLeg(provider, model, span.TotalMinutes));
            }

            facts.Add(new LiveSessionFacts(session.Key, legs, reported));
            seen.Add(session.Key);
        }

        // A usage report whose mint audit is gone entirely: tokens only, no minutes.
        foreach (var (sessionId, usage) in usageBySession)
        {
            if (!seen.Contains(sessionId)) facts.Add(new LiveSessionFacts(sessionId, [], usage));
        }

        return facts;
    }

    private static bool TryReadUsage(string text, string json, out LiveUsage usage)
    {
        usage = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            long Read(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n > 0 ? n : 0L;

            var (provider, model) = ParseMint(text);
            if (root.TryGetProperty("provider", out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                provider = p.GetString()!.Trim().ToLowerInvariant();

            var basis = root.TryGetProperty("basis", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "sum" : "sum";
            usage = new LiveUsage(
                provider, model, Read("inputTokens"), Read("outputTokens"), Read("cachedInputTokens"),
                Read("inputAudioTokens"), Read("outputAudioTokens"), basis);
            return usage.Input + usage.Output > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Provider-reported tokens x the owner's blended per-million rates; null unless BOTH rates are set AND the
    /// report is one whose meaning is known: the provider's own end-of-session total (<c>final</c>), or OpenAI's
    /// per-response usage, which is billed per response. Gemini's per-message metadata is shown but never priced,
    /// because whether it is cumulative or per turn is not established.
    /// </summary>
    private static decimal? TokenCost(LiveUsage usage, string provider, Dictionary<string, StoredRate> rates)
    {
        var meaningKnown = usage.Basis == "final" || provider == "openai";
        if (!meaningKnown) return null;
        if (!rates.TryGetValue(provider, out var r) || r.InputPerMillionUsd is null || r.OutputPerMillionUsd is null)
            return null;
        return usage.Input / 1_000_000m * r.InputPerMillionUsd.Value + usage.Output / 1_000_000m * r.OutputPerMillionUsd.Value;
    }

    /// <summary>
    /// One session priced per provider leg: reported tokens x token rates when both exist, else connected
    /// minutes x the per-minute rate, and unpriced (null) when the provider is no longer recorded.
    /// </summary>
    private static List<LiveLegCost> PriceSession(LiveSessionFacts facts, Dictionary<string, StoredRate> rates)
    {
        var result = new List<LiveLegCost>();
        var usageApplied = false;
        var legs = facts.Legs
            .GroupBy(l => (l.Provider, l.Model))
            .Select(g => new LiveLeg(g.Key.Provider, g.Key.Model, g.Sum(x => x.Minutes)))
            .ToList();

        foreach (var leg in legs)
        {
            if (leg.Provider == "unknown")
            {
                result.Add(new LiveLegCost(leg.Provider, leg.Model, leg.Minutes, null, false, null));
                continue;
            }

            LiveUsage? usage = null;
            if (!usageApplied && facts.Usage is { } reported && reported.Provider == leg.Provider)
            {
                usage = reported;
                usageApplied = true;
            }

            var tokenCost = usage is not null ? TokenCost(usage, leg.Provider, rates) : null;
            var cost = tokenCost ?? (decimal)leg.Minutes * RateFor(leg.Provider, rates, out _);
            result.Add(new LiveLegCost(leg.Provider, leg.Model, leg.Minutes, cost, tokenCost is not null, usage));
        }

        if (!usageApplied && facts.Usage is { } orphan)
        {
            var tokenCost = orphan.Provider != "unknown" ? TokenCost(orphan, orphan.Provider, rates) : null;
            result.Add(new LiveLegCost(orphan.Provider, orphan.Model, 0, tokenCost, tokenCost is not null, orphan));
        }

        return result;
    }

    private sealed class LiveAggregate
    {
        public HashSet<string> Sessions { get; } = new(StringComparer.Ordinal);
        public double Minutes { get; set; }
        public decimal Cost { get; set; }
        public long Input { get; set; }
        public long Output { get; set; }
        public long Cached { get; set; }
        public long Reported { get; set; }
        public long TokenPriced { get; set; }
    }

    private async Task<(CostComponent Component, long Sessions, double Minutes, double UnpricedMinutes, long ReportedSessions)> LiveVoiceAsync(
        LearnerDbContext db,
        DateTimeOffset? start,
        DateTimeOffset now,
        Dictionary<string, StoredRate> rates,
        CancellationToken ct)
    {
        var facts = await LoadLiveSessionsAsync(db, start ?? now.AddDays(-365), ct);

        var aggregates = new Dictionary<(string Provider, string Model), LiveAggregate>();
        foreach (var session in facts)
        {
            foreach (var leg in PriceSession(session, rates))
            {
                var key = (leg.Provider, leg.Model);
                if (!aggregates.TryGetValue(key, out var agg))
                {
                    agg = new LiveAggregate();
                    aggregates[key] = agg;
                }

                agg.Sessions.Add(session.SessionId);
                agg.Minutes += leg.Minutes;
                agg.Cost += leg.CostUsd ?? 0m;
                if (leg.Usage is { } u)
                {
                    agg.Reported++;
                    agg.Input += u.Input;
                    agg.Output += u.Output;
                    agg.Cached += u.Cached;
                }

                if (leg.TokenPriced) agg.TokenPriced++;
            }
        }

        var rows = new List<CostRow>();
        double unpricedMinutes = 0;
        foreach (var entry in aggregates.OrderByDescending(x => x.Value.Cost).ThenByDescending(x => x.Value.Minutes))
        {
            var provider = entry.Key.Provider;
            var model = entry.Key.Model;
            var agg = entry.Value;
            var unknown = provider == "unknown";
            RateFor(provider, rates, out var ownerSet);
            if (unknown) unpricedMinutes += agg.Minutes;
            var basis = unknown
                ? "unpriced"
                : agg.TokenPriced > 0 && agg.TokenPriced >= agg.Sessions.Count ? "reported_tokens"
                : agg.TokenPriced > 0 ? "mixed"
                : ownerSet ? "duration_estimate" : "duration_assumed";
            rows.Add(new CostRow(
                ProviderId: provider,
                ProviderName: provider == "openai" ? "OpenAI GPT Live" : provider == "gemini" ? "Gemini Live" : unknown ? "Provider not recorded" : provider,
                Model: model,
                Kind: "live_voice",
                Requests: agg.Sessions.Count,
                Successes: agg.Sessions.Count,
                FailedAttempts: 0,
                Retries: 0,
                PromptTokens: agg.Input,
                CompletionTokens: agg.Output,
                CacheTokens: agg.Cached,
                CostUsd: unknown ? 0m : Math.Round(agg.Cost, 4),
                ApiEquivalentUsd: null,
                Basis: basis,
                Minutes: Math.Round(agg.Minutes, 2),
                ReportedSessions: agg.Reported));
        }

        return (
            Total("speaking-live-voice", "Speaking live voice agent", rows),
            facts.Count,
            Math.Round(aggregates.Values.Sum(a => a.Minutes), 2),
            Math.Round(unpricedMinutes, 2),
            rows.Sum(r => r.ReportedSessions));
    }

    private static (string Provider, string Model) ParseMint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("unknown", "unknown");
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
            // to the speaking runs reconstructed below; priced exactly like the live voice component.
            var since = start ?? now.AddDays(-365);
            var facts = await LoadLiveSessionsAsync(db, since, ct);
            var owners = await db.SpeakingPatientTurns.AsNoTracking()
                .Where(m => m.Role == LiveVoiceService.LiveVoiceSessionRole && m.CreatedAt >= since)
                .Join(
                    db.SpeakingSessions.AsNoTracking(),
                    m => m.SessionId,
                    s => s.Id,
                    (m, s) => new { m.SessionId, s.UserId, m.CreatedAt })
                .Take(20000)
                .ToListAsync(ct);
            var ownerBySession = owners
                .GroupBy(o => o.SessionId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (UserId: g.First().UserId, At: g.Min(x => x.CreatedAt)), StringComparer.Ordinal);

            var liveSessions = new List<LiveSession>();
            foreach (var session in facts)
            {
                if (!ownerBySession.TryGetValue(session.SessionId, out var owner)) continue;
                var legs = PriceSession(session, rates);
                var main = legs.OrderByDescending(l => l.Minutes).FirstOrDefault();
                liveSessions.Add(new LiveSession(
                    owner.UserId, owner.At, main?.Provider ?? "unknown", main?.Model ?? "unknown",
                    legs.Sum(l => l.CostUsd ?? 0m)));
            }

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
