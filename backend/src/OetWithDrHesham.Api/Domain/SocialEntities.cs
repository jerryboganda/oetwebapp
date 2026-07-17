using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

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

// ── Sponsor Seat Packs (institutional billing) ──

/// <summary>A purchased bundle of learner seats that a sponsor can assign.
/// Seat packs are the billing unit for institutional sponsors — they buy N
/// seats at a per-seat price and assign them to individual learners.</summary>
[Index(nameof(SponsorId))]
public class SponsorSeatPack
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    /// <summary>Human-friendly label: "50-seat OET Nursing – Q2 2026"</summary>
    [MaxLength(256)]
    public string Name { get; set; } = default!;

    /// <summary>Total seats purchased in this pack.</summary>
    public int TotalSeats { get; set; }

    /// <summary>Seats currently assigned to learners.</summary>
    public int AssignedSeats { get; set; }

    /// <summary>Per-seat unit price at time of purchase (decimal, e.g. 29.99).</summary>
    public decimal UnitPrice { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "GBP";

    /// <summary>Stripe payment intent or invoice ID backing this purchase.</summary>
    [MaxLength(256)]
    public string? StripePaymentId { get; set; }

    /// <summary>Pack lifecycle: "active", "exhausted", "expired", "cancelled"</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset PurchasedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Individual seat assignment linking a seat-pack seat to a learner.</summary>
[Index(nameof(SeatPackId))]
[Index(nameof(LearnerId))]
public class SponsorSeatAssignment
{
    [Key]
    public Guid Id { get; set; }

    public Guid SeatPackId { get; set; }

    [MaxLength(64)]
    public string LearnerId { get; set; } = default!;

    [MaxLength(256)]
    public string LearnerEmail { get; set; } = default!;

    /// <summary>"assigned", "revoked", "expired"</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "assigned";

    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Immutable ledger entry for seat-pack billing events (purchase,
/// top-up, refund, seat-assign, seat-revoke).</summary>
[Index(nameof(SponsorId))]
public class SponsorBillingEvent
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    public Guid? SeatPackId { get; set; }

    /// <summary>"purchase", "top_up", "refund", "seat_assigned", "seat_revoked", "pack_expired"</summary>
    [MaxLength(32)]
    public string EventType { get; set; } = default!;

    public decimal? Amount { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public int? SeatsDelta { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? ActorUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
