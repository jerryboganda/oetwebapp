using System.Net;
using System.Text;
using System.Text.Json;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Configuration;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Tests.Provisioning;

/// <summary>The service-plane client (OET-RWP/1 section 7.1): headers, retries, error mapping, strict node ids, tolerant readers, no secrets in messages.</summary>
public sealed class HttpFleetApiTests
{
    private const string NodeId = "rw_00000000000000000000000001";
    private const string Root = "/v1/internal/fleet";

    private static readonly string Bearer = "ofs1_0123456789abcdef_" + new string('B', 43);
    private static readonly string NodeToken = "orw1_0123456789abcdef_" + new string('A', 43);

    private readonly StubHttpHandler _handler = new();
    private readonly ManualTimeProvider _time = new();
    private readonly AdvancingDelay _delay;
    private string? _bearer = Bearer;

    public HttpFleetApiTests()
    {
        _delay = new AdvancingDelay(_time);
    }

    private sealed class StaticCredential : IApiCredentialProvider
    {
        private readonly Func<string?> _read;

        public StaticCredential(Func<string?> read)
        {
            _read = read;
        }

        public string? GetBearer() => _read();
    }

    private HttpFleetApi Create()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://api.example.test") };
        return new HttpFleetApi(http, new StaticCredential(() => _bearer), Options.Create(new FleetOptions()), _delay, NullLogger<HttpFleetApi>.Instance);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private void Respond(HttpStatusCode status, string body) => _handler.Responses.Enqueue(_ => Reply(status, body));

    private static string NodeJson(string id = NodeId, string status = "Active") =>
        "{\"id\":\"" + id + "\",\"nodeRef\":\"helper-eu-01\",\"displayName\":\"Helper EU 01\",\"status\":\"" + status + "\",\"health\":\"Online\","
        + "\"lastHeartbeatAt\":\"2026-10-05T12:00:00Z\","
        + "\"agent\":{\"version\":\"1.0.0\",\"imageDigest\":\"sha256:" + new string('a', 64) + "\",\"protocol\":1,\"instanceId\":\"00000000-0000-0000-0000-000000000001\","
        + "\"kinds\":[{\"kind\":\"pdf.extract\",\"schemaVersions\":[1],\"engineVersion\":\"pdfpig:1.7.0-custom-5/oet-text:1\"}]},"
        + "\"capacity\":{\"cpuBudgetFreeMilli\":2000,\"memBudgetFreeMiB\":3500,\"tmpFreeMiB\":2900,\"heavySlotsFree\":1,\"effectiveConcurrency\":2},"
        + "\"load\":{\"cpuPct\":20,\"memFreePct\":80,\"pressure\":\"normal\"},"
        + "\"leases\":{\"count\":1,\"weight\":1,\"max\":2},"
        + "\"policy\":{\"revision\":4,\"allowedKinds\":[\"pdf.extract\"],\"maxConcurrency\":2},"
        + "\"appliedRevision\":4,\"integrityStrikes\":0,"
        + "\"lastCanary\":{\"at\":\"2026-10-05T11:59:00Z\",\"ok\":true},"
        + "\"tokens\":{\"activeCount\":1,\"nextExpiryAt\":\"2026-11-04T12:00:00Z\"},"
        + "\"someFutureField\":{\"ignored\":true}}";

    // ---- headers and credential ------------------------------------------------------------

