using System.Net;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// Authentication, protocol negotiation, rate limits and error mapping of the two planes, through a real ASP.NET Core pipeline
/// (OET-RWP/1 sections 2.2 to 2.6, RW-001, RW-002, RW-007 to RW-012, RW-158). No PostgreSQL: every path asserted here ends before
/// any claim/lease SQL.
/// </summary>
public sealed class RemoteHttpPlaneTests
{
    private const string JobPlane = "/v1/internal/remote-worker";
    private const string FleetPlane = "/v1/internal/fleet";
    private const string SomeJobId = "rj_00000000000000000000000001";

    private static readonly IReadOnlyDictionary<string, string?> NoConfig = new Dictionary<string, string?>();

    private static async Task<string> BodyOf(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    private static void AssertProtocolHeaders(HttpResponseMessage response)
    {
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Remote-Protocol")));
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Remote-Protocol-Min")));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    // ── authentication (section 2.2) ─────────────────────────────────────────

    [Fact]
    public async Task NoCredential_IsA401WithTheErrorEnvelope_AndEveryResponseCarriesTheProtocolHeaders()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);

        var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", bearer: null, json: "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertProtocolHeaders(response);
        using var body = JsonDocument.Parse(await BodyOf(response));
        Assert.Equal("unauthorized", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.GetProperty("retryable").GetBoolean());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EveryKindOfBadCredential_GetsTheIdenticalBody_SoNothingCanBeProbed()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var good = await host.AddNodeAsync();
        var revoked = await host.AddNodeAsync(revokedCredential: true);
        var expired = await host.AddNodeAsync(credentialLifetime: TimeSpan.FromSeconds(-5));
        var revokedNode = await host.AddNodeAsync(RemoteNodeStatus.Revoked);
        var fleet = await host.AddFleetCredentialAsync();
        var unknownId = RemoteTokenFormat.Generate(RemoteTokenFormat.NodeKind).Token;
        var wrongSecret = good.Token[..good.Token.LastIndexOf('_')] + "_" + new string('a', RemoteTokenFormat.SecretLength);

        var candidates = new Dictionary<string, string>
        {
            ["unknown token id"] = unknownId,
            ["wrong secret"] = wrongSecret,
            ["revoked credential"] = revoked.Token,
            ["expired credential"] = expired.Token,
            ["revoked node"] = revokedNode.Token,
            ["fleet credential on the job plane"] = fleet.Token,
            ["a learner JWT"] = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJsZWFybmVyIn0.c2lnbmF0dXJl",
            ["garbage"] = "not-a-token",
        };

        string? reference = null;
        foreach (var (label, token) in candidates)
        {
            var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", token, json: "{}");

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{label} should be 401 but was {(int)response.StatusCode}");
            AssertProtocolHeaders(response);
            var body = await BodyOf(response);
            reference ??= body;
            Assert.True(string.Equals(reference, body, StringComparison.Ordinal), $"{label} produced a different body");
        }
    }

