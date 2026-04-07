using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

// ── Exam Booking ──

public class ExamBooking
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    public DateOnly ExamDate { get; set; }

    [MaxLength(128)]
    public string? BookingReference { get; set; }

    [MaxLength(512)]
    public string? ExternalUrl { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "planned";       // "planned", "booked", "confirmed", "completed", "cancelled"

    [MaxLength(128)]
    public string? TestCenter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// ── Content Marketplace ──

public class ContentContributor
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(1024)]
    public string? Bio { get; set; }

    [MaxLength(32)]
    public string VerificationStatus { get; set; } = "unverified";   // "unverified", "verified", "trusted"

    public int SubmissionCount { get; set; }
    public int ApprovedCount { get; set; }
    public double Rating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class ContentSubmission
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ContributorId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? TaskTypeId { get; set; }

    public string ContentPayloadJson { get; set; } = "{}";

    [MaxLength(32)]
    public string ContentType { get; set; } = "practice_task";

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(32)]
    public string? Difficulty { get; set; }

    [MaxLength(512)]
    public string? Tags { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "pending";     // "pending", "in_review", "approved", "rejected"

    [MaxLength(64)]
    public string? ReviewedBy { get; set; }

    public string? ReviewNotes { get; set; }

    [MaxLength(64)]
    public string? PublishedContentId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
