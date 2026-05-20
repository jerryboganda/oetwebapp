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
///      <c>appliedRuleIds</c> value must exist in the rulebook or the draft
///      fails closed.
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
            if (parsed is null)
            {
                return BuildFallbackDraft(request, profession,
                    "AI draft response was not usable; a deterministic starter template was returned. Please edit before saving.");
            }
        }
        catch (PromptNotGroundedException)
        {
            // Grounding violations are architectural — never silently degrade.
            throw;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per AGENTS.md "Pronunciation Module" contract:
            //   "unusable replies fall back to a deterministic starter
            //    template with a `warning` surfaced to the admin"
            // We MUST NOT leak the raw provider error text to the admin (it
            // can carry HTML, secrets, or stack traces). Log the detail
            // server-side and return a sanitized fallback.
            logger.LogWarning(ex, "Pronunciation AI draft provider error.");
            return BuildFallbackDraft(request, profession,
                "AI draft generation failed; a deterministic starter template was returned. Please edit before saving.");
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
            throw ApiException.ServiceUnavailable(
                "PRONUNCIATION_RULEBOOK_NOT_AVAILABLE",
                $"No pronunciation rulebook found for profession '{profession}'.");
        }

        if (parsed.AppliedRuleIds.Count == 0)
        {
            return BuildFallbackDraft(request, profession,
                "AI draft did not cite any valid pronunciation rulebook rules; a deterministic starter template was returned. Please edit before saving.");
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

    /// <summary>
    /// Deterministic starter template returned when the AI provider errors
    /// or returns an unusable response. Per AGENTS.md, the admin must
    /// receive a populated, editable draft with a sanitized warning — the
    /// raw provider error must NEVER reach the UI (it can carry HTML,
    /// stack traces, or secrets). The first valid rulebook rule ID is
    /// cited so the resulting drill survives the publish gate without
    /// further admin work.
    /// </summary>
    private PronunciationDrillDraftResult BuildFallbackDraft(
        AdminPronunciationDrillAiDraftRequest request,
        ExamProfession profession,
        string warning)
    {
        string? primaryRuleId = request.PrimaryRuleId;
        try
        {
            var rb = loader.Load(RuleKind.Pronunciation, profession);
            if (string.IsNullOrWhiteSpace(primaryRuleId))
                primaryRuleId = rb.Rules.FirstOrDefault()?.Id;
        }
        catch (RulebookNotFoundException)
        {
            // Leave primaryRuleId null; admin will edit before saving.
        }

        var phoneme = string.IsNullOrWhiteSpace(request.Phoneme) ? "—" : request.Phoneme!;
        var difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "medium" : request.Difficulty!;
        var focus = string.IsNullOrWhiteSpace(request.Focus) ? "phoneme" : request.Focus!;
        var ruleIds = string.IsNullOrWhiteSpace(primaryRuleId)
            ? Array.Empty<string>()
            : new[] { primaryRuleId! };

        return new PronunciationDrillDraftResult(
            TargetPhoneme: phoneme,
            Label: $"Drill — /{phoneme}/ (starter template)",
            Difficulty: difficulty,
            Focus: focus,
            ExampleWords: new[] { "patient", "treatment", "diagnosis" },
            MinimalPairs: Array.Empty<MinimalPairDto>(),
            Sentences: new[] { "The patient requires immediate treatment." },
            TipsHtml: "<p>Edit this starter template before saving. AI draft was unavailable.</p>",
            AppliedRuleIds: ruleIds,
            PrimaryRuleId: primaryRuleId,
            Warning: warning,
            SelfCheckNotes: null);
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
