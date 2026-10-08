using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// The saved, owner-set provider order of ONE AI pipeline stage (Writing grading, Writing reviewer,
/// Speaking grading, Speaking reviewer, Speaking live voice). Owner directive 2026-10-09: this row is the
/// source of truth once it exists; the built-in default order is used only to create the row at first
/// setup (insert-only) and never overrides a saved decision.
///
/// <see cref="ChainJson"/> holds the ordered hops (see AiPipelineHop). <see cref="Version"/> is bumped by
/// every write and compared with the client's expected version (optimistic concurrency). Each write also
/// appends an <see cref="AiPipelineStageRevision"/>, which is the audit trail and the rollback source.
/// </summary>
[Index(nameof(StageKey), IsUnique = true)]
public class AiPipelineStage
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>writing.grade, writing.grade.review, speaking.grade, speaking.grade.review, speaking.live_voice.</summary>
    [MaxLength(64)]
    public string StageKey { get; set; } = default!;

    /// <summary>JSON array of hops, in priority order.</summary>
    public string ChainJson { get; set; } = "[]";

    /// <summary>Whole stage switch (reviewers: off; grading stages stay true).</summary>
    public bool StageEnabled { get; set; } = true;

    [ConcurrencyCheck]
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

/// <summary>One immutable saved version of a stage: audit trail, last-known-good and rollback source.</summary>
[Index(nameof(StageKey), nameof(Version), IsUnique = true)]
public class AiPipelineStageRevision
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string StageKey { get; set; } = default!;

    public int Version { get; set; }

    public string ChainJson { get; set; } = "[]";

    public bool StageEnabled { get; set; } = true;

    /// <summary>create | update | rollback.</summary>
    [MaxLength(16)]
    public string Kind { get; set; } = "update";

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? ChangedByAdminId { get; set; }

    [MaxLength(128)]
    public string? ChangedByName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
