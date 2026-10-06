using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fleet.Core.Policy;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Web;

/// <summary>The JSON API the dashboard is built on (OET-RWP/1 section 8.8), the sync endpoint, metrics and the event stream, over the real application.</summary>
public sealed class WebApiTests : IAsyncLifetime
{
    private const string SyncToken = "sync-secret-token-value";
    private const string MetricsToken = "scrape-secret-token-value";
    private const string RegistryToken = "ghs_TESTONLYTOKEN0123456789abcdef";

    private FleetWebFactory _factory = null!;
    private HttpClient _client = null!;
    private string _secret = null!;
    private string _csrf = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _factory = new FleetWebFactory();
        (_client, _secret, _csrf) = await _factory.SignedInAsync();
        _driver = new EnrollmentDriver(_factory.World, _factory.Services);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private FleetWorld World => _factory.World;

    private T Get<T>()
        where T : notnull => _factory.Services.GetRequiredService<T>();

    /// <summary>A state-changing call with the antiforgery header and a fresh authenticator code (the clock moves three steps so the code is certainly unused).</summary>
    private async Task<HttpResponseMessage> PrivilegedAsync(HttpMethod method, string path, object? body = null)
    {
        _factory.Time.Advance(TimeSpan.FromSeconds(90));
        using var request = FleetWebFactory.ApiRequest(method, path, _csrf, _factory.Code(_secret), body);
        return await _client.SendAsync(request);
    }

