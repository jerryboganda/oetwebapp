using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Operations;

/// <summary>The single writer of node policy (OET-RWP/1 section 8.4): precedence, validation, optimistic revisions and the approved-image window.</summary>
public sealed class PolicyServiceTests : IAsyncLifetime
{
    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private FleetWorld World => _host.World;

    private PolicyService Policies => _host.Get<PolicyService>();

    private static string Digest(int number) => "sha256:" + new string((char)('a' + number), 64);

    private static string ImageId(int number) => "sha256:" + new string((char)('0' + number), 64);

    private async Task<bool> AuditedAsync(string action)
    {
        var rows = await _host.Get<IAuditService>().ListAsync(500);
        return rows.Any(r => r.Action == action);
    }

    [Fact]
    public async Task Without_a_stored_policy_a_small_host_gets_a_policy_scaled_to_its_own_facts()
    {
        var helper = World.Provisioner.AddHost(EnrollmentDriver.DefaultAddress);
        helper.CpuCores = 2;
        helper.MemMiB = 2048;
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        var expected = PolicyDefaults.ForHost(2, 2048);
        var effective = await Policies.EffectiveAsync(host, CancellationToken.None);

        Assert.Equal(expected.Budgets, effective.Budgets);
        Assert.Equal(expected.MaxConcurrency, effective.MaxConcurrency);
        Assert.NotEqual(PolicyDefaults.Global().Budgets, effective.Budgets);

        // That is also what the node was registered with, so a small VPS never starts with the 4 vCPU budgets.
        var registered = World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.InitialPolicy!;
        Assert.Equal(expected.Budgets, registered.Budgets);
        Assert.Equal(expected.MaxConcurrency, registered.MaxConcurrency);
    }

    [Fact]
    public async Task The_host_override_beats_the_global_policy_which_beats_the_scaled_default()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();
        Assert.Equal(PolicyDefaults.ForHost(4, 7900).Budgets, (await Policies.EffectiveAsync(host, CancellationToken.None)).Budgets);
        Assert.Null(await Policies.GetStoredGlobalAsync(CancellationToken.None));

        var global = PolicyDefaults.Global() with
        {
            MaxConcurrency = 1,
            PerKind = new Dictionary<string, int> { ["pdf.extract"] = 1 },
            Budgets = new Budgets(2000, 2048, 1024),
        };
        await Policies.SetGlobalAsync(global, "owner", CancellationToken.None);
        Assert.Equal(global.Budgets, (await Policies.EffectiveAsync(host, CancellationToken.None)).Budgets);

        var hostPolicy = global with
        {
            MaxConcurrency = 2,
            PerKind = new Dictionary<string, int> { ["pdf.extract"] = 2 },
            Budgets = new Budgets(1500, 1536, 768),
        };
        await Policies.SetHostOverrideAsync(host.Id, hostPolicy, "owner", CancellationToken.None);
        var effective = await Policies.EffectiveAsync(host, CancellationToken.None);
        Assert.Equal(hostPolicy.Budgets, effective.Budgets);
        Assert.Equal(2, effective.MaxConcurrency);
        Assert.Equal(1, (await Policies.GetGlobalAsync(CancellationToken.None)).MaxConcurrency);

        await Policies.ClearHostOverrideAsync(host.Id, "owner", CancellationToken.None);
        Assert.Null(await Policies.GetHostOverrideAsync(host.Id, CancellationToken.None));
        Assert.Equal(global.Budgets, (await Policies.EffectiveAsync(host, CancellationToken.None)).Budgets);

