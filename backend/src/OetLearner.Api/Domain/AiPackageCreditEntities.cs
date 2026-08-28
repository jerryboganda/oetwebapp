using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

public enum AiPackageCreditReason
{
    Purchase = 0,
    GradingDeduct = 1,
    RefundOnFailure = 2,
    Expiry = 3,
    AdminAdjustment = 4,
    MockDeduct = 5,
    MockRefundOnFailure = 6,
    PassExpiry = 7,
    ObjectivePracticeDeduct = 8,
    GrantReversed = 9,
}

[Index(nameof(UserId), IsUnique = true)]
public class AiPackageCreditAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Restricted flexible pool usable only for Writing or Speaking
    /// (Quick Check / Exam Prep Pro credits). Never consumed by
    /// Listening/Reading attempts.
    /// </summary>
    public int FlexibleCredits { get; set; }

    /// <summary>
    /// Universal Shared AI Credits (Full Course gift currency). Usable across
    /// all four subtests: Reading 1, Listening 1, Writing 2, Speaking 2.
    /// </summary>
    public int SharedCredits { get; set; }

    public int WritingOnlyCredits { get; set; }
    public int SpeakingOnlyCredits { get; set; }
    public int? ListeningTestsRemaining { get; set; }
    public int? ReadingTestsRemaining { get; set; }
    public int MockExamsRemaining { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool ExpiredBecausePassed { get; set; }
    public DateTimeOffset? PassedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(UserId), nameof(SourceReferenceId))]
public class AiPackageCreditTransaction
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AccountId { get; set; } = default!;

    [MaxLength(128)]
    public string? StripeSessionId { get; set; }

    [MaxLength(64)]
    public string? PackageId { get; set; }

    [MaxLength(32)]
    public string? PackageType { get; set; }

    public int SharedCreditsDelta { get; set; }
    public int FlexibleCreditsDelta { get; set; }
    public int WritingOnlyCreditsDelta { get; set; }
    public int SpeakingOnlyCreditsDelta { get; set; }
    public int ListeningTestsDelta { get; set; }
    public int ReadingTestsDelta { get; set; }
    public int MockExamsDelta { get; set; }

    [MaxLength(4000)]
    public string? AllocationJson { get; set; }

    public AiPackageCreditReason Reason { get; set; }

    [MaxLength(128)]
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Stable purchase owner used when a learner has multiple grants with the
    /// same package code. Activity/refund references remain in
    /// <see cref="ReferenceId"/>.
    /// </summary>
    [MaxLength(128)]
    public string? SourceReferenceId { get; set; }

    [MaxLength(64)]
    public string? JobId { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }
}

[Index(nameof(UserId), nameof(Expired), nameof(ExpiresAt))]
[Index(nameof(AccountId), nameof(Expired))]
[Index(nameof(SourceReferenceId))]
public class AiPackageCreditLot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AccountId { get; set; } = default!;

    [MaxLength(64)]
    public string? PackageId { get; set; }

    [MaxLength(32)]
    public string? PackageType { get; set; }

    public int SharedCredits { get; set; }
    public int FlexibleCredits { get; set; }
    public int WritingOnlyCredits { get; set; }
    public int SpeakingOnlyCredits { get; set; }
    public int? ListeningTestsRemaining { get; set; }
    public int? ReadingTestsRemaining { get; set; }
    public int MockExamsRemaining { get; set; }
    public bool UnlimitedGrading { get; set; }
    public bool UnlimitedListening { get; set; }
    public bool UnlimitedReading { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    [MaxLength(128)]
    public string? SourceReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public bool Expired { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
}

[Index(nameof(UserId), nameof(ExamDate))]
public class LearnerExamOutcome
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public bool Passed { get; set; }
    public DateTimeOffset ExamDate { get; set; }

    [MaxLength(64)]
    public string RecordedByAdminId { get; set; } = default!;

    [MaxLength(128)]
    public string RecordedByAdminName { get; set; } = default!;

    [MaxLength(512)]
    public string? EvidenceNote { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
