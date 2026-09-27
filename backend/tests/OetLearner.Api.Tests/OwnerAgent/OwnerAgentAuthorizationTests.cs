using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// Authorization matrix for /v1/owner-agent (CONTRACT.md §5) and the hub:
/// learner / content_author / non-owner system_admin / owner-without-unlock ⇒ 403;
/// owner + unlock ⇒ 200; kill switch off ⇒ 503; /me never leaks to non-owners.
/// </summary>
public sealed class OwnerAgentAuthorizationTests
{
    public static IEnumerable<object[]> ForbiddenIdentities()
    {
        yield return ["learner", ApplicationUserRoles.Learner, (string?)null];
        yield return ["content-author", ApplicationUserRoles.Admin, $"{AdminPermissions.ContentRead},{AdminPermissions.ContentWrite}"];
        yield return ["non-owner-system-admin", ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin];
        yield return ["expert", ApplicationUserRoles.Expert, (string?)null];
    }

    [Theory]
    [MemberData(nameof(ForbiddenIdentities))]
    public async Task NonOwners_AreForbidden_EvenWhenTheConsoleIsOn(string label, string role, string? permissions)
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        using var client = factory.CreateDevClient($"auth_{label}_{Guid.NewGuid():N}", role, permissions);

        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Get, "/v1/owner-agent/status"),
                     (HttpMethod.Post, "/v1/owner-agent/unlock"),
                     (HttpMethod.Post, "/v1/owner-agent/kill-switch"),
                     (HttpMethod.Get, "/v1/owner-agent/audit"),
                 })
        {
            using var request = new HttpRequestMessage(method, url);
            if (method == HttpMethod.Post)
            {
                request.Content = JsonContent.Create(new { password = "x", code = "123456" });
            }

            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{label} {method} {url} => {(int)response.StatusCode}");
        }

        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task NonOwnerSystemAdmin_WithAForgedLookingTicket_IsStillForbidden()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var ownerClient = factory.CreateBearerClient(owner.AccessToken);
        var ownerTicket = await OwnerAgentWebApplicationFactory.UnlockAsync(ownerClient, owner);

        // Another system_admin replays the owner's ticket from their own session.
        await SeedAdminAccountAsync(factory, "auth_other_system_admin");
        var (_, otherToken) = await factory.MintSessionAsync("auth_other_system_admin", "auth_other_system_admin@example.test",
            ApplicationUserRoles.Admin, [AdminPermissions.SystemAdmin]);
        using var otherClient = factory.CreateBearerClient(otherToken);
        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ownerTicket);

        var response = await otherClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_WithoutUnlock_IsForbidden_WithUnlockRequiredCode()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var response = await client.GetAsync("/v1/owner-agent/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRequired, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task Owner_ViaDevAuthWithoutSessionFamily_CannotUnlock()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        await factory.SeedOwnerAsync();
        using var client = factory.CreateDevClient(OwnerAgentWebApplicationFactory.OwnerAccountId, ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin);

        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = OwnerAgentWebApplicationFactory.OwnerPassword, code = "123456" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("owner_agent_session_family_required", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Fact]
    public async Task Owner_WithUnlock_Gets200_AndTheSidecarIsCalled()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var statusResponse = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using (var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("test", document.RootElement.GetProperty("version").GetString());
        }

        using var audit = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/audit?take=10", ticket);
        var auditResponse = await client.SendAsync(audit);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        Assert.Equal("intact", auditResponse.Headers.GetValues("X-Owner-Agent-Audit-Chain").Single());
        using (var document = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync()))
        {
            // { items, chainIntact } — the shape lib/owner-agent/api.ts reads.
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.True(document.RootElement.GetProperty("chainIntact").GetBoolean());
            var items = document.RootElement.GetProperty("items");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);
            var unlockRow = Assert.Single(items.EnumerateArray(),
                item => item.GetProperty("action").GetString() == "unlock");
            Assert.Equal("OwnerAgent", unlockRow.GetProperty("resourceType").GetString());
            Assert.True(unlockRow.GetProperty("hashValid").GetBoolean());
            var details = unlockRow.GetProperty("details");
            Assert.Equal(JsonValueKind.Object, details.ValueKind);
            Assert.True(details.TryGetProperty("ticketId", out _));
        }

        var sidecarCall = Assert.Single(factory.Sidecar.Requests);
        Assert.Equal("/v1/status", sidecarCall.PathAndQuery);
    }

    [Fact]
    public async Task Unlock_SurvivesAccessTokenRefresh_ButDiesWhenTheSessionFamilyIsRevoked()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        // "Refresh": a new access token in the SAME refresh-token family.
        var (_, refreshedToken) = await factory.MintSessionAsync(owner.AccountId, owner.Email, ApplicationUserRoles.Admin,
            [AdminPermissions.SystemAdmin], owner.FamilyId);
        using var refreshedClient = factory.CreateBearerClient(refreshedToken);
        using (var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket))
        {
            Assert.Equal(HttpStatusCode.OK, (await refreshedClient.SendAsync(request)).StatusCode);
        }

        // A different session of the same owner cannot reuse the ticket.
        var (_, otherSessionToken) = await factory.MintSessionAsync(owner.AccountId, owner.Email, ApplicationUserRoles.Admin,
            [AdminPermissions.SystemAdmin]);
        using var otherSessionClient = factory.CreateBearerClient(otherSessionToken);
        using (var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket))
        {
            var response = await otherSessionClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(OwnerAgentFailureCodes.UnlockSessionMismatch, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        }

        // Session revoked (sign-out / admin revoke): the JWT pipeline rejects the token outright.
        await factory.RevokeFamilyAsync(owner.FamilyId);
        using (var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket))
        {
            var response = await refreshedClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Lock_RevokesTheTicket()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        using (var lockRequest = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/lock", ticket))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(lockRequest)).StatusCode);
        }

        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var response = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Fact]
    public async Task UnlockRefresh_ReturnsANewWorkingTicket_WithTheSameAbsoluteExpiry()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var unlockResponse = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new
        {
            password = owner.Password,
            code = TestWebApplicationFactoryCodes.Now(owner),
        });
        using var unlockDocument = JsonDocument.Parse(await unlockResponse.Content.ReadAsStringAsync());
        var ticket = unlockDocument.RootElement.GetProperty("ticket").GetString()!;
        var absolute = unlockDocument.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset();

        using var refresh = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/unlock/refresh", ticket);
        var refreshResponse = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        using var refreshDocument = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        Assert.Equal(absolute, refreshDocument.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset());

        var newTicket = refreshDocument.RootElement.GetProperty("ticket").GetString()!;
        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", newTicket);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(status)).StatusCode);
    }

    [Fact]
    public async Task KillSwitchFlagOff_Returns503_ForTheOwner()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        await factory.SetFeatureFlagAsync(false);

        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var response = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("owner_agent_disabled", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.DoesNotContain(factory.Sidecar.Requests, r => r.PathAndQuery == "/v1/status");
    }

    [Fact]
    public async Task MissingFlagRow_FailsClosed()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SeedOwnerAsync();
        using var client = factory.CreateDevClient(OwnerAgentWebApplicationFactory.OwnerAccountId, ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin);

        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = "x", code = "123456" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task EnvSwitchOff_Returns503_EvenWithTheFlagOn()
    {
        await using var factory = new OwnerAgentWebApplicationFactory { OwnerAgentEnabled = false };
        await factory.SetFeatureFlagAsync(true);
        await factory.SeedOwnerAsync();
        using var client = factory.CreateDevClient(OwnerAgentWebApplicationFactory.OwnerAccountId, ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin);

        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = "x", code = "123456" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("owner_agent_disabled", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Fact]
    public async Task Me_TellsNonOwnersNothing_AndOwnersTheirState()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        using var admin = factory.CreateDevClient("auth_plain_admin", ApplicationUserRoles.Admin, AdminPermissions.SystemAdmin);

        var nonOwner = await admin.GetFromJsonAsync<JsonElement>("/v1/owner-agent/me");
        Assert.False(nonOwner.GetProperty("isOwner").GetBoolean());
        Assert.False(nonOwner.GetProperty("featureEnabled").GetBoolean());

        var owner = await factory.SeedOwnerAsync();
        using var ownerClient = factory.CreateBearerClient(owner.AccessToken);
        var locked = await ownerClient.GetFromJsonAsync<JsonElement>("/v1/owner-agent/me");
        Assert.True(locked.GetProperty("isOwner").GetBoolean());
        Assert.False(locked.GetProperty("unlocked").GetBoolean());
        Assert.True(locked.GetProperty("featureEnabled").GetBoolean());

        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(ownerClient, owner);
        using var me = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/me", ticket);
        var unlockedResponse = await ownerClient.SendAsync(me);
        var unlockedBody = await unlockedResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(unlockedBody);
        Assert.True(document.RootElement.GetProperty("unlocked").GetBoolean());
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("unlockExpiresAt").ValueKind);
        Assert.DoesNotContain(ticket, unlockedBody, StringComparison.Ordinal);
    }

    // ── Hub ─────────────────────────────────────────────────────────────────

    private static HubConnection CreateHubConnection(OwnerAgentWebApplicationFactory factory, string accessToken, string? ticket, bool asCookie = false)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress!, "/v1/owner-agent/hub"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (ticket is not null && asCookie)
                {
                    // What the browser sends through the Next proxy: only the HttpOnly cookie.
                    options.Headers["Cookie"] = $"{OwnerAgentUnlockCookie.Name}={ticket}";
                }
                else if (ticket is not null)
                {
                    options.Headers[OwnerAgentHeaders.Unlock] = ticket;
                }
            })
            .Build();

    [Fact]
    public async Task Hub_WithoutUnlockHeader_RefusesToConnect()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();

        await using var connection = CreateHubConnection(factory, owner.AccessToken, ticket: null);
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task Hub_NonOwnerWithTheOwnersTicket_RefusesToConnect()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var ownerClient = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(ownerClient, owner);

        await SeedAdminAccountAsync(factory, "auth_hub_other_admin");
        var (_, otherToken) = await factory.MintSessionAsync("auth_hub_other_admin", "hub-other@example.test",
            ApplicationUserRoles.Admin, [AdminPermissions.SystemAdmin]);

        await using var connection = CreateHubConnection(factory, otherToken, ticket);
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hub_OwnerWithUnlock_StreamsTheSidecarEvents(bool asCookie)
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        await using var connection = CreateHubConnection(factory, owner.AccessToken, ticket, asCookie);
        await connection.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var types = new List<string>();
        await foreach (var element in connection.StreamAsync<JsonElement>("Stream", FakeSidecarHandler.SessionId, 0L, timeout.Token))
        {
            types.Add(element.GetProperty("type").GetString()!);
        }

        Assert.Equal(new[] { "turn_started", "heartbeat", "text_delta", "turn_complete" }, types);
        var streamCall = Assert.Single(factory.Sidecar.Requests, r => r.PathAndQuery.Contains("/events", StringComparison.Ordinal));
        Assert.Equal($"/v1/sessions/{FakeSidecarHandler.SessionId}/events?after=0", streamCall.PathAndQuery);
        Assert.Equal("text/event-stream", streamCall.Headers["Accept"]);
    }

    [Fact]
    public async Task Hub_RejectsNonUlidSessionIds_WithoutCallingTheSidecar()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        await using var connection = CreateHubConnection(factory, owner.AccessToken, ticket);
        await connection.StartAsync();

        await Assert.ThrowsAsync<HubException>(async () =>
        {
            await foreach (var _ in connection.StreamAsync<JsonElement>("Stream", "../admin/stop-all", 0L))
            {
            }
        });
        Assert.DoesNotContain(factory.Sidecar.Requests, r => r.PathAndQuery.Contains("/events", StringComparison.Ordinal));
    }

    private static async Task SeedAdminAccountAsync(OwnerAgentWebApplicationFactory factory, string accountId)
        => await factory.EnsureAuthAccountAsync(accountId, ApplicationUserRoles.Admin, $"{accountId}@example.test", [AdminPermissions.SystemAdmin]);
}
