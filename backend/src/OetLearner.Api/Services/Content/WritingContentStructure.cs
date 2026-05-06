using System.Text.Json;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Helpers for authored OET Writing task structure stored on
/// ContentPaper.ExtractedTextJson["writingStructure"].
/// </summary>
public static class WritingContentStructure
{
    public const string StructureKey = "writingStructure";
    public const string PracticeDisclaimer =
        "Practice estimate only. This is not an official OET score or result.";

    private static readonly HashSet<string> CanonicalLetterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "routine_referral",
        "urgent_referral",
        "non_medical_referral",
        "update_discharge",
        "update_referral_specialist_to_gp",
        "transfer_letter"
    };

    public static bool IsCanonicalLetterType(string? letterType)
        => !string.IsNullOrWhiteSpace(letterType) && CanonicalLetterTypes.Contains(letterType.Trim());

    public static Dictionary<string, object?> ExtractStructure(string? extractedTextJson)
    {
        var root = SpeakingContentStructure.ToDictionary(JsonSupport.Deserialize<Dictionary<string, object?>>(
            extractedTextJson,
            new Dictionary<string, object?>()));

        var nested = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(root, StructureKey));
        return nested.Count > 0 ? nested : root;
    }

    public static string ReplaceStructure(string? extractedTextJson, JsonElement structure)
    {
        var root = SpeakingContentStructure.ToDictionary(JsonSupport.Deserialize<Dictionary<string, object?>>(
            extractedTextJson,
            new Dictionary<string, object?>()));

        root[StructureKey] = SpeakingContentStructure.ToDictionary(structure);
        return JsonSupport.Serialize(root);
    }

    public static WritingStructureValidationReport Validate(ContentPaper paper)
    {
        var issues = new List<WritingStructureValidationIssue>();

        if (!string.Equals(paper.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            return new WritingStructureValidationReport(true, issues);
        }

        var structure = ExtractStructure(paper.ExtractedTextJson);
        RequireText(issues, "letter_type", paper.LetterType,
            "Writing content requires a canonical letter type.");
        if (!string.IsNullOrWhiteSpace(paper.LetterType) && !IsCanonicalLetterType(paper.LetterType))
        {
            issues.Add(new("letter_type", "error", "Letter type must be one of the canonical Writing letter types."));
        }
        RequireText(issues, "task_prompt", ReadString(structure, "taskPrompt", "task", "brief", "scenario"),
            "Writing content requires a learner-facing task prompt.");
        RequireText(issues, "case_notes", BuildCaseNotesText(structure),
            "Writing content requires authored learner-facing case notes text.");
        RequireText(issues, "model_answer", BuildModelAnswerText(structure),
            "Writing content requires authored model answer text for post-submit study.");

        var criteriaFocus = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(structure, "criteriaFocus")),
            SplitCsv(paper.TagsCsv));
        if (criteriaFocus.Count == 0)
        {
            issues.Add(new("criteria_focus", "warning", "Criteria focus is recommended for learner filtering and feedback."));
        }

        return new WritingStructureValidationReport(
            !issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            issues);
    }

    public static Dictionary<string, object?> BuildContentItemDetail(ContentPaper paper)
    {
        var structure = ExtractStructure(paper.ExtractedTextJson);
        var letterType = paper.LetterType ?? ReadString(structure, "letterType", "taskType") ?? "writing_task";
        var caseNotes = BuildCaseNotesText(structure);
        var criteriaFocus = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(structure, "criteriaFocus")),
            SplitCsv(paper.TagsCsv));

        return new Dictionary<string, object?>
        {
            ["letterType"] = letterType,
            ["taskType"] = letterType,
            ["scenarioType"] = letterType,
            ["scenario"] = ReadString(structure, "scenario", "taskPrompt", "task", "brief") ?? paper.Title,
            ["taskPrompt"] = ReadString(structure, "taskPrompt", "task", "brief", "scenario") ?? paper.Title,
            ["taskDate"] = ReadString(structure, "taskDate", "date"),
            ["writerRole"] = ReadString(structure, "writerRole", "candidateRole"),
            ["recipient"] = ReadString(structure, "recipient", "recipientName"),
            ["purpose"] = ReadString(structure, "purpose", "request"),
            ["caseNotes"] = caseNotes,
            ["caseNoteSections"] = ReadCaseNoteSections(structure),
            ["criteriaFocus"] = criteriaFocus,
            ["sourceProvenance"] = paper.SourceProvenance,
            ["practiceDisclaimer"] = PracticeDisclaimer,
        };
    }

    public static Dictionary<string, object?> BuildModelAnswerPayload(ContentPaper paper)
    {
        var structure = ExtractStructure(paper.ExtractedTextJson);
        return new Dictionary<string, object?>
        {
            ["letterType"] = paper.LetterType ?? ReadString(structure, "letterType", "taskType"),
            ["paragraphs"] = ReadModelAnswerParagraphs(structure),
            ["practiceDisclaimer"] = PracticeDisclaimer,
            ["sourceProvenance"] = paper.SourceProvenance,
        };
    }

    public static string BuildCaseNotesText(IReadOnlyDictionary<string, object?> structure)
    {
        var direct = ReadString(structure, "caseNotes", "caseNotesText", "stimulus");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var sections = ReadCaseNoteSections(structure);
        return sections.Count == 0
            ? string.Empty
            : string.Join("\n\n", sections.Select(section =>
            {
                var heading = section.GetValueOrDefault("heading")?.ToString();
                var items = section.TryGetValue("items", out var value)
                    ? SpeakingContentStructure.ReadStringList(value)
                    : [];
                var body = string.Join("\n", items.Select(item => $"- {item}"));
                return string.IsNullOrWhiteSpace(heading) ? body : $"{heading}\n{body}";
            }).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    public static string BuildModelAnswerText(IReadOnlyDictionary<string, object?> structure)
    {
        var direct = ReadString(structure, "modelAnswer", "modelAnswerText", "referenceAnswer");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        return string.Join("\n\n", ReadModelAnswerParagraphs(structure)
            .Select(p => p.GetValueOrDefault("text")?.ToString())
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static List<Dictionary<string, object?>> ReadCaseNoteSections(IReadOnlyDictionary<string, object?> structure)
        => ReadObjectList(SpeakingContentStructure.ReadValue(structure, "caseNoteSections", "sections"));

    private static List<Dictionary<string, object?>> ReadModelAnswerParagraphs(IReadOnlyDictionary<string, object?> structure)
    {
        var authored = ReadObjectList(SpeakingContentStructure.ReadValue(structure, "modelAnswerParagraphs", "paragraphs"));
        if (authored.Count > 0) return authored;

        var text = ReadString(structure, "modelAnswer", "modelAnswerText", "referenceAnswer");
        if (string.IsNullOrWhiteSpace(text)) return [];

        return text
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((paragraph, index) => new Dictionary<string, object?>
            {
                ["id"] = $"p-{index + 1}",
                ["text"] = paragraph,
                ["rationale"] = string.Empty,
                ["criteria"] = Array.Empty<string>(),
                ["included"] = Array.Empty<string>(),
                ["excluded"] = Array.Empty<string>(),
                ["languageNotes"] = string.Empty,
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> ReadObjectList(object? value)
    {
        if (value is null) return [];
        if (value is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            return array.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.Object)
                .Select(element => SpeakingContentStructure.ToDictionary(element))
                .ToList();
        }

        if (value is IEnumerable<object?> objects)
        {
            return objects.Select(SpeakingContentStructure.ToDictionary).Where(dict => dict.Count > 0).ToList();
        }

        return [];
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, params string[] keys)
        => SpeakingContentStructure.ReadString(source, keys);

    private static void RequireText(List<WritingStructureValidationIssue> issues, string code, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new WritingStructureValidationIssue(code, "error", message));
        }
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private static List<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(text => !text.StartsWith("access:", StringComparison.OrdinalIgnoreCase))
                .ToList();
}

public sealed record WritingStructureValidationIssue(
    string Code,
    string Severity,
    string Message);

public sealed record WritingStructureValidationReport(
    bool IsPublishReady,
    IReadOnlyList<WritingStructureValidationIssue> Issues);