    /// <summary>A state-changing call that needs no step-up (only the session and the antiforgery header).</summary>
    private async Task<HttpResponseMessage> PlainAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = FleetWebFactory.ApiRequest(method, path, _csrf, null, body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, "expected " + expected + " but got " + response.StatusCode + ": " + text);
        return JsonDocument.Parse(text);
    }

    private static object ReleaseBody(char sha = '1') => new
    {
        sha = new string(sha, 40),
        runId = "123456789",
        agentRepository = Fleet.Core.Ssh.FleetCtlVerbs.AgentRepository,
        agentDigest = FleetWorld.AgentDigest,
        agentImageId = FleetWorld.AgentImageId,
        managerDigest = (string?)null,
    };

    // ---- reads on a fresh manager ----------------------------------------------------------

    [Fact]
    public async Task A_fresh_manager_reports_empty_collections_the_default_policy_and_an_intact_audit_log()
    {
        using (var hosts = await JsonOf(await _client.GetAsync("/api/v1/hosts"), HttpStatusCode.OK))
        {
            Assert.Equal(0, hosts.RootElement.GetArrayLength());
        }

        using (var operations = await JsonOf(await _client.GetAsync("/api/v1/operations"), HttpStatusCode.OK))
        {
            Assert.Equal(0, operations.RootElement.GetArrayLength());
        }

        using (var releases = await JsonOf(await _client.GetAsync("/api/v1/releases"), HttpStatusCode.OK))
        {
            Assert.Equal(0, releases.RootElement.GetArrayLength());
        }

        using (var nodes = await JsonOf(await _client.GetAsync("/api/v1/nodes"), HttpStatusCode.OK))
        {
            Assert.False(nodes.RootElement.GetProperty("reachable").GetBoolean());
            Assert.Equal(0, nodes.RootElement.GetProperty("nodes").GetArrayLength());
        }

        using (var policy = await JsonOf(await _client.GetAsync("/api/v1/policy"), HttpStatusCode.OK))
        {
            Assert.Equal(2, policy.RootElement.GetProperty("maxConcurrency").GetInt32());
            Assert.Equal("pdf.extract", policy.RootElement.GetProperty("allowedKinds")[0].GetString());
            Assert.Equal(3000, policy.RootElement.GetProperty("budgets").GetProperty("cpuMilli").GetInt32());
        }

        using (var health = await JsonOf(await _client.GetAsync("/api/v1/health"), HttpStatusCode.OK))
        {
            Assert.True(health.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(health.RootElement.GetProperty("auditChainIntact").GetBoolean());
        }

        using (var verify = await JsonOf(await _client.GetAsync("/api/v1/audit/verify"), HttpStatusCode.OK))
        {
            Assert.True(verify.RootElement.GetProperty("intact").GetBoolean());
        }

        using var audit = await JsonOf(await _client.GetAsync("/api/v1/audit?take=50"), HttpStatusCode.OK);
        var actions = audit.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("action").GetString()).ToList();
        Assert.Contains("owner.created", actions);
        Assert.Contains("owner.login", actions);
    }

    [Fact]
    public async Task Unknown_ids_are_404_with_a_stable_code_and_an_unknown_route_is_404()
    {
        foreach (var path in new[] { "/api/v1/hosts/no-such-host", "/api/v1/operations/no-such-operation" })
        {
            using var missing = await JsonOf(await _client.GetAsync(path), HttpStatusCode.NotFound);
            Assert.Equal("not_found", missing.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/nothing-here")).StatusCode);
    }

    // ---- adding a helper -------------------------------------------------------------------

    [Fact]
    public async Task Bad_input_is_a_422_with_issues_and_never_creates_anything()
    {
        using (var empty = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", new { }), HttpStatusCode.UnprocessableEntity))
        {
            Assert.Equal("validation_failed", empty.RootElement.GetProperty("code").GetString());
            var fields = empty.RootElement.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("field").GetString()).ToList();
            Assert.Contains("nodeRef", fields);
            Assert.Contains("address", fields);
        }

        var primary = new { nodeRef = "helper-eu-01", displayName = "Helper", address = "185.252.233.186", sshPort = 22 };
        using (var forbidden = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", primary), HttpStatusCode.UnprocessableEntity))
        {
            Assert.Contains(forbidden.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "forbidden_host");
        }

        var hostile = new { nodeRef = "helper-eu-01", displayName = "Helper", address = "203.0.113.10; rm -rf /", sshPort = 22 };
        using (var injected = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", hostile), HttpStatusCode.UnprocessableEntity))
        {
            Assert.Contains(injected.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("field").GetString() == "address");
        }

        var badPort = new { nodeRef = "helper-eu-01", displayName = "Helper", address = "203.0.113.10", sshPort = 0 };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", badPort)).StatusCode);

        using var hosts = await JsonOf(await _client.GetAsync("/api/v1/hosts"), HttpStatusCode.OK);
        Assert.Equal(0, hosts.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_400_before_any_code_is_used()
    {
        _factory.Time.Advance(TimeSpan.FromSeconds(90));
        var code = _factory.Code(_secret);
        using (var broken = FleetWebFactory.ApiRequest(HttpMethod.Post, "/api/v1/hosts", _csrf, code))
        {
            broken.Content = new StringContent("{\"nodeRef\":", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(broken)).StatusCode);
        }

        // The very same code still works: the request never reached the step-up check.
        using var valid = FleetWebFactory.ApiRequest(
            HttpMethod.Post,
            "/api/v1/hosts",
            _csrf,
            code,
            new { nodeRef = "helper-eu-01", displayName = "Helper", address = "203.0.113.10", sshPort = 22 });
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(valid)).StatusCode);
    }

    // ---- the whole enrollment over HTTP ----------------------------------------------------

    [Fact]
    public async Task The_owner_enrolls_a_helper_over_the_json_api_from_nothing_to_active_and_no_secret_is_ever_echoed()
    {
        var helper = World.Provisioner.AddHost("203.0.113.10");
        var runner = Get<OperationRunner>();
        var addBody = new { nodeRef = "helper-eu-01", displayName = "Helper EU 01", address = "203.0.113.10", sshPort = 22, region = "eu-central", provider = "ExampleHost" };

        // 1. Add (a duplicate is a no-op that returns the same operation).
        string hostId;
        string operationId;
        using (var added = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", addBody), HttpStatusCode.Created))
        {
            Assert.False(added.RootElement.GetProperty("alreadyExisted").GetBoolean());
            Assert.Equal("Created", added.RootElement.GetProperty("operation").GetProperty("state").GetString());
            hostId = added.RootElement.GetProperty("host").GetProperty("id").GetString()!;
            operationId = added.RootElement.GetProperty("operation").GetProperty("id").GetString()!;
        }

        using (var duplicate = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts", addBody), HttpStatusCode.OK))
        {
            Assert.True(duplicate.RootElement.GetProperty("alreadyExisted").GetBoolean());
            Assert.Equal(operationId, duplicate.RootElement.GetProperty("operation").GetProperty("id").GetString());
        }

        // 2. The scan shows the key to be compared out of band; nothing is trusted yet.
        await runner.RunAsync(operationId, CancellationToken.None);
        using (var pending = await JsonOf(await _client.GetAsync("/api/v1/operations/" + operationId), HttpStatusCode.OK))
        {
            Assert.Equal("HostKeyPending", pending.RootElement.GetProperty("state").GetString());
            Assert.Equal(helper.Fingerprint, pending.RootElement.GetProperty("hostKeyCandidates")[0].GetProperty("fingerprint").GetString());
            Assert.Equal(13, pending.RootElement.GetProperty("steps").GetArrayLength());
        }

        // 3. Wrong characters are refused (and the operation stays where it was); the right ones pin the key.
        using (var wrong = await JsonOf(
                   await PrivilegedAsync(HttpMethod.Post, "/api/v1/operations/" + operationId + "/confirm-host-key", new { fingerprint = "AAAAAAAA" }),
                   HttpStatusCode.UnprocessableEntity))
        {
            Assert.Contains(wrong.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "host_key_mismatch");
        }

        var typed = helper.Fingerprint["SHA256:".Length..][..8];
        using (var confirmed = await JsonOf(
                   await PrivilegedAsync(HttpMethod.Post, "/api/v1/operations/" + operationId + "/confirm-host-key", new { fingerprint = typed }),
                   HttpStatusCode.OK))
        {
            Assert.Equal("HostKeyConfirmed", confirmed.RootElement.GetProperty("state").GetString());
            Assert.True(confirmed.RootElement.GetProperty("awaitingOwnerCredential").GetBoolean());
        }

        // 4. The temporary owner key goes in once and never comes back out.
        var submit = await PrivilegedAsync(HttpMethod.Post, "/api/v1/operations/" + operationId + "/owner-credential", new { user = "root", privateKey = helper.OwnerKeyText });
        var submitText = await submit.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Contains("Bootstrapping", submitText);
        Assert.DoesNotContain("OWNER-KEY-MARKER", submitText);

        // 5. Release: recorded by the owner, approved with a step-up, the registry token arrives from CI.
        string releaseId;
        using (var recorded = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/releases", ReleaseBody()), HttpStatusCode.OK))
        {
            Assert.False(recorded.RootElement.GetProperty("approved").GetBoolean());
            releaseId = recorded.RootElement.GetProperty("id").GetString()!;
        }

        using (var approved = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/releases/" + releaseId + "/approve"), HttpStatusCode.OK))
        {
            Assert.True(approved.RootElement.GetProperty("release").GetProperty("approved").GetBoolean());
        }

        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_sync_token"), SyncToken + "\n");
        using (var ci = _factory.CreateHttps())
        {
            using var sync = new HttpRequestMessage(HttpMethod.Post, "/internal/sync")
            {
                Content = JsonContent.Create(new { record = ReleaseBody(), registryUsername = "ci-user", registryToken = RegistryToken }),
            };
            sync.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SyncToken);
            using var synced = await JsonOf(await ci.SendAsync(sync), HttpStatusCode.OK);
            Assert.True(synced.RootElement.GetProperty("approved").GetBoolean());
        }

        // 6. The runner finishes the job.
        await runner.RunAsync(operationId, CancellationToken.None);
        using (var finished = await JsonOf(await _client.GetAsync("/api/v1/operations/" + operationId), HttpStatusCode.OK))
        {
            Assert.Equal("Active", finished.RootElement.GetProperty("state").GetString());
            Assert.All(finished.RootElement.GetProperty("steps").EnumerateArray(), step => Assert.Contains(step.GetProperty("state").GetString(), new[] { "done", "skipped" }));
        }

        using (var hosts = await JsonOf(await _client.GetAsync("/api/v1/hosts"), HttpStatusCode.OK))
        {
            var host = Assert.Single(hosts.RootElement.EnumerateArray());
            Assert.Equal("Active", host.GetProperty("lifecycle").GetString());
            Assert.Equal(helper.Fingerprint, host.GetProperty("hostKeyFingerprint").GetString());
            Assert.False(host.TryGetProperty("hostKeyPublic", out _));
        }

        using (var detail = await JsonOf(await _client.GetAsync("/api/v1/hosts/" + hostId), HttpStatusCode.OK))
        {
            Assert.Equal("Active", detail.RootElement.GetProperty("node").GetProperty("status").GetString());
            Assert.Equal("Active", detail.RootElement.GetProperty("enrollOperation").GetProperty("state").GetString());
        }

        // 7. The fleet picture, then day-two: a wrong-state action is a 409, a drain is a durable operation.
        await Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        using (var nodes = await JsonOf(await _client.GetAsync("/api/v1/nodes"), HttpStatusCode.OK))
        {
            Assert.True(nodes.RootElement.GetProperty("reachable").GetBoolean());
            Assert.Equal("Active", Assert.Single(nodes.RootElement.GetProperty("nodes").EnumerateArray()).GetProperty("status").GetString());
        }

        using (var enableActive = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts/" + hostId + "/enable"), HttpStatusCode.Conflict))
        {
            Assert.Equal("invalid_state", enableActive.RootElement.GetProperty("code").GetString());
        }

        string drainId;
        using (var drain = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts/" + hostId + "/drain"), HttpStatusCode.OK))
        {
            Assert.Equal("Queued", drain.RootElement.GetProperty("state").GetString());
            drainId = drain.RootElement.GetProperty("id").GetString()!;
        }

        await runner.RunAsync(drainId, CancellationToken.None);
        using (var drained = await JsonOf(await _client.GetAsync("/api/v1/hosts/" + hostId), HttpStatusCode.OK))
        {
            Assert.Equal("Draining", drained.RootElement.GetProperty("host").GetProperty("lifecycle").GetString());
            Assert.Equal("Draining", drained.RootElement.GetProperty("node").GetProperty("status").GetString());
        }

        // 8. Retry and cancel of a finished operation are refused, an unknown host is a 404.
        using (var retry = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/operations/" + operationId + "/retry"), HttpStatusCode.Conflict))
        {
            Assert.Equal("invalid_state", retry.RootElement.GetProperty("code").GetString());
        }

        using (var cancel = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/operations/" + operationId + "/cancel"), HttpStatusCode.Conflict))
        {
            Assert.Equal("invalid_state", cancel.RootElement.GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await PrivilegedAsync(HttpMethod.Post, "/api/v1/hosts/no-such-host/drain")).StatusCode);

        // 9. The audit trail names what happened and contains no secret.
        var auditText = await (await _client.GetAsync("/api/v1/audit?take=500")).Content.ReadAsStringAsync();
        foreach (var action in new[] { "host.added", "enroll.host_key_pinned", "enroll.owner_credential_submitted", "release.approved", "release.sync", "enroll.completed", "host.drain_requested" })
        {
            Assert.Contains(action, auditText);
        }

        var nodeToken = World.Api.Nodes.Single().Tokens.First().Value;
        foreach (var secret in new[] { "OWNER-KEY-MARKER", RegistryToken, SyncToken, nodeToken })
        {
            Assert.DoesNotContain(secret, auditText);
        }

        Assert.True((await Get<IAuditService>().VerifyAsync()).Intact);
    }

    // ---- policy ----------------------------------------------------------------------------

    [Fact]
    public async Task The_global_policy_can_be_replaced_with_a_step_up_and_a_bad_one_is_refused_unchanged()
    {
        var tooMany = PolicyDefaults.Global() with { MaxConcurrency = 9, PerKind = new Dictionary<string, int> { ["pdf.extract"] = 2 } };
        using (var refused = await JsonOf(await PrivilegedAsync(HttpMethod.Put, "/api/v1/policy", tooMany), HttpStatusCode.UnprocessableEntity))
        {
            Assert.Equal("validation_failed", refused.RootElement.GetProperty("code").GetString());
        }

        var smaller = PolicyDefaults.Global() with
        {
            MaxConcurrency = 1,
            PerKind = new Dictionary<string, int> { ["pdf.extract"] = 1 },
            Budgets = new Budgets(2000, 2048, 1024),
        };
        using (var stored = await JsonOf(await PrivilegedAsync(HttpMethod.Put, "/api/v1/policy", smaller), HttpStatusCode.OK))
        {
            Assert.Equal(1, stored.RootElement.GetProperty("policy").GetProperty("maxConcurrency").GetInt32());
            Assert.Equal(0, stored.RootElement.GetProperty("pushed").GetArrayLength());
        }

        using (var read = await JsonOf(await _client.GetAsync("/api/v1/policy"), HttpStatusCode.OK))
        {
            Assert.Equal(1, read.RootElement.GetProperty("maxConcurrency").GetInt32());
            Assert.Equal(2000, read.RootElement.GetProperty("budgets").GetProperty("cpuMilli").GetInt32());
        }

        // Without a step-up nothing changes at all.
        var noCode = FleetWebFactory.ApiRequest(HttpMethod.Put, "/api/v1/policy", _csrf, null, PolicyDefaults.Global());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(noCode)).StatusCode);
        using var unchanged = await JsonOf(await _client.GetAsync("/api/v1/policy"), HttpStatusCode.OK);
        Assert.Equal(1, unchanged.RootElement.GetProperty("maxConcurrency").GetInt32());
    }

    [Fact]
    public async Task A_host_policy_override_is_pushed_and_an_unreachable_api_is_a_502_with_its_stable_code()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();
        var override1 = PolicyDefaults.Global() with
        {
            MaxConcurrency = 1,
            PerKind = new Dictionary<string, int> { ["pdf.extract"] = 1 },
            Budgets = new Budgets(1500, 1536, 768),
        };

        using (var pushed = await JsonOf(await PrivilegedAsync(HttpMethod.Put, "/api/v1/hosts/" + host.Id + "/policy", override1), HttpStatusCode.OK))
        {
            Assert.Equal(1, pushed.RootElement.GetProperty("policy").GetProperty("maxConcurrency").GetInt32());
            Assert.True(pushed.RootElement.GetProperty("revision").GetInt32() > 0);
        }

        Assert.Equal(1, World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.Policy!.MaxConcurrency);

        World.Api.Reachable = false;
        using (var down = await JsonOf(await PrivilegedAsync(HttpMethod.Put, "/api/v1/hosts/" + host.Id + "/policy", override1), HttpStatusCode.BadGateway))
        {
            Assert.Equal("unreachable", down.RootElement.GetProperty("code").GetString());
        }

        // Reading a host never fails just because the API is down: the node part is simply missing.
        using (var detail = await JsonOf(await _client.GetAsync("/api/v1/hosts/" + host.Id), HttpStatusCode.OK))
        {
            Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("node").ValueKind);
        }

        using var unknownHost = await JsonOf(await PrivilegedAsync(HttpMethod.Put, "/api/v1/hosts/no-such-host/policy", override1), HttpStatusCode.UnprocessableEntity);
        Assert.Contains(unknownHost.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "host_not_found");
    }

    // ---- releases and rollouts -------------------------------------------------------------

    [Fact]
    public async Task A_malformed_release_is_refused_and_a_rollout_needs_an_approved_digest()
    {
        var bad = new { sha = "123", runId = "x", agentRepository = "ghcr.io/evil/agent", agentDigest = "latest", agentImageId = "nope", managerDigest = (string?)null };
        using (var refused = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/releases", bad), HttpStatusCode.UnprocessableEntity))
        {
            Assert.True(refused.RootElement.GetProperty("issues").GetArrayLength() >= 4);
        }

        using (var unapproved = await JsonOf(
                   await PrivilegedAsync(HttpMethod.Post, "/api/v1/rollouts", new { digest = FleetWorld.AgentDigest }),
                   HttpStatusCode.UnprocessableEntity))
        {
            Assert.Contains(unapproved.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "image_digest_unapproved");
        }

        string releaseId;
        using (var recorded = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/releases", ReleaseBody()), HttpStatusCode.OK))
        {
            releaseId = recorded.RootElement.GetProperty("id").GetString()!;
        }

        using (var approve = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/releases/" + releaseId + "/approve"), HttpStatusCode.OK))
        {
            Assert.True(approve.RootElement.GetProperty("release").GetProperty("approved").GetBoolean());
        }

        using var nobody = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/rollouts", new { digest = FleetWorld.AgentDigest }), HttpStatusCode.Conflict);
        Assert.Equal("no_hosts", nobody.RootElement.GetProperty("code").GetString());

        using var unknown = await JsonOf(await PrivilegedAsync(HttpMethod.Post, "/api/v1/releases/rel_missing/approve"), HttpStatusCode.UnprocessableEntity);
        Assert.Contains(unknown.RootElement.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "release_not_found");
    }

    // ---- placement -------------------------------------------------------------------------

    [Fact]
    public async Task Placement_decisions_name_their_kind_in_words_and_reservations_can_be_released()
    {
        await _driver.EnrollToActiveAsync();
        await Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        var job = new { kind = "pdf.extract", schemaVersion = 1, engineVersion = FakeFleetApi.Engine, weight = 1, cpuMilli = 1000, memMiB = 2048, tmpMiB = 256 };

        string reservationId;
        using (var remote = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/placement/decide", job), HttpStatusCode.OK))
        {
            Assert.Equal("Remote", remote.RootElement.GetProperty("kind").GetString());
            Assert.Equal(World.Api.Nodes.Single().Id, remote.RootElement.GetProperty("nodeId").GetString());
            reservationId = remote.RootElement.GetProperty("reservationId").GetString()!;
        }

        using (var second = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/placement/decide", job), HttpStatusCode.OK))
        {
            Assert.Equal("Local", second.RootElement.GetProperty("kind").GetString());
            Assert.Equal("local_headroom", second.RootElement.GetProperty("reason").GetString());
        }

        using (var released = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/placement/release/" + reservationId), HttpStatusCode.OK))
        {
            Assert.True(released.RootElement.GetProperty("released").GetBoolean());
        }

        using (var again = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/placement/release/" + reservationId), HttpStatusCode.OK))
        {
            Assert.False(again.RootElement.GetProperty("released").GetBoolean());
        }

        // A canary exists to test one specific node: aimed at another node it waits, however long it has waited, and never runs on the primary.
        var canary = new { kind = "pdf.extract", schemaVersion = 1, engineVersion = FakeFleetApi.Engine, weight = 1, purpose = "canary", targetNodeId = "rw_other", waitedMinutes = 500 };
        using var waiting = await JsonOf(await PlainAsync(HttpMethod.Post, "/api/v1/placement/decide", canary), HttpStatusCode.OK);
        Assert.Equal("Wait", waiting.RootElement.GetProperty("kind").GetString());
        Assert.Equal("canary_no_target", waiting.RootElement.GetProperty("reason").GetString());
    }

    // ---- metrics ---------------------------------------------------------------------------

    [Fact]
    public async Task Metrics_are_readable_by_the_owner_session_and_by_a_scraper_holding_the_metrics_token_only()
    {
        using var asOwner = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, asOwner.StatusCode);
        Assert.StartsWith("text/plain; version=0.0.4", asOwner.Content.Headers.ContentType!.ToString());
        Assert.Contains("fleet_api_reachable", await asOwner.Content.ReadAsStringAsync());

        using var scraper = _factory.CreateHttps();
        using (var noToken = new HttpRequestMessage(HttpMethod.Get, "/metrics"))
        {
            noToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MetricsToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scraper.SendAsync(noToken)).StatusCode);
        }

        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_metrics_token"), MetricsToken + "\n");
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_sync_token"), SyncToken + "\n");

        using (var good = new HttpRequestMessage(HttpMethod.Get, "/metrics"))
        {
            good.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MetricsToken);
            using var response = await scraper.SendAsync(good);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("fleet_rollout_token_present 0\n", text);
            Assert.DoesNotContain(MetricsToken, text);
        }

        foreach (var wrong in new[] { SyncToken, "wrong", string.Empty })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, (await scraper.SendAsync(request)).StatusCode);
        }

        using var basic = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        basic.Headers.Authorization = new AuthenticationHeaderValue("Basic", MetricsToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await scraper.SendAsync(basic)).StatusCode);
    }

    // ---- the CI sync endpoint --------------------------------------------------------------

    private async Task<HttpResponseMessage> SyncAsync(HttpClient client, string? bearer, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/sync") { Content = JsonContent.Create(body) };
        if (bearer is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task The_sync_endpoint_does_not_exist_until_a_token_file_does_and_then_needs_exactly_that_token()
    {
        using var ci = _factory.CreateHttps();
        var body = new { record = ReleaseBody(), registryUsername = "ci-user", registryToken = RegistryToken };

        Assert.Equal(HttpStatusCode.NotFound, (await SyncAsync(ci, SyncToken, body)).StatusCode);

        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_sync_token"), SyncToken + "\n");
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_metrics_token"), MetricsToken + "\n");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SyncAsync(ci, null, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SyncAsync(ci, "wrong-token", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SyncAsync(ci, MetricsToken, body)).StatusCode);
        Assert.False(Get<RolloutTokenHolder>().HasToken);
        Assert.Empty(await Get<ReleaseService>().ListAsync(CancellationToken.None));

        using (var accepted = await JsonOf(await SyncAsync(ci, SyncToken, body), HttpStatusCode.OK))
        {
            Assert.False(accepted.RootElement.GetProperty("approved").GetBoolean());
            Assert.False(string.IsNullOrEmpty(accepted.RootElement.GetProperty("id").GetString()));
        }

        Assert.True(Get<RolloutTokenHolder>().HasToken);
        Assert.Single(await Get<ReleaseService>().ListAsync(CancellationToken.None));

        // Idempotent on the commit.
        using var again = await JsonOf(await SyncAsync(ci, SyncToken, body), HttpStatusCode.OK);
        Assert.Single(await Get<ReleaseService>().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_sync_with_an_invalid_payload_is_a_422_that_does_not_echo_the_token()
    {
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "fleet_sync_token"), SyncToken + "\n");
        using var ci = _factory.CreateHttps();
        var bad = new { record = new { sha = "123", runId = "1", agentRepository = "x", agentDigest = "y" }, registryUsername = "ci user", registryToken = RegistryToken };

        var response = await SyncAsync(ci, SyncToken, bad);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(RegistryToken, text);
        Assert.False(Get<RolloutTokenHolder>().HasToken);
    }

    // ---- server-sent events ----------------------------------------------------------------

    [Fact]
    public async Task The_event_stream_announces_itself_and_then_delivers_published_events_as_sse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await _client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);

        Assert.Equal(": connected", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(timeout.Token));

        Get<IEventBus>().Publish("test.event", new { id = 7, note = "hello" });

        Assert.Equal("event: test.event", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("data: {\"id\":7,\"note\":\"hello\"}", await reader.ReadLineAsync(timeout.Token));
    }

    [Fact]
    public async Task An_operation_change_reaches_a_connected_dashboard_without_any_secret_in_it()
    {
        var helper = World.Provisioner.AddHost("203.0.113.10");
        var (operation, _) = await _driver.AddAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await _client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal(": connected", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(timeout.Token));

        await Get<OperationRunner>().RunAsync(operation.Id, CancellationToken.None);

        var seen = new List<string>();
        while (!seen.Any(line => line.StartsWith("data:", StringComparison.Ordinal) && line.Contains("HostKeyPending", StringComparison.Ordinal)))
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            Assert.NotNull(line);
            seen.Add(line!);
        }

        Assert.Contains("event: operation.updated", seen);
        Assert.DoesNotContain(seen, line => line.Contains(helper.OwnerKeyText.Split('\n')[1], StringComparison.Ordinal));
    }

}
