using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingModelAnswerSemanticRequest(
    Guid ScenarioId,
    ExamProfession Profession,
    string LetterType,
    string TaskPrompt,
    string CaseNotes,
    string LetterText,
    string RulePackHash,
    string AdminUserId);

public sealed record WritingModelAnswerSemanticViolation(string RuleId, string Quote, string Message);

public sealed record WritingModelAnswerSemanticResult(
    bool Passed,
    bool Unavailable,
    IReadOnlyList<WritingModelAnswerSemanticViolation> Violations,
    string? Model,
    string? RulebookVersion,
    string? Error);

/// <summary>
/// Independent, task-aware SEMANTIC Model Answer validator (Addendum Rev8 §14):
/// receives the exact case notes, exact Writing Task, profession, letter type
/// and the same active rule pack (grounded prompt for the profession + the
/// owner Rev8 house style + the rule-pack fingerprint) the generator used, and
/// judges what a regex cannot: purpose clarity, relevance, chronology,
/// urgency structure, background placement, factual fidelity/certainty,
/// recipient appropriateness, non-judgmental wording and profession rules.
/// </summary>
public interface IWritingModelAnswerSemanticValidator
{
    Task<WritingModelAnswerSemanticResult> ValidateAsync(WritingModelAnswerSemanticRequest request, CancellationToken ct = default);
}

public sealed class WritingModelAnswerSemanticValidator(
    IAiGatewayService gateway,
    ILogger<WritingModelAnswerSemanticValidator> logger) : IWritingModelAnswerSemanticValidator
{
    // Must fit AiOperation.PromptVersion varchar(32).
    public const string PromptVersion = "writing.model-answer-validate.v1";
    private const string PinnedProvider = "anthropic";
    private const string PinnedModel = "claude-sonnet-5";

    public async Task<WritingModelAnswerSemanticResult> ValidateAsync(WritingModelAnswerSemanticRequest request, CancellationToken ct = default)
    {
        try
        {
            var legacyLetterType = WritingLetterTypeTaxonomy.ToLegacyLetterType(request.LetterType);
            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = request.Profession,
                LetterType = legacyLetterType,
                Task = AiTaskMode.GenerateContent,
            });

            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                Provider = PinnedProvider,
                Model = PinnedModel,
                Temperature = 0,
                MaxTokens = 16000,
                EnableExtendedThinking = true,
                ThinkingEffort = "high",
                FeatureCode = AiFeatureCodes.WritingModelAnswerPregenerate,
                PromptTemplateId = PromptVersion,
                UserId = request.AdminUserId,
                AssessmentContext = AiAssessmentContext.Practice,
                ResourceId = request.ScenarioId.ToString("D"),
                ResourceType = "writing_task_model_answer_validation",
                UserInput = BuildUserInput(request, legacyLetterType),
            }, ct);

            var parsed = Parse(result.Completion);
            if (parsed is null)
            {
                return new WritingModelAnswerSemanticResult(false, true, [], result.ResolvedModel, result.RulebookVersion,
                    "semantic_validator_unreadable");
            }

            return new WritingModelAnswerSemanticResult(
                parsed.Count == 0, false, parsed, result.ResolvedModel, result.RulebookVersion, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Semantic Model Answer validation failed for scenario {ScenarioId}", request.ScenarioId);
            return new WritingModelAnswerSemanticResult(false, true, [], null, null, "semantic_validator_failed");
        }
    }

    internal static string BuildUserInput(WritingModelAnswerSemanticRequest request, string legacyLetterType) => $$"""
        ROLE: You are the INDEPENDENT validator of a saved OET Writing MODEL ANSWER (an exemplar shown
        to every candidate). You do not rewrite it. You report every violation of the active rules that
        remains in it. Ignore the default reply format of the system prompt: reply with ONE JSON object
        only, exactly {"violations":[{"ruleId":"...","quote":"...","message":"..."}]} — an empty
        "violations" array means the letter fully complies.

        Profession: {{request.Profession}}
        Letter type: {{legacyLetterType}}
        Active rule pack: {{request.RulePackHash}} (validator {{WritingRuleEngine.ValidatorVersion}}, owner house style {{WritingRev8HouseStyle.Version}})

        Check ONLY what a deterministic regex cannot, strictly against the EXACT case notes and task below:
        1. PURPOSE — the introduction states the task-specific purpose/request immediately and correctly (right recipient, right action); nothing actionable is delayed to the end.
        2. FIDELITY — every fact is present in the case notes; no invented diagnosis, test, treatment, dose, route, frequency, date or request; laterality, doses, units, dates and CERTAINTY LEVEL are preserved (a suspected diagnosis stays suspected; a prophylactic intention is not upgraded to a guarantee).
        3. RELEVANCE — only information relevant to this recipient and purpose; no important relevant item omitted (for a hospital urgent referral: every ongoing condition with active medication and its dose).
        4. ORGANISATION — routine/non-urgent: main complaint/current reason first, relevant background near the end before the closure; urgent: body paragraph 1 is today's/current presentation ONLY, then earlier history chronologically; discharge/update: letter-type exceptions respected (no family/social/smoking/occupation history the recipient already knows).
        5. CLOSURE — the closure closes the letter: no management/history after the request, no verbatim repetition of the introduction's request, ends with a contact-offer sentence{{(legacyLetterType == "urgent_referral" ? "; urgent: contains \"at your earliest convenience\" before that final sentence" : "")}}.
        6. TONE & PERSON — neutral, non-emotional, non-judgmental; the named patient is never referred to as "the patient" or by a relationship label; register suits the recipient.
        7. PROFESSION — the profession-specific rules of the active rulebook for {{request.Profession}}.
        Use ruleId values from the active rulebook or OWN-W-001..OWN-W-038. Quote the exact offending words.
        Do not report stylistic preferences that the rules do not require. Do not report deterministic
        layout/format rules (spacing, brackets, date format, DOB colon, medication punctuation, number words,
        linker vocabulary) — those are checked separately. The owner Rev8 house style is in the system
        prompt (guardrail 11) and is authoritative.

        EXACT WRITING TASK:
        ---
        {{request.TaskPrompt}}
        ---
        EXACT CASE NOTES:
        ---
        {{request.CaseNotes}}
        ---
        MODEL ANSWER UNDER VALIDATION:
        ---
        {{request.LetterText}}
        ---
        """;

    internal static IReadOnlyList<WritingModelAnswerSemanticViolation>? Parse(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            var root = doc.RootElement;
            // Tolerate a model that wrapped the object in the system prompt's
            // {"content": "..."} envelope.
            if (!root.TryGetProperty("violations", out var violations)
                && root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                return Parse(content.GetString());
            }
            if (!root.TryGetProperty("violations", out violations) || violations.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<WritingModelAnswerSemanticViolation>();
            foreach (var v in violations.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                var ruleId = v.TryGetProperty("ruleId", out var r) ? r.GetString() ?? "semantic" : "semantic";
                var quote = v.TryGetProperty("quote", out var q) ? q.GetString() ?? "" : "";
                var message = v.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(message)) continue;
                list.Add(new WritingModelAnswerSemanticViolation(ruleId.Trim(), quote.Trim(), message.Trim()));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
