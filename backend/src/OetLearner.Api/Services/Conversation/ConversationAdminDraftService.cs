using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// AI-assisted conversation template authoring for the admin CMS. Mirrors
/// <see cref="OetLearner.Api.Services.Pronunciation.IPronunciationAdminDraftService"/>:
///   1. Routes the call through the grounded AI gateway
///      (<c>Kind = Conversation</c> + <c>Task = GenerateConversationScenario</c>).
///   2. Forces platform credentials via
///      <c>FeatureCode = AiFeatureCodes.AdminConversationDraft</c>.
///   3. NEVER persists directly; returns a populated DTO that the admin can
///      edit and save via the regular create endpoint.
/// </summary>
public interface IConversationAdminDraftService
{
    Task<ConversationTemplateDraftResult> DraftAsync(
        AdminConversationTemplateAiDraftRequest request,
        string? adminId,
        string? adminName,
        CancellationToken ct);
}

public sealed record ConversationTemplateDraftResult(
    string Title,
    string TaskTypeCode,
    string Profession,
    string Scenario,
    string RoleDescription,
    string PatientContext,
    string ExpectedOutcomes,
    string Difficulty,
    int EstimatedDurationSeconds,
    IReadOnlyList<string> Objectives,
    IReadOnlyList<string> ExpectedRedFlags,
    IReadOnlyList<string> KeyVocabulary,
    string? Warning);

