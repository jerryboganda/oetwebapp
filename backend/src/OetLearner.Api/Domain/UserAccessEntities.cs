using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Per-user override of an admin-togglable subscription module
/// (see <see cref="Services.Entitlements.ModuleKeys"/>). This is the per-USER
/// layer on top of the per-PLAN <see cref="BillingPlan.DashboardModulesJson"/>:
/// an admin can explicitly ENABLE a module the plan does not grant, or DISABLE a
/// module the plan does grant, for a single learner. Resolved in
/// <see cref="Services.Entitlements.EffectiveEntitlementResolver"/>.
///
/// DISABLE is modelled as an explicit deny that survives the fail-open
/// <see cref="Services.Entitlements.EffectiveEntitlementSnapshot.IsModuleEnabled"/>
/// contract (an empty module list means "all enabled"), so a disable can never be
/// silently swallowed by shrinking the enabled set to empty.
/// </summary>
[Index(nameof(UserId), nameof(ModuleKey), IsUnique = true)]
public class UserModuleOverride
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>References <see cref="LearnerUser.Id"/>.</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>One of <see cref="Services.Entitlements.ModuleKeys"/> (PascalCase).</summary>
    [MaxLength(32)]
    public string ModuleKey { get; set; } = default!;

    /// <summary>true = force-enable, false = force-disable for this learner.</summary>
    public bool Enabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Per-user allow-list of Materials folders. When a learner has ANY rows, the
/// Materials tree and download authorization are RESTRICTED to those folders
/// (plus their ancestors for navigation and descendants for content) — a
/// restriction WITHIN what the plan already grants. No rows == inherit the plan /
/// audience behaviour unchanged. Enforced in
/// <see cref="Services.Content.MaterialAccessService"/>.
/// </summary>
[Index(nameof(UserId), nameof(FolderId), IsUnique = true)]
public class UserMaterialFolderAccess
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>References <see cref="LearnerUser.Id"/>.</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>References <see cref="MaterialFolder.Id"/> (e.g. <c>mfd_*</c>).</summary>
    [MaxLength(64)]
    public string FolderId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Per-user allow-list of Recall sets. When a learner has ANY rows, recall /
/// vocabulary queries are RESTRICTED to those set codes. No rows == inherit
/// (all sets the module grants). Enforced in
/// <see cref="Services.VocabularyService"/>.
/// </summary>
[Index(nameof(UserId), nameof(RecallSetCode), IsUnique = true)]
public class UserRecallSetAccess
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>References <see cref="LearnerUser.Id"/>.</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    /// <summary>References <see cref="RecallSetTag.Code"/> (lowercase).</summary>
    [MaxLength(64)]
    public string RecallSetCode { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}
