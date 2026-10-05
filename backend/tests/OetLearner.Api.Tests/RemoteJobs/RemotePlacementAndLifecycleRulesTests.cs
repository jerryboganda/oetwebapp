using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// Pure rules of the state machine: claim eligibility (RW-025, RW-026, RW-115, RW-116), fair share (RW-036),
/// lost-lease classification (RW-043, RW-045), complete replay (RW-072, RW-073), failure codes (RW-048, RW-049, RW-082).
/// </summary>
public sealed class RemotePlacementAndLifecycleRulesTests
{
    private static readonly RemoteJobsOptions Options = new();
    private static readonly IReadOnlyDictionary<string, int> NoLeases = new Dictionary<string, int>();

    private static RemoteNodePolicyView Node(
        string status = RemoteNodeStatus.Active,
        string[]? kinds = null,
        int max = 2,
        Dictionary<string, int>? perKind = null,
        int cpu = 3000,
        int mem = 5120,
        int tmp = 3072,
        bool paused = false)
        => new(
            status,
            paused,
            kinds ?? new[] { RemoteJobKinds.PdfExtract, RemoteJobKinds.CompanionIndexPrep },
            max,
            perKind ?? new Dictionary<string, int>(),
            cpu,
            mem,
            tmp);

    private static RemoteFlagSnapshot Flags(params string[] keys) => new(new HashSet<string>(keys, StringComparer.Ordinal));

    private static readonly string[] AllOn =
    [
        RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract, RemoteJobFlagKeys.PdfExtractShadow, RemoteJobFlagKeys.CompanionIndexPrep,
    ];

