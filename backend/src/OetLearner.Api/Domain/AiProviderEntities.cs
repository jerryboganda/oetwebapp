using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Dialect the provider speaks. Determines which concrete
/// <c>IAiModelProvider</c> the gateway dispatches to.</summary>
public enum AiProviderDialect
{
    OpenAiCompatible = 0,
    Anthropic = 1,
    Mock = 99,
}

/// <summary>
/// DB-backed provider registry (Slice 5). Replaces config-only registration
/// so admins can add/rotate providers without a redeploy. The concrete
/// <c>IAiModelProvider</c> implementations resolve the active row at call
/// time via <see cref="Code"/>.
///
/// Platform keys stored here are encrypted via ASP.NET Data Protection,
/// exactly like BYOK keys in <c>UserAiCredential</c>.
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class AiProvider
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Stable code: <c>digitalocean-serverless</c>,
    /// <c>openai-platform</c>, <c>anthropic</c>, <c>openrouter</c>, …</summary>
    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public AiProviderDialect Dialect { get; set; } = AiProviderDialect.OpenAiCompatible;

    [MaxLength(512)]
    public string BaseUrl { get; set; } = default!;

    /// <summary>Encrypted platform API key (via purpose-scoped Data Protection).</summary>
    [MaxLength(4096)]
    public string EncryptedApiKey { get; set; } = string.Empty;

    [MaxLength(16)]
    public string ApiKeyHint { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DefaultModel { get; set; } = "";

    /// <summary>Comma-separated allow-list of permitted models. Empty = all.</summary>
    [MaxLength(1024)]
    public string AllowedModelsCsv { get; set; } = string.Empty;

    /// <summary>Price per 1,000 prompt tokens, USD. Stored at provider level
    /// so cost estimates are consistent across features.</summary>
    public decimal PricePer1kPromptTokens { get; set; }

    /// <summary>Price per 1,000 completion tokens, USD.</summary>
    public decimal PricePer1kCompletionTokens { get; set; }

    /// <summary>Polly retry count for transient failures.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Polly circuit-breaker threshold (consecutive failures).</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Circuit-breaker rolling window.</summary>
    public int CircuitBreakerWindowSeconds { get; set; } = 30;

    /// <summary>Display order in failover routing. Lower = tried first.</summary>
    public int FailoverPriority { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}
