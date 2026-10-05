using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>Headroom (RW-117), parity/verify sampling, rate limits (RW-008, RW-009, RW-064), caches and small helpers. No Postgres.</summary>
public sealed class RemoteSupportingLogicTests
{
    // ── primary headroom (cgroup v2 readings) ────────────────────────────────

    private const string CalmCpu = "some avg10=1.23 avg60=0.50 avg300=0.10 total=12345\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n";
    private const string CalmMemory = "some avg10=0.50 avg60=0.10 avg300=0.00 total=99\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n";

    [Fact]
    public void Headroom_ACalmPrimary_HasHeadroom()
    {
        Assert.True(PrimaryHeadroomEvaluator.Evaluate(CalmCpu, CalmMemory, "1000000", "4000000"));
    }

    [Fact]
    public void Headroom_PressureAtOrOverTheLimit_MeansNone()
    {
        Assert.False(PrimaryHeadroomEvaluator.Evaluate("some avg10=25.00 avg60=1 avg300=1 total=1", CalmMemory, "1", "4000000"));
        Assert.False(PrimaryHeadroomEvaluator.Evaluate(CalmCpu, "some avg10=5.00 avg60=1 avg300=1 total=1", "1", "4000000"));
        Assert.True(PrimaryHeadroomEvaluator.Evaluate("some avg10=24.99 avg60=1 avg300=1 total=1", "some avg10=4.99 avg60=1 avg300=1 total=1", "1", "4000000"));
    }

    [Fact]
    public void Headroom_AWorkingSetOverThreeQuartersOfTheLimit_MeansNone()
    {
        Assert.False(PrimaryHeadroomEvaluator.Evaluate(CalmCpu, CalmMemory, "3000000", "4000000"));
        Assert.True(PrimaryHeadroomEvaluator.Evaluate(CalmCpu, CalmMemory, "2999999", "4000000"));
    }

    [Fact]
    public void Headroom_AnUnlimitedMemoryLimit_SkipsOnlyTheWorkingSetTest()
    {
        Assert.True(PrimaryHeadroomEvaluator.Evaluate(CalmCpu, CalmMemory, "9999999999", "max"));
        Assert.False(PrimaryHeadroomEvaluator.Evaluate("some avg10=90.00 avg60=1 avg300=1 total=1", CalmMemory, "1", "max"));
    }

    [Theory]
    [InlineData(null, "x", "1", "2")]
    [InlineData("garbage", CalmMemory, "1", "2")]
    [InlineData(CalmCpu, null, "1", "2")]
    [InlineData(CalmCpu, CalmMemory, null, "2")]
    [InlineData(CalmCpu, CalmMemory, "not-a-number", "2")]
    [InlineData(CalmCpu, CalmMemory, "1", null)]
    [InlineData(CalmCpu, CalmMemory, "1", "0")]
    [InlineData(CalmCpu, CalmMemory, "1", "garbage")]
    public void Headroom_AMissingOrUnparseableReading_FailsClosed(string? cpu, string? memory, string? current, string? max)
    {
        Assert.False(PrimaryHeadroomEvaluator.Evaluate(cpu, memory, current, max));
    }

    [Fact]
    public void Headroom_ParsesTheSomeLineOnly()
    {
        Assert.Equal(1.23, PrimaryHeadroomEvaluator.ParseSomeAvg10(CalmCpu));
        Assert.Null(PrimaryHeadroomEvaluator.ParseSomeAvg10("full avg10=9.00 avg60=0 avg300=0 total=0"));
        Assert.Null(PrimaryHeadroomEvaluator.ParseSomeAvg10(null));
    }

    // ── shadow / verify comparison ───────────────────────────────────────────

    private static readonly string[] OraclePages =
    [
        "The oracle page one carries a comfortable amount of ordinary extracted text.",
        "The oracle page two carries a little more extracted text for the comparison.",
    ];

