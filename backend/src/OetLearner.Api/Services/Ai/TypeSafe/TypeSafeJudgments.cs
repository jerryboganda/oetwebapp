using System.Text.Json;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>The three TypeSafe SystemOne question kinds. Wire types:
/// <c>noul</c> | <c>choice</c> | <c>score</c>.</summary>
public enum JevQuestionKind
{
    Noul = 0,
    Choice = 1,
    Score = 2,
}

/// <summary>
/// One typed judgment question. Question ids are code-only — the TypeSafe API
/// never sends them to the model — so every instruction must carry its full
/// meaning on its own, and the state must name the fields the instruction
/// references. Jev reads literally: state the exact condition, put boundary
/// cases in the criteria, and never bundle several judgments into one
/// question (see docs.typesafe.ai model-jaggedness for jev-1.13 limits).
/// </summary>
public sealed record JevQuestion
{
    public required string Id { get; init; }
    public required JevQuestionKind Kind { get; init; }
    public required string Instructions { get; init; }

    /// <summary>Choice only: option key → optional rubric description
    /// (null when the key needs no extra detail). Options are a closed set —
    /// include an explicit "none"/"unclear" key when nothing may fit.</summary>
    public IReadOnlyDictionary<string, string?>? ChoiceCriteria { get; init; }

    /// <summary>Noul only: what a yes (near 1) and a no (near 0) mean.
    /// Keys "true" and "false".</summary>
    public IReadOnlyDictionary<string, string?>? NoulCriteria { get; init; }

    /// <summary>Score only: ordered level descriptions (at least two).
    /// Levels must describe concrete situations and stand on their own.</summary>
    public IReadOnlyList<string>? ScoreLevels { get; init; }
}

/// <summary>
/// One judgment request: state + model + parallel questions. Independent
/// questions over the same state MUST go in one call — they run in parallel
/// and each additional call re-pays the state tokens. A second request is
/// warranted only when an earlier answer is needed to build new state.
/// </summary>
public sealed record JevJudgmentRequest
{
    /// <summary>Plain-text state. Exactly one of <see cref="StateText"/> /
    /// <see cref="StateJson"/> must be supplied.</summary>
    public string? StateText { get; init; }

    /// <summary>Structured state (object/array), already built as JSON.</summary>
    public JsonElement? StateJson { get; init; }

    public required IReadOnlyList<JevQuestion> Questions { get; init; }
}

/// <summary>Parsed Noul answer — P(yes) on 0..1. There is deliberately no
/// confidence field: a Noul near 0.5 means genuinely torn, not "medium".</summary>
public sealed record JevNoulAnswer(double Probability);

/// <summary>Parsed Choice answer — winning option, full distribution, and
/// the distribution concentration. Confidence summarizes the distribution,
/// not workflow correctness; several acceptable options can spread it.</summary>
public sealed record JevChoiceAnswer(
    string Choice,
    IReadOnlyDictionary<string, double> Probabilities,
    double Confidence);

/// <summary>Parsed Score answer — probability-weighted position across the
/// ordered levels (can land between levels), per-level probabilities, and
/// the distribution concentration. Weak in numerical calibration: threshold
/// with it, never interpolate an exact number from it.</summary>
public sealed record JevScoreAnswer(
    double Score,
    IReadOnlyDictionary<string, double> Probabilities,
    double Confidence);

/// <summary>Answer for one question id, tagged by kind so callers can switch.</summary>
public sealed record JevAnswer(
    JevQuestionKind Kind,
    JevNoulAnswer? Noul,
    JevChoiceAnswer? Choice,
    JevScoreAnswer? Score);

/// <summary>Outcome of a governed judgment call. Fail-soft by contract:
/// <see cref="JevCallStatus.Unavailable"/> means "carry on without the
/// judgment" — it must never surface as a learner-visible failure.</summary>
public enum JevCallStatus
{
    /// <summary>Master switch off or key missing — nothing was sent.</summary>
    Disabled = 0,

    /// <summary>Judgments returned.</summary>
    Ok = 1,

    /// <summary>Policy refused, lease blocked, provider error, or timeout.
    /// No judgment is available; the caller's fallback path applies.</summary>
    Unavailable = 2,
}

public sealed record JevJudgmentResult(
    JevCallStatus Status,
    string? Model,
    IReadOnlyDictionary<string, JevAnswer>? Answers,
    int InputTokens,
    int OutputTokens,
    string? Reason)
{
    public bool IsOk => Status == JevCallStatus.Ok;

    public static JevJudgmentResult Disabled(string reason) =>
        new(JevCallStatus.Disabled, null, null, 0, 0, reason);

    public static JevJudgmentResult Unavailable(string reason) =>
        new(JevCallStatus.Unavailable, null, null, 0, 0, reason);

    /// <summary>Convenience accessor for single-question calls; throws for
    /// non-Ok results, so gate on <see cref="IsOk"/> first.</summary>
    public JevAnswer this[string questionId] => Answers![questionId];
}

/// <summary>Governance metadata for one judgment call — who is asking, under
/// which feature code, and (optionally) which business row this judgment
/// belongs to. Pass a fresh <see cref="ResourceVersion"/> when deliberately
/// replaying an identical payload (e.g. calibration re-runs): the control
/// plane treats same-key operations as duplicates otherwise.</summary>
public sealed record JevCallMetadata
{
    public required string FeatureCode { get; init; }
    public string? UserId { get; init; }
    public string? ResourceId { get; init; }
    public string? ResourceType { get; init; }
    public int? ResourceVersion { get; init; }
}
