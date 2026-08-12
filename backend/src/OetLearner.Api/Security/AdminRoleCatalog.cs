using OetLearner.Api.Domain;

namespace OetLearner.Api.Security;

/// <summary>
/// Immutable built-in admin role presets. The role list is deliberately kept
/// next to the permission constants so the admin UI and role-assignment API
/// cannot drift from the permissions enforced by authentication policies.
/// </summary>
public sealed record AdminRoleDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions);

public static class AdminRoleCatalog
{
    public const string SystemAdmin = "system_admin";
    public const string ContentAuthor = "content_author";
    public const string ContentEditor = "content_editor";
    public const string ClinicalReviewer = "clinical_reviewer";
    public const string LanguageAssessor = "language_assessor";
    public const string Reviewer = "reviewer";
    public const string BillingAdmin = "billing_admin";
    public const string CustomerSupport = "customer_support";
    public const string RefundSpecialist = "refund_specialist";
    public const string CatalogEditor = "catalog_editor";
    public const string SubscriptionManager = "subscription_manager";

    private static readonly IReadOnlyList<AdminRoleDefinition> BuiltInRoleList =
    [
        new(SystemAdmin, "System Admin", "Full system access", AdminPermissions.All.ToArray()),
        new(ContentAuthor, "Content Author", "Question, answer-key, and explicit-variant authoring without candidate-result access", [AdminPermissions.ContentRead, AdminPermissions.ContentWrite]),
        // Retained for existing assignments; new v1.1 assignments should use
        // ContentAuthor so the role name matches the specification.
        new(ContentEditor, "Content Editor", "Content read/write access", [AdminPermissions.ContentRead, AdminPermissions.ContentWrite]),
        new(ClinicalReviewer, "Clinical Reviewer", "Clinical content review without candidate-result access", [AdminPermissions.ContentRead, AdminPermissions.ContentEditorReview]),
        new(LanguageAssessor, "Language Assessor", "Language-quality content review without candidate-result access", [AdminPermissions.ContentRead, AdminPermissions.ContentEditorReview]),
        new(Reviewer, "Reviewer", "Review operations access", [AdminPermissions.ContentRead, AdminPermissions.ReviewOps]),
        new(BillingAdmin, "Billing Admin", "Full billing management (legacy superset)", [AdminPermissions.BillingRead, AdminPermissions.BillingWrite]),
        new(CustomerSupport, "Customer Support", "Ticket-linked, time-limited candidate support access", [AdminPermissions.CustomerSupportRead, AdminPermissions.CustomerSupportWrite]),
        new(RefundSpecialist, "Refund Specialist", "Read billing data and issue refunds / handle disputes only", [AdminPermissions.BillingRead, AdminPermissions.BillingRefundWrite]),
        new(CatalogEditor, "Catalog Editor", "Read billing data and edit plans, add-ons, coupons, wallet tiers, free-tier, score-guarantee", [AdminPermissions.BillingRead, AdminPermissions.BillingCatalogWrite]),
        new(SubscriptionManager, "Subscription Manager", "Read billing data and manage subscriptions + wallet spend only", [AdminPermissions.BillingRead, AdminPermissions.BillingSubscriptionWrite])
    ];

    public static IReadOnlyList<AdminRoleDefinition> BuiltInRoles => BuiltInRoleList;

    public static IReadOnlySet<string> BuiltInRoleIds { get; } =
        BuiltInRoleList.Select(role => role.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static AdminRoleDefinition? Find(string roleId)
        => BuiltInRoleList.FirstOrDefault(role =>
            string.Equals(role.Id, roleId, StringComparison.OrdinalIgnoreCase));
}