    private static PdfSummaryView SummaryOf(IReadOnlyList<string> pages)
    {
        var derived = RemoteCanary.Derive(pages);
        return new PdfSummaryView(false, derived.PageCount, derived.EmbeddedChars, derived.TextSha256, derived.PagesSha256);
    }

    [Fact]
    public void Parity_IdenticalExtractionHasNoDifferences()
    {
        Assert.Empty(PdfParity.Differences(SummaryOf(OraclePages), OraclePages, 50));
    }

    [Fact]
    public void Parity_ReportsEachDifferingFieldByName()
    {
        var summary = SummaryOf(OraclePages) with { TextSha256 = new string('0', 64), EmbeddedChars = 1 };

        var differences = PdfParity.Differences(summary, OraclePages, 50);

        Assert.Contains("textSha256", differences);
        Assert.Contains("embeddedChars", differences);
        Assert.DoesNotContain("pageCount", differences);
        Assert.DoesNotContain("pagesSha256", differences);
    }

    [Fact]
    public void Parity_ANeedsOcrDisagreementIsTheOnlyDifferenceReported()
    {
        var remoteSaysText = SummaryOf(OraclePages);
        var tiny = new[] { "short" };

        Assert.Equal(new[] { "needsOcr" }, PdfParity.Differences(remoteSaysText, tiny, 50).ToArray());
        Assert.Equal(
            new[] { "needsOcr" },
            PdfParity.Differences(new PdfSummaryView(true, 1, 5, null, null), OraclePages, 50).ToArray());
        Assert.Empty(PdfParity.Differences(new PdfSummaryView(true, 7, 5, null, null), tiny, 50));
    }

    [Fact]
    public void VerifySampling_IsDeterministic_AndRoughlyTheConfiguredFraction()
    {
        Assert.False(PdfParity.IsSampled("rj_x", 0));
        Assert.False(PdfParity.IsSampled("rj_x", -1));
        Assert.True(PdfParity.IsSampled("rj_x", 1));

        var ids = Enumerable.Range(0, 4000).Select(i => RemoteIds.Sha256Hex("job-" + i)[..26]).ToList();
        var first = ids.Count(id => PdfParity.IsSampled(id, 0.1));
        var second = ids.Count(id => PdfParity.IsSampled(id, 0.1));

        Assert.Equal(first, second);
        Assert.InRange(first, 250, 550);
    }

    // ── rate limits ──────────────────────────────────────────────────────────

    private static MutableClock ClockTenSecondsIntoAMinute()
        => new(new DateTimeOffset(2026, 10, 5, 12, 0, 10, TimeSpan.Zero));

