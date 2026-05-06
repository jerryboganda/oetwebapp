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
    ListeningValidationCounts Counts)
{
    /// <summary>Where the item counts came from: <c>"relational"</c> when the
    /// <c>ListeningQuestion</c> table had rows for the paper, otherwise
    /// <c>"json"</c> (legacy <c>ExtractedTextJson.listeningQuestions</c>).</summary>
    public string Source { get; init; } = "json";
}

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

        // Prefer the relational ListeningQuestion table when authoring has
        // migrated off the legacy ExtractedTextJson blob; fall back to the
        // JSON shape so older draft papers keep validating.
        var hasRelational = await db.Set<ListeningQuestion>()
            .AnyAsync(q => q.PaperId == paperId, ct);

        int partA, partB, partC, total;
        List<ListeningValidationIssue> structuralWarnings;
        string source;
        if (hasRelational)
        {
            (partA, partB, partC, total, structuralWarnings) = await CountItemsRelationalAsync(paperId, ct);
            source = "relational";
        }
        else
        {
            (partA, partB, partC, total, structuralWarnings) = CountItems(paper.ExtractedTextJson);
            source = "json";
        }
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
            new ListeningValidationCounts(partA, partB, partC, total))
        {
            Source = source,
        };
    }

    /// <summary>Tally items per part directly from the relational
    /// <see cref="ListeningQuestion"/> table by joining to
    /// <see cref="ListeningPart"/> and bucketing the <see cref="ListeningPartCode"/>
    /// (<c>A1/A2 → A</c>, <c>B → B</c>, <c>C1/C2 → C</c>).</summary>
    private async Task<(int partA, int partB, int partC, int total, List<ListeningValidationIssue> warnings)>
        CountItemsRelationalAsync(string paperId, CancellationToken ct)
    {
        var warnings = new List<ListeningValidationIssue>();

        var questions = await db.Set<ListeningQuestion>()
            .AsNoTracking()
            .Where(q => q.PaperId == paperId)
            .Include(q => q.Options)
            .ToListAsync(ct);
        var partIds = questions
            .Select(q => q.ListeningPartId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var parts = await db.Set<ListeningPart>()
            .AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartCode, StringComparer.Ordinal, ct);
        var rows = questions
            .Select(q => new
            {
                PartCode = parts.GetValueOrDefault(q.ListeningPartId),
                q.QuestionNumber,
                q.QuestionType,
                q.Stem,
                q.CorrectAnswerJson,
                Options = q.Options
                    .Select(o => new RelationalOption(o.OptionKey, o.Text, o.IsCorrect))
                    .ToArray(),
            })
            .ToList();

        var a1 = rows.Count(row => row.PartCode == ListeningPartCode.A1);
        var a2 = rows.Count(row => row.PartCode == ListeningPartCode.A2);
        var b = rows.Count(row => row.PartCode == ListeningPartCode.B);
        var c1 = rows.Count(row => row.PartCode == ListeningPartCode.C1);
        var c2 = rows.Count(row => row.PartCode == ListeningPartCode.C2);
        var a = a1 + a2;
        var c = c1 + c2;

        var duplicateNumbers = rows
            .GroupBy(row => row.QuestionNumber)
            .Count(group => group.Count() > 1);
        if (duplicateNumbers > 0)
        {
            warnings.Add(new("listening_duplicate_question_numbers", "error",
                $"Question numbers must be unique across the paper; {duplicateNumbers} duplicate number group(s) found."));
        }

        if (a1 is not 0 and not 12 || a2 is not 0 and not 12)
        {
            warnings.Add(new("listening_part_a_split", "error",
                $"Part A requires two consultations with exactly 12 items each; found A1={a1}, A2={a2}."));
        }
        if (c1 is not 0 and not 6 || c2 is not 0 and not 6)
        {
            warnings.Add(new("listening_part_c_split", "error",
                $"Part C requires two presentations with exactly 6 items each; found C1={c1}, C2={c2}."));
        }

        var blankStems = rows.Count(row => string.IsNullOrWhiteSpace(row.Stem));
        if (blankStems > 0)
        {
            warnings.Add(new("listening_blank_stems", "error",
                $"Every Listening item requires learner-facing question text; {blankStems} item(s) have a blank stem."));
        }

        var blankAnswers = rows.Count(row => string.IsNullOrWhiteSpace(ReadJsonString(row.CorrectAnswerJson)));
        if (blankAnswers > 0)
        {
            warnings.Add(new("listening_blank_answers", "error",
                $"Every Listening item requires a non-empty correct answer; {blankAnswers} item(s) are missing one."));
        }

        var partBItemsWithoutThreeOptions = rows.Count(row => row.PartCode == ListeningPartCode.B
            && !HasValidMcqShape(row.QuestionType, ReadJsonString(row.CorrectAnswerJson), row.Options));
        if (partBItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_b_mcq_shape", "error",
                $"Part B requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partBItemsWithoutThreeOptions} item(s) violate this."));
        }

        var partCItemsWithoutThreeOptions = rows.Count(row => (row.PartCode == ListeningPartCode.C1 || row.PartCode == ListeningPartCode.C2)
            && !HasValidMcqShape(row.QuestionType, ReadJsonString(row.CorrectAnswerJson), row.Options));
        if (partCItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_c_mcq_shape", "error",
                $"Part C requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partCItemsWithoutThreeOptions} item(s) violate this."));
        }

        return (a, b, c, a + b + c, warnings);
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
        int a1 = 0, a2 = 0, c1 = 0, c2 = 0;
        var partBItemsWithoutThreeOptions = 0;
        var partCItemsWithoutThreeOptions = 0;
        var blankAnswers = 0;
        var blankStems = 0;
        var numbers = new Dictionary<int, int>();

        foreach (var q in questions)
        {
            if (TryGetInt(q, "number") is int number)
            {
                numbers[number] = numbers.GetValueOrDefault(number) + 1;
            }
            if (string.IsNullOrWhiteSpace(ReadString(q, "text") ?? ReadString(q, "stem"))) blankStems++;
            if (string.IsNullOrWhiteSpace(ReadString(q, "correctAnswer"))) blankAnswers++;

            var partCode = (q.GetValueOrDefault("partCode") ?? q.GetValueOrDefault("part"))?.ToString()
                ?.Trim().ToUpperInvariant() ?? "A";

            if (partCode.StartsWith("A", StringComparison.Ordinal))
            {
                a++;
                if (partCode == "A1") a1++;
                else if (partCode == "A2") a2++;
            }
            else if (partCode.StartsWith("B", StringComparison.Ordinal))
            {
                b++;
                if (!HasValidMcqShape(q)) partBItemsWithoutThreeOptions++;
            }
            else if (partCode.StartsWith("C", StringComparison.Ordinal))
            {
                c++;
                if (partCode == "C1") c1++;
                else if (partCode == "C2") c2++;
                if (!HasValidMcqShape(q)) partCItemsWithoutThreeOptions++;
            }
        }

        var duplicateNumbers = numbers.Count(kvp => kvp.Value > 1);
        if (duplicateNumbers > 0)
        {
            warnings.Add(new("listening_duplicate_question_numbers", "error",
                $"Question numbers must be unique across the paper; {duplicateNumbers} duplicate number group(s) found."));
        }

        if ((a1 > 0 || a2 > 0) && (a1 != 12 || a2 != 12))
        {
            warnings.Add(new("listening_part_a_split", "error",
                $"Part A requires two consultations with exactly 12 items each; found A1={a1}, A2={a2}."));
        }
        if ((c1 > 0 || c2 > 0) && (c1 != 6 || c2 != 6))
        {
            warnings.Add(new("listening_part_c_split", "error",
                $"Part C requires two presentations with exactly 6 items each; found C1={c1}, C2={c2}."));
        }

        if (blankStems > 0)
        {
            warnings.Add(new("listening_blank_stems", "error",
                $"Every Listening item requires learner-facing question text; {blankStems} item(s) have a blank stem."));
        }

        if (blankAnswers > 0)
        {
            warnings.Add(new("listening_blank_answers", "error",
                $"Every Listening item requires a non-empty correct answer; {blankAnswers} item(s) are missing one."));
        }

        if (partBItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_b_mcq_shape", "error",
                $"Part B requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partBItemsWithoutThreeOptions} item(s) violate this."));
        }

        if (partCItemsWithoutThreeOptions > 0)
        {
            warnings.Add(new("listening_part_c_mcq_shape", "error",
                $"Part C requires single-select MCQ with exactly 3 options and a matching correct answer per item; {partCItemsWithoutThreeOptions} item(s) violate this."));
        }

        return (a, b, c, a + b + c, warnings);
    }

    private static bool HasValidMcqShape(Dictionary<string, object?> question)
    {
        var type = ReadString(question, "type") ?? ReadString(question, "questionType");
        var options = ReadOptions(question);
        var correctAnswer = ReadString(question, "correctAnswer")?.Trim();
        if (!string.Equals(type?.Trim(), "multiple_choice_3", StringComparison.OrdinalIgnoreCase)
            || options.Count != 3
            || string.IsNullOrWhiteSpace(correctAnswer))
        {
            return false;
        }

        return options.Count(option => string.Equals(option.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private static bool HasValidMcqShape(
        ListeningQuestionType questionType,
        string? correctAnswer,
        IReadOnlyCollection<RelationalOption> options)
    {
        var normalizedCorrectAnswer = correctAnswer?.Trim();
        if (questionType != ListeningQuestionType.MultipleChoice3
            || options.Count != 3
            || string.IsNullOrWhiteSpace(normalizedCorrectAnswer))
        {
            return false;
        }

        var correctOptions = options.Count(option => option.IsCorrect);
        var matchingOptions = options
            .Where(option => string.Equals(option.OptionKey.Trim(), normalizedCorrectAnswer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Text.Trim(), normalizedCorrectAnswer, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return correctOptions == 1
            && matchingOptions.Length == 1
            && matchingOptions[0].IsCorrect;
    }

    private static IReadOnlyList<string> ReadOptions(Dictionary<string, object?> question)
    {
        var raw = question.GetValueOrDefault("options");
        if (raw is null) return Array.Empty<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string?>>(JsonSerializer.Serialize(raw));
            return list?
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Select(option => option!.Trim())
                .ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private sealed record RelationalOption(string OptionKey, string Text, bool IsCorrect);

    private static string? ReadString(Dictionary<string, object?> question, string key)
        => question.GetValueOrDefault(key)?.ToString();

    private static int? TryGetInt(Dictionary<string, object?> question, string key)
    {
        var raw = question.GetValueOrDefault(key);
        return raw switch
        {
            int n => n,
            long n => checked((int)n),
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var n) => n,
            _ when int.TryParse(raw?.ToString(), out var n) => n,
            _ => null,
        };
    }

    private static string? ReadJsonString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Number => doc.RootElement.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
