using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// W1 of the AI cost/reliability remediation (successor to the W0 Listening
/// retry-storm repair, incident INC-2026-CLAUDE-01). This is the control-plane
/// state machine every AI call will eventually route through: one durable
/// <see cref="AiOperation"/> row per logical unit of work (a scoring pass, a
/// coach reply, an admin batch job, …), leased by exactly one worker at a
/// time, retried on a bounded schedule, and resolved to exactly one terminal
/// <see cref="AiOperationState"/>.
///
/// <para>
/// <b>W1 scope is schema-only.</b> No runtime service reads or writes these
/// tables yet — the gateway, background workers, and scoring pipelines keep
/// using <see cref="AiUsageRecord"/> exactly as before. This file only lands
/// the durable structures a later wave will wire up, so the migration and
/// model can be reviewed independently of any behavior change.
/// </para>
/// </summary>
public enum AiOperationState
{
    /// <summary>Created, not yet picked up by a worker.</summary>
    Queued = 0,
    /// <summary>Claimed by a worker; <see cref="AiOperation.LeaseOwner"/> /
    /// <see cref="AiOperation.LeaseExpiresAt"/> are set.</summary>
    Leased = 1,
    /// <summary>The provider call returned a usable result; the operation is
    /// finishing post-processing (persistence, credit commit, …).</summary>
    ProviderSucceeded = 2,
    /// <summary>Terminal success — <see cref="AiOperation.ResultRef"/> points
    /// at the durable result.</summary>
    Completed = 3,
    /// <summary>Attempt failed transiently; <see cref="AiOperation.NextAttemptAt"/>
    /// is set and the operation returns to the queue.</summary>
    RetryScheduled = 4,
    /// <summary>Terminal — refused because the budget reservation could not
    /// be granted (see <see cref="AiOperation.BudgetReservationId"/>).</summary>
    BlockedBudget = 5,
    /// <summary>Terminal — the outcome could not be determined (provider
    /// response was ambiguous/lost) and no further attempt is safe.</summary>
    Indeterminate = 6,
    /// <summary>Terminal — every attempt failed and <see cref="AiOperation.AttemptLimit"/>
    /// was exhausted.</summary>
    FailedTerminal = 7,
    /// <summary>Terminal — same disposition as the W0 Listening
    /// <c>skipped_no_evidence</c> skip reason, generalised to any feature: no
    /// attempt was safe to make (e.g. quarantined credential, ungrounded
    /// prompt) so the operation is closed without ever calling a provider.</summary>
    SkippedNoEvidence = 8,
    /// <summary>Terminal — cancelled by the caller or a supervising process
    /// before it reached a provider outcome.</summary>
    Cancelled = 9,
}

/// <summary>
/// Coarse routing/priority class for an operation. Lets the worker and budget
/// layers apply different scheduling and budget-denial behaviour without
/// inspecting <see cref="AiOperation.FeatureCode"/> string patterns.
/// </summary>
public enum AiOperationClass
{
    /// <summary>Feeds a candidate score (Writing/Speaking/Listening/Pronunciation/
    /// Conversation grading). Never silently downgraded — see
    /// <c>docs/AI-USAGE-POLICY.md</c>.</summary>
    ScoringCritical = 0,
    /// <summary>Learner-facing non-scoring feature (coach, explain, chat).</summary>
    InteractiveLearning = 1,
    /// <summary>Admin authoring/content-generation batch work.</summary>
    AdminBatch = 2,
}

