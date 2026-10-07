using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Per-USER override of AI Learning Companion (Sami) access — the per-learner
/// layer on top of the per-PLAN <see cref="PlanModuleOverride"/> and the plan's
/// catalog module list (<see cref="BillingPlan.DashboardModulesJson"/>).
///
/// <para>
/// It exists so an operator can ENABLE a learner whose package does not grant the
/// companion, or DISABLE one whose package does, <b>with provenance</b>: the row
/// records whether the grant was a plain admin enable or a promotional grant, when
/// a promotional grant lapses, who wrote it and why. The learner gate reads it as
/// the FIRST step (an admin act is more specific than both the plan override and
/// the catalog manifest) and reports the resulting source/reason on
/// <c>GET /v1/companion/session</c>; the operator surface is
/// <c>/v1/admin/companion/access/users/{userId}</c>.
/// </para>
///
/// <para>
/// Deliberately its OWN table rather than rows in <see cref="UserModuleOverride"/>:
/// that generic table is rewritten wholesale by the admin user-access scope save
/// (<c>UserAccessAllocationService.PutScopeAsync</c> removes every row for the user
/// and re-adds the posted set), which would silently wipe the provenance, the
/// expiry and the note — and an expiry honoured only in the companion gate would
/// still leave the module-looking grant visible to the snapshot resolver.
/// </para>
///
/// <para>
/// Rows are keyed by (<see cref="UserId"/>, <see cref="ModuleKey"/>) so the shape
/// matches the two sibling override tables; the companion gate always queries
/// <c>ModuleKeys.AiCompanion</c>, so today this table only ever carries Sami Chat
/// overrides.
/// </para>
/// </summary>
public class CompanionUserAccess
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

    /// <summary>
    /// true = force-enable the companion for this learner (wins over the package
    /// rule), false = disable it for this learner (wins over every grant).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <see cref="CompanionAccessSources.AdminEnabled"/> or
    /// <see cref="CompanionAccessSources.Promotional"/> for a grant, and
    /// <see cref="CompanionAccessSources.ManuallyDisabled"/> for a disable —
    /// stored (not derived) so the operator's intent survives an audit read.
    /// </summary>
    [MaxLength(32)]
    public string Source { get; set; } = CompanionAccessSources.AdminEnabled;

    /// <summary>
    /// When this grant lapses (UTC). Null = no expiry, which is the normal case for
    /// a support/comp enable. A lapsed grant is ignored by the gate: it can only
    /// ever stop granting, never revoke what the package already grants.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Operator note ("UAT tester", "compensation ticket #123"). Never shown to the learner.</summary>
    [MaxLength(512)]
    public string? Note { get; set; }

    /// <summary>Admin account id that last wrote this row (audit trail).</summary>
    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Provenance vocabulary for companion access, as reported on the learner session
/// (<c>access.source</c>) and the per-user admin read. Values are lowercase snake
/// case because they are wire values; the first five are the required taxonomy, the
/// sixth ("none") is the honest answer when nothing grants it.
///
/// <para>
/// Only <see cref="CompanionAccessSources.AdminEnabled"/>,
/// <see cref="CompanionAccessSources.Promotional"/> and
/// <see cref="CompanionAccessSources.ManuallyDisabled"/> are ever PERSISTED on
/// <see cref="CompanionUserAccess"/>; the rest are computed at read time.
/// </para>
/// </summary>
public static class CompanionAccessSources
{
    /// <summary>The learner's package grants it (catalog module list, or a per-plan admin override).</summary>
    public const string PackageIncluded = "package_included";

    /// <summary>A per-user admin grant with no expiry.</summary>
    public const string AdminEnabled = "admin_enabled";

    /// <summary>A per-user admin grant that carries an expiry (comp, trial, UAT access).</summary>
    public const string Promotional = "promotional";

    /// <summary>A per-user admin disable — always wins over every grant.</summary>
    public const string ManuallyDisabled = "manually_disabled";

    /// <summary>A per-user grant that lapsed and was the only thing granting access.</summary>
    public const string Expired = "expired";

    /// <summary>Nothing grants the companion: no per-user override and the package does not include it.</summary>
    public const string None = "none";

    /// <summary>True for the two grant kinds an operator may post.</summary>
    public static bool IsGrantSource(string? source)
        => string.Equals(source, AdminEnabled, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, Promotional, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a posted/stored value to a grant kind: "promotional" stays promotional,
    /// anything else (including null/blank and an unrecognised historical value)
    /// reads as a plain admin enable. Granting is never widened by an unreadable
    /// label — only the reporting label changes.
    /// </summary>
    public static string NormalizeGrantSource(string? source)
        => string.Equals(source, Promotional, StringComparison.OrdinalIgnoreCase) ? Promotional : AdminEnabled;
}

/// <summary>
/// Why the companion is or is not usable right now (<c>access.reason</c>). Kept
/// next to the entity so the learner gate, the admin read and the frontend's
/// reason map are changed together. These are wire values: never rename one
/// without updating <c>lib/api/companion.ts</c> and <c>messages/*/companion.json</c>.
/// </summary>
public static class CompanionAccessReasons
{
    public const string Ok = "ok";

    /// <summary>The platform-wide <c>ai_learning_companion</c> flag is off.</summary>
    public const string CompanionDisabled = "companion_disabled";

    /// <summary>AI is disabled for this account by an admin (<c>AiUserQuotaOverride.AiDisabled</c>).</summary>
    public const string AiDisabled = "ai_disabled";

    /// <summary>Global emergency kill switch.</summary>
    public const string KillSwitch = "kill_switch";

    /// <summary>The AI quota policy could not be read (fail closed).</summary>
    public const string PolicyUnavailable = "policy_unavailable";

    /// <summary>No package or per-user grant includes the companion.</summary>
    public const string PackageRequired = "package_required";

    /// <summary>The quota plan's allowed-feature list excludes every companion feature code.</summary>
    public const string PlanExcludesCompanion = "plan_excludes_companion";

    public const string MonthlyCapReached = "monthly_cap_reached";
    public const string DailyCapReached = "daily_cap_reached";

    /// <summary>An operator disabled the companion for this learner.</summary>
    public const string ManuallyDisabled = "manually_disabled";

    /// <summary>The learner's per-user grant lapsed and nothing else grants access.</summary>
    public const string Expired = "expired";
}
