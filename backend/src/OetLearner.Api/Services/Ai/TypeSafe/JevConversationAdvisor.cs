using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>Advisory judgment of the LEARNER's latest conversation turn,
/// produced alongside the AI patient's next reply. Strictly informational:
/// nothing here gates, scores, or ends a session — the conversation engine
/// owns all of that. A future UI may surface coaching hints from it.</summary>
public sealed record ConversationTurnSignal(
    /// <summary>Does the learner's turn stay inside the clinical role-play scenario?</summary>
    double StaysInRole,
    /// <summary>Is the turn a clinically sensible next step for the scenario?</summary>
    double ClinicallyAppropriate,
    /// <summary>Does the turn contain abusive, hateful, threatening, or sexually explicit content?</summary>
    double UnsafeContent,
    JevCallStatus Status);

/// <summary>
/// Phase-2 conversation slice (flag <c>TypeSafe:ConversationAdvisoryEnabled</c>,
/// default OFF). One parallel-Noul judgment of the learner's latest turn in
/// the AI-patient role-play, run alongside reply generation. Fail-soft twice
/// over: returns null on any failure and never throws.
/// </summary>
public interface IJevConversationAdvisor
{
    /// <summary>Judges the learner's latest transcript turn. Returns null
    /// when the flag is off, the judgment is unavailable, or anything
    /// crashes — the conversation flow carries on unchanged.</summary>
    Task<ConversationTurnSignal?> AssessLatestTurnAsync(
        string transcriptJson,
        int turnIndex,
        string? userId,
        CancellationToken ct);
}

public sealed class JevConversationAdvisor(
    ITypeSafeJudgmentService judgments,
    IOptions<TypeSafeOptions> options,
    ILogger<JevConversationAdvisor> logger) : IJevConversationAdvisor
{
    public async Task<ConversationTurnSignal?> AssessLatestTurnAsync(
        string transcriptJson,
        int turnIndex,
        string? userId,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.ConversationAdvisoryEnabled) return null;
        if (string.IsNullOrWhiteSpace(transcriptJson)) return null;

        try
        {
            var result = await judgments.AskAsync(new JevJudgmentRequest
            {
                StateJson = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    transcript = transcriptJson,
                    turnIndex,
                }),
                Questions =
                [
                    new JevQuestion
                    {
                        Id = "jev_stays_in_role",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "In `state.transcript`, does the learner turn with the highest index stay inside the clinical role-play scenario? Answer yes only when the learner speaks as a clinician in the scenario.",
                        NoulCriteria = new Dictionary<string, string?>
                        {
                            ["true"] = "The learner's latest turn is spoken in-clinica-role, addressing the patient within the scenario.",
                            ["false"] = "The learner's latest turn breaks role, addresses the system/examiner, or is unrelated to the scenario.",
                        },
                    },
                    new JevQuestion
                    {
                        Id = "jev_clinically_appropriate",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "In `state.transcript`, is the learner turn with the highest index a clinically sensible utterance for the scenario so far (gathering information, explaining, reassuring, or advancing the consultation)?",
                        NoulCriteria = new Dictionary<string, string?>
                        {
                            ["true"] = "The turn moves the consultation forward sensibly for the clinical context.",
                            ["false"] = "The turn is incoherent, clinically nonsensical, or does not advance the consultation.",
                        },
                    },
                    new JevQuestion
                    {
                        Id = "jev_unsafe_content",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "Does the learner turn with the highest index in `state.transcript` contain abusive, hateful, threatening, or sexually explicit content?",
                        NoulCriteria = new Dictionary<string, string?>
                        {
                            ["true"] = "The turn contains abusive, hateful, threatening, or explicit content.",
                            ["false"] = "The turn contains no such content.",
                        },
                    },
                ],
            }, new JevCallMetadata
            {
                FeatureCode = AiFeatureCodes.JevConversationTurn,
                UserId = userId,
                ResourceType = "conversation_turn",
            }, ct);

            if (!result.IsOk) return null;

            double Get(string id) => result.Answers!.TryGetValue(id, out var a) && a.Noul is not null ? a.Noul.Probability : 0.5;
            return new ConversationTurnSignal(
                Get("jev_stays_in_role"),
                Get("jev_clinically_appropriate"),
                Get("jev_unsafe_content"),
                JevCallStatus.Ok);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Jev conversation advisory crashed; turn carries no advisory signal.");
            return null;
        }
    }
}
