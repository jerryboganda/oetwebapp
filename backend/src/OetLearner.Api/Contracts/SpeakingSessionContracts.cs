namespace OetLearner.Api.Contracts;

// Phase 2 (B.3, D.1, F) of the OET Speaking module roadmap.
//
// Request/response shapes for the typed Speaking session lifecycle —
// surfaced under `/v1/speaking/sessions` and consumed by the frontend
// `lib/api/speaking-sessions.ts` client. The shapes mirror the
// learner-safe projection emitted by
// `LearnerService.SpeakingRolePlayCards.cs` (no interlocutor data leaks
// across the wire).

/// <summary>POST /v1/speaking/sessions body. Used by the learner UI to
/// start a typed Speaking session against a published role-play card.</summary>
public record CreateSpeakingSessionRequest(
    string RolePlayCardId,
    string Mode,
    string? MockSetId = null,
    string? BookingId = null,
    string? ConsentVersion = null);

/// <summary>Response from <c>POST /v1/speaking/sessions</c>. Carries the
/// learner-safe role-play card projection plus the precomputed prep/
/// role-play time windows so the client can drive the prep + countdown
/// timers without recomputing offsets.</summary>
public record CreateSpeakingSessionResponse(
    string SessionId,
    DateTimeOffset PrepStartedAt,
    DateTimeOffset PrepEndsAt,
    DateTimeOffset RolePlayEndsAt,
    string ConsentVersion,
    object Card);

/// <summary>Response from <c>GET /v1/speaking/sessions/{id}</c>. Returns
/// the current state of the session along with the same learner-safe
/// card projection used at create time.</summary>
public record SpeakingSessionDetail(
    string SessionId,
    string Mode,
    string State,
    string RolePlayCardId,
    DateTimeOffset? PrepStartedAt,
    DateTimeOffset? RolePlayStartedAt,
    DateTimeOffset? EndedAt,
    int ElapsedSeconds,
    string ConsentVersion,
    object Card);

/// <summary>One criterion in the AI assessment per-criterion drawer.
/// `Score`/`MaxScore` matches the canonical 0–6 linguistic / 0–3 clinical
/// rubric enforced by <see cref="OetLearner.Api.Services.OetScoring"/>.</summary>
public record CriterionScore(
    int Score,
    int MaxScore,
    string Rationale,
    string[] EvidenceQuotes);

/// <summary>Response from <c>POST /v1/speaking/sessions/{id}/ai-assess</c>
/// and <c>GET /v1/speaking/sessions/{id}/ai-assessment</c>. Always
/// advisory — the headline <c>EstimatedScaledScore</c> is recomputed
/// through <see cref="OetLearner.Api.Services.OetScoring.SpeakingProjectedScaled(OetLearner.Api.Services.OetScoring.SpeakingCriterionScores)"/>
/// so the 0–500 number is the single source of truth.</summary>
public record SpeakingAiAssessmentProjection(
    string AssessmentId,
    string Provider,
    string ModelId,
    string PromptTemplateId,
    IDictionary<string, CriterionScore> CriterionScores,
    int EstimatedScaledScore,
    string ReadinessBand,
    string OverallSummary,
    string ConfidenceBand,
    DateTimeOffset GeneratedAt,
    bool IsAdvisory);

/// <summary>POST /v1/speaking/sessions/{id}/consent body. The learner
/// confirms a specific consent version which the session and any
/// downstream <c>SpeakingRecording</c> rows are stamped with.</summary>
public record SpeakingConsentRequest(string ConsentVersion);
