using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Immutable server-side actor state captured when a v1.1 speaking card is
/// revealed. It is deliberately separate from the learner-facing card and
/// from the assessment snapshot so the actor can never fall back to a mutable
/// card row or a previous card transcript.
/// </summary>
public sealed class SpeakingSimulationV11PersonaRuntimeSnapshot
{
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? ExamSessionId { get; set; }

    [MaxLength(64)]
    public string SpeakingSessionId { get; set; } = default!;

    [MaxLength(64)]
    public string RolePlayCardId { get; set; } = default!;

    [MaxLength(2)]
    public string CardSlot { get; set; } = "standalone";

    [MaxLength(32)]
    public string ProfessionId { get; set; } = "medicine";

    [MaxLength(64)]
    public string SpecVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PersonaVersion { get; set; } = "speaking-simulation-v1.1-persona";

    /// <summary>Timestamp-derived version of the authored card pinned at reveal.</summary>
    [MaxLength(64)]
    public string CardVersion { get; set; } = string.Empty;

    /// <summary>Unique actor memory boundary. No transcript or hidden facts may
    /// cross this scope unless the explicit second-visit activation succeeds.</summary>
    [MaxLength(128)]
    public string MemoryScopeKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ScenarioTitle { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Setting { get; set; } = string.Empty;

    [MaxLength(256)]
    public string CandidateRole { get; set; } = string.Empty;

    [MaxLength(256)]
    public string InterlocutorRole { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PatientEmotion { get; set; } = string.Empty;

    [MaxLength(256)]
    public string CommunicationGoal { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ClinicalTopic { get; set; } = string.Empty;

    [MaxLength(32)]
    public string PersonaRole { get; set; } = "patient";

    /// <summary>JSON array of current-card fact keys the actor may use.</summary>
    public string AllowedFactsJson { get; set; } = "[]";

    /// <summary>JSON array of fact keys explicitly approved for Card B
    /// carry-over. It is never inferred from the patient role.</summary>
    public string ApprovedCarryFactKeysJson { get; set; } = "[]";

    /// <summary>JSON array of facts the actor must never reveal or infer.</summary>
    public string ProhibitedFactsJson { get; set; } = "[]";

    /// <summary>JSON object mapping current-card fact keys to reveal rules.</summary>
    public string RevealConditionsJson { get; set; } = "{}";

    /// <summary>Explicit author-approved signal required before Card B can
    /// carry any selected Card A facts. The patient role alone is insufficient.</summary>
    [MaxLength(500)]
    public string? ExplicitSecondVisitIndicator { get; set; }

    /// <summary>JSON object containing only the approved carried facts after
    /// explicit second-visit activation. Never contains a transcript.</summary>
    public string CarriedFactsJson { get; set; } = "{}";

    public bool FollowUpEligible { get; set; }

    public bool FollowUpActivated { get; set; }

    public DateTimeOffset? FollowUpActivatedAt { get; set; }

    /// <summary>Server-only serialized persona and hidden facts for the current
    /// card. This field must never be included in learner DTOs or hub events.</summary>
    public string PersonaJson { get; set; } = "{}";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
