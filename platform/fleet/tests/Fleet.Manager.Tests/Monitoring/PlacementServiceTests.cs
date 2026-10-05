using Fleet.Core.Placement;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Monitoring;

/// <summary>"Where would this work go?" over the live fleet picture, each node's effective policy and the primary's pressure (OET-RWP/1 section 3.8).</summary>
public sealed class PlacementServiceTests : IAsyncLifetime
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

    private PlacementService Placement => _host.Get<PlacementService>();

    private FakePressureSource Pressure => (FakePressureSource)_host.Get<IPrimaryPressureSource>();

    private static PlacementRequest Job(string purpose = "apply") =>
        new("pdf.extract", 1, FakeFleetApi.Engine, 1, 1000, 2048, 256, purpose);

    private async Task<FakeApiNode> ActiveNodeAsync()
    {
        var (operation, _) = await _driver.EnrollToActiveAsync();
        Assert.Equal("Active", operation.State);
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        return World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!;
    }

    [Fact]
    public async Task Work_goes_to_the_active_node_and_a_reservation_stops_it_being_promised_twice()
    {
        var node = await ActiveNodeAsync();

        var first = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(PlacementKind.Remote, first.Kind);
        Assert.Equal(node.Id, first.NodeId);
        Assert.NotNull(first.ReservationId);

        // The node reports one free heavy slot: the second concurrent decision must not claim it again.
        var second = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.NotEqual(PlacementKind.Remote, second.Kind);
        Assert.Contains(second.Rejections, rejection => rejection.EndsWith(":no_capacity", StringComparison.Ordinal));

        Assert.True(Placement.Release(first.ReservationId!));
        var third = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(PlacementKind.Remote, third.Kind);
    }

    [Fact]
    public async Task A_reservation_that_nobody_claims_expires_by_itself()
    {
        await ActiveNodeAsync();
        var first = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(PlacementKind.Remote, first.Kind);

        World.Time.Advance(TimeSpan.FromSeconds(46));

        Assert.Equal(PlacementKind.Remote, (await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task Without_a_node_the_primary_does_the_work_only_when_it_has_headroom()
    {
        await ActiveNodeAsync();
        var held = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(PlacementKind.Remote, held.Kind);

        // Calm primary: local fallback.
        var calm = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(PlacementKind.Local, calm.Kind);
        Assert.Equal("local_headroom", calm.Reason);

        // Busy primary: wait, until the hard limit of an hour has passed.
        Pressure.Pressure = new PrimaryPressure(80, 0.5, 40);
        var busy = await Placement.DecideAsync(Job(), TimeSpan.FromMinutes(10), CancellationToken.None);
        Assert.Equal(PlacementKind.Wait, busy.Kind);
        var overdue = await Placement.DecideAsync(Job(), TimeSpan.FromMinutes(61), CancellationToken.None);
        Assert.Equal(PlacementKind.Local, overdue.Kind);
        Assert.Equal("local_hard_after", overdue.Reason);
    }

    [Fact]
    public async Task Unreadable_pressure_means_no_headroom()
    {
        await ActiveNodeAsync();
        await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);
        Pressure.Pressure = null;

        var decision = await Placement.DecideAsync(Job(), TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(PlacementKind.Wait, decision.Kind);
    }

    [Fact]
    public async Task A_draining_node_receives_no_new_work()
    {
        var node = await ActiveNodeAsync();
        await World.Api.DrainAsync(node.Id, CancellationToken.None);
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

        var decision = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(PlacementKind.Local, decision.Kind);
        Assert.Contains(decision.Rejections, rejection => rejection.EndsWith(":status_Draining", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_canary_never_falls_back_to_the_primary()
    {
        var node = await ActiveNodeAsync();
        await World.Api.DrainAsync(node.Id, CancellationToken.None);
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

        var decision = await Placement.DecideAsync(Job("canary"), TimeSpan.FromMinutes(120), CancellationToken.None);

        Assert.Equal(PlacementKind.Wait, decision.Kind);
        Assert.Equal("canary_no_target", decision.Reason);
    }

    [Fact]
    public async Task An_unknown_engine_version_or_kind_is_not_placed_on_a_node_that_does_not_advertise_it()
    {
        await ActiveNodeAsync();

        var wrongEngine = await Placement.DecideAsync(Job() with { EngineVersion = "other-engine:9" }, TimeSpan.Zero, CancellationToken.None);
        Assert.NotEqual(PlacementKind.Remote, wrongEngine.Kind);
        Assert.Contains(wrongEngine.Rejections, rejection => rejection.EndsWith(":kind_version_mismatch", StringComparison.Ordinal));

        var wrongKind = await Placement.DecideAsync(Job() with { Kind = "media.audio-extract" }, TimeSpan.Zero, CancellationToken.None);
        Assert.NotEqual(PlacementKind.Remote, wrongKind.Kind);
        Assert.Contains(wrongKind.Rejections, rejection => rejection.EndsWith(":kind_not_allowed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Before_the_first_poll_there_is_no_picture_and_nothing_is_placed_remotely()
    {
        await _driver.EnrollToActiveAsync();

        var decision = await Placement.DecideAsync(Job(), TimeSpan.Zero, CancellationToken.None);

        Assert.NotEqual(PlacementKind.Remote, decision.Kind);
        Assert.Empty(await Placement.SnapshotsAsync(CancellationToken.None));
    }
}
