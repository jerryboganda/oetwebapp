using System.Text.Json;
using Fleet.Core.Domain;
using Fleet.Core.Policy;
using Fleet.Manager.Api;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;

namespace Fleet.Manager.Tests.ConsoleViews;

/// <summary>What the owner console reads: the read models built from the real services over a real database and the fake outside world.</summary>
public sealed class DashboardServiceTests : IAsyncLifetime
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

    private DashboardService Dashboard => _host.Get<DashboardService>();

    private Task PollAsync() => _host.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

    private async Task<HostEntity> EnrollAndPollAsync()
    {
        await _driver.EnrollToActiveAsync();
        await PollAsync();
        return await _driver.HostAsync();
    }

    // ---- the fleet overview ---------------------------------------------------------------

    [Fact]
    public async Task A_new_manager_has_an_empty_fleet_and_says_the_api_has_not_been_polled_yet()
    {
        var overview = await Dashboard.GetOverviewAsync(CancellationToken.None);

        Assert.Empty(overview.Helpers);
        Assert.Equal(0, overview.Totals.Helpers);
        Assert.Null(overview.Totals.Utilisation);
        Assert.True(overview.Primary.PressureReadable);
        Assert.True(overview.Primary.HasHeadroom);
        Assert.True(overview.IntegrityOk);
        Assert.False(overview.PullTokenHeld);
        Assert.False(overview.ApiReachable);
        Assert.Contains(overview.Attention, item => item.Severity == "warn" && item.Text.Contains("has not been polled yet"));
    }

    [Fact]
    public async Task An_active_helper_shows_its_slots_and_budgets_and_goes_offline_when_its_heartbeats_stop()
    {
        var host = await EnrollAndPollAsync();

        var online = await Dashboard.GetOverviewAsync(CancellationToken.None);

        var line = Assert.Single(online.Helpers);
        Assert.Equal(host.Id, line.Host.Id);
        Assert.Equal("online", line.Presence);
        Assert.Equal(1, online.Totals.Active);
        Assert.Equal(2, online.Totals.SlotsTotal);
        Assert.Equal(0, online.Totals.SlotsUsed);
        Assert.Equal(0.0, online.Totals.Utilisation);
        Assert.Equal(0, online.Totals.Offline);
        Assert.Equal(2000, online.Totals.CpuBudgetFreeMilli);
        Assert.Equal(3500, online.Totals.MemBudgetFreeMiB);
        Assert.True(online.ApiReachable);
        Assert.Empty(online.Attention);

        World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.AutoHeartbeat = false;
        World.Time.Advance(TimeSpan.FromMinutes(15));
        await PollAsync();

        var offline = await Dashboard.GetOverviewAsync(CancellationToken.None);
        Assert.Equal("offline", Assert.Single(offline.Helpers).Presence);
        Assert.Equal(1, offline.Totals.Offline);
        Assert.Equal(0, offline.Totals.SlotsTotal);
        Assert.Contains(offline.Attention, item => item.Severity == "warn" && item.Text.Contains("offline"));
        Assert.True(offline.Helpers[0].HeartbeatAge >= TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_running_job_counts_towards_the_slots_in_use_and_the_utilisation()
    {
        await EnrollAndPollAsync();
        World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.LeaseCount = 1;
        await PollAsync();

        var overview = await Dashboard.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, overview.Totals.SlotsUsed);
        Assert.Equal(0.5, overview.Totals.Utilisation);
        Assert.Equal(0.5, overview.Helpers[0].Utilisation);
    }

    [Fact]
    public async Task A_removed_helper_leaves_the_overview_and_a_failed_or_waiting_operation_asks_for_attention()
    {
        var (operation, helper) = await _driver.AddAsync();
        operation = await _driver.RunAsync(operation.Id);

        var waiting = await Dashboard.GetOverviewAsync(CancellationToken.None);

        Assert.Contains(waiting.Attention, item => item.Severity == "info" && item.Text.Contains("host-key fingerprint"));
        Assert.Contains(waiting.InFlight, op => op.Id == operation.Id && op.NeedsOwner);
        Assert.Equal(1, waiting.Totals.Enrolling);

        await _driver.Enrollment.CancelAsync(operation.Id, "owner", CancellationToken.None);
        var cancelled = await Dashboard.GetOverviewAsync(CancellationToken.None);
        Assert.Empty(cancelled.Helpers);
        Assert.NotNull(helper);
    }

    [Fact]
    public async Task A_helper_whose_host_key_changed_is_flagged_as_a_problem()
    {
        var host = await EnrollAndPollAsync();
        await _host.Get<HostStore>().UpdateAsync(host.Id, h => h.Alert = "host_key_changed", CancellationToken.None);

        var overview = await Dashboard.GetOverviewAsync(CancellationToken.None);

        Assert.Contains(overview.Attention, item => item.Severity == "bad" && item.Text.Contains("host key changed"));
        Assert.Equal("bad", overview.Attention[0].Severity);
    }

    [Fact]
    public async Task A_release_waiting_for_approval_is_pointed_out()
    {
        await _driver.Releases.IngestAsync(_driver.Record('9'), "ci", CancellationToken.None);

        var overview = await Dashboard.GetOverviewAsync(CancellationToken.None);

        Assert.Contains(overview.Attention, item => item.Text.Contains("waiting for your approval") && item.Href == "/Operations?view=releases");
    }

    // ---- one helper -------------------------------------------------------------------------

    [Fact]
    public async Task The_page_of_a_helper_has_its_hardware_components_limits_history_and_only_hints_of_its_credentials()
    {
        var host = await EnrollAndPollAsync();

        var page = (await Dashboard.GetHostAsync(host.Id, CancellationToken.None))!;

        Assert.Equal("Active", page.Line.Host.Lifecycle);
        Assert.True(page.Actions.CanDrain && page.Actions.CanDisable && page.Actions.CanRotateToken && page.Actions.CanRepair && page.Actions.CanRemove);
        Assert.False(page.Actions.CanResume);
        Assert.False(page.Actions.NeedsRepin);
        var report = page.Line.Report!;
        Assert.True(report.CtlReported);
        Assert.Equal("27.3.1", report.DockerVersion);
        Assert.Equal("Ubuntu 24.04 LTS", report.Os);
        Assert.NotNull(report.CtlLatencyMs);
        Assert.Contains("pdf.extract", page.Policy.Effective.AllowedKinds);
        Assert.False(page.Policy.IsOverride);
        Assert.Contains("pdf.extract", page.RegistryKinds);
        Assert.NotEmpty(page.Health);
        Assert.Equal("Active", page.Health[0].Status);
        Assert.NotNull(page.EnrollOperation);
        Assert.Contains(page.Operations, op => op.Kind == "enroll" && op.State == "Active");
        Assert.Contains(page.Audit, line => line.Action == "host.added");

        var purposes = page.Credentials.Select(c => c.Purpose).ToList();
        Assert.Contains(CredentialPurposes.ManagerSsh, purposes);
        Assert.Contains(CredentialPurposes.NodeTokenRender, purposes);
        Assert.DoesNotContain(CredentialPurposes.OwnerBootstrap, purposes);
        Assert.All(page.Credentials, c => Assert.Equal(8, c.Hint.Length));
        Assert.Null(await Dashboard.GetHostAsync("no-such-host", CancellationToken.None));
    }

    [Fact]
    public async Task The_ssh_round_trip_of_the_status_call_is_recorded_for_the_latency_column()
    {
        var host = await EnrollAndPollAsync();

        var row = (await _host.Get<HostStore>().GetAsync(host.Id, CancellationToken.None))!;

        Assert.Contains("\"latencyMs\":", row.LastStatusJson);
        Assert.NotNull(HostStatusParser.Parse(row.LastStatusJson)!.CtlLatencyMs);
    }

    [Theory]
    [InlineData("Enrolling", false, false, false, false, false, false, true)]
    [InlineData("Active", true, true, false, true, true, false, true)]
    [InlineData("Draining", true, true, true, true, true, false, true)]
    [InlineData("Disabled", false, true, true, true, true, false, true)]
    [InlineData("Removing", false, false, false, false, false, false, false)]
    [InlineData("Removed", false, false, false, false, false, false, false)]
    public void The_actions_on_offer_follow_the_lifecycle_exactly_as_the_services_allow_them(
        string lifecycle,
        bool drain,
        bool disable,
        bool resume,
        bool rotate,
        bool repair,
        bool repin,
        bool remove)
    {
        var host = new HostEntity { Lifecycle = lifecycle, ApiNodeId = lifecycle == "Enrolling" ? null : "rw_1", AgentDigest = lifecycle == "Enrolling" ? null : "sha256:x", HostKeyAlgo = lifecycle == "Enrolling" ? null : "ssh-ed25519" };

        var actions = DashboardService.ActionsFor(host);

        Assert.Equal(drain, actions.CanDrain);
        Assert.Equal(disable, actions.CanDisable);
        Assert.Equal(resume, actions.CanResume);
        Assert.Equal(rotate && lifecycle != "Removing" && lifecycle != "Removed", actions.CanRotateToken);
        Assert.Equal(repair && lifecycle != "Removing" && lifecycle != "Removed", actions.CanRepair);
        Assert.Equal(repin, actions.NeedsRepin);
        Assert.Equal(remove, actions.CanRemove);
    }

    [Fact]
    public void A_changed_host_key_blocks_resume_and_repair_until_it_is_re_pinned()
    {
        var host = new HostEntity { Lifecycle = "Disabled", ApiNodeId = "rw_1", AgentDigest = "sha256:x", HostKeyAlgo = "ssh-ed25519", Alert = "host_key_changed" };

        var actions = DashboardService.ActionsFor(host);

        Assert.True(actions.NeedsRepin);
        Assert.False(actions.CanResume);
        Assert.False(actions.CanRepair);
        Assert.True(actions.CanRemove);
    }

    [Theory]
    [InlineData("HostKeyPending", true, "fingerprint")]
    [InlineData("HostKeyConfirmed", true, "SSH key")]
    [InlineData("AwaitingOwner", true, "SSH key")]
    [InlineData("ImageAwaitingSync", false, "sync")]
    [InlineData("Bootstrapping", false, "")]
    [InlineData("Active", false, "")]
    public void What_the_owner_has_to_do_next_is_named_for_every_state_that_waits_for_them(string state, bool needsOwner, string contains)
    {
        var (next, owner) = DashboardService.NextActionFor(new OperationEntity { State = state });

        Assert.Equal(needsOwner, owner);
        Assert.Contains(contains, next);
    }

    [Fact]
    public void A_failed_operation_says_what_to_do_for_the_reasons_that_need_the_owner()
    {
        Assert.Contains("SSH key", DashboardService.NextActionFor(new OperationEntity { State = "Failed", FailureReason = FailureReasons.AuthFailed }).Next);
        Assert.Contains("SSH key", DashboardService.NextActionFor(new OperationEntity { State = "Failed", FailureReason = FailureReasons.OwnerCredentialExpired }).Next);
        Assert.Contains("re-pin", DashboardService.NextActionFor(new OperationEntity { State = "Failed", FailureReason = FailureReasons.HostKeyChanged }).Next);
        var (other, needsOwner) = DashboardService.NextActionFor(new OperationEntity { State = "Failed", FailureReason = FailureReasons.CanaryTimeout });
        Assert.True(needsOwner);
        Assert.Contains("retry or cancel", other);
    }

    // ---- an operation, the list, releases, audit ---------------------------------------------

    [Fact]
    public async Task An_operation_page_knows_what_a_key_can_do_and_whether_one_is_saved()
    {
        var (operation, helper) = await _driver.AddAsync();

        var created = (await Dashboard.GetOperationAsync(operation.Id, CancellationToken.None))!;
        Assert.Equal(OwnerKeyAcceptance.Stage, created.KeyAcceptance);
        Assert.Null(created.SavedKey);
        Assert.True(created.CanCancel);
        Assert.False(created.CanRetry);

        await _driver.Enrollment.StageOwnerCredentialAsync(operation.Id, "root", helper.OwnerKeyText, null, "owner", CancellationToken.None);
        var saved = (await Dashboard.GetOperationAsync(operation.Id, CancellationToken.None))!;
        Assert.NotNull(saved.SavedKey);
        Assert.Equal(8, saved.SavedKey!.Hint.Length);
        Assert.NotNull(saved.SavedKey.ExpiresAt);

        Assert.Null(await Dashboard.GetOperationAsync("op_missing", CancellationToken.None));
    }

    [Fact]
    public async Task The_operations_list_filters_by_kind_and_state_and_the_audit_tab_can_verify_the_chain()
    {
        var host = await EnrollAndPollAsync();
        await _host.Get<HostService>().StartDrainAsync(host.Id, "owner", CancellationToken.None);

        var all = await Dashboard.GetOperationsAsync(null, null, CancellationToken.None);
        Assert.Contains(all.Operations, op => op.Kind == "enroll");
        Assert.Contains(all.Operations, op => op.Kind == "drain");
        Assert.Equal(1, all.OpenCount);
        Assert.Contains("rotate-token", all.Kinds);

        var drains = await Dashboard.GetOperationsAsync("drain", null, CancellationToken.None);
        Assert.All(drains.Operations, op => Assert.Equal("drain", op.Kind));
        var open = await Dashboard.GetOperationsAsync(null, "open", CancellationToken.None);
        Assert.All(open.Operations, op => Assert.True(op.IsOpen));
        var done = await Dashboard.GetOperationsAsync(null, "done", CancellationToken.None);
        Assert.All(done.Operations, op => Assert.False(op.IsOpen));
        var failed = await Dashboard.GetOperationsAsync(null, "failed", CancellationToken.None);
        Assert.Empty(failed.Operations);

        var plain = await Dashboard.GetAuditAsync(50, verify: false, CancellationToken.None);
        Assert.Null(plain.ChainIntact);
        Assert.NotEmpty(plain.Lines);
        var verified = await Dashboard.GetAuditAsync(50, verify: true, CancellationToken.None);
        Assert.True(verified.ChainIntact);
        Assert.True(verified.ChainChecked > 5);
    }

    [Fact]
    public async Task Releases_show_which_is_current_and_which_sit_in_the_rollback_window()
    {
        await _driver.ApproveReleaseAsync('1', FleetWorld.AgentDigest, FleetWorld.AgentImageId);
        await _driver.Releases.IngestAsync(_driver.Record('2', FleetWorld.NextDigest, FleetWorld.NextImageId), "ci", CancellationToken.None);

        var view = await Dashboard.GetReleasesAsync(CancellationToken.None);

        Assert.Equal(2, view.Releases.Count);
        var approved = view.Releases.Single(r => r.Release.Approved);
        Assert.True(approved.IsCurrent);
        Assert.True(approved.InWindow);
        var waiting = view.Releases.Single(r => !r.Release.Approved);
        Assert.False(waiting.IsCurrent);
        Assert.False(waiting.InWindow);
        Assert.False(view.PullTokenHeld);
        Assert.Null(view.OpenRollout);
    }

    // ---- workloads ----------------------------------------------------------------------------

    [Fact]
    public async Task Workloads_show_the_queue_from_the_api_the_eligible_helpers_and_where_the_next_job_goes()
    {
        await EnrollAndPollAsync();
        World.Api.StatsJson = "{\"queue\":{\"pdf.extract\":{\"Queued\":4,\"Leased\":1,\"Succeeded\":9,\"Failed\":1}},\"oldestQueuedAgeSeconds\":125,\"leasedCount\":1,\"leasedWeightByNode\":{}}";

        var view = await Dashboard.GetWorkloadsAsync(CancellationToken.None);

        Assert.True(view.StatsAvailable);
        Assert.Equal(125L, view.OldestQueuedAgeSeconds);
        var pdf = view.Kinds.Single(k => k.Kind == "pdf.extract");
        Assert.Equal(4, pdf.Queue.Queued);
        Assert.Equal(1, pdf.Queue.Leased);
        Assert.Equal(0.9, pdf.Queue.CompletionRate!.Value, 3);
        Assert.Equal(new[] { EnrollmentDriver.DefaultNodeRef }, pdf.EligibleNodes.ToArray());
        Assert.Equal(1, pdf.AllowedNodes);
        Assert.Equal("helper", pdf.Placement);
        Assert.Contains(EnrollmentDriver.DefaultNodeRef, pdf.PlacementReason);
        Assert.Equal("OET · Content papers", pdf.Project);
        var node = Assert.Single(view.Nodes);
        Assert.Equal(2, node.SlotCap);
        Assert.Equal("pdf.extract", node.Kinds);
    }

    [Fact]
    public async Task Without_an_eligible_helper_the_primary_takes_the_job_while_it_has_headroom_and_otherwise_it_waits()
    {
        var pressure = (FakePressureSource)_host.Get<IPrimaryPressureSource>();

        var local = await Dashboard.GetWorkloadsAsync(CancellationToken.None);
        var kind = local.Kinds.Single(k => k.Kind == "pdf.extract");
        Assert.Equal("primary", kind.Placement);
        Assert.Empty(kind.EligibleNodes);

        pressure.Pressure = null;
        var waiting = await Dashboard.GetWorkloadsAsync(CancellationToken.None);
        Assert.Equal("wait", waiting.Kinds.Single(k => k.Kind == "pdf.extract").Placement);
        Assert.Contains("60 minutes", waiting.Kinds.Single(k => k.Kind == "pdf.extract").PlacementReason);
    }

    [Fact]
    public async Task A_drained_helper_is_not_eligible_and_the_reason_is_shown()
    {
        var host = await EnrollAndPollAsync();
        var drain = await _host.Get<HostService>().StartDrainAsync(host.Id, "owner", CancellationToken.None);
        await _driver.Runner.RunAsync(drain.Id, CancellationToken.None);
        await PollAsync();

        var view = await Dashboard.GetWorkloadsAsync(CancellationToken.None);

        var kind = view.Kinds.Single(k => k.Kind == "pdf.extract");
        Assert.Empty(kind.EligibleNodes);
        Assert.Contains(kind.Rejections, text => text.Contains("status_Draining"));
        Assert.Equal("primary", kind.Placement);
    }

    [Fact]
    public async Task When_the_api_is_down_the_workloads_say_so_and_still_render()
    {
        await EnrollAndPollAsync();
        World.Api.Reachable = false;

        var view = await Dashboard.GetWorkloadsAsync(CancellationToken.None);

        Assert.False(view.StatsAvailable);
        Assert.Equal("unreachable", view.StatsError);
        Assert.Contains(view.Kinds, k => k.Kind == "pdf.extract");
    }

    // ---- policies, credentials, projects ----------------------------------------------------

    [Fact]
    public async Task Policies_show_the_default_until_one_is_saved_and_which_helpers_have_their_own_limits()
    {
        var host = await EnrollAndPollAsync();

        var initial = await Dashboard.GetPoliciesAsync(CancellationToken.None);
        Assert.True(initial.GlobalIsDefault);
        Assert.True(initial.RegistryKnown);
        Assert.Contains("pdf.extract", initial.RegistryKinds);
        var line = Assert.Single(initial.Hosts);
        Assert.False(line.HasOverride);

        var policies = _host.Get<PolicyService>();
        await policies.SetGlobalAsync(PolicyDefaults.Global() with { MaxConcurrency = 1, PerKind = new Dictionary<string, int> { ["pdf.extract"] = 1 } }, "owner", CancellationToken.None);
        await policies.SetHostOverrideAsync(host.Id, PolicyDefaults.Global(), "owner", CancellationToken.None);

        var saved = await Dashboard.GetPoliciesAsync(CancellationToken.None);
        Assert.False(saved.GlobalIsDefault);
        Assert.Equal(1, saved.Global.MaxConcurrency);
        Assert.True(Assert.Single(saved.Hosts).HasOverride);
        Assert.Equal(2, saved.Hosts[0].MaxConcurrency);
    }

    [Fact]
    public async Task The_credentials_view_holds_hints_and_dates_and_never_a_secret()
    {
        var (operation, helper) = await _driver.AddAsync();
        await _driver.Enrollment.StageOwnerCredentialAsync(operation.Id, "root", helper.OwnerKeyText, null, "owner", CancellationToken.None);
        var hostId = (await _driver.HostAsync()).Id;
        await File.WriteAllTextAsync(Path.Combine(_host.SecretsDirectory, "fleet_sync_token"), "SYNC-SECRET-VALUE\n");
        await File.WriteAllTextAsync(Path.Combine(_host.SecretsDirectory, "fleet_api_credential"), "API-CREDENTIAL-SECRET-VALUE\n");

        var staged = await Dashboard.GetCredentialsAsync(CancellationToken.None);

        var owner = Assert.Single(staged.OwnerKeys);
        Assert.Equal(hostId, owner.HostId);
        Assert.Equal(8, owner.Hint.Length);
        Assert.NotNull(owner.ExpiresAt);
        Assert.Contains(staged.AwaitingKey, op => op.Id == operation.Id);
        Assert.True(staged.Services.Single(s => s.Name == "CI sync token").Present);
        Assert.True(staged.Services.Single(s => s.Name == "Fleet API credential").Present);
        Assert.False(staged.Services.Single(s => s.Name == "Metrics scrape token").Present);
        Assert.Equal(8, staged.MasterKeyId.Length);
        Assert.False(staged.PreviousKeyLoaded);

        // Finish the enrollment: the owner key is gone, a manager key and a node token appear, again as hints only.
        operation = await _driver.RunAsync(operation.Id);
        operation = await _driver.ConfirmAsync(operation.Id, helper);
        await _driver.ApproveReleaseAsync();
        await _driver.SyncAsync();
        await _driver.RunAsync(operation.Id);
        await PollAsync();
        var done = await Dashboard.GetCredentialsAsync(CancellationToken.None);
        Assert.Empty(done.OwnerKeys);
        Assert.Single(done.ManagerKeys);
        var token = Assert.Single(done.NodeTokens);
        Assert.True(token.CanRotate);
        Assert.Equal(1, token.ActiveTokens);

        var everything = JsonSerializer.Serialize(staged) + JsonSerializer.Serialize(done);
        var nodeToken = World.Api.Nodes.Single().Tokens.First().Value;
        foreach (var secret in new[] { helper.OwnerKeyText.Split('\n')[1], "SYNC-SECRET-VALUE", "API-CREDENTIAL-SECRET-VALUE", nodeToken, "ghs_TESTONLYTOKEN0123456789abcdef" })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_project_overview_reports_the_integration_and_says_what_it_cannot_see()
    {
        await EnrollAndPollAsync();
        await File.WriteAllTextAsync(Path.Combine(_host.SecretsDirectory, "fleet_api_credential"), "API-CREDENTIAL-SECRET-VALUE\n");

        var view = await Dashboard.GetProjectsAsync(CancellationToken.None);

        Assert.True(view.Oet.ApiReachable);
        Assert.True(view.Oet.CredentialProvisioned);
        Assert.True(view.Oet.StatusFetched);
        Assert.Equal(1, view.Oet.ProtocolCurrent);
        Assert.Equal(1, view.Oet.ProtocolMinimum);
        Assert.Contains("pdf.extract", view.Oet.Kinds);
        Assert.Equal(1, view.Oet.NodesRegistered);
        Assert.Equal(1, view.Oet.NodesActive);
        Assert.Equal(1, view.Oet.NodesOnline);
        Assert.False(view.Oet.SyncEndpointEnabled);
        Assert.Equal(0, view.Oet.UnapprovedReleases);
        Assert.NotNull(view.Oet.CurrentRelease);
        Assert.Equal(3, view.Projects.Count);
        Assert.Equal("unknown", view.Projects.Single(p => p.Name.StartsWith("Other projects")).Status);
        Assert.DoesNotContain("API-CREDENTIAL-SECRET-VALUE", JsonSerializer.Serialize(view), StringComparison.Ordinal);

        World.Api.Reachable = false;
        var down = await Dashboard.GetProjectsAsync(CancellationToken.None);
        Assert.False(down.Oet.StatusFetched);
        Assert.Equal("unreachable", down.Oet.StatusError);
    }

    // ---- the credential actions ---------------------------------------------------------------

    [Fact]
    public async Task An_owner_key_can_be_erased_early_and_the_erasure_is_audited_without_any_secret()
    {
        var (operation, helper) = await _driver.AddAsync();
        var hostId = (await _driver.HostAsync()).Id;
        await _driver.Enrollment.StageOwnerCredentialAsync(operation.Id, "root", helper.OwnerKeyText, null, "owner", CancellationToken.None);
        var credentials = _host.Get<CredentialService>();

        Assert.True(await credentials.RevokeOwnerKeyAsync(hostId, "owner", CancellationToken.None));
        Assert.False(await _host.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.False(await credentials.RevokeOwnerKeyAsync(hostId, "owner", CancellationToken.None));
        await Assert.ThrowsAsync<FleetNotFoundException>(() => credentials.RevokeOwnerKeyAsync("no-such-host", "owner", CancellationToken.None));

        var audit = await _host.Get<IAuditService>().ListAsync(100);
        Assert.Equal(2, audit.Count(row => row.Action == "credential.owner_key_revoked"));
        Assert.DoesNotContain(audit, row => row.DetailsJson.Contains(helper.OwnerKeyText.Split('\n')[1], StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_registry_pull_token_can_be_discarded_and_the_action_is_audited()
    {
        await _driver.SyncAsync();
        var tokens = _host.Get<RolloutTokenHolder>();
        Assert.True(tokens.HasToken);
        var credentials = _host.Get<CredentialService>();

        Assert.True(await credentials.DiscardPullTokenAsync("owner", CancellationToken.None));
        Assert.False(tokens.HasToken);
        Assert.False(await credentials.DiscardPullTokenAsync("owner", CancellationToken.None));
        Assert.Contains(await _host.Get<IAuditService>().ListAsync(50), row => row.Action == "release.pull_token_discarded");
    }

    // ---- the health history -------------------------------------------------------------------

    private static ApiNode Node(string id, string status, string health) =>
        new(id, "ref-" + id, "Name", status, health, null, null, null, null, null, null, null, null, null, null, 0, 0, null, null);

    [Fact]
    public void The_history_records_only_real_changes_newest_first_and_is_bounded()
    {
        var state = new FleetState();
        var at = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        state.SetNodes(new[] { Node("a", "Active", "Online") }, at);
        state.SetNodes(new[] { Node("a", "Active", "Online") }, at.AddSeconds(15));
        state.SetNodes(new[] { Node("a", "Active", "Stale") }, at.AddSeconds(30));
        state.SetNodes(new[] { Node("a", "Draining", "Stale") }, at.AddSeconds(45));

        var history = state.HistoryFor("a");
        Assert.Equal(3, history.Count);

        // Newest first: Draining/Stale (from Active/Stale), Active/Stale (from Active/Online), Active/Online (first seen).
        Assert.Equal("Draining", history[0].Status);
        Assert.Equal("Stale", history[0].Health);
        Assert.Equal("Active", history[0].FromStatus);
        Assert.Equal("Stale", history[0].FromHealth);
        Assert.Equal("Stale", history[1].Health);
        Assert.Equal("Online", history[1].FromHealth);
        Assert.Null(history[2].FromStatus);
        Assert.Null(history[2].FromHealth);
        Assert.Equal("Online", history[2].Health);
        Assert.Equal(at, history[2].At);
        Assert.Empty(state.HistoryFor("unknown"));

        for (var i = 0; i < 250; i++)
        {
            state.SetNodes(new[] { Node("b", "Active", i % 2 == 0 ? "Online" : "Offline") }, at.AddMinutes(i));
        }

        Assert.Equal(FleetState.MaxHistoryPerNode, state.HistoryFor("b").Count);
        Assert.Equal(at.AddMinutes(249), state.HistoryFor("b")[0].At);
    }

    [Fact]
    public async Task The_manager_records_a_helper_going_offline_in_its_history()
    {
        var host = await EnrollAndPollAsync();
        World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.AutoHeartbeat = false;
        World.Time.Advance(TimeSpan.FromMinutes(15));
        await PollAsync();

        var page = (await Dashboard.GetHostAsync(host.Id, CancellationToken.None))!;

        Assert.Equal("Offline", page.Health[0].Health);
        Assert.Equal("Online", page.Health[0].FromHealth);
        Assert.Contains(page.Health, change => change.Health == "Online");
    }
}
