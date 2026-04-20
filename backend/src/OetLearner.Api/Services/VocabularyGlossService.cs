using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

/// <summary>
/// Learner-facing on-demand gloss. Routes through the AI Gateway with
/// <see cref="AiFeatureCodes.VocabularyGloss"/>. Free tier is capped at
/// 5 calls per rolling 24h; paid tier unlimited (quota service enforces).
///
/// If an existing active <see cref="VocabularyTerm"/> matches the word
/// (case-insensitive, same exam family), the gloss short-circuits to the
/// authored term and returns <c>MatchedExistingTerm=true</c>.
/// </summary>
public sealed class VocabularyGlossService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<VocabularyGlossService> logger)
{
    public async Task<VocabularyGlossResponse> GlossAsync(
        string userId,
        VocabularyGlossRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Word))
            throw ApiException.Validation("WORD_REQUIRED", "Word is required.");

        var word = request.Word.Trim();
        if (word.Length > 64)
            throw ApiException.Validation("WORD_TOO_LONG", "Word exceeds maximum length (64 chars).");

        // Existing-term short-circuit (preferred — cheap and consistent).
        var lower = word.ToLowerInvariant();
        var existing = await db.VocabularyTerms
            .FirstOrDefaultAsync(t => t.Status == "active" && t.Term.ToLower() == lower, ct);
        if (existing is not null)
        {
            var syn = TryParseStringArray(existing.SynonymsJson);
            return new VocabularyGlossResponse(
                Term: existing.Term,
                IpaPronunciation: existing.IpaPronunciation,
                ShortDefinition: existing.Definition,
                ExampleSentence: existing.ExampleSentence,
                ContextNotes: existing.ContextNotes,
                Synonyms: syn,
                Register: "clinical",
                AppliedRuleIds: Array.Empty<string>(),
                RulebookVersion: "existing",
                MatchedExistingTerm: true,
                ExistingTermId: existing.Id);
        }

        var profession = ParseProfession(request.Profession);
        OetRulebook rulebook;
        try { rulebook = rulebookLoader.Load(RuleKind.Vocabulary, profession); }
        catch (RulebookNotFoundException)
        {
            rulebook = rulebookLoader.Load(RuleKind.Vocabulary, ExamProfession.Medicine);
        }
        var ruleIds = rulebook.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Vocabulary,
            Profession = profession,
            Task = AiTaskMode.GenerateVocabularyGloss,
        });

        var userMessage = BuildGlossUserMessage(word, request.Context, request.LetterType);

        VocabularyGlossResponse? parsed = null;
        try
        {
            var aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = "mock",
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.VocabularyGloss,
                UserId = userId,
            }, ct);

            parsed = TryParseGloss(aiResult.Completion, word, ruleIds, rulebook.Version);
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vocabulary gloss — provider error; returning deterministic fallback.");
        }

        parsed ??= new VocabularyGlossResponse(
            Term: word,
            IpaPronunciation: null,
            ShortDefinition: $"{word}: concise medical definition unavailable from the AI provider right now.",
            ExampleSentence: $"The patient's notes referenced {word}.",
            ContextNotes: string.IsNullOrWhiteSpace(request.Context) ? null : $"Requested with context: {Truncate(request.Context!, 160)}",
            Synonyms: Array.Empty<string>(),
            Register: "clinical",
            AppliedRuleIds: Array.Empty<string>(),
            RulebookVersion: rulebook.Version,
            MatchedExistingTerm: false,
            ExistingTermId: null);

        return parsed;
    }

    private static string BuildGlossUserMessage(string word, string? context, string? letterType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("A learner has requested a medical-English gloss for the following word.");
        sb.AppendLine();
        sb.AppendLine($"Word: {word}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine("Surrounding context (≤ 400 chars):");
            sb.AppendLine(Truncate(context!, 400));
        }
        if (!string.IsNullOrWhiteSpace(letterType))
            sb.AppendLine($"Letter type: {letterType}");
        sb.AppendLine();
        sb.AppendLine("Produce the JSON gloss strictly per the reply format above. Cite rule IDs from the vocabulary rulebook only.");
        return sb.ToString();
    }

    private static VocabularyGlossResponse? TryParseGloss(string completion, string word, HashSet<string> validRuleIds, string rulebookVersion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;

        var jsonText = ExtractJsonBlock(completion);
        if (jsonText is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var term = (SafeString(root, "term") ?? word).Trim();
            var shortDef = SafeString(root, "shortDefinition")?.Trim();
            var example = SafeString(root, "exampleSentence")?.Trim();
            if (string.IsNullOrWhiteSpace(shortDef) || string.IsNullOrWhiteSpace(example))
                return null;

            var ipa = SafeString(root, "ipaPronunciation");
            var contextNotes = SafeString(root, "contextNotes");
            var register = (SafeString(root, "register") ?? "clinical").Trim().ToLowerInvariant();
            var synonyms = ParseStringArray(root, "synonyms");
            var applied = ParseStringArray(root, "appliedRuleIds")
                .Where(id => validRuleIds.Contains(id))
                .ToList();

            return new VocabularyGlossResponse(
                Term: term,
                IpaPronunciation: ipa,
                ShortDefinition: shortDef!,
                ExampleSentence: example!,
                ContextNotes: contextNotes,
                Synonyms: synonyms,
                Register: register,
                AppliedRuleIds: applied,
                RulebookVersion: rulebookVersion,
                MatchedExistingTerm: false,
                ExistingTermId: null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonBlock(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var fenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0) fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return null;
        var afterFence = trimmed.IndexOf('\n', fenceStart);
        if (afterFence < 0) return null;
        var closeFence = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
        if (closeFence < 0) return null;
        var inner = trimmed[(afterFence + 1)..closeFence].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }

    private static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
    }

    private static List<string> ParseStringArray(JsonElement el, string property)
    {
        var result = new List<string>();
        if (!el.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in v.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s!.Trim());
        }
        return result;
    }

    private static List<string> TryParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json!) ?? new(); }
        catch { return new(); }
    }

    private static ExamProfession ParseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(raw.Replace("-", ""), ignoreCase: true, out var p)
            ? p
            : ExamProfession.Medicine;
    }

    private static string Truncate(string raw, int max)
        => string.IsNullOrEmpty(raw) ? "" : (raw.Length <= max ? raw : raw[..max].TrimEnd() + "…");
}
