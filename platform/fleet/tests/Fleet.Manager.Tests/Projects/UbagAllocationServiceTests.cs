using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Manager.Configuration;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Persistence;
using Fleet.Manager.Projects;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Tests.Projects;

/// <summary>
/// The UBAG project allocation (the shared manager's second consumer, OET first): the published
/// grant is min(the UBAG ceiling for the host's hardware, the hardware minus the OET budget in
/// force), only for hosts the owner listed, and never for a host that cannot safely receive work.
/// These are owner-run manual tests like the rest of this suite; no workflow executes them.
/// </summary>
public sealed class UbagAllocationServiceTests : IAsyncLifetime
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

    private async Task<UbagAllocationSnapshot?> BuildAsync(UbagOptions options)
    {
        var service = new UbagAllocationService(
            _host.Get<HostStore>(),
            _host.Get<Fleet.Manager.Operations.PolicyService>(),
            _host.Get<FleetState>(),
            _host.Get<Fleet.Manager.Projects.UbagTrustService>(),
            new OptionsWrapper<FleetOptions>(new FleetOptions { Ubag = options }),
            World.Time);
        return await service.BuildAsync(CancellationToken.None);
    }

    private static UbagOptions Enabled(params string[] hosts) => new()
    {
        Enabled = true,
        Hosts = hosts,
    };

    [Fact]
    public void The_ceiling_table_matches_the_agreed_numbers()
    {
        Assert.Equal((1500, 2560), UbagAllocationService.CeilingFor(2, 3950));
        Assert.Equal((3000, 5120), UbagAllocationService.CeilingFor(4, 7900));
        // Any other size: 75 % CPU / 62.5 % RAM.
        Assert.Equal((6000, 6250), UbagAllocationService.CeilingFor(8, 10000));
    }

    [Fact]
    public void Node_ids_and_uri_sans_are_stable_and_prefixed()
    {
        var nodeId = UbagAllocationService.NodeIdFor("01234567-89ab-cdef-0123-456789abcdef");
        Assert.Equal("ubag-01234567-89ab-cdef-0123-456789abcdef", nodeId);
        Assert.Equal("spiffe://ubag/node/" + nodeId, UbagAllocationService.UriSanFor(nodeId));
    }

    [Fact]
    public async Task Disabled_by_default_and_nothing_publishes_without_a_listed_host()
    {
        await _driver.EnrollToActiveAsync();
        Assert.Null(await BuildAsync(new UbagOptions()));
        Assert.Contains("\"allocations\":[]", (await BuildAsync(Enabled("some-other-host")))!.Body);
    }

    [Fact]
    public async Task A_listed_active_host_publishes_what_remains_after_the_oet_budget()
    {
        var helper = World.Provisioner.AddHost(EnrollmentDriver.DefaultAddress);
        helper.CpuCores = 4;
        helper.MemMiB = 7900;
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        var snapshot = await BuildAsync(Enabled(host.Id));
        Assert.NotNull(snapshot);
        using var document = JsonDocument.Parse(snapshot!.Body);
        var list = document.RootElement;
        Assert.Equal(1, list.GetProperty("schema_version").GetInt32());
        var allocation = Assert.Single(list.GetProperty("allocations").EnumerateArray());
        Assert.Equal("ubag-" + host.Id, allocation.GetProperty("node_id").GetString());
        Assert.Equal("spiffe://ubag/node/ubag-" + host.Id, allocation.GetProperty("cert_identity").GetProperty("uri_san").GetString());
        Assert.Equal("203.0.113.10:7443", allocation.GetProperty("endpoint").GetString());

        // OET's effective budget on a 4-core default policy is (4-1)*1000 CPU and 65 % of RAM;
        // UBAG gets the remainder under its own 4-core ceiling of 3000/5120.
        var policy = PolicyDefaults.ForHost(4, 7900);
        Assert.Equal(Math.Min(3000, 4000 - policy.Budgets.CpuMilli), allocation.GetProperty("cpu_millis").GetInt32());
        Assert.Equal(Math.Min(5120, 7900 - policy.Budgets.MemMiB) * 1024L * 1024L, allocation.GetProperty("memory_bytes").GetInt64());
        Assert.Equal(1, allocation.GetProperty("max_browser_workloads").GetInt32());
        Assert.False(allocation.GetProperty("voice_capable").GetBoolean());
        Assert.Equal("active", allocation.GetProperty("state").GetString());
        Assert.Equal("known", allocation.GetProperty("reservation_state").GetString());
        Assert.True(allocation.GetProperty("generation").GetInt64() > 0);

        // The ETag is the sha256 of exactly these bytes, so a revalidation round trips.
        var want = "\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot.Body))).ToLowerInvariant() + "\"";
        Assert.Equal(want, snapshot.ETag);
    }

    [Fact]
    public async Task A_draining_or_unhealthy_host_publishes_draining_and_a_removed_one_publishes_nothing()
    {
        var helper = World.Provisioner.AddHost(EnrollmentDriver.DefaultAddress);
        helper.CpuCores = 4;
        helper.MemMiB = 7900;
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        // A fresh Active host publishes active.
        Assert.Contains("\"state\":\"active\"", (await BuildAsync(Enabled(host.Id)))!.Body);

        // The API heartbeat goes stale, so the monitor's node is no longer usable.
        World.Time.Advance(TimeSpan.FromMinutes(15));
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        var snapshot = await BuildAsync(Enabled(host.Id));
        Assert.Contains("\"state\":\"draining\"", snapshot!.Body);

        // A removed host is not named at all: UBAG keeps it draining through the absence rule.
        await _host.Get<HostStore>().UpdateAsync(host.Id, h => h.Lifecycle = nameof(HostLifecycle.Removing));
        var removed = await BuildAsync(Enabled(host.Id));
        Assert.Contains("\"allocations\":[]", removed!.Body);
    }

    [Fact]
    public async Task An_oet_budget_that_fills_the_host_publishes_nothing()
    {
        var helper = World.Provisioner.AddHost(EnrollmentDriver.DefaultAddress);
        helper.CpuCores = 2;
        helper.MemMiB = 3950;
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();

        // OET claims everything its scaled default allows on the 2-core shape; nothing remains.
        var policies = _host.Get<Fleet.Manager.Operations.PolicyService>();
        var full = PolicyDefaults.ForHost(2, 3950);
        await policies.SetHostOverrideAsync(host.Id, full, "owner", CancellationToken.None);

        var snapshot = await BuildAsync(Enabled("*"));
        Assert.NotNull(snapshot);
        Assert.Contains("\"allocations\":[]", snapshot!.Body);
    }

    [Fact]
    public void Weird_regions_publish_as_default()
    {
        Assert.Equal("eu-west", UbagAllocationService.SanitizeRegion("EU-West"));
        Assert.Equal("default", UbagAllocationService.SanitizeRegion("eu west!"));
        Assert.Equal("default", UbagAllocationService.SanitizeRegion(null));
        Assert.Equal("default", UbagAllocationService.SanitizeRegion("-west"));
        Assert.Equal("default", UbagAllocationService.SanitizeRegion(new string('a', 33)));
    }
}
