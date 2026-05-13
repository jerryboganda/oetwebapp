using System.Text.Json;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Shared helpers for authored OET Speaking role-play structure stored on
/// ContentPaper.ExtractedTextJson["speakingStructure"].
/// </summary>
public static class SpeakingContentStructure
{
    public const string StructureKey = "speakingStructure";
    public const int DefaultPrepTimeSeconds = 180;
    public const int DefaultRoleplayTimeSeconds = 300;
    public const string PracticeDisclaimer =
        "Practice estimate only. This is not an official OET score or result.";

    public static Dictionary<string, object?> ExtractStructure(string? extractedTextJson)
    {
        var root = ToDictionary(JsonSupport.Deserialize<Dictionary<string, object?>>(
            extractedTextJson,
            new Dictionary<string, object?>()));

        var nested = ToDictionary(ReadValue(root, StructureKey));
        if (nested.Count > 0)
        {
            return nested;
        }

        return root;
    }

    public static string ReplaceStructure(string? extractedTextJson, JsonElement structure)
    {
        var root = ToDictionary(JsonSupport.Deserialize<Dictionary<string, object?>>(
            extractedTextJson,
            new Dictionary<string, object?>()));

        root[StructureKey] = ToDictionary(structure);
        return JsonSupport.Serialize(root);
    }

    public static SpeakingStructureValidationReport Validate(ContentPaper paper)
    {
        var issues = new List<SpeakingStructureValidationIssue>();

        if (!string.Equals(paper.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            return new SpeakingStructureValidationReport(true, issues);
        }

        var structure = ExtractStructure(paper.ExtractedTextJson);
        var candidate = ToDictionary(ReadValue(structure, "candidateCard"));
        var interlocutor = ToDictionary(ReadValue(structure, "interlocutorCard"));

        RequireText(issues, "candidate_setting", ReadString(candidate, "setting") ?? ReadString(structure, "setting"),
            "Candidate card requires a clinical setting.");
        RequireText(issues, "candidate_role", ReadString(candidate, "candidateRole", "role") ?? ReadString(structure, "candidateRole", "role"),
            "Candidate card requires the candidate role/profession.");
        RequireText(issues, "patient_role", ReadString(candidate, "patientRole", "patient") ?? ReadString(structure, "patientRole", "patient"),
            "Candidate card requires the patient/client role.");
        RequireText(issues, "candidate_background", ReadString(candidate, "background") ?? ReadString(structure, "background", "caseNotes"),
            "Candidate card requires background/context.");
        RequireText(issues, "candidate_task", ReadString(candidate, "task", "brief") ?? ReadString(structure, "task", "brief"),
            "Candidate card requires a role-play task/brief.");

        var tasks = FirstNonEmptyList(
            ReadStringList(ReadValue(candidate, "tasks")),
            ReadStringList(ReadValue(structure, "tasks")),
            ReadStringList(ReadValue(structure, "roleObjectives")));
        if (tasks.Count == 0)
        {
            issues.Add(new("candidate_objectives", "error", "Candidate card requires at least one role objective/task bullet."));
        }

        if (ReadStringList(ReadValue(structure, "warmUpQuestions")).Count == 0)
        {
            issues.Add(new("warm_up_questions", "error", "Speaking content requires at least one warm-up question."));
        }

        RequireText(issues, "patient_emotion", ReadString(structure, "patientEmotion"),
            "Speaking content requires patient emotion.");
        RequireText(issues, "communication_goal", ReadString(structure, "communicationGoal", "purpose"),
            "Speaking content requires a communication goal/purpose.");
        RequireText(issues, "clinical_topic", ReadString(structure, "clinicalTopic"),
            "Speaking content requires a clinical topic.");

        var criteriaFocus = FirstNonEmptyList(
            ReadStringList(ReadValue(structure, "criteriaFocus")),
            SplitCsv(paper.TagsCsv));
        if (criteriaFocus.Count == 0)
        {
            issues.Add(new("criteria_focus", "error", "Speaking content requires criteria focus/tags."));
        }

        var prep = ReadInt(structure, "prepTimeSeconds") ?? DefaultPrepTimeSeconds;
        var roleplay = ReadInt(structure, "roleplayTimeSeconds") ?? DefaultRoleplayTimeSeconds;
        if (prep < 60)
        {
            issues.Add(new("prep_time", "error", "Preparation time must be at least 60 seconds."));
        }
        if (roleplay < 180)
        {
            issues.Add(new("roleplay_time", "error", "Role-play time must be at least 180 seconds."));
        }

        var cuePrompts = FirstNonEmptyList(
            ReadStringList(ReadValue(interlocutor, "cuePrompts")),
            ReadStringList(ReadValue(interlocutor, "prompts")),
            ReadStringList(ReadValue(interlocutor, "objectives")));
        var patientProfile = ReadString(interlocutor, "patientProfile", "background", "hiddenInformation");
        if (string.IsNullOrWhiteSpace(patientProfile) && cuePrompts.Count == 0)
        {
            issues.Add(new("interlocutor_card", "error",
                "Hidden interlocutor card requires patient profile/background or cue prompts."));
        }

        return new SpeakingStructureValidationReport(
            !issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            issues);
    }

    public static Dictionary<string, object?> BuildContentItemDetail(ContentPaper paper)
    {
        var structure = ExtractStructure(paper.ExtractedTextJson);
        var candidate = ToDictionary(ReadValue(structure, "candidateCard"));
        var interlocutor = ToDictionary(ReadValue(structure, "interlocutorCard"));

        var role = ReadString(candidate, "candidateRole", "role")
                   ?? ReadString(structure, "candidateRole", "role")
                   ?? FormatProfession(paper.ProfessionId);
        var setting = ReadString(candidate, "setting") ?? ReadString(structure, "setting") ?? "Clinical setting";
        var patient = ReadString(candidate, "patientRole", "patient") ?? ReadString(structure, "patientRole", "patient") ?? "Patient";
        var task = ReadString(candidate, "task", "brief") ?? ReadString(structure, "task", "brief")
                   ?? "Complete the role play using patient-centred communication.";
        var background = ReadString(candidate, "background") ?? ReadString(structure, "background", "caseNotes") ?? string.Empty;
        var tasks = FirstNonEmptyList(
            ReadStringList(ReadValue(candidate, "tasks")),
            ReadStringList(ReadValue(structure, "tasks")),
            ReadStringList(ReadValue(structure, "roleObjectives")));
        var warmUps = ReadStringList(ReadValue(structure, "warmUpQuestions"));
        var criteriaFocus = FirstNonEmptyList(
            ReadStringList(ReadValue(structure, "criteriaFocus")),
            SplitCsv(paper.TagsCsv));

        return new Dictionary<string, object?>
        {
            ["candidateCard"] = new
            {
                role,
                candidateRole = role,
                setting,
                patient,
                patientRole = patient,
                task,
                brief = task,
                background,
                tasks,
            },
            ["interlocutorCard"] = interlocutor,
            ["warmUpQuestions"] = warmUps,
            ["prepTimeSeconds"] = ReadInt(structure, "prepTimeSeconds") ?? DefaultPrepTimeSeconds,
            ["roleplayTimeSeconds"] = ReadInt(structure, "roleplayTimeSeconds") ?? DefaultRoleplayTimeSeconds,
            ["roleplayCount"] = Math.Clamp(ReadInt(structure, "roleplayCount", "rolePlayCount", "numberOfRoleplays") ?? 2, 1, 2),
            ["patientEmotion"] = ReadString(structure, "patientEmotion") ?? "neutral",
            ["communicationGoal"] = ReadString(structure, "communicationGoal", "purpose") ?? "Build rapport and complete the clinical task.",
            ["clinicalTopic"] = ReadString(structure, "clinicalTopic") ?? paper.CardType ?? "general_roleplay",
            ["criteriaFocus"] = criteriaFocus,
            ["disclaimer"] = PracticeDisclaimer,
            ["complianceNotes"] = ReadString(structure, "complianceNotes"),
            // Backward-compatible flat fields used by the existing role-card UI.
            ["role"] = role,
            ["setting"] = setting,
            ["patient"] = patient,
            ["task"] = task,
            ["brief"] = task,
            ["background"] = background,
            ["tasks"] = tasks,
            ["sourceProvenance"] = paper.SourceProvenance,
        };
    }

    public static Dictionary<string, object?> ToDictionary(object? value)
    {
        if (value is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (value is Dictionary<string, object?> dict)
        {
            return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                element.GetRawText(),
                JsonSupport.Options);
            return parsed is null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(parsed, StringComparer.OrdinalIgnoreCase);
        }

        var json = JsonSupport.Serialize(value);
        return ToDictionary(JsonSerializer.Deserialize<JsonElement>(json, JsonSupport.Options));
    }

    public static object? ReadValue(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var (candidateKey, value) in source)
            {
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
        }

        return null;
    }

