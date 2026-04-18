using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>Lifecycle state for a stored BYOK credential.</summary>
public enum AiCredentialStatus
{
    Active = 0,
    /// <summary>Last call returned 401/403. In cooldown until retry is allowed.</summary>
    Invalid = 1,
    /// <summary>User revoked the key.</summary>
    Revoked = 2,
}

/// <summary>
/// User-supplied (BYOK) API credential. Encrypted at rest via ASP.NET
/// Data Protection; never returned to the client after save.
/// Only a <see cref="KeyHint"/> (last 4 chars) is exposed for UI.
///
/// See <c>docs/AI-USAGE-POLICY.md</c> §1 and §8.
/// </summary>
[Index(nameof(UserId), nameof(ProviderCode), IsUnique = true)]
public class UserAiCredential
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? AuthAccountId { get; set; }

    /// <summary>Stable provider code matching an <c>AiProvider.Code</c> row,
    /// e.g. <c>openai-platform</c>, <c>anthropic</c>, <c>openrouter</c>,
    /// <c>gemini</c>.</summary>
    [MaxLength(64)]
    public string ProviderCode { get; set; } = default!;

    /// <summary>Opaque ciphertext from <c>IDataProtector.Protect()</c>.
    /// Purpose-scoped so a compromised protector used elsewhere (e.g. MFA)
    /// cannot decrypt these values.</summary>
    [MaxLength(4096)]
    public string EncryptedKey { get; set; } = default!;

    /// <summary>Last 4 chars of the plaintext for display. Safe to expose.</summary>
    [MaxLength(16)]
    public string KeyHint { get; set; } = default!;

    /// <summary>Optional: restrict which models this key may be used with.
    /// Comma-separated.</summary>
    [MaxLength(1024)]
    public string ModelAllowlistCsv { get; set; } = string.Empty;

    public AiCredentialStatus Status { get; set; } = AiCredentialStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }

    [MaxLength(32)]
    public string? LastErrorCode { get; set; }

    /// <summary>Cooldown expiry after a 401/403. BYOK resolver skips this
    /// credential until <c>DateTimeOffset.UtcNow &gt; CooldownUntil</c>.</summary>
    public DateTimeOffset? CooldownUntil { get; set; }

    public ApplicationUserAccount? AuthAccount { get; set; }
}

/// <summary>Per-user BYOK preferences. See §1 of the policy.</summary>
public enum AiCredentialMode
{
    /// <summary>Prefer BYOK, fall through to platform when not available.</summary>
    Auto = 0,
    /// <summary>Refuse platform-funded calls entirely. BYOK or nothing.</summary>
    ByokOnly = 1,
    /// <summary>Ignore any stored BYOK; always use platform.</summary>
    PlatformOnly = 2,
}

public class UserAiPreferences
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public AiCredentialMode Mode { get; set; } = AiCredentialMode.Auto;

    /// <summary>When BYOK errors, may the resolver transparently use
    /// platform credits? Defaults true to avoid silent failures.</summary>
    public bool AllowPlatformFallback { get; set; } = true;

    /// <summary>Per-feature override: feature code → mode. Stored as JSON.</summary>
    [MaxLength(2048)]
    public string PerFeatureOverridesJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
