using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// AI-assisted pronunciation drill authoring for the admin CMS. Mirrors
/// <see cref="OetLearner.Api.Services.Grammar.IGrammarDraftService"/>:
///   1. Routes the call through the grounded AI gateway
///      (<c>Kind = Pronunciation</c> + <c>Task = GeneratePronunciationDrill</c>).
///   2. Forces platform credentials via
///      <c>FeatureCode = AiFeatureCodes.AdminPronunciationDraft</c>.
///   3. Validates the reply against the loaded pronunciation rulebook — every
///      <c>appliedRuleIds</c> value must exist in the rulebook or the draft is
///      rejected and a deterministic fallback template is returned.
///   4. NEVER persists directly; returns a populated DTO that the admin can
///      edit and save via the regular create endpoint.
/// </summary>
public interface IPronunciationAdminDraftService
{
    Task<PronunciationDrillDraftResult> GenerateDraftAsync(
        AdminPronunciationDrillAiDraftRequest request,
        string? adminId,
        CancellationToken ct);
}

public sealed record PronunciationDrillDraftResult(
    string TargetPhoneme,
    string Label,
    string Difficulty,
    string Focus,
    IReadOnlyList<string> ExampleWords,
    IReadOnlyList<MinimalPairDto> MinimalPairs,
    IReadOnlyList<string> Sentences,
    string TipsHtml,
    IReadOnlyList<string> AppliedRuleIds,
    string? PrimaryRuleId,
    string? Warning,
    string? SelfCheckNotes);

public sealed record MinimalPairDto(string A, string B);

