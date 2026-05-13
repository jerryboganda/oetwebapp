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
    // H1 (security): per-account sign-in failure counter + soft lockout. The
    // IP-keyed AuthBruteforce limiter protects against volumetric attacks;
    // these columns protect against credential stuffing where the attacker
    // rotates IPs but targets a single account. Lockout is time-boxed (see
    // AuthService.SignInAsync) so support does not need to unlock accounts.
    public int FailedSignInCount { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
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

    // H3 (security): refresh-token family. Every token issued from a
    // successful sign-in shares a FamilyId; each refresh rotation preserves
    // it. Presenting a token whose FamilyId has ANY revoked-and-reused member
    // is treated as compromise → all active tokens in the family are revoked
    // (see AuthService.RefreshAsync). Initial family = token.Id.
    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    [MaxLength(512)]
    public string? DeviceInfo { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

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
    public const string Sponsor = "sponsor";
}

/// <summary>Granular permissions for admin users. Stored as comma-separated claim values.</summary>
public static class AdminPermissions
{
    public const string ContentRead = "content:read";
    public const string ContentWrite = "content:write";
    public const string ContentPublish = "content:publish";
    public const string ContentEditorReview = "content:editor_review";
    public const string ContentPublisherApproval = "content:publisher_approval";
    public const string BillingRead = "billing:read";

    /// <summary>
    /// Legacy superset write permission. Continues to grant every billing-write
    /// surface. Billing-hardening I-7 (May 2026) introduces 3 granular siblings
    /// below that should be preferred for new role definitions; existing
    /// admins with just <c>billing:write</c> remain fully capable.
    /// </summary>
    public const string BillingWrite = "billing:write";

    /// <summary>Billing-hardening I-7: grants refund + dispute mutations only.</summary>
    public const string BillingRefundWrite = "billing:refund_write";

    /// <summary>Billing-hardening I-7: grants catalog (plans, add-ons, coupons,
    /// wallet-tiers, free-tier, score-guarantee review) mutations only.</summary>
    public const string BillingCatalogWrite = "billing:catalog_write";

    /// <summary>Billing-hardening I-7: grants subscription lifecycle mutations
    /// (create, change-plan, extend, cancel, reactivate, status, wallet spend) only.</summary>
    public const string BillingSubscriptionWrite = "billing:subscription_write";

    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string ReviewOps = "review_ops";
    public const string QualityAnalytics = "quality_analytics";
    public const string AiConfig = "ai_config";
    public const string FeatureFlags = "feature_flags";
    public const string AuditLogs = "audit_logs";
    public const string SystemAdmin = "system_admin";
    public const string ManagePermissions = "manage_permissions";

    /// <summary>Full permission set granted to system administrators.</summary>
    public static readonly string[] All =
    [
        ContentRead, ContentWrite, ContentPublish,
        ContentEditorReview, ContentPublisherApproval,
        BillingRead, BillingWrite,
        BillingRefundWrite, BillingCatalogWrite, BillingSubscriptionWrite,
        UsersRead, UsersWrite,
        ReviewOps, QualityAnalytics, AiConfig,
        FeatureFlags, AuditLogs, SystemAdmin,
        ManagePermissions
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

/// <summary>Reusable permission role template.</summary>
public class PermissionTemplate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>JSON array of permission strings, e.g. ["content:read","content:write"]</summary>
    public string Permissions { get; set; } = "[]";

    [MaxLength(128)]
    public string CreatedBy { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Admin user record for role-based management.</summary>
public class AdminUser
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(256)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(256)]
    public string Email { get; set; } = default!;

    [MaxLength(64)]
    public string Role { get; set; } = "unassigned";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