/// <summary>
/// Durable control-plane row for one logical AI unit of work. Exactly one
/// <see cref="AiOperation"/> exists per idempotent request (enforced by the
/// unique <see cref="IdempotencyKey"/>); every provider attempt against it is
/// recorded as a child <see cref="AiOperationAttempt"/> row so retries never
/// double-charge credits/budget or double-write a result.
/// </summary>
public class AiOperation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Coarse module grouping for reporting/rate-limiting, e.g.
    /// <c>listening</c>, <c>writing</c>, <c>speaking</c>. Distinct from the
    /// granular <see cref="FeatureCode"/>.</summary>
    [MaxLength(32)]
    public string Module { get; set; } = default!;

    /// <summary>Stable feature identifier — matches <c>AiFeatureCodes</c> /
    /// <see cref="AiUsageRecord.FeatureCode"/>.</summary>
    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    [MaxLength(64)]
    public string? UserId { get; set; }

    /// <summary>Sponsor / organisation scope, mirrors <see cref="AiUsageRecord.TenantId"/>.</summary>
    [MaxLength(64)]
    public string? TenantId { get; set; }

    /// <summary>The domain row this operation is scoring/processing (e.g. a
    /// submission or answer id). Opaque to the control plane.</summary>
    [MaxLength(64)]
    public string? ResourceId { get; set; }

    /// <summary>Discriminator for <see cref="ResourceId"/> (e.g.
    /// <c>writing_submission</c>, <c>listening_answer</c>).</summary>
    [MaxLength(64)]
    public string? ResourceType { get; set; }

    /// <summary>Version of the resource this operation targets, so a later
    /// edit to the same resource creates a new operation instead of racing
    /// the in-flight one.</summary>
    public int? ResourceVersion { get; set; }

    /// <summary>SHA-256 (hex) of the normalized request payload. Used to
    /// detect duplicate submissions independent of <see cref="IdempotencyKey"/>.</summary>
    [MaxLength(64)]
    public string? RequestHash { get; set; }

    /// <summary>Prompt-template version, mirrors <see cref="AiUsageRecord.PromptTemplateId"/>
    /// semantics but scoped to the operation rather than a single attempt.</summary>
    [MaxLength(32)]
    public string? PromptVersion { get; set; }

    /// <summary>Rulebook version stamped at operation creation time.</summary>
    [MaxLength(32)]
    public string? RulebookVersion { get; set; }

    /// <summary>Caller-supplied or derived idempotency key. Unique — retrying
    /// the same logical request resolves to the same <see cref="AiOperation"/>
    /// row instead of creating a duplicate.</summary>
    [MaxLength(256)]
    public string IdempotencyKey { get; set; } = default!;

    public AiOperationState State { get; set; } = AiOperationState.Queued;

    public AiOperationClass OperationClass { get; set; } = AiOperationClass.InteractiveLearning;

    /// <summary>Provider id chosen by the routing policy for the current/last
    /// attempt. Null before the first attempt is leased.</summary>
    [MaxLength(64)]
    public string? SelectedProviderId { get; set; }

    [MaxLength(128)]
    public string? SelectedModel { get; set; }

    /// <summary>Reference to the <see cref="AiCreditReservation"/> row backing
    /// this operation's learner credit spend, if any.</summary>
    [MaxLength(64)]
    public string? CreditReservationId { get; set; }

    /// <summary>Reference to the budget reservation (against an
    /// <see cref="AiBudgetPeriod"/>) backing this operation's platform spend,
    /// if any.</summary>
    [MaxLength(64)]
    public string? BudgetReservationId { get; set; }

    /// <summary>Maximum number of provider attempts allowed before the
    /// operation moves to <see cref="AiOperationState.FailedTerminal"/>.</summary>
    public int AttemptLimit { get; set; } = 3;

    /// <summary>Earliest instant a worker may pick this operation up again.
    /// Null when the operation is not awaiting a scheduled retry.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Identifier of the worker instance currently holding the
    /// lease (e.g. <c>host:pid:guid</c>). Null when unleased.</summary>
    [MaxLength(128)]
    public string? LeaseOwner { get; set; }

    /// <summary>Lease expiry — a worker that dies without releasing the lease
    /// makes the operation reclaimable once this passes.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Opaque pointer to the durable result once
    /// <see cref="State"/> is <see cref="AiOperationState.Completed"/> (e.g.
    /// an <see cref="AiUsageRecord"/> id or a domain result row id).</summary>
    [MaxLength(128)]
    public string? ResultRef { get; set; }
}
