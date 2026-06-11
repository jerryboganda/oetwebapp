namespace OetLearner.Api.Contracts;

// Speaking module rebuild (2026-06-11 spec).
//
// Request/response shapes for the two-card Speaking exam (Intro → Card A →
// Card B). Surfaced under `/v1/speaking/exams` and consumed by the frontend
// `lib/api/speaking-exams.ts` client.
//
// MISSION CRITICAL: the learner projection NEVER serialises the roleplayer
// (patient) card, the hidden card type, or any InterlocutorScript field.

/// <summary>POST /v1/speaking/exams body. `Mode` is "ai" or "live_tutor".
/// Provide either a curated `MockSetId` (a published two-card pair) or a
/// `ProfessionId` (the server picks a random published pair for that
/// profession). `BookingId` is required for live-tutor exams.</summary>
public record CreateSpeakingExamRequest(
    string Mode,
    string? MockSetId = null,
    string? ProfessionId = null,
    string? BookingId = null);

/// <summary>POST /v1/speaking/exams/{id}/start-card has no body; the server
/// starts whichever card's discussion phase is current.</summary>

/// <summary>Phase clock for the current exam stage. Computed entirely
/// server-side from persisted timestamps so a reconnecting/clock-skewed
/// client cannot gain or lose time.</summary>
public record SpeakingExamClock(
    string Stage,
    DateTimeOffset ServerNow,
    DateTimeOffset? StageStartedAt,
    DateTimeOffset? StageEndsAt,
    int? SecondsRemaining,
    bool Expired);

/// <summary>Response from <c>GET /v1/speaking/exams/{id}</c> and the
/// transition endpoints. The learner-safe candidate card is included only for
/// the prep/active phases of the current card; intro and completed phases
/// carry a null card. `CurrentSessionId` is the child SpeakingSession the
/// frontend hands to ConversationHub for the AI patient.</summary>
public record SpeakingExamDetail(
    string ExamId,
    string Mode,
    string State,
    string ProfessionId,
    int CurrentCardNumber,
    string? CurrentSessionId,
    object? CurrentCard,
    SpeakingExamClock Clock,
    DateTimeOffset? CompletedAt);

/// <summary>One card's result inside the exam report.</summary>
public record SpeakingExamCardResult(
    int CardNumber,
    string SessionId,
    string Status,                 // "scored" | "pending" | "awaiting_tutor"
    SpeakingAiAssessmentProjection? Assessment);

/// <summary>Response from <c>GET /v1/speaking/exams/{id}/results</c>.</summary>
public record SpeakingExamResults(
    string ExamId,
    string Mode,
    string State,
    string OverallStatus,          // "scored" | "pending" | "awaiting_tutor"
    int? CombinedScaledScore,
    string? ReadinessBand,
    IReadOnlyList<SpeakingExamCardResult> Cards);

// ── Live-tutor exam (2026-06-11 rebuild) — TUTOR-ONLY views ─────────────────
// The roleplayer (patient) card the human tutor plays. NEVER returned by any
// learner endpoint — only the tutor/expert exam view below.

public record SpeakingExamRoleplayerCard(
    int CardNumber,
    string Setting,
    string InterlocutorRole,
    string? PatientName,
    string? PatientAge,
    string PatientBackground,
    string[] PatientTasks,
    int? DisplayCardNumber,
    string? CardTypeName);

/// <summary>Tutor-only view of a live-tutor exam: both roleplayer cards + the
/// current server-authoritative phase so the human can role-play and follow the
/// timed structure. Returned only on the expert route.</summary>
public record SpeakingExamTutorView(
    string ExamId,
    string Mode,
    string State,
    int CurrentCardNumber,
    string ProfessionId,
    string? BookingId,
    SpeakingExamClock Clock,
    IReadOnlyList<SpeakingExamRoleplayerCard> Cards);
