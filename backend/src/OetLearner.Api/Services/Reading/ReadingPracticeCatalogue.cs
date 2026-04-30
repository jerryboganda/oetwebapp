using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Practice Catalogue — Phase 3b
//
// Static catalogue of skill-scoped drill templates and a sampler that picks
// matching ReadingQuestion ids for a given paper. Drill templates are
// hardcoded — they describe an OET teaching pattern, not user content. The
// `SkillTag` value matches the lower-cased skillTag column on
// ReadingQuestion authored via the structure editor; the sampler is
// permissive when a question carries no skill tag (Part-A scan + Part-B
// distractor templates rely on PartCode alone).
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ReadingDrillTemplate(
    string Code,
    string Title,
    string Description,
    ReadingPartCode PartCode,
    string? SkillTag,
    int QuestionCount,
    int Minutes);

public static class ReadingDrillCatalogue
{
    public static readonly IReadOnlyList<ReadingDrillTemplate> All = new[]
    {
        new ReadingDrillTemplate(
            Code: "part-a-scan",
            Title: "Part A: rapid scanning",
            Description: "Scan four short medical texts for facts, numbers, and drug names.",
            PartCode: ReadingPartCode.A,
            SkillTag: null,
            QuestionCount: 8,
            Minutes: 6),
        new ReadingDrillTemplate(
            Code: "part-b-distractor",
            Title: "Part B: workplace distractors",
            Description: "Identify the option supported by the exact wording of short workplace extracts.",
            PartCode: ReadingPartCode.B,
            SkillTag: null,
            QuestionCount: 6,
            Minutes: 8),
        new ReadingDrillTemplate(
            Code: "part-c-inference",
            Title: "Part C: inference",
            Description: "Work out implied meaning from longer healthcare articles.",
            PartCode: ReadingPartCode.C,
            SkillTag: "inference",
            QuestionCount: 6,
            Minutes: 10),
        new ReadingDrillTemplate(
            Code: "part-c-attitude",
            Title: "Part C: writer attitude",
            Description: "Pin down the writer or quoted expert's view in long-form healthcare texts.",
            PartCode: ReadingPartCode.C,
            SkillTag: "attitude",
            QuestionCount: 6,
            Minutes: 10),
        new ReadingDrillTemplate(
            Code: "part-c-vocabulary",
            Title: "Part C: vocabulary in context",
            Description: "Decode unfamiliar words and phrases from surrounding meaning.",
            PartCode: ReadingPartCode.C,
            SkillTag: "vocabulary",
            QuestionCount: 6,
            Minutes: 8),
        new ReadingDrillTemplate(
            Code: "part-c-reference",
            Title: "Part C: reference",
            Description: "Resolve pronouns and references like \"it\", \"this\", \"they\".",
            PartCode: ReadingPartCode.C,
            SkillTag: "reference",
            QuestionCount: 6,
            Minutes: 8),
    };

    public static ReadingDrillTemplate? Find(string code) =>
        All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
}

public static class ReadingPracticeSampler
{
    /// <summary>
    /// Sample up to <paramref name="count"/> question ids on the given paper
    /// matching <paramref name="partCode"/> and (optionally) <paramref name="skillTag"/>.
    /// Returns a deterministic-per-call shuffled list; callers persist the
    /// result on <c>ReadingAttempt.ScopeJson</c> so re-grading uses the
    /// same subset.
    /// </summary>
    public static async Task<List<string>> SampleAsync(
        LearnerDbContext db,
        string paperId,
        ReadingPartCode partCode,
        string? skillTag,
        int count,
        CancellationToken ct)
    {
        if (count <= 0) return new();

        var query = db.ReadingQuestions.AsNoTracking()
            .Where(q => q.Part!.PaperId == paperId && q.Part.PartCode == partCode);
        if (!string.IsNullOrWhiteSpace(skillTag))
        {
            var tagLower = skillTag.ToLowerInvariant();
            query = query.Where(q => q.SkillTag != null && q.SkillTag.ToLower() == tagLower);
        }

        var ids = await query
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return new();

        // Deterministic shuffle using a per-call seed. Callers don't need
        // cryptographic randomness — they just need the same list within
        // one HTTP request so the persisted scope matches the rendered set.
        var rnd = new Random();
        return ids.OrderBy(_ => rnd.Next()).Take(count).ToList();
    }

    /// <summary>
    /// Sample a balanced subset across Parts A, B, C for mini-tests. Roughly
    /// preserves the canonical 20:6:16 ratio while clamping to
    /// <paramref name="count"/> total.
    /// </summary>
    public static async Task<List<string>> SampleMixedAsync(
        LearnerDbContext db,
        string paperId,
        int count,
        CancellationToken ct)
    {
        if (count <= 0) return new();

        var rnd = new Random();
        var picked = new List<string>(count);
        var ratios = new (ReadingPartCode part, double share)[]
        {
            (ReadingPartCode.A, 20.0 / 42.0),
            (ReadingPartCode.B, 6.0 / 42.0),
            (ReadingPartCode.C, 16.0 / 42.0),
        };
        foreach (var (part, share) in ratios)
        {
            var want = Math.Max(1, (int)Math.Round(count * share));
            var ids = await db.ReadingQuestions.AsNoTracking()
                .Where(q => q.Part!.PaperId == paperId && q.Part.PartCode == part)
                .Select(q => q.Id)
                .ToListAsync(ct);
            picked.AddRange(ids.OrderBy(_ => rnd.Next()).Take(want));
        }
        return picked.Take(count).ToList();
    }
}
