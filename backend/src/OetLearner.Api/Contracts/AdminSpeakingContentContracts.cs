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
    bool? IsLiveTutorEligible,
    // Speaking module rebuild (2026-06-11). Hidden card type + printed card no.
    string? CardTypeId = null,
    int? DisplayCardNumber = null);

/// <summary>Bulk lifecycle action over a set of role-play cards. <c>Action</c>
/// is one of <c>publish</c> | <c>archive</c> (duplicate stays a per-row action
/// and is intentionally NOT bulk-able). The whole batch runs inside ONE
/// transaction with ONE audit row; per-card publish-gate failures land in the
/// result's <c>Failed</c>/<c>Errors</c> rather than aborting the batch.</summary>
public sealed record RolePlayCardBulkRequest(string Action, string[] Ids);

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
    bool? IsLiveTutorEligible = null,
    // Speaking module rebuild (2026-06-11). Hidden card type + printed card no.
    // Sentinel "" on CardTypeId clears the type; null leaves it unchanged.
    string? CardTypeId = null,
    int? DisplayCardNumber = null);

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
    string[]? LayLanguageTriggers = null,
    // Speaking module rebuild (2026-06-11). The printed ROLEPLAYER (patient)
    // card face — never shown to students.
    string? PatientBackground = null,
    string? PatientTask1 = null,
    string? PatientTask2 = null,
    string? PatientTask3 = null,
    string? PatientTask4 = null,
    string? PatientTask5 = null);

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
    DateTimeOffset? ArchivedAt,
    // Speaking module rebuild (2026-06-11). Hidden card type (admin/tutor only).
    string? CardTypeId = null,
    string? CardTypeName = null);

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
    AdminInterlocutorScriptDetail? InterlocutorScript,
    // Speaking module rebuild (2026-06-11). Hidden card type metadata (admin/
    // tutor only) + printed card number.
    string? CardTypeId = null,
    string? CardTypeName = null,
    int? DisplayCardNumber = null);

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
    DateTimeOffset UpdatedAt,
    // Speaking module rebuild (2026-06-11). The printed roleplayer card face.
    string PatientBackground = "",
    string?[]? PatientTasks = null);

// ── Card types (2026-06-11 rebuild) — fully admin-configurable taxonomy ─────

/// <summary>Create/update a hidden speaking card type. Students never see it.</summary>
public record AdminSpeakingCardTypeUpsertRequest(
    string Name,
    string? Description = null,
    int? SortOrder = null,
    bool? IsActive = null);

