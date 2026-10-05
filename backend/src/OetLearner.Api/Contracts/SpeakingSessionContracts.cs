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
/// timers without recomputing offsets. <c>IsFreeSample</c>: the server bound
/// this session as a use of the learner's free sample (0 AI credits).</summary>
public record CreateSpeakingSessionResponse(
    string SessionId,
    DateTimeOffset PrepStartedAt,
    DateTimeOffset PrepEndsAt,
    DateTimeOffset RolePlayEndsAt,
    string ConsentVersion,
    object Card,
    bool IsFreeSample = false,
    bool ConsentAccepted = false,
    bool LiveVoiceAvailable = false);

/// <summary>Response from <c>GET /v1/speaking/sessions/{id}</c>. Returns
/// the current state of the session along with the same learner-safe
/// card projection used at create time. The warm-up timestamps were
/// added in Phase 3 so the frontend state-router can tell warmup from
/// prep without a separate API call. <c>RolePlayEndsAt</c> is the server's
/// deadline for the role-play (start plus the card's capped time), null until
/// the role-play has started; clients should count down to it rather than to
/// the raw card seconds. <c>Admission</c> is non-null only while the live AI session cap is full
/// and this practice card is waiting in the line (state stays <c>warmup</c>; nothing is timed and no
/// credit is held); see <see cref="SpeakingLiveAdmissionView"/>.</summary>
public record SpeakingSessionDetail(
    string SessionId,
    string Mode,
    string State,
    string RolePlayCardId,
    DateTimeOffset? WarmupStartedAt,
    DateTimeOffset? WarmupEndedAt,
    DateTimeOffset? PrepStartedAt,
    DateTimeOffset? RolePlayStartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? SubmittedAt,
    int ElapsedSeconds,
    string ConsentVersion,
    object Card,
    string? FeedbackMessage = null,
    bool IsFreeSample = false,
    bool ConsentAccepted = false,
    bool LiveVoiceAvailable = false,
    DateTimeOffset? RolePlayEndsAt = null,
    SpeakingLiveAdmissionView? Admission = null);

/// <summary>One criterion in the AI assessment per-criterion drawer.
/// `Score`/`MaxScore` matches the canonical 0–6 linguistic / 0–3 clinical
/// rubric enforced by <see cref="OetLearner.Api.Services.OetScoring"/>.</summary>
public record CriterionScore(
    int Score,
    int MaxScore,
    string Rationale,
    string[] EvidenceQuotes);

/// <summary>One strength or priority weakness in the candidate's report. <c>Criterion</c> is one of the nine
/// criterion codes (or <c>overall</c>); <c>Quote</c> is the candidate's own words when the grader found some;
/// <c>Action</c> is the one concrete thing to do differently next attempt (weaknesses only).</summary>
public sealed record SpeakingFeedbackItem(string Criterion, string Text, string? Quote, string? Action);

/// <summary>One personalised practice drill built from this attempt: what the candidate did, what to rehearse,
/// and a short example phrase or question to use.</summary>
public sealed record SpeakingReportDrill(string Title, string Criterion, string WeakPoint, string Practise, string? Example);

/// <summary>The coaching half of an AI Speaking result (owner spec 4 Oct 2026, sections 4 and 10): 2–4
/// strengths, 2–5 priority weaknesses that each end in an action, and personalised drills. Plain language —
/// internal rule IDs are scrubbed before it reaches a candidate.</summary>
public sealed record SpeakingFeedbackReport(
    IReadOnlyList<SpeakingFeedbackItem> Strengths,
    IReadOnlyList<SpeakingFeedbackItem> PriorityWeaknesses,
    IReadOnlyList<SpeakingReportDrill> Drills);

/// <summary>One thing the audio judge heard that affected Intelligibility: which clip, about which second, what.</summary>
public sealed record SpeakingAudioObservation(int Clip, int ApproxSecond, string Issue, string? Example);

/// <summary>What the candidate's Intelligibility score was judged from (owner spec 4 Oct 2026). <c>Source</c> is
/// <c>audio</c> (judged from the recording) or <c>transcript_only</c> (estimated from the transcript: limited
/// evidence, stated plainly). <c>ReasonText</c> says in plain words why there was no audio evidence.</summary>
public sealed record SpeakingIntelligibilityEvidence(
    string Source,
    string? Reason,
    string? ReasonText,
    string Confidence,
    IReadOnlyList<SpeakingAudioObservation> Observations);

/// <summary>Response from <c>POST /v1/speaking/sessions/{id}/ai-assess</c>
/// and <c>GET /v1/speaking/sessions/{id}/ai-assessment</c>. Always
/// advisory — the headline <c>EstimatedScaledScore</c> is the reported score
/// (<see cref="OetLearner.Api.Services.OetScoring.SpeakingReportedScaled(OetLearner.Api.Services.OetScoring.SpeakingCriterionScores)"/>:
/// 0–500 in steps of 10), the single number the grade, readiness band and pass line derive from.
/// <c>Grade</c> is the OET letter for that number; <c>ScoreLabel</c> is <c>provisional</c> until the
/// grader has been calibrated against expert-labelled performances, then <c>ai_practice_estimate</c>.</summary>
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
    bool IsAdvisory,
    string? Grade = null,
    string? ScoreLabel = null,
    SpeakingFeedbackReport? Report = null,
    SpeakingIntelligibilityEvidence? IntelligibilityEvidence = null);

/// <summary>POST /v1/speaking/sessions/{id}/consent body. The learner
/// confirms a specific consent version which the session and any
/// downstream <c>SpeakingRecording</c> rows are stamped with.</summary>
public record SpeakingConsentRequest(string ConsentVersion);

/// <summary>Response from <c>GET /v1/speaking/sessions/{id}/clock</c> (WS1,
/// §1.2/§13.3/§22.5). The single authoritative source of session timing —
/// computed entirely server-side from persisted timestamps plus the card's
/// prep/role-play windows, so a reconnecting or clock-skewed client cannot
/// gain or lose time. The hub's <c>TimeNearlyUp</c>/<c>TimeUp</c> broadcasts
/// are convenience signals only; this endpoint is authoritative.
/// <c>HardStopAt</c> is when the server force-ends an unfinished role-play
/// (deadline plus grace); null unless the session is active.</summary>
public record SpeakingSessionClock(
    string Stage,
    int RoleplayIndex,
    DateTimeOffset ServerNow,
    DateTimeOffset? StageStartedAt,
    DateTimeOffset? StageEndsAt,
    int? SecondsRemaining,
    bool Expired,
    string[] CanAdvanceTo,
    DateTimeOffset? HardStopAt = null);

/// <summary>POST /v1/speaking/sessions/{id}/technical-issue body (§22.5).</summary>
public record SpeakingTechnicalIssueRequest(string? Note);