        Assert.True(await AuditedAsync("policy.global_set"));
        Assert.True(await AuditedAsync("policy.host_set"));
        Assert.True(await AuditedAsync("policy.host_cleared"));
    }

    [Fact]
    public async Task The_effective_policy_always_carries_the_approved_image_window()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        var effective = await Policies.EffectiveAsync(host, CancellationToken.None);

        Assert.Equal(FleetWorld.AgentDigest, Assert.Single(effective.AgentImage.ApprovedDigests));
        Assert.Equal(FleetWorld.AgentDigest, effective.AgentImage.Target);
        Assert.Equal("1.0.0", effective.AgentImage.MinVersion);

        // A stored policy cannot drop it, whatever it contains.
        await Policies.SetGlobalAsync(PolicyDefaults.Global(), "owner", CancellationToken.None);
        var stored = await Policies.EffectiveAsync(host, CancellationToken.None);
        Assert.Equal(FleetWorld.AgentDigest, Assert.Single(stored.AgentImage.ApprovedDigests));
    }

    [Fact]
    public async Task Out_of_range_policies_and_unknown_kinds_are_refused_and_nothing_is_stored()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        var tooMany = PolicyDefaults.Global() with { MaxConcurrency = 9, PerKind = new Dictionary<string, int> { ["pdf.extract"] = 2 } };
        await Assert.ThrowsAsync<FleetValidationException>(() => Policies.SetGlobalAsync(tooMany, "owner", CancellationToken.None));
        await Assert.ThrowsAsync<FleetValidationException>(() => Policies.SetHostOverrideAsync(host.Id, tooMany, "owner", CancellationToken.None));

        // The API's kind registry limits what a policy may allow.
        var unknownKind = PolicyDefaults.Global() with
        {
            AllowedKinds = new[] { "media.audio-extract" },
            PerKind = new Dictionary<string, int> { ["media.audio-extract"] = 1 },
        };
        await Assert.ThrowsAsync<FleetValidationException>(() => Policies.SetGlobalAsync(unknownKind, "owner", CancellationToken.None));

        var unknownHost = await Assert.ThrowsAsync<FleetValidationException>(
            () => Policies.SetHostOverrideAsync("no-such-host", PolicyDefaults.Global(), "owner", CancellationToken.None));
        Assert.Contains(unknownHost.Issues, issue => issue.Code == "host_not_found");

        Assert.Null(await Policies.GetStoredGlobalAsync(CancellationToken.None));
        Assert.Null(await Policies.GetHostOverrideAsync(host.Id, CancellationToken.None));
        Assert.False(await AuditedAsync("policy.global_set"));
    }

    [Fact]
    public async Task A_stale_revision_is_re_read_and_the_push_is_retried_once()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();
        var node = World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!;
        World.Api.BumpRevisionOnNextPut = true;
        var putsBefore = World.Api.PutPolicyCalls;

        var revision = await Policies.PushAsync(host, CancellationToken.None);

        Assert.Equal(putsBefore + 2, World.Api.PutPolicyCalls);
        Assert.Equal(node.PolicyRevision, revision);
        Assert.Equal(revision, (await _driver.HostAsync()).DesiredRevision);
    }

    [Fact]
    public async Task Pushing_a_host_without_a_node_is_a_programming_error_not_a_silent_success()
    {
        await _driver.AddAsync();
        var host = await _driver.HostAsync();
        Assert.Null(host.ApiNodeId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Policies.PushAsync(host, CancellationToken.None));
    }

    [Fact]
    public async Task Push_all_reports_every_node_and_one_failure_never_stops_the_others()
    {
        await _driver.EnrollToActiveAsync("helper-eu-01", "203.0.113.10");
        await _driver.EnrollToActiveAsync("helper-eu-02", "203.0.113.11");
        World.Api.RejectPolicyForNodeRefs.Add("helper-eu-01");

        var results = await Policies.PushAllAsync("owner", CancellationToken.None);

        Assert.Equal(2, results.Count);
        var rejected = results.Single(r => r.NodeRef == "helper-eu-01");
        Assert.False(rejected.Success);
        Assert.Equal("policy_rejected", rejected.Error);
        var accepted = results.Single(r => r.NodeRef == "helper-eu-02");
        Assert.True(accepted.Success);
        Assert.NotNull(accepted.Revision);

        var entry = (await _host.Get<IAuditService>().ListAsync(20)).First(r => r.Action == "policy.push_all");
        Assert.Contains("\"failed\":1", entry.DetailsJson);
        Assert.Contains("\"pushed\":1", entry.DetailsJson);
    }

    [Fact]
    public async Task The_approved_image_window_is_the_newest_digest_plus_the_previous_two()
    {
        var releases = _host.Get<ReleaseService>();
        Assert.Null((await releases.AgentImagePolicyAsync(CancellationToken.None)).Target);

        for (var number = 1; number <= 4; number++)
        {
            World.Time.Advance(TimeSpan.FromMinutes(1));
            await _driver.ApproveReleaseAsync((char)('0' + number), Digest(number), ImageId(number));
        }

        var window = await releases.AgentImagePolicyAsync(CancellationToken.None);

        Assert.Equal(new[] { Digest(4), Digest(3), Digest(2) }, window.ApprovedDigests.ToArray());
        Assert.Equal(Digest(4), window.Target);
        Assert.Equal("1.0.0", window.MinVersion);
        Assert.Empty(PolicyValidator.Validate(PolicyDefaults.WithAgentImage(PolicyDefaults.Global(), window)));
    }
}