    [Fact]
    public async Task ANodeTokenCannotCallTheServicePlane_AndAFleetTokenCannotCallTheJobPlane()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig, RemoteJobFlagKeys.FleetService);
        var node = await host.AddNodeAsync();
        var fleet = await host.AddFleetCredentialAsync();

        var nodeOnFleet = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes", node.Token);
        var fleetOnJobs = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", fleet.Token, json: "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, nodeOnFleet.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fleetOnJobs.StatusCode);
    }

    [Fact]
    public async Task ARepeatedFailedVerification_IsThrottledPerSource_WithRetryAfter()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var good = await host.AddNodeAsync();
        var wrong = good.Token[..good.Token.LastIndexOf('_')] + "_" + new string('b', RemoteTokenFormat.SecretLength);

        for (var i = 0; i < RemoteCredentialAuthenticationHandler.FailedVerificationsPerMinute; i++)
        {
            var failed = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", wrong, json: "{}");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // From here even the CORRECT secret is refused for the rest of the window: the source is throttled, not the credential.
        var throttled = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", good.Token, json: "{}");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("60", Assert.Single(throttled.Headers.GetValues("Retry-After")));
        AssertProtocolHeaders(throttled);
        using (var body = JsonDocument.Parse(await BodyOf(throttled)))
        {
            Assert.Equal("rate_limited", body.RootElement.GetProperty("code").GetString());
            Assert.True(body.RootElement.GetProperty("retryable").GetBoolean());
        }

        host.Clock.Advance(TimeSpan.FromSeconds(61));
        var recovered = await host.SendAsync(HttpMethod.Post, JobPlane + "/jobs/not-a-job/heartbeat", good.Token, json: "{}");
        Assert.Equal(HttpStatusCode.NotFound, recovered.StatusCode);
    }

    [Fact]
    public async Task Revocation_BitesAtOnceOnEveryRouteExceptTheTwoHeartbeats_WhichMayUseAFiveSecondCache()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();
        var heartbeat = JobPlane + "/jobs/not-a-job/heartbeat";
        var uncached = JobPlane + "/jobs/not-a-job/fail";

        // A heartbeat from an authenticated node reaches the handler (404 for the made-up job id).
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, heartbeat, node.Token, json: "{}")).StatusCode);

        await host.RevokeAsync(node.TokenId);

        // The heartbeat route may still answer from its short-lived cache ...
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, heartbeat, node.Token, json: "{}")).StatusCode);
        // ... but every other route reads the credential fresh, so the revocation is felt immediately ...
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, uncached, node.Token, json: "{}")).StatusCode);
        // ... and a failed verification evicts the cached entry, so the heartbeat route follows at once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, heartbeat, node.Token, json: "{}")).StatusCode);
    }

    [Fact]
    public async Task TheHeartbeatCache_NeverOutlivesItsFiveSeconds()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();
        var heartbeat = JobPlane + "/jobs/not-a-job/heartbeat";

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, heartbeat, node.Token, json: "{}")).StatusCode);
        await host.RevokeAsync(node.TokenId);
        host.Clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, heartbeat, node.Token, json: "{}")).StatusCode);
    }

    // ── protocol negotiation (section 2.4) ───────────────────────────────────

    [Fact]
    public async Task AMissingOrMalformedProtocolHeader_IsA400()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();

        foreach (var protocol in new string?[] { null, "", "abc", "0", "-1", "1.5" })
        {
            var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", node.Token, protocol, "{}");
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"protocol '{protocol}' should be 400 but was {(int)response.StatusCode}");
            AssertProtocolHeaders(response);
        }
    }

    [Fact]
    public async Task AnUnsupportedProtocol_IsA426ListingWhatIsSupported()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();

        var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", node.Token, "7", "{}");

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        using var body = JsonDocument.Parse(await BodyOf(response));
        Assert.Equal("protocol_unsupported", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(new[] { 1 }, body.RootElement.GetProperty("supported").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public async Task N_And_NMinusOne_AreBothAccepted_WhenTheServerHasMovedToProtocolTwo()
    {
        var config = new Dictionary<string, string?> { ["RemoteJobs:CurrentProtocol"] = "2", ["RemoteJobs:MinProtocol"] = "1" };
        await using var host = await RemoteHttpHost.StartAsync(config);
        var node = await host.AddNodeAsync();

        var current = await host.SendAsync(HttpMethod.Post, JobPlane + "/jobs/not-a-job/fail", node.Token, "2", "{}");
        var previous = await host.SendAsync(HttpMethod.Post, JobPlane + "/jobs/not-a-job/fail", node.Token, "1", "{}");
        var future = await host.SendAsync(HttpMethod.Post, JobPlane + "/jobs/not-a-job/fail", node.Token, "3", "{}");

        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, previous.StatusCode);
        Assert.Equal((HttpStatusCode)426, future.StatusCode);
        Assert.Equal("2", Assert.Single(current.Headers.GetValues("X-Remote-Protocol")));
        Assert.Equal("1", Assert.Single(current.Headers.GetValues("X-Remote-Protocol-Min")));
    }

    [Fact]
    public async Task AnUnknownPathOnAPlane_StillCarriesTheProtocolHeaders()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);

        var response = await host.SendAsync(HttpMethod.Get, JobPlane + "/no-such-route", bearer: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertProtocolHeaders(response);
    }

    // ── per-node rate limits (section 2.6) ───────────────────────────────────

    [Fact]
    public async Task ClaimIsRateLimitedPerVerifiedNode_NotPerSource()
    {
        var config = new Dictionary<string, string?> { ["RemoteJobs:ClaimRatePerMinute"] = "3" };
        await using var host = await RemoteHttpHost.StartAsync(config);
        var first = await host.AddNodeAsync();
        var second = await host.AddNodeAsync();

        for (var i = 0; i < 3; i++)
        {
            var allowed = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", first.Token, json: "{}");
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode); // reaches the handler, which rejects the empty body
        }

        var limited = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", first.Token, json: "{}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After"))) is > 0 and <= 60);
        AssertProtocolHeaders(limited);

        // another node behind the same address is unaffected
        var other = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", second.Token, json: "{}");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);

        // and the window resets
        host.Clock.Advance(TimeSpan.FromSeconds(61));
        var later = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", first.Token, json: "{}");
        Assert.Equal(HttpStatusCode.BadRequest, later.StatusCode);
    }

    // ── status gates (section 4.0) ───────────────────────────────────────────

    [Fact]
    public async Task APendingNode_MayNotTouchAJob()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var pending = await host.AddNodeAsync(RemoteNodeStatus.Pending);

        var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/jobs/" + SomeJobId + "/heartbeat", pending.Token, json: "{\"fence\":1}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await BodyOf(response));
        Assert.Equal("node_not_active", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AMalformedJobId_Is404WithoutAnyDatabaseRead()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();

        foreach (var path in new[]
        {
            "/jobs/not-a-job/heartbeat",
            "/jobs/RJ_00000000000000000000000001/complete",
            "/jobs/rj_short/fail",
        })
        {
            var response = await host.SendAsync(HttpMethod.Post, JobPlane + path, node.Token, json: "{}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var body = JsonDocument.Parse(await BodyOf(response));
            Assert.Equal("job_not_found", body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task AClaimWithABodyThatIsNotAValidRequest_Is400_NeverAFrameworkShapedError()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();

        foreach (var json in new[] { "{}", "[]", "not json", "{\"claimId\":\"nope\"}", "{\"claimId\":1}" })
        {
            var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", node.Token, json: json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await BodyOf(response));
            Assert.Equal("bad_request", body.RootElement.GetProperty("code").GetString());
        }
    }

    // ── errors never leak (section 2.5) ──────────────────────────────────────

    [Fact]
    public async Task AnInternalFailure_IsAGenericRetryable500_WithoutAStackTraceOrAProviderName()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig);
        var node = await host.AddNodeAsync();
        var claim = JsonSerializer.Serialize(new
        {
            claimId = Guid.NewGuid(),
            instanceId = Guid.NewGuid(),
            appliedRevision = 0,
            kinds = new[] { new { kind = "pdf.extract", schemaVersions = new[] { 1 }, engineVersion = RemoteTestData.Engine() } },
            capacity = new { cpuBudgetFreeMilli = 3000, memBudgetFreeMiB = 5120, tmpFreeMiB = 3072, heavySlotsFree = 2, effectiveConcurrency = 2 },
            agent = new { version = "1.0.0", imageDigest = "sha256:" + new string('b', 64), protocol = 1, protocolsSupported = new[] { 1 } },
        });

        // The InMemory provider cannot run the claim SQL: the handler throws, and the filter turns it into the envelope.
        var response = await host.SendAsync(HttpMethod.Post, JobPlane + "/claim", node.Token, json: claim);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertProtocolHeaders(response);
        var text = await BodyOf(response);
        using var body = JsonDocument.Parse(text);
        Assert.Equal("internal_error", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("PostgreSQL", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
    }

    // ── the service plane (section 7) ────────────────────────────────────────

    [Fact]
    public async Task TheServicePlane_AnswersA404WithAProblemBody_WhileItsFlagIsOff()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig); // every flag off
        var fleet = await host.AddFleetCredentialAsync();

        var response = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes", fleet.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertProtocolHeaders(response);
        using var body = JsonDocument.Parse(await BodyOf(response));
        Assert.Equal("fleet_service_disabled", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheServicePlane_RequiresTheFleetCredential_AndAProtocolHeader_WhenTheFlagIsOn()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig, RemoteJobFlagKeys.FleetService);
        var fleet = await host.AddFleetCredentialAsync();

        var anonymous = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", bearer: null);
        var noProtocol = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", fleet.Token, protocol: null);
        var ok = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", fleet.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noProtocol.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ok.StatusCode);
        using var body = JsonDocument.Parse(await BodyOf(ok));
        Assert.Equal("node_not_found", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheServicePlane_HonoursTheSourceAllowList()
    {
        var config = new Dictionary<string, string?> { ["RemoteJobs:FleetAllowedCidrs:0"] = "10.9.0.0/16" };
        await using var host = await RemoteHttpHost.StartAsync(config, RemoteJobFlagKeys.FleetService);
        var fleet = await host.AddFleetCredentialAsync();

        // The in-process test server presents no remote address, which a configured allow-list must refuse.
        var response = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", fleet.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await BodyOf(response));
        Assert.Equal("forbidden_ip", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheServicePlane_IsRateLimitedPerCredential()
    {
        await using var host = await RemoteHttpHost.StartAsync(NoConfig, RemoteJobFlagKeys.FleetService);
        var fleet = await host.AddFleetCredentialAsync();

        for (var i = 0; i < RemoteFleetPlaneFilter.RequestsPerMinute; i++)
        {
            var allowed = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", fleet.Token);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }

        var limited = await host.SendAsync(HttpMethod.Get, FleetPlane + "/nodes/not-a-node", fleet.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
