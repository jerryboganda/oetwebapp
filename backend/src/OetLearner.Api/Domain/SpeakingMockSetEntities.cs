using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md.
//
// A `SpeakingMockSet` is the curatorial pairing of two `ContentItem` rows
// (both with `SubtestCode = "speaking"`) that a learner attempts back to
// back as a single mock — matching the §1 "two role-plays" requirement of
// the OET Speaking specification.
//
// Each learner attempt at a mock set is captured by a `SpeakingMockSession`
// which links the two underlying `Attempt` rows. Combined criterion
// averages and a single readiness band are projected at session level via
// `SpeakingMockSetService.GetMockSessionAsync`.
//
// Free-tier learners are capped at `FreeTierConfig.MaxSpeakingMockSets`
// distinct sessions per rolling 7 days (Q1 of decisions §6 — locked).
public enum SpeakingMockSetStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public enum SpeakingMockSessionState
{
    InProgress = 0,
    Completed = 1,
    Abandoned = 2,
}

public class SpeakingMockSet
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = "nursing";

    [MaxLength(160)]
    public string Title { get; set; } = default!;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RolePlay1ContentId { get; set; } = default!;

    [MaxLength(64)]
    public string RolePlay2ContentId { get; set; } = default!;

    public SpeakingMockSetStatus Status { get; set; } = SpeakingMockSetStatus.Draft;

    [MaxLength(16)]
    public string Difficulty { get; set; } = "core"; // core | extension | exam

    /// <summary>
    /// Optional comma-separated criterion codes the curator wants this set
    /// to stress (e.g. "informationGiving,relationshipBuilding"). Surfaced
    /// in the orchestrator UI but does NOT short-circuit AI grading.
    /// </summary>
    [MaxLength(256)]
    public string CriteriaFocus { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Tags { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class SpeakingMockSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockSetId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Generic `Attempt` IDs created up-front by `StartMockSetAsync` — one
    /// per role-play. Both share `Attempt.ComparisonGroupId == this.Id` so
    /// downstream analytics can group them without a join.
    /// </summary>
    [MaxLength(64)]
    public string Attempt1Id { get; set; } = default!;

    [MaxLength(64)]
    public string Attempt2Id { get; set; } = default!;

    [MaxLength(16)]
    public string Mode { get; set; } = "exam"; // exam | self

    public SpeakingMockSessionState State { get; set; } = SpeakingMockSessionState.InProgress;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Snapshot of combined readiness band code at completion. Allows
    /// historical browsing without recomputing if either underlying
    /// evaluation is later mutated by an expert.
    /// </summary>
    [MaxLength(32)]
    public string? ReadinessBandSnapshot { get; set; }

    public int? CombinedScaledSnapshot { get; set; }
}
