using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>Content revision snapshot for version history.</summary>
public class ContentRevision
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ContentItemId { get; set; } = default!;

    public int RevisionNumber { get; set; }

    [MaxLength(32)]
    public string State { get; set; } = "saved";

    [MaxLength(512)]
    public string? ChangeNote { get; set; }

    /// <summary>Full JSON snapshot of the content item at this revision.</summary>
    public string SnapshotJson { get; set; } = "{}";

    [MaxLength(128)]
    public string CreatedBy { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Versioned AI evaluation configuration.</summary>
public class AIConfigVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Model { get; set; } = default!;

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(64)]
    public string TaskType { get; set; } = default!;

    public AIConfigStatus Status { get; set; } = AIConfigStatus.Testing;

    public double Accuracy { get; set; }

    public double ConfidenceThreshold { get; set; }

    [MaxLength(512)]
    public string? RoutingRule { get; set; }

    [MaxLength(128)]
    public string? ExperimentFlag { get; set; }

    [MaxLength(128)]
    public string PromptLabel { get; set; } = default!;

    [MaxLength(512)]
    public string? ChangeNote { get; set; }

    [MaxLength(128)]
    public string CreatedBy { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Feature flag for rollout and kill-switch management.</summary>
public class FeatureFlag
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(128)]
    public string Key { get; set; } = default!;

    public FeatureFlagType FlagType { get; set; } = FeatureFlagType.Release;

    public bool Enabled { get; set; }

    public int RolloutPercentage { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Audit trail for all admin-initiated changes.</summary>
public class AuditEvent
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public DateTimeOffset OccurredAt { get; set; }

    [MaxLength(64)]
    public string ActorId { get; set; } = default!;

    [MaxLength(64)]
    public string? ActorAuthAccountId { get; set; }

    [MaxLength(128)]
    public string ActorName { get; set; } = default!;

    [MaxLength(128)]
    public string Action { get; set; } = default!;

    [MaxLength(64)]
    public string ResourceType { get; set; } = default!;

    [MaxLength(64)]
    public string? ResourceId { get; set; }

    [MaxLength(1024)]
    public string? Details { get; set; }

    public ApplicationUserAccount? ActorAuthAccount { get; set; }
}

/// <summary>Subscription plan definition for billing administration.</summary>
public class BillingPlan
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public decimal Price { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "USD";

    [MaxLength(16)]
    public string Interval { get; set; } = "month";

    public int ActiveSubscribers { get; set; }

    public BillingPlanStatus Status { get; set; } = BillingPlanStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
