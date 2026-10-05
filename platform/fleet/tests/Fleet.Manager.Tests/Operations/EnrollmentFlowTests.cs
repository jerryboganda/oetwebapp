using System.Text;
using Fleet.Core.Domain;
using Fleet.Core.Validation;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Operations;

/// <summary>Enrollment end to end over the real services and database (OET-RWP/1 section 8): states, steps, trust, secrets, failures.</summary>
public sealed class EnrollmentFlowTests : IAsyncLifetime
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

    [Fact]
    public async Task A_helper_goes_from_nothing_to_active_and_everything_is_created_exactly_once()
    {
        var (operation, helper) = await _driver.EnrollToActiveAsync();

        Assert.Equal("Active", operation.State);
        Assert.NotNull(operation.FinishedAt);
        Assert.All(operation.Steps, step => Assert.Contains(step.State, new[] { "done", "skipped" }));
        Assert.Equal(13, operation.Steps.Count);

        // What the playbooks and ctl verbs did on the helper.
        Assert.True(helper.FleetUser && helper.DockerInstalled && helper.FirewallApplied && helper.BaselineApplied && helper.SshHardened);
        Assert.NotNull(helper.ManagerPublicLine);
        Assert.Contains(FleetWorld.AgentDigest, helper.Images);
        Assert.Equal(FleetWorld.AgentDigest, helper.RunningDigest);
        Assert.True(helper.UnitWritten);
        Assert.Equal(1, helper.Logins);
        Assert.Equal(1, helper.Logouts);
        Assert.False(helper.LoggedIn, "the registry credential must not survive on the helper");
        Assert.Contains("OET_NODE_ID=rw_", helper.EnvText);
        Assert.Contains("OET_AGENT_IMAGE_DIGEST=" + FleetWorld.AgentDigest, helper.EnvText);
        foreach (var step in new[] { EnrollStep.FleetUser, EnrollStep.InstallKey, EnrollStep.Docker, EnrollStep.Firewall, EnrollStep.HostBaseline, EnrollStep.HardenSsh })
        {
            Assert.Equal(1, helper.ApplyCounts[step]);
        }

        // What the API knows.
        var node = Assert.Single(World.Api.Nodes);
        Assert.Equal("Active", node.Status);
        Assert.Equal("helper-eu-01", node.NodeRef);
        Assert.Equal(1, World.Api.RegisterCalls);
        Assert.Equal(FleetWorld.AgentDigest, node.AgentDigest);

        // What the manager recorded.
        var host = await _driver.HostAsync();
        Assert.Equal("Active", host.Lifecycle);
        Assert.Equal(node.Id, host.ApiNodeId);
        Assert.Equal(FleetWorld.AgentDigest, host.AgentDigest);
        Assert.Equal("Ubuntu 24.04 LTS", host.Os);
        Assert.Equal(4, host.CpuCores);
        Assert.Equal(7900, host.MemMib);
        Assert.StartsWith("SHA256:", host.HostKeySha256);
        Assert.True((await _host.Get<IAuditService>().VerifyAsync()).Intact);
        Assert.True((await _host.Get<OperationStore>().VerifyChainAsync(CancellationToken.None)).Intact);
    }

    [Fact]
    public async Task The_host_key_is_only_trusted_after_the_owner_types_the_fingerprint()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("HostKeyPending", operation.State);
        Assert.Equal(helper.Fingerprint, Assert.Single(operation.HostKeyCandidates).Fingerprint);
        Assert.Null((await _driver.HostAsync()).HostKeySha256);

        // The runner has nothing to do until the owner acts.
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("HostKeyPending", operation.State);

        await Assert.ThrowsAsync<FleetValidationException>(() =>
            _driver.Enrollment.ConfirmHostKeyAsync(operation.Id, "AAAAAAAA", "owner", CancellationToken.None));
        Assert.Null((await _driver.HostAsync()).HostKeySha256);

        operation = await _driver.ConfirmAsync(operation.Id, helper);
        Assert.Equal("HostKeyConfirmed", operation.State);
        var host = await _driver.HostAsync();
        Assert.Equal(helper.Fingerprint, host.HostKeySha256);
        Assert.Equal(helper.KeyBlob, host.HostKeyPublic);
        Assert.NotNull(host.HostKeyPinnedAt);
    }

    [Fact]
    public async Task Three_wrong_fingerprint_confirmations_fail_the_operation_and_a_retry_starts_over_at_the_prompt()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var error = await Assert.ThrowsAsync<FleetValidationException>(() =>
                _driver.Enrollment.ConfirmHostKeyAsync(operation.Id, "ZZZZZZZZ", "owner", CancellationToken.None));
            Assert.Equal(FailureReasons.HostKeyMismatch, error.Issues.Single().Code);
        }

        operation = await _driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None);
        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.HostKeyMismatch, operation.FailureReason);
        Assert.Equal("HostKeyPending", operation.ResumeState);

        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        Assert.Equal("HostKeyPending", operation.State);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        Assert.Equal("HostKeyConfirmed", operation.State);
    }

    [Fact]
    public async Task A_host_key_that_changes_after_pinning_hard_fails_disables_the_host_and_needs_a_repin()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);

        helper.KeyBlob = FakeKeys.HostKeyBlob("a different machine now answers on this address");
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.HostKeyChanged, operation.FailureReason);
        var host = await _driver.HostAsync();
        Assert.Equal("Disabled", host.Lifecycle);
        Assert.Equal(FailureReasons.HostKeyChanged, host.Alert);
        Assert.Equal(0, helper.ApplyCounts.Values.Sum());

        // Retrying is refused until the owner re-verifies the fingerprint out of band.
        var refused = await Assert.ThrowsAsync<FleetOperationException>(() => _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None));
        Assert.Equal("repin_required", refused.Code);

        var security = _host.Get<HostSecurityService>();
        var candidates = await security.BeginRepinAsync(host.Id, "owner", CancellationToken.None);
        await Assert.ThrowsAsync<FleetValidationException>(() => security.ConfirmRepinAsync(host.Id, "AAAAAAAA", "owner", CancellationToken.None));
        await security.ConfirmRepinAsync(host.Id, candidates.Single().Fingerprint["SHA256:".Length..][..8], "owner", CancellationToken.None);

        host = await _driver.HostAsync();
        Assert.Null(host.Alert);
        Assert.Equal(helper.Fingerprint, host.HostKeySha256);
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("ImageAwaitingSync", operation.State);
    }

    [Fact]
    public async Task The_owner_credential_is_destroyed_at_S8_and_leaves_no_plaintext_anywhere()
    {
        var (_, helper) = await _driver.AddAsync();
        var operationId = (await _host.Get<OperationStore>().FindEnrollAsync((await _driver.HostAsync()).Id, CancellationToken.None))!.Id;
        await _driver.RunAsync(operationId);
        await _driver.ConfirmAsync(operationId, helper);
        await _driver.SubmitOwnerKeyAsync(operationId, helper);

        var hostId = (await _driver.HostAsync()).Id;
        Assert.True(await _host.Get<CredentialStore>().ExistsActiveAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        var marker = Encoding.UTF8.GetBytes(helper.OwnerKeyText.Split('\n')[1]);
        Assert.False(ContainsBytes(await _host.ReadDatabaseBytesAsync(), marker), "the owner key must be encrypted at rest");

        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        var finished = await _driver.RunAsync(operationId);

        Assert.Equal("Active", finished.State);
        Assert.False(await _host.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.False(ContainsBytes(await _host.ReadDatabaseBytesAsync(), marker));
        Assert.DoesNotContain(helper.OwnerKeyText.Split('\n')[1], _host.Logs.All);
        Assert.All(helper.CtlLog, call => Assert.DoesNotContain("OWNER-KEY-MARKER", string.Join(' ', call.Args)));
        Assert.Equal("done", finished.Steps.Single(s => s.Name == "discard-owner-key").State);
    }

    [Fact]
    public async Task Tokens_travel_on_stdin_only_and_never_reach_argv_logs_or_the_database()
    {
        var (operation, helper) = await _driver.EnrollToActiveAsync();
        Assert.Equal("Active", operation.State);

        var nodeToken = World.Api.Nodes.Single().Tokens.Single().Value;
        const string registryToken = "ghs_TESTONLYTOKEN0123456789abcdef";

        Assert.Contains(nodeToken, helper.Stdin["put-env"]);
        Assert.Contains(registryToken, helper.Stdin["login"]);
        Assert.All(helper.CtlLog, call => Assert.DoesNotContain(nodeToken, string.Join(' ', call.Args)));
        Assert.All(helper.CtlLog, call => Assert.DoesNotContain(registryToken, string.Join(' ', call.Args)));

        Assert.DoesNotContain(nodeToken, _host.Logs.All);
        Assert.DoesNotContain(registryToken, _host.Logs.All);
        var database = await _host.ReadDatabaseBytesAsync();
        Assert.False(ContainsBytes(database, Encoding.UTF8.GetBytes(nodeToken)));
        Assert.False(ContainsBytes(database, Encoding.UTF8.GetBytes(registryToken)));

        // Only a fingerprint of the rendered token is on record.
        var hostId = (await _driver.HostAsync()).Id;
        var info = await _host.Get<CredentialStore>().GetInfoAsync(hostId, CredentialPurposes.NodeTokenRender, CancellationToken.None);
        Assert.NotNull(info);
        Assert.False(info!.HasCiphertext);
        Assert.Equal(8, info.FingerprintHint.Length);
    }

    [Fact]
    public async Task A_duplicate_add_is_a_noop_that_returns_the_existing_operation()
    {
        var (first, _) = await _driver.AddAsync();
        var duplicate = await _driver.Enrollment.AddHostAsync(
            new AddHostRequest("helper-eu-01", "Helper EU 01", "203.0.113.10", 22, "eu-central", "ExampleHost"),
            "owner",
            "request-2",
            CancellationToken.None);

        Assert.True(duplicate.AlreadyExisted);
        Assert.Equal(first.Id, duplicate.Operation.Id);

        // The same machine under a different reference is the same host.
        var sameAddress = await _driver.Enrollment.AddHostAsync(
            new AddHostRequest("another-name", "Another", "203.0.113.10", 22, null, null),
            "owner",
            "request-3",
            CancellationToken.None);
        Assert.True(sameAddress.AlreadyExisted);
        Assert.Equal(first.Id, sameAddress.Operation.Id);

        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Hosts.CountAsync());
        Assert.Equal(1, await db.Operations.CountAsync());
        Assert.Equal(13, await db.OperationSteps.CountAsync());
    }

    [Fact]
    public async Task A_duplicate_add_after_completion_is_still_a_noop_and_does_not_restart_anything()
    {
        var (finished, helper) = await _driver.EnrollToActiveAsync();
        var applyBefore = helper.ApplyCounts.Values.Sum();

        var again = await _driver.Enrollment.AddHostAsync(
            new AddHostRequest("helper-eu-01", "Helper EU 01", "203.0.113.10", 22, null, null),
            "owner",
            "request-9",
            CancellationToken.None);

        Assert.True(again.AlreadyExisted);
        Assert.Equal(finished.Id, again.Operation.Id);
        Assert.Equal("Active", again.Operation.State);
        await _driver.RunAsync(finished.Id);
        Assert.Equal(applyBefore, helper.ApplyCounts.Values.Sum());
        Assert.Equal(1, World.Api.RegisterCalls);
    }

    [Theory]
    [InlineData("helper-1", "185.252.233.186", "forbidden_host")]
    [InlineData("helper-1", "10.0.0.7", "forbidden_host")]
    [InlineData("helper-1", "203.0.113.10; rm -rf /", "address_invalid")]
    [InlineData("helper-1", "$(id).example.com", "address_invalid")]
    [InlineData("HELPER", "203.0.113.10", "node_ref_invalid")]
    [InlineData("helper-1", "api.oetwithdrhesham.co.uk", "forbidden_host")]
    public async Task Hostile_input_is_refused_before_any_row_or_connection_exists(string nodeRef, string address, string code)
    {
        var error = await Assert.ThrowsAsync<FleetValidationException>(() =>
            _driver.Enrollment.AddHostAsync(new AddHostRequest(nodeRef, "Helper", address, 22, null, null), "owner", null, CancellationToken.None));
        Assert.Contains(error.Issues, issue => issue.Code == code);

        var factory = _host.Get<IDbContextFactory<FleetDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.Hosts.CountAsync());
        Assert.Equal(0, World.Provisioner.Calls);
    }

    [Fact]
    public async Task A_removed_node_reference_can_never_be_reused()
    {
        await _driver.EnrollToActiveAsync();
        var host = await _driver.HostAsync();
        var removal = await _driver.Hosts.StartRemoveAsync(host.Id, force: false, "owner", CancellationToken.None);
        await _driver.RunAsync(removal.Id);

        var error = await Assert.ThrowsAsync<FleetValidationException>(() => _driver.Enrollment.AddHostAsync(
            new AddHostRequest("helper-eu-01", "Helper EU 01", "203.0.113.10", 22, null, null),
            "owner",
            null,
            CancellationToken.None));
        Assert.Contains(error.Issues, issue => issue.Code == "node_ref_in_use");
    }

    [Fact]
    public async Task An_unreachable_host_fails_the_scan_and_can_be_retried()
    {
        var (operation, helper) = await _driver.AddAsync();
        helper.Reachable = false;
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.HostKeyUnreachable, operation.FailureReason);
        Assert.Equal("Created", operation.ResumeState);

        helper.Reachable = true;
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        Assert.Equal("Created", operation.State);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("HostKeyPending", operation.State);
    }

    [Fact]
    public async Task Preflight_rejections_name_the_unmet_requirement()
    {
        var (operation, helper) = await _driver.AddAsync();
        helper.OsSupported = false;
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.PreflightRejected, operation.FailureReason);
        Assert.Equal("os_unsupported", operation.FailureDetail);
        Assert.Equal("failed", operation.Steps.Single(s => s.Name == "preflight").State);
        Assert.Equal("Failed", (await _driver.HostAsync()).Lifecycle);

        // A foreign OET workload is refused too.
        helper.OsSupported = true;
        helper.ForeignContainers.Add("oet-api-blue");
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("existing_oet_workload", operation.FailureDetail);

        helper.ForeignContainers.Clear();
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("ImageAwaitingSync", operation.State);
        Assert.Equal("Enrolling", (await _driver.HostAsync()).Lifecycle);
    }

    [Fact]
    public async Task A_wrong_owner_key_fails_authentication_and_a_correct_one_resumes()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.Enrollment.SubmitOwnerCredentialAsync(operation.Id, "root", FakeKeys.OwnerKeyText("WRONG-KEY"), "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.AuthFailed, operation.FailureReason);
        Assert.True(operation.AwaitingOwnerCredential);

        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        Assert.Equal("Bootstrapping", operation.State);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
    }

    [Theory]
    [InlineData("not a key at all")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nENCRYPTED PASSPHRASE-PROTECTED\n-----END OPENSSH PRIVATE KEY-----\n")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nPASSPHRASE-PROTECTED\n-----END RSA PRIVATE KEY-----\n")]
    public async Task Unusable_owner_keys_are_rejected_without_being_stored(string key)
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);

        await Assert.ThrowsAsync<FleetValidationException>(() =>
            _driver.Enrollment.SubmitOwnerCredentialAsync(operation.Id, "root", key, "owner", CancellationToken.None));
        var hostId = (await _driver.HostAsync()).Id;
        Assert.False(await _host.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.Equal("HostKeyConfirmed", (await _driver.Hosts.GetOperationAsync(operation.Id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task An_owner_credential_older_than_sixty_minutes_stops_the_bootstrap_until_it_is_resubmitted()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);

        World.Time.Advance(TimeSpan.FromMinutes(61));
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.OwnerCredentialExpired, operation.FailureReason);
        Assert.Equal(0, helper.ApplyCounts.Values.Sum());
        var refused = await Assert.ThrowsAsync<FleetOperationException>(() => _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None));
        Assert.Equal("owner_credential_required", refused.Code);

        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
    }

    [Fact]
    public async Task Cancelling_destroys_the_owner_credential_and_frees_the_host_for_removal()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        var hostId = (await _driver.HostAsync()).Id;
        Assert.True(await _host.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));

        operation = await _driver.Enrollment.CancelAsync(operation.Id, "owner", CancellationToken.None);

        Assert.Equal("Cancelled", operation.State);
        Assert.False(await _host.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.Equal("Failed", (await _driver.HostAsync()).Lifecycle);
        await Assert.ThrowsAsync<FleetOperationException>(() => _driver.Enrollment.CancelAsync(operation.Id, "owner", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_before_the_key_is_pinned_releases_the_address_for_a_new_add()
    {
        var (operation, _) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        await _driver.Enrollment.CancelAsync(operation.Id, "owner", CancellationToken.None);

        Assert.Equal("Removed", (await _driver.HostAsync()).Lifecycle);
        var again = await _driver.Enrollment.AddHostAsync(
            new AddHostRequest("helper-eu-02", "Helper EU 02", "203.0.113.10", 22, null, null),
            "owner",
            null,
            CancellationToken.None);
        Assert.False(again.AlreadyExisted);
    }

    [Fact]
    public async Task Without_a_registry_token_the_operation_waits_for_a_sync_and_then_continues()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();

        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("ImageAwaitingSync", operation.State);
        Assert.Equal("done", operation.Steps.Single(s => s.Name == "discard-owner-key").State);
        Assert.Empty(helper.Images);

        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("ImageAwaitingSync", operation.State);

        await _driver.SyncAsync();
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
        Assert.Contains(FleetWorld.AgentDigest, helper.Images);
    }

    [Fact]
    public async Task An_unapproved_release_fails_the_image_step_and_approval_fixes_it()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.SyncAsync();

        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.ImageDigestUnapproved, operation.FailureReason);
        Assert.Empty(helper.Images);

        await _driver.ApproveReleaseAsync();
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
        Assert.Equal(1, helper.Pulls);
    }

    [Fact]
    public async Task A_pulled_image_with_the_wrong_id_is_refused()
    {
        World.PublishAgentImage(FleetWorld.AgentDigest, "sha256:" + new string('9', 64));
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();

        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.ImageIdMismatch, operation.FailureReason);
        Assert.False(helper.LoggedIn, "logout must run even when the pull verification fails");
        Assert.Equal(1, helper.Logouts);
    }

    [Fact]
    public async Task A_failed_pull_still_logs_out()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        World.Provisioner.Registry.Remove(FleetWorld.AgentDigest);

        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal(FailureReasons.ImagePullFailed, operation.FailureReason);
        Assert.False(helper.LoggedIn);
        Assert.Equal(1, helper.Logouts);

        World.PublishAgentImage(FleetWorld.AgentDigest, FleetWorld.AgentImageId);
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
    }

    [Fact]
    public async Task A_lost_registration_response_is_recovered_by_rotating_the_token()
    {
        World.Api.DropNextRegisterResponse = true;
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();

        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.ApiUnreachable, operation.FailureReason);
        Assert.Single(World.Api.Nodes);
        Assert.Equal(1, World.Api.RegisterCalls);

        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Active", operation.State);
        Assert.Single(World.Api.Nodes);
        Assert.Equal(2, World.Api.RegisterCalls);
        Assert.Equal(1, World.Api.RotateCalls);
        var node = World.Api.Nodes.Single();
        var delivered = helper.EnvText!.Split('\n').Single(l => l.StartsWith("OET_NODE_TOKEN=", StringComparison.Ordinal))["OET_NODE_TOKEN=".Length..];
        Assert.True(World.Api.TokenAccepted(node.Id, delivered));
    }

    [Fact]
    public async Task A_canary_mismatch_fails_the_operation_and_keeps_the_node_out_of_service()
    {
        World.Api.CanaryResultOk = false;
        var (operation, _) = await _driver.EnrollToActiveAsync();

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.CanaryMismatch, operation.FailureReason);
        Assert.Equal("Probation", World.Api.Nodes.Single().Status);

        World.Api.CanaryResultOk = true;
        operation = await _driver.Enrollment.RetryAsync(operation.Id, "owner", CancellationToken.None);
        operation = await _driver.RunAsync(operation.Id);
        Assert.Equal("Active", operation.State);
        Assert.Equal("Active", World.Api.Nodes.Single().Status);
    }

    [Fact]
    public async Task A_silent_canary_times_out()
    {
        World.Api.AutoCompleteCanary = false;
        var (operation, _) = await _driver.EnrollToActiveAsync();

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.CanaryTimeout, operation.FailureReason);
    }

    [Fact]
    public async Task An_agent_that_never_heartbeats_fails_verification()
    {
        World.Provisioner.OnAgentStart = (_, _, _) => { };
        var (operation, _) = await _driver.EnrollToActiveAsync();

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.AgentNotHeartbeating, operation.FailureReason);
        Assert.Equal("failed", operation.Steps.Single(s => s.Name == "verify").State);
    }

    [Fact]
    public async Task An_unreachable_api_fails_registration_with_api_unreachable()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        World.Api.Reachable = false;

        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.ApiUnreachable, operation.FailureReason);
        Assert.Equal("agent-start", operation.CurrentStep);
    }

    [Fact]
    public async Task Steps_failing_for_unexpected_reasons_are_reported_with_a_stable_reason_not_a_stack_trace()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        operation = await _driver.SubmitOwnerKeyAsync(operation.Id, helper);
        World.Provisioner.FailStepOnce[EnrollStep.Docker] =
            ProvisionResult_Fail("not-a-known-reason", "<b>helper said</b> \u001b[31mboom\u001b[0m " + new string('x', 900));

        operation = await _driver.RunAsync(operation.Id);

        Assert.Equal("Failed", operation.State);
        Assert.Equal(FailureReasons.InternalError, operation.FailureReason);
        Assert.True(FailureReasons.IsKnown(operation.FailureReason));
        Assert.NotNull(operation.FailureDetail);
        Assert.DoesNotContain("<b>", operation.FailureDetail);
        Assert.DoesNotContain("\u001b", operation.FailureDetail);
        Assert.True(operation.FailureDetail!.Length <= 700);
    }

    private static Fleet.Manager.Provisioning.ProvisionResult ProvisionResult_Fail(string reason, string detail) =>
        Fleet.Manager.Provisioning.ProvisionResult.Fail(reason, detail, "step failed");

    private static bool ContainsBytes(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
