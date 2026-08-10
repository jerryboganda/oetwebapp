using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Assessment subtests governed by the v1.1 score/marking model.</summary>
public enum AssessmentSubtest
{
    Listening = 1,
    Reading = 2,
}

/// <summary>Lifecycle for owner-controlled assessment governance records.</summary>
public enum AssessmentGovernanceStatus
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Effective = 3,
    Locked = 4,
    Retired = 5,
    Completed = 6,
}

/// <summary>
/// A named, owner-controlled lookup table for one Listening/Reading raw score
/// conversion profile. The table is deliberately relational so completeness
/// and uniqueness can be validated before a scaled score is emitted.
/// </summary>
[Index(nameof(Assessment), nameof(ScopeKey), nameof(VersionKey), IsUnique = true,
    Name = "UX_AssessmentScoreConversionTable_Assessment_Scope_Version")]
public sealed class AssessmentScoreConversionTable
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Assessment { get; set; } = default!;

    [MaxLength(64)]
    public string ScopeKey { get; set; } = "default";

    [MaxLength(64)]
    public string VersionKey { get; set; } = default!;

    public AssessmentGovernanceStatus Status { get; set; } = AssessmentGovernanceStatus.Draft;
    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    [MaxLength(64)]
    public string CreatedByUserId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set once a submitted attempt records this table version.</summary>
    public bool HasBeenUsed { get; set; }

    public ICollection<AssessmentScoreConversionRow> Rows { get; set; } = new List<AssessmentScoreConversionRow>();
}

/// <summary>One exact raw-score row in an assessment conversion table.</summary>
[Index(nameof(TableId), nameof(RawScore), IsUnique = true,
    Name = "UX_AssessmentScoreConversionRow_Table_RawScore")]
public sealed class AssessmentScoreConversionRow
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string TableId { get; set; } = default!;

    public int RawScore { get; set; }
    public int ConvertedScore { get; set; }

    [MaxLength(16)]
    public string? Grade { get; set; }

    /// <summary>Owner-authored pathway/pass label for this exact raw score.</summary>
    public bool? Passed { get; set; }

    public AssessmentScoreConversionTable? Table { get; set; }
}

/// <summary>
/// Immutable-at-use marking policy version. PolicyJson contains only marking
/// behavior and delivery-lock configuration; question answers and rationales
/// remain on their own versioned content records.
/// </summary>
[Index(nameof(Assessment), nameof(ScopeKey), nameof(VersionKey), IsUnique = true,
    Name = "UX_AssessmentMarkingPolicy_Assessment_Scope_Version")]
public sealed class AssessmentMarkingPolicyVersion
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Assessment { get; set; } = default!;

    [MaxLength(64)]
    public string ScopeKey { get; set; } = "default";

    [MaxLength(64)]
    public string VersionKey { get; set; } = default!;

    [MaxLength(16384)]
    public string PolicyJson { get; set; } = "{}";

    public AssessmentGovernanceStatus Status { get; set; } = AssessmentGovernanceStatus.Draft;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    [MaxLength(64)]
    public string CreatedByUserId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool HasBeenUsed { get; set; }
}

/// <summary>
/// Stored author-approved rationale/evidence for post-submit review and
/// grounded AI. This record intentionally contains no candidate PII.
/// </summary>
[Index(nameof(Assessment), nameof(QuestionRevisionId), IsUnique = true,
    Name = "UX_AssessmentRationale_Assessment_QuestionRevision")]
public sealed class AssessmentRationale
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Assessment { get; set; } = default!;

    [MaxLength(128)]
    public string QuestionRevisionId { get; set; } = default!;

    [MaxLength(4096)]
    public string SourceSentence { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string RationaleText { get; set; } = string.Empty;

    public int EvidenceCount { get; set; }
    public AssessmentGovernanceStatus Status { get; set; } = AssessmentGovernanceStatus.Draft;

    [MaxLength(64)]
    public string CreatedByUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Auditable controlled re-mark request for key/variant corrections.</summary>
[Index(nameof(Assessment), nameof(Status), nameof(CreatedAt),
    Name = "IX_AssessmentReMarkJob_Assessment_Status_CreatedAt")]
public sealed class AssessmentReMarkJob
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Assessment { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string QuestionRevisionId { get; set; } = default!;

    [MaxLength(4096)]
    public string Reason { get; set; } = default!;

    [MaxLength(4096)]
    public string OriginalKeySnapshotJson { get; set; } = "{}";

    [MaxLength(4096)]
    public string NewKeySnapshotJson { get; set; } = "{}";

    [MaxLength(64)]
    public string RequestedByUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public AssessmentGovernanceStatus Status { get; set; } = AssessmentGovernanceStatus.InReview;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    [MaxLength(8192)]
    public string? AffectedAttemptIdsJson { get; set; }
}
