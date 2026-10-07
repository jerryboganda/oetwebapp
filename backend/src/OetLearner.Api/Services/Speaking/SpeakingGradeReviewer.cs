using System.Text.Json;
using System.Text.Json.Nodes;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.Review;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Secondary reviewer of a Speaking grade (<c>speaking.grade.review</c>): after Claude (Max) has graded, GPT-6.1 Sol on the
/// Codex subscription route re-reads the same transcript and Claude's grade and returns a corrected grade in the same JSON schema.
/// Runs through the SHARED reviewer pipeline (<see cref="SharedReviewerRunner"/> + <see cref="CodexReviewerGate"/>), so
/// Writing and Speaking reviews share one bounded capacity limit, wait FIFO, and one automatic fallback: when Codex cannot
/// take or complete the review inside its bounded budgets (saturated lane, quota, unavailable, timeout, retries exhausted)
/// the SAME review prompt runs on the configured API reviewer route instead. Bounded and best effort: the reviewer can move
/// a criterion score by at most one band and it never blocks or fails a grade - if BOTH routes fail, Claude's grade stands
/// untouched and the trace records why. Works on the completion text so the classic and v1.1 assessors share it unchanged.
/// </summary>
public static class SpeakingGradeReviewer
{
    // v2 (8 Oct 2026): the reviewer may no longer move a score for accent or pronunciation (owner guardrail: a noticeable
    // first-language accent is not a penalty while the candidate is easily understood), nor lower two criteria for one event.
    public const string TemplateId = "speaking.grade.review.v2";

    private const string Instructions = """

        ── SECONDARY REVIEW ──
        You are the second assessor. The grade above was produced by a first assessor. Re-read the transcript and the rubric,
        then return the SAME JSON object and schema, corrected only where the first grade is wrong: a score the transcript does
        not support, a rationale that misreads it, or an evidence quote that is not in the transcript. Change a score only with
        transcript evidence, and by no more than one band. If the grade is right, return it unchanged. JSON only.

        Guardrails for this review (they bind you as they bound the first assessor):
          * Do not use a native-speaker standard. A noticeable first-language accent is never a reason to change a score while
            the candidate is easily understood. Accent is not a Grammar or Appropriateness problem either.
          * The transcript is automatic speech recognition. An odd or misspelt word may be a recognition error caused by accent
            or audio quality; do not change a score for it unless the candidate's wording is clearly wrong across several turns
            or the patient reacts to it.
          * Never change Intelligibility when an ACOUSTIC EVIDENCE block is present (it is final). When there is none, change it
            only if the patient visibly asks the candidate to repeat or clarify.
          * One event counts against one criterion only. A hesitation, repetition, filler, restart or self-correction is Fluency
            evidence only; never lower Grammar or Intelligibility for it.
        """;

    /// <summary>
    /// Reviews one primary Speaking grade through the shared reviewer pipeline. <paramref name="assessmentId"/> is the
    /// session/exam id for queue logs; <paramref name="options"/> defaults to the startup-configured shared policy
    /// (tests pass their own bounded policy). Returns Claude's grade untouched when both reviewer routes fail
    /// (never fails the grade, never waits on a Codex quota reset).
    /// </summary>
    public static async Task<SpeakingReviewResult> ReviewAsync(
        IAiGatewayService gateway,
        AiGatewayRequest primaryRequest,
        AiGatewayResult primary,
        ILogger logger,
        CancellationToken ct,
        string assessmentId = "speaking_session",
        SharedReviewerOptions? options = null)
    {
        var shared = options ?? SharedReviewerOptions.Current;
        var template = ReviewRequest(primaryRequest);

        try
        {
            var (result, info) = await SharedReviewerRunner.RunAsync(
                shared,
                CodexReviewerGate.Default,
                "speaking",
                assessmentId,
                token => AttemptAsync(gateway, template, primary, WritingSubscriptionProviders.Codex, WritingSubscriptionProviders.CodexModel, token),
                token => AttemptAsync(gateway, template, primary, shared.ApiFallbackProvider, shared.ApiFallbackModel, token),
                logger,
                ct);

            return result with { Trace = result.Trace with { Provider = info.FinalProvider, FallbackReason = info.FallbackReason } };
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogWarning("Speaking review skipped ({ErrorType}); keeping the primary grade.", ex.GetType().Name);
            return new SpeakingReviewResult(
                primary.Completion,
                new SpeakingReviewTrace("failed", null, ScoresOf(primary.Completion) ?? new Dictionary<string, int>(), null, []));
        }
    }

    /// <summary>The reviewer request for one route: Claude's grade plus the review instructions, pinned per attempt.</summary>
    private static AiGatewayRequest ReviewRequest(AiGatewayRequest primaryRequest) => primaryRequest with
    {
        MaxTokens = 8000,
        FeatureCode = AiFeatureCodes.SpeakingGradeReview,
        PromptTemplateId = TemplateId,
        // Reviews a grade the learner already holds: no credit movement, no plan-feature gate.
        FreeSampleGrant = true,
    };

