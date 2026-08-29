using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// W11 — short-lived encrypted raw provider payload. After <see cref="ExpiresAt"/>
/// the ciphertext is purged; hash + provenance remain.
/// </summary>
[Index(nameof(ExpiresAt))]
[Index(nameof(OperationId))]
public class AiRawResponse
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string? OperationId { get; set; }

    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    [MaxLength(64)]
    public string? ProviderCode { get; set; }

    public string? PayloadCiphertext { get; set; }

    [MaxLength(64)]
    public string PayloadSha256 { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }
}