public sealed class ConversationAdminDraftService(
    IAiGatewayService gateway,
    ILogger<ConversationAdminDraftService> logger) : IConversationAdminDraftService
{
    public async Task<ConversationTemplateDraftResult> DraftAsync(
        AdminConversationTemplateAiDraftRequest request,
        string? adminId,
        string? adminName,
        CancellationToken ct)
    {
        var profession = ParseProfession(request.Profession);
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Conversation,
            Profession = profession,
            Task = AiTaskMode.GenerateConversationScenario,
        });

        var userPrompt = BuildUserPrompt(request);

        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userPrompt,
                Model = "auto",
                Temperature = 0.4,
                MaxTokens = 1400,
                UserId = adminId,
                FeatureCode = AiFeatureCodes.AdminConversationDraft,
                PromptTemplateId = "conversation.admin.draft.v1",
            }, ct);

            var parsed = ParseDraft(result.Completion, request);
            if (parsed is null)
            {
                // Fallback to a deterministic starter template so the admin
                // never sees a hard failure. They can re-prompt or edit.
                parsed = FallbackTemplate(request, profession);
                return parsed with { Warning = "AI returned an unusable response; populated a starter template instead." };
            }
            return parsed;
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Conversation AI draft provider error.");
            throw ApiException.ServiceUnavailable(
                "CONVERSATION_AI_DRAFT_UNAVAILABLE",
                "Conversation AI draft generation is unavailable right now. Please try again.");
        }
    }

    private static string BuildUserPrompt(AdminConversationTemplateAiDraftRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Profession: {r.Profession}");
        sb.AppendLine($"Topic: {r.Topic ?? "(any clinically relevant topic)"}");
        if (!string.IsNullOrWhiteSpace(r.Scenario))
            sb.AppendLine($"Admin scenario hint: {r.Scenario}");
        sb.AppendLine($"Task type: {r.TaskType ?? "oet-roleplay"}");
        sb.AppendLine($"Estimated duration (seconds): {r.DurationSeconds ?? 300}");
        sb.AppendLine();
        sb.AppendLine("Author one OET conversation template. Reply STRICTLY as a single JSON object with keys: title, taskTypeCode (\"oet-roleplay\" or \"oet-handover\"), scenario, roleDescription, patientContext, expectedOutcomes, difficulty (\"easy\"|\"medium\"|\"hard\"), estimatedDurationSeconds (integer), objectives (array of >=3 short strings), expectedRedFlags (array of strings), keyVocabulary (array of strings).");
        return sb.ToString();
    }

    private static ConversationTemplateDraftResult? ParseDraft(
        string completion,
        AdminConversationTemplateAiDraftRequest req)
    {
        var json = ExtractJsonObject(completion);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string title = S(root, "title") ?? $"OET {req.TaskType ?? "Roleplay"}";
            string taskType = NormaliseTaskType(S(root, "taskTypeCode") ?? req.TaskType);
            string scenario = S(root, "scenario") ?? req.Scenario ?? "";
            string role = S(root, "roleDescription") ?? "";
            string patient = S(root, "patientContext") ?? "";
            string outcomes = S(root, "expectedOutcomes") ?? "";
            string difficulty = S(root, "difficulty") ?? "medium";
            int duration = ReadInt(root, "estimatedDurationSeconds")
                            ?? req.DurationSeconds
                            ?? 300;
            var objectives = ReadStringList(root, "objectives");
            var redFlags = ReadStringList(root, "expectedRedFlags");
            var vocab = ReadStringList(root, "keyVocabulary");

            if (string.IsNullOrWhiteSpace(scenario) || objectives.Count == 0)
                return null;

            return new ConversationTemplateDraftResult(
                Title: title,
                TaskTypeCode: taskType,
                Profession: req.Profession,
                Scenario: scenario,
                RoleDescription: role,
                PatientContext: patient,
                ExpectedOutcomes: outcomes,
                Difficulty: difficulty,
                EstimatedDurationSeconds: duration,
                Objectives: objectives,
                ExpectedRedFlags: redFlags,
                KeyVocabulary: vocab,
                Warning: null);
        }
        catch
        {
            return null;
        }
    }

    private static ConversationTemplateDraftResult FallbackTemplate(
        AdminConversationTemplateAiDraftRequest r,
        ExamProfession profession)
    {
        var topic = string.IsNullOrWhiteSpace(r.Topic) ? "patient consultation" : r.Topic!;
        return new ConversationTemplateDraftResult(
            Title: $"OET {(string.Equals(r.TaskType, "oet-handover", StringComparison.OrdinalIgnoreCase) ? "Handover" : "Roleplay")} — {topic}",
            TaskTypeCode: NormaliseTaskType(r.TaskType),
            Profession: r.Profession,
            Scenario: r.Scenario ?? $"You are a {profession.ToString().ToLowerInvariant()} professional. Conduct a {topic}.",
            RoleDescription: $"Take the role of the {profession.ToString().ToLowerInvariant()} professional in this scenario.",
            PatientContext: "Patient presents with concerns relevant to the scenario. Adjust details as needed.",
            ExpectedOutcomes: "Establish rapport, gather relevant history, explain next steps, confirm understanding.",
            Difficulty: "medium",
            EstimatedDurationSeconds: r.DurationSeconds ?? 300,
            Objectives: new[]
            {
                "Open the conversation with appropriate greeting and identification.",
                "Elicit relevant clinical information.",
                "Explain next steps in patient-friendly language.",
            },
            ExpectedRedFlags: Array.Empty<string>(),
            KeyVocabulary: Array.Empty<string>(),
            Warning: null);
    }

    private static string NormaliseTaskType(string? tt)
    {
        if (string.IsNullOrWhiteSpace(tt)) return "oet-roleplay";
        var v = tt.Trim().ToLowerInvariant();
        return v is "oet-handover" or "handover" ? "oet-handover" : "oet-roleplay";
    }

    private static string? S(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? ReadInt(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static List<string> ReadStringList(JsonElement root, string name)
    {
        var list = new List<string>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                    list.Add(e.GetString()!);
        }
        return list;
    }

    private static string? ExtractJsonObject(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        int start = completion.IndexOf('{');
        int end = completion.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        return completion.Substring(start, end - start + 1);
    }

    private static ExamProfession ParseProfession(string? prof)
    {
        if (string.IsNullOrWhiteSpace(prof) || prof.Equals("all", StringComparison.OrdinalIgnoreCase))
            return ExamProfession.Medicine;
        var norm = prof.Replace("-", "_").ToLowerInvariant();
        foreach (var v in Enum.GetValues<ExamProfession>())
        {
            if (string.Equals(v.ToString(), norm, StringComparison.OrdinalIgnoreCase)) return v;
        }
        if (norm.Replace("_", "") == "occupationaltherapy") return ExamProfession.OccupationalTherapy;
        if (norm.Replace("_", "") == "speechpathology") return ExamProfession.SpeechPathology;
        return ExamProfession.Medicine;
    }
}
