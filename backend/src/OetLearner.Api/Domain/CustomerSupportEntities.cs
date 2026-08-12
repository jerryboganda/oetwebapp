using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

public static class CustomerSupportCaseStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
}

/// <summary>
/// A time-limited, ticket-linked grant for customer-support access to one
/// learner. The case is separate from the learner so support cannot turn a
/// candidate identifier into an unrestricted lookup.
/// </summary>
[Index(nameof(ExternalTicketId), nameof(CandidateUserId), IsUnique = true)]
[Index(nameof(CandidateUserId), nameof(Status), nameof(ExpiresAt))]
public sealed class CustomerSupportCase
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string ExternalTicketId { get; set; } = default!;

    [MaxLength(64)]
    public string CandidateUserId { get; set; } = default!;

    [MaxLength(256)]
    public string Subject { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = CustomerSupportCaseStatuses.Open;

    [MaxLength(64)]
    public string OpenedByAdminId { get; set; } = default!;

    [MaxLength(128)]
    public string OpenedByAdminName { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