/// <summary>Admin projection of a card type.</summary>
public record AdminSpeakingCardTypeDetail(
    string Id,
    string Name,
    string Description,
    int SortOrder,
    bool IsActive,
    int CardCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── Phase 11 (G.11) — AI-assisted authoring ────────────────────────────────

/// <summary>Single-shot AI draft request for a candidate card + paired
/// interlocutor script. The admin fills in profession + topic + emotion +
/// difficulty; the backend builds a grounded prompt and asks the gateway
/// for one structured-JSON response containing both the candidate card and
/// the interlocutor script. The resulting card is persisted with
/// <c>Status = Draft</c> and an admin reviews + edits before publishing.</summary>
public record AdminRolePlayCardAiDraftRequest(
    string ProfessionId,
    string? Topic,
    string? Emotion,
    string? Difficulty,
    string? Setting,
    string? CandidateRole,
    string? InterlocutorRole,
    string? CommunicationGoal,
    // WS9 (SPK-007) — OCR/text extracted from an imported source PDF. When
    // present, the grounded prompt receives it as reference material so the
    // draft is structured from the admin's own source paper rather than fully
    // synthesised. Never copied verbatim (originality guard still applies).
    string? SourceMaterial = null);

/// <summary>Response from a single-shot AI draft. Contains the persisted
/// draft card id so the admin UI can deep-link to the editor, plus the
/// projected detail (card + script) for an inline preview.</summary>
public record AdminRolePlayCardAiDraftResponse(
    string CardId,
    AdminRolePlayCardDetail Card,
    string? Warning);

/// <summary>Enqueue a batch generation job. The background worker picks
/// Pending rows on a 60-second tick and produces drafts at low
/// concurrency.</summary>
public record AdminRolePlayCardBatchRequest(
    string ProfessionId,
    int Count,
    string[]? TopicList,
    DifficultyBucket[]? DifficultyDistribution,
    string? IdempotencyKey);

/// <summary>Maps a difficulty code to the number of cards to draft at
/// that difficulty in the batch.</summary>
public record DifficultyBucket(string Difficulty, int Count);

/// <summary>Compact projection of a batch request row for the admin
/// queue.</summary>
public record AdminRolePlayCardBatchSummary(
    string BatchId,
    string ProfessionId,
    int Count,
    int GeneratedCount,
    string Status,
    string RequestedByAdminId,
    string? RequestedByAdminName,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Single-shot AI draft request for a micro-drill.</summary>
public record AdminSpeakingDrillAiDraftRequest(
    string DrillKind,
    string? ProfessionId,
    string? Topic,
    string? CriterionFocus,
    string? Difficulty);

/// <summary>Response from a single-shot drill draft. Contains the drillId
/// + a flat projection of the persisted draft.</summary>
public record AdminSpeakingDrillAiDraftResponse(
    string DrillId,
    string DrillKind,
    string? ProfessionId,
    string Title,
    string InstructionText,
    string[] TargetCriteria,
    int? RecommendedAfterSessionScoreBelow,
    string Status,
    DateTimeOffset CreatedAt,
    string? Warning);

/// <summary>Auto-pair two published role-play cards from the same
/// profession into a draft mock set. The service picks two cards with
/// complementary criteria focus (one info-giving heavy, one
/// info-gathering heavy where possible).</summary>
public record AdminSpeakingMockSetAutoPairRequest(
    string ProfessionId,
    string? Difficulty,
    string? Title);

/// <summary>Response from an auto-pair. Returns the new mock set id +
/// the two role-play content ids that were chosen.</summary>
public record AdminSpeakingMockSetAutoPairResponse(
    string MockSetId,
    string Title,
    string ProfessionId,
    string RolePlay1ContentId,
    string RolePlay1Title,
    string RolePlay2ContentId,
    string RolePlay2Title,
    string? Warning);

// ── WS9 (SPK-007) — scanned/text PDF import → structured draft ──────────────

/// <summary>One field-presence check in the import builder-validation report.
/// <c>Required</c> checks that fail are publish blockers; advisory checks are
/// surfaced as warnings only.</summary>
public record SpeakingImportFieldCheck(
    string Field,
    bool Detected,
    bool Required,
    string? Note);

/// <summary>Builder-validation report produced after extracting text from an
/// imported source PDF. <c>IsPublishable</c> is true only when every required
/// field was detected in the extracted text. Mirrors the publish-gate intent
/// so an admin sees the same blockers before a draft is even created.</summary>
public record SpeakingImportValidationReport(
    bool IsPublishable,
    IReadOnlyList<SpeakingImportFieldCheck> Checks,
    IReadOnlyList<string> Blockers);

/// <summary>Result of importing a source PDF. The source asset is always
/// persisted (provenance) even when extraction yields no text (scanned PDF
/// with no OCR provider configured) so the admin can attach it for manual
/// structuring. <c>DraftCardId</c> is set only when <c>autoDraft</c> was
/// requested and extraction produced usable text.</summary>
public record SpeakingContentImportResult(
    string SourceAssetKey,
    long SourceBytes,
    int ExtractedChars,
    bool LikelyScanned,
    SpeakingImportValidationReport Validation,
    string? DraftCardId,
    AdminRolePlayCardDetail? Draft,
    string? Warning,
    string? SourceMediaId = null);
