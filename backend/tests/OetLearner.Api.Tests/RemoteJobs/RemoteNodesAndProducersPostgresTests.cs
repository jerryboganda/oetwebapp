using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// Nodes (registration, policy, status machine, tokens, canary, heartbeat), the producers' placement decisions, and the service
/// plane's job actions, on real PostgreSQL. RW-005, RW-006, RW-031, RW-056, RW-057, RW-131 to RW-134, RW-090 to RW-092.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteNodesAndProducersPostgresTests
{
    private const string Actor = "fleet:test";
    private const string ActorName = "fleet-manager";

    private static async Task<(string NodeId, RemoteIssuedToken Token)> RegisterAsync(
        RemotePgHarness h,
        string nodeRef = "helper-1",
        bool active = false)
    {
        await using var db = h.NewContext();
        var result = await h.Nodes(db).RegisterAsync(
            new RemoteRegisterRequestDto
            {
                NodeRef = nodeRef,
                DisplayName = "Helper " + nodeRef,
                Region = "uk",
                Provider = "test-cloud",
                Policy = new RemotePolicyUpdateDto { AllowedKinds = new List<string> { RemoteJobKinds.PdfExtract } },
            },
            Actor,
            ActorName,
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.True(result.Created);
        var nodeId = (string)result.Node!["id"]!;
        if (active)
        {
            await h.SqlAsync(
                """UPDATE "RemoteWorkers" SET "Status" = 'Active', "LastHeartbeatAt" = clock_timestamp() WHERE "Id" = @id;""",
                ("id", nodeId));
        }

        return (nodeId, result.Token!);
    }

    private static async Task<RemoteNodeResult> TransitionAsync(RemotePgHarness h, string nodeId, string action)
    {
        await using var db = h.NewContext();
        return await h.Nodes(db).TransitionAsync(nodeId, action, null, Actor, ActorName, CancellationToken.None);
    }

    private static RemoteNodeHeartbeatRequestDto Beat(RemotePgHarness h, string nodeId, Guid? instance = null, DateTimeOffset? startedAt = null)
        => new()
        {
            InstanceId = instance ?? h.InstanceOf(nodeId),
            AppliedRevision = 0,
            State = "ready",
            Agent = new RemoteAgentDto
            {
                Version = "1.0.0",
                ImageDigest = "sha256:" + new string('b', 64),
                Protocol = 1,
                ProtocolsSupported = new List<int> { 1 },
                StartedAt = startedAt ?? DateTimeOffset.UtcNow,
            },
            Kinds = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract) },
            Capacity = RemoteTestData.Capacity(),
            Leases = new List<RemoteLeaseReportDto>(),
        };

    private static async Task<RemoteNodeHeartbeatOutcome> HeartbeatAsync(RemotePgHarness h, string nodeId, RemoteNodeHeartbeatRequestDto request)
    {
        await using var db = h.NewContext();
        var node = await h.NodeAsync(nodeId);
        return await h.Nodes(db).HeartbeatAsync(node, request, CancellationToken.None);
    }

    // ── registration and tokens ──────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Register_IsIdempotentOnTheNodeRef_AndTheTokenIsReturnedExactlyOnce()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, token) = await RegisterAsync(h);

        await using var db = h.NewContext();
        var again = await h.Nodes(db).RegisterAsync(
            new RemoteRegisterRequestDto { NodeRef = "helper-1", DisplayName = "Other name" }, Actor, ActorName, CancellationToken.None);

        Assert.Null(again.Error);
        Assert.False(again.Created);
        Assert.Null(again.Token);
        Assert.Equal(nodeId, (string)again.Node!["id"]!);
        Assert.StartsWith("orw1_", token.Value, StringComparison.Ordinal);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteWorkers";"""));
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteCredentials";"""));
        Assert.Equal("Pending", (await h.NodeAsync(nodeId)).Status);

        var bad = await h.Nodes(db).RegisterAsync(
            new RemoteRegisterRequestDto { NodeRef = "Bad Ref!", DisplayName = "x" }, Actor, ActorName, CancellationToken.None);
        Assert.Equal(400, bad.Error!.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Register_StoresOnlyTheHash_AndNoAuditRowEverCarriesASecret()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, token) = await RegisterAsync(h);
        await using (var db = h.NewContext())
        {
            var (rotated, error) = await h.Nodes(db).RotateTokenAsync(nodeId, new RemoteRotateRequestDto { GraceSeconds = 3600 }, Actor, ActorName, CancellationToken.None);
            Assert.Null(error);
            Assert.NotNull(rotated);
        }

        await TransitionAsync(h, nodeId, "quarantine");
        await TransitionAsync(h, nodeId, "revoke");

        var secrets = new List<string>();
        foreach (var value in new[] { token.Value })
        {
            secrets.Add(value);
            secrets.Add(value.Split('_')[2]);
            secrets.Add(RemoteTokenFormat.HashSecretHex(value.Split('_')[2]));
        }

        var stored = await h.ScalarAsync<string>("""SELECT string_agg("SecretHash", ',') FROM "RemoteCredentials";""");
        Assert.DoesNotContain(token.Value.Split('_')[2], stored!, StringComparison.Ordinal);
        Assert.Contains(RemoteTokenFormat.HashSecretHex(token.Value.Split('_')[2]), stored!, StringComparison.Ordinal);

        var audit = await h.ScalarAsync<string>(
            """SELECT string_agg(COALESCE("Details", '') || ' ' || COALESCE("ResourceId", '') || ' ' || "ActorName", ' ') FROM "AuditEvents";""");
        Assert.False(string.IsNullOrEmpty(audit));
        foreach (var secret in secrets) Assert.DoesNotContain(secret, audit!, StringComparison.Ordinal);
        Assert.DoesNotContain("orw1_", audit!, StringComparison.Ordinal);
        Assert.True(await h.AuditCountAsync("RemoteWorker.Register") >= 1);
        Assert.True(await h.AuditCountAsync("RemoteWorker.TokenRotate") >= 1);
        Assert.True(await h.AuditCountAsync("RemoteWorker.Revoke") >= 1);
    }

    [PostgreSqlFact]
    public async Task RotateToken_KeepsTheOldTokenOnlyForTheGracePeriod_AndCapsActiveCredentialsAtThree()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h);

        async Task<(RemoteIssuedToken? Token, RemoteProblemResult? Error)> RotateAsync(int grace)
        {
            await using var db = h.NewContext();
            return await h.Nodes(db).RotateTokenAsync(nodeId, new RemoteRotateRequestDto { GraceSeconds = grace }, Actor, ActorName, CancellationToken.None);
        }

        var first = await RotateAsync(3600);
        Assert.Null(first.Error);
        Assert.Equal(2, await h.CountAsync(
            """SELECT COUNT(*)::int FROM "RemoteCredentials" WHERE "NodeId" = @n AND "RevokedAt" IS NULL AND "ExpiresAt" > clock_timestamp();""", ("n", nodeId)));
        // the credential that existed before the rotation now expires within the grace window, not in 30 days
        Assert.Equal(1, await h.CountAsync(
            """SELECT COUNT(*)::int FROM "RemoteCredentials" WHERE "NodeId" = @n AND "ExpiresAt" < clock_timestamp() + interval '2 hours';""", ("n", nodeId)));

        var second = await RotateAsync(3600);
        Assert.Null(second.Error);

        var third = await RotateAsync(3600);
        Assert.Equal(409, third.Error!.StatusCode);
        Assert.Equal("too_many_credentials", third.Error.Code);

        var bad = await RotateAsync(-1);
        Assert.Equal(400, bad.Error!.StatusCode);
    }

    [PostgreSqlFact]
    public async Task RotateToken_WithNoGrace_RevokesThePreviousCredentialAtOnce()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h);
        await using var db = h.NewContext();

        var (token, error) = await h.Nodes(db).RotateTokenAsync(nodeId, new RemoteRotateRequestDto { GraceSeconds = 0 }, Actor, ActorName, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(token);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteCredentials" WHERE "RevokedAt" IS NULL;"""));
    }

    [PostgreSqlFact]
    public async Task FleetCredential_IsIssuedOnce_Hashed_RotatedWithAGrace_AndCapped()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        RemoteIssuedToken? first = null;
        for (var i = 0; i < 3; i++)
        {
            await using var db = h.NewContext();
            var service = new RemoteFleetCredentialService(db, h.Settings, h.AuthCache, h.Time);
            var (token, error) = await service.IssueAsync(3600, null, "owner-1", "Owner", CancellationToken.None);
            Assert.Null(error);
            first ??= token;
        }

        Assert.StartsWith("ofs1_", first!.Value, StringComparison.Ordinal);
        Assert.Equal(3, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteCredentials" WHERE "Kind" = 'fleet' AND "NodeId" IS NULL;"""));

        await using var last = h.NewContext();
        var (_, capped) = await new RemoteFleetCredentialService(last, h.Settings, h.AuthCache, h.Time).IssueAsync(3600, null, "owner-1", "Owner", CancellationToken.None);
        Assert.Equal("too_many_credentials", capped!.Code);

        var audit = await h.ScalarAsync<string>("""SELECT string_agg(COALESCE("Details", ''), ' ') FROM "AuditEvents" WHERE "ResourceType" = 'RemoteFleetCredential';""");
        Assert.DoesNotContain(first.Value.Split('_')[2], audit!, StringComparison.Ordinal);
    }

    // ── policy (optimistic concurrency) ──────────────────────────────────────

    [PostgreSqlFact]
    public async Task Policy_IsUpdatedUnderAnExpectedRevision_AndABadValueIsRefused()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h);
        var revision = (await h.NodeAsync(nodeId)).PolicyRevision;

        async Task<(long? Revision, RemoteProblemResult? Error)> UpdateAsync(RemotePolicyUpdateDto policy)
        {
            await using var db = h.NewContext();
            return await h.Nodes(db).UpdatePolicyAsync(nodeId, policy, Actor, ActorName, CancellationToken.None);
        }

        var ok = await UpdateAsync(new RemotePolicyUpdateDto { ExpectedRevision = revision, MaxConcurrency = 4, Paused = true });
        Assert.Null(ok.Error);
        Assert.Equal(revision + 1, ok.Revision);
        var node = await h.NodeAsync(nodeId);
        Assert.Equal(4, node.MaxConcurrency);
        Assert.True(node.Paused);
        Assert.Equal(revision + 1, node.PolicyRevision);

        var stale = await UpdateAsync(new RemotePolicyUpdateDto { ExpectedRevision = revision, MaxConcurrency = 1 });
        Assert.Equal(409, stale.Error!.StatusCode);
        Assert.Equal("policy_revision_conflict", stale.Error.Code);
        Assert.Equal(4, (await h.NodeAsync(nodeId)).MaxConcurrency);

        var current = revision + 1;
        var tooBig = await UpdateAsync(new RemotePolicyUpdateDto { ExpectedRevision = current, MaxConcurrency = 99 });
        Assert.Equal(422, tooBig.Error!.StatusCode);
        Assert.Equal("policy_invalid", tooBig.Error.Code);

        var unknownKind = await UpdateAsync(new RemotePolicyUpdateDto { ExpectedRevision = current, AllowedKinds = new List<string> { "crypto.mine" } });
        Assert.Equal("policy_invalid", unknownKind.Error!.Code);

        var missingRevision = await UpdateAsync(new RemotePolicyUpdateDto { MaxConcurrency = 2 });
        Assert.Equal(400, missingRevision.Error!.StatusCode);

        Assert.Equal(current, (await h.NodeAsync(nodeId)).PolicyRevision);
    }

    // ── the status machine ───────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Transitions_FollowTheStateMachine_AndEveryChangeBumpsThePolicyRevision()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h, active: true);
        var revision = (await h.NodeAsync(nodeId)).PolicyRevision;

        var drain = await TransitionAsync(h, nodeId, "drain");
        Assert.Null(drain.Error);
        Assert.Equal("Draining", (await h.NodeAsync(nodeId)).Status);
        Assert.True((await h.NodeAsync(nodeId)).PolicyRevision > revision);

        Assert.Null((await TransitionAsync(h, nodeId, "disable")).Error);
        Assert.Equal("Disabled", (await h.NodeAsync(nodeId)).Status);

        Assert.Null((await TransitionAsync(h, nodeId, "enable")).Error);
        Assert.Equal("Active", (await h.NodeAsync(nodeId)).Status);

        Assert.Null((await TransitionAsync(h, nodeId, "quarantine")).Error);
        Assert.Equal("Quarantined", (await h.NodeAsync(nodeId)).Status);

        // leaving quarantine needs a fresh passing canary
        var enable = await TransitionAsync(h, nodeId, "enable");
        Assert.Equal(409, enable.Error!.StatusCode);
        Assert.Equal("canary_required", enable.Error.Code);

        var drainFromQuarantine = await TransitionAsync(h, nodeId, "drain");
        Assert.Equal("invalid_transition", drainFromQuarantine.Error!.Code);
        Assert.Equal("Quarantined", drainFromQuarantine.Error.Reason);

        Assert.Equal("node_not_found", (await TransitionAsync(h, RemoteIds.NewNodeId(DateTimeOffset.UtcNow), "drain")).Error!.Code);
    }

    [PostgreSqlFact]
    public async Task Revoke_RequeuesTheNodesLeasesWithARefund_RevokesItsCredentials_AndConverges()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h, active: true);
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(nodeId);
        var before = (await h.NodeAsync(nodeId)).PolicyRevision;

        var revoked = await TransitionAsync(h, nodeId, "revoke");

        Assert.Null(revoked.Error);
        var node = await h.NodeAsync(nodeId);
        Assert.Equal("Revoked", node.Status);
        Assert.True(node.PolicyRevision > before);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteCredentials" WHERE "NodeId" = @n AND "RevokedAt" IS NULL;""", ("n", nodeId)));

        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal(0, row.Attempt);          // the node was at fault, not the job
        Assert.Equal(1, row.ReleaseCount);
        Assert.Equal("node_revoked", row.FailureCode);
        Assert.Null(row.LeaseOwner);

        // a retrying manager gets the same answer, and a revoked node can never be brought back
        Assert.Null((await TransitionAsync(h, nodeId, "revoke")).Error);
        Assert.Equal("invalid_transition", (await TransitionAsync(h, nodeId, "enable")).Error!.Code);
        Assert.Equal(1, await h.AuditCountAsync("RemoteWorker.Revoke"));

        await using var db = h.NewContext();
        var (rotated, error) = await h.Nodes(db).RotateTokenAsync(nodeId, null, Actor, ActorName, CancellationToken.None);
        Assert.Null(rotated);
        Assert.Equal("invalid_transition", error!.Code);
    }

    [PostgreSqlFact]
    public async Task Canary_GatesActivation_AndOnlyOneCanaryMayBeOpenPerNode()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (nodeId, _) = await RegisterAsync(h);

        // a first heartbeat moves Pending to Probation (and bumps the revision)
        var first = await HeartbeatAsync(h, nodeId, Beat(h, nodeId));
        Assert.Null(first.Error);
        Assert.Equal("Probation", (await h.NodeAsync(nodeId)).Status);

        var needsCanary = await TransitionAsync(h, nodeId, "enable");
        Assert.Equal("canary_required", needsCanary.Error!.Code);

        await using var db = h.NewContext();
        var (jobId, error) = await h.Nodes(db).EnqueueCanaryAsync(nodeId, Actor, ActorName, CancellationToken.None);
        Assert.Null(error);
        Assert.NotNull(jobId);

        await using var db2 = h.NewContext();
        var (_, second) = await h.Nodes(db2).EnqueueCanaryAsync(nodeId, Actor, ActorName, CancellationToken.None);
        Assert.Equal("canary_in_progress", second!.Code);

        // the canary targets only this node and never appears as ordinary work for another
        var stranger = await h.AddNodeAsync();
        var outcome = await h.ClaimAsync(stranger);
        Assert.Null(outcome.Leased);
    }

    // ── node heartbeat ───────────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Heartbeat_TheNewerInstanceWins_TheOlderIsToldItWasSuperseded()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var current = Guid.NewGuid();
        var node = await h.AddNodeAsync(currentInstance: current.ToString("D"));
        await h.SqlAsync(
            """UPDATE "RemoteWorkers" SET "CurrentInstanceStartedAt" = clock_timestamp() - interval '1 minute' WHERE "Id" = @id;""",
            ("id", node));

        var older = await HeartbeatAsync(h, node, Beat(h, node, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal(409, older.Error!.StatusCode);
        Assert.Equal("instance_superseded", older.Error.Code);
        Assert.Equal(current.ToString("D"), (await h.NodeAsync(node)).CurrentInstanceId);

        var newerInstance = Guid.NewGuid();
        var newer = await HeartbeatAsync(h, node, Beat(h, node, newerInstance, DateTimeOffset.UtcNow));
        Assert.Null(newer.Error);
        Assert.Equal(newerInstance.ToString("D"), (await h.NodeAsync(node)).CurrentInstanceId);

        // the same instance simply keeps heartbeating
        Assert.Null((await HeartbeatAsync(h, node, Beat(h, node, newerInstance, DateTimeOffset.UtcNow))).Error);
    }

    [PostgreSqlFact]
    public async Task Heartbeat_ARevokedNodeIsTurnedAwayWith401_NotGivenInstructions()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(RemoteNodeStatus.Revoked);

        var outcome = await HeartbeatAsync(h, node, Beat(h, node));

        Assert.Equal(401, outcome.Error!.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Heartbeat_AuditsReportedLeases_AndReleasesALeaseMissingFromTwoConsecutiveHeartbeats()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync();
        var job = await h.ClaimOneAsync(node);
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "LeasedAt" = clock_timestamp() - interval '30 seconds' WHERE "Id" = @id;""", ("id", job.Id));

        // reported with the right fence: valid
        var reporting = Beat(h, node);
        reporting.Leases = new List<RemoteLeaseReportDto> { new() { JobId = job.Id, Fence = job.FenceToken, Stage = "parsing", ElapsedMs = 100 } };
        var reported = await HeartbeatAsync(h, node, reporting);
        Assert.Null(reported.Error);
        var audit = Assert.IsType<List<Dictionary<string, object?>>>(reported.Body!["leaseAudit"]);
        Assert.Equal(true, Assert.Single(audit)["valid"]);

        // reported with a wrong fence: invalid
        var wrong = Beat(h, node);
        wrong.Leases = new List<RemoteLeaseReportDto> { new() { JobId = job.Id, Fence = 99, Stage = "parsing" } };
        var wrongAudit = Assert.IsType<List<Dictionary<string, object?>>>((await HeartbeatAsync(h, node, wrong)).Body!["leaseAudit"]);
        Assert.Equal(false, Assert.Single(wrongAudit)["valid"]);
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));

        // absent once: not yet released; absent twice in a row: the agent lost it (crash recovery in ~30 s, with a refund)
        await HeartbeatAsync(h, node, Beat(h, node));
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));
        await HeartbeatAsync(h, node, Beat(h, node));

        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal(0, row.Attempt);
        Assert.Equal(1, row.ReleaseCount);
        Assert.Equal("orphan_reconciled", row.FailureCode);
    }

    [PostgreSqlFact]
    public async Task Heartbeat_AnswersWithTheDesiredState_AndTheFeatureFlags()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();

        var outcome = await HeartbeatAsync(h, node, Beat(h, node));

        Assert.Null(outcome.Error);
        var body = outcome.Body!;
        var desired = Assert.IsType<Dictionary<string, object?>>(body["desired"]);
        Assert.Equal(false, desired["drain"]);
        var features = Assert.IsType<Dictionary<string, object?>>(body["features"]);
        Assert.Equal(true, features["enabled"]);
        var kinds = Assert.IsType<Dictionary<string, object?>>(features["kinds"]);
        Assert.Equal(true, kinds[RemoteJobKinds.PdfExtract]);
        Assert.Equal(false, kinds[RemoteJobKinds.CompanionIndexPrep]);
        Assert.Equal(outcome.DesiredRevision, (await h.NodeAsync(node)).PolicyRevision);
    }

    // ── producers: placement and the local fallback ──────────────────────────

    private static RemotePdfExtractionProducer Producer(
        RemotePgHarness h,
        LearnerDbContext db,
        FixedHeadroom headroom,
        RemoteLocalWaitTracker? waits = null,
        TestRuntimeSettingsProvider? runtimeSettings = null)
    {
        var placement = new RemotePlacement(db, h.Settings, headroom, h.Time);
        return new RemotePdfExtractionProducer(
            db,
            h.Flags,
            h.Queue(db),
            placement,
            runtimeSettings ?? new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            h.Storage,
            h.Settings,
            headroom,
            waits ?? new RemoteLocalWaitTracker(h.Time),
            h.Time,
            NullLogger<RemotePdfExtractionProducer>.Instance);
    }

    private static async Task<RemoteExtractionDecision> DecideAsync(
        RemotePgHarness h,
        FixedHeadroom headroom,
        RemoteLocalWaitTracker? waits = null,
        TestRuntimeSettingsProvider? runtimeSettings = null)
    {
        await using var db = h.NewContext();
        return await Producer(h, db, headroom, waits, runtimeSettings).HandlePaperAsync("paper-1", CancellationToken.None);
    }

    [PostgreSqlFact]
    public async Task Producer_WithEveryFlagOff_ChangesNothing_AndTheLocalPathRuns()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        await h.AddNodeAsync();
        h.Flags.Set();

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true });

        Assert.Equal(RemoteExtractionDecision.Local, decision);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Producer_EnqueuesOneIdempotentJobPerAsset_WhenAHealthyNodeCanTakeIt()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        await h.AddNodeAsync();
        var headroom = new FixedHeadroom { Value = true };

        var first = await DecideAsync(h, headroom);
        var second = await DecideAsync(h, headroom);

        Assert.Equal(RemoteExtractionDecision.Remote, first);
        Assert.Equal(RemoteExtractionDecision.Remote, second);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
        var job = await h.JobAsync(await h.ScalarAsync<string>("""SELECT "Id" FROM "RemoteJobs";""") ?? string.Empty);
        Assert.Equal(RemoteJobKinds.PdfExtract, job.Kind);
        Assert.Equal(RemoteJobPurpose.Apply, job.Purpose);
        Assert.Equal("media-1", job.ResourceId);
        Assert.Equal(RemoteTestData.Sha, job.InputSha256);
        Assert.Equal(RemoteTestData.Engine(), job.EngineVersion);
        Assert.NotNull(job.FallbackAfter);
        // the worker's oldest-first window keeps rotating: a paper that remote jobs own is bumped like a locally processed one
        Assert.True(await h.ScalarAsync<bool>("""SELECT "UpdatedAt" > now() - interval '1 hour' FROM "ContentPapers" WHERE "Id" = 'paper-1';"""));
    }

    [PostgreSqlFact]
    public async Task Producer_WithNoNodeAvailable_RunsLocallyWhileThePrimaryHasHeadroom_AndOtherwiseWaits()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();

        var withHeadroom = await DecideAsync(h, new FixedHeadroom { Value = true });
        var without = await DecideAsync(h, new FixedHeadroom { Value = false });

        Assert.Equal(RemoteExtractionDecision.Local, withHeadroom);
        Assert.Equal(RemoteExtractionDecision.Skip, without);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Producer_NeverSendsWorkToANodeThatIsNotActiveFreshAndAllowed()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        await h.AddNodeAsync(RemoteNodeStatus.Draining);
        await h.AddNodeAsync(RemoteNodeStatus.Probation);
        await h.AddNodeAsync(freshHeartbeat: false);
        await h.AddNodeAsync(paused: true);
        await h.AddNodeAsync(allowedKinds: new[] { RemoteJobKinds.CompanionIndexPrep });

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true });

        Assert.Equal(RemoteExtractionDecision.Local, decision);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Producer_OnlyThePdfPigTierIsRemote_AzureStaysLocal()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        await h.AddNodeAsync();
        var azure = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            PdfExtraction = TestRuntimeSettingsProvider.DefaultPdfExtraction() with { Provider = "azure" },
        });

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true }, runtimeSettings: azure);

        Assert.Equal(RemoteExtractionDecision.Local, decision);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Producer_AFallbackLocalJobRunsLocallyOnlyWithHeadroomOrAfterTheHardDeadline()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        var job = await h.EnqueueAsync();
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "State" = 'FallbackLocal', "FailureCode" = 'queue_timeout' WHERE "Id" = @id;""", ("id", job.JobId));

        Assert.Equal(RemoteExtractionDecision.Local, await DecideAsync(h, new FixedHeadroom { Value = true }));
        Assert.Equal(RemoteExtractionDecision.Skip, await DecideAsync(h, new FixedHeadroom { Value = false }));

        await h.SqlAsync("""UPDATE "RemoteJobs" SET "CreatedAt" = clock_timestamp() - interval '2 hours' WHERE "Id" = @id;""", ("id", job.JobId));
        Assert.Equal(RemoteExtractionDecision.Local, await DecideAsync(h, new FixedHeadroom { Value = false }));
    }

    [PostgreSqlFact]
    public async Task Producer_AQuarantinedJobIsNeverHandedToTheInProcessPath()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        var job = await h.EnqueueAsync();
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "State" = 'Quarantined', "Attempt" = "MaxAttempts" WHERE "Id" = @id;""", ("id", job.JobId));

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true });

        Assert.Equal(RemoteExtractionDecision.Skip, decision);
    }

    [PostgreSqlFact]
    public async Task Producer_ADeterministicRejectionHandsTheAssetToTheLocalPath()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync();
        var job = await h.EnqueueAsync();
        await h.SqlAsync("""UPDATE "RemoteJobs" SET "State" = 'Failed', "FailureCode" = 'content_rejected' WHERE "Id" = @id;""", ("id", job.JobId));

        Assert.Equal(RemoteExtractionDecision.Local, await DecideAsync(h, new FixedHeadroom { Value = false }));
    }

    [PostgreSqlFact]
    public async Task Producer_BackfillsAMissingAssetHash_BeforeEnqueueing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var bytes = new byte[300];
        new Random(3).NextBytes(bytes);
        await h.Storage.WriteAsync("media/sample.pdf", new MemoryStream(bytes), CancellationToken.None);
        await h.SeedPdfAsync(size: bytes.Length);
        await h.SqlAsync("""UPDATE "MediaAssets" SET "Sha256" = NULL WHERE "Id" = 'media-1';""");
        await h.AddNodeAsync();

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true });

        Assert.Equal(RemoteExtractionDecision.Remote, decision);
        var expected = RemoteIds.Sha256Hex(bytes);
        Assert.Equal(expected, await h.ScalarAsync<string>("""SELECT "Sha256" FROM "MediaAssets" WHERE "Id" = 'media-1';"""));
        Assert.Equal(expected, await h.ScalarAsync<string>("""SELECT "InputSha256" FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Producer_ShadowModeComparesAssetsThatAreAlreadyCached_WithoutApplyingAnything()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await h.SeedPdfAsync(extractedJson: "{\"asset-1\":\"already extracted text for this asset\"}");
        await h.AddNodeAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtractShadow); // the apply flag stays off

        var decision = await DecideAsync(h, new FixedHeadroom { Value = true });

        Assert.Equal(RemoteExtractionDecision.Local, decision);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "Purpose" = 'shadow' AND "FallbackAfter" IS NULL;"""));
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "Purpose" = 'apply';"""));
        Assert.Equal("{\"asset-1\":\"already extracted text for this asset\"}", await h.PaperJsonAsync());
    }

    [PostgreSqlFact]
    public async Task Placement_DecidesRemoteLocalOrWait()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await using var db = h.NewContext();
        var headroom = new FixedHeadroom();
        var placement = new RemotePlacement(db, h.Settings, headroom, h.Time);

        headroom.Value = false;
        Assert.Equal(RemotePlacementDecision.Wait, await placement.DecideAsync(RemoteJobKinds.PdfExtract, 1, CancellationToken.None));
        headroom.Value = true;
        Assert.Equal(RemotePlacementDecision.Local, await placement.DecideAsync(RemoteJobKinds.PdfExtract, 1, CancellationToken.None));

        var node = await h.AddNodeAsync(maxConcurrency: 1);
        Assert.Equal(RemotePlacementDecision.Remote, await placement.DecideAsync(RemoteJobKinds.PdfExtract, 1, CancellationToken.None));

        // a node whose only slot is leased has no free weight for another job
        await h.InsertLeasedJobAsync(node, RemoteJobKinds.PdfExtract);
        Assert.Equal(RemotePlacementDecision.Local, await placement.DecideAsync(RemoteJobKinds.PdfExtract, 1, CancellationToken.None));
    }

    // ── service plane: job actions ───────────────────────────────────────────

    [PostgreSqlFact]
    public async Task FleetJobs_FollowTheJobStateMachine_AndEveryActionIsAudited()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var job = await h.EnqueueAsync();

        async Task<(string? State, RemoteProblemResult? Error)> Act(string action, string id)
        {
            await using var db = h.NewContext();
            var service = h.FleetJobs(db);
            return action switch
            {
                "cancel" => await service.CancelAsync(id, Actor, ActorName, CancellationToken.None),
                "requeue" => await service.RequeueAsync(id, Actor, ActorName, CancellationToken.None),
                _ => await service.ForceLocalAsync(id, Actor, ActorName, CancellationToken.None),
            };
        }

        Assert.Equal("Cancelled", (await Act("cancel", job.JobId)).State);
        Assert.Equal("Queued", (await Act("requeue", job.JobId)).State);
        Assert.Equal("FallbackLocal", (await Act("force-local", job.JobId)).State);

        var illegal = await Act("cancel", job.JobId); // only a Queued job can be cancelled
        Assert.Equal(409, illegal.Error!.StatusCode);
        Assert.Equal("invalid_transition", illegal.Error.Code);
        Assert.Equal("FallbackLocal", illegal.Error.Reason);

        var missing = await Act("cancel", RemoteIds.NewJobId(DateTimeOffset.UtcNow));
        Assert.Equal("job_not_found", missing.Error!.Code);

        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Cancel"));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Requeue"));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.ForceLocal"));
    }

    [PostgreSqlFact]
    public async Task FleetJobs_RequeueResetsAttemptsButNeverTheFence_AndAListCarriesNoContent()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var job = await h.EnqueueAsync();
        var leased = await h.ClaimOneAsync(node);
        await FailTerminalAsync(h, node, leased);

        await using var db = h.NewContext();
        var service = h.FleetJobs(db);
        var (state, error) = await service.RequeueAsync(job.JobId, Actor, ActorName, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("Queued", state);
        var row = await h.JobAsync(job.JobId);
        Assert.Equal(0, row.Attempt);
        Assert.Equal(1, row.FenceToken);
        Assert.Null(row.FailureCode);

        var listed = await service.ListAsync(null, null, null, 50, CancellationToken.None);
        var json = JsonSerializer.Serialize(listed);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("media/sample.pdf", json, StringComparison.Ordinal);
        Assert.Contains(job.JobId, json, StringComparison.Ordinal);
    }

    private static async Task FailTerminalAsync(RemotePgHarness h, string node, RemoteJobRow leased)
    {
        await using var db = h.NewContext();
        var (status, _, error) = await h.Lifecycle(db).FailAsync(
            leased.Id, node, new RemoteFailRequestDto { Fence = leased.FenceToken, Code = "not_pdf" }, CancellationToken.None);
        Assert.Null(error);
        Assert.Equal("failed", status);
    }
}
