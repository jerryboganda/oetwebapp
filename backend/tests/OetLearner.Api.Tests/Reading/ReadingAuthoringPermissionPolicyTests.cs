using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests.Reading;

public sealed class ReadingAuthoringPermissionPolicyTests
{
    [Fact]
    public void ContentAuthor_cannot_publish_or_emergency_override()
    {
        var contentAuthor = string.Join(',', AdminPermissions.ContentRead, AdminPermissions.ContentWrite);

        Assert.False(ReadingAuthoringPermissionPolicy.CanPublish(contentAuthor));
        Assert.False(ReadingAuthoringPermissionPolicy.CanEmergencyOverride(contentAuthor));
    }

    [Fact]
    public void Publisher_approval_can_publish_but_only_system_admin_can_override()
    {
        Assert.True(ReadingAuthoringPermissionPolicy.CanPublish(AdminPermissions.ContentPublisherApproval));
        Assert.False(ReadingAuthoringPermissionPolicy.CanMutatePublishedPaper(AdminPermissions.ContentPublisherApproval));
        Assert.False(ReadingAuthoringPermissionPolicy.CanEmergencyOverride(AdminPermissions.ContentPublisherApproval));
        Assert.True(ReadingAuthoringPermissionPolicy.CanEmergencyOverride(AdminPermissions.SystemAdmin));
    }

    [Fact]
    public void Content_publish_can_mutate_published_papers()
    {
        Assert.True(ReadingAuthoringPermissionPolicy.CanMutatePublishedPaper(AdminPermissions.ContentPublish));
        Assert.True(ReadingAuthoringPermissionPolicy.CanMutatePublishedPaper(AdminPermissions.SystemAdmin));
    }

    [Fact]
    public void Empty_or_malformed_permission_claims_fail_closed()
    {
        Assert.False(ReadingAuthoringPermissionPolicy.CanPublish(null));
        Assert.False(ReadingAuthoringPermissionPolicy.CanPublish("content:publisher_approval_typo"));
        Assert.False(ReadingAuthoringPermissionPolicy.CanEmergencyOverride("system_admin_typo"));
    }
}
