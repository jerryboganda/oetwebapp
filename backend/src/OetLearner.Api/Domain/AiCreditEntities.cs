using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Source of a credit ledger entry.</summary>
public enum AiCreditSource
{
    /// <summary>Monthly / period renewal from the user's plan.</summary>
    PlanRenewal = 0,
    /// <summary>Admin-granted promo credits.</summary>
    Promo = 1,
    /// <summary>Paid top-up via Stripe / BillingAddOn.</summary>
    Purchase = 2,
    /// <summary>Manual admin adjustment (refund, complaint, etc.).</summary>
    AdminAdjustment = 3,
    /// <summary>Debit posted by the gateway on every successful call.</summary>
    UsageDebit = 4,
    /// <summary>Expiration: negative entry that zeroes out a previous grant.</summary>
    Expiration = 5,
}

/// <summary>
/// Append-only ledger of AI credit movements per user. Tokens and USD both
/// tracked so admins can reason in either unit. Balance = SUM(TokensDelta).
///
/// Never deleted; corrections are new entries.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(ExpiresAt))]
public class AiCreditLedgerEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>Signed token delta. Positive for grants, negative for
    /// debits/expirations.</summary>
    public int TokensDelta { get; set; }

    /// <summary>Signed cost delta in USD. Informational; the canonical
    /// unit is tokens.</summary>
    public decimal CostDeltaUsd { get; set; }

    public AiCreditSource Source { get; set; }

    /// <summary>Natural-language description for admin explorer.</summary>
    [MaxLength(256)]
    public string? Description { get; set; }

    /// <summary>Optional reference to another table (e.g. stripe payment id,
    /// audit event id, usage record id).</summary>
    [MaxLength(64)]
    public string? ReferenceId { get; set; }

    /// <summary>When this grant expires. Null = no expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>If this entry has been expired by an <see cref="Expiration"/>
    /// entry, that entry's id.</summary>
    [MaxLength(64)]
    public string? ExpiredByEntryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }
}
