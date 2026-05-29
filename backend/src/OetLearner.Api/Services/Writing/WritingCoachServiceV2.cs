using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingCoachHint(string Category, string Text, string? RuleId, int? CharStart, int? CharEnd);

public sealed record WritingCoachRequest(
    string UserId,
    string SessionId,
    Guid ScenarioId,
    string DraftContent,
    int WordCount,
    string? LetterType,
    string? Profession);

public sealed record WritingCoachResponse(
    string SessionId,
    IReadOnlyList<WritingCoachHint> Hints,
    bool Throttled,
    bool DailyCapReached,
    int HintsRemainingInSession,
    int SecondsUntilNextHint);

public interface IWritingCoachServiceV2
{
    Task<WritingCoachResponse> RequestHintAsync(WritingCoachRequest request, CancellationToken ct);
    Task<IReadOnlyList<WritingCoachHintResponse>> RequestHintsAsync(string userId, WritingCoachHintRequest request, CancellationToken ct);
}

/// <summary>
/// Real Haiku 4.5 coach pipeline. Enforces:
///   * 1 hint per 30s per (userId,sessionId)
///   * 80 hints max per session
///   * Per-learner USD cap from WritingV2Options (daily reset)
///   * Hard kill switch when Writing:CoachEnabled=false
/// In-memory state via IMemoryCache; cost rollup table-backed by AI usage so
/// the existing IAiGatewayService accounting is the source of truth.
/// </summary>
public sealed class WritingCoachServiceV2(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IMemoryCache cache,
    IOptions<WritingV2Options> options,
    TimeProvider clock,
    ILogger<WritingCoachServiceV2> logger) : IWritingCoachServiceV2
{
    public async Task<WritingCoachResponse> RequestHintAsync(WritingCoachRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // §8 mock-mode suppression: refuse hints if the user has an active mock writing attempt.
        // Defense-in-depth — the mock writing UI never calls this endpoint, but a user could
        // invoke it directly via DevTools during a mock exam to gain unfair AI assistance.
        var hasActiveMockWriting = await db.Attempts.AsNoTracking().AnyAsync(
            a => a.UserId == request.UserId
                && a.SubtestCode == "writing"
                && (a.Context == "mock" || a.Context == "mock_set")
                && a.SubmittedAt == null, ct);
        if (hasActiveMockWriting)
        {
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: false, DailyCapReached: true,
                HintsRemainingInSession: 0, SecondsUntilNextHint: 0);
        }
        var opts = options.Value;
        if (!opts.CoachEnabled)
        {
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: false, DailyCapReached: true,
                HintsRemainingInSession: 0, SecondsUntilNextHint: 0);
        }

        var now = clock.GetUtcNow();
        var rateKey = $"writing:coach:rate:{request.UserId}:{request.SessionId}";
        var sessionKey = $"writing:coach:count:{request.UserId}:{request.SessionId}";
        var dailyCostKey = $"writing:coach:cost:{request.UserId}:{now.UtcDateTime:yyyy-MM-dd}";

        var lastHintAt = cache.Get<DateTimeOffset?>(rateKey);
        var elapsedSeconds = lastHintAt is null ? int.MaxValue : (int)Math.Max(0, (now - lastHintAt.Value).TotalSeconds);
        var secondsUntilNext = Math.Max(0, opts.CoachMinSecondsBetweenHints - elapsedSeconds);
        if (secondsUntilNext > 0)
        {
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: true, DailyCapReached: false,
                HintsRemainingInSession: Math.Max(0, opts.CoachMaxHintsPerSession - cache.Get<int?>(sessionKey).GetValueOrDefault()),
                SecondsUntilNextHint: secondsUntilNext);
        }

        var sessionCount = cache.Get<int?>(sessionKey).GetValueOrDefault();
        if (sessionCount >= opts.CoachMaxHintsPerSession)
        {
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: true, DailyCapReached: true,
                HintsRemainingInSession: 0, SecondsUntilNextHint: 0);
        }

        var dailyCost = cache.Get<decimal?>(dailyCostKey).GetValueOrDefault();
        if (dailyCost >= opts.CoachDailyCostCapPerLearnerUsd)
        {
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: true, DailyCapReached: true,
                HintsRemainingInSession: Math.Max(0, opts.CoachMaxHintsPerSession - sessionCount),
                SecondsUntilNextHint: 0);
        }

        AiGatewayResult result;
        try
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = request.LetterType ?? "routine_referral",
                Task = AiTaskMode.Coach,
            });
            result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildCoachInput(request),
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingCoachV1,
                PromptTemplateId = "writing.coach.v1",
                UserId = request.UserId,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing coach AI call failed for user {UserId}", request.UserId);
            return new WritingCoachResponse(request.SessionId, Array.Empty<WritingCoachHint>(),
                Throttled: false, DailyCapReached: false,
                HintsRemainingInSession: Math.Max(0, opts.CoachMaxHintsPerSession - sessionCount),
                SecondsUntilNextHint: opts.CoachMinSecondsBetweenHints);
        }

        var hints = ParseCoachHints(result.Completion, request.DraftContent.Length);
        await PersistSessionAsync(request, hints, ct);

        var costForThisCall = EstimateCallCost(result.Usage);
        cache.Set(rateKey, now, TimeSpan.FromMinutes(5));
        cache.Set(sessionKey, sessionCount + hints.Count, TimeSpan.FromHours(6));
        cache.Set(dailyCostKey, dailyCost + costForThisCall, EndOfDay(now));

        return new WritingCoachResponse(request.SessionId, hints,
            Throttled: false,
            DailyCapReached: dailyCost + costForThisCall >= opts.CoachDailyCostCapPerLearnerUsd,
            HintsRemainingInSession: Math.Max(0, opts.CoachMaxHintsPerSession - sessionCount - hints.Count),
            SecondsUntilNextHint: opts.CoachMinSecondsBetweenHints);
    }

    private static string BuildCoachInput(WritingCoachRequest request)
    {
        return string.Join('\n',
            $"Profession: {request.Profession ?? "medicine"}",
            $"Letter type: {request.LetterType ?? "routine_referral"}",
            $"Word count: {request.WordCount}",
            "Letter so far:",
            "---",
            request.DraftContent,
            "---",
            "Return JSON { \"hints\": [{\"category\":\"style|structure|length|encouragement\",\"text\":\"≤12 words\",\"ruleId\":\"optional\",\"charStart\":int?,\"charEnd\":int?}] } with at most 4 hints.");
    }

    private static IReadOnlyList<WritingCoachHint> ParseCoachHints(string completion, int draftLength)
    {
        if (string.IsNullOrWhiteSpace(completion)) return Array.Empty<WritingCoachHint>();
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return Array.Empty<WritingCoachHint>();
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("hints", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<WritingCoachHint>();
            }
            var hints = new List<WritingCoachHint>();
            foreach (var h in arr.EnumerateArray())
            {
                var category = h.TryGetProperty("category", out var cEl) && cEl.ValueKind == JsonValueKind.String
                    ? NormalizeCategory(cEl.GetString())
                    : "style";
                var text = h.TryGetProperty("text", out var tEl) && tEl.ValueKind == JsonValueKind.String
                    ? NormalizeHintText(tEl.GetString())
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var ruleId = h.TryGetProperty("ruleId", out var rEl) && rEl.ValueKind == JsonValueKind.String ? NormalizeRuleId(rEl.GetString()) : null;
                var charStart = h.TryGetProperty("charStart", out var csEl) && csEl.TryGetInt32(out var cs) ? cs : (int?)null;
                var charEnd = h.TryGetProperty("charEnd", out var ceEl) && ceEl.TryGetInt32(out var ce) ? ce : (int?)null;
                if (charStart is < 0 || charEnd is < 0 || (charStart.HasValue && charEnd.HasValue && charEnd <= charStart) || charEnd > draftLength)
                {
                    charStart = null;
                    charEnd = null;
                }
                hints.Add(new WritingCoachHint(category, text, ruleId, charStart, charEnd));
            }
            return hints.Take(4).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WritingCoachHint>();
        }
    }

    private static string NormalizeCategory(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "structure" => "structure",
            "length" => "length",
            "encouragement" => "encouragement",
            _ => "style",
        };

    private static string NormalizeHintText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= 180 ? text : text[..180];
    }

    private static string? NormalizeRuleId(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length is < 2 or > 32) return null;
        return text.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_') ? text : null;
    }

    private async Task PersistSessionAsync(WritingCoachRequest request, IReadOnlyList<WritingCoachHint> hints, CancellationToken ct)
    {
        // The legacy WritingCoachSession/WritingCoachSuggestion entities use a
        // schema (AttemptId / SuggestionType / OriginalText / SuggestedText /
        // Explanation / StartOffset / EndOffset) that does not map cleanly
        // onto the V2 hint shape (Category / Text / RuleId / Char*). We keep
        // the legacy tables untouched here so V1 endpoints continue to read
        // their own historical data; V2 hints are returned to the caller and
        // tracked in-memory (rate limiter + per-session counter via IMemoryCache).
        //
        // A dedicated WritingCoachHintV2 table can replace this when the V2
        // hint history needs server-side persistence — out of scope for WS5.
        await Task.CompletedTask;
        _ = (db, request, hints);
    }

    private static decimal EstimateCallCost(AiUsage? usage)
    {
        if (usage is null) return 0m;
        // Approximate Haiku 4.5 pricing: $1 / 1M input + $5 / 1M output tokens.
        var input = (decimal)usage.PromptTokens / 1_000_000m;
        var output = (decimal)usage.CompletionTokens / 1_000_000m;
        return input + output * 5m;
    }

    private static TimeSpan EndOfDay(DateTimeOffset now)
    {
        var midnightUtc = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return midnightUtc - now;
    }

    public async Task<IReadOnlyList<WritingCoachHintResponse>> RequestHintsAsync(string userId, WritingCoachHintRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await RequestHintAsync(new WritingCoachRequest(
            UserId: userId,
            SessionId: request.SessionId,
            ScenarioId: request.ScenarioId,
            DraftContent: request.LetterContent,
            WordCount: request.WordCount,
            LetterType: request.LetterType,
            Profession: request.Profession), ct);
        var now = clock.GetUtcNow();
        return response.Hints.Select(h => new WritingCoachHintResponse(
            Id: Guid.NewGuid().ToString("N"),
            SessionId: request.SessionId,
            Category: h.Category,
            Text: h.Text,
            RuleId: h.RuleId,
            CharStart: h.CharStart,
            CharEnd: h.CharEnd,
            CreatedAt: now,
            Dismissed: false)).ToList();
    }
}
