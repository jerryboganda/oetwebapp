namespace OetWithDrHesham.Api.Contracts;

// Phase 5 (G) of the OET Speaking module roadmap.
//
// Request/response shapes for the Speaking Drill bank — small,
// single-focus practice items (Opening, Empathy, Ice, OpenQuestion,
// LayLanguage, Signposting, CheckingUnderstanding, Reassurance,
// Closing, Pronunciation, Fluency, Grammar).
//
// Admin authoring uses `AdminDrillCreateRequest`/`AdminDrillUpdateRequest`
// to create the underlying `ContentItem` and `SpeakingDrillItem` rows
// in the same transaction (mirrors `AdminService.SpeakingMockSets.cs`).
//
// Learners consume `DrillSummary`/`DrillAttemptDetail`/`DrillScoringResponse`
// via `SpeakingDrillService`.

// ── Admin requests ───────────────────────────────────────────────────────

/// <summary>Create a new speaking drill. Wraps a fresh `ContentItem`
/// (ContentType="speaking_drill", SubtestCode="speaking", Status=Draft)
/// inside the same transaction.</summary>
public record AdminDrillCreateRequest(
    string DrillKind,
    string? ProfessionId,
    string Title,
    string InstructionText,
    string[] TargetCriteria,
    int? RecommendedAfterSessionScoreBelow);

/// <summary>Partial update of an existing drill. Every field is
/// optional — only supplied (non-null) fields are applied so admins can
/// tweak the prompt without re-supplying the rest of the drill.</summary>
public record AdminDrillUpdateRequest(
    string? DrillKind = null,
    string? ProfessionId = null,
    string? Title = null,
    string? InstructionText = null,
    string[]? TargetCriteria = null,
    int? RecommendedAfterSessionScoreBelow = null);

/// <summary>Atomic bulk action over a set of speaking drills.
/// <c>Action</c> is one of <c>publish</c> | <c>archive</c> | <c>delete</c>.
/// All matching per-item operations run inside a single transaction with
/// exactly one audit entry (mirrors the vocabulary bulk surface).</summary>
public sealed record SpeakingDrillBulkRequest(string Action, string[] Ids);

// ── Learner-facing response shapes ───────────────────────────────────────

/// <summary>Compact drill record for the learner-facing list. Includes
/// per-learner state (has the learner attempted this drill before, and
/// their best score). Driven by the join between `SpeakingDrillItem` /
/// `ContentItem` / `SpeakingDrillAttempt`.</summary>
public record DrillSummary(
    string DrillId,
    string DrillKind,
    string Title,
    string InstructionText,
    string[] TargetCriteria,
    bool HasAttempted,
    int? BestScore);

/// <summary>Full detail of one attempt. Returned by Start + Score
/// endpoints so the player UI can render progress without extra
/// round-trips.</summary>
public record DrillAttemptDetail(
    string AttemptId,
    string DrillId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? Score,
    string? FeedbackSummary);

/// <summary>AI feedback payload returned after `/score` completes.
/// Mirrors `SpeakingDrillAttempt.AiFeedbackJson` and is the shape the
/// `<DrillPlayer />` renders to the learner.</summary>
public record DrillScoringResponse(
    string AttemptId,
    int Score,
    string Summary,
    string[] SpecificComments,
    string[] NextRecommendations);
