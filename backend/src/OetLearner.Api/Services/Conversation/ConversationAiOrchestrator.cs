using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// Orchestrates grounded AI calls for the conversation feature. Every call
/// MUST pass through <see cref="IAiGatewayService.BuildGroundedPrompt"/> so
/// the gateway's <c>PromptNotGroundedException</c> guard fires on any bypass
/// attempt.
/// </summary>
public interface IConversationAiOrchestrator
{
    Task<ConversationAiReply> GenerateOpeningAsync(ConversationAiContext context, CancellationToken ct);
    Task<ConversationAiReply> GenerateReplyAsync(ConversationAiContext context, CancellationToken ct);
    Task<ConversationAiEvaluation> EvaluateAsync(ConversationAiContext context, CancellationToken ct);
}

public sealed record ConversationAiContext(
    string SessionId,
    string UserId,
    string? AuthAccountId,
    string? TenantId,
    ExamProfession Profession,
    string TaskTypeCode,
    string ScenarioJson,
    string TranscriptJson,
    int TurnIndex,
    int ElapsedSeconds,
    int RemainingSeconds,
    string? CandidateCountry);

public sealed record ConversationAiReply(
    string Text,
    string? EmotionHint,
    bool ShouldEnd,
    IReadOnlyList<string> AppliedRuleIds,
    string RulebookVersion);

public sealed record ConversationAiCriterion(
    string Id,
    double Score06,
    string Evidence,
    IReadOnlyList<string> Quotes);

public sealed record ConversationAiAnnotation(
    int TurnNumber,
    string Type,
    string Category,
    string RuleId,
    string Evidence,
    string? Suggestion);

public sealed record ConversationAiEvaluation(
    IReadOnlyList<ConversationAiCriterion> Criteria,
    IReadOnlyList<ConversationAiAnnotation> TurnAnnotations,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<string> SuggestedPractice,
    IReadOnlyList<string> AppliedRuleIds,
    string? Advisory,
    string RulebookVersion);