    [Fact]
    public void RateLimit_AllowsTheLimit_ThenRefuses_ThenResetsWithTheWindow()
    {
        var clock = ClockTenSecondsIntoAMinute();
        var limits = new RemoteRateLimits(clock);

        for (var i = 0; i < 3; i++) Assert.True(limits.TryConsume("claim", "rw_a", 3, out _));
        Assert.False(limits.TryConsume("claim", "rw_a", 3, out var retryAfter));
        Assert.Equal(50, retryAfter);

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limits.TryConsume("claim", "rw_a", 3, out _));
    }

    [Fact]
    public void RateLimit_IsKeyedPerNodeAndPerBucket_NotShared()
    {
        var limits = new RemoteRateLimits(ClockTenSecondsIntoAMinute());

        Assert.True(limits.TryConsume("claim", "rw_a", 1, out _));
        Assert.False(limits.TryConsume("claim", "rw_a", 1, out _));
        Assert.True(limits.TryConsume("claim", "rw_b", 1, out _));
        Assert.True(limits.TryConsume("complete", "rw_a", 1, out _));
    }

    [Fact]
    public void RateLimit_FailedVerifications_ThrottleBySourceWithoutConsumingAnAllowance()
    {
        var limits = new RemoteRateLimits(ClockTenSecondsIntoAMinute());

        for (var i = 0; i < 119; i++) limits.Hit("remote-auth-fail", "203.0.113.9");
        Assert.False(limits.IsExceeded("remote-auth-fail", "203.0.113.9", 120, out _));
        limits.Hit("remote-auth-fail", "203.0.113.9");
        Assert.True(limits.IsExceeded("remote-auth-fail", "203.0.113.9", 120, out var retryAfter));
        Assert.Equal(50, retryAfter);
        Assert.False(limits.IsExceeded("remote-auth-fail", "203.0.113.10", 120, out _));
    }

    [Fact]
    public void Streams_AreLimitedPerNode_AndReleasedOnDispose()
    {
        var limits = new RemoteRateLimits(ClockTenSecondsIntoAMinute());

        var first = limits.TryAcquireStream("rw_a", 2);
        var second = limits.TryAcquireStream("rw_a", 2);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(limits.TryAcquireStream("rw_a", 2));
        Assert.Equal(2, limits.OpenStreams("rw_a"));

        first!.Dispose();
        first.Dispose(); // releasing twice must not free a slot that is still held
        Assert.Equal(1, limits.OpenStreams("rw_a"));
        Assert.NotNull(limits.TryAcquireStream("rw_a", 2));

        // another node is unaffected
        Assert.NotNull(limits.TryAcquireStream("rw_b", 2));
    }

    // ── auth cache / orphan tracker / wait tracker ───────────────────────────

    [Fact]
    public void AuthCache_ServesAnEntryForFiveSeconds_AndCanBeRevoked()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var cache = new RemoteAuthCache(clock);
        var snapshot = new RemoteAuthSnapshot(new RemoteCredential { TokenId = "0123456789abcdef" }, null, clock.GetUtcNow());

        cache.Set("0123456789abcdef", snapshot);
        Assert.True(cache.TryGet("0123456789abcdef", out _));

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.True(cache.TryGet("0123456789abcdef", out _));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(cache.TryGet("0123456789abcdef", out _));

        cache.Set("0123456789abcdef", snapshot with { LoadedAt = clock.GetUtcNow() });
        cache.Remove("0123456789abcdef");
        Assert.False(cache.TryGet("0123456789abcdef", out _));
    }

    [Fact]
    public void AuthCache_RecordsCredentialUseAtMostOncePerMinute()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var cache = new RemoteAuthCache(clock);

        Assert.True(cache.ShouldRecordUse("0123456789abcdef"));
        Assert.False(cache.ShouldRecordUse("0123456789abcdef"));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(cache.ShouldRecordUse("0123456789abcdef"));
    }

    [Fact]
    public void OrphanTracker_ReleasesOnlyALeaseMissingFromTwoConsecutiveHeartbeats()
    {
        var tracker = new RemoteOrphanTracker();

        Assert.Empty(tracker.ConfirmMissing("rw_a", new[] { "rj_1", "rj_2" }));
        // rj_1 is missing again, rj_2 reappeared, rj_3 is newly missing
        var confirmed = tracker.ConfirmMissing("rw_a", new[] { "rj_1", "rj_3" });
        Assert.Equal(new[] { "rj_1" }, confirmed.ToArray());

        // rj_1 was confirmed and forgotten; rj_3 needs one more heartbeat
        Assert.Equal(new[] { "rj_3" }, tracker.ConfirmMissing("rw_a", new[] { "rj_3" }).ToArray());

        // other nodes are independent, and Forget clears one
        Assert.Empty(tracker.ConfirmMissing("rw_b", new[] { "rj_3" }));
        tracker.Forget("rw_b");
        Assert.Empty(tracker.ConfirmMissing("rw_b", new[] { "rj_3" }));
    }

    [Fact]
    public void LocalWaitTracker_FindsTheHardDeadlineOnlyAfterTheFullWait()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new RemoteLocalWaitTracker(clock);
        var hardAfter = TimeSpan.FromMinutes(60);

        Assert.False(tracker.HardDeadlinePassed("pdf:p1", hardAfter));
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(tracker.HardDeadlinePassed("pdf:p1", hardAfter));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(tracker.HardDeadlinePassed("pdf:p1", hardAfter));

        tracker.Clear("pdf:p1");
        Assert.False(tracker.HardDeadlinePassed("pdf:p1", hardAfter));
    }

    [Fact]
    public void LocalWaitTracker_WithAnIdleReset_NeverFailsANewWaitOnTheStaleClockOfAnEarlierOne()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new RemoteLocalWaitTracker(clock);
        var hardAfter = TimeSpan.FromMinutes(60);
        var idle = TimeSpan.FromMinutes(15);

        // asked about regularly: the wait accumulates exactly as without the reset
        Assert.False(tracker.HardDeadlinePassed("audio:r1", hardAfter, idle));
        for (var minutes = 10; minutes < 60; minutes += 10)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.False(tracker.HardDeadlinePassed("audio:r1", hardAfter, idle));
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(tracker.HardDeadlinePassed("audio:r1", hardAfter, idle));

        // nobody asked for longer than the idle window: that wait ended elsewhere, so this one starts afresh
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.False(tracker.HardDeadlinePassed("audio:r1", hardAfter, idle));
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.False(tracker.HardDeadlinePassed("audio:r1", hardAfter, idle));

        // without the reset a caller keeps the original clock (the PDF producer's behaviour is unchanged)
        Assert.False(tracker.HardDeadlinePassed("pdf:p1", hardAfter));
        clock.Advance(TimeSpan.FromHours(3));
        Assert.True(tracker.HardDeadlinePassed("pdf:p1", hardAfter));
    }

    // ── small pure helpers ───────────────────────────────────────────────────

    [Fact]
    public void NodeHealth_IsDerivedFromTheLastHeartbeat()
    {
        var options = new RemoteJobsOptions().Normalized();
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(RemoteNodeView.Unseen, RemoteNodeView.Health(null, now, options));
        Assert.Equal(RemoteNodeView.Online, RemoteNodeView.Health(now.AddSeconds(-10), now, options));
        Assert.Equal(RemoteNodeView.Stale, RemoteNodeView.Health(now.AddSeconds(-120), now, options));
        Assert.Equal(RemoteNodeView.Offline, RemoteNodeView.Health(now.AddHours(-2), now, options));
    }

    [Fact]
    public void NodeView_NeverCarriesASecretOrAStorageKey()
    {
        var node = new RemoteWorker
        {
            Id = "rw_00000000000000000000000001",
            NodeRef = "helper-1",
            DisplayName = "Helper 1",
            Status = RemoteNodeStatus.Active,
            StatusChangedAt = DateTimeOffset.UtcNow,
            AllowedKinds = [RemoteJobKinds.PdfExtract],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "fleet:test",
            LastCanaryAt = DateTimeOffset.UtcNow,
            LastCanaryOk = true,
        };

        var json = JsonSerializer.Serialize(
            RemoteNodeView.Build(node, new RemoteNodeAggregates(1, 1, 2, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, new RemoteJobsOptions()),
            RemoteJson.Response);

        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orw1_", json, StringComparison.Ordinal);
        Assert.Contains("\"nodeRef\":\"helper-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"activeCount\":2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DesiredState_DrainsForDrainingAndDisabled_AndCarriesTheRevision()
    {
        var node = new RemoteWorker
        {
            Id = "rw_00000000000000000000000001",
            Status = RemoteNodeStatus.Draining,
            PolicyRevision = 9,
            AllowedKinds = [RemoteJobKinds.PdfExtract],
        };

        var desired = RemoteDesiredState.Build(node);

        Assert.Equal(9L, desired["revision"]);
        Assert.Equal(true, desired["drain"]);

        node.Status = RemoteNodeStatus.Active;
        Assert.Equal(false, RemoteDesiredState.Build(node)["drain"]);
        node.Status = RemoteNodeStatus.Disabled;
        Assert.Equal(true, RemoteDesiredState.Build(node)["drain"]);
    }

    [Fact]
    public void ReadManifest_ParsesTheInputsAndToleratesGarbage()
    {
        var json = "[{\"name\":\"pdf\",\"sizeBytes\":12,\"sha256\":\"" + RemoteTestData.Sha + "\",\"contentType\":\"application/pdf\",\"storageKey\":\"k/1.pdf\"},"
                   + "{\"name\":\"incomplete\"},42]";

        var manifest = RemoteInputOutputService.ReadManifest(json);

        var entry = Assert.Single(manifest);
        Assert.Equal("pdf", entry.Name);
        Assert.Equal(12, entry.SizeBytes);
        Assert.Equal("k/1.pdf", entry.StorageKey);

        Assert.Empty(RemoteInputOutputService.ReadManifest("not json"));
        Assert.Empty(RemoteInputOutputService.ReadManifest("{}"));
    }

    [Fact]
    public void OutputKey_IsPerJobAndPerFence()
    {
        Assert.Equal("remote-jobs/rj_1/3/audio.m4a", RemoteInputOutputService.OutputKey("rj_1", 3, "audio.m4a"));
    }

    [Fact]
    public void OffersKind_RequiresTheExactKindSchemaAndEngine()
    {
        var kinds = "[{\"kind\":\"pdf.extract\",\"schemaVersions\":[1,2],\"engineVersion\":\"e:1\"}]";

        Assert.True(RemotePlacement.OffersKind(kinds, "pdf.extract", 2, "e:1"));
        Assert.False(RemotePlacement.OffersKind(kinds, "pdf.extract", 3, "e:1"));
        Assert.False(RemotePlacement.OffersKind(kinds, "pdf.extract", 1, "e:2"));
        Assert.False(RemotePlacement.OffersKind(kinds, "companion.index-prep", 1, "e:1"));
        Assert.False(RemotePlacement.OffersKind("garbage", "pdf.extract", 1, "e:1"));
        Assert.False(RemotePlacement.OffersKind(null, "pdf.extract", 1, "e:1"));
    }

    [Fact]
    public void FleetPlane_SourceAllowList_AcceptsLoopbackAndConfiguredNetworksOnly()
    {
        var none = Array.Empty<string>();
        Assert.True(RemoteFleetPlaneFilter.IsAllowedSource(null, none));
        Assert.True(RemoteFleetPlaneFilter.IsAllowedSource(IPAddress.Parse("198.51.100.7"), none));

        var allowed = new[] { "127.0.0.0/8", "172.18.0.0/16" };
        Assert.True(RemoteFleetPlaneFilter.IsAllowedSource(IPAddress.Parse("127.0.0.1"), allowed));
        Assert.True(RemoteFleetPlaneFilter.IsAllowedSource(IPAddress.Parse("172.18.4.9"), allowed));
        Assert.True(RemoteFleetPlaneFilter.IsAllowedSource(IPAddress.Parse("127.0.0.1").MapToIPv6(), allowed));
        Assert.False(RemoteFleetPlaneFilter.IsAllowedSource(IPAddress.Parse("198.51.100.7"), allowed));
        Assert.False(RemoteFleetPlaneFilter.IsAllowedSource(null, allowed));
    }

    [Fact]
    public void Settings_SupportedProtocolsSpanNMinusOneToN()
    {
        var settings = RemoteTestData.Settings(new RemoteJobsOptions { CurrentProtocol = 2, MinProtocol = 1 });

        Assert.Equal(new[] { 1, 2 }, settings.SupportedProtocols.ToArray());
        Assert.Equal(new[] { 1 }, RemoteTestData.Settings().SupportedProtocols.ToArray());
    }

    [Fact]
    public void Problems_CarryTheEnvelopeAndNeverTheLeakyDetail()
    {
        Assert.Equal(401, RemoteProblems.Unauthorized().StatusCode);
        Assert.Equal("unauthorized", RemoteProblems.Unauthorized().Code);
        Assert.Equal(409, RemoteProblems.LeaseLost("expired").StatusCode);
        Assert.Equal("expired", RemoteProblems.LeaseLost("expired").Reason);
        Assert.Equal(426, RemoteProblems.UpgradeRequired([1]).StatusCode);
        Assert.Equal(429, RemoteProblems.RateLimited(7).StatusCode);
    }

    // ── feature flags: cached, fail closed ───────────────────────────────────

    private sealed class ToggleScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public bool Fail { get; set; }

        public IServiceScope CreateScope()
            => Fail ? throw new InvalidOperationException("database down") : inner.CreateScope();
    }

    private static (RemoteJobFlags Flags, ToggleScopeFactory Factory, MutableClock Clock, IServiceProvider Provider) BuildFlags()
    {
        var dbName = "flags-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var factory = new ToggleScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        return (new RemoteJobFlags(factory, clock, NullLogger<RemoteJobFlags>.Instance), factory, clock, provider);
    }

    private static async Task SetFlagAsync(IServiceProvider provider, string key, bool enabled)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key);
        if (row is null)
        {
            db.FeatureFlags.Add(new FeatureFlag
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = key,
                Key = key,
                FlagType = FeatureFlagType.Operational,
                Enabled = enabled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Enabled = enabled;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Flags_AreOffByDefault_AndCachedForFiveSeconds()
    {
        var (flags, _, clock, provider) = BuildFlags();

        Assert.False((await flags.GetAsync(CancellationToken.None)).Master);

        await SetFlagAsync(provider, RemoteJobFlagKeys.Master, true);
        Assert.False((await flags.GetAsync(CancellationToken.None)).Master); // still the cached answer

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True((await flags.GetAsync(CancellationToken.None)).Master);

        await SetFlagAsync(provider, RemoteJobFlagKeys.Master, false);
        flags.Invalidate();
        Assert.False((await flags.GetAsync(CancellationToken.None)).Master);
    }

    [Fact]
    public async Task Flags_AnUnreadableDatabaseFailsClosed_AtOnceWhenNothingWasEverLoaded()
    {
        var (flags, factory, _, _) = BuildFlags();
        factory.Fail = true;

        var snapshot = await flags.GetAsync(CancellationToken.None);

        Assert.False(snapshot.Master);
        Assert.False(snapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply));
    }

    [Fact]
    public async Task Flags_ABriefOutageKeepsTheLastAnswer_ButALongOneTurnsEverythingOff()
    {
        var (flags, factory, clock, provider) = BuildFlags();
        await SetFlagAsync(provider, RemoteJobFlagKeys.Master, true);
        Assert.True((await flags.GetAsync(CancellationToken.None)).Master);

        factory.Fail = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True((await flags.GetAsync(CancellationToken.None)).Master); // within the stale allowance

        // The outage continues: repeated failed attempts must NOT keep refreshing the allowance.
        for (var i = 0; i < 12; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(6));
            await flags.GetAsync(CancellationToken.None);
        }

        Assert.False((await flags.GetAsync(CancellationToken.None)).Master);
    }

    [Fact]
    public async Task Flags_TheNewestRowWinsWhenAKeyWasEverDuplicated()
    {
        var (flags, _, _, provider) = BuildFlags();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var older = DateTimeOffset.UtcNow.AddMinutes(-5);
            db.FeatureFlags.Add(new FeatureFlag { Id = "a", Name = "a", Key = RemoteJobFlagKeys.Master, Enabled = true, CreatedAt = older, UpdatedAt = older });
            db.FeatureFlags.Add(new FeatureFlag { Id = "b", Name = "b", Key = RemoteJobFlagKeys.Master, Enabled = false, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.False((await flags.GetAsync(CancellationToken.None)).Master);
    }
}
