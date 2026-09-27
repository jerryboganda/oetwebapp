using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// The API ⇄ sidecar relay: fresh requests with only the internal headers, id validation
/// before URL building, step-up enforcement, secret hygiene, failure mapping.
/// </summary>
public sealed class OwnerAgentRelayTests
{
    private static readonly HashSet<string> AllowedSidecarHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        OwnerAgentClient.InternalTokenHeader,
        OwnerAgentClient.OwnerAccountHeader,
        OwnerAgentClient.RequestIdHeader,
        "Accept",
    };

    private static async Task<(OwnerAgentWebApplicationFactory Factory, OwnerSeed Owner, HttpClient Client, string Ticket)> UnlockedAsync()
    {
        var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);
        return (factory, owner, client, ticket);
    }

    [Fact]
    public async Task Relay_NeverForwardsBrowserHeaders()
    {
        var (factory, owner, client, ticket) = await UnlockedAsync();
        await using var _ = factory;
        using var __ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post,
            $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/messages", ticket, new { text = "hello agent" });
        request.Headers.Add("Cookie", "oet_rt=browser-refresh-cookie; oet_csrf=browser-csrf");
        request.Headers.Add("x-csrf-token", "browser-csrf");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("Origin", "https://app.example.test");
        request.Headers.Add("X-Debug-Role", "admin");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sidecarCall = Assert.Single(factory.Sidecar.Requests);
        Assert.Equal($"/v1/sessions/{FakeSidecarHandler.SessionId}/messages", sidecarCall.PathAndQuery);
        Assert.All(sidecarCall.Headers.Keys, name => Assert.Contains(name, AllowedSidecarHeaders));
        Assert.Equal(OwnerAgentWebApplicationFactory.InternalToken, sidecarCall.Headers[OwnerAgentClient.InternalTokenHeader]);
        Assert.Equal(owner.AccountId, sidecarCall.Headers[OwnerAgentClient.OwnerAccountHeader]);
        Assert.DoesNotContain(owner.AccessToken, string.Join("|", sidecarCall.Headers.Values), StringComparison.Ordinal);
        Assert.DoesNotContain(ticket, string.Join("|", sidecarCall.Headers.Values), StringComparison.Ordinal);

        // Body is re-serialized from whitelisted fields only.
        using var body = JsonDocument.Parse(sidecarCall.Body!);
        Assert.Equal("hello agent", body.RootElement.GetProperty("text").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("not-a-ulid")]
    [InlineData("01J9ZX7Q2V3M4N5P6R7S8T9V0")]
    [InlineData("01J9ZX7Q2V3M4N5P6R7S8T9V0WX")]
    [InlineData("01J9ZX7Q2V3M4N5P6R7S8T9VIL")]
    [InlineData("..%2F..%2Fv1%2Fadmin%2Fstop-all")]
    public async Task InvalidSessionIds_Are400_AndNeverReachTheSidecar(string sessionId)
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, $"/v1/owner-agent/sessions/{sessionId}", ticket);
        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"{sessionId} => {(int)response.StatusCode}");
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task UnknownEngine_Is400_AndNeverReachesTheSidecar()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get,
            $"/v1/owner-agent/auth/gemini/flows/{FakeSidecarHandler.FlowId}", ticket);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_engine", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.Empty(factory.Sidecar.Requests);
    }

    public static IEnumerable<object[]> StepUpRoutes()
    {
        yield return ["PUT", "/v1/owner-agent/github-tokens", """{"agentToken":"agent-token-value-for-tests-only"}"""];
        yield return ["POST", "/v1/owner-agent/auth/claude/connect", null!];
        yield return ["POST", "/v1/owner-agent/auth/codex/logout", null!];
        yield return ["POST", $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/ship", """{"prTitle":"t"}"""];
        yield return ["PATCH", $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}", """{"mode":"autopilot"}"""];
        yield return ["POST", "/v1/owner-agent/sessions", """{"engine":"claude","model":"opaque-model","mode":"autopilot"}"""];
    }

    [Theory]
    [MemberData(nameof(StepUpRoutes))]
    public async Task HighRiskActions_RequireAFreshSingleUseStepUp(string method, string url, string? json)
    {
        var (factory, owner, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        HttpRequestMessage Build(string? stepUp)
        {
            var request = OwnerAgentWebApplicationFactory.Unlocked(new HttpMethod(method), url, ticket, stepUp: stepUp);
            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return request;
        }

        using (var withoutStepUp = Build(null))
        {
            var denied = await client.SendAsync(withoutStepUp);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(OwnerAgentStepUpFailureCodes.Required, await OwnerAgentWebApplicationFactory.ReadCodeAsync(denied));
        }

        Assert.Empty(factory.Sidecar.Requests);

        var stepUp = await OwnerAgentWebApplicationFactory.StepUpAsync(client, owner, ticket);
        using (var first = Build(stepUp))
        {
            var allowed = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using (var replay = Build(stepUp))
        {
            var reused = await client.SendAsync(replay);
            Assert.Equal(HttpStatusCode.Forbidden, reused.StatusCode);
            Assert.Equal(OwnerAgentStepUpFailureCodes.AlreadyUsed, await OwnerAgentWebApplicationFactory.ReadCodeAsync(reused));
        }

        Assert.Single(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task NonAutopilotModeChange_DoesNotNeedStepUp_ButIsAudited()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Patch,
            $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}", ticket, new { mode = "guarded" });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var audit = await db.AuditEvents.SingleAsync(e => e.ResourceType == OwnerAgentAuditService.ResourceType && e.Action == OwnerAgentAuditActions.ModeChanged);
        Assert.Equal(FakeSidecarHandler.SessionId, audit.ResourceId);
        Assert.Contains("\"guarded\"", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GithubTokens_AreWriteOnly_NeverEchoed_NorAudited()
    {
        var (factory, owner, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        const string agentToken = "agent-token-value-for-tests-only";
        const string shipToken = "ship-token-value-for-tests-only";

        var stepUp = await OwnerAgentWebApplicationFactory.StepUpAsync(client, owner, ticket);
        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Put, "/v1/owner-agent/github-tokens", ticket,
            new { agentToken, shipToken }, stepUp);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The fake sidecar echoes its input; the API must re-shape to GithubStatus only.
        Assert.DoesNotContain(agentToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(shipToken, body, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(body))
        {
            Assert.True(document.RootElement.GetProperty("agentTokenSet").GetBoolean());
            Assert.True(document.RootElement.GetProperty("shipTokenSet").GetBoolean());
            Assert.False(document.RootElement.TryGetProperty("echo", out _));
        }

        // The sidecar did receive them (write path works).
        var sidecarCall = Assert.Single(factory.Sidecar.Requests);
        Assert.Contains(agentToken, sidecarCall.Body, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var auditDetails = await db.AuditEvents
            .Where(e => e.ResourceType == OwnerAgentAuditService.ResourceType)
            .Select(e => e.Details)
            .ToListAsync();
        Assert.All(auditDetails, details =>
        {
            Assert.DoesNotContain(agentToken, details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(shipToken, details ?? string.Empty, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Unlock_NeverEchoesOrAuditsThePasswordOrCode()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var code = TestWebApplicationFactoryCodes.Now(owner);

        var failed = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = "wrong-" + owner.Password, code });
        var failedBody = await failed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        Assert.DoesNotContain(owner.Password, failedBody, StringComparison.Ordinal);

        var ok = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = owner.Password, code = TestWebApplicationFactoryCodes.Next(owner) });
        var okBody = await ok.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.DoesNotContain(owner.Password, okBody, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rows = await db.AuditEvents.Where(e => e.ResourceType == OwnerAgentAuditService.ResourceType).ToListAsync();
        Assert.Contains(rows, r => r.Action == OwnerAgentAuditActions.UnlockFailed);
        Assert.Contains(rows, r => r.Action == OwnerAgentAuditActions.Unlock);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain(owner.Password, row.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(code, row.Details ?? string.Empty, StringComparison.Ordinal);
        });
        var securityDetails = await db.SecurityEvents.Select(e => e.DetailsJson).ToListAsync();
        Assert.All(securityDetails, details => Assert.DoesNotContain(owner.Password, details ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SidecarRejectingOurCredentials_Is502_AndNeverLeaksTheInternalToken()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        factory.Sidecar.Responder = _ => FakeSidecarHandler.Json(HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"bad_token\",\"message\":\"got " + OwnerAgentWebApplicationFactory.InternalToken + "\"}}");

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(OwnerAgentSidecarException.Rejected, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.DoesNotContain(OwnerAgentWebApplicationFactory.InternalToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SidecarServerError_Is502_WithoutTheUpstreamBody()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        factory.Sidecar.Responder = _ => FakeSidecarHandler.Json(HttpStatusCode.InternalServerError,
            """{"error":{"code":"boom","message":"stack trace with /var/lib/oet-agent paths"}}""");

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("/var/lib/oet-agent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SidecarClientErrors_PassThrough_WithTheContractErrorShape()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        factory.Sidecar.Responder = _ => FakeSidecarHandler.Json((HttpStatusCode)423,
            """{"error":{"code":"draining","message":"The console is draining."}}""");

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post,
            $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/interrupt", ticket);
        var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("draining", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        // Also flat, so the app's shared API client (lib/api/client.ts) reads code + message.
        Assert.Equal("draining", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("The console is draining.", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Lease_IsClampedToThreeMinutesAndTheUnlock()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/lease", ticket,
            new { expiresAt = DateTimeOffset.UtcNow.AddHours(5) });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var sidecarCall = Assert.Single(factory.Sidecar.Requests);
        using var body = JsonDocument.Parse(sidecarCall.Body!);
        var forwarded = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.True(forwarded <= DateTimeOffset.UtcNow.AddMinutes(3).AddSeconds(5), $"lease {forwarded:O} not clamped");
    }

    [Fact]
    public async Task KillSwitch_And_ApplyUpdate_AreRelayed_AndAudited()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using (var kill = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/kill-switch", ticket))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(kill)).StatusCode);
        }

        using (var apply = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/apply-update", ticket))
        {
            var response = await client.SendAsync(apply);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("draining").GetBoolean());
            Assert.True(document.RootElement.GetProperty("dispatched").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("activeTurns").GetInt32());
        }

        Assert.Contains(factory.Sidecar.Requests, r => r.Method == "POST" && r.PathAndQuery == "/v1/admin/stop-all");
        // The sidecar drains and dispatches agent-console.yml (the API holds no GitHub credential).
        Assert.Single(factory.Sidecar.Requests, r => r.Method == "POST" && r.PathAndQuery == "/v1/admin/apply-update");

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IOwnerAgentAuditService>();
        var page = await audit.ListAsync(50, CancellationToken.None);
        Assert.True(page.ChainIntact);
        Assert.Contains(page.Items, i => i.Action == OwnerAgentAuditActions.KillSwitch);
        Assert.Contains(page.Items, i => i.Action == OwnerAgentAuditActions.ApplyUpdate);
    }

    [Fact]
    public async Task Resume_RelaysDrainFalse_AndIsAudited()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using (var resume = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/resume", ticket))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(resume)).StatusCode);
        }

        var drain = Assert.Single(factory.Sidecar.Requests, r => r.Method == "POST" && r.PathAndQuery == "/v1/admin/drain");
        Assert.Contains("\"draining\":false", drain.Body, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IOwnerAgentAuditService>();
        var page = await audit.ListAsync(50, CancellationToken.None);
        Assert.Contains(page.Items, i => i.Action == OwnerAgentAuditActions.Resume);
    }

    [Fact]
    public async Task MessageAudit_KeepsOnlyA200CharPreview()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        var text = "start-" + new string('m', 5_000) + "-end-marker";

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post,
            $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/messages", ticket, new { text });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.AuditEvents.SingleAsync(e => e.Action == OwnerAgentAuditActions.MessageSent);
        Assert.DoesNotContain("-end-marker", row.Details, StringComparison.Ordinal);
        using var details = JsonDocument.Parse(row.Details!);
        var data = details.RootElement.GetProperty("data");
        Assert.Equal(text.Length, data.GetProperty("length").GetInt32());
        Assert.True(data.GetProperty("preview").GetString()!.Length <= 200);
    }

    // ── OwnerAgentClient in isolation ───────────────────────────────────────

    private static OwnerAgentClient CreateClient(FakeSidecarHandler handler, bool enabled = true)
    {
        var options = Options.Create(new OwnerAgentOptions
        {
            Enabled = enabled,
            BaseUrl = OwnerAgentWebApplicationFactory.SidecarBaseUrl,
            InternalToken = OwnerAgentWebApplicationFactory.InternalToken,
            OwnerAccountIds = OwnerAgentWebApplicationFactory.OwnerAccountId,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri(OwnerAgentWebApplicationFactory.SidecarBaseUrl + "/") };
        return new OwnerAgentClient(http, options, NullLogger<OwnerAgentClient>.Instance);
    }

    [Fact]
    public async Task Client_SseReader_ParsesEvents_SkipsCommentsAndGarbage()
    {
        var handler = new FakeSidecarHandler
        {
            Responder = _ => FakeSidecarHandler.Sse(
                ": keep-alive comment\n\n"
                + "id: 7\nevent: text_delta\ndata: {\"seq\":7,\"type\":\"text_delta\",\"data\":{\"text\":\"a\"}}\n\n"
                + "event: text_delta\ndata: not-json\n\n"
                + "id: 8\nevent: tool_call\ndata: {\"seq\":8,\n"
                + "data: \"type\":\"tool_call\",\"data\":{}}\n\n"),
        };
        var client = CreateClient(handler);

        var events = new List<JsonElement>();
        await foreach (var element in client.StreamEventsAsync(FakeSidecarHandler.SessionId, 6, OwnerAgentWebApplicationFactory.OwnerAccountId, "req-1", CancellationToken.None))
        {
            events.Add(element);
        }

        Assert.Equal(new long[] { 7, 8 }, events.Select(e => e.GetProperty("seq").GetInt64()).ToArray());
        Assert.Equal("tool_call", events[1].GetProperty("type").GetString());
        var call = Assert.Single(handler.Requests);
        Assert.Equal($"/v1/sessions/{FakeSidecarHandler.SessionId}/events?after=6", call.PathAndQuery);
        Assert.Equal("req-1", call.Headers[OwnerAgentClient.RequestIdHeader]);
    }

    [Fact]
    public async Task Client_RefusesWhenNotConfigured_WithoutSendingAnything()
    {
        var handler = new FakeSidecarHandler();
        var client = CreateClient(handler, enabled: false);

        var ex = await Assert.ThrowsAsync<OwnerAgentSidecarException>(() => client.SendAsync(
            HttpMethod.Get, OwnerAgentSidecarRoutes.Status, null, OwnerAgentWebApplicationFactory.OwnerAccountId, null, CancellationToken.None));

        Assert.Equal(OwnerAgentSidecarException.NotConfigured, ex.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Client_RefusesNonOwnerAccountHeader_AndUnsafeRequestIds()
    {
        var handler = new FakeSidecarHandler();
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<OwnerAgentSidecarException>(() => client.SendAsync(
            HttpMethod.Get, OwnerAgentSidecarRoutes.Status, null, "auth_not_an_owner", null, CancellationToken.None));
        Assert.Empty(handler.Requests);

        await client.SendAsync(HttpMethod.Get, OwnerAgentSidecarRoutes.Status, null,
            OwnerAgentWebApplicationFactory.OwnerAccountId, "bad id\r\nX-Injected: 1", CancellationToken.None);
        var call = Assert.Single(handler.Requests);
        Assert.False(call.Headers.ContainsKey(OwnerAgentClient.RequestIdHeader));
        Assert.False(call.Headers.ContainsKey("X-Injected"));
    }

    [Fact]
    public async Task Client_RejectsNonJsonBodies()
    {
        var handler = new FakeSidecarHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html"),
            },
        };
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<OwnerAgentSidecarException>(() => client.SendAsync(
            HttpMethod.Get, OwnerAgentSidecarRoutes.Status, null, OwnerAgentWebApplicationFactory.OwnerAccountId, null, CancellationToken.None));
        Assert.Equal(OwnerAgentSidecarException.BadResponse, ex.Code);
    }

    [Fact]
    public async Task OutboundHeaderFilter_DropsEverythingOutsideTheSidecarAllowList()
    {
        // Simulates headers that app-wide delegating handlers (e.g. Sentry trace/baggage
        // propagation) could add after OwnerAgentClient built the request.
        var sidecar = new FakeSidecarHandler();
        using var invoker = new HttpMessageInvoker(new OwnerAgentOutboundHeaderFilter(sidecar));
        using var request = new HttpRequestMessage(HttpMethod.Post, OwnerAgentWebApplicationFactory.SidecarBaseUrl + "/v1/lease")
        {
            Content = new StringContent("""{"expiresAt":null}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(OwnerAgentClient.InternalTokenHeader, OwnerAgentWebApplicationFactory.InternalToken);
        request.Headers.TryAddWithoutValidation(OwnerAgentClient.OwnerAccountHeader, OwnerAgentWebApplicationFactory.OwnerAccountId);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("sentry-trace", "0123456789abcdef0123456789abcdef-0123456789abcdef-1");
        request.Headers.TryAddWithoutValidation("baggage", "sentry-trace_id=0123,browser-supplied=value");
        request.Headers.TryAddWithoutValidation("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        request.Headers.TryAddWithoutValidation("Cookie", "oet_rt=browser-refresh-cookie");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer browser-access-token");
        request.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        var call = Assert.Single(sidecar.Requests);
        Assert.All(call.Headers.Keys, name => Assert.Contains(name, AllowedSidecarHeaders));
        Assert.Equal(OwnerAgentWebApplicationFactory.InternalToken, call.Headers[OwnerAgentClient.InternalTokenHeader]);
        Assert.Equal(OwnerAgentWebApplicationFactory.OwnerAccountId, call.Headers[OwnerAgentClient.OwnerAccountHeader]);
        Assert.False(request.Content.Headers.Contains("Content-Disposition"));
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(FakeSidecarHandler.SessionId, true)]
    [InlineData("01j9zx7q2v3m4n5p6r7s8t9v0w", true)]
    [InlineData("81J9ZX7Q2V3M4N5P6R7S8T9V0W", false)]
    [InlineData("01J9ZX7Q2V3M4N5P6R7S8T9VOU", false)]
    [InlineData("01J9ZX7Q2V3M4N5P6R7S8T9V0W/../x", false)]
    [InlineData("", false)]
    public void UlidValidation(string value, bool expected)
        => Assert.Equal(expected, OwnerAgentIds.IsUlid(value));
}