    private static ClaimPlan Plan(
        RemoteNodePolicyView node,
        IReadOnlyList<RemoteKindOfferDto>? offers = null,
        RemoteCapacityDto? capacity = null,
        RemoteFlagSnapshot? flags = null,
        int leasedTotal = 0,
        IReadOnlyDictionary<string, int>? leasedByKind = null)
        => RemoteClaimPlanner.Plan(
            node,
            offers ?? new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract) },
            capacity ?? RemoteTestData.Capacity(),
            flags ?? Flags(AllOn),
            Options,
            leasedTotal,
            leasedByKind ?? NoLeases);

    // ── claim eligibility ────────────────────────────────────────────────────

    [Fact]
    public void ActiveNode_WithEverythingOn_IsOfferedApplyShadowAndCanary()
    {
        var plan = Plan(Node());

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(RemoteJobKinds.PdfExtract, entry.Kind);
        Assert.Equal(1, entry.SchemaVersion);
        Assert.Equal(new[] { "apply", "shadow", "canary" }, entry.Purposes.ToArray());
        Assert.Equal(2, entry.MaxWeight);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void MasterOff_OffersNothing()
    {
        var plan = Plan(Node(), flags: Flags(RemoteJobFlagKeys.PdfExtract));

        Assert.True(plan.IsEmpty);
        Assert.Equal("kind_disabled", plan.EmptyReason);
    }

    [Fact]
    public void KindFlagOff_OffersOnlyTheCanary()
    {
        var plan = Plan(Node(), flags: Flags(RemoteJobFlagKeys.Master));

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(new[] { "canary" }, entry.Purposes.ToArray());
    }

    [Fact]
    public void KindsAreEnforcedFromTheNodePolicy_NotFromWhatTheAgentOffers()
    {
        // RW-026 / RW-115: the agent offers companion.index-prep, the policy does not allow it.
        var node = Node(kinds: new[] { RemoteJobKinds.PdfExtract });
        var offers = new List<RemoteKindOfferDto>
        {
            RemoteTestData.Offer(RemoteJobKinds.CompanionIndexPrep),
        };

        var plan = Plan(node, offers);

        Assert.True(plan.IsEmpty);
        Assert.Equal("kind_disabled", plan.EmptyReason);
    }

    [Fact]
    public void AKindOutsideTheRegistry_IsIgnored()
    {
        var offers = new List<RemoteKindOfferDto>
        {
            new() { Kind = "crypto.mine", SchemaVersions = new List<int> { 1 }, EngineVersion = "x:1" },
        };

        Assert.True(Plan(Node(kinds: new[] { "crypto.mine" }), offers).IsEmpty);
    }

    [Fact]
    public void AnEngineOrSchemaMismatch_IsNeverOffered()
    {
        var wrongEngine = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract, "pdfpig:0.0.1/oet-text:99") };
        Assert.True(Plan(Node(), wrongEngine).IsEmpty);

        var wrongSchema = new List<RemoteKindOfferDto> { RemoteTestData.Offer(RemoteJobKinds.PdfExtract, null, 2) };
        Assert.True(Plan(Node(), wrongSchema).IsEmpty);
    }

    [Fact]
    public void ProbationAndQuarantine_AreOnlyEverOfferedTheCanary()
    {
        foreach (var status in new[] { RemoteNodeStatus.Probation, RemoteNodeStatus.Quarantined })
        {
            var entry = Assert.Single(Plan(Node(status)).Entries);
            Assert.Equal(new[] { "canary" }, entry.Purposes.ToArray());
        }
    }

    [Theory]
    [InlineData(RemoteNodeStatus.Pending)]
    [InlineData(RemoteNodeStatus.Draining)]
    [InlineData(RemoteNodeStatus.Disabled)]
    [InlineData(RemoteNodeStatus.Revoked)]
    public void OtherStatuses_AreOfferedNothing(string status)
    {
        var plan = Plan(Node(status));

        Assert.True(plan.IsEmpty);
        Assert.Equal("kind_disabled", plan.EmptyReason);
    }

    [Fact]
    public void ConcurrencyIsWeighted_AFullNodeGetsNoCapacity()
    {
        var plan = Plan(Node(max: 2), leasedTotal: 2, leasedByKind: new Dictionary<string, int> { [RemoteJobKinds.PdfExtract] = 2 });

        Assert.True(plan.IsEmpty);
        Assert.Equal("no_capacity", plan.EmptyReason);
    }

    [Fact]
    public void APerKindCap_LimitsThatKindEvenWhenTheNodeHasRoom()
    {
        var node = Node(max: 4, perKind: new Dictionary<string, int> { [RemoteJobKinds.PdfExtract] = 1 });
        var capacity = RemoteTestData.Capacity(heavySlots: 4, effective: 4);

        var full = Plan(node, capacity: capacity, leasedTotal: 1, leasedByKind: new Dictionary<string, int> { [RemoteJobKinds.PdfExtract] = 1 });
        Assert.True(full.IsEmpty);
        Assert.Equal("no_capacity", full.EmptyReason);

        var free = Plan(node, capacity: capacity);
        Assert.Equal(1, Assert.Single(free.Entries).MaxWeight);
    }

    [Fact]
    public void TheAgentCanOnlyNarrowTheOffer_NeverWidenIt()
    {
        // The agent reports no free heavy slot: nothing is offered even though the policy has room.
        Assert.True(Plan(Node(), capacity: RemoteTestData.Capacity(heavySlots: 0)).IsEmpty);

        // The agent claims far more memory than the policy budget: the policy wins (RW-116).
        var tight = Node(mem: 1000);
        var plan = Plan(tight, capacity: RemoteTestData.Capacity(mem: 100000));
        Assert.True(plan.IsEmpty);
        Assert.Equal("no_capacity", plan.EmptyReason);
    }

    [Fact]
    public void DuplicateOffersOfOneKind_AreCollapsed()
    {
        var offers = new List<RemoteKindOfferDto>
        {
            RemoteTestData.Offer(RemoteJobKinds.PdfExtract),
            RemoteTestData.Offer(RemoteJobKinds.PdfExtract),
        };

        Assert.Single(Plan(Node(), offers).Entries);
    }

    // ── fair share ───────────────────────────────────────────────────────────

    [Fact]
    public void FairShare_Norm_IsLeasedWeightOverEffectiveConcurrency()
    {
        Assert.Equal(0.5, RemoteFairShare.Norm(1, 4, 2));
        Assert.Equal(1.0, RemoteFairShare.Norm(2, 2, 8));
        Assert.Equal(3.0, RemoteFairShare.Norm(3, 0, 0));
    }

    [Fact]
    public void FairShare_DeclinesWhenAnEmptierPeerCanCoverTheDueWork()
    {
        var peers = new List<FairSharePeer> { new("rw_peer", FreeSlots: 2, Norm: 0.0) };

        Assert.True(RemoteFairShare.ShouldDecline(selfNorm: 1.0, peers, dueEligibleQueuedJobs: 2, declinedInARow: 0));
        // More due work than the peers have free slots: this node must not decline.
        Assert.False(RemoteFairShare.ShouldDecline(1.0, peers, dueEligibleQueuedJobs: 3, declinedInARow: 0));
    }

    [Fact]
    public void FairShare_NeverStarvesANodeDeclinedTwiceInARow()
    {
        var peers = new List<FairSharePeer> { new("rw_peer", 5, 0.0) };
        Assert.False(RemoteFairShare.ShouldDecline(1.0, peers, 1, declinedInARow: 2));
    }

    [Fact]
    public void FairShare_IgnoresPeersThatAreNotClearlyEmptier_OrHaveNoFreeSlot()
    {
        var closeBy = new List<FairSharePeer> { new("rw_a", 2, 0.8) };
        Assert.False(RemoteFairShare.ShouldDecline(1.0, closeBy, 1, 0));

        var full = new List<FairSharePeer> { new("rw_b", 0, 0.0) };
        Assert.False(RemoteFairShare.ShouldDecline(1.0, full, 1, 0));

        Assert.False(RemoteFairShare.ShouldDecline(1.0, new List<FairSharePeer>(), 1, 0));
    }

    // ── lost lease classification ────────────────────────────────────────────

    private static RemoteJobSnapshot Snapshot(RemoteJobRow row, bool pastDeadline = false, bool pastExpiry = false)
        => new(row, pastDeadline, pastExpiry);

    [Fact]
    public void LostReason_NamesTheCauseFromTheRowAndTheDatabaseClock()
    {
        const string node = "rw_00000000000000000000000001";

        Assert.Equal("cancelled", RemoteLeaseClassifier.LostReason(Snapshot(RemoteTestData.Row(RemoteJobState.Cancelled)), node, 1));
        Assert.Equal("superseded", RemoteLeaseClassifier.LostReason(Snapshot(RemoteTestData.Row(RemoteJobState.Queued)), node, 1));
        Assert.Equal("terminal", RemoteLeaseClassifier.LostReason(Snapshot(RemoteTestData.Row(RemoteJobState.Succeeded)), node, 1));
        Assert.Equal("terminal", RemoteLeaseClassifier.LostReason(Snapshot(RemoteTestData.Row(RemoteJobState.Quarantined)), node, 1));

        var mine = RemoteTestData.Row(RemoteJobState.Leased, leaseOwner: node, fence: 4);
        Assert.Equal("expired", RemoteLeaseClassifier.LostReason(Snapshot(mine, pastExpiry: true), node, 4));
        Assert.Equal("deadline", RemoteLeaseClassifier.LostReason(Snapshot(mine, pastDeadline: true, pastExpiry: true), node, 4));

        // Another node holds a NEWER fence: this holder has been superseded.
        var reclaimed = RemoteTestData.Row(RemoteJobState.Leased, leaseOwner: "rw_00000000000000000000000002", fence: 5);
        Assert.Equal("superseded", RemoteLeaseClassifier.LostReason(Snapshot(reclaimed), node, 4));

        // The SAME fence number held by someone else is a guess or a bug, never a legitimate holder.
        var guess = RemoteTestData.Row(RemoteJobState.Leased, leaseOwner: "rw_00000000000000000000000002", fence: 4);
        Assert.Equal("not_owner", RemoteLeaseClassifier.LostReason(Snapshot(guess), node, 4));
    }

    // ── complete: replay and conflict ────────────────────────────────────────

    private const string Node1 = "rw_00000000000000000000000001";

    [Fact]
    public void Complete_SameFenceNodeAndHash_ReplaysTheStoredOutcome()
    {
        var sha = RemoteIds.Sha256Hex("result");
        var row = RemoteTestData.Row(
            RemoteJobState.Succeeded, fence: 3, settledFence: 3, settledBy: Node1, resultSha: sha, applyOutcome: "Applied");

        var outcome = RemoteCompletionService.Classify(Snapshot(row), Node1, 3, sha, hashMatches: true);

        Assert.Null(outcome.Error);
        Assert.Equal("succeeded", outcome.Status);
        Assert.Equal("Applied", outcome.Outcome);
        Assert.True(outcome.Replayed);
    }

    [Fact]
    public void Complete_SameFenceButADifferentHash_IsAResultConflict()
    {
        var row = RemoteTestData.Row(
            RemoteJobState.Succeeded, fence: 3, settledFence: 3, settledBy: Node1, resultSha: RemoteIds.Sha256Hex("first"), applyOutcome: "Applied");

        var outcome = RemoteCompletionService.Classify(Snapshot(row), Node1, 3, RemoteIds.Sha256Hex("second"), hashMatches: true);

        Assert.NotNull(outcome.Error);
        Assert.Equal(409, outcome.Error!.StatusCode);
        Assert.Equal("result_conflict", outcome.Error.Code);
    }

    [Fact]
    public void Complete_AZombieOfAnEarlierFence_IsToldItsLeaseIsLost_AndNothingElse()
    {
        // The job was reclaimed and is leased to someone else at fence 6; the zombie still holds fence 5.
        var row = RemoteTestData.Row(RemoteJobState.Leased, leaseOwner: "rw_00000000000000000000000002", fence: 6);

        var outcome = RemoteCompletionService.Classify(Snapshot(row), Node1, 5, RemoteIds.Sha256Hex("late"), hashMatches: true);

        Assert.Equal(409, outcome.Error!.StatusCode);
        Assert.Equal("lease_lost", outcome.Error.Code);
        Assert.Equal("superseded", outcome.Error.Reason);
        Assert.Null(outcome.Status);
    }

    [Fact]
    public void Complete_AnExpiredButUnreapedLease_IsLostToo()
    {
        var row = RemoteTestData.Row(RemoteJobState.Leased, leaseOwner: Node1, fence: 2);

        var outcome = RemoteCompletionService.Classify(Snapshot(row, pastExpiry: true), Node1, 2, RemoteIds.Sha256Hex("x"), hashMatches: true);

        Assert.Equal("lease_lost", outcome.Error!.Code);
        Assert.Equal("expired", outcome.Error.Reason);
    }

    [Fact]
    public void Complete_AContentRejectionReplaysAsTheSameRejection()
    {
        var row = RemoteTestData.Row(
            RemoteJobState.Failed, fence: 1, settledFence: 1, settledBy: Node1, settledCode: RemoteFailCodes.ContentRejected);

        var outcome = RemoteCompletionService.Classify(Snapshot(row), Node1, 1, RemoteIds.Sha256Hex("x"), hashMatches: true);

        Assert.Null(outcome.Error);
        Assert.Equal("rejected", outcome.Status);
        Assert.True(outcome.Replayed);
        Assert.Equal(RemoteFailCodes.ContentRejected, outcome.Code);
    }

    [Fact]
    public void Complete_ADiscardedResultReplaysAsTheSameDiscard()
    {
        var sha = RemoteIds.Sha256Hex("discarded");
        var row = RemoteTestData.Row(
            RemoteJobState.Cancelled, fence: 1, settledFence: 1, settledBy: Node1, resultSha: sha, applyOutcome: "Discarded", failureCode: "stale_input");

        var outcome = RemoteCompletionService.Classify(Snapshot(row), Node1, 1, sha, hashMatches: true);

        Assert.Equal(409, outcome.Error!.StatusCode);
        Assert.Equal("stale_input", outcome.Error.Code);
    }

    // ── failure codes ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("timeout", true, false)]
    [InlineData("oom", true, false)]
    [InlineData("internal_error", true, false)]
    [InlineData("input_hash_mismatch", true, false)]
    [InlineData("not_pdf", false, false)]
    [InlineData("extract_exception", false, false)]
    [InlineData("shutdown", true, true)]
    [InlineData("drain", true, true)]
    [InlineData("pressure_shed", true, true)]
    [InlineData("protocol_mismatch", true, true)]
    public void FailCodes_RetryabilityAndRefundComeFromTheServerTable(string code, bool retryable, bool refund)
    {
        Assert.True(RemoteFailCodes.IsKnown(code));
        Assert.Equal(retryable, RemoteFailCodes.IsRetryable(code));
        Assert.Equal(refund, RemoteFailCodes.IsRefund(code));
    }

    [Fact]
    public void FailCodes_AnUnknownCodeIsNotKnown_AndNeverRetriesOrRefunds()
    {
        Assert.False(RemoteFailCodes.IsKnown("because"));
        Assert.False(RemoteFailCodes.IsKnown(null));
        Assert.False(RemoteFailCodes.IsRetryable("because"));
        Assert.False(RemoteFailCodes.IsRefund("because"));
    }

    [Fact]
    public void FailCodes_OnlyDeterministicConditionsMayFallBackToTheLocalPath()
    {
        Assert.True(RemoteFailCodes.AllowsLocalFallback("not_pdf"));
        Assert.True(RemoteFailCodes.AllowsLocalFallback(RemoteFailCodes.ContentRejected));
        Assert.False(RemoteFailCodes.AllowsLocalFallback("timeout"));
        Assert.False(RemoteFailCodes.AllowsLocalFallback("oom"));
        Assert.False(RemoteFailCodes.AllowsLocalFallback(null));
    }

    [Fact]
    public void StatusOf_MapsTheStoredStateToTheWireStatus()
    {
        Assert.Equal("failed", RemoteJobLifecycleService.StatusOf(RemoteJobState.Failed));
        Assert.Equal("quarantined", RemoteJobLifecycleService.StatusOf(RemoteJobState.Quarantined));
        Assert.Equal("queued", RemoteJobLifecycleService.StatusOf(RemoteJobState.Queued));
    }

    [Fact]
    public void BuildMetricsJson_DropsHostileKeysAndNonFiniteNumbers()
    {
        Assert.Null(RemoteJobLifecycleService.BuildMetricsJson(null, null));
        Assert.Null(RemoteJobLifecycleService.BuildMetricsJson(null, new Dictionary<string, double>()));

        var json = RemoteJobLifecycleService.BuildMetricsJson("parsing", new Dictionary<string, double>
        {
            ["pages"] = 12,
            [new string('k', 49)] = 1,
            ["nan"] = double.NaN,
            ["inf"] = double.PositiveInfinity,
        });

        Assert.NotNull(json);
        Assert.Contains("\"stage\":\"parsing\"", json, StringComparison.Ordinal);
        Assert.Contains("\"pages\":12", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nan", json, StringComparison.Ordinal);
        Assert.DoesNotContain("inf", json, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('k', 49), json, StringComparison.Ordinal);
    }

    // ── node status vocabulary ───────────────────────────────────────────────

    [Fact]
    public void NodeAndJobStates_AreTheExactStringsTheDatabaseConstrains()
    {
        Assert.Equal(
            new[] { "Pending", "Probation", "Active", "Draining", "Disabled", "Quarantined", "Revoked" }.OrderBy(s => s),
            RemoteNodeStatus.All.OrderBy(s => s));
        Assert.Equal(
            new[] { "Succeeded", "Failed", "Quarantined", "FallbackLocal", "Cancelled" }.OrderBy(s => s),
            RemoteJobState.Terminal.OrderBy(s => s));
    }
}
