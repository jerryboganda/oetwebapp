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

/// <summary>
/// Admin-editable definition of a payment method shown to learners on the manual
/// payment page (InstaPay, Vodafone Cash/Fawry, QNB, Stripe, PayPal, Monzo, …).
/// Replaces the previously hard-coded frontend list so account details can be
/// changed without a code deploy. Seeded from the original hard-coded values.
/// </summary>
[Index(nameof(Key), IsUnique = true)]
[Index(nameof(Category), nameof(IsActive), nameof(DisplayOrder))]
public class PaymentMethodConfig
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable slug used as the submitted <c>Method</c> value, e.g. <c>instapay_qr_link</c>.</summary>
    [MaxLength(64)]
    public string Key { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    /// <summary>inside_egypt | international.</summary>
    [MaxLength(32)]
    public string Category { get; set; } = "international";

    /// <summary>Primary detail line (handle / number / account name).</summary>
    [MaxLength(256)]
    public string Detail { get; set; } = string.Empty;

    /// <summary>Secondary detail line (link / account number / SWIFT).</summary>
    [MaxLength(256)]
    public string? Meta { get; set; }

    [MaxLength(2048)]
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Optional badge, e.g. "Inside Egypt only.".</summary>
    [MaxLength(256)]
    public string? Note { get; set; }

    /// <summary>When true the learner is told the payment reference must be "full name - course name".</summary>
    public bool ReferenceRule { get; set; }

    /// <summary>When true a QR image is shown (uploaded blob via <see cref="QrImageKey"/>, else a static fallback).</summary>
    public bool ShowQr { get; set; }

    /// <summary>IFileStorage key for an admin-uploaded QR image. Never exposed to learners directly.</summary>
    [MaxLength(512)]
    public string? QrImageKey { get; set; }

    /// <summary>Lucide icon name string, mapped to a component on the frontend.</summary>
    [MaxLength(64)]
    public string? IconName { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