public sealed class ConversationAiOrchestrator(
    IAiGatewayService gateway,
    IRulebookLoader rulebookLoader,
    IOptions<ConversationOptions> options,
    ILogger<ConversationAiOrchestrator> logger) : IConversationAiOrchestrator
{
    private readonly ConversationOptions _options = options.Value;

    public Task<ConversationAiReply> GenerateOpeningAsync(ConversationAiContext ctx, CancellationToken ct)
        => GenerateReplyInternalAsync(ctx, AiTaskMode.GenerateConversationOpening, AiFeatureCodes.ConversationOpening, ct);

    public Task<ConversationAiReply> GenerateReplyAsync(ConversationAiContext ctx, CancellationToken ct)
        => GenerateReplyInternalAsync(ctx, AiTaskMode.GenerateConversationReply, AiFeatureCodes.ConversationReply, ct);

    private async Task<ConversationAiReply> GenerateReplyInternalAsync(
        ConversationAiContext ctx, AiTaskMode task, string featureCode, CancellationToken ct)
    {
        var grounding = new AiGroundingContext
        {
            Kind = RuleKind.Conversation,
            Profession = ctx.Profession,
            Task = task,
            CandidateCountry = ctx.CandidateCountry,
            ConversationScenarioJson = ctx.ScenarioJson,
            ConversationTranscriptJson = ctx.TranscriptJson,
            ConversationTaskTypeCode = ctx.TaskTypeCode,
            ConversationTurnIndex = ctx.TurnIndex,
            ConversationElapsedSeconds = ctx.ElapsedSeconds,
            ConversationRemainingSeconds = ctx.RemainingSeconds,
        };
        var prompt = gateway.BuildGroundedPrompt(grounding);
        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = BuildReplyUserInput(ctx, task),
            UserId = ctx.UserId,
            AuthAccountId = ctx.AuthAccountId,
            TenantId = ctx.TenantId,
            FeatureCode = featureCode,
            Model = string.IsNullOrWhiteSpace(_options.ReplyModel) ? "mock" : _options.ReplyModel,
            Temperature = _options.ReplyTemperature,
            MaxTokens = 300,
            PromptTemplateId = task.ToString(),
        }, ct);

        var (text, emotion, shouldEnd, rules) = ParseReply(result.Completion, prompt.Metadata.AppliedRuleIds);
        if (string.IsNullOrWhiteSpace(text))
            text = FallbackReplyText(task);
        return new ConversationAiReply(
            Text: text,
            EmotionHint: emotion,
            ShouldEnd: shouldEnd,
            AppliedRuleIds: rules,
            RulebookVersion: result.RulebookVersion);
    }

    public async Task<ConversationAiEvaluation> EvaluateAsync(ConversationAiContext ctx, CancellationToken ct)
    {
        var grounding = new AiGroundingContext
        {
            Kind = RuleKind.Conversation,
            Profession = ctx.Profession,
            Task = AiTaskMode.EvaluateConversation,
            CandidateCountry = ctx.CandidateCountry,
            ConversationScenarioJson = ctx.ScenarioJson,
            ConversationTranscriptJson = ctx.TranscriptJson,
            ConversationTaskTypeCode = ctx.TaskTypeCode,
            ConversationTurnIndex = ctx.TurnIndex,
            ConversationElapsedSeconds = ctx.ElapsedSeconds,
        };
        var prompt = gateway.BuildGroundedPrompt(grounding);
        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = "Evaluate the transcript above strictly in the reply format.",
            UserId = ctx.UserId,
            AuthAccountId = ctx.AuthAccountId,
            TenantId = ctx.TenantId,
            FeatureCode = AiFeatureCodes.ConversationEvaluation,
            Model = string.IsNullOrWhiteSpace(_options.EvaluationModel) ? "mock" : _options.EvaluationModel,
            Temperature = _options.EvaluationTemperature,
            MaxTokens = 1200,
            PromptTemplateId = "EvaluateConversation",
        }, ct);

        return ParseEvaluation(result.Completion, prompt.Metadata.AppliedRuleIds, result.RulebookVersion);
    }

    private static string BuildReplyUserInput(ConversationAiContext ctx, AiTaskMode task)
    {
        return task switch
        {
            AiTaskMode.GenerateConversationOpening =>
                "Produce the AI partner's first spoken utterance in role. Stay in character. 1–3 sentences. Respond strictly in the JSON format.",
            _ =>
                $"Produce the AI partner's next in-role spoken reply. Turn index: {ctx.TurnIndex}. Elapsed: {ctx.ElapsedSeconds}s, remaining: {ctx.RemainingSeconds}s. 1–3 sentences. Respond strictly in the JSON format.",
        };
    }

    private (string text, string? emotion, bool shouldEnd, IReadOnlyList<string> rules) ParseReply(
        string completion, IReadOnlyList<string> allowedRules)
    {
        try
        {
            var body = ExtractJson(completion);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
            var emotion = root.TryGetProperty("emotionHint", out var e) ? e.GetString() : null;
            var shouldEnd = root.TryGetProperty("shouldEnd", out var se) && se.ValueKind == JsonValueKind.True;
            var rules = ExtractStringArray(root, "appliedRuleIds")
                .Where(id => allowedRules.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            return (text, emotion, shouldEnd, rules);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse AI conversation reply; falling back to plain text.");
            return (completion.Trim(), null, false, Array.Empty<string>());
        }
    }

    private ConversationAiEvaluation ParseEvaluation(string completion, IReadOnlyList<string> allowedRules, string rulebookVersion)
    {
        try
        {
            var body = ExtractJson(completion);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var criteria = new List<ConversationAiCriterion>();
            if (root.TryGetProperty("criteria", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cs.EnumerateArray())
                {
                    var id = c.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                    var score = c.TryGetProperty("score06", out var sEl) && sEl.ValueKind == JsonValueKind.Number ? sEl.GetDouble() : 0.0;
                    var evidence = c.TryGetProperty("evidence", out var evEl) ? (evEl.GetString() ?? "") : "";
                    var quotes = ExtractStringArray(c, "quotes");
                    criteria.Add(new ConversationAiCriterion(id, Math.Clamp(score, 0, 6), evidence, quotes));
                }
            }
            // Ensure canonical 4 criteria exist even if AI missed some.
            foreach (var need in new[] { "intelligibility", "fluency", "appropriateness", "grammar_expression" })
            {
                if (!criteria.Any(x => string.Equals(x.Id, need, StringComparison.OrdinalIgnoreCase)))
                    criteria.Add(new ConversationAiCriterion(need, 0, "no evidence", Array.Empty<string>()));
            }

            var annotations = new List<ConversationAiAnnotation>();
            if (root.TryGetProperty("turnAnnotations", out var tas) && tas.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in tas.EnumerateArray())
                {
                    var turnNumber = a.TryGetProperty("turnNumber", out var tn) && tn.ValueKind == JsonValueKind.Number ? tn.GetInt32() : 0;
                    var type = a.TryGetProperty("type", out var ty) ? (ty.GetString() ?? "improvement") : "improvement";
                    var category = a.TryGetProperty("category", out var cat) ? (cat.GetString() ?? "general") : "general";
                    var ruleId = a.TryGetProperty("ruleId", out var r) ? (r.GetString() ?? "") : "";
                    var evidence = a.TryGetProperty("evidence", out var ev) ? (ev.GetString() ?? "") : "";
                    var suggestion = a.TryGetProperty("suggestion", out var sg) ? sg.GetString() : null;
                    // Require every rule reference to exist in the loaded rulebook.
                    if (!string.IsNullOrEmpty(ruleId) && !allowedRules.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Discarding annotation with unknown ruleId {RuleId}", ruleId);
                        continue;
                    }
                    annotations.Add(new ConversationAiAnnotation(turnNumber, type, category, ruleId, evidence, suggestion));
                }
            }

            var strengths = ExtractStringArray(root, "strengths");
            var improvements = ExtractStringArray(root, "improvements");
            var suggestedPractice = ExtractStringArray(root, "suggestedPractice");
            var appliedRules = ExtractStringArray(root, "appliedRuleIds")
                .Where(id => allowedRules.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var advisory = root.TryGetProperty("advisory", out var adv) ? adv.GetString() : "AI-generated — advisory only, not a CBLA result.";

            return new ConversationAiEvaluation(
                Criteria: criteria,
                TurnAnnotations: annotations,
                Strengths: strengths,
                Improvements: improvements,
                SuggestedPractice: suggestedPractice,
                AppliedRuleIds: appliedRules,
                Advisory: advisory,
                RulebookVersion: rulebookVersion);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse AI conversation evaluation; returning degraded minimum.");
            return new ConversationAiEvaluation(
                Criteria: new[]
                {
                    new ConversationAiCriterion("intelligibility", 0, "unable to parse", Array.Empty<string>()),
                    new ConversationAiCriterion("fluency", 0, "unable to parse", Array.Empty<string>()),
                    new ConversationAiCriterion("appropriateness", 0, "unable to parse", Array.Empty<string>()),
                    new ConversationAiCriterion("grammar_expression", 0, "unable to parse", Array.Empty<string>()),
                },
                TurnAnnotations: Array.Empty<ConversationAiAnnotation>(),
                Strengths: Array.Empty<string>(),
                Improvements: new[] { "AI evaluator could not parse the response. Please try again." },
                SuggestedPractice: Array.Empty<string>(),
                AppliedRuleIds: Array.Empty<string>(),
                Advisory: "AI-generated — parse error, scores set to 0 pending retry.",
                RulebookVersion: rulebookVersion);
        }
    }

    private static string FallbackReplyText(AiTaskMode task) => task == AiTaskMode.GenerateConversationOpening
        ? "Hello. How can I help you today?"
        : "Could you tell me a little more about that, please?";

    private static string[] ExtractStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
        }
        return list.ToArray();
    }

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        var s = raw.Trim();
        // Strip ```json fences if present.
        if (s.StartsWith("```"))
        {
            var first = s.IndexOf('\n');
            if (first > 0) s = s[(first + 1)..];
            var last = s.LastIndexOf("```", StringComparison.Ordinal);
            if (last > 0) s = s[..last];
        }
        // Find first { and last } in case the model added prose.
        var open = s.IndexOf('{');
        var close = s.LastIndexOf('}');
        if (open >= 0 && close > open) return s.Substring(open, close - open + 1);
        return s;
    }
}
