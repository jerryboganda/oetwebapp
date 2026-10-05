using System.Text.RegularExpressions;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Monitoring;

/// <summary>The Prometheus text format, what <c>/metrics</c> may and may not say, and the data behind <c>/healthz</c>.</summary>
public sealed class ObservabilityTests : IAsyncLifetime
{
    private static readonly Regex SampleLine = new(@"\A[a-zA-Z_:][a-zA-Z0-9_:]*(\{[^}]*\})? \S+\z", RegexOptions.CultureInvariant);

    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public void The_metrics_writer_declares_each_family_once_and_escapes_labels_and_help()
    {
        var writer = new MetricsWriter();
        writer.Gauge("fleet_x", "help \\ with\nnewline", 1.5, ("node", "a\"b\\c\nd"));
        writer.Gauge("fleet_x", "ignored second help", 2, ("node", "plain"));

        var text = writer.ToString();

        Assert.Equal(1, Regex.Matches(text, "# TYPE fleet_x gauge").Count);
        Assert.Contains("# HELP fleet_x help \\\\ with\\nnewline\n", text);
        Assert.Contains("fleet_x{node=\"a\\\"b\\\\c\\nd\"} 1.5\n", text);
        Assert.Contains("fleet_x{node=\"plain\"} 2\n", text);
    }

    [Fact]
    public void The_metrics_writer_refuses_names_that_could_break_the_format()
    {
        var writer = new MetricsWriter();

        Assert.Throws<ArgumentException>(() => writer.Gauge("bad name", "help", 1));
        Assert.Throws<ArgumentException>(() => writer.Gauge("1starts_with_digit", "help", 1));
        Assert.Throws<ArgumentException>(() => writer.Gauge("fine", "help", 1, ("bad-label", "v")));
    }

    [Fact]
    public async Task The_scrape_body_describes_the_fleet_in_valid_exposition_format_and_leaks_nothing()
    {
        var (operation, helper) = await _driver.EnrollToActiveAsync();
        Assert.Equal("Active", operation.State);
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

        var text = await _host.Get<MetricsService>().RenderAsync(CancellationToken.None);

        Assert.Contains("fleet_api_reachable 1\n", text);
        Assert.Contains("fleet_nodes{status=\"Active\",health=\"Online\"} 1\n", text);
        Assert.Contains("fleet_hosts{lifecycle=\"Active\"} 1\n", text);
        Assert.Contains("fleet_operations{kind=\"enroll\",state=\"Active\"} 1\n", text);
        Assert.Contains("fleet_node_leased_weight{node=\"helper-eu-01\"} 0\n", text);
        Assert.Contains("fleet_audit_chain_intact 1\n", text);
        Assert.Contains("fleet_operations_chain_intact 1\n", text);
        Assert.Contains("fleet_rollout_token_present 1\n", text);
        Assert.Contains("fleet_host_alerts 0\n", text);

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')))
        {
            Assert.Matches(SampleLine, line);
        }

        // Counts and ages only: never an address, a token, a key or a digest.
        var node = _host.World.Api.Nodes.Single();
        Assert.DoesNotContain(EnrollmentDriver.DefaultAddress, text);
        Assert.DoesNotContain(node.Tokens.Single().Value, text);
        Assert.DoesNotContain("PRIVATE KEY", text);
        Assert.DoesNotContain(helper.OwnerKeyText.Split('\n')[1], text);
        Assert.DoesNotContain(FleetWorld.AgentDigest, text);
        Assert.DoesNotContain("ghs_", text);
    }

    [Fact]
    public async Task The_scrape_body_reports_alerts_and_an_api_that_cannot_be_reached()
    {
        var (_, helper) = await _driver.EnrollToActiveAsync();
        var monitor = _host.Get<NodeMonitor>();
        await monitor.PollOnceAsync(CancellationToken.None);
        helper.KeyBlob = FakeKeys.HostKeyBlob("someone-else");
        _host.World.Time.Advance(TimeSpan.FromMinutes(6));
        await monitor.PollOnceAsync(CancellationToken.None);
        _host.World.Api.Reachable = false;
        await monitor.PollOnceAsync(CancellationToken.None);

        var text = await _host.Get<MetricsService>().RenderAsync(CancellationToken.None);

        Assert.Contains("fleet_host_alerts 1\n", text);
        Assert.Contains("fleet_api_reachable 0\n", text);
        Assert.Contains("fleet_hosts{lifecycle=\"Disabled\"} 1\n", text);
    }

    [Fact]
    public async Task Health_is_ok_after_a_clean_start_and_counts_the_operations_still_open()
    {
        var reporter = _host.Get<HealthReporter>();
        var clean = await reporter.GetAsync(CancellationToken.None);
        Assert.True(clean.Ok);
        Assert.True(clean.Database);
        Assert.True(clean.AuditChainIntact);
        Assert.True(clean.OperationsChainIntact);
        Assert.Null(clean.IntegrityProblem);
        Assert.Equal(0, clean.OpenOperations);
        Assert.Equal(8, clean.MasterKeyId!.Length);

        await _driver.AddAsync();
        Assert.Equal(1, (await reporter.GetAsync(CancellationToken.None)).OpenOperations);

        await _driver.EnrollToActiveAsync("helper-eu-02", "203.0.113.11");
        Assert.Equal(1, (await reporter.GetAsync(CancellationToken.None)).OpenOperations);
    }

    [Fact]
    public async Task Health_says_whether_the_api_was_reachable_at_the_last_poll()
    {
        var reporter = _host.Get<HealthReporter>();
        Assert.False((await reporter.GetAsync(CancellationToken.None)).ApiReachable);

        await _driver.EnrollToActiveAsync();
        await _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        var healthy = await reporter.GetAsync(CancellationToken.None);

        Assert.True(healthy.ApiReachable);
        Assert.Equal(1, healthy.Nodes);
        Assert.NotNull(healthy.LastPollAt);
        Assert.Equal(HealthReporter.Version, healthy.Version);
    }

    [Fact]
    public void Reading_the_primary_pressure_never_throws_whatever_the_platform()
    {
        var reading = new ProcPressureSource().Read();

        Assert.True(reading is null || reading.CpuSomeAvg10 is not null);
    }
}
