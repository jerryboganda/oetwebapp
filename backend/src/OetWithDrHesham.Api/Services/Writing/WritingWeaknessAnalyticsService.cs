using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Aggregates a learner's Writing weakness signals into the shape expected by
/// the <c>/writing/analytics</c> learner page. Today the source of truth is
/// <c>WritingRuleViolation</c> rows produced by the writing evaluation
/// pipeline; the service also surfaces the latest grade and per-criterion
/// (Purpose) trend pulled from <c>Evaluation</c> rows so spec §14 cards have
/// real data. Future sources (anchored expert comments, AI issue JSON,
/// accepted coach suggestions) can be added without changing the response
/// shape.
/// </summary>
public sealed class WritingWeaknessAnalyticsService(
    LearnerDbContext db,
    TimeProvider clock)
{
    /// <summary>The 8 canonical tags rendered by the learner analytics page —
    /// must stay in sync with <c>lib/writing-analytics/types.ts</c>. Unknown
    /// rule ids are dropped so a typo in the rulebook never breaks the chart.</summary>
    private static readonly IReadOnlyList<string> CanonicalTags = new[]
    {
        "missing_key_content",
        "irrelevant_content",
        "unclear_purpose",
        "informal_tone",
        "abbreviation_issue",
        "poor_paragraphing",
        "inaccurate_transfer",
        "grammar_articles",
    };

    private static readonly Dictionary<string, string> TagLabels = new()
    {
        ["missing_key_content"] = "Missing key content",
        ["irrelevant_content"] = "Irrelevant content",
        ["unclear_purpose"] = "Unclear purpose",
        ["informal_tone"] = "Informal tone",
        ["abbreviation_issue"] = "Abbreviation issue",
        ["poor_paragraphing"] = "Poor paragraphing",
        ["inaccurate_transfer"] = "Inaccurate transfer of facts",
        ["grammar_articles"] = "Grammar / articles",
    };

    /// <summary>Maps rulebook rule ids (the dominant source today) to the
    /// canonical analytics tags. Rules not in this map contribute no tagged
    /// signal — they still count in the trend bucket but not in topTags.
    /// Keep the keys lowercase + underscore-separated to match the
    /// rulebook conventions.</summary>
    private static readonly Dictionary<string, (string tag, string? criterion)> RuleIdToTag =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Content selection / transfer
            ["missing_key_content"] = ("missing_key_content", "content"),
            ["irrelevant_content"] = ("irrelevant_content", "conciseness"),
            ["inaccurate_transfer"] = ("inaccurate_transfer", "content"),
            // Purpose
            ["unclear_purpose"] = ("unclear_purpose", "purpose"),
            ["missing_purpose"] = ("unclear_purpose", "purpose"),
            // Genre / style
            ["informal_tone"] = ("informal_tone", "genre"),
            ["abbreviation_issue"] = ("abbreviation_issue", "genre"),
            ["abbreviation_overuse"] = ("abbreviation_issue", "genre"),
            // Organisation
            ["poor_paragraphing"] = ("poor_paragraphing", "organization"),
            ["paragraph_order"] = ("poor_paragraphing", "organization"),
            // Language
            ["grammar_articles"] = ("grammar_articles", "language"),
            ["article_misuse"] = ("grammar_articles", "language"),
        };

    public async Task<WeaknessSummaryDto> ComputeForLearnerAsync(
        string userId, int trendDays, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));

        var clampedDays = Math.Clamp(trendDays <= 0 ? 14 : trendDays, 7, 90);
        var now = clock.GetUtcNow();
        var endDate = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var since = endDate.AddDays(-(clampedDays - 1));

        // Source 1: rule violations (the dominant signal today).
        var violations = await db.WritingRuleViolations
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.GeneratedAt >= since)
            .Select(v => new { v.RuleId, v.GeneratedAt })
            .ToListAsync(ct);

        // Source 2: evaluations for grade-trend + purpose-trend (spec §14).
        var evaluations = await db.Evaluations
            .AsNoTracking()
            .Join(db.Attempts.AsNoTracking(),
                e => e.AttemptId, a => a.Id,
                (e, a) => new { e, a })
            .Where(x => x.a.UserId == userId
                     && x.e.SubtestCode == "writing"
                     && x.e.CreatedAt >= since)
            .OrderBy(x => x.e.CreatedAt)
            .Select(x => new
            {
                x.e.Id,
                x.e.CreatedAt,
                x.e.ScoreRange,
                x.e.GradeRange,
                x.e.CriterionScoresJson,
            })
            .ToListAsync(ct);

        // Build tag buckets.
        var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var criterionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var trendBuckets = BuildEmptyTrend(endDate, clampedDays);
        var trendIndex = trendBuckets
            .Select((b, i) => (b.Date, i))
            .ToDictionary(t => t.Date, t => t.i, StringComparer.Ordinal);

        DateTimeOffset? firstSeen = null;
        DateTimeOffset? lastSeen = null;

        foreach (var v in violations)
        {
            if (RuleIdToTag.TryGetValue(v.RuleId, out var mapping))
            {
                tagCounts.TryGetValue(mapping.tag, out var c);
                tagCounts[mapping.tag] = c + 1;
                if (mapping.criterion is not null)
                {
                    criterionCounts.TryGetValue(mapping.criterion, out var cc);
                    criterionCounts[mapping.criterion] = cc + 1;
                }
            }
            var iso = v.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd");
            if (trendIndex.TryGetValue(iso, out var idx))
            {
                trendBuckets[idx] = trendBuckets[idx] with { Count = trendBuckets[idx].Count + 1 };
            }
            firstSeen = firstSeen is null || v.GeneratedAt < firstSeen ? v.GeneratedAt : firstSeen;
            lastSeen = lastSeen is null || v.GeneratedAt > lastSeen ? v.GeneratedAt : lastSeen;
        }

        var total = violations.Count;

        var topTags = tagCounts
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(kv => new WeaknessTagDto(
                Tag: kv.Key,
                Label: TagLabels.TryGetValue(kv.Key, out var lbl) ? lbl : kv.Key,
                Count: kv.Value,
                Share: total > 0 ? kv.Value / (double)total : 0))
            .ToList();

        var criterionTotal = criterionCounts.Values.Sum();
        var byCriterion = criterionCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WeaknessCriterionDto(
                Criterion: kv.Key,
                Label: CriterionLabel(kv.Key),
                Count: kv.Value,
                Share: criterionTotal > 0 ? kv.Value / (double)criterionTotal : 0))
            .ToList();

        var gradeTrend = evaluations
            .Where(e => !string.IsNullOrWhiteSpace(e.GradeRange))
            .Select(e => new GradeTrendPointDto(
                Date: e.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd"),
                GradeRange: e.GradeRange!,
                ScoreRange: e.ScoreRange))
            .ToList();

        var purposeTrend = evaluations
            .Select(e =>
            {
                var score = TryReadCriterionScore(e.CriterionScoresJson, "purpose");
                return score is null
                    ? null
                    : new PurposeTrendPointDto(
                        Date: e.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd"),
                        Score: score.Value,
                        MaxScore: 3);
            })
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        return new WeaknessSummaryDto(
            TotalObservations: total,
            TopTags: topTags,
            ByCriterion: byCriterion,
            Trend: trendBuckets,
            FirstSeenAt: firstSeen?.UtcDateTime.ToString("o") ?? string.Empty,
            LastSeenAt: lastSeen?.UtcDateTime.ToString("o") ?? string.Empty,
            GradeTrend: gradeTrend,
            PurposeTrend: purposeTrend);
    }

    private static List<WeaknessTrendBucketDto> BuildEmptyTrend(DateTimeOffset endDate, int days)
    {
        var buckets = new List<WeaknessTrendBucketDto>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var d = endDate.AddDays(-i).UtcDateTime.ToString("yyyy-MM-dd");
            buckets.Add(new WeaknessTrendBucketDto(d, 0));
        }
        return buckets;
    }

    private static string CriterionLabel(string criterion) => criterion switch
    {
        "purpose" => "Purpose",
        "content" => "Content",
        "conciseness" => "Conciseness & Clarity",
        "genre" => "Genre & Style",
        "organization" => "Organisation & Layout",
        "language" => "Language",
        _ => criterion,
    };

    private static int? TryReadCriterionScore(string? json, string criterionCode)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("criterionCode", out var codeProp)
                    && !entry.TryGetProperty("code", out codeProp))
                {
                    continue;
                }
                if (codeProp.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(codeProp.GetString(), criterionCode, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.TryGetProperty("score", out var scoreProp) && scoreProp.TryGetInt32(out var s))
                    return s;
            }
        }
        catch (JsonException) { /* malformed JSON — drop the row */ }
        return null;
    }
}

public sealed record WeaknessSummaryDto(
    int TotalObservations,
    IReadOnlyList<WeaknessTagDto> TopTags,
    IReadOnlyList<WeaknessCriterionDto> ByCriterion,
    IReadOnlyList<WeaknessTrendBucketDto> Trend,
    string FirstSeenAt,
    string LastSeenAt,
    IReadOnlyList<GradeTrendPointDto> GradeTrend,
    IReadOnlyList<PurposeTrendPointDto> PurposeTrend);

public sealed record WeaknessTagDto(string Tag, string Label, int Count, double Share);
public sealed record WeaknessCriterionDto(string Criterion, string Label, int Count, double Share);
public sealed record WeaknessTrendBucketDto(string Date, int Count);
public sealed record GradeTrendPointDto(string Date, string GradeRange, string ScoreRange);
public sealed record PurposeTrendPointDto(string Date, int Score, int MaxScore);
