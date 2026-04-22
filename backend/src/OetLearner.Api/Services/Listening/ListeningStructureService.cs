using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening structure service — Publish-gate validator
//
// Validates the canonical OET Listening shape before a ContentPaper flips
// to Published. Non-configurable invariant:
//
//   Part A = 24 items (two consultations, 12 items each; partCode A / A1 / A2)
//   Part B =  6 items (six workplace extracts, one 3-option MCQ each; partCode B)
//   Part C = 12 items (two presentations, 6 items each; partCode C / C1 / C2)
//   ───────────────
//   Total  = 42 items  (30 / 42 ≡ 350 / 500 pass anchor via OetScoring)
//
// Questions are authored in ContentPaper.ExtractedTextJson under the
// "listeningQuestions" key — the same shape the learner player consumes
// (lib/listening-sections.ts / ListeningLearnerService.ExtractQuestions).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningStructureService
{
    Task<ListeningValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct);
}

public sealed record ListeningValidationReport(
    bool IsPublishReady,
    IReadOnlyList<ListeningValidationIssue> Issues,
    ListeningValidationCounts Counts);

public sealed record ListeningValidationIssue(
    string Code,
    string Severity, // "error" | "warning"
    string Message);

public sealed record ListeningValidationCounts(
    int PartACount, int PartBCount, int PartCCount, int TotalItems);

public sealed class ListeningStructureService(LearnerDbContext db) : IListeningStructureService
{
    /// <summary>Canonical OET Listening shape. Non-configurable invariant.</summary>
    public const int CanonicalPartACount = 24;
    public const int CanonicalPartBCount = 6;
    public const int CanonicalPartCCount = 12;
    public const int CanonicalTotalItems = CanonicalPartACount + CanonicalPartBCount + CanonicalPartCCount; // 42

    public async Task<ListeningValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        var issues = new List<ListeningValidationIssue>();
        var (partA, partB, partC, total, structuralWarnings) = CountItems(paper.ExtractedTextJson);
        issues.AddRange(structuralWarnings);

        if (total == 0)
        {
            issues.Add(new(
                Code: "listening_no_items",
                Severity: "error",
                Message: "No authored Listening questions were found in ExtractedTextJson.listeningQuestions."));
        }
        else
        {
            if (partA != CanonicalPartACount)
                issues.Add(new("listening_part_a_count", "error",
                    $"Part A has {partA} item(s); OET requires exactly {CanonicalPartACount} (12 per consultation)."));
            if (partB != CanonicalPartBCount)
                issues.Add(new("listening_part_b_count", "error",
                    $"Part B has {partB} item(s); OET requires exactly {CanonicalPartBCount} (one per workplace extract)."));
            if (partC != CanonicalPartCCount)
                issues.Add(new("listening_part_c_count", "error",
                    $"Part C has {partC} item(s); OET requires exactly {CanonicalPartCCount} (6 per presentation)."));
        }

        var isPublishReady = !issues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new ListeningValidationReport(
            isPublishReady,
            issues,
            new ListeningValidationCounts(partA, partB, partC, total));
    }

    /// <summary>Parse ExtractedTextJson.listeningQuestions and tally items by
    /// normalized part. Accepts both granular (A1/A2/C1/C2) and legacy (A/C)
    /// partCodes. Also validates Part B items expose a 3-option MCQ.</summary>
    private static (int partA, int partB, int partC, int total, List<ListeningValidationIssue> warnings)
        CountItems(string? extractedTextJson)
    {
        var warnings = new List<ListeningValidationIssue>();
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return (0, 0, 0, 0, warnings);

        List<Dictionary<string, object?>> questions;
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault("listeningQuestions");
            if (raw is null) return (0, 0, 0, 0, warnings);
            questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                        ?? new List<Dictionary<string, object?>>();
        }
        catch (JsonException)
        {
            warnings.Add(new("listening_invalid_json", "error",
                "ExtractedTextJson is not valid JSON; cannot validate Listening structure."));
            return (0, 0, 0, 0, warnings);
        }

        int a = 0, b = 0, c = 0;
        var partBItemsWithoutThreeOptions = 0;

        foreach (var q in questions)
        {
            var partCode = (q.GetValueOrDefault("partCode") ?? q.GetValueOrDefault("part"))?.ToString()
                ?.Trim().ToUpperInvariant() ?? "A";

            if (partCode.StartsWith("A", StringComparison.Ordinal)) a++;
            else if (partCode.StartsWith("B", StringComparison.Ordinal))
            {
                b++;
                if (CountOptions(q) != 3) partBItemsWithoutThreeOptions++;
            }
            else if (partCode.StartsWith("C", StringComparison.Ordinal)) c++;
        }

        if (partBItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_b_mcq_shape", "error",
                $"Part B requires single-select MCQ with exactly 3 options per item; {partBItemsWithoutThreeOptions} item(s) violate this."));
        }

        return (a, b, c, a + b + c, warnings);
    }

    private static int CountOptions(Dictionary<string, object?> question)
    {
        var raw = question.GetValueOrDefault("options");
        if (raw is null) return 0;
        try
        {
            var list = JsonSerializer.Deserialize<List<object?>>(JsonSerializer.Serialize(raw));
            return list?.Count ?? 0;
        }
        catch (JsonException) { return 0; }
    }
}
