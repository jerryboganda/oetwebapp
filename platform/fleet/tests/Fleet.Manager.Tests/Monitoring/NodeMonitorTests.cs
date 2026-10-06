using Fleet.Core.Domain;
using Fleet.Manager.Api;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Monitoring;

/// <summary>
/// Node health and state tracking (OET-RWP/1 section 7.4, RW-144): the OET API is the authoritative liveness view; the monitor
/// mirrors it onto the host rows, adopts lifecycle changes only when they persist, raises alerts and schedules the 14-day token rotation.
/// </summary>
public sealed class NodeMonitorTests : IAsyncLifetime
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

    private NodeMonitor Poller => _host.Get<NodeMonitor>();

    private Task PollAsync() => Poller.PollOnceAsync(CancellationToken.None);

    private async Task<(HostEntity Host, FakeHost Helper, FakeApiNode Node)> ActiveAsync()
    {
        var (operation, helper) = await _driver.EnrollToActiveAsync();
        Assert.Equal("Active", operation.State);
        return (await _driver.HostAsync(), helper, World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!);
    }

    private static List<FleetEvent> Drain(EventSubscription subscription)
    {
        var events = new List<FleetEvent>();
        while (subscription.Reader.TryRead(out var next))
        {
            events.Add(next);
        }

        return events;
    }

    private async Task<bool> AuditedAsync(string action) =>
        (await _host.Get<IAuditService>().ListAsync(500)).Any(r => r.Action == action);

    private async Task<IReadOnlyList<OperationEntity>> RotationsAsync(string hostId) =>
        (await _host.Get<OperationStore>().ListAsync(50, hostId, CancellationToken.None)).Where(o => o.Kind == "rotate-token").ToList();

    // ---- mirroring -------------------------------------------------------------------------

    [Fact]
    public async Task A_poll_mirrors_the_api_view_onto_the_host_row_and_the_fleet_picture()
    {
        var (_, _, node) = await ActiveAsync();
        World.Time.Advance(TimeSpan.FromSeconds(10));

        await PollAsync();

        var state = _host.Get<FleetState>();
        Assert.True(state.ApiReachable);
        Assert.Equal<DateTimeOffset?>(World.Time.GetUtcNow(), state.LastPollAt);
        Assert.Equal(node.Id, Assert.Single(state.Nodes).Id);

        var row = await _driver.HostAsync();
        Assert.Equal<DateTimeOffset?>(World.Time.GetUtcNow(), row.LastStatusAt);
        Assert.Contains("\"status\":\"Active\"", row.LastStatusJson);
        Assert.Contains("\"health\":\"Online\"", row.LastStatusJson);
        Assert.Contains(FleetWorld.AgentDigest, row.LastStatusJson);
        Assert.Equal(node.PolicyRevision, row.DesiredRevision);
        Assert.Equal(row.DesiredRevision, row.AppliedRevision);
        Assert.Equal(FleetWorld.AgentDigest, row.AgentDigest);
    }

    [Fact]
    public async Task The_helper_is_asked_for_its_restricted_status_at_most_every_five_minutes()
    {
        var (_, helper, _) = await ActiveAsync();
        int StatusCalls() => helper.CtlLog.Count(call => call.Verb == "status");

        await PollAsync();
        var afterFirst = StatusCalls();
        var row = await _driver.HostAsync();
        Assert.Contains("oet-fleet-ctl.status/1", row.LastStatusJson);

        World.Time.Advance(TimeSpan.FromMinutes(1));
        await PollAsync();
        Assert.Equal(afterFirst, StatusCalls());

        World.Time.Advance(TimeSpan.FromMinutes(5));
        await PollAsync();
        Assert.Equal(afterFirst + 1, StatusCalls());
    }

    [Fact]
    public async Task An_unreachable_api_keeps_the_last_known_picture_and_is_reported()
    {
        await ActiveAsync();
        await PollAsync();
        using var subscription = _host.Get<IEventBus>().Subscribe();
        var state = _host.Get<FleetState>();

        World.Api.Reachable = false;
        await PollAsync();

        Assert.False(state.ApiReachable);
        Assert.Equal(FleetApiException.Unreachable, state.LastApiError);
        Assert.Single(state.Nodes);
        Assert.Contains(Drain(subscription), fleetEvent => fleetEvent.Type == "api.unreachable");

        World.Api.Reachable = true;
        await PollAsync();
        Assert.True(state.ApiReachable);
        Assert.Null(state.LastApiError);
    }

    [Fact]
    public async Task A_status_change_is_published_once_not_on_every_poll()
    {
        var (_, _, node) = await ActiveAsync();
        await PollAsync();
        using var subscription = _host.Get<IEventBus>().Subscribe();

        await PollAsync();
        await PollAsync();
        Assert.DoesNotContain(Drain(subscription), fleetEvent => fleetEvent.Type == "node.updated");

        node.LeaseCount = 2;
        await PollAsync();
        await PollAsync();
        Assert.Single(Drain(subscription), fleetEvent => fleetEvent.Type == "node.updated");
    }

    // ---- lifecycle adoption ----------------------------------------------------------------

    [Fact]
    public async Task A_lifecycle_the_api_disagrees_with_is_adopted_only_after_two_consecutive_polls()
    {
        var (_, _, node) = await ActiveAsync();
        await PollAsync();

        await World.Api.DisableAsync(node.Id, CancellationToken.None);
        await PollAsync();
        Assert.Equal("Active", (await _driver.HostAsync()).Lifecycle);

        await PollAsync();
        Assert.Equal("Disabled", (await _driver.HostAsync()).Lifecycle);
        Assert.True(await AuditedAsync("host.lifecycle_synced"));

        await World.Api.EnableAsync(node.Id, CancellationToken.None);
        await PollAsync();
        await PollAsync();
        Assert.Equal("Active", (await _driver.HostAsync()).Lifecycle);
    }

    [Fact]
    public async Task A_disagreement_that_does_not_persist_is_never_adopted()
    {
        var (_, _, node) = await ActiveAsync();
        await PollAsync();

        await World.Api.DisableAsync(node.Id, CancellationToken.None);
        await PollAsync();
        await World.Api.EnableAsync(node.Id, CancellationToken.None);
        await PollAsync();
        await World.Api.DisableAsync(node.Id, CancellationToken.None);
        await PollAsync();

        Assert.Equal("Active", (await _driver.HostAsync()).Lifecycle);
        Assert.False(await AuditedAsync("host.lifecycle_synced"));
    }

    [Fact]
    public async Task A_quarantined_node_raises_one_alert_that_clears_when_the_node_recovers()
    {
        var (_, _, node) = await ActiveAsync();
        using var subscription = _host.Get<IEventBus>().Subscribe();

        await World.Api.QuarantineAsync(node.Id, CancellationToken.None);
        await PollAsync();
        Assert.Equal("quarantined", (await _driver.HostAsync()).Alert);
        Assert.Single(Drain(subscription), fleetEvent => fleetEvent.Type == "alert.raised");

        await PollAsync();
        var quarantinedRow = await _driver.HostAsync();
        Assert.Equal("Disabled", quarantinedRow.Lifecycle);
        Assert.Equal("quarantined", quarantinedRow.Alert);
        Assert.DoesNotContain(Drain(subscription), fleetEvent => fleetEvent.Type == "alert.raised");

        // Release from quarantine needs a passing canary newer than the quarantine itself.
        node.LastCanary = new FakeCanary(World.Time.GetUtcNow(), true);
        await World.Api.EnableAsync(node.Id, CancellationToken.None);
        await PollAsync();
        Assert.Null((await _driver.HostAsync()).Alert);
        await PollAsync();
        Assert.Equal("Active", (await _driver.HostAsync()).Lifecycle);
    }

    // ---- host key --------------------------------------------------------------------------

    [Fact]
    public async Task A_changed_host_key_found_by_the_status_poll_disables_the_host_and_never_clears_by_itself()
    {
        var (host, helper, node) = await ActiveAsync();
        helper.KeyBlob = FakeKeys.HostKeyBlob("rebuilt-or-intercepted");
        using var subscription = _host.Get<IEventBus>().Subscribe();

        await PollAsync();

        var row = await _driver.HostAsync();
        Assert.Equal("Disabled", row.Lifecycle);
        Assert.Equal(FailureReasons.HostKeyChanged, row.Alert);
        Assert.Equal("Disabled", node.Status);
        Assert.Contains(Drain(subscription), fleetEvent => fleetEvent.Type == "alert.raised");
        Assert.True(await AuditedAsync("host.host_key_changed"));

        World.Time.Advance(TimeSpan.FromMinutes(6));
        await PollAsync();
        await PollAsync();
        var later = await _driver.HostAsync();
        Assert.Equal(FailureReasons.HostKeyChanged, later.Alert);
        Assert.Equal("Disabled", later.Lifecycle);

        var refusal = await Assert.ThrowsAsync<FleetOperationException>(
            () => _host.Get<HostService>().StartEnableAsync(host.Id, "owner", CancellationToken.None));
        Assert.Equal("invalid_state", refusal.Code);
    }

    // ---- token rotation schedule -----------------------------------------------------------

    [Fact]
    public async Task The_node_token_is_rotated_after_fourteen_days_exactly_once()
    {
        var (host, helper, node) = await ActiveAsync();
        var firstToken = Assert.Single(node.Tokens).Value;

        World.Time.Advance(TimeSpan.FromDays(13));
        await PollAsync();
        Assert.Empty(await RotationsAsync(host.Id));

        World.Time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await PollAsync();
        await PollAsync();
        var rotation = Assert.Single(await RotationsAsync(host.Id));
        Assert.Equal("Queued", rotation.State);

        var finished = await _driver.RunAsync(rotation.Id);
        Assert.Equal("Succeeded", finished.State);
        Assert.NotEqual(firstToken, helper.EnvText!.Split('\n').Single(l => l.StartsWith("OET_NODE_TOKEN=", StringComparison.Ordinal))["OET_NODE_TOKEN=".Length..]);

        await PollAsync();
        Assert.Single(await RotationsAsync(host.Id));
    }

    [Fact]
    public async Task A_failed_rotation_stays_visible_instead_of_respawning_every_poll()
    {
        var (host, _, _) = await ActiveAsync();
        World.Provisioner.FailCtlOnce["put-env"] = new CtlResult(false, null, "disk full", "{\"ok\":false,\"error\":\"disk full\"}", 1);
        World.Time.Advance(TimeSpan.FromDays(15));

        await PollAsync();
        var rotation = Assert.Single(await RotationsAsync(host.Id));
        var failed = await _driver.RunAsync(rotation.Id);
        Assert.Equal("Failed", failed.State);

        await PollAsync();
        await PollAsync();

        Assert.Single(await RotationsAsync(host.Id));
    }

    // ---- pressure governor -----------------------------------------------------------------

    [Fact]
    public async Task Sustained_node_load_reduces_capacity_and_a_continuous_calm_restores_it_one_step_at_a_time()
    {
        var (_, _, node) = await ActiveAsync();
        node.CpuPct = 90;

        await PollAsync();
        var governor = Poller.GovernorFor(node.Id)!;
        Assert.Equal(2, governor.EffectiveConcurrency);

        World.Time.Advance(TimeSpan.FromSeconds(15));
        await PollAsync();
        Assert.Equal(1, governor.EffectiveConcurrency);
        Assert.True(governor.IsReduced);
        Assert.Equal("reduced", governor.Pressure);

        node.CpuPct = 20;
        await PollAsync();
        World.Time.Advance(TimeSpan.FromSeconds(119));
        await PollAsync();
        Assert.Equal(1, governor.EffectiveConcurrency);

        World.Time.Advance(TimeSpan.FromSeconds(2));
        await PollAsync();
        Assert.Equal(2, governor.EffectiveConcurrency);
        Assert.False(governor.IsReduced);
    }

    [Fact]
    public async Task A_node_the_monitor_has_never_seen_has_no_governor()
    {
        await ActiveAsync();
        Assert.Null(Poller.GovernorFor("rw_unknown"));
    }
}
