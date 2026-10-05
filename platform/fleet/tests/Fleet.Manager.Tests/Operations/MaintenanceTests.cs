using System.Text;
using Fleet.Core.Domain;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Tests.Operations;

/// <summary>
/// Day-two operations on enrolled helpers: drain, disable, enable, repair, removal and token rotation. Each action is one durable
/// operation over the real services, database, API contract and ctl verb patterns (OET-RWP/1 sections 8.3, 8.5).
/// </summary>
public sealed class MaintenanceTests : IAsyncLifetime
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

    private HostService Hosts => _host.Get<HostService>();

    private async Task<(HostEntity Host, FakeHost Helper)> EnrollAsync(
        string nodeRef = EnrollmentDriver.DefaultNodeRef,
        string address = EnrollmentDriver.DefaultAddress)
    {
        var (operation, helper) = await _driver.EnrollToActiveAsync(nodeRef, address);
        Assert.Equal("Active", operation.State);
        return (await _driver.HostAsync(nodeRef), helper);
    }

    private Task<OperationView> RunAsync(OperationView operation) => _driver.RunAsync(operation.Id);

    private FakeApiNode Node(string nodeRef = EnrollmentDriver.DefaultNodeRef) =>
        World.Api.NodeByRef(nodeRef) ?? throw new InvalidOperationException("the API has no node " + nodeRef);

    private async Task<int> CredentialCountAsync(string hostId, string? purpose = null)
    {
        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return purpose is null
            ? await db.Credentials.CountAsync(c => c.HostId == hostId)
            : await db.Credentials.CountAsync(c => c.HostId == hostId && c.Purpose == purpose);
    }

    private async Task<bool> AuditedAsync(string action, string? target = null)
    {
        var rows = await _host.Get<IAuditService>().ListAsync(500);
        return rows.Any(r => r.Action == action && (target is null || r.Target == target));
    }

    private static string TokenIn(FakeHost helper) =>
        helper.EnvText!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("OET_NODE_TOKEN=", StringComparison.Ordinal))["OET_NODE_TOKEN=".Length..];

    // ---- drain -----------------------------------------------------------------------------

    [Fact]
    public async Task Drain_waits_for_the_leases_to_finish_and_then_succeeds()
    {
        var (host, _) = await EnrollAsync();
        Node().LeaseCount = 3;
        World.Api.LeasesFinishPerPoll = 1;

        var started = await Hosts.StartDrainAsync(host.Id, "owner", CancellationToken.None);
        Assert.Equal("Queued", started.State);
        Assert.Equal(new[] { "api-drain", "wait-leases" }, started.Steps.Select(s => s.Name).ToArray());

        var delaysBefore = World.Delay.Delays;
        var finished = await RunAsync(started);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal("no leases remain", finished.Steps[1].Summary);
        Assert.Equal(2, World.Delay.Delays - delaysBefore);
        Assert.Equal("Draining", Node().Status);
        Assert.Equal("Draining", (await _driver.HostAsync()).Lifecycle);
        Assert.True(await AuditedAsync("host.drain_requested", "helper-eu-01"));
    }

    [Fact]
    public async Task Drain_continues_after_the_wait_window_when_leases_never_finish()
    {
        var (host, _) = await EnrollAsync();
        Node().LeaseCount = 2;

        var finished = await RunAsync(await Hosts.StartDrainAsync(host.Id, "owner", CancellationToken.None));

        Assert.Equal("Succeeded", finished.State);
        Assert.Contains("2 lease(s) still held", finished.Steps[1].Summary);
        Assert.Equal("Draining", Node().Status);
    }

    [Fact]
    public async Task A_second_request_while_an_operation_is_open_returns_that_operation()
    {
        var (host, _) = await EnrollAsync();

        var first = await Hosts.StartDrainAsync(host.Id, "owner", CancellationToken.None);
        var second = await Hosts.StartDrainAsync(host.Id, "owner", CancellationToken.None);
        Assert.Equal(first.Id, second.Id);

        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Operations.CountAsync(o => o.Kind == "drain"));
        }

        await RunAsync(first);

        // Once it has finished a new drain (of a still-draining host) is a new operation.
        var third = await Hosts.StartDrainAsync(host.Id, "owner", CancellationToken.None);
        Assert.NotEqual(first.Id, third.Id);
    }

    [Fact]
    public async Task Actions_are_refused_in_the_wrong_state_and_for_unknown_hosts()
    {
        var (enrolling, _) = await _driver.AddAsync("helper-eu-05", "203.0.113.15");
        var enrollingHostId = enrolling.HostId!;

        foreach (var action in new Func<Task>[]
                 {
                     () => Hosts.StartDrainAsync(enrollingHostId, "owner", CancellationToken.None),
                     () => Hosts.StartDisableAsync(enrollingHostId, "owner", CancellationToken.None),
                     () => Hosts.StartEnableAsync(enrollingHostId, "owner", CancellationToken.None),
                     () => Hosts.StartRotateTokenAsync(enrollingHostId, "owner", CancellationToken.None),
                     () => Hosts.StartRepairAsync(enrollingHostId, "owner", CancellationToken.None),
                 })
        {
            var refusal = await Assert.ThrowsAsync<FleetOperationException>(action);
            Assert.Equal("invalid_state", refusal.Code);
        }

        await Assert.ThrowsAsync<FleetNotFoundException>(() => Hosts.StartDrainAsync("no-such-host", "owner", CancellationToken.None));
        await Assert.ThrowsAsync<FleetNotFoundException>(() => Hosts.StartRemoveAsync("no-such-host", false, "owner", CancellationToken.None));

        var (active, _) = await EnrollAsync();
        var enableActive = await Assert.ThrowsAsync<FleetOperationException>(() => Hosts.StartEnableAsync(active.Id, "owner", CancellationToken.None));
        Assert.Equal("invalid_state", enableActive.Code);
    }

    // ---- disable and enable ----------------------------------------------------------------

    [Fact]
    public async Task Disable_and_enable_move_the_node_and_the_host_together()
    {
        var (host, _) = await EnrollAsync();

        var disabled = await RunAsync(await Hosts.StartDisableAsync(host.Id, "owner", CancellationToken.None));
        Assert.Equal("Succeeded", disabled.State);
        Assert.Equal("Disabled", Node().Status);
        Assert.Equal("Disabled", (await _driver.HostAsync()).Lifecycle);

        var enableOperation = await Hosts.StartEnableAsync(host.Id, "owner", CancellationToken.None);
        Assert.Equal(new[] { "ensure-canary", "api-enable" }, enableOperation.Steps.Select(s => s.Name).ToArray());
        var enabled = await RunAsync(enableOperation);

        Assert.Equal("Succeeded", enabled.State);
        Assert.Equal("skipped", enabled.Steps[0].State);
        Assert.Equal("Active", Node().Status);
        Assert.Equal("Active", (await _driver.HostAsync()).Lifecycle);
    }

    [Fact]
    public async Task Disabling_a_disabled_host_is_harmless()
    {
        var (host, _) = await EnrollAsync();
        await RunAsync(await Hosts.StartDisableAsync(host.Id, "owner", CancellationToken.None));
        var disableCallsBefore = World.Api.Calls.Count(c => c == "disable");

        var again = await RunAsync(await Hosts.StartDisableAsync(host.Id, "owner", CancellationToken.None));

        Assert.Equal("Succeeded", again.State);
        Assert.Equal(disableCallsBefore, World.Api.Calls.Count(c => c == "disable"));
    }

    [Fact]
    public async Task Enable_is_refused_while_a_host_key_alert_is_open()
    {
        var (host, _) = await EnrollAsync();
        await RunAsync(await Hosts.StartDisableAsync(host.Id, "owner", CancellationToken.None));
        await _host.Get<HostStore>().UpdateAsync(host.Id, h => h.Alert = FailureReasons.HostKeyChanged, CancellationToken.None);

        var refusal = await Assert.ThrowsAsync<FleetOperationException>(() => Hosts.StartEnableAsync(host.Id, "owner", CancellationToken.None));

        Assert.Equal("invalid_state", refusal.Code);
        Assert.Contains("host-key alert", refusal.Message);
        Assert.Equal("Disabled", Node().Status);
    }

    // ---- repair ----------------------------------------------------------------------------

    [Fact]
    public async Task Repair_reruns_the_root_steps_with_a_fresh_owner_key_and_changes_only_what_drifted()
    {
        var (host, helper) = await EnrollAsync();
        helper.SshHardened = false;

        var started = await Hosts.StartRepairAsync(host.Id, "owner", CancellationToken.None);
        Assert.Equal("AwaitingOwner", started.State);
        Assert.True(started.AwaitingOwnerCredential);
        Assert.Equal(8, started.Steps.Count);

        // Nothing runs until the owner supplies a temporary key again.
        var blocked = await RunAsync(started);
        Assert.Equal("AwaitingOwner", blocked.State);
        Assert.Equal(1, helper.ApplyCounts[EnrollStep.HardenSsh]);

        await _driver.SubmitOwnerKeyAsync(started.Id, helper);
        var repaired = await RunAsync(started);

        Assert.Equal("Succeeded", repaired.State);
        Assert.Equal(2, helper.ApplyCounts[EnrollStep.HardenSsh]);
        Assert.Equal(1, helper.ApplyCounts[EnrollStep.FleetUser]);
        Assert.Equal(1, helper.ApplyCounts[EnrollStep.Docker]);
        Assert.Equal(1, helper.ApplyCounts[EnrollStep.Firewall]);
        Assert.Equal(1, helper.KeyInstalls);
        Assert.True(helper.SshHardened);
        Assert.Equal(0, await CredentialCountAsync(host.Id, CredentialPurposes.OwnerBootstrap));
    }

    // ---- removal ---------------------------------------------------------------------------

    [Fact]
    public async Task Removal_uninstalls_only_fleet_owned_components_revokes_the_node_and_erases_the_vault()
    {
        var (host, helper) = await EnrollAsync();
        helper.ForeignContainers.Add("customer-database");
        var foreignImage = Assert.Single(helper.ForeignImages);
        var node = Node();
        var tokens = node.Tokens.Select(t => t.Value).ToList();
        Assert.True(await CredentialCountAsync(host.Id) > 0);

        var started = await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None);
        Assert.Equal(
            new[] { "api-drain", "wait-leases-strict", "uninstall", "api-revoke", "destroy-credentials", "finalize-remove" },
            started.Steps.Select(s => s.Name).ToArray());
        Assert.Equal("Removing", (await _driver.HostAsync()).Lifecycle);

        var finished = await RunAsync(started);

        Assert.Equal("Succeeded", finished.State);
        Assert.All(finished.Steps, step => Assert.Contains(step.State, new[] { "done", "skipped" }));

        // What was ours is gone.
        Assert.True(helper.Uninstalled);
        Assert.Null(helper.RunningDigest);
        Assert.Empty(helper.Images);
        Assert.Null(helper.EnvText);
        Assert.False(helper.UnitWritten);
        Assert.True(helper.ManagerAccountLocked);

        // What was not ours is untouched: Docker, the firewall, the host baseline, other containers and images.
        Assert.True(helper.DockerInstalled);
        Assert.True(helper.FirewallApplied);
        Assert.True(helper.BaselineApplied);
        Assert.Equal("customer-database", Assert.Single(helper.ForeignContainers));
        Assert.Equal(foreignImage, Assert.Single(helper.ForeignImages));

        // The node and every credential it ever held are dead.
        Assert.Equal("Revoked", node.Status);
        Assert.All(tokens, token => Assert.False(World.Api.TokenAccepted(node.Id, token)));
        Assert.All(tokens, token => Assert.False(World.Api.AgentHeartbeat(node.Id, token, FleetWorld.AgentDigest)));

        // Nothing about the helper remains in the vault; the host row stays as history and keeps its reference.
        Assert.Equal(0, await CredentialCountAsync(host.Id));
        var after = await _driver.HostAsync();
        Assert.Equal("Removed", after.Lifecycle);
        Assert.Null(after.Alert);
        Assert.True(await AuditedAsync("host.removed", "helper-eu-01"));

        // The address is free again under a new reference.
        var (again, _) = await _driver.AddAsync("helper-eu-10", EnrollmentDriver.DefaultAddress);
        Assert.Equal("Created", again.State);
        Assert.NotEqual(host.Id, again.HostId);
    }

    [Fact]
    public async Task Removing_a_host_whose_enrollment_never_finished_abandons_it_without_touching_the_helper()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("HostKeyPending", operation.State);
        var hostId = operation.HostId!;

        var removal = await Hosts.StartRemoveAsync(hostId, force: false, "owner", CancellationToken.None);
        var finished = await RunAsync(removal);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal("Cancelled", (await Hosts.GetOperationAsync(operation.Id, CancellationToken.None)).State);
        Assert.Equal("Removed", (await _driver.HostAsync()).Lifecycle);
        Assert.Empty(helper.CtlLog);
        Assert.Empty(helper.ApplyCounts);
        Assert.False(helper.Uninstalled);
    }

    [Fact]
    public async Task Removal_is_strict_about_leases_and_resumes_when_the_work_has_finished()
    {
        var (host, helper) = await EnrollAsync();
        Node().LeaseCount = 2;

        var failed = await RunAsync(await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None));

        Assert.Equal("Failed", failed.State);
        Assert.Equal(FailureReasons.DrainTimeout, failed.FailureReason);
        Assert.Contains("2 lease(s)", failed.FailureDetail);
        Assert.Equal(new[] { "done", "failed", "pending", "pending", "pending", "pending" }, failed.Steps.Select(s => s.State).ToArray());

        // Nothing was pulled out from under running work.
        Assert.False(helper.Uninstalled);
        Assert.NotNull(helper.RunningDigest);
        Assert.NotEqual("Revoked", Node().Status);
        Assert.True(await CredentialCountAsync(host.Id) > 0);

        Node().LeaseCount = 0;
        await _driver.Enrollment.RetryAsync(failed.Id, "owner", CancellationToken.None);
        var finished = await RunAsync(failed);

        Assert.Equal("Succeeded", finished.State);
        Assert.True(helper.Uninstalled);
        Assert.Equal("Revoked", Node().Status);
        Assert.Equal("Removed", (await _driver.HostAsync()).Lifecycle);
    }

    [Fact]
    public async Task An_unreachable_helper_blocks_removal_until_the_owner_forces_it()
    {
        var (host, helper) = await EnrollAsync();
        helper.Reachable = false;

        var failed = await RunAsync(await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None));

        Assert.Equal("Failed", failed.State);
        Assert.Equal(FailureReasons.SshUnreachable, failed.FailureReason);
        Assert.NotEqual("Revoked", Node().Status);
        Assert.True(await CredentialCountAsync(host.Id) > 0, "a failed removal must not erase what is still needed to reach the helper");

        // A failed operation can be abandoned (it would otherwise block a new removal forever) and the owner can then force it.
        var cancelled = await _driver.Enrollment.CancelAsync(failed.Id, "owner", CancellationToken.None);
        Assert.Equal("Cancelled", cancelled.State);

        var forced = await RunAsync(await Hosts.StartRemoveAsync(host.Id, force: true, "owner", CancellationToken.None));

        Assert.Equal("Succeeded", forced.State);
        Assert.Equal("skipped", forced.Steps.Single(s => s.Name == "uninstall").State);
        Assert.False(helper.Uninstalled, "an unreachable helper cannot be cleaned; the removal says so instead of pretending");
        Assert.Equal("Revoked", Node().Status);
        Assert.Equal(0, await CredentialCountAsync(host.Id));
        Assert.Equal("Removed", (await _driver.HostAsync()).Lifecycle);
    }

    [Fact]
    public async Task Cancelling_a_removal_before_it_started_leaves_the_host_out_of_service_never_half_removed()
    {
        var (host, helper) = await EnrollAsync();
        var queued = await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None);
        Assert.Equal("Removing", (await _driver.HostAsync()).Lifecycle);

        var cancelled = await _driver.Enrollment.CancelAsync(queued.Id, "owner", CancellationToken.None);

        Assert.Equal("Cancelled", cancelled.State);
        Assert.Equal("Disabled", (await _driver.HostAsync()).Lifecycle);
        Assert.False(helper.Uninstalled);
        Assert.Equal("Active", Node().Status);
    }

    [Fact]
    public async Task Cancelling_a_removal_that_failed_on_leases_keeps_the_host_draining_and_removable()
    {
        var (host, _) = await EnrollAsync();
        Node().LeaseCount = 1;
        var failed = await RunAsync(await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None));
        Assert.Equal("Failed", failed.State);

        await _driver.Enrollment.CancelAsync(failed.Id, "owner", CancellationToken.None);

        var lifecycle = (await _driver.HostAsync()).Lifecycle;
        Assert.Equal("Draining", lifecycle);
        Node().LeaseCount = 0;
        var again = await RunAsync(await Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None));
        Assert.Equal("Succeeded", again.State);
    }

    // ---- token rotation --------------------------------------------------------------------

    [Fact]
    public async Task Rotating_the_token_keeps_the_old_one_valid_for_the_grace_period_only()
    {
        var (host, helper) = await EnrollAsync();
        var node = Node();
        var oldToken = Assert.Single(node.Tokens).Value;
        Assert.Equal(oldToken, TokenIn(helper));

        var started = await Hosts.StartRotateTokenAsync(host.Id, "owner", CancellationToken.None);
        Assert.Equal(new[] { "rotate-and-render", "restart-agent", "verify-heartbeat", "finalize-rotate" }, started.Steps.Select(s => s.Name).ToArray());
        var finished = await RunAsync(started);

        Assert.Equal("Succeeded", finished.State);
        var newToken = TokenIn(helper);
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(1, helper.Restarts);
        Assert.Contains("OET_AGENT_IMAGE_DIGEST=" + FleetWorld.AgentDigest, helper.EnvText);

        // Both work during the grace period, so a restart in the middle of a rotation never strands the agent.
        Assert.True(World.Api.TokenAccepted(node.Id, oldToken));
        Assert.True(World.Api.TokenAccepted(node.Id, newToken));

        World.Time.Advance(TimeSpan.FromSeconds(3601));
        Assert.False(World.Api.TokenAccepted(node.Id, oldToken), "the old token must stop working after the grace period");
        Assert.True(World.Api.TokenAccepted(node.Id, newToken));

        // The new token never reached argv, a log line, the audit log or the database; only its fingerprint is recorded.
        Assert.All(helper.CtlLog, call => Assert.DoesNotContain(newToken, string.Join(' ', call.Args)));
        Assert.DoesNotContain(newToken, _host.Logs.All);
        Assert.False(ByteSearch.Contains(await _host.ReadDatabaseBytesAsync(), Encoding.UTF8.GetBytes(newToken)));
        var info = await _host.Get<CredentialStore>().GetInfoAsync(host.Id, CredentialPurposes.NodeTokenRender, CancellationToken.None);
        Assert.NotNull(info);
        Assert.False(info!.HasCiphertext);
        Assert.True(await AuditedAsync("host.token_rotated", "helper-eu-01"));
    }

    [Fact]
    public async Task A_rotation_that_would_exceed_three_live_tokens_fails_cleanly_and_succeeds_after_the_grace_period()
    {
        var (host, _) = await EnrollAsync();

        Assert.Equal("Succeeded", (await RunAsync(await Hosts.StartRotateTokenAsync(host.Id, "owner", CancellationToken.None))).State);
        Assert.Equal("Succeeded", (await RunAsync(await Hosts.StartRotateTokenAsync(host.Id, "owner", CancellationToken.None))).State);

        var refused = await RunAsync(await Hosts.StartRotateTokenAsync(host.Id, "owner", CancellationToken.None));
        Assert.Equal("Failed", refused.State);
        Assert.Equal(FailureReasons.TokenRenderFailed, refused.FailureReason);
        Assert.Contains("too_many_credentials", refused.FailureDetail);
        Assert.Equal(3, Node().Tokens.Count(t => !t.Revoked && t.ExpiresAt > World.Time.GetUtcNow()));

        // Once the older tokens have aged out the same operation continues from the step that failed.
        World.Time.Advance(TimeSpan.FromSeconds(3601));
        await _driver.Enrollment.RetryAsync(refused.Id, "owner", CancellationToken.None);
        var retried = await RunAsync(refused);

        Assert.Equal("Succeeded", retried.State);
        Assert.True(Node().Tokens.Count(t => !t.Revoked && t.ExpiresAt > World.Time.GetUtcNow()) <= 3);
    }

    // ---- read models -----------------------------------------------------------------------

    [Fact]
    public async Task The_host_detail_joins_the_manager_record_the_api_node_and_the_enrollment()
    {
        var (host, _) = await EnrollAsync();

        var detail = await Hosts.GetHostAsync(host.Id, includeNode: true, CancellationToken.None);

        Assert.Equal("Active", detail.Host.Lifecycle);
        Assert.NotNull(detail.Node);
        Assert.Equal("Active", detail.Node!.Status);
        Assert.Equal("Active", detail.EnrollOperation!.State);

        World.Api.Reachable = false;
        var offline = await Hosts.GetHostAsync(host.Id, includeNode: true, CancellationToken.None);
        Assert.Null(offline.Node);
        Assert.Equal("Active", offline.Host.Lifecycle);
    }
}
