namespace OetLearner.Api.Contracts;

// OET Speaking module — double-marking + senior moderation wire format
// (Developer Implementation Notes §15.4 / §15.5).
//
// The nine criterion scores reuse the same linguistic (0–6) + clinical (0–3)
// shape as the tutor assessment surface so a marker UI can submit the exact
// same payload it already collects.

/// <summary>The nine OET speaking criterion scores carried across the
/// moderation surface (second-marker submit + moderator finalize).</summary>
public sealed record SpeakingCriterionScorePayload(
    int Intelligibility,
    int Fluency,
    int Appropriateness,
    int GrammarExpression,
    int RelationshipBuilding,
    int PatientPerspective,
    int Structure,
    int InformationGathering,
    int InformationGiving)
{
    public int EstimatedScaledScore { get; init; }
}

/// <summary>POST /v1/expert/speaking/sessions/{id}/moderation/open body.
/// Opens (or returns the existing) moderation case for a finished session
/// whose primary tutor assessment is already final.</summary>
public sealed record OpenSpeakingModerationRequest(string? Reason);

/// <summary>POST /v1/expert/speaking/sessions/{id}/moderation/second-mark body.
/// The second independent assessor's nine scores plus narrative feedback.</summary>
public sealed record SpeakingSecondMarkRequest(
    int? Intelligibility,
    int? Fluency,
    int? Appropriateness,
    int? GrammarExpression,
    int? RelationshipBuilding,
    int? PatientPerspective,
    int? Structure,
    int? InformationGathering,
    int? InformationGiving,
    string? OverallFeedbackMarkdown,
    string[]? Strengths,
    string[]? Improvements,
    string[]? RecommendedDrills);

/// <summary>POST /v1/expert/speaking/sessions/{id}/moderation/finalize body.
/// The senior moderator's reconciled final nine scores, a decision note, and
/// an optional request for the learner to reattempt instead of releasing a
/// score.</summary>
public sealed record SpeakingModerationFinalizeRequest(
    int? Intelligibility,
    int? Fluency,
    int? Appropriateness,
    int? GrammarExpression,
    int? RelationshipBuilding,
    int? PatientPerspective,
    int? Structure,
    int? InformationGathering,
    int? InformationGiving,
    string? OverallFeedbackMarkdown,
    string? DecisionNote,
    bool RequestReattempt);

/// <summary>Projection of a <c>SpeakingModerationCase</c> returned by the
/// moderation queue + case-detail endpoints.</summary>
public sealed record SpeakingModerationCaseProjection(
    string Id,
    string SessionId,
    string Reason,
    string Status,
    string? FirstMarkerId,
    string? FirstAssessmentId,
    SpeakingCriterionScorePayload? FirstScore,
    string? SecondMarkerId,
    string? SecondAssessmentId,
    SpeakingCriterionScorePayload? SecondScore,
    string? ModeratorId,
    string? FinalAssessmentId,
    SpeakingCriterionScorePayload? FinalScore,
    int? VariancePoints,
    string? VarianceReason,
    string? FinalDecisionNote,
    bool RequestReattempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One row in the moderator queue listing returned by
/// GET /v1/expert/speaking/moderation/queue.</summary>
public sealed record SpeakingModerationQueueItem(
    string CaseId,
    string SessionId,
    string ProfessionId,
    string Reason,
    string Status,
    int? VariancePoints,
    bool NeedsSecondMark,
    bool NeedsModeration,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
