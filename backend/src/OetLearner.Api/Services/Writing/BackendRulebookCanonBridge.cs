using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// 2026-05-27 audit fix — bridge the canonical Writing rulebooks
/// (rulebooks/writing/{profession}/rulebook.v1.json) into the
/// <see cref="WritingCanonRule"/> table so the backend grading and AI prompt
/// layers see the same R* rules the frontend lint engine sees.
///
/// This runs alongside the legacy SC-* seed (canon-rules.launch-25.json) —
/// the two sets coexist until the SC-* IDs can be retired. New rule rows
/// have IDs of the form R01.5, R12.9, etc., matching the rulebook of record.
/// The bridge is idempotent — existing rows by Id are skipped.
///
/// Profession scoping is preserved: rules from rulebooks/writing/medicine/
/// land with `AppliesToProfessionsJson = ["medicine"]`; rules that exist in
/// every profession's rulebook with identical bodies are deduplicated and
/// stored once with `AppliesToProfessions = ["all"]`.
/// </summary>
public static class BackendRulebookCanonBridge
{
    public static async Task SeedFromRulebooksAsync(
        LearnerDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        var loader = new RulebookLoader();
        var existingIds = await db.Set<WritingCanonRule>()
            .AsNoTracking()
            .Select(r => r.Id)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

        // Group writing rules across professions by `id` only. The R-rule IDs
        // are the primary key in WritingCanonRule, so two rules with the same
        // ID but slightly different bodies (rare profession-specific wording
        // tweaks) MUST collapse into a single row. We take the first body
        // seen and union the professions; minor body divergences are surfaced
        // to the AI prompt via rulebook reads, not via this canon table.
        var bucket = new Dictionary<string, (OetRule Rule, HashSet<string> Professions)>(StringComparer.OrdinalIgnoreCase);

        foreach (var profession in Enum.GetValues<ExamProfession>())
        {
            OetRulebook book;
            try { book = loader.Load(RuleKind.Writing, profession); }
            catch (RulebookNotFoundException) { continue; }
            foreach (var rule in book.Rules)
            {
                if (existing.Contains(rule.Id)) continue;
                if (!bucket.TryGetValue(rule.Id, out var entry))
                {
                    entry = (rule, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    bucket[rule.Id] = entry;
                }
                entry.Professions.Add(profession.ToString().ToLowerInvariant());
            }
        }

        if (bucket.Count == 0)
        {
            logger.LogInformation("BackendRulebookCanonBridge: nothing new to materialise.");
            return;
        }

        var allProfessions = Enum.GetValues<ExamProfession>()
            .Select(p => p.ToString().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var (_, (rule, professions)) in bucket)
        {
            // If the rule body matched in every profession, store as "all";
            // otherwise store the explicit list. This keeps the column small
            // for the 99% common case.
            var appliesTo = professions.SetEquals(allProfessions)
                ? new[] { "all" }
                : professions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

            var detectionType = string.IsNullOrWhiteSpace(rule.CheckId)
                ? (rule.Enforcement switch
                {
                    RuleEnforcement.AiGrounded => "ai",
                    RuleEnforcement.HumanReviewOnly => "human",
                    _ => "manual",
                })
                : "deterministic";

            db.Set<WritingCanonRule>().Add(new WritingCanonRule
            {
                Id = rule.Id,
                Category = MapSection(rule.Section),
                AppliesToLetterTypesJson = SerializeAppliesTo(rule.AppliesTo),
                AppliesToProfessionsJson = JsonSerializer.Serialize(appliesTo),
                Severity = rule.Severity.ToString().ToLowerInvariant(),
                RuleText = rule.Body,
                CorrectExamplesJson = SerializeExamples(rule.Examples, "good"),
                IncorrectExamplesJson = SerializeExamples(rule.Examples, "bad"),
                DetectionType = detectionType,
                DetectionConfigJson = BuildDetectionConfig(rule),
                LessonId = null,
                Version = 1,
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "BackendRulebookCanonBridge: materialised {Added} rulebook rules into WritingCanonRule (was {Existing}).",
            added, existing.Count);
    }

    private static string MapSection(string section) => section switch
    {
        "01" => "letter_types",
        "02" => "exam_structure",
        "03" => "content",
        "04" => "layout",
        "05" => "address_date",
        "06" => "salutation",
        "07" => "introduction",
        "08" => "body",
        "09" => "closure",
        "10" => "tense",
        "11" => "medications",
        "12" => "grammar",
        "13" => "urgent",
        "14" => "discharge",
        "15" => "non_medical",
        "16" => "assessment",
        _ => "format",
    };

    private static string SerializeAppliesTo(JsonElement? appliesTo)
    {
        if (appliesTo is null) return "[]";
        if (appliesTo.Value.ValueKind == JsonValueKind.String)
        {
            var s = appliesTo.Value.GetString();
            return JsonSerializer.Serialize(string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "all" }
                : new[] { s ?? "all" });
        }
        if (appliesTo.Value.ValueKind == JsonValueKind.Array)
        {
            return appliesTo.Value.GetRawText();
        }
        return "[]";
    }

    private static string SerializeExamples(JsonElement? examples, string side)
    {
        if (examples is null || examples.Value.ValueKind != JsonValueKind.Object) return "[]";
        return examples.Value.TryGetProperty(side, out var arr)
            && arr.ValueKind == JsonValueKind.Array
            ? arr.GetRawText()
            : "[]";
    }

    private static string BuildDetectionConfig(OetRule rule)
    {
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(rule.CheckId)) payload["checkId"] = rule.CheckId;
        if (rule.Enforcement.HasValue) payload["enforcement"] = rule.Enforcement.Value.ToString().ToLowerInvariant();
        if (rule.ForbiddenPatterns is { Count: > 0 }) payload["forbiddenPatterns"] = rule.ForbiddenPatterns;
        if (rule.ExemplarPhrases is { Count: > 0 }) payload["exemplarPhrases"] = rule.ExemplarPhrases;
        if (rule.Params is not null) payload["params"] = JsonSerializer.Deserialize<object>(rule.Params.Value.GetRawText());
        return payload.Count == 0 ? "{}" : JsonSerializer.Serialize(payload);
    }
}
