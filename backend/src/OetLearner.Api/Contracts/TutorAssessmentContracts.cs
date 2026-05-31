using System.Text.Json;

namespace OetLearner.Api.Contracts;

// Phase 4 (B.4) of the OET Speaking module roadmap.
//
// Wire-format records for the tutor assessment surface and the dual
// scoring projection. Both AI and Tutor assessments share the same nine
// criterion fields but are persisted as strictly separate entities
// (`SpeakingAiAssessment` vs `SpeakingTutorAssessment`) — the contracts
// below enforce that separation at the API boundary so the learner UI
// renders both columns side-by-side without ever co-mingling the data.

/// <summary>
/// POST /v1/expert/speaking/sessions/{id}/tutor-assessment body. All nine
/// criterion scores are optional at draft time so a tutor may save a
/// half-finished review.
/// </summary>
public sealed record TutorAssessmentDraftRequest(
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
    string[]? RecommendedDrills,
    string[]? RecommendedRulebookEntries);

/// <summary>
/// POST /v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}/submit
/// body. All nine scores are REQUIRED at submit time and validated against
/// the linguistic (0–6) and clinical (0–3) score ranges before
/// <c>IsFinal</c> is flipped.
/// </summary>
public sealed record TutorAssessmentSubmitRequest(
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
    string[]? RecommendedDrills,
    string[]? RecommendedRulebookEntries);

/// <summary>Tutor assessment projection returned by the learner-facing
/// dual GET. Surfaces the full rubric, the projected scaled score, and
/// any narrative feedback. <c>IsFinal</c> tells the UI whether to render
/// the score or keep it behind a "review in progress" banner.</summary>
public sealed record TutorAssessmentProjection(
    string AssessmentId,
    string TutorId,
    string? TutorName,
    int Intelligibility,
    int Fluency,
    int Appropriateness,
    int GrammarExpression,
    int RelationshipBuilding,
    int PatientPerspective,
    int Structure,
    int InformationGathering,
    int InformationGiving,
    int EstimatedScaledScore,
    string ReadinessBand,
    string? OverallFeedbackMarkdown,
    string[] Strengths,
    string[] Improvements,
    string[] RecommendedDrills,
    bool IsFinal,
    DateTimeOffset? SubmittedAt);

/// <summary>AI assessment projection in the dual response. Same shape
/// the AI scorer produces (Phase 2) so the learner UI can render the two
/// columns from a single payload. The endpoint never mutates this row.</summary>
public sealed record AiAssessmentProjection(
    string AssessmentId,
    string Provider,
    string ModelId,
    int Intelligibility,
    int Fluency,
    int Appropriateness,
    int GrammarExpression,
    int RelationshipBuilding,
    int PatientPerspective,
    int Structure,
    int InformationGathering,
    int InformationGiving,
    int EstimatedScaledScore,
    string ReadinessBand,
    string OverallSummary,
    string ConfidenceBand,
    DateTimeOffset GeneratedAt);

/// <summary>GET /v1/speaking/sessions/{id}/assessments response. Carries
/// both assessment tracks plus the divergence metric. Either side may be
/// null if that track has not yet produced a row.</summary>
public sealed record DualAssessmentResponse(
    string SessionId,
    AiAssessmentProjection? Ai,
    TutorAssessmentProjection? Tutor,
    TutorAssessmentProjection[] TutorHistory,
    DivergenceMetric? Divergence);

/// <summary>Per-criterion signed deltas between the tutor's final score and
/// the AI score, plus the scaled-score delta and an agreement band computed
/// from absolute difference magnitude:
/// <c>close</c> (sum-of-abs ≤ 4), <c>moderate</c> (≤ 10), else
/// <c>wide</c>. Drives the calibration banner in the learner UI.</summary>
public sealed record DivergenceMetric(
    IDictionary<string, int> PerCriterion,
    int ScaledDelta,
    string AgreementBand);

/// <summary>POST /v1/expert/speaking/sessions/{id}/comments body. The
/// tutor anchors a comment to a specific transcript segment + audio
/// timestamp window.</summary>
public sealed record TutorTimestampedCommentRequest(
    int TranscriptSegmentIndex,
    int StartMs,
    int EndMs,
    string CriterionCode,
    string Severity,
    string BodyMarkdown,
    string? LinkedRulebookEntryCode,
    string? LinkedDrillId);

public sealed record SpeakingTranscriptContextPayload(
    JsonElement[] Segments);

public sealed record SpeakingTimestampedCommentPayload(
    string CommentId,
    int SegmentStartMs,
    int SegmentEndMs,
    int TranscriptSegmentIndex,
    string Criterion,
    string Severity,
    string Body,
    string AuthorRole,
    DateTimeOffset CreatedAt);

public sealed record TutorSessionContextPayload(
    string? RecordingUrl,
    SpeakingTranscriptContextPayload Transcript,
    SpeakingTimestampedCommentPayload[] Comments);

/// <summary>One row in the tutor review queue listing returned by
/// GET /v1/expert/speaking/queue.</summary>
public sealed record TutorReviewQueueItem(
    string SessionId,
    string UserId,
    string LearnerDisplayName,
    string RolePlayCardId,
    string CardId,
    string? ScenarioTitle,
    string CardTitle,
    string ProfessionId,
    DateTimeOffset? EndedAt,
    int ElapsedSeconds,
    int DurationSeconds,
    string? AiReadinessBand,
    int? AiScaledScore,
    bool HasDraft,
    bool ClaimedByMe,
    bool ClaimedBySomeoneElse,
    DateTimeOffset? ClaimExpiresAt);
