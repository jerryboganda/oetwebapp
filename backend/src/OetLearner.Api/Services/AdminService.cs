using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService(
    LearnerDbContext db,
    EmailOtpService emailOtpService,
    PasswordPolicyService? passwordPolicyService,
    IPasswordHasher<ApplicationUserAccount> passwordHasher,
    TimeProvider timeProvider,
    NotificationService notifications,
    LearnerService learnerService,
    OetLearner.Api.Services.Vocabulary.IVocabularyAudioQueue? vocabularyAudioQueue = null,
    IConversationOptionsProvider? conversationOptionsProvider = null,
    OetLearner.Api.Services.VoiceDesign.IVoiceDesignRegenerationService? voiceDesignRegeneration = null,
    OetLearner.Api.Services.Professions.IProfessionCatalogService? professionCatalog = null,
    ISecurityEventLogger? securityEventLogger = null,
    OetLearner.Api.Services.Settings.IRuntimeSettingsProvider? runtimeSettingsProvider = null,
    OetLearner.Api.Services.Admin.UserHardDeleteService? userHardDeleteService = null,
    ISessionRevocationService? sessionRevocationService = null,
    IAiPackageCreditService? aiPackageCredits = null,
    // Owner-account protections (Owner Agent Console, plan Phase 3). Optional so
    // hand-built test instances keep compiling; DI always supplies it.
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.OwnerAgentOptions>? ownerAgentOptions = null)
{
    private const string ActiveUserStatus = "active";
    private const string SuspendedUserStatus = "suspended";
    private const string DeletedUserStatus = "deleted";
    private const int CatalogJsonMaxLength = 2048;
    private const int BillingDiagnosticsExampleLimit = 3;
    private const string ProviderLifecycleSource = "payment_webhook_event";

    private static readonly HashSet<string> PlanIntervals = new(StringComparer.OrdinalIgnoreCase)
    {
        "month",
        "monthly",
        "year",
        "yearly",
        // OET 2026 27-SKU catalogue uses one-time purchases with fixed-duration
        // access (e.g. 6 months). The Oet2026CatalogSeeder writes `Interval =
        // "one_time"` to BillingPlan; admin edits must round-trip the same
        // value back through the upsert validator without rejection.
        "one_time",
        "one-time"
    };

    private static readonly HashSet<string> AddOnIntervals = new(StringComparer.OrdinalIgnoreCase)
    {
        "one_time",
        "one-time",
        "month",
        "monthly",
        "year",
        "yearly"
    };

    // Valid values for BillingPlan.DiagnosticMockEntitlement (formerly defined in
    // MockDiagnosticEntitlementService, inlined here after that service was removed).
    private static readonly IReadOnlySet<string> DiagnosticMockEntitlementValues =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unlimited", "one_per_lifetime", "one_per_renewal_period", "paid_per_use", "disabled",
        };

    // ════════════════════════════════════════════
    //  Transaction + Audit helpers
    // ════════════════════════════════════════════

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (db.Database.IsInMemory()) return null;
        return await db.Database.BeginTransactionAsync(ct);
    }

    private async Task CommitIfOwnedAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Owner-account protection: refuses a credential/privilege mutation that targets an
    /// allow-listed owner account unless the acting admin is that owner (403
    /// <c>owner_account_protected</c>). See <see cref="OwnerAgentAccountProtection"/>.
    /// </summary>
    private void EnsureOwnerAccountMutationAllowed(string actorId, string? targetAuthAccountId, string operation)
        => OwnerAgentAccountProtection.EnsureMutationAllowed(ownerAgentOptions?.Value, actorId, targetAuthAccountId, operation);

    private async Task LogAuditAsync(string actorId, string actorName, string action,
        string resourceType, string? resourceId, string? details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorAuthAccountId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> RevokeActiveRefreshTokensAsync(string authAccountId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var revoked = 0;
        var activeRefreshTokens = await db.RefreshTokenRecords
            .Where(t => t.ApplicationUserAccountId == authAccountId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in activeRefreshTokens)
        {
            token.RevokedAt = now;
            revoked++;
        }

        return revoked;
    }

    private PasswordPolicyService GetPasswordPolicyService()
        => passwordPolicyService ?? throw new InvalidOperationException("Password policy service is not configured.");

    /// <summary>Load the effective permission set for an admin user.</summary>
    private async Task<HashSet<string>> GetEffectivePermissionsAsync(string adminId, CancellationToken ct)
    {
        var grants = await db.AdminPermissionGrants
            .AsNoTracking()
            .Where(g => g.AdminUserId == adminId)
            .Select(g => g.Permission)
            .ToListAsync(ct);
        var perms = new HashSet<string>(grants, StringComparer.OrdinalIgnoreCase);
        // Preserve the legacy implicit system-admin behavior only for accounts
        // that predate the explicit AdminUser role catalog. A role-managed
        // account marked unassigned must remain empty after revocation.
        var catalogRole = await db.AdminUsers
            .AsNoTracking()
            .Where(user => user.Id == adminId)
            .Select(user => user.Role)
            .SingleOrDefaultAsync(ct);
        if (perms.Count == 0 && !string.Equals(catalogRole, "unassigned", StringComparison.OrdinalIgnoreCase))
            perms.Add(AdminPermissions.SystemAdmin);
        return perms;
    }

    private static bool CanResendInvite(ApplicationUserAccount? authAccount, string status)
        => authAccount is not null
           && status is not DeletedUserStatus
           && authAccount.LastLoginAt is null;

    private Task NotifyAdminsAsync(
        NotificationEventKey eventKey,
        string entityType,
        string entityId,
        string versionOrDateBucket,
        string message,
        CancellationToken ct)
        => notifications.CreateForAdminsAsync(
            eventKey,
            entityType,
            entityId,
            versionOrDateBucket,
            new Dictionary<string, object?>
            {
                ["message"] = message
            },
            ct);

    private sealed record AdminUserTarget(
        string Id,
        string Role,
        string Email,
        string Name,
        string Status,
        string? AuthAccountId,
        string? ProfessionId);

    private sealed record AdminUserListRow(
        string Id,
        string Name,
        string Email,
        string Role,
        string Status,
        string? AuthAccountId,
        DateTimeOffset? LastLogin,
        DateTimeOffset? CreatedAt,
        string? Profession);

    private static string NormalizeStoredUserStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is ActiveUserStatus or SuspendedUserStatus or DeletedUserStatus
            ? normalized
            : ActiveUserStatus;
    }

    private static string ResolveUserStatus(string? storedStatus, bool isDeleted)
        => isDeleted ? DeletedUserStatus : NormalizeStoredUserStatus(storedStatus);

    /// <summary>Resolves an admin `userId` (learner/expert/admin-account) to
    /// the auth-account id security data is keyed on, for
    /// <c>AdminSecurityService</c> — thin public wrapper around
    /// <see cref="ResolveUserTargetAsync"/> so that service doesn't duplicate
    /// the three-way learner/expert/admin resolution.</summary>
    public async Task<string?> ResolveAuthAccountIdAsync(string userId, CancellationToken ct)
        => (await ResolveUserTargetAsync(userId, ct)).AuthAccountId;

    private async Task<AdminUserTarget> ResolveUserTargetAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var learnerAuthAccount = learner.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == learner.AuthAccountId, ct)
                : null;
            return new AdminUserTarget(
                learner.Id,
                learner.Role,
                learner.Email,
                learner.DisplayName,
                ResolveUserStatus(learner.AccountStatus, learnerAuthAccount?.DeletedAt is not null),
                learner.AuthAccountId,
                learner.ActiveProfessionId);
        }

        var expert = await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var expertAuthAccount = expert.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == expert.AuthAccountId, ct)
                : null;
            var specialties = JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>());
            return new AdminUserTarget(
                expert.Id,
                expert.Role,
                expert.Email,
                expert.DisplayName,
                ResolveUserStatus(expert.IsActive ? ActiveUserStatus : SuspendedUserStatus, expertAuthAccount?.DeletedAt is not null),
                expert.AuthAccountId,
                specialties.FirstOrDefault());
        }

        var adminAccount = await db.ApplicationUserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == userId && a.Role == ApplicationUserRoles.Admin, ct);
        if (adminAccount is not null)
        {
            return new AdminUserTarget(
                adminAccount.Id,
                adminAccount.Role,
                adminAccount.Email,
                adminAccount.Email,
                ResolveUserStatus(ActiveUserStatus, adminAccount.DeletedAt is not null),
                adminAccount.Id,
                null);
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    private static string GenerateAccountId(string role)
    {
        var value = $"auth_{role}_{Guid.NewGuid():N}";
        return value[..Math.Min(64, value.Length)];
    }

    private static string GenerateDomainId(string prefix)
    {
        var value = $"{prefix}-{Guid.NewGuid():N}";
        return value[..Math.Min(64, value.Length)];
    }

    private static string EscapeCsv(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{normalized}\"";
    }

    /// <summary>Allowed storefront groups for an <c>ai_package</c> add-on.</summary>
    private static readonly HashSet<string> AiPackageGroups =
        new(StringComparer.OrdinalIgnoreCase) { "full", "listening", "reading", "writing", "speaking", "mock" };

    private async Task<List<TItem>> ToOrderedListDescendingAsync<TItem, TKey>(
        IQueryable<TItem> query,
        Expression<Func<TItem, TKey>> orderBy,
        CancellationToken ct,
        int? skip = null,
        int? take = null)
    {
        if (!db.Database.IsSqlite())
        {
            IQueryable<TItem> orderedQuery = query.OrderByDescending(orderBy);
            if (skip is int skipCount)
            {
                orderedQuery = orderedQuery.Skip(skipCount);
            }

            if (take is int takeCount)
            {
                orderedQuery = orderedQuery.Take(takeCount);
            }

            return await orderedQuery.ToListAsync(ct);
        }

        IEnumerable<TItem> orderedItems = (await query.ToListAsync(ct))
            .OrderByDescending(orderBy.Compile());

        if (skip is int skipLimit)
        {
            orderedItems = orderedItems.Skip(skipLimit);
        }

        if (take is int takeLimit)
        {
            orderedItems = orderedItems.Take(takeLimit);
        }

        return orderedItems.ToList();
    }

    private async Task<DateTimeOffset?> MaxDateTimeOffsetAsync<TItem>(
        IQueryable<TItem> query,
        Expression<Func<TItem, DateTimeOffset?>> selector,
        CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
        {
            return await query.MaxAsync(selector, ct);
        }

        return (await query.ToListAsync(ct))
            .Select(selector.Compile())
            .Max();
    }

    private sealed record BillingCatalogVersionMetadata(int VersionCount, int? ActiveVersionNumber, int? LatestVersionNumber);

    private static readonly BillingCatalogVersionMetadata EmptyBillingCatalogVersionMetadata = new(0, null, null);

    // ── Bulk User Import ─────────────────────────────────

    private const int MaxImportFileBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxImportRows = 1000;
    private static readonly HashSet<string> ValidImportRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationUserRoles.Learner,
        ApplicationUserRoles.Expert,
        ApplicationUserRoles.Admin
    };
    private static readonly EmailAddressAttribute EmailValidator = new();
}
