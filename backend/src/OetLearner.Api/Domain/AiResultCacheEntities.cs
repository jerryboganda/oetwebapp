using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// W5 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// versioned result cache shared by Listening (this wave) and Reading (W9).
/// Unique <see cref="CacheKey"/> is a digest of feature + version dimensions
/// so a prompt/rulebook/question revision never serves a stale payload.
/// </summary>
public sealed class AiResultCacheEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CacheKey { get; set; } = default!;

    [MaxLength(64)]
    public string FeatureCode { get; set; } = default!;

    [MaxLength(32)]
    public string Module { get; set; } = default!;

    [MaxLength(64)]
    public string? PromptVersion { get; set; }

    [MaxLength(64)]
    public string? RulebookVersion { get; set; }

    [MaxLength(64)]
    public string? ResourceVersion { get; set; }

    public string PayloadJson { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastServedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public int ServedCount { get; set; }
}
