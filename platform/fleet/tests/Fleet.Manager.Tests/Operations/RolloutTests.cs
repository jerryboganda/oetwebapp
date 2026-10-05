using Fleet.Core.Domain;
using Fleet.Core.Validation;
using Fleet.Manager.Operations;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Operations;

/// <summary>
/// The rolling image update (OET-RWP/1 section 8.7): approved digests only, one helper at a time (drain, wait, pull, run, verify,
/// enable, canary), a failure halts everything behind it, and the previous approved digest is the rollback.
/// </summary>
public sealed class RolloutTests : IAsyncLifetime
{
    private const string FirstRef = "helper-eu-01";
    private const string SecondRef = "helper-eu-02";
    private const string FirstAddress = "203.0.113.10";
    private const string SecondAddress = "203.0.113.11";

    private FleetTestHost _host = null!;
    private EnrollmentDriver _driver = null!;

    public async Task InitializeAsync()
    {
        _host = await FleetTestHost.CreateAsync();
        _driver = new EnrollmentDriver(_host);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private FleetWorld World => _host.World;

    private HostService Hosts => _host.Get<HostService>();

    private FakeApiNode Node(string nodeRef) => World.Api.NodeByRef(nodeRef) ?? throw new InvalidOperationException("no node " + nodeRef);

    private async Task<(FakeHost First, FakeHost Second)> TwoActiveHelpersAsync()
    {
        var (_, first) = await _driver.EnrollToActiveAsync(FirstRef, FirstAddress);
        var (_, second) = await _driver.EnrollToActiveAsync(SecondRef, SecondAddress);
        return (first, second);
    }

    /// <summary>Publishes and approves the next agent image. The clock moves first so the approval and any new canary are strictly later than enrollment.</summary>
    private async Task PublishNextReleaseAsync()
    {
        World.Time.Advance(TimeSpan.FromMinutes(1));
        World.PublishAgentImage(FleetWorld.NextDigest, FleetWorld.NextImageId);
        await _driver.ApproveReleaseAsync('2', FleetWorld.NextDigest, FleetWorld.NextImageId);
        await _driver.SyncAsync();
    }

    private Task<OperationView> RunAsync(OperationView operation) => _driver.RunAsync(operation.Id);

    private static CtlResult Boom() => new(false, null, "boom", "{\"ok\":false,\"error\":\"boom\"}", 1);

    [Fact]
    public async Task A_rollout_updates_one_helper_at_a_time_in_node_order()
    {
        var (first, second) = await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();

        var started = await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None);
        Assert.Equal("Queued", started.State);
        Assert.Equal(14, started.Steps.Count);
        Assert.Equal("roll-drain:" + FirstRef, started.Steps[0].Name);
        Assert.All(started.Steps.Take(7), step => Assert.EndsWith(":" + FirstRef, step.Name));
        Assert.All(started.Steps.Skip(7), step => Assert.EndsWith(":" + SecondRef, step.Name));

        var callsBefore = World.Api.Calls.Count;
        var finished = await RunAsync(started);

        Assert.Equal("Succeeded", finished.State);
        Assert.All(finished.Steps, step => Assert.Contains(step.State, new[] { "done", "skipped" }));

        // Strictly sequential: the second helper is never drained before the first one is enabled again.
        var transitions = World.Api.Calls.Skip(callsBefore).Where(call => call is "drain" or "enable").ToArray();
        Assert.Equal(new[] { "drain", "enable", "drain", "enable" }, transitions);

        foreach (var helper in new[] { first, second })
        {
            Assert.Equal(FleetWorld.NextDigest, helper.RunningDigest);
            Assert.Contains(FleetWorld.NextDigest, helper.Images);
            Assert.False(helper.LoggedIn, "the registry credential must not survive a rollout");
            Assert.Contains("OET_AGENT_IMAGE_DIGEST=" + FleetWorld.NextDigest, helper.EnvText);
        }

        foreach (var nodeRef in new[] { FirstRef, SecondRef })
        {
            var node = Node(nodeRef);
            Assert.Equal("Active", node.Status);
            Assert.Equal(FleetWorld.NextDigest, node.AgentDigest);
            var host = await _driver.HostAsync(nodeRef);
            Assert.Equal("Active", host.Lifecycle);
            Assert.Equal(FleetWorld.NextDigest, host.AgentDigest);
        }

        // The same node token keeps working: a rollout replaces the container, not the credential.
        Assert.All(World.Api.Nodes, node => Assert.Single(node.Tokens));
    }

