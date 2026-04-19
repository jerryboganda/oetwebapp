using System.Text.Json;
using System.Text.Json.Serialization;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// Pure, DB-free rule engine that scores <see cref="StudyPlanAssignmentRule"/>
/// rows against a <see cref="LearnerPlanContext"/>. The caller pre-loads the
/// rules, the engine has no side effects, and every decision is deterministic
/// and unit-testable.
/// </summary>
public interface IStudyPlannerRuleEngine
{
    RuleMatchResult Match(LearnerPlanContext ctx, IReadOnlyList<StudyPlanAssignmentRule> candidates);
}

public sealed class StudyPlannerRuleEngine : IStudyPlannerRuleEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public RuleMatchResult Match(LearnerPlanContext ctx, IReadOnlyList<StudyPlanAssignmentRule> candidates)
    {
        var matched = new List<(StudyPlanAssignmentRule Rule, int Score)>();
        var considered = new List<string>(candidates.Count);
        foreach (var rule in candidates.Where(r => r.IsActive && r.ExamFamilyCode == ctx.ExamFamilyCode))
        {
            considered.Add(rule.Id);
            var cond = ParseCondition(rule.ConditionJson);
            if (cond is null) continue;
            if (!Matches(cond, ctx)) continue;
            // Score = weight (primary) ... Priority acts as tiebreaker (lower wins).
            matched.Add((rule, rule.Weight));
        }
        if (matched.Count == 0)
            return new RuleMatchResult(null, Array.Empty<string>(), considered, "no_rule_matched");

        var best = matched
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Rule.Priority)
            .ThenBy(m => m.Rule.CreatedAt)
            .First();
        var allMatchedIds = matched.Select(m => m.Rule.Id).ToArray();
        return new RuleMatchResult(best.Rule.TargetTemplateId, allMatchedIds, considered, "rule_matched");
    }

    private static RuleCondition? ParseCondition(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RuleCondition();
        try { return JsonSerializer.Deserialize<RuleCondition>(json, JsonOpts); }
        catch { return null; }
    }

    private static bool Matches(RuleCondition c, LearnerPlanContext ctx)
    {
        if (c.Professions is { Count: > 0 } profs)
        {
            if (ctx.ProfessionId is null || !profs.Contains(ctx.ProfessionId, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (c.Countries is { Count: > 0 } countries)
        {
            if (ctx.TargetCountry is null || !countries.Contains(ctx.TargetCountry, StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (c.MinWeeksToExam is int minw && (ctx.WeeksToExam is null || ctx.WeeksToExam < minw)) return false;
        if (c.MaxWeeksToExam is int maxw && (ctx.WeeksToExam is null || ctx.WeeksToExam > maxw)) return false;
        if (c.MinHoursPerWeek is int minh && (ctx.HoursPerWeek is null || ctx.HoursPerWeek < minh)) return false;
        if (c.MaxHoursPerWeek is int maxh && (ctx.HoursPerWeek is null || ctx.HoursPerWeek > maxh)) return false;
        if (c.MinOverallTarget is int target)
        {
            var any = new[] { ctx.TargetWritingScore, ctx.TargetSpeakingScore, ctx.TargetReadingScore, ctx.TargetListeningScore }
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            if (any.Length == 0 || any.Max() < target) return false;
        }
        if (c.WeakSubtests is { Count: > 0 } weaks)
        {
            var require = (c.RequireWeakSubtests ?? "any").ToLowerInvariant();
            var ctxWeak = ctx.WeakSubtests ?? Array.Empty<string>();
            if (require == "all")
            {
                if (!weaks.All(w => ctxWeak.Contains(w, StringComparer.OrdinalIgnoreCase))) return false;
            }
            else // any
            {
                if (!weaks.Any(w => ctxWeak.Contains(w, StringComparer.OrdinalIgnoreCase))) return false;
            }
        }
        return true;
    }

    private sealed class RuleCondition
    {
        [JsonPropertyName("professions")] public List<string>? Professions { get; set; }
        [JsonPropertyName("countries")] public List<string>? Countries { get; set; }
        [JsonPropertyName("minWeeksToExam")] public int? MinWeeksToExam { get; set; }
        [JsonPropertyName("maxWeeksToExam")] public int? MaxWeeksToExam { get; set; }
        [JsonPropertyName("minHoursPerWeek")] public int? MinHoursPerWeek { get; set; }
        [JsonPropertyName("maxHoursPerWeek")] public int? MaxHoursPerWeek { get; set; }
        [JsonPropertyName("minOverallTarget")] public int? MinOverallTarget { get; set; }
        [JsonPropertyName("weakSubtests")] public List<string>? WeakSubtests { get; set; }
        [JsonPropertyName("requireWeakSubtests")] public string? RequireWeakSubtests { get; set; }
    }
}
