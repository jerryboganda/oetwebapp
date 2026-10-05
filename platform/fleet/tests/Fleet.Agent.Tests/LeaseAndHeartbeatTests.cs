namespace Fleet.Agent.Tests;

/// <summary>Lease and job-heartbeat logic of protocol 4.2.3 (RW-040..RW-055).</summary>
public sealed class LeaseAndHeartbeatTests
{
    private static (JobHeartbeatService Service, FakeApi Api, FakeClock Clock, AgentStatus Status) Create()
    {
        var clock = new FakeClock();
        var api = new FakeApi();
        var status = new AgentStatus(TestLog.Instance, TestIds.Digest, "1.0.0");
        return (new JobHeartbeatService(api, clock, status, TestLog.Instance), api, clock, status);
    }

    private static JobLease NewLease(FakeClock clock, string? id = null, long fence = 7) =>
        new(id ?? TestIds.Job(1), fence, 20, clock.NowMs, clock.NowMs + 115_000);

    [Fact]
    public void RW054_local_expiry_is_send_time_plus_remaining_minus_five_seconds()
    {
        var lease = new JobLease(TestIds.Job(1), 1, 20, 0, 0);

        lease.UpdateExpiry(sendMonoMs: 10_000, leaseRemainingMs: 120_000);

        Assert.Equal(125_000, lease.LocalExpiryMs);
        Assert.False(lease.IsExpired(124_999));
        Assert.True(lease.IsExpired(125_000));
    }