    [Fact]
    public async Task Every_node_accepts_the_new_digest_before_any_agent_runs_it()
    {
        await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();
        var putsBefore = World.Api.PutPolicyCalls;

        await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None);

        Assert.Equal(putsBefore + 2, World.Api.PutPolicyCalls);
        foreach (var nodeRef in new[] { FirstRef, SecondRef })
        {
            var window = Node(nodeRef).Policy!.AgentImage.ApprovedDigests;
            Assert.Equal(FleetWorld.NextDigest, window[0]);
            Assert.Contains(FleetWorld.AgentDigest, window);
        }
    }

    [Fact]
    public async Task A_failure_halts_the_rollout_leaves_later_helpers_untouched_and_the_previous_digest_is_the_rollback()
    {
        var (first, second) = await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();
        World.Provisioner.FailCtlOnce["run"] = Boom();
        var secondPullsBefore = second.Pulls;

        var started = await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None);
        var failed = await RunAsync(started);

        Assert.Equal("Failed", failed.State);
        Assert.Equal(FailureReasons.AgentStartFailed, failed.FailureReason);
        Assert.Equal(
            new[] { "done", "done", "done", "failed" },
            failed.Steps.Take(4).Select(s => s.State).ToArray());
        Assert.All(failed.Steps.Skip(4), step => Assert.Equal("pending", step.State));

        // The first helper still runs the old agent and is drained; the second one was never touched.
        Assert.Equal(FleetWorld.AgentDigest, first.RunningDigest);
        Assert.Equal("Draining", Node(FirstRef).Status);
        Assert.Equal(FleetWorld.AgentDigest, second.RunningDigest);
        Assert.Equal(secondPullsBefore, second.Pulls);
        Assert.DoesNotContain(FleetWorld.NextDigest, second.Images);
        Assert.Equal("Active", Node(SecondRef).Status);

        // Only one rollout may be open at a time: asking again returns the failed one.
        var again = await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None);
        Assert.Equal(failed.Id, again.Id);

        // Roll back: abandon the failed rollout and roll to the previous approved digest. The drained helper takes part.
        var cancelled = await _driver.Enrollment.CancelAsync(failed.Id, "owner", CancellationToken.None);
        Assert.Equal("Cancelled", cancelled.State);
        var rollback = await Hosts.StartRolloutAsync(FleetWorld.AgentDigest, "owner", CancellationToken.None);
        Assert.Equal(14, rollback.Steps.Count);
        var rolledBack = await RunAsync(rollback);

        Assert.Equal("Succeeded", rolledBack.State);
        foreach (var nodeRef in new[] { FirstRef, SecondRef })
        {
            Assert.Equal("Active", Node(nodeRef).Status);
            Assert.Equal(FleetWorld.AgentDigest, Node(nodeRef).AgentDigest);
            Assert.Equal("Active", (await _driver.HostAsync(nodeRef)).Lifecycle);
        }

        Assert.Equal(FleetWorld.AgentDigest, first.RunningDigest);
        Assert.Equal(FleetWorld.AgentDigest, second.RunningDigest);
        Assert.False(first.LoggedIn);
        Assert.False(second.LoggedIn);
    }

    [Fact]
    public async Task A_wrong_canary_answer_drains_the_node_again_and_stops_the_rollout()
    {
        var (first, second) = await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();
        World.Api.CanaryResultOk = false;

        var failed = await RunAsync(await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None));

        Assert.Equal("Failed", failed.State);
        Assert.Equal(FailureReasons.CanaryMismatch, failed.FailureReason);
        Assert.Equal("failed", failed.Steps[6].State);
        Assert.Equal(FleetWorld.NextDigest, first.RunningDigest);
        Assert.Equal("Draining", Node(FirstRef).Status);

        Assert.Equal(FleetWorld.AgentDigest, second.RunningDigest);
        Assert.Equal("Active", Node(SecondRef).Status);
    }

    [Fact]
    public async Task A_rollout_whose_image_cannot_be_pulled_stops_before_the_agent_is_replaced()
    {
        var (first, second) = await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();
        World.Provisioner.Registry.Remove(FleetWorld.NextDigest);

        var failed = await RunAsync(await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None));

        Assert.Equal("Failed", failed.State);
        Assert.Equal(FailureReasons.ImagePullFailed, failed.FailureReason);
        Assert.Equal(FleetWorld.AgentDigest, first.RunningDigest);
        Assert.Equal(FleetWorld.AgentDigest, second.RunningDigest);
        Assert.False(first.LoggedIn, "even a failed pull logs out");
    }

    [Fact]
    public async Task A_disabled_helper_stays_out_of_the_rollout()
    {
        var (first, second) = await TwoActiveHelpersAsync();
        var secondHost = await _driver.HostAsync(SecondRef);
        await _driver.RunAsync((await Hosts.StartDisableAsync(secondHost.Id, "owner", CancellationToken.None)).Id);
        await PublishNextReleaseAsync();

        var started = await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None);
        Assert.Equal(7, started.Steps.Count);
        var finished = await RunAsync(started);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(FleetWorld.NextDigest, first.RunningDigest);
        Assert.Equal(FleetWorld.AgentDigest, second.RunningDigest);
        Assert.Equal("Disabled", Node(SecondRef).Status);
    }

    [Fact]
    public async Task A_failed_rollout_retries_from_the_step_that_failed_and_not_from_the_beginning()
    {
        await TwoActiveHelpersAsync();
        await PublishNextReleaseAsync();
        World.Provisioner.FailCtlOnce["run"] = Boom();
        var failed = await RunAsync(await Hosts.StartRolloutAsync(FleetWorld.NextDigest, "owner", CancellationToken.None));
        Assert.Equal("Failed", failed.State);

        // The first helper already pulled the new image; only the second one has still to pull it.
        var pullsBefore = World.Provisioner.Hosts.Sum(h => h.Pulls);
        await _driver.Enrollment.RetryAsync(failed.Id, "owner", CancellationToken.None);
        var finished = await RunAsync(failed);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(pullsBefore + 1, World.Provisioner.Hosts.Sum(h => h.Pulls));
        foreach (var helper in World.Provisioner.Hosts)
        {
            Assert.Equal(FleetWorld.NextDigest, helper.RunningDigest);
        }
    }

    [Fact]
    public async Task Only_approved_valid_digests_can_be_rolled_out_and_there_must_be_someone_to_update()
    {
        await _driver.ApproveReleaseAsync();

        var malformed = await Assert.ThrowsAsync<FleetValidationException>(() => Hosts.StartRolloutAsync("latest", "owner", CancellationToken.None));
        Assert.Contains(malformed.Issues, issue => issue.Code == "digest_invalid");

        var unapproved = await Assert.ThrowsAsync<FleetValidationException>(
            () => Hosts.StartRolloutAsync("sha256:" + new string('9', 64), "owner", CancellationToken.None));
        Assert.Contains(unapproved.Issues, issue => issue.Code == "image_digest_unapproved");

        var nobody = await Assert.ThrowsAsync<FleetOperationException>(() => Hosts.StartRolloutAsync(FleetWorld.AgentDigest, "owner", CancellationToken.None));
        Assert.Equal("no_hosts", nobody.Code);
    }
}
