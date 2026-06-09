using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Bank-transfer / Fawry-voucher / cash payment claim submitted by a learner.
/// Admin verifies the proof and approves; on approval, access is granted via
/// the same code path as gateway payments.
/// </summary>
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(ProofHashHex))]
[Index(nameof(Status), nameof(SubmittedAt))]
public class ManualPaymentRequest
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? QuoteId { get; set; }

    public decimal AmountAmount { get; set; }

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    /// <summary>bank_transfer | wise | fawry_offline | other.</summary>
    [MaxLength(32)]
    public string Method { get; set; } = default!;

    /// <summary>URL to the uploaded payment proof image/PDF.</summary>
    [MaxLength(2048)]
    public string ProofUrl { get; set; } = string.Empty;

    /// <summary>SHA-256 hex hash of the uploaded proof file — used to reject duplicates.</summary>
    [MaxLength(64)]
    public string ProofHashHex { get; set; } = string.Empty;

    /// <summary>Bank transaction reference / Fawry voucher code provided by the learner.</summary>
    [MaxLength(128)]
    public string Reference { get; set; } = string.Empty;

    [MaxLength(128)]
    public string CandidateFullName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string CandidateEmail { get; set; } = string.Empty;

    [MaxLength(64)]
    public string CandidateWhatsApp { get; set; } = string.Empty;

    [MaxLength(128)]
    public string CourseName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? CourseId { get; set; }

    [MaxLength(16)]
    public string PaymentCategory { get; set; } = "international";

    /// <summary>pending | needs_review | approved | paid | rejected | cancelled.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    [MaxLength(64)]
    public string? ReviewedByAdminId { get; set; }

    [MaxLength(1024)]
    public string? AdminNotes { get; set; }

    [MaxLength(64)]
    public string? AccessGrantedSubscriptionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Admin-editable bank account information shown to learners during manual payment.</summary>
[Index(nameof(Region), nameof(Currency), nameof(IsActive))]
public class BankAccountConfig
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Region { get; set; } = default!;

    [MaxLength(8)]
    public string Currency { get; set; } = default!;

    [MaxLength(128)]
    public string BankName { get; set; } = default!;

    [MaxLength(128)]
    public string AccountHolderName { get; set; } = default!;

    [MaxLength(64)]
    public string? Iban { get; set; }

    [MaxLength(64)]
    public string? SwiftBic { get; set; }

    [MaxLength(128)]
    public string? AccountNumber { get; set; }

    [MaxLength(128)]
    public string? RoutingOrSortCode { get; set; }

    [MaxLength(2048)]
    public string? InstructionsMarkdown { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