public sealed class PronunciationAdminDraftService(
    IAiGatewayService gateway,
    IRulebookLoader loader,
    ILogger<PronunciationAdminDraftService> logger) : IPronunciationAdminDraftService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PronunciationDrillDraftResult> GenerateDraftAsync(
        AdminPronunciationDrillAiDraftRequest request,
        string? adminId,
        CancellationToken ct)
    {
        var profession = ParseProfession(request.Profession);
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Pronunciation,
            Profession = profession,
            Task = AiTaskMode.GeneratePronunciationDrill,
        });

        var userPrompt = BuildUserPrompt(request);
        string? warning = null;
        PronunciationDrillDraftResult? parsed = null;

        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userPrompt,
                Model = "auto",
                Temperature = 0.3,
                MaxTokens = 1200,
                UserId = adminId,
                FeatureCode = AiFeatureCodes.AdminPronunciationDraft,
                PromptTemplateId = "pronunciation.admin.draft.v1",
            }, ct);

            parsed = ParseDraft(result.Completion, request, prompt.Metadata.RulebookVersion);
            if (parsed is null) warning = "AI response could not be parsed — using deterministic fallback template.";
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pronunciation AI draft failed — using deterministic fallback.");
            warning = "AI provider error: draft generation failed; using deterministic fallback template.";
        }

        if (parsed is null)
        {
            parsed = FallbackDraft(request);
        }

        // Validate appliedRuleIds against the rulebook.
        try
        {
            var rulebook = loader.Load(RuleKind.Pronunciation, profession);
            var knownIds = new HashSet<string>(
                rulebook.Rules.Select(r => r.Id),
                StringComparer.OrdinalIgnoreCase);
            var unknown = parsed.AppliedRuleIds.Where(id => !knownIds.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                warning = (warning is null ? "" : warning + " ") +
                          $"Ignored unknown rule IDs: {string.Join(", ", unknown)}.";
                parsed = parsed with
                {
                    AppliedRuleIds = parsed.AppliedRuleIds.Where(id => knownIds.Contains(id)).ToList(),
                };
            }
        }
        catch (RulebookNotFoundException)
        {
            warning = (warning is null ? "" : warning + " ") +
                      $"No pronunciation rulebook found for profession '{profession}' — applied rule IDs not validated.";
        }

        return parsed with { Warning = warning };
    }

    private static string BuildUserPrompt(AdminPronunciationDrillAiDraftRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Phoneme or focus target: {r.Phoneme ?? "(not specified)"}");
        sb.AppendLine($"Focus category: {r.Focus ?? "phoneme"}");
        sb.AppendLine($"Profession: {r.Profession ?? "medicine"}");
        sb.AppendLine($"Difficulty: {r.Difficulty ?? "medium"}");
        if (!string.IsNullOrWhiteSpace(r.PrimaryRuleId))
            sb.AppendLine($"Primary rule ID: {r.PrimaryRuleId}");
        if (!string.IsNullOrWhiteSpace(r.Prompt))
            sb.AppendLine($"Admin prompt: {r.Prompt}");
        sb.AppendLine();
        sb.AppendLine("Generate a pronunciation drill that would help an OET candidate in this profession improve the target sound. Use profession-appropriate medical vocabulary. Respond strictly in the reply format above.");
        return sb.ToString();
    }

    private static PronunciationDrillDraftResult? ParseDraft(
        string completion,
        AdminPronunciationDrillAiDraftRequest req,
        string rulebookVersion)
    {
        var json = ExtractJsonObject(completion);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string target = S(root, "targetPhoneme") ?? req.Phoneme ?? "";
            string label = S(root, "label") ?? $"Drill — /{target}/";
            string difficulty = S(root, "difficulty") ?? req.Difficulty ?? "medium";
            string focus = S(root, "focus") ?? req.Focus ?? "phoneme";
            string tips = SafeHtmlSanitizer.SanitizeLimitedHtml(S(root, "tipsHtml"));
            string? selfCheck = S(root, "selfCheckNotes");

            var words = ReadStringList(root, "exampleWords");
            var sentences = ReadStringList(root, "sentences");
            var applied = ReadStringList(root, "appliedRuleIds");
            var pairs = new List<MinimalPairDto>();
            if (root.TryGetProperty("minimalPairs", out var mp) && mp.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in mp.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var a = e.TryGetProperty("a", out var aa) ? aa.GetString() : null;
                    var b = e.TryGetProperty("b", out var bb) ? bb.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                        pairs.Add(new MinimalPairDto(a!, b!));
                }
            }

            return new PronunciationDrillDraftResult(
                TargetPhoneme: target,
                Label: label,
                Difficulty: difficulty,
                Focus: focus,
                ExampleWords: words,
                MinimalPairs: pairs,
                Sentences: sentences,
                TipsHtml: tips,
                AppliedRuleIds: applied,
                PrimaryRuleId: applied.FirstOrDefault() ?? req.PrimaryRuleId,
                Warning: null,
                SelfCheckNotes: selfCheck);
        }
        catch
        {
            return null;
        }
    }

    private static PronunciationDrillDraftResult FallbackDraft(AdminPronunciationDrillAiDraftRequest r)
    {
        var phoneme = string.IsNullOrWhiteSpace(r.Phoneme) ? "θ" : r.Phoneme!;
        var focus = string.IsNullOrWhiteSpace(r.Focus) ? "phoneme" : r.Focus!;
        return new PronunciationDrillDraftResult(
            TargetPhoneme: phoneme,
            Label: $"Drill — /{phoneme}/",
            Difficulty: string.IsNullOrWhiteSpace(r.Difficulty) ? "medium" : r.Difficulty!,
            Focus: focus,
            ExampleWords: new[] { "patient", "therapy", "method" },
            MinimalPairs: Array.Empty<MinimalPairDto>(),
            Sentences: new[]
            {
                "Please describe any symptoms you have noticed recently.",
                "The patient was referred for further assessment.",
            },
            TipsHtml: "<p>AI draft unavailable. Edit this draft before publishing.</p>",
            AppliedRuleIds: string.IsNullOrWhiteSpace(r.PrimaryRuleId)
                ? Array.Empty<string>()
                : new[] { r.PrimaryRuleId! },
            PrimaryRuleId: r.PrimaryRuleId,
            Warning: "Deterministic fallback used — AI draft was not available.",
            SelfCheckNotes: null);
    }

    private static string? S(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

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
