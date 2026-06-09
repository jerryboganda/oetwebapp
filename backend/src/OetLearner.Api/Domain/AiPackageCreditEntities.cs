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
}

[Index(nameof(UserId), IsUnique = true)]
public class AiPackageCreditAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int FlexibleCredits { get; set; }
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

    public int FlexibleCreditsDelta { get; set; }
    public int WritingOnlyCreditsDelta { get; set; }
    public int SpeakingOnlyCreditsDelta { get; set; }
    public int ListeningTestsDelta { get; set; }
    public int ReadingTestsDelta { get; set; }
    public int MockExamsDelta { get; set; }

    public AiPackageCreditReason Reason { get; set; }

    [MaxLength(128)]
    public string? ReferenceId { get; set; }

    [MaxLength(64)]
    public string? JobId { get; set; }

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }
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
