using Fleet.Core.Placement;
using Fleet.Core.Policy;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Placement;

/// <summary>OET-RWP/1 section 3.8: eligibility, capacity reservation, per-kind caps, drain, fallback only with headroom.</summary>
public sealed class PlacementEngineTests
{
    private const string Engine = FakeFleetApi.Engine;

    private readonly ManualTimeProvider _time = new();

    private PlacementEngine NewEngine(IReadOnlySet<string>? enabledKinds = null, TimeSpan? ttl = null) =>
        new(new CapacityReservations(_time, ttl), new PlacementOptions { EnabledKinds = enabledKinds });

    private static PlacementRequest Job(string kind = "pdf.extract", int weight = 1, string purpose = "apply", string? target = null) =>
        new(kind, 1, Engine, weight, 1000, 2048, 256, purpose, target);

    private static NodeSnapshot Node(
        string id,
        string status = "Active",
        string health = "Online",
        int leased = 0,
        int max = 2,
        int effective = 2,
        int slotsFree = 2,
        int memFree = 4000,
        int cpuFree = 2000,
        int tmpFree = 2900,
        string[]? kinds = null,
        string engine = Engine,
        bool paused = false,
        IReadOnlyDictionary<string, int>? leasedByKind = null,
        IReadOnlyDictionary<string, int>? perKind = null)
    {
        var allowed = kinds ?? new[] { "pdf.extract" };
        var policy = PolicyDefaults.Global() with
        {
            AllowedKinds = allowed,
            MaxConcurrency = max,
            PerKind = perKind ?? allowed.ToDictionary(k => k, _ => max),
        };
        return new NodeSnapshot(
            id,
            id,
            status,
            health,
            paused,
            policy,
            allowed.Select(k => new AgentKind(k, new[] { 1 }, engine)).ToList(),
            new NodeCapacity(cpuFree, memFree, tmpFree, slotsFree, effective),
            leased,
            leasedByKind);
    }

    private static PrimaryPressure Calm => new(5, 0.5, 40);

    [Fact]
    public void Work_goes_to_the_node_with_the_lowest_normalised_load()
    {
        var engine = NewEngine();
        var nodes = new[] { Node("busy", leased: 1, slotsFree: 1), Node("idle", leased: 0), Node("half", leased: 1, max: 4, effective: 4, slotsFree: 3) };

        var decision = engine.Decide(Job(), nodes, Calm, TimeSpan.Zero);

        Assert.Equal(PlacementKind.Remote, decision.Kind);
        Assert.Equal("idle", decision.NodeId);
        Assert.NotNull(decision.ReservationId);
    }