    public static string? ReadString(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            var text = ValueToString(ReadValue(source, key));
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    public static int? ReadInt(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadValue(source, key);
            switch (value)
            {
                case int i:
                    return i;
                case long l:
                    return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
                case double d:
                    return (int)Math.Round(d);
                case JsonElement { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var parsed):
                    return parsed;
                case string s when int.TryParse(s, out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    public static List<string> ReadStringList(object? value)
    {
        switch (value)
        {
            case null:
                return [];
            case string text:
                return SplitMultiline(text);
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                return array.EnumerateArray()
                    .Select(e => ValueToString(e))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .ToList();
            case JsonElement { ValueKind: JsonValueKind.String } textElement:
                return SplitMultiline(textElement.GetString() ?? string.Empty);
            case IEnumerable<string> strings:
                return strings.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            case IEnumerable<object?> objects:
                return objects
                    .Select(ValueToString)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .ToList();
            default:
                var fallback = ValueToString(value);
                return string.IsNullOrWhiteSpace(fallback) ? [] : [fallback.Trim()];
        }
    }

    private static void RequireText(List<SpeakingStructureValidationIssue> issues, string code, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new SpeakingStructureValidationIssue(code, "error", message));
        }
    }

    private static string? ValueToString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Number } element => element.ToString(),
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        _ => value.ToString(),
    };

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private static List<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(text => !text.StartsWith("access:", StringComparison.OrdinalIgnoreCase))
                .ToList();

    private static List<string> SplitMultiline(string value)
        => value.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

    private static string FormatProfession(string? professionId)
    {
        if (string.IsNullOrWhiteSpace(professionId))
        {
            return "Candidate";
        }

        var spaced = professionId.Replace('_', ' ').Replace('-', ' ');
        return string.Concat(spaced[..1].ToUpperInvariant(), spaced[1..]);
    }
}

public sealed record SpeakingStructureValidationIssue(
    string Code,
    string Severity,
    string Message);

public sealed record SpeakingStructureValidationReport(
    bool IsPublishReady,
    IReadOnlyList<SpeakingStructureValidationIssue> Issues);
