namespace OetLearner.Api.Contracts;

// Phase 1 (B.1) of the OET Speaking module roadmap — admin-facing request
// and response shapes for the two-card role-play schema. See
// C:\Users\Dr Faisal Maqsood PC\.claude\plans\1-oet-speaking-module-quirky-kurzweil.md.
//
// The learner-facing projection of a `RolePlayCard` lives in
// `LearnerService.SpeakingRolePlayCards.cs` and MUST NEVER include any
// field from `InterlocutorScript`. These admin DTOs are the only place
// that exposes the hidden interlocutor card to a non-test caller.

// ── Role-play card requests ───────────────────────────────────────────────

/// <summary>Create the candidate-facing role-play card. Wraps a fresh
/// `ContentItem` row inside the same transaction. Status starts as Draft —
/// promote via the `/publish` endpoint after wiring an
/// `InterlocutorScript`.</summary>
public record AdminRolePlayCardCreateRequest(
    string ProfessionId,
    string ScenarioTitle,
    string Setting,
    string CandidateRole,
    string? InterlocutorRole,
    string? PatientName,
    string? PatientAge,
    string Background,
    string? Task1,
    string? Task2,
    string? Task3,
    string? Task4,
    string? Task5,
    bool? AllowedNotes,
    int? PrepTimeSeconds,
    int? RolePlayTimeSeconds,
    string PatientEmotion,
    string CommunicationGoal,
    string ClinicalTopic,
    string? Difficulty,
    string[]? CriteriaFocus,
    string? Disclaimer,
    bool? IsLiveTutorEligible);

/// <summary>Partial update for an existing role-play card. Every field is
/// optional — only supplied (non-null) fields are applied so admins can
/// patch a single task or background paragraph without re-supplying the
/// rest of the card.</summary>
public record AdminRolePlayCardUpdateRequest(
    string? ProfessionId = null,
    string? ScenarioTitle = null,
    string? Setting = null,
    string? CandidateRole = null,
    string? InterlocutorRole = null,
    string? PatientName = null,
    string? PatientAge = null,
    string? Background = null,
    string? Task1 = null,
    string? Task2 = null,
    string? Task3 = null,
    string? Task4 = null,
    string? Task5 = null,
    bool? AllowedNotes = null,
    int? PrepTimeSeconds = null,
    int? RolePlayTimeSeconds = null,
    string? PatientEmotion = null,
    string? CommunicationGoal = null,
    string? ClinicalTopic = null,
    string? Difficulty = null,
    string[]? CriteriaFocus = null,
    string? Disclaimer = null,
    bool? IsLiveTutorEligible = null);

// ── Interlocutor script request ──────────────────────────────────────────

/// <summary>Create or update the hidden interlocutor script for a given
/// role-play card (1:1 by `RolePlayCardId`). The endpoint behaves as
/// upsert: if no script exists for the card, a new row is created;
/// otherwise the existing row is updated in-place. ResistanceLevel is
/// supplied as the string code "low"|"medium"|"high" via
/// <see cref="OetLearner.Api.Domain.ResistanceLevels.Parse"/>.</summary>
public record AdminInterlocutorScriptUpsertRequest(
    string OpeningResponse,
    string? Prompt1 = null,
    string? Prompt2 = null,
    string? Prompt3 = null,
    string? HiddenInformation = null,
    string? ResistanceLevel = null,
    string? ClosingCue = null,
    string? EmotionalState = null,
    string? ProfessionRoleNotes = null,
    string[]? LayLanguageTriggers = null);

// ── Response shapes ──────────────────────────────────────────────────────

/// <summary>Compact row used by the admin list endpoint.</summary>
public record AdminRolePlayCardSummary(
    string CardId,
    string ContentItemId,
    string ProfessionId,
    string ScenarioTitle,
    string Setting,
    string CandidateRole,
    string InterlocutorRole,
    string Difficulty,
    string Status,
    bool HasInterlocutorScript,
    bool IsLiveTutorEligible,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>Full admin projection of a role-play card. The
/// <see cref="InterlocutorScript"/> property surfaces the hidden card
/// alongside — admins/tutors only. Never returned by learner endpoints.</summary>
public record AdminRolePlayCardDetail(
    string CardId,
    string ContentItemId,
    string ProfessionId,
    string ScenarioTitle,
    string Setting,
    string CandidateRole,
    string InterlocutorRole,
    string? PatientName,
    string? PatientAge,
    string Background,
    string?[] Tasks,
    bool AllowedNotes,
    int PrepTimeSeconds,
    int RolePlayTimeSeconds,
    string PatientEmotion,
    string CommunicationGoal,
    string ClinicalTopic,
    string Difficulty,
    string[] CriteriaFocus,
    string Disclaimer,
    string Status,
    bool IsLiveTutorEligible,
    string? CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt,
    AdminInterlocutorScriptDetail? InterlocutorScript);

/// <summary>Admin projection of the hidden interlocutor card. Holds every
/// field that drives the AI patient persona and the tutor cue panel.</summary>
public record AdminInterlocutorScriptDetail(
    string ScriptId,
    string RolePlayCardId,
    string OpeningResponse,
    string? Prompt1,
    string? Prompt2,
    string? Prompt3,
    string HiddenInformation,
    string ResistanceLevel,
    string ClosingCue,
    string EmotionalState,
    string? ProfessionRoleNotes,
    string[] LayLanguageTriggers,
    string? CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
