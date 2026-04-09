using System.Text.Json;
using System.Text.Json.Nodes;

namespace OetLearner.Api.Services;

/// <summary>
/// Validates ContentItem.DetailJson and ModelAnswerJson against expected subtest schemas.
/// Used during import and admin content creation to ensure structural consistency.
/// </summary>
public static class ContentSchemaValidator
{
    public static ContentValidationResult ValidateDetailJson(string subtestCode, string detailJson)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(detailJson) || detailJson == "{}")
        {
            errors.Add("DetailJson is empty.");
            return new ContentValidationResult(false, errors);
        }

        JsonNode? root;
        try { root = JsonNode.Parse(detailJson); }
        catch (JsonException ex)
        {
            errors.Add($"DetailJson is not valid JSON: {ex.Message}");
            return new ContentValidationResult(false, errors);
        }

        if (root is not JsonObject obj)
        {
            errors.Add("DetailJson must be a JSON object.");
            return new ContentValidationResult(false, errors);
        }

        switch (subtestCode.ToLowerInvariant())
        {
            case "writing":
                ValidateWritingDetail(obj, errors);
                break;
            case "speaking":
                ValidateSpeakingDetail(obj, errors);
                break;
            case "reading":
                ValidateReadingDetail(obj, errors);
                break;
            case "listening":
                ValidateListeningDetail(obj, errors);
                break;
            default:
                // No validation for unknown subtests
                break;
        }

        return new ContentValidationResult(errors.Count == 0, errors);
    }

    public static ContentValidationResult ValidateModelAnswerJson(string subtestCode, string modelAnswerJson)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(modelAnswerJson) || modelAnswerJson == "{}")
            return new ContentValidationResult(true, errors); // Model answers are optional

        JsonNode? root;
        try { root = JsonNode.Parse(modelAnswerJson); }
        catch (JsonException ex)
        {
            errors.Add($"ModelAnswerJson is not valid JSON: {ex.Message}");
            return new ContentValidationResult(false, errors);
        }

        if (root is not JsonObject)
        {
            errors.Add("ModelAnswerJson must be a JSON object.");
            return new ContentValidationResult(false, errors);
        }

        return new ContentValidationResult(true, errors);
    }

    private static void ValidateWritingDetail(JsonObject obj, List<string> errors)
    {
        if (obj["caseNotes"] is not JsonObject caseNotes)
        {
            errors.Add("Writing DetailJson must contain a 'caseNotes' object.");
        }
        else
        {
            RequireString(caseNotes, "patientName", errors, "caseNotes");
            RequireString(caseNotes, "diagnosis", errors, "caseNotes");
            RequireString(caseNotes, "history", errors, "caseNotes");
            RequireString(caseNotes, "treatment", errors, "caseNotes");
        }

        RequireString(obj, "taskInstructions", errors);
        RequireString(obj, "writingType", errors);

        if (obj["wordLimit"] is not JsonValue wl || !wl.TryGetValue<int>(out _))
            errors.Add("Writing DetailJson must contain integer 'wordLimit'.");
        if (obj["timeLimit"] is not JsonValue tl || !tl.TryGetValue<int>(out _))
            errors.Add("Writing DetailJson must contain integer 'timeLimit'.");
    }

    private static void ValidateSpeakingDetail(JsonObject obj, List<string> errors)
    {
        if (obj["roleCard"] is not JsonObject roleCard)
        {
            errors.Add("Speaking DetailJson must contain a 'roleCard' object.");
        }
        else
        {
            RequireString(roleCard, "setting", errors, "roleCard");
            RequireString(roleCard, "candidateRole", errors, "roleCard");
            RequireString(roleCard, "patientRole", errors, "roleCard");

            if (roleCard["taskObjectives"] is not JsonArray)
                errors.Add("roleCard must contain a 'taskObjectives' array.");
        }
    }

    private static void ValidateReadingDetail(JsonObject obj, List<string> errors)
    {
        RequireString(obj, "part", errors);

        if (obj["passages"] is not JsonArray passages || passages.Count == 0)
        {
            errors.Add("Reading DetailJson must contain a non-empty 'passages' array.");
        }
        else
        {
            foreach (var p in passages)
            {
                if (p is JsonObject po)
                {
                    RequireString(po, "id", errors, "passages[]");
                    RequireString(po, "text", errors, "passages[]");
                }
            }
        }

        if (obj["questions"] is not JsonArray questions || questions.Count == 0)
        {
            errors.Add("Reading DetailJson must contain a non-empty 'questions' array.");
        }
        else
        {
            foreach (var q in questions)
            {
                if (q is JsonObject qo)
                {
                    RequireString(qo, "id", errors, "questions[]");
                    RequireString(qo, "questionText", errors, "questions[]");
                    RequireString(qo, "questionType", errors, "questions[]");
                }
            }
        }
    }

    private static void ValidateListeningDetail(JsonObject obj, List<string> errors)
    {
        RequireString(obj, "part", errors);

        if (obj["audioSegments"] is not JsonArray segments || segments.Count == 0)
            errors.Add("Listening DetailJson must contain a non-empty 'audioSegments' array.");

        if (obj["questions"] is not JsonArray questions || questions.Count == 0)
        {
            errors.Add("Listening DetailJson must contain a non-empty 'questions' array.");
        }
        else
        {
            foreach (var q in questions)
            {
                if (q is JsonObject qo)
                {
                    RequireString(qo, "id", errors, "questions[]");
                    RequireString(qo, "questionText", errors, "questions[]");
                    RequireString(qo, "questionType", errors, "questions[]");
                }
            }
        }
    }

    private static void RequireString(JsonObject obj, string field, List<string> errors, string? context = null)
    {
        var prefix = context != null ? $"{context}." : "";
        if (obj[field] is not JsonValue v || v.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetValue<string>()))
            errors.Add($"Missing or empty required string field '{prefix}{field}'.");
    }
}

public record ContentValidationResult(bool IsValid, List<string> Errors);
