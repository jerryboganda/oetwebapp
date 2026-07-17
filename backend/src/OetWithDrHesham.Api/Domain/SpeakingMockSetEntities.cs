using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

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

// P5 of the OET Speaking sequential plan. The legacy `State` enum above is
// coarse-grained (only "in progress" / "completed" / "abandoned"); the
// orchestrator UI needs a strict step-by-step state machine that mirrors
// the real OET sequence Prep1 -> RP1 -> Bridge -> Prep2 -> RP2 -> Results.
//
// Stored as a string column on `SpeakingMockSession.OrchestratorState` so we
// can extend it in future without an enum migration.
public static class SpeakingMockOrchestratorStates
{
    public const string Prep1       = "prep1";
    public const string Active1     = "active1";
    public const string Finished1   = "finished1";
    /// <summary>Short interlocutor handoff between the two role-plays. Started by
    /// <c>POST .../bridge/start</c>, ended by <c>POST .../bridge/finish</c>.</summary>
    public const string Bridge      = "bridge";
    public const string Prep2       = "prep2";
    public const string Active2     = "active2";
    public const string Finished2   = "finished2";
    public const string Aggregated  = "aggregated";

    /// <summary>Canonical ordering for the state machine, used by transition
    /// guards. Index of the current state must equal index of the target - 1
    /// for any forward move (Bridge is the only "rest" stop with no audio).</summary>
    public static readonly string[] Ordered = new[]
    {
        Prep1, Active1, Finished1, Bridge, Prep2, Active2, Finished2, Aggregated,
    };
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

    /// <summary>
    /// Granular orchestrator state (P5). One of
    /// <see cref="SpeakingMockOrchestratorStates"/>. Drives the frontend
    /// orchestrator at <c>app/speaking/mocks/[id]/page.tsx</c>: the page reads
    /// this value on every request and redirects the learner to the matching
    /// sub-route (RP1, bridge, RP2, results).
    /// </summary>
    [MaxLength(16)]
    public string OrchestratorState { get; set; } = SpeakingMockOrchestratorStates.Prep1;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When the bridge between role-play 1 and role-play 2 was entered. Used
    /// by analytics to measure how long learners pause between halves and by
    /// the orchestrator to enforce a minimum bridge time before allowing
    /// finish-bridge.
    /// </summary>
    public DateTimeOffset? BridgeStartedAt { get; set; }

    /// <summary>
    /// Snapshot of combined readiness band code at completion. Allows
    /// historical browsing without recomputing if either underlying
    /// evaluation is later mutated by an expert.
    /// </summary>
    [MaxLength(32)]
    public string? ReadinessBandSnapshot { get; set; }

    public int? CombinedScaledSnapshot { get; set; }
}