    [Fact]
    public void the_first_abort_reason_wins_and_cancels_the_token_once()
    {
        var lease = new JobLease(TestIds.Job(1), 1, 20, 0, 1000);

        Assert.True(lease.Abort(AbortReason.LeaseLost));
        Assert.False(lease.Abort(AbortReason.Timeout));

        Assert.Equal(AbortReason.LeaseLost, lease.Reason);
        Assert.True(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public void aborting_after_dispose_is_harmless()
    {
        var lease = new JobLease(TestIds.Job(1), 1, 20, 0, 1000);
        lease.Dispose();

        Assert.True(lease.Abort(AbortReason.Shutdown));
    }

    [Fact]
    public void heartbeats_are_sent_every_twenty_seconds_and_extend_the_local_expiry()
    {
        var (service, api, clock, _) = Create();
        var lease = NewLease(clock);
        service.Register(lease);

        service.Pass();
        Assert.Empty(api.JobHeartbeats);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();

        var sent = Assert.Single(api.JobHeartbeats);
        Assert.Equal(7, sent.Request.Fence);
        Assert.Equal(clock.NowMs + 120_000 - 5_000, lease.LocalExpiryMs);

        clock.Advance(TimeSpan.FromSeconds(19));
        service.Pass();
        Assert.Single(api.JobHeartbeats);
        clock.Advance(TimeSpan.FromSeconds(1));
        service.Pass();
        Assert.Equal(2, api.JobHeartbeats.Count);
    }

    [Fact]
    public void RW055_a_409_lease_lost_aborts_the_job_and_sends_nothing_further()
    {
        var (service, api, clock, _) = Create();
        api.OnJobHeartbeat = (_, _) => Api.Error<JobHeartbeatResponse>(ApiKind.Conflict, 409, "lease_lost", "expired");
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();
        clock.Advance(TimeSpan.FromSeconds(40));
        service.Pass();

        Assert.Equal(AbortReason.LeaseLost, lease.Reason);
        Assert.Single(api.JobHeartbeats);
    }

    [Fact]
    public void RW054_a_lease_past_its_local_expiry_is_aborted_without_any_call()
    {
        var (service, api, clock, _) = Create();
        api.OnJobHeartbeat = (_, _) => Api.Error<JobHeartbeatResponse>(ApiKind.Network, 0);
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(116));
        service.Pass();

        Assert.Equal(AbortReason.LocalExpiry, lease.Reason);
    }

    [Fact]
    public void transient_failures_retry_with_backoff_inside_the_interval_and_never_abort_early()
    {
        var (service, api, clock, _) = Create();
        var calls = 0;
        api.OnJobHeartbeat = (_, _) => ++calls < 3
            ? Api.Error<JobHeartbeatResponse>(ApiKind.Network, 0)
            : Api.Ok(new JobHeartbeatResponse { LeaseRemainingMs = 120_000 });
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass(); // fails; next attempt within ~1 s
        clock.Advance(TimeSpan.FromSeconds(2));
        service.Pass(); // fails again; next within ~2 s
        clock.Advance(TimeSpan.FromSeconds(3));
        service.Pass(); // succeeds

        Assert.Equal(3, api.JobHeartbeats.Count);
        Assert.Equal(AbortReason.None, lease.Reason);
        Assert.Equal(clock.NowMs + 115_000, lease.LocalExpiryMs);
    }

    [Fact]
    public void RW123_unauthorized_job_heartbeat_enters_auth_failed_and_aborts()
    {
        var (service, api, clock, status) = Create();
        api.OnJobHeartbeat = (_, _) => Api.Error<JobHeartbeatResponse>(ApiKind.Unauthorized, 401);
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();

        Assert.Equal(AbortReason.AuthFailed, lease.Reason);
        Assert.Equal(AgentState.AuthFailed, status.Decision.State);
    }

    [Fact]
    public void RW121_rollback_skew_stops_job_heartbeats_and_lets_the_lease_expire_locally()
    {
        var (service, api, clock, status) = Create();
        api.OnJobHeartbeat = (_, _) => Api.Error<JobHeartbeatResponse>(ApiKind.RouteAbsent, 404);
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();
        Assert.Equal(AgentState.ApiUnsupported, status.Decision.State);
        Assert.Equal(AbortReason.None, lease.Reason);

        // No further heartbeats while the API cannot be spoken to; the lease runs out at its local expiry.
        clock.Advance(TimeSpan.FromSeconds(30));
        service.Pass();
        Assert.Single(api.JobHeartbeats);
        clock.Advance(TimeSpan.FromSeconds(70));
        service.Pass();
        Assert.Equal(AbortReason.LocalExpiry, lease.Reason);
    }

    [Fact]
    public void quarantine_during_a_heartbeat_aborts_the_job()
    {
        var (service, api, clock, _) = Create();
        api.OnJobHeartbeat = (_, _) => Api.Error<JobHeartbeatResponse>(ApiKind.Forbidden, 403, "node_quarantined");
        var lease = NewLease(clock);
        service.Register(lease);

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();

        Assert.Equal(AbortReason.Quarantined, lease.Reason);
    }

    [Fact]
    public void the_desired_revision_in_a_heartbeat_answer_pokes_the_node_heartbeat()
    {
        var (service, api, clock, status) = Create();
        var poked = 0;
        status.DesiredRevisionStale += () => poked++;
        api.OnJobHeartbeat = (_, _) => Api.Ok(new JobHeartbeatResponse { LeaseRemainingMs = 120_000, DesiredRevision = 9 });
        service.Register(NewLease(clock));

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();

        Assert.Equal(1, poked);
    }

    [Fact]
    public void every_due_lease_is_renewed_in_one_pass()
    {
        var (service, api, clock, _) = Create();
        service.Register(NewLease(clock, TestIds.Job(1), 1));
        service.Register(NewLease(clock, TestIds.Job(2), 2));

        clock.Advance(TimeSpan.FromSeconds(20));
        service.Pass();

        Assert.Equal(new[] { TestIds.Job(1), TestIds.Job(2) }, api.JobHeartbeats.Select(h => h.JobId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void RW053_a_dedicated_loop_keeps_its_cadence_while_the_thread_pool_is_saturated()
    {
        var ticks = 0;
        var blockers = Environment.ProcessorCount * 8;
        using var release = new ManualResetEventSlim(false);
        using var finished = new CountdownEvent(blockers);
        using var loop = new DedicatedLoop("test-heartbeat", () =>
        {
            Interlocked.Increment(ref ticks);
            return TimeSpan.FromMilliseconds(20);
        }, TestLog.Instance);

        // Occupy far more pool work items than there are cores; none of them can finish until released.
        for (var i = 0; i < blockers; i++)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                release.Wait();
                finished.Signal();
            });
        }

        loop.Start();
        Thread.Sleep(600);
        var observed = Volatile.Read(ref ticks);
        release.Set();
        finished.Wait(TimeSpan.FromSeconds(30));

        Assert.True(observed >= 10, "dedicated thread starved: " + observed);
    }

    [Fact]
    public void a_poke_cuts_the_sleep_short_and_a_failing_tick_does_not_kill_the_loop()
    {
        var ticks = 0;
        using var loop = new DedicatedLoop("test-poke", () =>
        {
            if (Interlocked.Increment(ref ticks) == 1) throw new InvalidOperationException("boom");
            return TimeSpan.FromMinutes(5);
        }, TestLog.Instance);

        loop.Start();
        SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 2, TimeSpan.FromSeconds(10)); // the failed tick retries after 1 s
        Assert.True(Volatile.Read(ref ticks) >= 2);

        loop.Poke();
        SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 3, TimeSpan.FromSeconds(10));
        Assert.True(Volatile.Read(ref ticks) >= 3);
    }
}

