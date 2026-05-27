using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Surfaces the Grammar rulebook content (sections + rules) to the learner-facing
/// /v1/grammar API. The rulebook JSON files at rulebooks/grammar/{profession}/rulebook.v1.json
/// are the single source of truth — this service does NOT introduce a new
/// database table for content. Per-learner progress is intentionally out of
/// scope for the initial cut and will be added in a follow-up via the existing
/// LearnerActivity pattern; the audit explicitly called out that the Grammar
/// module had no backend API at all, so this closes the surface gap.
/// </summary>
public interface IGrammarRulebookService
{
    GrammarTopicSummary[] GetTopics(ExamProfession profession);
    GrammarLessonView? GetLesson(ExamProfession profession, string ruleId);
    GrammarLessonViewList GetLessonsForTopic(ExamProfession profession, string sectionId);
}

public sealed class GrammarRulebookService(IServiceScopeFactory scopeFactory) : IGrammarRulebookService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    public GrammarTopicSummary[] GetTopics(ExamProfession profession)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IRulebookLoader>();
        var book = loader.Load(RuleKind.Grammar, profession);
        return book.Sections
            .OrderBy(s => s.Order ?? 0)
            .Select(s =>
            {
                var count = book.Rules.Count(r => r.Section == s.Id);
                var critical = book.Rules.Count(r => r.Section == s.Id && r.Severity == RuleSeverity.Critical);
                return new GrammarTopicSummary(s.Id, s.Title, s.Order ?? 0, count, critical);
            })
            .ToArray();
    }

    public GrammarLessonViewList GetLessonsForTopic(ExamProfession profession, string sectionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IRulebookLoader>();
        var book = loader.Load(RuleKind.Grammar, profession);
        var section = book.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (section is null) throw new KeyNotFoundException($"Grammar topic '{sectionId}' not found for profession {profession}.");
        var lessons = book.Rules
            .Where(r => string.Equals(r.Section, sectionId, StringComparison.OrdinalIgnoreCase))
            .Select(r => new GrammarLessonView(
                r.Id,
                r.Title,
                r.Body,
                r.Severity,
                r.ExemplarPhrases?.ToArray() ?? Array.Empty<string>(),
                BuildExamples(r)))
            .ToArray();
        return new GrammarLessonViewList(section.Id, section.Title, lessons);
    }

    public GrammarLessonView? GetLesson(ExamProfession profession, string ruleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IRulebookLoader>();
        var rule = loader.FindRule(RuleKind.Grammar, profession, ruleId);
        if (rule is null) return null;
        return new GrammarLessonView(
            rule.Id,
            rule.Title,
            rule.Body,
            rule.Severity,
            rule.ExemplarPhrases?.ToArray() ?? Array.Empty<string>(),
            BuildExamples(rule));
    }

    private static GrammarExample[] BuildExamples(OetRule rule)
    {
        if (rule.Examples is null) return Array.Empty<GrammarExample>();
        var good = new List<string>();
        var bad = new List<string>();
        if (rule.Examples.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (rule.Examples.Value.TryGetProperty("good", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in g.EnumerateArray()) good.Add(item.GetString() ?? "");
            }
            if (rule.Examples.Value.TryGetProperty("bad", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in b.EnumerateArray()) bad.Add(item.GetString() ?? "");
            }
        }
        var max = Math.Max(good.Count, bad.Count);
        var examples = new List<GrammarExample>(max);
        for (var i = 0; i < max; i++)
        {
            examples.Add(new GrammarExample(
                i < good.Count ? good[i] : null,
                i < bad.Count ? bad[i] : null));
        }
        return examples.ToArray();
    }
}

public sealed record GrammarTopicSummary(string Id, string Title, int Order, int LessonCount, int CriticalCount);

public sealed record GrammarLessonView(
    string RuleId,
    string Title,
    string Body,
    RuleSeverity Severity,
    string[] ExemplarPhrases,
    GrammarExample[] Examples);

public sealed record GrammarExample(string? Good, string? Bad);

public sealed record GrammarLessonViewList(string SectionId, string SectionTitle, GrammarLessonView[] Lessons);
