using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// A manually-entered credit/promotional grant for one AI provider (owner directive 2026-10-09,
/// phase 2). Providers do not expose prepaid balances over an API (Anthropic and OpenAI both have
/// no balance endpoint — verified 2026-10-09), so the operator types the grant here when credits
/// arrive (e.g. "$200 promotional Anthropic API credits") and the dashboard computes
/// <c>remaining = GrantUsd − internally-tracked spend on that provider since StartsAt</c>.
///
/// <para>This is deliberately a computed figure: the dashboard labels it "computed internally",
/// never "verified by the provider". Deleting a row is part of the audit trail via
/// <c>AuditEvents</c>; rows are never silently edited — corrections add a new row.</para>
/// </summary>
[Index(nameof(ProviderCode), nameof(StartsAt))]
public class AiCreditGrant
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary><see cref="AiProvider.Code"/> the grant belongs to (e.g. <c>anthropic</c>).</summary>
    [MaxLength(64)]
    public string ProviderCode { get; set; } = default!;

    /// <summary>Credit amount in USD as entered by the operator.</summary>
    public decimal GrantUsd { get; set; }

    /// <summary>Spend on the provider before this instant does not count against the grant.</summary>
    public DateTimeOffset StartsAt { get; set; }

    [MaxLength(512)]
    public string? Note { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(128)]
    public string? CreatedByAdminName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
