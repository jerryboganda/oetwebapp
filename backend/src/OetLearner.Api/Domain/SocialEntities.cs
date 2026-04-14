using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// ── Certificates ──

public class Certificate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string UserDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string Type { get; set; } = default!;          // "course_completion", "mock_achievement", "streak_milestone", "level_milestone"

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string DataJson { get; set; } = "{}";          // Scores, dates, specific achievements

    [MaxLength(512)]
    public string? PdfUrl { get; set; }

    [MaxLength(64)]
    public string VerificationCode { get; set; } = default!;

    public DateTimeOffset IssuedAt { get; set; }
}

// ── Referral Program ──

public class ReferralCode
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string Code { get; set; } = default!;          // Short unique code (e.g., "FAISAL2026")

    public int TotalReferrals { get; set; }
    public int ConvertedReferrals { get; set; }
    public decimal TotalCreditsEarned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class Referral
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReferrerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ReferredUserId { get; set; }

    [MaxLength(256)]
    public string ReferredEmail { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "pending";       // "pending", "registered", "converted", "credited"

    public decimal CreditAmount { get; set; } = 10;       // AUD

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public DateTimeOffset? CreditedAt { get; set; }
}

// ── Sponsor & Cohorts ──

public class SponsorAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(32)]
    public string Type { get; set; } = default!;          // "parent", "employer", "institution"

    [MaxLength(256)]
    public string ContactEmail { get; set; } = default!;

    [MaxLength(256)]
    public string? OrganizationName { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}

public class SponsorLearnerLink
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerId { get; set; } = default!;

    public bool LearnerConsented { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? ConsentedAt { get; set; }
}

public class Cohort
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;          // "2026 Q2 Nursing Batch"

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public int MaxSeats { get; set; }
    public int EnrolledCount { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";        // "draft", "active", "completed", "archived"

    public DateTimeOffset CreatedAt { get; set; }
}

public class CohortMember
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string CohortId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "active";        // "active", "completed", "withdrawn"

    public DateTimeOffset EnrolledAt { get; set; }
}

// ── Sponsorship (self-service by sponsor) ──

public class Sponsorship
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SponsorUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? LearnerUserId { get; set; }

    [MaxLength(256)]
    public string LearnerEmail { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "Pending";       // "Pending", "Active", "Revoked"

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
