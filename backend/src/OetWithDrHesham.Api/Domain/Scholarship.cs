using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain;

/// <summary>
/// Admin-granted financial-aid access. Distinct from coupons: bypasses payment
/// and is tracked separately in audit logs and metrics.
/// </summary>
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(Status), nameof(ExpiresAt))]
public class Scholarship
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string GrantedByAdminId { get; set; } = default!;

    /// <summary>need_based | partner_institute | testimonial | goodwill | other.</summary>
    [MaxLength(32)]
    public string Reason { get; set; } = default!;

    /// <summary>Tier the scholarship grants — basic, premium, intensive.</summary>
    [MaxLength(32)]
    public string AccessTier { get; set; } = default!;

    [MaxLength(2048)]
    public string EntitlementsJson { get; set; } = "{}";

    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    [MaxLength(64)]
    public string? RevokedByAdminId { get; set; }

    [MaxLength(1024)]
    public string? AdminNotes { get; set; }

    /// <summary>pending | active | revoked | expired.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
