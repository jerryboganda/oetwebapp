using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests;

public sealed class AdminRoleCatalogTests
{
    [Fact]
    public void Built_in_role_ids_and_permissions_are_unique_and_known()
    {
        var roles = AdminRoleCatalog.BuiltInRoles;

        Assert.Equal(roles.Count, roles.Select(role => role.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(roles, role =>
        {
            Assert.False(string.IsNullOrWhiteSpace(role.Id));
            Assert.NotEmpty(role.Permissions);
            Assert.All(role.Permissions, permission => Assert.Contains(permission, AdminPermissions.All));
        });
    }

    [Fact]
    public void V11_roles_are_scoped_without_candidate_result_or_billing_access()
    {
        var contentAuthor = AdminRoleCatalog.Find(AdminRoleCatalog.ContentAuthor);
        var clinicalReviewer = AdminRoleCatalog.Find(AdminRoleCatalog.ClinicalReviewer);
        var languageAssessor = AdminRoleCatalog.Find(AdminRoleCatalog.LanguageAssessor);
        var support = AdminRoleCatalog.Find(AdminRoleCatalog.CustomerSupport);
        Assert.NotNull(contentAuthor);
        Assert.NotNull(clinicalReviewer);
        Assert.NotNull(languageAssessor);
        Assert.NotNull(support);

        Assert.Equal(
            [AdminPermissions.ContentRead, AdminPermissions.ContentWrite],
            contentAuthor!.Permissions);
        Assert.Equal(
            [AdminPermissions.ContentRead, AdminPermissions.ContentEditorReview],
            clinicalReviewer!.Permissions);
        Assert.Equal(
            [AdminPermissions.ContentRead, AdminPermissions.ContentEditorReview],
            languageAssessor!.Permissions);
        Assert.Equal(
            [AdminPermissions.CustomerSupportRead, AdminPermissions.CustomerSupportWrite],
            support!.Permissions);

        Assert.DoesNotContain(AdminPermissions.AssessmentResultsRead, contentAuthor.Permissions);
        Assert.DoesNotContain(AdminPermissions.AssessmentResultsWrite, clinicalReviewer.Permissions);
        Assert.DoesNotContain(AdminPermissions.BillingRead, languageAssessor.Permissions);
    }
}
