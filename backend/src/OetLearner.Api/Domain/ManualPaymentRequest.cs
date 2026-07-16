using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Universal proof-of-payment record — exactly one row per order.
/// <see cref="Kind"/> discriminates the two sources: a learner-uploaded file for
/// offline methods (bank transfer / Fawry voucher / Vodafone Cash / …), which the
/// admin verifies and approves; or a receipt the system mints automatically when a
/// card gateway completes. On approval, access is granted via the same code path as
/// gateway payments.
///
/// NAME IS LOAD-BEARING: production runs blue/green, so the entity and its
/// <c>ManualPaymentRequests</c> table keep the original name even though the record
/// is no longer manual-only — a rename would break the outgoing container mid-rollover.
/// Admin UI calls it "Payment Proof".
/// </summary>
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(ProofHashHex))]
[Index(nameof(Status), nameof(SubmittedAt))]
[Index(nameof(PaymentTransactionId))]
[Index(nameof(Kind), nameof(Status))]
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

    /// <summary>bank_transfer | wise | fawry_offline | other — or the gateway name for a
    /// <see cref="PaymentProofKinds.GatewayReceipt"/> row.</summary>
    [MaxLength(32)]
    public string Method { get; set; } = default!;

    /// <summary><see cref="PaymentProofKinds"/> — learner_upload | gateway_receipt.</summary>
    [MaxLength(24)]
    public string Kind { get; set; } = PaymentProofKinds.LearnerUpload;

    /// <summary>Card gateway that produced this receipt (stripe / paypal / easykash / …).
    /// Null for learner uploads.</summary>
    [MaxLength(32)]
    public string? Gateway { get; set; }

    /// <summary>The <see cref="PaymentTransaction"/> this receipt was minted from. Also the
    /// idempotency key — the auto-receipt writer must not create a second row for the same
    /// transaction. Null for learner uploads.</summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>Buyer's registered profession at purchase time, captured so the order record
    /// stays truthful even if the profession is later changed by an admin.</summary>
    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    /// <summary>Storage key for the uploaded payment proof image/PDF. Null for gateway
    /// receipts, which have no file. Never exposed to clients — the DTO carries
    /// <c>HasProof</c> instead.</summary>
    [MaxLength(2048)]
    public string? ProofUrl { get; set; } = string.Empty;

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

    // ── Admin proof waiver ──
    // Set when an admin releases a pending offline order without an uploaded file
    // (payment confirmed out-of-band). All three are written together, with an AuditEvent.

    [MaxLength(64)]
    public string? ProofWaivedByAdminId { get; set; }

    public DateTimeOffset? ProofWaivedAt { get; set; }

    [MaxLength(512)]
    public string? ProofWaiverReason { get; set; }

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
