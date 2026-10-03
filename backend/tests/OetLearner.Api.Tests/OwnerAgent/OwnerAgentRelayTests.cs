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
using OetLearner.Api.Security;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// The API ⇄ sidecar relay: fresh requests with only the internal headers, id validation
/// before URL building, one unlock (header or HttpOnly cookie) covering every action with no
/// per-action step-up, the unlock cookie lifecycle, secret hygiene, failure mapping.
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

    // ── Jev development triage is advice for the break-glass console, never a gate on it ──

    private static async Task<(OwnerAgentWebApplicationFactory Factory, HttpClient Client, string Ticket)> JevUnlockedAsync()
    {
        var factory = new OwnerAgentWebApplicationFactory { JevDevelopmentEnabled = true };
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);
        return (factory, client, ticket);
    }

    private static async Task<HttpResponseMessage> PostMessageAsync(HttpClient client, string ticket, string text)
    {
        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post,
            $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/messages", ticket,
            new { text, model = "owner-selected-model", effort = "high" });
        return await client.SendAsync(request);
    }

    private static bool IsMessagePost(CapturedSidecarRequest call)
        => call.Method == "POST" && call.PathAndQuery.EndsWith("/messages", StringComparison.Ordinal);

    private static JsonElement ForwardedMessage(OwnerAgentWebApplicationFactory factory)
    {
        var call = Assert.Single(factory.Sidecar.Requests, IsMessagePost);
        using var body = JsonDocument.Parse(call.Body!);
        return body.RootElement.Clone();
    }

    /// <summary>
    /// A well-formed Ok triage result whose Choice answers share <paramref name="confidence"/>;
    /// <paramref name="effortTier"/> (when given) is the advisory effort_tier answer.
    /// </summary>
    private static JevJudgmentResult Triage(OwnerAgentWebApplicationFactory factory, string task, string risk, double confidence, string? effortTier = null)
    {
        static JevAnswer Choice(string[] keys, string winner, double top)
        {
            var rest = (1 - top) / (keys.Length - 1);
            var probabilities = keys.ToDictionary(key => key, key => key == winner ? top : rest);
            return new JevAnswer(JevQuestionKind.Choice, null, new JevChoiceAnswer(winner, probabilities, top), null);
        }

        var answers = new Dictionary<string, JevAnswer>
        {
            ["task_kind"] = Choice(["implement", "debug", "review", "verify", "plan", "content", "other", "unclear"], task, confidence),
            ["risk_level"] = Choice(["low", "elevated", "high", "unclear"], risk, confidence),
        };
        if (effortTier is not null)
        {
            answers["effort_tier"] = Choice(["lookup", "bounded_edit", "cross_module", "unclear"], effortTier, confidence);
        }

        return new JevJudgmentResult(
            JevCallStatus.Ok,
            factory.Services.GetRequiredService<IOptions<TypeSafeOptions>>().Value.Model,
            answers,
            200, 10, null);
    }

    private static async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string ticket, string? initialMessage)
    {
        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/sessions", ticket,
            new { engine = "claude", model = "owner-selected-model", effort = "high", mode = "guarded", initialMessage });
        return await client.SendAsync(request);
    }

    private static bool IsCreatePost(CapturedSidecarRequest call)
        => call.Method == "POST" && call.PathAndQuery == "/v1/sessions";

    private static JsonElement ForwardedCreate(OwnerAgentWebApplicationFactory factory)
    {
        var call = Assert.Single(factory.Sidecar.Requests, IsCreatePost);
        using var body = JsonDocument.Parse(call.Body!);
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task Jev_Unavailable_FailsOpen_AndForwardsTheMessageWithoutAdvice()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;

        // RecordingJevJudgments defaults to Unavailable("test_unavailable"): an outage / open breaker.
        var response = await PostMessageAsync(client, ticket, "Diagnose this grading exception");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Judgments.Calls);
        var forwarded = ForwardedMessage(factory);
        Assert.Equal("Diagnose this grading exception", forwarded.GetProperty("text").GetString());
        Assert.Equal("owner-selected-model", forwarded.GetProperty("model").GetString());
        Assert.False(forwarded.TryGetProperty("jevAdvisory", out var jevAdvisoryProp));
    }

    [Fact]
    public async Task Jev_Disabled_NoKey_FailsOpen_AndForwardsTheMessageWithoutAdvice()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = JevJudgmentResult.Disabled("typesafe_key_missing");

        var response = await PostMessageAsync(client, ticket, "Diagnose this grading exception");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(ForwardedMessage(factory).TryGetProperty("jevAdvisory", out var jevAdvisoryProp));
    }

    [Fact]
    public async Task Jev_ReviewRequired_BlocksASubstantiveMessage_AndNothingIsForwarded()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "debug", "elevated", confidence: 0.55);

        var response = await PostMessageAsync(client, ticket,
            "Please look into the failing grading exception in the production API and tell me what you find.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("jev_review_required", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.DoesNotContain(factory.Sidecar.Requests, IsMessagePost);
    }

    [Fact]
    public async Task Jev_ReviewRequired_OnATerseFollowUp_IsForwardedWithoutAdvice()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "unclear", "unclear", confidence: 0.55);

        var response = await PostMessageAsync(client, ticket, "continue");

        // The real sidecar 409s any advisory that is not status "ok" / requiresHumanReview false.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("continue", ForwardedMessage(factory).GetProperty("text").GetString());
        Assert.False(ForwardedMessage(factory).TryGetProperty("jevAdvisory", out var jevAdvisoryProp));
    }

    [Theory]
    [InlineData(59, HttpStatusCode.OK)]
    [InlineData(60, HttpStatusCode.Conflict)]
    public async Task Jev_ReviewRequired_BlocksFromSixtyCharacters(int length, HttpStatusCode expected)
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "debug", "elevated", confidence: 0.55);

        var response = await PostMessageAsync(client, ticket, new string('a', length));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Jev_ConfidentTriage_IsForwardedWithTheAdviceAttached()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "debug", "elevated", confidence: 0.95, effortTier: "cross_module");

        var response = await PostMessageAsync(client, ticket, "Diagnose this grading exception");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var advisory = ForwardedMessage(factory).GetProperty("jevAdvisory");
        Assert.Equal("ok", advisory.GetProperty("status").GetString());
        Assert.False(advisory.GetProperty("requiresHumanReview").GetBoolean());
        Assert.Equal("debug", advisory.GetProperty("taskKind").GetString());
        Assert.Equal("elevated", advisory.GetProperty("riskLevel").GetString());
        Assert.Equal("cross_module", advisory.GetProperty("effortTier").GetString());
        // Triage is advice: the owner's own model / effort choice is relayed untouched.
        Assert.Equal("owner-selected-model", ForwardedMessage(factory).GetProperty("model").GetString());
        Assert.Equal("high", ForwardedMessage(factory).GetProperty("effort").GetString());
    }

    [Fact]
    public async Task Jev_UnclearEffortTier_IsForwardedWithoutATier_AndNeverBlocks()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "debug", "elevated", confidence: 0.95, effortTier: "unclear");

        var response = await PostMessageAsync(client, ticket, "Diagnose this grading exception");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var advisory = ForwardedMessage(factory).GetProperty("jevAdvisory");
        Assert.Equal("ok", advisory.GetProperty("status").GetString());
        Assert.True(!advisory.TryGetProperty("effortTier", out var tierProp) || tierProp.ValueKind == JsonValueKind.Null);
    }

    // ── The first message of a new session gets the same triage as a follow-up ──

    [Fact]
    public async Task Jev_Off_FirstMessage_IsRelayedUntouched_WithNoJevCallOrAuditKeys()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        var response = await PostCreateAsync(client, ticket, "Diagnose this grading exception in the production API");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.Judgments.Calls);
        var forwarded = ForwardedCreate(factory);
        Assert.Equal("Diagnose this grading exception in the production API", forwarded.GetProperty("initialMessage").GetString());
        Assert.False(forwarded.TryGetProperty("jevAdvisory", out var advisoryProp));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.AuditEvents.SingleAsync(e => e.Action == OwnerAgentAuditActions.SessionCreated);
        using var details = JsonDocument.Parse(row.Details!);
        Assert.False(details.RootElement.GetProperty("data").TryGetProperty("jevStatus", out var jevStatusProp));
    }

    [Fact]
    public async Task Jev_NoInitialMessage_MakesNoJevCall()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;

        var response = await PostCreateAsync(client, ticket, initialMessage: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.Judgments.Calls);
        Assert.False(ForwardedCreate(factory).TryGetProperty("jevAdvisory", out var advisoryProp));
    }

    [Fact]
    public async Task Jev_Unavailable_OnTheFirstMessage_FailsOpen_AndTheSessionIsCreatedWithoutAdvice()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;

        // RecordingJevJudgments defaults to Unavailable("test_unavailable"): an outage / open breaker.
        var response = await PostCreateAsync(client, ticket, "Diagnose this grading exception in the production API");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Judgments.Calls);
        var forwarded = ForwardedCreate(factory);
        Assert.Equal("Diagnose this grading exception in the production API", forwarded.GetProperty("initialMessage").GetString());
        Assert.Equal("owner-selected-model", forwarded.GetProperty("model").GetString());
        Assert.False(forwarded.TryGetProperty("jevAdvisory", out var advisoryProp));
    }

    [Fact]
    public async Task Jev_Disabled_NoKey_OnTheFirstMessage_FailsOpen()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = JevJudgmentResult.Disabled("typesafe_key_missing");

        var response = await PostCreateAsync(client, ticket, "Diagnose this grading exception in the production API");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(ForwardedCreate(factory).TryGetProperty("jevAdvisory", out var advisoryProp));
    }

    [Fact]
    public async Task Jev_ReviewRequired_BlocksASubstantiveFirstMessage_AndNoSessionIsCreated()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "debug", "elevated", confidence: 0.55);

        var response = await PostCreateAsync(client, ticket,
            "Please look into the failing grading exception in the production API and tell me what you find.");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("jev_review_required", await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.DoesNotContain(factory.Sidecar.Requests, IsCreatePost);
    }

    [Theory]
    [InlineData(59, HttpStatusCode.OK)]
    [InlineData(60, HttpStatusCode.Conflict)]
    public async Task Jev_ReviewRequired_BlocksAFirstMessage_FromSixtyCharacters(int length, HttpStatusCode expected)
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "unclear", "unclear", confidence: 0.55);

        var response = await PostCreateAsync(client, ticket, new string('a', length));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            // Too terse to triage: relayed with no advice (the sidecar would 409 a non-ok advisory).
            Assert.False(ForwardedCreate(factory).TryGetProperty("jevAdvisory", out var advisoryProp));
        }
        else
        {
            Assert.DoesNotContain(factory.Sidecar.Requests, IsCreatePost);
        }
    }

    [Fact]
    public async Task Jev_ConfidentTriage_OnTheFirstMessage_IsForwardedWithTheAdviceAndEffortTier()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "implement", "low", confidence: 0.95, effortTier: "bounded_edit");

        var response = await PostCreateAsync(client, ticket, "Add a unit test for the retry helper");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Judgments.Calls);
        var forwarded = ForwardedCreate(factory);
        var advisory = forwarded.GetProperty("jevAdvisory");
        Assert.Equal("ok", advisory.GetProperty("status").GetString());
        Assert.False(advisory.GetProperty("requiresHumanReview").GetBoolean());
        Assert.Equal("implement", advisory.GetProperty("taskKind").GetString());
        Assert.Equal("low", advisory.GetProperty("riskLevel").GetString());
        Assert.Equal("bounded_edit", advisory.GetProperty("effortTier").GetString());
        // Advice never changes what the owner picked for the session.
        Assert.Equal("owner-selected-model", forwarded.GetProperty("model").GetString());
        Assert.Equal("high", forwarded.GetProperty("effort").GetString());
        Assert.Equal("guarded", forwarded.GetProperty("mode").GetString());
        Assert.Equal("Add a unit test for the retry helper", forwarded.GetProperty("initialMessage").GetString());
    }

    [Fact]
    public async Task Jev_FirstMessageTriage_IsAuditedOnTheSessionCreatedRow()
    {
        var (factory, client, ticket) = await JevUnlockedAsync();
        await using var _ = factory;
        using var __ = client;
        factory.Judgments.Result = Triage(factory, "implement", "low", confidence: 0.95, effortTier: "bounded_edit");

        var response = await PostCreateAsync(client, ticket, "Add a unit test for the retry helper");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.AuditEvents.SingleAsync(e => e.Action == OwnerAgentAuditActions.SessionCreated);
        using var details = JsonDocument.Parse(row.Details!);
        var data = details.RootElement.GetProperty("data");
        Assert.Equal("ok", data.GetProperty("jevStatus").GetString());
        Assert.Equal("implement", data.GetProperty("jevTask").GetString());
        Assert.Equal("low", data.GetProperty("jevRisk").GetString());
        Assert.Equal("bounded_edit", data.GetProperty("jevEffort").GetString());
        Assert.Equal(scope.ServiceProvider.GetRequiredService<IOptions<TypeSafeOptions>>().Value.Model, data.GetProperty("jevModel").GetString());
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

    [Fact]
    public async Task OpenCodeConnect_ForwardsOnlyTheProviderAndOAuthMethod()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post,
            "/v1/owner-agent/auth/opencode/connect", ticket, new { providerId = "github-copilot", methodIndex = 0 });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sidecarCall = Assert.Single(factory.Sidecar.Requests);
        Assert.Equal("/v1/auth/opencode/connect", sidecarCall.PathAndQuery);
        using var body = JsonDocument.Parse(sidecarCall.Body!);
        Assert.Equal("github-copilot", body.RootElement.GetProperty("providerId").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("methodIndex").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("apiKey", out _));
    }

    /// <summary>The six actions that used to demand a per-action TOTP step-up.</summary>
    public static IEnumerable<object?[]> FormerStepUpRoutes()
    {
        yield return ["PUT", "/v1/owner-agent/github-tokens", """{"agentToken":"agent-token-value-for-tests-only"}"""];
        yield return ["POST", "/v1/owner-agent/auth/claude/connect", null];
        yield return ["POST", "/v1/owner-agent/auth/codex/logout", null];
        yield return ["POST", $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}/ship", """{"prTitle":"t"}"""];
        yield return ["PATCH", $"/v1/owner-agent/sessions/{FakeSidecarHandler.SessionId}", """{"mode":"autopilot"}"""];
        yield return ["POST", "/v1/owner-agent/sessions", """{"engine":"claude","model":"opaque-model","mode":"autopilot"}"""];
    }

    public static IEnumerable<object?[]> FormerStepUpRoutesByPresentation()
    {
        foreach (var route in FormerStepUpRoutes())
        {
            yield return [.. route, false];
            yield return [.. route, true];
        }
    }

    private static HttpRequestMessage Build(string method, string url, string? json, string? ticket, bool asCookie)
    {
        var request = ticket is null
            ? new HttpRequestMessage(new HttpMethod(method), url)
            : asCookie
                ? OwnerAgentWebApplicationFactory.WithUnlockCookie(new HttpMethod(method), url, ticket)
                : OwnerAgentWebApplicationFactory.Unlocked(new HttpMethod(method), url, ticket);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    [Theory]
    [MemberData(nameof(FormerStepUpRoutesByPresentation))]
    public async Task FormerStepUpActions_SucceedWithOnlyTheUnlock(string method, string url, string? json, bool asCookie)
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        // No X-Owner-Agent-StepUp header anywhere: the unlock alone is enough, every time.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = Build(method, url, json, ticket, asCookie);
            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{method} {url} (cookie={asCookie}) => {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        Assert.Equal(2, factory.Sidecar.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(FormerStepUpRoutes))]
    public async Task FormerStepUpActions_WithoutAnyUnlock_Are403_AndNeverReachTheSidecar(string method, string url, string? json)
    {
        var (factory, owner, client, _) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        // A fresh client: no header, no cookie.
        using var bare = factory.CreateBearerClient(owner.AccessToken);

        using var request = Build(method, url, json, ticket: null, asCookie: false);
        var response = await bare.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRequired, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task StepUpRoute_IsGone()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Post, "/v1/owner-agent/step-up", ticket, new { code = "123456" });
        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"=> {(int)response.StatusCode}");
    }

    // ── Unlock cookie (oet_owner_unlock) ────────────────────────────────────

    [Fact]
    public async Task Unlock_SetsAnHttpOnlySecureStrictCookie_ForTheFixedSixtyMinutes()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var before = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new
        {
            password = owner.Password,
            code = TestWebApplicationFactoryCodes.Now(owner),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ticket = document.RootElement.GetProperty("ticket").GetString()!;
        var expiresAt = document.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        var absoluteExpiresAt = document.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset();
        Assert.Equal(expiresAt, absoluteExpiresAt);
        Assert.InRange(expiresAt, before.AddMinutes(60).AddSeconds(-5), DateTimeOffset.UtcNow.AddMinutes(60).AddSeconds(5));

        var setCookie = Assert.Single(OwnerAgentWebApplicationFactory.UnlockSetCookies(response));
        Assert.StartsWith($"{OwnerAgentUnlockCookie.Name}={ticket};", setCookie, StringComparison.Ordinal);
        var attributes = setCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.Contains(attributes, a => a.Equals("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attributes, a => a.Equals("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attributes, a => a.Equals("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attributes, a => a.Equals("path=/", StringComparison.OrdinalIgnoreCase));
        var maxAge = attributes.Single(a => a.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(int.Parse(maxAge["max-age=".Length..], System.Globalization.CultureInfo.InvariantCulture), 3_590, 3_600);
    }

    [Fact]
    public async Task UnlockMinutesOption_DrivesTheCookieMaxAge()
    {
        await using var factory = new OwnerAgentWebApplicationFactory { UnlockMinutes = 15 };
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new
        {
            password = owner.Password,
            code = TestWebApplicationFactoryCodes.Now(owner),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = Assert.Single(OwnerAgentWebApplicationFactory.UnlockSetCookies(response));
        var maxAge = setCookie.Split(';', StringSplitOptions.TrimEntries)
            .Single(a => a.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(int.Parse(maxAge["max-age=".Length..], System.Globalization.CultureInfo.InvariantCulture), 890, 900);
    }

    [Fact]
    public async Task CookieOnly_Authorizes_AndMeReportsUnlocked()
    {
        var (factory, owner, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        // What a reload / new tab looks like: a new client that only has the cookie.
        using var browser = factory.CreateBearerClient(owner.AccessToken);

        using (var status = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Get, "/v1/owner-agent/status", ticket))
        {
            Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(status)).StatusCode);
        }

        using var me = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Get, "/v1/owner-agent/me", ticket);
        var meResponse = await browser.SendAsync(me);
        var meBody = await meResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(meBody);
        Assert.True(document.RootElement.GetProperty("unlocked").GetBoolean());
        Assert.Equal(
            document.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset(),
            document.RootElement.GetProperty("unlockExpiresAt").GetDateTimeOffset());
        Assert.DoesNotContain(ticket, meBody, StringComparison.Ordinal);

        // The cookie is never forwarded to the sidecar.
        Assert.All(factory.Sidecar.Requests, r =>
            Assert.DoesNotContain(ticket, string.Join("|", r.Headers.Values), StringComparison.Ordinal));
    }

    [Fact]
    public async Task HeaderOnly_StillAuthorizes()
    {
        var (factory, owner, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        using var scripted = factory.CreateBearerClient(owner.AccessToken);

        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        Assert.Equal(HttpStatusCode.OK, (await scripted.SendAsync(status)).StatusCode);
    }

    [Fact]
    public async Task Header_TakesPrecedenceOverTheCookie()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var request = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        request.Headers.Add(OwnerAgentHeaders.Unlock, "not-a-ticket");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockInvalid, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task NeitherHeaderNorCookie_Is403_UnlockRequired()
    {
        var (factory, owner, client, _) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        using var bare = factory.CreateBearerClient(owner.AccessToken);

        var response = await bare.GetAsync("/v1/owner-agent/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRequired, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
        Assert.Empty(factory.Sidecar.Requests);
    }

    [Fact]
    public async Task Lock_ClearsTheCookie_AndRevokesTheTicket()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using (var lockRequest = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Post, "/v1/owner-agent/lock", ticket))
        {
            var locked = await client.SendAsync(lockRequest);
            Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
            var cleared = Assert.Single(OwnerAgentWebApplicationFactory.UnlockSetCookies(locked));
            Assert.StartsWith($"{OwnerAgentUnlockCookie.Name}=;", cleared, StringComparison.Ordinal);
            Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", cleared, StringComparison.OrdinalIgnoreCase);
        }

        // Even a browser that kept the old cookie is locked out (durable watermark).
        using var status = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var response = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked, await OwnerAgentWebApplicationFactory.ReadCodeAsync(response));
    }

    [Fact]
    public async Task UnlockRefresh_NeverExtendsTheExpiry()
    {
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        using var me = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Get, "/v1/owner-agent/me", ticket);
        using var meDocument = JsonDocument.Parse(await (await client.SendAsync(me)).Content.ReadAsStringAsync());
        var expiresAt = meDocument.RootElement.GetProperty("unlockExpiresAt").GetDateTimeOffset();

        using var refresh = OwnerAgentWebApplicationFactory.WithUnlockCookie(HttpMethod.Post, "/v1/owner-agent/unlock/refresh", ticket);
        var refreshResponse = await client.SendAsync(refresh);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        using var refreshDocument = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        Assert.Equal(expiresAt, refreshDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(expiresAt, refreshDocument.RootElement.GetProperty("absoluteExpiresAt").GetDateTimeOffset());
        Assert.Single(OwnerAgentWebApplicationFactory.UnlockSetCookies(refreshResponse));
    }

    [Fact]
    public async Task SignOut_ClearsTheUnlockCookie()
    {
        // (The ticket itself dies with the revoked session family — see
        // OwnerAgentAuthorizationTests.Unlock_SurvivesAccessTokenRefresh_ButDiesWhenTheSessionFamilyIsRevoked.)
        var (factory, _, client, _) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;

        var signOut = await client.PostAsJsonAsync("/v1/auth/sign-out", new { refreshToken = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        var cleared = Assert.Single(OwnerAgentWebApplicationFactory.UnlockSetCookies(signOut));
        Assert.StartsWith($"{OwnerAgentUnlockCookie.Name}=;", cleared, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonAutopilotModeChange_IsAudited()
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
        var (factory, _, client, ticket) = await UnlockedAsync();
        await using var __ = factory;
        using var ___ = client;
        const string agentToken = "agent-token-value-for-tests-only";
        const string shipToken = "ship-token-value-for-tests-only";

        using var request = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Put, "/v1/owner-agent/github-tokens", ticket,
            new { agentToken, shipToken });
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
