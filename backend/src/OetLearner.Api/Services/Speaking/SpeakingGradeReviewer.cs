using System.Text.Json;
using System.Text.Json.Nodes;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Secondary reviewer of a Speaking grade (<c>speaking.grade.review</c>): after Claude (Max) has graded, GPT-6.1 Sol on the
/// Codex subscription route re-reads the same transcript and Claude's grade and returns a corrected grade in the same JSON schema.
/// Best effort and bounded: the reviewer can move a criterion score by at most one band, it never blocks or fails a grade
/// (any failure, timeout or unreadable reply returns Claude's grade untouched), and it is never a paid API route.
/// Works on the completion text so the classic and v1.1 assessors share it unchanged.
/// </summary>
public static class SpeakingGradeReviewer
{
    public const string TemplateId = "speaking.grade.review.v1";
    private const int BudgetSeconds = 600;

    private const string Instructions = """

        ── SECONDARY REVIEW ──
        You are the second assessor. The grade above was produced by a first assessor. Re-read the transcript and the rubric,
        then return the SAME JSON object and schema, corrected only where the first grade is wrong: a score the transcript does
        not support, a rationale that misreads it, or an evidence quote that is not in the transcript. Change a score only with
        transcript evidence, and by no more than one band. If the grade is right, return it unchanged. JSON only.
        """;

    /// <summary>Returns the completion to parse: Claude's, or Claude's with the reviewer's bounded corrections.</summary>
    public static async Task<string> ReviewAsync(
        IAiGatewayService gateway,
        AiGatewayRequest primaryRequest,
        AiGatewayResult primary,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));
            var review = await gateway.CompleteAsync(primaryRequest with
            {
                UserInput = primaryRequest.UserInput + "\n\n── FIRST ASSESSOR'S GRADE ──\n" + primary.Completion + Instructions,
                Provider = WritingSubscriptionProviders.Codex,
                Model = WritingSubscriptionProviders.CodexModel,
                MaxTokens = 8000,
                FeatureCode = AiFeatureCodes.SpeakingGradeReview,
                PromptTemplateId = TemplateId,
                // Reviews a grade the learner already holds: no credit movement, no plan-feature gate.
                FreeSampleGrant = true,
            }, budget.Token);

            var merged = Merge(primary.Completion, review.Completion);
            return merged ?? primary.Completion;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogWarning("Speaking review skipped ({ErrorType}); keeping the primary grade.", ex.GetType().Name);
            return primary.Completion;
        }
    }

    /// <summary>The primary JSON with each criterion's score moved toward the reviewer's by at most one band; a moved
    /// criterion also takes the reviewer's rationale and quotes. Null = reviewer reply unreadable or nothing changed.</summary>
    internal static string? Merge(string? primaryJson, string? reviewJson)
    {
        if (Parse(primaryJson) is not { } primary || Parse(reviewJson) is not { } review) return null;
        if (primary["criterionScores"] is not JsonObject pc || review["criterionScores"] is not JsonObject rc) return null;

        var changed = false;
        foreach (var (name, pNode) in pc.ToList())
        {
            if (!rc.TryGetPropertyValue(name, out var rNode)) continue;
            var pScore = ScoreOf(pNode);
            var rScore = ScoreOf(rNode);
            if (pScore is null || rScore is null || pScore == rScore) continue;

            var moved = pScore.Value + Math.Sign(rScore.Value - pScore.Value);
            if (pNode is JsonObject po)
            {
                po["score"] = moved;
                if (rNode is JsonObject ro)
                {
                    if (ro["rationale"] is JsonNode rationale) po["rationale"] = rationale.DeepClone();
                    if (ro["evidenceQuotes"] is JsonNode quotes) po["evidenceQuotes"] = quotes.DeepClone();
                }
            }
            else
            {
                pc[name] = moved;
            }
            changed = true;
        }

        return changed ? primary.ToJsonString() : null;
    }

    private static int? ScoreOf(JsonNode? node)
    {
        var value = node is JsonObject o ? o["score"] : node;
        return value is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    }

    private static JsonObject? Parse(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonNode.Parse(completion[start..(end + 1)]) as JsonObject; }
        catch (JsonException) { return null; }
    }
}