    [Fact]
    public void A_tie_prefers_more_free_capacity_and_then_the_node_reference()
    {
        var engine = NewEngine();
        var two = Node("b-node", max: 2, effective: 2, slotsFree: 2);
        var four = Node("c-node", max: 4, effective: 4, slotsFree: 4);
        Assert.Equal("c-node", engine.Decide(Job(), new[] { two, four }, Calm, TimeSpan.Zero).NodeId);

        var engineB = NewEngine();
        var a = Node("a-node");
        var b = Node("b-node");
        Assert.Equal("a-node", engineB.Decide(Job(), new[] { b, a }, Calm, TimeSpan.Zero).NodeId);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Probation")]
    [InlineData("Draining")]
    [InlineData("Disabled")]
    [InlineData("Quarantined")]
    [InlineData("Revoked")]
    public void Only_active_nodes_take_ordinary_work(string status)
    {
        var engine = NewEngine();
        var node = Node("n1", status: status);
        Assert.False(engine.IsEligible(node, Job(), out var reason));
        Assert.Equal("status_" + status, reason);
        Assert.Equal(PlacementKind.Local, engine.Decide(Job(), new[] { node }, Calm, TimeSpan.Zero).Kind);
    }

    [Theory]
    [InlineData("Stale")]
    [InlineData("Offline")]
    [InlineData("Unseen")]
    public void A_node_that_is_not_online_gets_nothing(string health)
    {
        Assert.False(NewEngine().IsEligible(Node("n1", health: health), Job(), out var reason));
        Assert.Equal("health_" + health, reason);
    }

    [Fact]
    public void A_paused_node_a_foreign_kind_a_version_mismatch_and_a_flag_off_all_exclude_the_node()
    {
        var engine = NewEngine();
        Assert.False(engine.IsEligible(Node("n1", paused: true), Job(), out var paused));
        Assert.Equal("paused", paused);

        Assert.False(engine.IsEligible(Node("n1"), Job("media.audio-extract"), out var notAllowed));
        Assert.Equal("kind_not_allowed", notAllowed);

        Assert.False(engine.IsEligible(Node("n1", engine: "pdfpig:other"), Job(), out var version));
        Assert.Equal("kind_version_mismatch", version);

        var flagged = NewEngine(enabledKinds: new HashSet<string> { "companion.index-prep" });
        Assert.False(flagged.IsEligible(Node("n1"), Job(), out var flag));
        Assert.Equal("kind_flag_off", flag);

        Assert.True(NewEngine().IsEligible(Node("n1"), Job(), out var ok));
        Assert.Equal("eligible", ok);
    }

    [Fact]
    public void Capacity_is_the_smaller_of_the_policy_cap_and_the_effective_concurrency_the_agent_reports()
    {
        var engine = NewEngine();
        var reduced = Node("n1", max: 4, effective: 1, leased: 1, slotsFree: 0);
        Assert.False(engine.IsEligible(reduced, Job(), out var reason));
        Assert.Equal("no_capacity", reason);

        Assert.True(engine.IsEligible(Node("n2", max: 4, effective: 4, leased: 1, slotsFree: 3), Job(), out _));
    }

    [Fact]
    public void Reservations_stop_two_producers_from_oversubscribing_one_slot()
    {
        var engine = NewEngine();
        var nodes = new[] { Node("n1", max: 1, effective: 1, slotsFree: 1) };

        var first = engine.Decide(Job(), nodes, Calm, TimeSpan.Zero);
        var second = engine.Decide(Job(), nodes, Calm, TimeSpan.Zero);

        Assert.Equal(PlacementKind.Remote, first.Kind);
        Assert.NotEqual(PlacementKind.Remote, second.Kind);
        Assert.Contains(second.Rejections, r => r.StartsWith("n1:"));
    }

    [Fact]
    public void A_release_or_the_ttl_gives_the_reserved_capacity_back()
    {
        var engine = NewEngine(ttl: TimeSpan.FromSeconds(30));
        var nodes = new[] { Node("n1", max: 1, effective: 1, slotsFree: 1) };

        var first = engine.Decide(Job(), nodes, Calm, TimeSpan.Zero);
        Assert.Equal(PlacementKind.Remote, first.Kind);
        Assert.Equal(1, engine.Reservations.Count);

        Assert.True(engine.Release(first.ReservationId!));
        Assert.Equal(PlacementKind.Remote, engine.Decide(Job(), nodes, Calm, TimeSpan.Zero).Kind);

        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(0, engine.Reservations.Count);
        Assert.Equal(PlacementKind.Remote, engine.Decide(Job(), nodes, Calm, TimeSpan.Zero).Kind);
    }

    [Fact]
    public void Per_kind_concurrency_is_enforced_with_leased_and_reserved_weight()
    {
        var engine = NewEngine();
        var node = Node(
            "n1",
            max: 4,
            effective: 4,
            slotsFree: 4,
            kinds: new[] { "pdf.extract", "media.audio-extract" },
            perKind: new Dictionary<string, int> { ["pdf.extract"] = 1, ["media.audio-extract"] = 2 },
            leasedByKind: new Dictionary<string, int> { ["pdf.extract"] = 1 });

        Assert.False(engine.IsEligible(node, Job("pdf.extract"), out var reason));
        Assert.Equal("kind_limit", reason);
        Assert.True(engine.IsEligible(node, Job("media.audio-extract"), out _));

        var open = Node("n2", max: 4, effective: 4, slotsFree: 4, perKind: new Dictionary<string, int> { ["pdf.extract"] = 1 });
        Assert.Equal(PlacementKind.Remote, engine.Decide(Job(), new[] { open }, Calm, TimeSpan.Zero).Kind);
        Assert.NotEqual(PlacementKind.Remote, engine.Decide(Job(), new[] { open }, Calm, TimeSpan.Zero).Kind);
    }

    [Theory]
    [InlineData(2300, 2000, 2900, "memory (job needs 2048 + 256 tmpfs)")]
    [InlineData(4000, 900, 2900, "cpu")]
    [InlineData(4000, 2000, 100, "tmp")]
    public void Resource_budgets_must_cover_the_job(int memFree, int cpuFree, int tmpFree, string what)
    {
        Assert.False(NewEngine().IsEligible(Node("n1", memFree: memFree, cpuFree: cpuFree, tmpFree: tmpFree), Job(), out var reason), what);
        Assert.Equal("no_capacity", reason);
    }

    [Fact]
    public void A_canary_may_only_run_on_a_probation_node_and_never_falls_back_to_the_primary()
    {
        var engine = NewEngine();
        var probation = Node("n1", status: "Probation");
        var active = Node("n2");

        Assert.True(engine.IsEligible(probation, Job(purpose: "canary"), out _));
        Assert.False(engine.IsEligible(probation, Job(), out _));

        var targeted = Job(purpose: "canary", target: "n1");
        Assert.Equal("n1", engine.Decide(targeted, new[] { active, probation }, Calm, TimeSpan.Zero).NodeId);

        var none = engine.Decide(Job(purpose: "canary", target: "missing"), new[] { active }, Calm, TimeSpan.FromHours(3));
        Assert.Equal(PlacementKind.Wait, none.Kind);
        Assert.Equal("canary_no_target", none.Reason);
    }

    [Fact]
    public void The_primary_is_used_only_with_headroom_or_after_the_hard_wait()
    {
        var engine = NewEngine();
        var empty = Array.Empty<NodeSnapshot>();

        var withRoom = engine.Decide(Job(), empty, Calm, TimeSpan.Zero);
        Assert.Equal(PlacementKind.Local, withRoom.Kind);
        Assert.Equal("local_headroom", withRoom.Reason);

        foreach (var pressured in new PrimaryPressure?[]
                 {
                     new(30, 0.5, 40),
                     new(5, 6, 40),
                     new(5, 0.5, 80),
                     new(null, 0.5, 40),
                     null,
                 })
        {
            Assert.Equal(PlacementKind.Wait, engine.Decide(Job(), empty, pressured, TimeSpan.FromMinutes(5)).Kind);
        }

        var hard = engine.Decide(Job(), empty, new PrimaryPressure(90, 20, 95), TimeSpan.FromMinutes(61));
        Assert.Equal(PlacementKind.Local, hard.Kind);
        Assert.Equal("local_hard_after", hard.Reason);
    }

    [Fact]
    public void Headroom_thresholds_are_exclusive_upper_bounds_and_unreadable_means_none()
    {
        Assert.True(PrimaryHeadroom.HasHeadroom(new PrimaryPressure(24.99, 4.99, 74.99)));
        Assert.False(PrimaryHeadroom.HasHeadroom(new PrimaryPressure(25, 1, 10)));
        Assert.False(PrimaryHeadroom.HasHeadroom(new PrimaryPressure(1, 5, 10)));
        Assert.False(PrimaryHeadroom.HasHeadroom(new PrimaryPressure(1, 1, 75)));
        Assert.False(PrimaryHeadroom.HasHeadroom(null));
    }

    [Fact]
    public async Task Concurrent_decisions_never_oversubscribe_a_node()
    {
        var engine = NewEngine();
        var nodes = new[] { Node("n1", max: 3, effective: 3, slotsFree: 3, memFree: 100_000, cpuFree: 100_000, tmpFree: 100_000) };

        var decisions = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => engine.Decide(Job(), nodes, null, TimeSpan.Zero))));

        Assert.Equal(3, decisions.Count(d => d.Kind == PlacementKind.Remote));
    }
}
