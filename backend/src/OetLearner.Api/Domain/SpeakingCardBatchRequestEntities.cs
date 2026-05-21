using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

// Phase 11 (G.11.4) of the OET Speaking module roadmap.
//
// `SpeakingCardBatchRequest` captures an admin's intent to batch-generate
// many role-play card drafts (and their paired interlocutor scripts) for a
// given profession. The background job `SpeakingCardBatchAuthor` picks up
// Pending rows on a 60-second tick, asks the AI gateway to draft each
// card via the `admin.content_generation` feature route, and persists the
// resulting drafts into `RolePlayCard` + `InterlocutorScript` with
// `Status = Draft` so admins can sweep them in the standard queue.
//
// The entity is intentionally light: only one row is touched per batch, and
// it tracks `Status` ∈ {Pending, Running, Completed, Failed}, `Count`,
// `GeneratedCount`, `Error`, and an `IdempotencyKey` so admins cannot
// accidentally enqueue the same job twice.

public enum SpeakingCardBatchRequestStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(IdempotencyKey), IsUnique = true)]
public class SpeakingCardBatchRequest
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Owning profession id (e.g. "nursing"). The job filters every
    /// generated card to this profession before persisting.</summary>
    [MaxLength(32)]
    public string ProfessionId { get; set; } = "nursing";

    /// <summary>How many cards to draft for this batch (1..50). The job
    /// enforces a hard upper bound and one card per AI call.</summary>
    public int Count { get; set; }

    /// <summary>How many cards have actually been drafted so far. Updated
    /// after each successful AI completion + persistence.</summary>
    public int GeneratedCount { get; set; }

    /// <summary>JSON array of free-text topic strings the admin wants the
    /// AI to cover (e.g. ["discharge","medication"]). Empty array means
    /// "any clinical topic appropriate to this profession".</summary>
    [MaxLength(2000)]
    public string TopicListJson { get; set; } = "[]";

    /// <summary>JSON object mapping difficulty code to the number of cards
    /// to draft at that difficulty (e.g. `{"core":3,"extension":1,"exam":1}`).
    /// Empty object means "all core". The job sums the buckets up to
    /// <see cref="Count"/>; any leftover spills into "core".</summary>
    [MaxLength(500)]
    public string DifficultyDistributionJson { get; set; } = "{}";

    public SpeakingCardBatchRequestStatus Status { get; set; } = SpeakingCardBatchRequestStatus.Pending;

    /// <summary>Admin who enqueued the batch (audit + display).</summary>
    [MaxLength(64)]
    public string RequestedByAdminId { get; set; } = default!;

    /// <summary>Optional admin display name for the queue list. Captured at
    /// enqueue time so we don't re-resolve when rendering.</summary>
    [MaxLength(160)]
    public string? RequestedByAdminName { get; set; }

    /// <summary>Optional client-supplied idempotency key. When present, the
    /// service refuses to enqueue a second batch with the same key.</summary>
    [MaxLength(96)]
    public string? IdempotencyKey { get; set; }

    /// <summary>Most recent error message when <see cref="Status"/> is
    /// Failed. Truncated to 1024 chars so the admin queue list always
    /// renders the head of a stack trace.</summary>
    [MaxLength(1024)]
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
