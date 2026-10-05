using Npgsql;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The lease state machine on real PostgreSQL (OET-RWP/1 sections 3 and 4): idempotent enqueue, claim (SKIP LOCKED, weighted,
/// capacity aware), heartbeat, fail, and the single reclaim path. RW-020 to RW-031, RW-040 to RW-051, RW-082. Skipped (never failed)
/// when <c>OET_TEST_POSTGRES_CONNECTION</c> is not set.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteLeaseStateMachinePostgresTests
{
    // ── enqueue (producer side) ──────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Enqueue_IsIdempotentOnTheKey_AndALiveJobIsNeverReset()
    {
        await using var h = await RemotePgHarness.CreateAsync();

        var first = await h.EnqueueAsync();
        var again = await h.EnqueueAsync();
        var forcedWhileQueued = await h.EnqueueAsync(force: true);

        Assert.True(first.Created);
        Assert.Equal("Queued", first.State);
        Assert.False(again.Created);
        Assert.Equal(first.JobId, again.JobId);
        Assert.Equal(first.JobId, forcedWhileQueued.JobId);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));

        var node = await h.AddNodeAsync();
        var leased = await h.ClaimOneAsync(node);
        var forcedWhileLeased = await h.EnqueueAsync(force: true);

        Assert.Equal(leased.Id, forcedWhileLeased.JobId);
        Assert.Equal("Leased", forcedWhileLeased.State);
        Assert.Equal(1, (await h.JobAsync(leased.Id)).Attempt);
    }

    [PostgreSqlFact]
    public async Task Enqueue_ATerminalJobStands_UnlessForced_AndTheFenceIsPreservedAcrossTheReset()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var queued = await h.EnqueueAsync();
        var leased = await h.ClaimOneAsync(node);
        Assert.Equal(1, leased.FenceToken);

        await h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "State" = 'Succeeded', "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL, "DeadlineAt" = NULL,
                "ResultSha256" = @sha, "ApplyOutcome" = 'Applied', "SettledFence" = 1, "SettledBy" = @node
            WHERE "Id" = @id;
            """,
            ("sha", RemoteTestData.Sha), ("node", node), ("id", queued.JobId));

        var notForced = await h.EnqueueAsync();
        Assert.False(notForced.Created);
        Assert.Equal("Succeeded", notForced.State);

        var forced = await h.EnqueueAsync(force: true);
        Assert.Equal(queued.JobId, forced.JobId);
        Assert.Equal("Queued", forced.State);

        var reset = await h.JobAsync(queued.JobId);
        Assert.Equal(0, reset.Attempt);
        Assert.Equal(1, reset.FenceToken); // never reset, so a zombie of the earlier incarnation stays fenced out
        Assert.Null(reset.ResultSha256);
        Assert.Null(reset.ApplyOutcome);
        Assert.Null(reset.SettledFence);

        var reclaimed = await h.ClaimOneAsync(node);
        Assert.Equal(2, reclaimed.FenceToken);
    }

    // ── claim ────────────────────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Claim_Race_ExactlyOneNodeLeasesAJob_AndEachJobGoesToOneNode()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var nodes = new List<string>();
        for (var i = 0; i < 6; i++) nodes.Add(await h.AddNodeAsync());
        var jobs = new List<string>();
        for (var i = 0; i < 3; i++) jobs.Add((await h.EnqueueAsync("media-" + i)).JobId);

        var outcomes = await Task.WhenAll(nodes.Select(node => h.ClaimAsync(node)));

        var leased = outcomes.Where(o => o.Leased is not null).Select(o => o.Leased!.Job).ToList();
        Assert.Equal(3, leased.Count);
        Assert.Equal(3, leased.Select(j => j.Id).Distinct().Count());
        Assert.All(leased, job =>
        {
            Assert.Equal(1, job.FenceToken);
            Assert.Equal(1, job.Attempt);
        });
        Assert.Equal(3, outcomes.Count(o => o.NoContentReason == "no_work"));
        foreach (var id in jobs) Assert.Equal("Leased", await h.StateOfAsync(id));
    }

    [PostgreSqlFact]
    public async Task Claim_NeverSelectsALeasedJob_AndTheFenceOnlyMovesAtClaim()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var first = await h.AddNodeAsync();
        var second = await h.AddNodeAsync();
        await h.EnqueueAsync();

        var firstLease = await h.ClaimOneAsync(first);
        Assert.Equal(1, firstLease.FenceToken);
        Assert.Equal(1, firstLease.Attempt);

        var other = await h.ClaimAsync(second);
        Assert.Null(other.Leased);
        Assert.Equal("no_work", other.NoContentReason);

        // The reaper requeues the expired lease; it never touches the fence, and a requeue is not an attempt.
        await h.ExpireLeaseAsync(firstLease.Id);
        await using (var db = h.NewContext())
        {
            var (requeued, quarantined) = await h.Sweeper(db).ReapExpiredAsync(h.Settings.Current, CancellationToken.None);
            Assert.Equal(1, requeued);
            Assert.Equal(0, quarantined);
        }

        var requeuedRow = await h.JobAsync(firstLease.Id);
        Assert.Equal("Queued", requeuedRow.State);
        Assert.Equal(1, requeuedRow.FenceToken);
        Assert.Equal(1, requeuedRow.Attempt);
        Assert.Null(requeuedRow.LeaseOwner);

        await h.MakeDueAsync(firstLease.Id);
        var reclaimed = await h.ClaimOneAsync(second);
        Assert.Equal(firstLease.Id, reclaimed.Id);
        Assert.Equal(2, reclaimed.FenceToken);
        Assert.Equal(2, reclaimed.Attempt);
        Assert.Equal(second, reclaimed.LeaseOwner);
    }

    [PostgreSqlFact]
    public async Task Claim_ARetriedClaimWithTheSameId_ReturnsTheSameLeaseAndDoesNotIncrementAgain()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var claimId = Guid.NewGuid();

        var first = await h.ClaimAsync(node, claimId);
        var replay = await h.ClaimAsync(node, claimId);

        Assert.NotNull(first.Leased);
        Assert.NotNull(replay.Leased);
        Assert.Equal(first.Leased!.Job.Id, replay.Leased!.Job.Id);
        Assert.Equal(1, replay.Leased.Job.FenceToken);
        Assert.Equal(1, replay.Leased.Job.Attempt);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "State" = 'Leased';"""));
    }

    [PostgreSqlFact]
    public async Task Claim_HonoursPriority_Target_DueTime_AndTheNodesWeightedCapacity()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var busy = await h.AddNodeAsync(maxConcurrency: 2);
        var other = await h.AddNodeAsync();
        var low = await h.EnqueueAsync("media-low", priority: 0);
        var high = await h.EnqueueAsync("media-high", priority: 5);
        var targeted = await h.EnqueueAsync("media-target", targetNode: other);
        var delayed = await h.EnqueueAsync("media-delayed", priority: 9);
        await h.SqlAsync(
            """UPDATE "RemoteJobs" SET "NextAttemptAt" = clock_timestamp() + interval '1 hour' WHERE "Id" = @id;""",
            ("id", delayed.JobId));

        var first = await h.ClaimOneAsync(busy);
        var second = await h.ClaimOneAsync(busy);
        var third = await h.ClaimAsync(busy);
        var forOther = await h.ClaimOneAsync(other);

        Assert.Equal(high.JobId, first.Id);   // highest priority that is due
        Assert.Equal(low.JobId, second.Id);   // the targeted job is not for this node; the delayed one is not due
        Assert.Null(third.Leased);
        Assert.Equal("no_capacity", third.NoContentReason); // two weight-1 leases fill a node whose maxConcurrency is 2
        Assert.Equal(targeted.JobId, forOther.Id);
        Assert.Equal("Queued", await h.StateOfAsync(delayed.JobId));
    }

    [PostgreSqlFact]
    public async Task Claim_AKindTheNodePolicyDoesNotAllow_IsNeverLeased_WhateverTheAgentOffers()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(allowedKinds: new[] { RemoteJobKinds.CompanionIndexPrep });
        var job = await h.EnqueueAsync();

        var outcome = await h.ClaimAsync(node);

        Assert.Null(outcome.Leased);
        Assert.Equal("kind_disabled", outcome.NoContentReason);
        Assert.Equal("Queued", await h.StateOfAsync(job.JobId));
    }

    [PostgreSqlFact]
    public async Task Claim_WithTheMasterSwitchOff_Answers204Disabled_AndLeasesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var job = await h.EnqueueAsync();
        h.Flags.Set(); // every flag off

        var outcome = await h.ClaimAsync(node);

        Assert.Null(outcome.Leased);
        Assert.Equal("disabled", outcome.NoContentReason);
        Assert.Equal("Queued", await h.StateOfAsync(job.JobId));
    }

    [PostgreSqlFact]
    public async Task Claim_ByANodeThatHasNotHeartbeatedRecently_Answers204HeartbeatStale()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(freshHeartbeat: false);
        await h.EnqueueAsync();

        var outcome = await h.ClaimAsync(node);

        Assert.Null(outcome.Leased);
        Assert.Equal("heartbeat_stale", outcome.NoContentReason);
    }

    [PostgreSqlFact]
    public async Task Claim_FollowsTheStatusTable_ForEveryNodeStatus()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var job = await h.EnqueueAsync();

        var pending = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Pending));
        Assert.Equal(403, pending.Error!.StatusCode);
        Assert.Equal("node_not_active", pending.Error.Code);

        var disabled = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Disabled));
        Assert.Equal("node_disabled", disabled.Error!.Code);

        var revoked = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Revoked));
        Assert.Equal(401, revoked.Error!.StatusCode);

        var quarantined = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Quarantined));
        Assert.Equal(403, quarantined.Error!.StatusCode);
        Assert.Equal("node_quarantined", quarantined.Error.Code);

        var draining = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Draining));
        Assert.Equal("draining", draining.NoContentReason);

        var paused = await h.ClaimAsync(await h.AddNodeAsync(paused: true));
        Assert.Equal("paused", paused.NoContentReason);

        // A node on probation is only ever offered the known-answer canary: ordinary work stays queued.
        var probation = await h.ClaimAsync(await h.AddNodeAsync(RemoteNodeStatus.Probation));
        Assert.Null(probation.Leased);
        Assert.Equal("no_work", probation.NoContentReason);

        Assert.Equal("Queued", await h.StateOfAsync(job.JobId));
    }

    [PostgreSqlFact]
    public async Task Claim_AnotherInstanceOfTheSameNode_IsSuperseded_WhileTheFirstIsAlive()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(currentInstance: Guid.NewGuid().ToString("D"));
        await h.EnqueueAsync();

        var intruder = await h.ClaimAsync(node, instanceId: Guid.NewGuid());

        Assert.Equal(409, intruder.Error!.StatusCode);
        Assert.Equal("instance_superseded", intruder.Error.Code);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "State" = 'Queued';"""));
    }

    // ── fair-share placement gate (section 3.8): the database half ───────────

    [PostgreSqlFact]
    public async Task FairShare_AFullerNodeIsDeclinedTwice_ThenServed_SoNobodyStarves()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.FairShareGate = true;
        var busy = await h.AddNodeAsync();
        await h.AddNodeAsync(); // an idle, eligible peer with two free slots
        await h.InsertLeasedJobAsync(busy, RemoteJobKinds.PdfExtract); // weight 1 of 2: norm 0.5 against the peer's 0
        var queued = await h.EnqueueAsync();

        var first = await h.ClaimAsync(busy);
        var second = await h.ClaimAsync(busy);

        Assert.Null(first.Leased);
        Assert.Equal("fair_share", first.NoContentReason);
        Assert.Null(second.Leased);
        Assert.Equal("fair_share", second.NoContentReason);
        Assert.Equal("Queued", await h.StateOfAsync(queued.JobId));
        Assert.Equal(2, (await h.NodeAsync(busy)).DeclinedInARow);

        // Declined twice in a row: the third claim is served whatever the peer's load (nobody starves).
        var third = await h.ClaimAsync(busy);

        Assert.NotNull(third.Leased);
        Assert.Equal(queued.JobId, third.Leased!.Job.Id);
        Assert.Equal(0, (await h.NodeAsync(busy)).DeclinedInARow);
    }

    [PostgreSqlFact]
    public async Task FairShare_TheEmptierNodeIsServedAtOnce_AndTheGateIsInertWhileOff()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.FairShareGate = true;
        var busy = await h.AddNodeAsync();
        var idle = await h.AddNodeAsync();
        await h.InsertLeasedJobAsync(busy, RemoteJobKinds.PdfExtract);
        await h.EnqueueAsync();

        var served = await h.ClaimAsync(idle);

        Assert.NotNull(served.Leased);

        // The same situation with the gate off: the fuller node is simply served.
        await using var off = await RemotePgHarness.CreateAsync();
        var fuller = await off.AddNodeAsync();
        await off.AddNodeAsync();
        await off.InsertLeasedJobAsync(fuller, RemoteJobKinds.PdfExtract);
        await off.EnqueueAsync();

        var plain = await off.ClaimAsync(fuller);

        Assert.NotNull(plain.Leased);
    }

    // ── job heartbeat ────────────────────────────────────────────────────────

    private static async Task<RemoteHeartbeatResult> BeatAsync(RemotePgHarness h, string nodeId, string jobId, long fence)
    {
        await using var db = h.NewContext();
        return await h.Lifecycle(db).HeartbeatAsync(
            jobId,
            nodeId,
            new RemoteJobHeartbeatRequestDto { Fence = fence, Stage = "parsing", Metrics = new Dictionary<string, double> { ["pages"] = 3 } },
            CancellationToken.None);
    }

    [PostgreSqlFact]
    public async Task Heartbeat_ExtendsTheLease_ClampedToTheDeadline_OnTheDatabaseClock()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);

        await h.SqlAsync(
            """UPDATE "RemoteJobs" SET "LeaseExpiresAt" = clock_timestamp() + interval '10 seconds' WHERE "Id" = @id;""",
            ("id", job.Id));
        var shortLease = (await h.JobAsync(job.Id)).LeaseExpiresAt!.Value;

        var beat = await BeatAsync(h, node, job.Id, 1);

        Assert.Null(beat.Error);
        Assert.True(beat.LeaseExpiresAt > shortLease.AddSeconds(60));
        Assert.InRange(beat.LeaseRemainingMs, 100_000, 125_000);
        Assert.NotNull((await h.JobAsync(job.Id)).MetricsJson);

        // The lease can never outlive the hard deadline, however often it is renewed.
        await h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "DeadlineAt" = clock_timestamp() + interval '30 seconds',
                "LeaseExpiresAt" = clock_timestamp() + interval '10 seconds' WHERE "Id" = @id;
            """,
            ("id", job.Id));
        var clamped = await BeatAsync(h, node, job.Id, 1);

        Assert.Null(clamped.Error);
        var row = await h.JobAsync(job.Id);
        Assert.Equal(row.DeadlineAt, row.LeaseExpiresAt);
    }

    [PostgreSqlFact]
    public async Task Heartbeat_AWrongFenceAnExpiredLeaseOrAnotherNode_IsLeaseLost_WithTheReason()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var intruder = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);

        var wrongFence = await BeatAsync(h, node, job.Id, 2);
        Assert.Equal(409, wrongFence.Error!.StatusCode);
        Assert.Equal("lease_lost", wrongFence.Error.Code);
        Assert.Equal("superseded", wrongFence.Error.Reason);

        var notOwner = await BeatAsync(h, intruder, job.Id, 1);
        Assert.Equal("not_owner", notOwner.Error!.Reason);

        // Expiry without a reaper pass: the guard itself (the database clock) refuses, the row is still Leased.
        await h.ExpireLeaseAsync(job.Id);
        var expired = await BeatAsync(h, node, job.Id, 1);
        Assert.Equal("expired", expired.Error!.Reason);
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));

        await h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "DeadlineAt" = clock_timestamp() - interval '1 second',
                "LeaseExpiresAt" = clock_timestamp() - interval '1 second' WHERE "Id" = @id;
            """,
            ("id", job.Id));
        var pastDeadline = await BeatAsync(h, node, job.Id, 1);
        Assert.Equal("deadline", pastDeadline.Error!.Reason);

        var missing = await BeatAsync(h, node, RemoteIds.NewJobId(DateTimeOffset.UtcNow), 1);
        Assert.Equal(404, missing.Error!.StatusCode);
    }

    // ── fail ─────────────────────────────────────────────────────────────────

    private static async Task<(string? Status, bool Replayed, RemoteProblemResult? Error)> FailAsync(
        RemotePgHarness h,
        string nodeId,
        string jobId,
        long fence,
        string code)
    {
        await using var db = h.NewContext();
        return await h.Lifecycle(db).FailAsync(jobId, nodeId, new RemoteFailRequestDto { Fence = fence, Code = code, Message = "test" }, CancellationToken.None);
    }

    [PostgreSqlFact]
    public async Task Fail_ARetryableCode_RequeuesWithBackoff_ConsumesTheAttempt_AndReplaysIdempotently()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);

        var (status, replayed, error) = await FailAsync(h, node, job.Id, 1, "timeout");

        Assert.Null(error);
        Assert.Equal("queued", status);
        Assert.False(replayed);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal(1, row.Attempt);
        Assert.Equal(0, row.ReleaseCount);
        Assert.Equal("timeout", row.FailureCode);
        Assert.Equal(1, row.SettledFence);
        Assert.Equal(node, row.SettledBy);
        Assert.Null(row.LeaseOwner);
        Assert.True(await h.ScalarAsync<bool>(
            """SELECT "NextAttemptAt" > clock_timestamp() + interval '3 seconds' FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", job.Id)));

        var (replayStatus, replayFlag, replayError) = await FailAsync(h, node, job.Id, 1, "timeout");
        Assert.Null(replayError);
        Assert.True(replayFlag);
        Assert.Equal("queued", replayStatus);
        Assert.Equal(1, (await h.JobAsync(job.Id)).Attempt);
    }

    [PostgreSqlFact]
    public async Task Fail_AShutdownIsRefunded_ButOnlyUpToTheReleaseLimit()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.ReleaseLimit = 1;
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();

        var first = await h.ClaimOneAsync(node);
        await FailAsync(h, node, first.Id, 1, "shutdown");
        var refunded = await h.JobAsync(first.Id);
        Assert.Equal(0, refunded.Attempt);
        Assert.Equal(1, refunded.ReleaseCount);
        Assert.True(await h.ScalarAsync<bool>(
            """SELECT "NextAttemptAt" <= clock_timestamp() + interval '1 second' FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", first.Id)));

        await h.MakeDueAsync(first.Id);
        var second = await h.ClaimOneAsync(node);
        Assert.Equal(1, second.Attempt);
        await FailAsync(h, node, second.Id, 2, "shutdown");

        var capped = await h.JobAsync(first.Id);
        Assert.Equal(1, capped.Attempt); // the limit is spent: no further free retries
        Assert.Equal(1, capped.ReleaseCount);
    }

    [PostgreSqlFact]
    public async Task Fail_ADeterministicCode_IsTerminal_AndTheAgentCannotChooseItsOwnRefund()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);

        var (status, _, error) = await FailAsync(h, node, job.Id, 1, "not_pdf");

        Assert.Null(error);
        Assert.Equal("failed", status);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Failed", row.State);
        Assert.Equal(1, row.Attempt);
        Assert.NotNull(row.CompletedAt);
    }

    [PostgreSqlFact]
    public async Task Fail_ExhaustingTheAttempts_Quarantines_AndNothingRunsItLocally()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var queued = await h.EnqueueAsync();

        string? lastStatus = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var job = await h.ClaimOneAsync(node);
            Assert.Equal(attempt, job.Attempt);
            var (status, _, error) = await FailAsync(h, node, job.Id, attempt, "timeout");
            Assert.Null(error);
            lastStatus = status;
            if (attempt < 3) await h.MakeDueAsync(queued.JobId);
        }

        Assert.Equal("quarantined", lastStatus);
        Assert.Equal("Quarantined", await h.StateOfAsync(queued.JobId));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Quarantined"));

        // a poison suspect is never claimable again and the fallback sweep does not hand it to the in-process path
        await h.MakeDueAsync(queued.JobId);
        Assert.Null((await h.ClaimAsync(node)).Leased);
        await using var db = h.NewContext();
        Assert.Equal(0, await h.Sweeper(db).SweepFallbackAsync(CancellationToken.None));
        Assert.Equal("Quarantined", await h.StateOfAsync(queued.JobId));
    }

    [PostgreSqlFact]
    public async Task Fail_AWrongFence_IsLeaseLost_AndChangesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);

        var (status, _, error) = await FailAsync(h, node, job.Id, 9, "timeout");

        Assert.Null(status);
        Assert.Equal("lease_lost", error!.Code);
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));
    }

    // ── reaper ───────────────────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Reaper_ThreeConcurrentPassesRequeueAnExpiredLeaseExactlyOnce()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);
        await h.ExpireLeaseAsync(job.Id);

        var passes = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var db = h.NewContext();
            return await h.Sweeper(db).ReapExpiredAsync(h.Settings.Current, CancellationToken.None);
        }));

        Assert.Equal(1, passes.Sum(pass => pass.Requeued));
        Assert.Equal(0, passes.Sum(pass => pass.Quarantined));
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal("lease_expired", row.FailureCode);
        Assert.Equal(node, row.LastFailedNodeId);
        Assert.Equal(1, row.Attempt);
    }

    [PostgreSqlFact]
    public async Task Reaper_AnExpiryAtTheAttemptCap_Quarantines_AndAudits()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "Attempt" = "MaxAttempts" WHERE "Id" = @id;""", ("id", job.Id));
        await h.ExpireLeaseAsync(job.Id);

        await using var db = h.NewContext();
        var (requeued, quarantined) = await h.Sweeper(db).ReapExpiredAsync(h.Settings.Current, CancellationToken.None);

        Assert.Equal(0, requeued);
        Assert.Equal(1, quarantined);
        Assert.Equal("Quarantined", await h.StateOfAsync(job.Id));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Quarantined"));
    }

    [PostgreSqlFact]
    public async Task Reaper_ARevokedNodesExpiredLease_IsRefunded()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);
        await h.SqlAsync("""UPDATE "RemoteWorkers" SET "Status" = 'Revoked' WHERE "Id" = @id;""", ("id", node));
        await h.ExpireLeaseAsync(job.Id);

        await using var db = h.NewContext();
        await h.Sweeper(db).ReapExpiredAsync(h.Settings.Current, CancellationToken.None);

        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal(0, row.Attempt);
        Assert.Equal(1, row.ReleaseCount);
    }

    [PostgreSqlFact]
    public async Task Reaper_QueuedApplyWorkThatWaitedTooLong_FallsBackToTheLocalPath_ButShadowWorkNeverDoes()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var overdue = await h.EnqueueAsync("media-overdue");
        var fresh = await h.EnqueueAsync("media-fresh");
        var shadow = await h.EnqueueAsync("media-shadow", purpose: RemoteJobPurpose.Shadow, withFallback: false);
        await h.SqlAsync(
            """UPDATE "RemoteJobs" SET "FallbackAfter" = clock_timestamp() - interval '1 minute' WHERE "Id" = @id;""",
            ("id", overdue.JobId));

        await using (var db = h.NewContext())
        {
            Assert.Equal(1, await h.Sweeper(db).SweepFallbackAsync(CancellationToken.None));
        }

        Assert.Equal("FallbackLocal", await h.StateOfAsync(overdue.JobId));
        Assert.Equal("queue_timeout", await h.ScalarAsync<string>("""SELECT "FailureCode" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", overdue.JobId)));
        Assert.Equal("Queued", await h.StateOfAsync(fresh.JobId));
        Assert.Equal("Queued", await h.StateOfAsync(shadow.JobId));

        // With the master switch off, ALL queued apply work goes to the local path within one pass; shadow work still does not.
        h.Flags.Set();
        await using (var db = h.NewContext())
        {
            Assert.Equal(1, await h.Sweeper(db).SweepFallbackAsync(CancellationToken.None));
        }

        Assert.Equal("FallbackLocal", await h.StateOfAsync(fresh.JobId));
        Assert.Equal("master_off", await h.ScalarAsync<string>("""SELECT "FailureCode" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", fresh.JobId)));
        Assert.Equal("Queued", await h.StateOfAsync(shadow.JobId));
    }

    [PostgreSqlFact]
    public async Task Reaper_PurgesOldTerminalRows_AndClearsParkedResultsAfterTheirShorterWindow()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var old = await h.EnqueueAsync("media-old");
        var recent = await h.EnqueueAsync("media-recent");
        var parked = await h.EnqueueAsync("media-parked");
        await h.SqlAsync(
            """UPDATE "RemoteJobs" SET "State" = 'Succeeded', "UpdatedAt" = clock_timestamp() - interval '40 days' WHERE "Id" = @id;""",
            ("id", old.JobId));
        await h.SqlAsync(
            """UPDATE "RemoteJobs" SET "State" = 'Succeeded', "UpdatedAt" = clock_timestamp() - interval '1 day' WHERE "Id" = @id;""",
            ("id", recent.JobId));
        await h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "State" = 'Succeeded', "ResultJson" = '{"chunks":[]}', "ApplyOutcome" = 'Deferred',
                "CompletedAt" = clock_timestamp() - interval '9 days', "UpdatedAt" = clock_timestamp() - interval '9 days'
            WHERE "Id" = @id;
            """,
            ("id", parked.JobId));

        await using var db = h.NewContext();
        var (purged, cleared) = await h.Sweeper(db).PurgeAsync(h.Settings.Current, CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.Equal(1, cleared);
        Assert.Null(await h.StateOfAsync(old.JobId));
        Assert.Equal("Succeeded", await h.StateOfAsync(recent.JobId));
        Assert.Null(await h.ScalarAsync<string>("""SELECT "ResultJson" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", parked.JobId)));
    }

    [PostgreSqlFact]
    public async Task Reaper_DeletesOutputsOfAFenceThatNeverSettled_ButKeepsTheLiveLeasesOutputs()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var jobId = await h.InsertLeasedJobAsync(node, fence: 3);

        var liveKey = RemoteInputOutputService.OutputKey(jobId, 3, "audio.m4a");
        var deadKey = RemoteInputOutputService.OutputKey(jobId, 2, "audio.m4a");
        await h.Storage.WriteAsync(liveKey, new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);
        await h.Storage.WriteAsync(deadKey, new MemoryStream(new byte[] { 4, 5, 6 }), CancellationToken.None);
        foreach (var (fence, key) in new[] { (3L, liveKey), (2L, deadKey) })
        {
            await h.SqlAsync(
                """
                INSERT INTO "RemoteJobOutputs" ("JobId", "Fence", "Name", "Sha256", "SizeBytes", "StorageKey", "CreatedAt")
                VALUES (@job, @fence, 'audio.m4a', @sha, 3, @key, clock_timestamp());
                """,
                ("job", jobId), ("fence", fence), ("sha", RemoteTestData.Sha), ("key", key));
        }

        await h.SqlAsync("""UPDATE "RemoteJobs" SET "UpdatedAt" = clock_timestamp() - interval '2 hours' WHERE "Id" = @id;""", ("id", jobId));

        await using var db = h.NewContext();
        var orphans = await h.Sweeper(db).SweepOrphanOutputsAsync(CancellationToken.None);

        Assert.Equal(1, orphans);
        Assert.False(await h.Storage.ExistsAsync(deadKey, CancellationToken.None));
        Assert.True(await h.Storage.ExistsAsync(liveKey, CancellationToken.None));
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    // ── schema constraints (the database refuses what the code must never do) ──

    [PostgreSqlFact]
    public async Task Schema_RefusesAnInconsistentLeaseAndAnAttemptBeyondTheCap()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var queued = await h.EnqueueAsync();

        // Leased with no owner/expiry/deadline
        var noOwner = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """UPDATE "RemoteJobs" SET "State" = 'Leased' WHERE "Id" = @id;""", ("id", queued.JobId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, noOwner.SqlState);

        // an owner on a job that is not leased
        var strayOwner = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "LeaseOwner" = 'rw_x', "LeaseExpiresAt" = clock_timestamp(), "DeadlineAt" = clock_timestamp()
            WHERE "Id" = @id;
            """,
            ("id", queued.JobId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, strayOwner.SqlState);

        var tooMany = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """UPDATE "RemoteJobs" SET "Attempt" = "MaxAttempts" + 1 WHERE "Id" = @id;""", ("id", queued.JobId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, tooMany.SqlState);

        var negativeFence = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """UPDATE "RemoteJobs" SET "FenceToken" = -1 WHERE "Id" = @id;""", ("id", queued.JobId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, negativeFence.SqlState);
    }

    [PostgreSqlFact]
    public async Task Schema_RefusesAPlaintextLookingCredentialHash_AndADuplicateIdempotencyKey()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();

        var badHash = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """
            INSERT INTO "RemoteCredentials" ("TokenId", "Kind", "NodeId", "SecretHash", "CreatedAt", "ExpiresAt", "CreatedBy")
            VALUES ('0123456789abcdef', 'node', @node, 'a-plaintext-secret', now(), now() + interval '1 day', 'test');
            """,
            ("node", node)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badHash.SqlState);

        var queued = await h.EnqueueAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => h.SqlAsync(
            """
            INSERT INTO "RemoteJobs" ("Id", "Kind", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256", "EngineVersion", "SettingsHash",
                "ParamsJson", "InputsJson", "LimitsJson", "State", "NextAttemptAt", "EnqueuedBy", "CreatedAt", "UpdatedAt")
            SELECT 'rj_duplicate', "Kind", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256", "EngineVersion", "SettingsHash",
                "ParamsJson", "InputsJson", "LimitsJson", 'Queued', clock_timestamp(), 'test', clock_timestamp(), clock_timestamp()
            FROM "RemoteJobs" WHERE "Id" = @id;
            """,
            ("id", queued.JobId)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }
}