    [Fact]
    public async Task Every_request_presents_the_bearer_the_protocol_number_and_a_fresh_correlation_id()
    {
        Respond(HttpStatusCode.OK, "{\"kinds\":[\"pdf.extract\"],\"protocol\":{\"current\":1,\"minimum\":1}}");
        Respond(HttpStatusCode.OK, "{\"kinds\":[]}");
        var api = Create();

        var status = await api.GetStatusAsync(CancellationToken.None);
        await api.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new[] { "pdf.extract" }, status.Kinds.ToArray());
        Assert.Equal(1, status.ProtocolCurrent);
        Assert.Equal(1, status.ProtocolMinimum);
        Assert.Equal(2, _handler.Requests.Count);
        var first = _handler.Requests[0];
        Assert.Equal(HttpMethod.Get, first.Method);
        Assert.Equal("api.example.test", first.Uri.Host);
        Assert.Equal(Root + "/status", first.Uri.AbsolutePath);
        Assert.Equal("Bearer " + Bearer, first.Headers["Authorization"]);
        Assert.Equal("1", first.Headers["X-Remote-Protocol"]);
        Assert.Contains("application/json", first.Headers["Accept"]);
        Assert.True(Guid.TryParse(first.Headers["X-Correlation-Id"], out var one));
        Assert.True(Guid.TryParse(_handler.Requests[1].Headers["X-Correlation-Id"], out var two));
        Assert.NotEqual(one, two);
    }

    [Fact]
    public async Task A_missing_credential_fails_before_any_request_is_sent()
    {
        _bearer = null;

        var error = await Assert.ThrowsAsync<FleetApiException>(() => Create().GetStatusAsync(CancellationToken.None));

        Assert.Equal(FleetApiException.CredentialMissing, error.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
        Assert.False(error.Retryable);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task The_credential_is_read_on_every_call_so_a_rotated_secret_file_takes_effect()
    {
        Respond(HttpStatusCode.OK, "{}");
        Respond(HttpStatusCode.OK, "{}");
        var api = Create();

        await api.GetStatusAsync(CancellationToken.None);
        _bearer = "ofs1_fedcba9876543210_" + new string('C', 43);
        await api.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Bearer " + Bearer, _handler.Requests[0].Headers["Authorization"]);
        Assert.StartsWith("Bearer ofs1_fedcba9876543210_", _handler.Requests[1].Headers["Authorization"]);
    }

    // ---- retries ---------------------------------------------------------------------------

    [Fact]
    public async Task Reads_are_retried_with_exponential_backoff_and_then_succeed()
    {
        Respond(HttpStatusCode.ServiceUnavailable, "{\"code\":\"overloaded\"}");
        Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>");
        Respond(HttpStatusCode.OK, "{\"kinds\":[\"pdf.extract\"]}");
        var started = _time.GetUtcNow();

        var status = await Create().GetStatusAsync(CancellationToken.None);

        Assert.Equal(new[] { "pdf.extract" }, status.Kinds.ToArray());
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal(2, _delay.Delays);
        Assert.Equal(TimeSpan.FromSeconds(3), _time.GetUtcNow() - started);
    }

    [Fact]
    public async Task A_read_gives_up_after_three_attempts_and_reports_the_apis_own_code()
    {
        for (var i = 0; i < 3; i++)
        {
            Respond(HttpStatusCode.ServiceUnavailable, "{\"code\":\"overloaded\",\"message\":\"try later\"}");
        }

        var error = await Assert.ThrowsAsync<FleetApiException>(() => Create().GetStatusAsync(CancellationToken.None));

        Assert.Equal("overloaded", error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task A_client_error_on_a_read_is_not_retried()
    {
        Respond(HttpStatusCode.BadRequest, "{\"code\":\"bad_request\"}");

        var error = await Assert.ThrowsAsync<FleetApiException>(() => Create().GetStatusAsync(CancellationToken.None));

        Assert.Equal("bad_request", error.Code);
        Assert.False(error.Retryable);
        Assert.Single(_handler.Requests);
        Assert.Equal(0, _delay.Delays);
    }

    [Fact]
    public async Task Mutations_are_never_retried_because_a_repeated_rotate_or_register_must_be_deliberate()
    {
        var api = Create();
        Respond(HttpStatusCode.ServiceUnavailable, "{\"code\":\"overloaded\"}");
        await Assert.ThrowsAsync<FleetApiException>(() => api.DrainAsync(NodeId, CancellationToken.None));
        Assert.Single(_handler.Requests);

        Respond(HttpStatusCode.ServiceUnavailable, "{\"code\":\"overloaded\"}");
        await Assert.ThrowsAsync<FleetApiException>(() => api.RotateTokenAsync(NodeId, 3600, 30, CancellationToken.None));
        Assert.Equal(2, _handler.Requests.Count);

        Respond(HttpStatusCode.ServiceUnavailable, "{\"code\":\"overloaded\"}");
        await Assert.ThrowsAsync<FleetApiException>(() => api.PutPolicyAsync(NodeId, PolicyDefaults.Global(), 1, CancellationToken.None));
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Equal(0, _delay.Delays);
    }

    // ---- errors ----------------------------------------------------------------------------

    [Fact]
    public async Task Error_bodies_become_stable_codes_and_a_token_in_a_message_is_redacted()
    {
        Respond(
            HttpStatusCode.Conflict,
            "{\"code\":\"policy_revision_conflict\",\"reason\":\"7\",\"message\":\"stale write by " + NodeToken + "\"}");

        var error = await Assert.ThrowsAsync<FleetApiException>(
            () => Create().PutPolicyAsync(NodeId, PolicyDefaults.Global(), 3, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Conflict, error.Status);
        Assert.Equal("policy_revision_conflict", error.Code);
        Assert.Equal("7", error.Reason);
        Assert.False(error.Retryable);
        Assert.DoesNotContain(NodeToken, error.Message);
        Assert.Contains("[redacted]", error.Message);
    }

    [Fact]
    public async Task A_proxy_error_page_falls_back_to_the_status_code_and_the_retryable_flag_can_override()
    {
        var api = Create();
        Respond(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>");
        var proxy = await Assert.ThrowsAsync<FleetApiException>(() => api.DrainAsync(NodeId, CancellationToken.None));
        Assert.Equal("http_502", proxy.Code);
        Assert.True(proxy.Retryable);

        Respond(HttpStatusCode.InternalServerError, "{\"code\":\"internal\",\"retryable\":false}");
        var fatal = await Assert.ThrowsAsync<FleetApiException>(() => api.DrainAsync(NodeId, CancellationToken.None));
        Assert.Equal("internal", fatal.Code);
        Assert.False(fatal.Retryable);

        Respond(HttpStatusCode.BadRequest, "{\"code\":\"busy\",\"retryable\":true}");
        var advised = await Assert.ThrowsAsync<FleetApiException>(() => api.DrainAsync(NodeId, CancellationToken.None));
        Assert.True(advised.Retryable);
    }

    [Fact]
    public async Task A_very_long_message_is_capped()
    {
        Respond(HttpStatusCode.UnprocessableEntity, "{\"code\":\"policy_invalid\",\"message\":\"" + new string('x', 5000) + "\"}");

        var error = await Assert.ThrowsAsync<FleetApiException>(
            () => Create().PutPolicyAsync(NodeId, PolicyDefaults.Global(), 1, CancellationToken.None));

        Assert.Equal("policy_invalid", error.Code);
        Assert.Equal(200, error.Message.Length);
    }

    [Fact]
    public async Task A_missing_node_is_null_but_any_other_not_found_is_an_error()
    {
        var api = Create();
        Respond(HttpStatusCode.NotFound, "{\"code\":\"node_not_found\"}");
        Assert.Null(await api.GetNodeAsync(NodeId, CancellationToken.None));
        Assert.Single(_handler.Requests);

        Respond(HttpStatusCode.NotFound, "{\"code\":\"route_not_found\"}");
        var error = await Assert.ThrowsAsync<FleetApiException>(() => api.GetNodeAsync(NodeId, CancellationToken.None));
        Assert.Equal("route_not_found", error.Code);
    }

    [Fact]
    public async Task An_unreachable_api_is_reported_without_the_transport_detail_after_the_read_retries()
    {
        _handler.Default = _ => throw new HttpRequestException("connection refused by 10.0.0.5 using secret-detail");
        var api = Create();

        var read = await Assert.ThrowsAsync<FleetApiException>(() => api.GetStatusAsync(CancellationToken.None));
        Assert.Equal(FleetApiException.Unreachable, read.Code);
        Assert.True(read.Retryable);
        Assert.DoesNotContain("secret-detail", read.Message);
        Assert.Equal(3, _handler.Requests.Count);

        var write = await Assert.ThrowsAsync<FleetApiException>(() => api.DrainAsync(NodeId, CancellationToken.None));
        Assert.Equal(FleetApiException.Unreachable, write.Code);
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task A_timeout_is_unreachable_but_the_callers_own_cancellation_is_not_swallowed()
    {
        _handler.Default = _ => throw new TaskCanceledException("the request timed out");
        var timedOut = await Assert.ThrowsAsync<FleetApiException>(() => Create().DrainAsync(NodeId, CancellationToken.None));
        Assert.Equal(FleetApiException.Unreachable, timedOut.Code);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().DrainAsync(NodeId, cancelled.Token));
    }

    // ---- ids -------------------------------------------------------------------------------

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("rw_short")]
    [InlineData("rw_0000000000000000000000000A")]
    [InlineData("RW_00000000000000000000000001")]
    [InlineData("rw_00000000000000000000000001/../admin")]
    [InlineData("")]
    public async Task A_node_id_is_validated_before_it_can_become_part_of_a_url(string id)
    {
        var api = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => api.GetNodeAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => api.DrainAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => api.RotateTokenAsync(id, 3600, 30, CancellationToken.None));
        Assert.Empty(_handler.Requests);
    }

    // ---- registration, tokens, policy, actions ---------------------------------------------

    [Fact]
    public async Task Registration_sends_the_node_and_its_initial_policy_and_returns_the_token_only_when_created()
    {
        Respond(
            HttpStatusCode.Created,
            "{\"node\":{\"id\":\"" + NodeId + "\",\"nodeRef\":\"helper-eu-01\",\"status\":\"Pending\",\"policyRevision\":1},"
            + "\"token\":{\"id\":\"t1\",\"value\":\"" + NodeToken + "\",\"expiresAt\":\"2026-11-04T12:00:00Z\"}}");
        Respond(HttpStatusCode.OK, "{\"node\":{\"id\":\"" + NodeId + "\",\"nodeRef\":\"helper-eu-01\",\"status\":\"Pending\",\"policyRevision\":1}}");
        var api = Create();
        var policy = PolicyDefaults.Global();
        var request = new RegisterNodeRequest(
            "helper-eu-01",
            "Helper EU 01",
            "eu-central",
            "ExampleHost",
            new RegisterPolicyDto(policy.AllowedKinds, policy.MaxConcurrency, policy.PerKind, policy.Budgets),
            30);

        var created = await api.RegisterNodeAsync(request, CancellationToken.None);
        var existing = await api.RegisterNodeAsync(request, CancellationToken.None);

        Assert.True(created.Created);
        Assert.Equal(NodeId, created.Node.Id);
        Assert.Equal("Pending", created.Node.Status);
        Assert.Equal(NodeToken, created.Token!.Value);
        Assert.Equal(DateTimeOffset.Parse("2026-11-04T12:00:00Z"), created.Token.ExpiresAt);
        Assert.False(existing.Created);
        Assert.Null(existing.Token);

        var sent = _handler.Requests[0];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(Root + "/nodes", sent.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("helper-eu-01", body.RootElement.GetProperty("nodeRef").GetString());
        Assert.Equal("eu-central", body.RootElement.GetProperty("region").GetString());
        Assert.Equal(30, body.RootElement.GetProperty("tokenTtlDays").GetInt32());
        var sentPolicy = body.RootElement.GetProperty("policy");
        Assert.Equal(2, sentPolicy.GetProperty("maxConcurrency").GetInt32());
        Assert.Equal(3000, sentPolicy.GetProperty("budgets").GetProperty("cpuMilli").GetInt32());
        Assert.Equal("pdf.extract", sentPolicy.GetProperty("allowedKinds")[0].GetString());
        Assert.False(body.RootElement.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task A_policy_write_carries_the_expected_revision_and_every_policy_field()
    {
        Respond(HttpStatusCode.OK, "{\"revision\":8}");
        var policy = PolicyDefaults.WithAgentImage(
            PolicyDefaults.Global(),
            new AgentImagePolicy(new[] { "sha256:" + new string('a', 64) }, "sha256:" + new string('a', 64), "1.0.0"));

        var revision = await Create().PutPolicyAsync(NodeId, policy, 7, CancellationToken.None);

        Assert.Equal(8, revision);
        var sent = _handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.Equal(Root + "/nodes/" + NodeId + "/policy", sent.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal(7, root.GetProperty("expectedRevision").GetInt32());
        Assert.Equal(2, root.GetProperty("maxConcurrency").GetInt32());
        Assert.Equal(2, root.GetProperty("perKind").GetProperty("pdf.extract").GetInt32());
        Assert.Equal(5120, root.GetProperty("budgets").GetProperty("memMiB").GetInt32());
        Assert.Equal(80, root.GetProperty("pressure").GetProperty("reduceCpuPct").GetInt32());
        Assert.Equal(120, root.GetProperty("pressure").GetProperty("restoreAfterSeconds").GetInt32());
        Assert.Equal(30, root.GetProperty("pollSeconds").GetProperty("max").GetInt32());
        Assert.Equal("1.0.0", root.GetProperty("agentImage").GetProperty("minVersion").GetString());
        Assert.Equal("sha256:" + new string('a', 64), root.GetProperty("agentImage").GetProperty("target").GetString());
        Assert.Equal(1, root.GetProperty("agentImage").GetProperty("approvedDigests").GetArrayLength());
    }

    [Fact]
    public async Task Rotation_sends_grace_and_ttl_and_a_response_without_a_token_is_an_error()
    {
        Respond(HttpStatusCode.OK, "{\"token\":{\"id\":\"t2\",\"value\":\"" + NodeToken + "\",\"expiresAt\":\"2026-11-04T12:00:00Z\"}}");
        Respond(HttpStatusCode.OK, "{}");
        var api = Create();

        var token = await api.RotateTokenAsync(NodeId, 3600, 30, CancellationToken.None);

        Assert.Equal(NodeToken, token.Value);
        Assert.DoesNotContain(NodeToken, token.ToString());
        var sent = _handler.Requests.Single();
        Assert.Equal(Root + "/nodes/" + NodeId + "/tokens/rotate", sent.Uri.AbsolutePath);
        using (var body = JsonDocument.Parse(sent.Body))
        {
            Assert.Equal(3600, body.RootElement.GetProperty("graceSeconds").GetInt32());
            Assert.Equal(30, body.RootElement.GetProperty("ttlDays").GetInt32());
        }

        var missing = await Assert.ThrowsAsync<FleetApiException>(() => api.RotateTokenAsync(NodeId, 3600, 30, CancellationToken.None));
        Assert.Equal("invalid_response", missing.Code);
    }

    [Fact]
    public async Task The_canary_returns_the_job_id_and_a_response_without_one_is_an_error()
    {
        Respond(HttpStatusCode.Accepted, "{\"jobId\":\"rj_000000000000000000000000a1\"}");
        Respond(HttpStatusCode.Accepted, "{}");
        var api = Create();

        Assert.Equal("rj_000000000000000000000000a1", await api.EnqueueCanaryAsync(NodeId, CancellationToken.None));
        Assert.Equal(Root + "/nodes/" + NodeId + "/canary", _handler.Requests[0].Uri.AbsolutePath);
        var missing = await Assert.ThrowsAsync<FleetApiException>(() => api.EnqueueCanaryAsync(NodeId, CancellationToken.None));
        Assert.Equal("invalid_response", missing.Code);
    }

    [Fact]
    public async Task Each_node_action_is_one_post_to_its_own_route()
    {
        var api = Create();
        var actions = new (string Route, Func<Task> Call)[]
        {
            ("enable", () => api.EnableAsync(NodeId, CancellationToken.None)),
            ("drain", () => api.DrainAsync(NodeId, CancellationToken.None)),
            ("disable", () => api.DisableAsync(NodeId, CancellationToken.None)),
            ("quarantine", () => api.QuarantineAsync(NodeId, CancellationToken.None)),
            ("revoke", () => api.RevokeAsync(NodeId, CancellationToken.None)),
        };

        foreach (var (_, call) in actions)
        {
            Respond(HttpStatusCode.OK, "{}");
            await call();
        }

        Assert.Equal(actions.Select(a => Root + "/nodes/" + NodeId + "/" + a.Route).ToArray(), _handler.Requests.Select(r => r.Uri.AbsolutePath).ToArray());
        Assert.All(_handler.Requests, request => Assert.Equal(HttpMethod.Post, request.Method));
        Assert.All(_handler.Requests, request => Assert.Equal("{}", request.Body));
    }

    // ---- reading nodes ---------------------------------------------------------------------

    [Fact]
    public async Task Listing_nodes_asks_for_two_hundred_and_maps_the_whole_node_object()
    {
        Respond(HttpStatusCode.OK, "{\"items\":[" + NodeJson() + "]}");

        var nodes = await Create().ListNodesAsync(CancellationToken.None);

        Assert.Equal("limit=200", _handler.Requests.Single().Uri.Query.TrimStart('?'));
        var node = Assert.Single(nodes);
        Assert.Equal(NodeId, node.Id);
        Assert.Equal("helper-eu-01", node.NodeRef);
        Assert.Equal("Active", node.Status);
        Assert.Equal("Online", node.Health);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T12:00:00Z"), node.LastHeartbeatAt);
        Assert.Equal("sha256:" + new string('a', 64), node.Agent!.ImageDigest);
        Assert.Equal(1, node.Agent.Protocol);
        Assert.Equal("pdf.extract", Assert.Single(node.Agent.Kinds!).Kind);
        Assert.Equal(2000, node.Capacity!.CpuBudgetFreeMilli);
        Assert.Equal(2, node.Capacity.EffectiveConcurrency);
        Assert.Equal(20.0, node.Load!.CpuPct);
        Assert.Equal(1, node.Leases!.Count);
        Assert.Equal(4, node.Policy!.Revision);
        Assert.Equal(4, node.AppliedRevision);
        Assert.True(node.LastCanary!.Ok);
        Assert.Equal(1, node.Tokens!.ActiveCount);
    }

    [Fact]
    public void The_node_list_is_accepted_as_a_bare_array_or_under_items_or_nodes_and_anything_else_is_empty()
    {
        Assert.Single(HttpFleetApi.ParseNodeList("[" + NodeJson() + "]"));
        Assert.Single(HttpFleetApi.ParseNodeList("{\"items\":[" + NodeJson() + "],\"next\":null}"));
        Assert.Single(HttpFleetApi.ParseNodeList("{\"nodes\":[" + NodeJson() + "]}"));
        Assert.Equal(2, HttpFleetApi.ParseNodeList("[" + NodeJson() + "," + NodeJson("rw_00000000000000000000000002") + "]").Count);
        Assert.Empty(HttpFleetApi.ParseNodeList("{}"));
        Assert.Empty(HttpFleetApi.ParseNodeList("{\"items\":\"nope\"}"));
        Assert.Empty(HttpFleetApi.ParseNodeList("42"));
        Assert.Empty(HttpFleetApi.ParseNodeList("[null]"));
    }

    [Fact]
    public void The_status_is_read_tolerantly_whatever_shape_the_registry_has()
    {
        var strings = HttpFleetApi.ParseStatus("{\"kinds\":[\"pdf.extract\",\"media.audio-extract\"],\"protocol\":{\"current\":2,\"min\":1}}");
        Assert.Equal(new[] { "pdf.extract", "media.audio-extract" }, strings.Kinds.ToArray());
        Assert.Equal(2, strings.ProtocolCurrent);
        Assert.Equal(1, strings.ProtocolMinimum);

        var objects = HttpFleetApi.ParseStatus("{\"kinds\":[{\"kind\":\"pdf.extract\",\"schemaVersions\":[1]},{\"nokind\":true},7],\"currentProtocol\":3,\"minProtocol\":2}");
        Assert.Equal(new[] { "pdf.extract" }, objects.Kinds.ToArray());
        Assert.Equal(3, objects.ProtocolCurrent);
        Assert.Equal(2, objects.ProtocolMinimum);

        var flat = HttpFleetApi.ParseStatus("{\"protocol\":5}");
        Assert.Empty(flat.Kinds);
        Assert.Equal(5, flat.ProtocolCurrent);
        Assert.Null(flat.ProtocolMinimum);

        foreach (var junk in new[] { "not json", "[]", "null", string.Empty })
        {
            var status = HttpFleetApi.ParseStatus(junk);
            Assert.Empty(status.Kinds);
            Assert.Null(status.ProtocolCurrent);
        }
    }

    [Fact]
    public async Task Stats_come_back_as_a_detached_json_element()
    {
        Respond(HttpStatusCode.OK, "{\"queued\":3,\"leased\":1}");

        var stats = await Create().GetStatsAsync(CancellationToken.None);

        Assert.Equal(3, stats.GetProperty("queued").GetInt32());
        Assert.Equal(Root + "/stats", _handler.Requests.Single().Uri.AbsolutePath);
    }
}