    private static async Task<SpeakingReviewResult> AttemptAsync(
        IAiGatewayService gateway,
        AiGatewayRequest template,
        AiGatewayResult primary,
        string providerCode,
        string model,
        CancellationToken token)
    {
        // The per-attempt wall-clock budget is the shared runner's (CodexAttemptSeconds); the token arrives with it.
        var review = await gateway.CompleteAsync(template with
        {
            UserInput = template.UserInput + "\n\n── FIRST ASSESSOR'S GRADE ──\n" + primary.Completion + Instructions,
            Provider = providerCode,
            Model = model,
        }, token);

        var merged = Merge(primary.Completion, review.Completion);
        var resolvedModel = string.IsNullOrWhiteSpace(review.ResolvedModel) ? model : review.ResolvedModel.Trim();
        return new SpeakingReviewResult(
            merged ?? primary.Completion,
            BuildTrace(primary.Completion, review.Completion, merged, resolvedModel));
    }

    /// <summary>What the review did: <c>ran</c> (it moved at least one criterion), <c>unchanged</c> (it replied and agreed) or
    /// <c>failed</c> (its reply was unreadable). Pure: unit-testable without a gateway.</summary>
    internal static SpeakingReviewTrace BuildTrace(string? primaryCompletion, string? reviewCompletion, string? mergedCompletion, string? model)
    {
        var primary = ScoresOf(primaryCompletion) ?? new Dictionary<string, int>();
        var reviewer = ScoresOf(reviewCompletion);
        var final = ScoresOf(mergedCompletion);
        var changes = new List<SpeakingReviewChange>();
        if (final is not null)
        {
            foreach (var (criterion, to) in final)
            {
                if (primary.TryGetValue(criterion, out var from) && from != to)
                {
                    changes.Add(new SpeakingReviewChange(
                        criterion, from, to, reviewer is not null && reviewer.TryGetValue(criterion, out var raw) ? raw : to));
                }
            }
        }

        var status = reviewer is null ? "failed" : changes.Count > 0 ? "ran" : "unchanged";
        return new SpeakingReviewTrace(status, model, primary, reviewer, changes);
    }

    /// <summary>The criterion scores a grade reply carries, by CANONICAL criterion code (the same mapping the grade parser
    /// applies: <c>grammar</c> is <c>grammarExpression</c>, <c>providingStructure</c> is <c>structure</c>; the canonical spelling
    /// wins when both are present); null when the reply is unreadable. A score written as "4" or 4.0 still counts.</summary>
    internal static Dictionary<string, int>? ScoresOf(string? completion)
    {
        if (Parse(completion) is not { } root || root["criterionScores"] is not JsonObject scores) return null;
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, node) in scores)
        {
            var code = SpeakingAiAssessmentService.CanonicalCriterionCode(name);
            var isAlias = !string.Equals(code, name, StringComparison.Ordinal);
            if (isAlias && map.ContainsKey(code)) continue;
            if (LenientScoreOf(node) is { } score) map[code] = score;
        }

        return map;
    }

    private static int? LenientScoreOf(JsonNode? node)
    {
        var value = node is JsonObject o ? o["score"] : node;
        if (value is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Round(d);
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
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

/// <summary>One criterion the reviewer moved: the first assessor's score, the score after the review (at most one band away),
/// and the score the reviewer itself proposed.</summary>
public sealed record SpeakingReviewChange(string Criterion, int From, int To, int ReviewerRaw);

/// <summary>
/// What the secondary review did to one grade. <c>Status</c>: <c>ran</c> | <c>unchanged</c> | <c>failed</c> | <c>skipped</c> (the
/// first grade was unreadable, so no review was attempted). <c>PrimaryScores</c> are Claude's own scores BEFORE the review;
/// <c>ReviewerScores</c> what the reviewer proposed (null when it failed). <c>Provider</c> is the route that produced the
/// review (<c>writing-codex-sub</c> or the API fallback row) and <c>FallbackReason</c> why the shared pipeline left Codex
/// (null on a Codex review). Scores only: no transcript, no learner text.
/// </summary>
public sealed record SpeakingReviewTrace(
    string Status,
    string? Model,
    IReadOnlyDictionary<string, int> PrimaryScores,
    IReadOnlyDictionary<string, int>? ReviewerScores,
    IReadOnlyList<SpeakingReviewChange> Changes,
    string? Provider = null,
    string? FallbackReason = null)
{
    public static SpeakingReviewTrace Skipped(IReadOnlyDictionary<string, int>? primaryScores = null)
        => new("skipped", null, primaryScores ?? new Dictionary<string, int>(), null, []);
}

/// <summary>The completion to parse after the review, and what the review did.</summary>
public sealed record SpeakingReviewResult(string Completion, SpeakingReviewTrace Trace);

