using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(NormalizedEmail), IsUnique = true)]
public class ApplicationUserAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [MaxLength(256)]
    public string NormalizedEmail { get; set; } = default!;

    [MaxLength(512)]
    public string PasswordHash { get; set; } = default!;

    [MaxLength(32)]
    public string Role { get; set; } = ApplicationUserRoles.Learner;

    [MaxLength(1024)]
    public string? ProtectedAuthenticatorSecret { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? AuthenticatorEnabledAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RefreshTokenRecord> RefreshTokens { get; set; } = new List<RefreshTokenRecord>();
    public ICollection<EmailOtpChallenge> EmailOtpChallenges { get; set; } = new List<EmailOtpChallenge>();
    public ICollection<MfaRecoveryCode> RecoveryCodes { get; set; } = new List<MfaRecoveryCode>();
    public ICollection<ExternalIdentityLink> ExternalIdentityLinks { get; set; } = new List<ExternalIdentityLink>();
}

[Index(nameof(ApplicationUserAccountId), nameof(TokenHash), IsUnique = true)]
public class RefreshTokenRecord
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    [MaxLength(512)]
    public string TokenHash { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public ApplicationUserAccount ApplicationUserAccount { get; set; } = default!;
}

public class EmailOtpChallenge
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    [MaxLength(64)]
    public string Purpose { get; set; } = "sign_in";

    [MaxLength(512)]
    public string CodeHash { get; set; } = default!;

    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }

    public ApplicationUserAccount ApplicationUserAccount { get; set; } = default!;
}

public class MfaRecoveryCode
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    [MaxLength(512)]
    public string CodeHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }

    public ApplicationUserAccount ApplicationUserAccount { get; set; } = default!;
}

[Index(nameof(Provider), nameof(ProviderSubject), IsUnique = true)]
public class ExternalIdentityLink
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    [MaxLength(32)]
    public string Provider { get; set; } = default!;

    [MaxLength(256)]
    public string ProviderSubject { get; set; } = default!;

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [MaxLength(128)]
    public string? FirstName { get; set; }

    [MaxLength(128)]
    public string? LastName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSignedInAt { get; set; }

    public ApplicationUserAccount ApplicationUserAccount { get; set; } = default!;
}

public static class ApplicationUserRoles
{
    public const string Learner = "learner";
    public const string Expert = "expert";
    public const string Admin = "admin";
}

/// <summary>Granular permissions for admin users. Stored as comma-separated claim values.</summary>
public static class AdminPermissions
{
    public const string ContentRead = "content:read";
    public const string ContentWrite = "content:write";
    public const string ContentPublish = "content:publish";
    public const string BillingRead = "billing:read";
    public const string BillingWrite = "billing:write";
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string ReviewOps = "review_ops";
    public const string QualityAnalytics = "quality_analytics";
    public const string AiConfig = "ai_config";
    public const string FeatureFlags = "feature_flags";
    public const string AuditLogs = "audit_logs";
    public const string SystemAdmin = "system_admin";

    /// <summary>Full permission set granted to system administrators.</summary>
    public static readonly string[] All =
    [
        ContentRead, ContentWrite, ContentPublish,
        BillingRead, BillingWrite,
        UsersRead, UsersWrite,
        ReviewOps, QualityAnalytics, AiConfig,
        FeatureFlags, AuditLogs, SystemAdmin
    ];
}

/// <summary>Admin permission assignment record.</summary>
public class AdminPermissionGrant
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AdminUserId { get; set; } = default!;

    [MaxLength(64)]
    public string Permission { get; set; } = default!;

    [MaxLength(128)]
    public string GrantedBy { get; set; } = default!;

    public DateTimeOffset GrantedAt { get; set; }

    public ApplicationUserAccount? AdminUser { get; set; }
}
