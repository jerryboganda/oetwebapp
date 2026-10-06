using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>Node heartbeat, claim loop and desired-state handling (RW-033..RW-036, RW-121..RW-125).</summary>
public sealed class NodeAndClaimTests
{
    private static ClaimedJob JobFor(Harness h, int n, byte[] pdf)
    {
        var job = TestJobs.Pdf(TestIds.Job(n), pdf);
        h.Io.Inputs["pdf"] = pdf;
        return job;
    }

    [Fact]
    public async Task RW033_no_claim_is_sent_before_the_first_heartbeat_succeeded()
    {
        using var h = new Harness();

        var (delay, _) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.Empty(h.Api.Claims);
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
        Assert.Equal(AgentState.Starting, h.Status.Decision.State);
    }

    [Fact]
    public void RW033_three_consecutive_heartbeat_failures_stop_claims_and_one_success_resumes()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnNodeHeartbeat = _ => Api.Error<NodeHeartbeatResponse>(ApiKind.Network, 0);

        h.Node.Tick();
        h.Node.Tick();
        Assert.True(h.Status.MayClaim);
        h.Node.Tick();
        Assert.False(h.Status.MayClaim);

        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Id = TestIds.NodeId, Status = "Active" }, Desired = Api.Desired() });
        h.Node.Tick();
        Assert.True(h.Status.MayClaim);
    }

    [Fact]
    public void the_heartbeat_request_declares_state_kinds_capacity_load_and_held_leases()
    {
        using var h = new Harness();
        h.MakeReady();
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        var lease = TestJobs.Lease(job, h.Clock);
        lease.Stage = "extracting";
        h.Leases.Add(lease);
        h.Capacity.Add(job.Id, job.Kind, job.Limits);

        var request = h.Node.BuildRequest();

        Assert.Equal("ready", request.State);
        Assert.Equal("c2f0a6a4-3a55-4d6e-b7f1-0a9b8c7d6e5f", request.InstanceId);
        Assert.Equal(1, request.AppliedRevision);
        Assert.Equal(TestIds.Digest, request.Agent.ImageDigest);
        Assert.Equal(1, request.Agent.Protocol);
        Assert.Equal(new[] { 1 }, request.Agent.ProtocolsSupported);
        Assert.NotNull(request.Agent.StartedAt);
        var kind = Assert.Single(request.Kinds, k => k.Kind == JobKinds.PdfExtract);
        Assert.Equal(EngineVersions.Pdf, kind.EngineVersion);
        Assert.Equal(3000, request.Capacity.CpuBudgetMilli);
        Assert.Equal(2000, request.Capacity.CpuBudgetFreeMilli);
        Assert.Equal(5120 - 2304, request.Capacity.MemBudgetFreeMiB);
        Assert.Equal(3072 - 256, request.Capacity.TmpFreeMiB);
        Assert.Equal(2, request.Capacity.HeavySlotsTotal);
        Assert.Equal(1, request.Capacity.HeavySlotsFree);
        Assert.Equal("normal", request.Load.Pressure);
        var held = Assert.Single(request.Leases);
        Assert.Equal((job.Id, job.Fence, "extracting"), (held.JobId, held.Fence, held.Stage));
    }

    [Fact]
    public void a_node_heartbeat_never_carries_an_empty_kinds_list_so_a_broken_host_can_still_report_itself()
    {
        using var h = new Harness();
        h.Executors.Replace([]); // nothing runnable: the first self-check has not completed (or has failed)

        // The API answers 400 to kinds[] outside 1..8 (protocol 4.7.1), which would reject every early heartbeat.
        var starting = h.Node.BuildRequest();
        Assert.Equal("starting", starting.State);
        var advertised = Assert.Single(starting.Kinds, k => k.Kind == JobKinds.PdfExtract);
        Assert.Equal(new[] { 1 }, advertised.SchemaVersions);
        Assert.Equal(EngineVersions.Pdf, advertised.EngineVersion);
        Assert.Matches(Wire.EngineVersionPattern, advertised.EngineVersion);

        // A failed self-check is carried by the state, not by an empty list.
        h.Node.Tick();
        h.Status.SetSelfCheck(false);
        var degraded = h.Node.BuildRequest();
        Assert.Equal("degraded", degraded.State);
        Assert.Equal("self_check_failed", degraded.DegradedReason);
        Assert.InRange(degraded.Kinds.Count, 1, 8);
        Assert.Contains(degraded.Kinds, k => k.Kind == JobKinds.PdfExtract);

        // The claim path is unchanged: only runnable kinds are ever offered work.
        Assert.Empty(h.Executors.Offers(Api.Desired(), h.Capacity));
    }

    [Fact]
    public void a_runnable_kind_is_advertised_once_with_its_own_engine_version()
    {
        using var h = new Harness();
        h.MakeReady();

        var request = h.Node.BuildRequest();

        Assert.Equal(request.Kinds.Select(k => k.Kind).Distinct().Count(), request.Kinds.Count);
        Assert.Equal(h.Executors.All.Count, h.Executors.Runnable().Count);
        foreach (var runnable in h.Executors.Runnable())
        {
            var matching = Assert.Single(request.Kinds, k => k.Kind == runnable.Kind);
            Assert.Equal(runnable.EngineVersion, matching.EngineVersion);
        }
    }

    [Fact]
    public async Task RW035_claim_offers_only_allowed_runnable_kinds_with_the_real_capacity_numbers()
    {
        using var h = new Harness();
        h.MakeReady();

        await h.Claims.StepAsync(CancellationToken.None);

        var claim = Assert.Single(h.Api.Claims);
        var offer = Assert.Single(claim.Kinds);
        Assert.Equal(JobKinds.PdfExtract, offer.Kind);
        Assert.Equal(new[] { 1 }, offer.SchemaVersions);
        Assert.Equal(EngineVersions.Pdf, offer.EngineVersion);
        Assert.Equal(3000, claim.Capacity.CpuBudgetFreeMilli);
        Assert.Equal(5120, claim.Capacity.MemBudgetFreeMiB);
        Assert.Equal(3072, claim.Capacity.TmpFreeMiB);
        Assert.Equal(2, claim.Capacity.HeavySlotsFree);
        Assert.Equal(2, claim.Capacity.EffectiveConcurrency);
        Assert.Equal(1, claim.Agent.Protocol);
        Assert.Null(claim.Agent.StartedAt);
        Assert.True(Guid.TryParse(claim.ClaimId, out _));
    }

    [Fact]
    public async Task a_kind_the_policy_does_not_allow_is_not_offered()
    {
        using var h = new Harness();
        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Active" }, Desired = Api.Desired(kinds: [JobKinds.MediaAudioExtract]) });
        h.MakeReady();

        var (delay, wakeable) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.Empty(h.Api.Claims);
        Assert.True(wakeable);
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public async Task RW034_a_lost_claim_response_is_replayed_with_the_same_claimId_then_a_fresh_one_is_used()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnClaim = _ => Api.Error<ClaimResponse>(ApiKind.Timeout, 0);

        await h.Claims.StepAsync(CancellationToken.None);
        await h.Claims.StepAsync(CancellationToken.None);
        Assert.Equal(h.Api.Claims[0].ClaimId, h.Api.Claims[1].ClaimId);
        Assert.Equal(h.Api.Claims[0].ClaimId, h.Claims.PendingClaimId);

        h.Api.OnClaim = _ => Api.Empty<ClaimResponse>("no_work", 10);
        await h.Claims.StepAsync(CancellationToken.None);
        Assert.Equal(h.Api.Claims[0].ClaimId, h.Api.Claims[2].ClaimId);
        Assert.Null(h.Claims.PendingClaimId);

        await h.Claims.StepAsync(CancellationToken.None);
        Assert.NotEqual(h.Api.Claims[2].ClaimId, h.Api.Claims[3].ClaimId);
    }

    [Fact]
    public async Task RW034_retry_after_is_honoured_with_up_to_twenty_percent_jitter_and_the_poll_floor()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnClaim = _ => Api.Empty<ClaimResponse>("no_work", 10);

        var (delay, wakeable) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.False(wakeable);
        Assert.InRange(delay.TotalSeconds, 10.0, 12.0);
        Assert.InRange(ClaimLoop.Poll(TimeSpan.FromSeconds(1), new PollSettings { Min = 5 }).TotalSeconds, 5.0, 6.0);
        Assert.InRange(ClaimLoop.Poll(null, new PollSettings { Idle = 10, Min = 5 }).TotalSeconds, 10.0, 12.0);
    }

    [Fact]
    public async Task rate_limited_claims_wait_at_least_the_server_value()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnClaim = _ => Api.Error<ClaimResponse>(ApiKind.RateLimited, 429, "rate_limited", retryAfterSeconds: 60);

        var (delay, _) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.InRange(delay.TotalSeconds, 60.0, 72.0);
    }

    [Fact]
    public async Task a_draining_204_marks_the_server_draining_and_stops_further_claims()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnClaim = _ => Api.Empty<ClaimResponse>("draining", 30);

        await h.Claims.StepAsync(CancellationToken.None);
        await h.Claims.StepAsync(CancellationToken.None);

        Assert.Single(h.Api.Claims);
        Assert.Equal(AgentState.Draining, h.Status.Decision.State);
    }

    [Fact]
    public async Task a_returned_job_is_started_and_the_next_claim_follows_immediately()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        var job = JobFor(h, 1, pdf);
        h.Api.OnClaim = _ => Api.Ok(new ClaimResponse { Job = job });

        var (delay, wakeable) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, delay);
        Assert.False(wakeable);
        await Harness.WaitForAsync(() => h.Api.Completes.Count == 1);
        var complete = Assert.Single(h.Api.Completes);
        Assert.Equal(job.Id, complete.JobId);
        await Harness.WaitForAsync(() => h.Supervisor.RunningCount == 0);
        Assert.Equal(0, h.Capacity.RunningCount);
        Assert.Equal(0, h.Leases.Count);
    }

    [Fact]
    public async Task a_malformed_job_from_the_server_is_ignored_and_never_started()
    {
        using var h = new Harness();
        h.MakeReady();
        var job = TestJobs.Pdf("not-a-job-id", CanaryPdf.Build());
        h.Api.OnClaim = _ => Api.Ok(new ClaimResponse { Job = job });

        var (delay, _) = await h.Claims.StepAsync(CancellationToken.None);

        Assert.Equal(0, h.Supervisor.RunningCount);
        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public async Task capacity_is_registered_before_the_next_claim_so_two_slots_never_oversubscribe()
    {
        using var h = new Harness();
        h.MakeReady();
        var gate = new TaskCompletionSource();
        var executor = new BlockingExecutor(gate.Task);
        h.Executors.Replace([executor]);
        var n = 0;
        h.Api.OnClaim = _ => Api.Ok(new ClaimResponse { Job = TestJobs.Pdf(TestIds.Job(++n), CanaryPdf.Build()) });

        await h.Claims.StepAsync(CancellationToken.None);
        await h.Claims.StepAsync(CancellationToken.None);
        var third = await h.Claims.StepAsync(CancellationToken.None);

        Assert.Equal(2, h.Api.Claims.Count);
        Assert.Equal(2, h.Capacity.RunningCount);
        Assert.Equal(TimeSpan.FromSeconds(1), third.Delay);
        Assert.Equal(1, h.Api.Claims[1].Capacity.HeavySlotsFree);
        gate.SetResult();
        await Harness.WaitForAsync(() => h.Supervisor.RunningCount == 0);
    }

    [Fact]
    public void RW122_a_policy_change_is_applied_and_a_newer_revision_header_pokes_the_heartbeat()
    {
        using var h = new Harness();
        var poked = 0;
        h.Status.DesiredRevisionStale += () => poked++;
        h.MakeReady();
        Assert.Equal(1, h.Status.AppliedRevision);

        h.Status.NoteDesiredRevision(2);
        Assert.Equal(1, poked);

        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse
        {
            Node = new NodeRef { Status = "Draining" },
            Desired = Api.Desired(revision: 2, drain: true, maxConcurrency: 1),
        });
        h.Node.Tick();

        Assert.Equal(2, h.Status.AppliedRevision);
        Assert.Equal(AgentState.Draining, h.Status.Decision.State);
        Assert.Equal(1, h.Capacity.Configured);
        h.Status.NoteDesiredRevision(2);
        Assert.Equal(1, poked);
    }

    [Fact]
    public void RW123_an_unapproved_image_digest_stops_claiming_but_keeps_heartbeating_and_reports_why()
    {
        using var h = new Harness();
        var desired = Api.Desired();
        desired.AgentImage!.ApprovedDigests = ["sha256:" + new string('1', 64)];
        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Active" }, Desired = desired });

        h.MakeReadyIgnoringState();
        var request = h.Node.BuildRequest();

        Assert.False(h.Status.MayClaim);
        Assert.Equal(AgentState.Degraded, h.Status.Decision.State);
        Assert.Equal("degraded", request.State);
        Assert.Equal("unapproved_digest", request.DegradedReason);
        h.Node.Tick();
        Assert.True(h.Api.NodeHeartbeats.Count >= 2);
    }

    [Fact]
    public void RW123_an_agent_below_the_minimum_version_stops_claiming()
    {
        using var h = new Harness();
        var desired = Api.Desired();
        desired.AgentImage!.MinVersion = "1.0.1";
        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Active" }, Desired = desired });

        h.MakeReadyIgnoringState();

        Assert.False(h.Status.MayClaim);
        Assert.Equal("below_min_version", h.Status.Decision.Reason);
    }

    [Fact]
    public void a_lease_the_api_reports_invalid_is_aborted_without_a_report()
    {
        using var h = new Harness();
        h.MakeReady();
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        var lease = TestJobs.Lease(job, h.Clock);
        h.Leases.Add(lease);
        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse
        {
            Node = new NodeRef { Status = "Active" },
            Desired = Api.Desired(),
            LeaseAudit = [new LeaseVerdict { JobId = job.Id, Fence = job.Fence, Valid = false }],
        });

        h.Node.Tick();

        Assert.Equal(AbortReason.LeaseLost, lease.Reason);
    }

    private static ApiResponse<NodeHeartbeatResponse> Success(NodeHeartbeatRequest request) =>
        Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Active" }, Desired = Api.Desired() });

    [Fact]
    public void RW121_unauthorized_and_skew_answers_move_the_agent_into_the_matching_probe_state()
    {
        using var h = new Harness();
        h.MakeReady();

        h.Api.OnNodeHeartbeat = _ => Api.Error<NodeHeartbeatResponse>(ApiKind.Unauthorized, 401);
        Assert.Equal(TimeSpan.FromSeconds(300), h.Node.Tick());
        Assert.Equal(AgentState.AuthFailed, h.Status.Decision.State);

        h.Api.OnNodeHeartbeat = Success;
        h.Node.Tick();
        h.Api.OnNodeHeartbeat = _ => Api.Error<NodeHeartbeatResponse>(ApiKind.RouteAbsent, 404);
        var delays = Enumerable.Range(0, 6).Select(_ => h.Node.Tick().TotalSeconds).ToArray();
        Assert.Equal(new[] { 30.0, 60.0, 120.0, 240.0, 300.0, 300.0 }, delays);
        Assert.Equal(AgentState.ApiUnsupported, h.Status.Decision.State);

        h.Api.OnNodeHeartbeat = Success;
        h.Node.Tick();
        h.Api.OnNodeHeartbeat = _ => Api.Error<NodeHeartbeatResponse>(ApiKind.ProtocolUnsupported, 426, "protocol_unsupported");
        Assert.Equal(TimeSpan.FromSeconds(60), h.Node.Tick());
        Assert.Equal(AgentState.ProtocolMismatch, h.Status.Decision.State);

        h.Api.OnNodeHeartbeat = Success;
        Assert.Equal(TimeSpan.FromSeconds(15), h.Node.Tick());
        Assert.Equal(AgentState.Ready, h.Status.Decision.State);
    }

    [Fact]
    public void instance_superseded_marks_the_agent_for_exit()
    {
        using var h = new Harness();
        h.MakeReady();
        var changes = new List<AgentState>();
        h.Status.Changed += d => changes.Add(d.State);
        h.Api.OnNodeHeartbeat = _ => Api.Error<NodeHeartbeatResponse>(ApiKind.Conflict, 409, "instance_superseded");

        h.Node.Tick();

        Assert.Equal(AgentState.Superseded, h.Status.Decision.State);
        Assert.Contains(AgentState.Superseded, changes);
    }

    [Fact]
    public void a_quarantined_node_status_stops_claims_with_the_server_reason()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Api.OnNodeHeartbeat = _ => Api.Ok(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Quarantined" }, Desired = Api.Desired() });

        h.Node.Tick();

        Assert.False(h.Status.MayClaim);
        Assert.Equal("node_quarantined", h.Status.Decision.Reason);
    }

    [Fact]
    public void the_final_heartbeat_reports_state_stopping()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Status.BeginStopping();

        h.Node.SendFinal();

        Assert.Equal("stopping", h.Api.NodeHeartbeats[^1].State);
    }

    [Fact]
    public void the_protocol_probe_cycles_through_every_supported_number()
    {
        var negotiator = new ProtocolNegotiator([1, 2]);
        Assert.Equal(2, negotiator.Current);
        Assert.False(negotiator.TryDowngrade([3]));
        Assert.True(negotiator.TryDowngrade([1]));
        Assert.Equal(1, negotiator.Current);
        Assert.Equal(1, negotiator.NextProbe());
        Assert.Equal(2, negotiator.NextProbe());
    }

    /// <summary>An executor that waits for a gate so tests can hold slots open.</summary>
    private sealed class BlockingExecutor : IJobExecutor
    {
        private readonly Task _gate;

        public BlockingExecutor(Task gate) => _gate = gate;

        public string Kind => JobKinds.PdfExtract;
        public int SchemaVersion => 1;
        public string EngineVersion => EngineVersions.Pdf;

        public async Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            return ExecutionResult.Ok(Encoding.UTF8.GetBytes("{}"), null, 0, 0);
        }
    }
}

internal static class HarnessExtensions
{
    /// <summary>Applies the first heartbeat and the self-check without asserting the resulting state.</summary>
    public static void MakeReadyIgnoringState(this Harness h)
    {
        h.Node.Tick();
        h.Status.SetSelfCheck(true);
    }
}
