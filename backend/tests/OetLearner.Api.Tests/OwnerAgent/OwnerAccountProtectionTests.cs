using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// Owner-account hardening: another admin (even a system_admin) cannot set/reset the
/// owner's password, clear its lockout, or change its permissions. The owner can act
/// on their own account, and non-owner accounts are unaffected.
/// </summary>
public sealed class OwnerAccountProtectionTests
{
    private const string OtherAdminId = "auth_owner_protection_other_admin";
    private const string PlainAdminId = "auth_owner_protection_plain_admin";

    private static async Task<OwnerAgentWebApplicationFactory> CreateAsync()
    {
        var factory = new OwnerAgentWebApplicationFactory();
        await factory.SeedOwnerAsync();
        await factory.EnsureAuthAccountAsync(OtherAdminId, ApplicationUserRoles.Admin, "other-admin@example.test", [AdminPermissions.SystemAdmin]);
        await factory.EnsureAuthAccountAsync(PlainAdminId, ApplicationUserRoles.Admin, "plain-admin@example.test", [AdminPermissions.ContentRead]);
        return factory;
    }

    private static HttpClient OtherAdmin(OwnerAgentWebApplicationFactory factory)
        => factory.CreateDevClient(OtherAdminId, ApplicationUserRoles.Admin,
            $"{AdminPermissions.SystemAdmin},{AdminPermissions.UsersWrite}");

    [Fact]
    public async Task OtherAdmin_CannotSetTheOwnersPassword()
    {
        await using var factory = await CreateAsync();
        using var client = OtherAdmin(factory);
        string? hashBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            hashBefore = (await scope.ServiceProvider.GetRequiredService<LearnerDbContext>().ApplicationUserAccounts
                .AsNoTracking().SingleAsync(a => a.Id == OwnerAgentWebApplicationFactory.OwnerAccountId)).PasswordHash;
        }

        var response = await client.PostAsJsonAsync(
            $"/v1/admin/users/{OwnerAgentWebApplicationFactory.OwnerAccountId}/password",
            new { password = "Brand-New-Passw0rd!-Test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentAccountProtection.ErrorCode, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));

        await using var verify = factory.Services.CreateAsyncScope();
        var after = await verify.ServiceProvider.GetRequiredService<LearnerDbContext>().ApplicationUserAccounts
            .AsNoTracking().SingleAsync(a => a.Id == OwnerAgentWebApplicationFactory.OwnerAccountId);
        Assert.Equal(hashBefore, after.PasswordHash);
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("unlock")]
    public async Task OtherAdmin_CannotResetOrUnlockTheOwner(string action)
    {
        await using var factory = await CreateAsync();
        using var client = OtherAdmin(factory);

        var response = await client.PostAsync($"/v1/admin/users/{OwnerAgentWebApplicationFactory.OwnerAccountId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentAccountProtection.ErrorCode, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Fact]
    public async Task OtherAdmin_CannotChangeTheOwnersPermissions()
    {
        await using var factory = await CreateAsync();
        using var client = OtherAdmin(factory);

        var response = await client.PutAsJsonAsync(
            $"/v1/admin/permissions/{OwnerAgentWebApplicationFactory.OwnerAccountId}",
            new { permissions = new[] { AdminPermissions.ContentRead } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentAccountProtection.ErrorCode, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task OtherAdmin_CannotAssignOrRemoveTheOwnersAdminRole(string method)
    {
        await using var factory = await CreateAsync();
        using var client = OtherAdmin(factory);
        int grantsBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            grantsBefore = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>().AdminPermissionGrants
                .CountAsync(grant => grant.AdminUserId == OwnerAgentWebApplicationFactory.OwnerAccountId);
        }

        using var request = new HttpRequestMessage(new HttpMethod(method),
            $"/v1/admin/roles/{AdminRoleCatalog.ContentAuthor}/users/{OwnerAgentWebApplicationFactory.OwnerAccountId}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentAccountProtection.ErrorCode, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));

        await using var verify = factory.Services.CreateAsyncScope();
        var grantsAfter = await verify.ServiceProvider.GetRequiredService<LearnerDbContext>().AdminPermissionGrants
            .CountAsync(grant => grant.AdminUserId == OwnerAgentWebApplicationFactory.OwnerAccountId);
        Assert.Equal(grantsBefore, grantsAfter);
    }

    [Fact]
    public async Task Owner_CanManageTheirOwnPermissions()
    {
        await using var factory = await CreateAsync();
        using var client = factory.CreateDevClient(OwnerAgentWebApplicationFactory.OwnerAccountId, ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin);

        var response = await client.PutAsJsonAsync(
            $"/v1/admin/permissions/{OwnerAgentWebApplicationFactory.OwnerAccountId}",
            new { permissions = new[] { AdminPermissions.SystemAdmin } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonOwnerTargets_AreUnaffected()
    {
        await using var factory = await CreateAsync();
        using var client = OtherAdmin(factory);

        var response = await client.PutAsJsonAsync(
            $"/v1/admin/permissions/{PlainAdminId}",
            new { permissions = new[] { AdminPermissions.ContentRead, AdminPermissions.ContentWrite } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Protection_IsIndependentOfTheConsoleSwitch_AndInertWithoutAnAllowList()
    {
        var disabledButListed = new OwnerAgentOptions { Enabled = false, OwnerAccountIds = "owner-a" };
        var ex = Assert.Throws<ApiException>(() =>
            OwnerAgentAccountProtection.EnsureMutationAllowed(disabledButListed, "someone-else", "owner-a", "set the password"));
        Assert.Equal(OwnerAgentAccountProtection.ErrorCode, ex.ErrorCode);

        OwnerAgentAccountProtection.EnsureMutationAllowed(disabledButListed, "owner-a", "owner-a", "set the password");
        OwnerAgentAccountProtection.EnsureMutationAllowed(new OwnerAgentOptions(), "someone-else", "owner-a", "set the password");
        OwnerAgentAccountProtection.EnsureMutationAllowed(null, "someone-else", "owner-a", "set the password");
    }
}
