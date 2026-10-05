using Fleet.Core.Domain;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Operations;

/// <summary>
/// RW-139: killing the manager at ANY point and starting it again on the same files resumes the enrollment without a duplicate
/// user, container, node or token. The "kill" is a cancellation raised inside the Nth provisioner call, either before the
/// helper changed (the step must then run once) or after (the step's predicate must then skip it), followed by a brand-new
/// service provider over the same SQLite file and vault.
/// </summary>
public sealed class ResumeAfterRestartTests
{
    public static IEnumerable<object[]> CrashPoints()
    {
        for (var call = 1; call <= 30; call++)
        {
            yield return new object[] { call, true };
            yield return new object[] { call, false };
        }
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task Killing_the_manager_at_any_provisioner_call_and_resuming_creates_no_duplicates(int crashAtCall, bool afterEffect)
    {
        var world = new FleetWorld();
        var crash = new CancellationTokenSource();
        world.Provisioner.CrashSource = crash;
        world.Provisioner.CrashAtCall = crashAtCall;
        world.Provisioner.CrashAfterEffect = afterEffect;
        await using var host = await FleetTestHost.CreateAsync(world: world);
        var driver = new EnrollmentDriver(host);

        var (operation, helper) = await driver.AddAsync();
        await driver.ApproveReleaseAsync();
        await driver.SyncAsync();

        var crashes = 0;
        for (var round = 0; round < 20 && operation.State != "Active"; round++)
        {
            try
            {
                operation = await driver.RunAsync(operation.Id, crash.Token);
            }
            catch (OperationCanceledException)
            {
                crashes++;
                await host.RestartAsync();
                crash = new CancellationTokenSource();
                world.Provisioner.CrashSource = crash;
                // The per-rollout registry token lived in memory only: the CI sync supplies a new one, as it would after a real restart.
                await driver.SyncAsync();
                operation = await driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None);
            }

            switch (operation.State)
            {
                case "HostKeyPending":
                    operation = await driver.ConfirmAsync(operation.Id, helper);
                    break;
                case "HostKeyConfirmed":
                    operation = await driver.SubmitOwnerKeyAsync(operation.Id, helper);
                    break;
                case "ImageAwaitingSync":
                    await driver.SyncAsync();
                    break;
                case "Failed":
                    Assert.Fail("the enrollment failed after a crash at provisioner call " + crashAtCall + " (after effect: " + afterEffect + "): " + operation.FailureReason + " " + operation.FailureDetail);
                    break;
            }
        }

        Assert.True(crashes <= 1, "the crash plan fires once");
        Assert.Equal("Active", operation.State);

        // No duplicate node, user, key, image or container; every host-side step ran at most once.
        var node = Assert.Single(world.Api.Nodes);
        Assert.Equal("Active", node.Status);
        foreach (var step in new[] { EnrollStep.FleetUser, EnrollStep.InstallKey, EnrollStep.Docker, EnrollStep.Firewall, EnrollStep.HostBaseline, EnrollStep.HardenSsh })
        {
            Assert.True(helper.ApplyCounts.GetValueOrDefault(step) <= 1, step + " ran more than once (crash at call " + crashAtCall + ")");
        }

        Assert.True(helper.KeyInstalls <= 1);
        Assert.Single(helper.Images);
        Assert.Equal(FleetWorld.AgentDigest, helper.RunningDigest);
        Assert.False(helper.LoggedIn, "the registry credential must be gone after the operation");
        Assert.True(node.Tokens.Count(t => !t.Revoked) <= 2, "at most the original token plus one rotated replacement");

        // The manager side: one host, one operation, one manager key, no owner credential, intact chains.
        var factory = host.Get<IDbContextFactory<FleetDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.Hosts.CountAsync());
            Assert.Equal(1, await db.Operations.CountAsync());
            Assert.Equal(1, await db.Credentials.CountAsync(c => c.Purpose == CredentialPurposes.ManagerSsh));
            Assert.Equal(0, await db.Credentials.CountAsync(c => c.Purpose == CredentialPurposes.OwnerBootstrap));
            Assert.Equal(1, await db.Credentials.CountAsync(c => c.Purpose == CredentialPurposes.NodeTokenRender));
        }

        Assert.True((await host.Get<IAuditService>().VerifyAsync()).Intact);
        Assert.True((await host.Get<OperationStore>().VerifyChainAsync(CancellationToken.None)).Intact);
    }

    [Fact]
    public async Task A_restart_while_waiting_for_the_owner_loses_nothing()
    {
        await using var host = await FleetTestHost.CreateAsync();
        var driver = new EnrollmentDriver(host);
        var (operation, helper) = await driver.AddAsync();
        operation = await driver.RunAsync(operation.Id);
        Assert.Equal("HostKeyPending", operation.State);

        await host.RestartAsync();
        operation = await driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None);
        Assert.Equal("HostKeyPending", operation.State);
        Assert.Equal(helper.Fingerprint, Assert.Single(operation.HostKeyCandidates).Fingerprint);

        operation = await driver.ConfirmAsync(operation.Id, helper);
        await host.RestartAsync();
        operation = await driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await host.RestartAsync();

        // The encrypted owner credential survived the restarts and is still within its 60 minutes.
        await driver.ApproveReleaseAsync();
        await driver.SyncAsync();
        operation = await driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
    }

    [Fact]
    public async Task A_step_interrupted_mid_flight_is_requeued_and_resumed_not_restarted_from_scratch()
    {
        var world = new FleetWorld();
        var crash = new CancellationTokenSource();
        world.Provisioner.CrashSource = crash;
        await using var host = await FleetTestHost.CreateAsync(world: world);
        var driver = new EnrollmentDriver(host);
        var (operation, helper) = await driver.AddAsync();
        operation = await driver.RunAsync(operation.Id);
        operation = await driver.ConfirmAsync(operation.Id, helper);
        operation = await driver.SubmitOwnerKeyAsync(operation.Id, helper);

        // Die during the firewall step (S5), right after the helper changed.
        world.Provisioner.CrashAfterApplyOfStep = EnrollStep.Firewall;
        await Assert.ThrowsAsync<OperationCanceledException>(() => driver.RunAsync(operation.Id, crash.Token));

        var interrupted = await driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None);
        Assert.Contains(interrupted.Steps, s => s.State == "running");
        Assert.Equal("Bootstrapping", interrupted.State);

        await host.RestartAsync();
        var recovered = await driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None);
        Assert.DoesNotContain(recovered.Steps, s => s.State == "running");
        Assert.Equal(new[] { "done", "done", "done", "done" }, recovered.Steps.Take(4).Select(s => s.State).ToArray());

        var applyBefore = helper.ApplyCounts.ToDictionary(p => p.Key, p => p.Value);
        await driver.ApproveReleaseAsync();
        await driver.SyncAsync();
        recovered = await driver.RunAsync(operation.Id);
        Assert.Equal("Active", recovered.State);
        foreach (var (step, count) in applyBefore)
        {
            Assert.Equal(count, helper.ApplyCounts[step]);
        }
    }

    [Fact]
    public async Task Dying_right_after_the_node_was_registered_is_recovered_by_rotating_the_token()
    {
        var world = new FleetWorld();
        var crash = new CancellationTokenSource();
        world.Api.CrashAfterRegister = crash;
        await using var host = await FleetTestHost.CreateAsync(world: world);
        var driver = new EnrollmentDriver(host);
        var (operation, helper) = await driver.AddAsync();
        operation = await driver.RunAsync(operation.Id);
        operation = await driver.ConfirmAsync(operation.Id, helper);
        operation = await driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await driver.ApproveReleaseAsync();
        await driver.SyncAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => driver.RunAsync(operation.Id, crash.Token));
        var node = Assert.Single(world.Api.Nodes);
        Assert.Equal("Pending", node.Status);
        Assert.Null((await driver.HostAsync()).ApiNodeId);

        await host.RestartAsync();
        await driver.SyncAsync();
        operation = await driver.RunAsync(operation.Id);

        Assert.Equal("Active", operation.State);
        Assert.Single(world.Api.Nodes);
        Assert.Equal(2, world.Api.RegisterCalls);
        Assert.Equal(1, world.Api.RotateCalls);
        var delivered = helper.EnvText!.Split('\n').Single(l => l.StartsWith("OET_NODE_TOKEN=", StringComparison.Ordinal))["OET_NODE_TOKEN=".Length..];
        Assert.True(world.Api.TokenAccepted(node.Id, delivered));
    }
}