/// <summary>The agent state machine (protocol 5.2) as a pure function of its facts.</summary>
public sealed class AgentStateMachineTests
{
    private static AgentFacts Ready() => new() { HeartbeatOk = true, SelfCheckDone = true, SelfCheckOk = true };

    [Fact]
    public void starts_in_starting_and_cannot_claim_until_heartbeat_and_self_check_are_done()
    {
        Assert.Equal(AgentState.Starting, AgentStateMachine.Derive(new AgentFacts()).State);
        Assert.False(AgentStateMachine.Derive(new AgentFacts { HeartbeatOk = true }).MayClaim);
        Assert.False(AgentStateMachine.Derive(new AgentFacts { SelfCheckDone = true, SelfCheckOk = true }).MayClaim);
        Assert.Equal(new AgentDecision(AgentState.Ready, true, null), AgentStateMachine.Derive(Ready()));
    }

    [Fact]
    public void RW033_three_consecutive_heartbeat_failures_stop_claims_and_a_success_resumes()
    {
        Assert.True(AgentStateMachine.Derive(Ready() with { ConsecutiveHeartbeatFailures = 2 }).MayClaim);
        var stopped = AgentStateMachine.Derive(Ready() with { ConsecutiveHeartbeatFailures = 3 });
        Assert.False(stopped.MayClaim);
        Assert.Equal(AgentState.Ready, stopped.State);
        Assert.True(AgentStateMachine.Derive(Ready() with { ConsecutiveHeartbeatFailures = 0 }).MayClaim);
    }

    // Theory parameters are plain values: AgentState is internal and a public test method cannot expose it.
    [Theory]
    [InlineData(true, false, "Draining")]
    [InlineData(false, true, "Paused")]
    public void drain_and_pause_stop_claims(bool drain, bool paused, string expectedState)
    {
        var expected = Enum.Parse<AgentState>(expectedState);
        var decision = AgentStateMachine.Derive(Ready() with { DesiredDrain = drain, DesiredPaused = paused });

        Assert.Equal(expected, decision.State);
        Assert.False(decision.MayClaim);
    }

    [Fact]
    public void RW123_unapproved_digest_below_min_version_and_a_failed_self_check_are_degraded_without_claims()
    {
        foreach (var facts in new[]
        {
            Ready() with { UnapprovedDigest = true },
            Ready() with { BelowMinVersion = true },
            new AgentFacts { HeartbeatOk = true, SelfCheckDone = true, SelfCheckOk = false },
        })
        {
            var decision = AgentStateMachine.Derive(facts);
            Assert.Equal(AgentState.Degraded, decision.State);
            Assert.False(decision.MayClaim);
        }
    }

    [Fact]
    public void pressure_degrades_but_still_claims_up_to_the_effective_concurrency()
    {
        var decision = AgentStateMachine.Derive(Ready() with { PressureReduced = true });

        Assert.Equal(AgentState.Degraded, decision.State);
        Assert.True(decision.MayClaim);
        Assert.Equal("pressure", decision.Reason);
    }

    [Fact]
    public void skew_and_authentication_states_take_precedence_over_everything_but_stopping()
    {
        Assert.Equal(AgentState.AuthFailed, AgentStateMachine.Derive(Ready() with { AuthFailed = true, DesiredDrain = true }).State);
        Assert.Equal(AgentState.ApiUnsupported, AgentStateMachine.Derive(Ready() with { ApiUnsupported = true }).State);
        Assert.Equal(AgentState.ProtocolMismatch, AgentStateMachine.Derive(Ready() with { ProtocolMismatch = true }).State);
        Assert.Equal(AgentState.Superseded, AgentStateMachine.Derive(Ready() with { Superseded = true, AuthFailed = true }).State);
        Assert.Equal(AgentState.Stopping, AgentStateMachine.Derive(Ready() with { Stopping = true, Superseded = true }).State);
    }

    [Fact]
    public void quarantine_and_disable_are_a_draining_state_with_the_server_reason()
    {
        var decision = AgentStateMachine.Derive(Ready() with { NodeForbidden = "node_quarantined" });

        Assert.Equal(AgentState.Draining, decision.State);
        Assert.Equal("node_quarantined", decision.Reason);
        Assert.False(decision.MayClaim);
    }

    [Theory]
    [InlineData("Starting", "starting")]
    [InlineData("Ready", "ready")]
    [InlineData("Degraded", "degraded")]
    [InlineData("Draining", "draining")]
    [InlineData("Paused", "paused")]
    [InlineData("ProtocolMismatch", "protocol_mismatch")]
    [InlineData("Stopping", "stopping")]
    public void the_wire_state_names_match_the_heartbeat_schema(string stateName, string wire)
    {
        Assert.Equal(wire, AgentStateMachine.WireState(Enum.Parse<AgentState>(stateName)));
    }
}
