using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// Optional Phase-7 layer: produces per-learner AI-authored rationale addenda
/// for plan items. Runs AFTER the deterministic generator picks a template +
/// materialises items, so the AI can ONLY add text — it cannot invent tasks.
///
/// <para>
/// Routes every call through <see cref="IAiGatewayService"/>, tagged with the
/// <c>AiFeatureCodes.StudyPlanReasoning</c> feature code. Grounded via the
/// existing Writing rulebook (the closest match for "personalised study
/// advice" in the current ruleset; it enforces the same guardrails).
/// </para>
///
/// <para>
/// Output is strictly validated against a narrow JSON schema. If the AI
/// returns anything that doesn't parse, we silently skip — the generator's
/// deterministic output remains intact. The gateway's own refusal path
/// (ungrounded / kill-switch / quota-exceeded) also short-circuits safely.
/// </para>
/// </summary>
public interface IStudyPlannerAiReasoner
{
    /// <summary>
    /// Given a fresh plan's materialised items + the learner context, return
    /// item-level rationale addenda. Never throws to the caller — returns an
    /// empty map on any failure.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ProduceAddendaAsync(
        LearnerPlanContext ctx,
        IReadOnlyList<StudyPlanItem> items,
        CancellationToken ct);
}

public sealed class StudyPlannerAiReasoner(IAiGatewayService gateway, ILogger<StudyPlannerAiReasoner>? logger = null)
    : IStudyPlannerAiReasoner
{
    public async Task<IReadOnlyDictionary<string, string>> ProduceAddendaAsync(
        LearnerPlanContext ctx,
        IReadOnlyList<StudyPlanItem> items,
        CancellationToken ct)
    {
        if (items.Count == 0) return new Dictionary<string, string>();
        try
        {
            var groundingCtx = new AiGroundingContext
            {
                Kind = RuleKind.Writing,                  // closest rule kind
                Profession = ParseProfession(ctx.ProfessionId),
                Task = AiTaskMode.GenerateContent,
                CandidateCountry = ctx.TargetCountry,
            };
            var prompt = gateway.BuildGroundedPrompt(groundingCtx);

            var itemSummary = items.Select(i => new
            {
                id = i.Id,
                title = i.Title,
                subtest = i.SubtestCode,
                durationMin = i.DurationMinutes,
                rationale = i.Rationale,
            }).ToArray();

            var userInput = BuildUserPrompt(ctx, itemSummary);

            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Provider = "",
                Model = "mock",
                Temperature = 0.3,
                MaxTokens = 1200,
                UserId = ctx.UserId,
                FeatureCode = AiFeatureCodes.StudyPlanReasoning,
                PromptTemplateId = "study_plan.reasoning.v1",
            }, ct);

            return ParseAddenda(result.Completion);
        }
        catch (PromptNotGroundedException ex)
        {
            logger?.LogWarning("Study plan AI reasoner refused: {Reason}", ex.Message);
            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            logger?.LogInformation(ex, "Study plan AI reasoner failed; using deterministic rationales only.");
            return new Dictionary<string, string>();
        }
    }

    private static string BuildUserPrompt(LearnerPlanContext ctx, object items)
    {
        // The gateway's task mode is GenerateContent — we ask for a tightly-
        // scoped JSON shape. The `addenda` map is 1-to-1 with item IDs; any
        // extra keys get ignored by the parser.
        var payload = new
        {
            learner = new
            {
                profession = ctx.ProfessionId,
                country = ctx.TargetCountry,
                weeksToExam = ctx.WeeksToExam,
                hoursPerWeek = ctx.HoursPerWeek,
                weakSubtests = ctx.WeakSubtests,
            },
            items,
            instruction = "For each item, produce a short (≤ 2 sentences, ≤ 280 chars) second-person rationale addendum tying the task to this learner's context. Respond with exactly this JSON shape: { \"addenda\": { \"<itemId>\": \"<text>\" } } — no prose before/after, no extra keys.",
        };
        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyDictionary<string, string> ParseAddenda(string completion)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(completion)) return result;
        // The mock provider (used for tests) returns templated text, not JSON;
        // try a JSON extract first, then silently fall back to empty.
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return result;
        var slice = completion.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(slice);
            if (!doc.RootElement.TryGetProperty("addenda", out var addenda)) return result;
            if (addenda.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in addenda.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var text = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text!.Length > 300) text = text[..297] + "…";
                result[prop.Name] = text;
            }
        }
        catch (JsonException)
        {
            // ignore; ungrounded / malformed output
        }
        return result;
    }

    private static ExamProfession ParseProfession(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ExamProfession.Medicine;
        return Enum.TryParse<ExamProfession>(id, ignoreCase: true, out var p) ? p : ExamProfession.Medicine;
    }
}
