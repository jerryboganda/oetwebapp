using System.Text.RegularExpressions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>Identifier, idempotency-key and registry rules of OET-RWP/1 (section 0.3 and section 6.0). Pure.</summary>
public sealed class RemoteIdsAndKeysTests
{
    private static readonly Regex JobIdPattern = new("^rj_[0-7][0-9a-hjkmnp-tv-z]{25}$", RegexOptions.CultureInvariant);
    private static readonly Regex NodeIdPattern = new("^rw_[0-7][0-9a-hjkmnp-tv-z]{25}$", RegexOptions.CultureInvariant);

    [Fact]
    public void NewJobId_AndNewNodeId_MatchTheirPatterns()
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 300; i++)
        {
            var job = RemoteIds.NewJobId(now);
            var node = RemoteIds.NewNodeId(now);
            Assert.Matches(JobIdPattern, job);
            Assert.Matches(NodeIdPattern, node);
            Assert.True(RemoteIds.IsJobId(job));
            Assert.True(RemoteIds.IsNodeId(node));
            Assert.False(RemoteIds.IsJobId(node));
            Assert.False(RemoteIds.IsNodeId(job));
        }
    }

    [Fact]
    public void NewUlid_IsDistinctAndSortsByTime()
    {
        var earlier = RemoteIds.NewUlid(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var later = RemoteIds.NewUlid(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(26, earlier.Length);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);

        var ids = Enumerable.Range(0, 500).Select(_ => RemoteIds.NewUlid(DateTimeOffset.UtcNow)).ToHashSet();
        Assert.Equal(500, ids.Count);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("rj_", false)]
    [InlineData("rj_0000000000000000000000000", false)]
    [InlineData("rj_00000000000000000000000000", true)]
    [InlineData("rj_80000000000000000000000000", false)]
    [InlineData("rj_0000000000000000000000000i", false)]
    [InlineData("RJ_00000000000000000000000000", false)]
    [InlineData("rw_00000000000000000000000000", false)]
    public void IsJobId_ValidatesExactly(string? value, bool expected)
        => Assert.Equal(expected, RemoteIds.IsJobId(value));

    [Fact]
    public void Sha256Hex_IsLowercaseAndMatchesAKnownVector()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", RemoteIds.Sha256Hex(string.Empty));
        Assert.True(RemoteIds.IsSha256Hex(RemoteIds.Sha256Hex("x")));
        Assert.False(RemoteIds.IsSha256Hex("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
        Assert.False(RemoteIds.IsSha256Hex("abc"));
        Assert.False(RemoteIds.IsSha256Hex(null));
    }

    [Fact]
    public void FormatTime_IsRfc3339UtcWithMilliseconds()
    {
        var value = new DateTimeOffset(2026, 10, 4, 12, 30, 45, 123, TimeSpan.FromHours(2));
        Assert.Equal("2026-10-04T10:30:45.123Z", RemoteIds.FormatTime(value));
        Assert.Null(RemoteIds.FormatTime((DateTimeOffset?)null));
    }

    [Fact]
    public void IdempotencyKey_IsTheDocumentedCompositionWhenItFits()
    {
        var sha = new string('a', 64);
        var settings = new string('b', 64);
        var key = RemoteJobKeys.IdempotencyKey("pdf.extract", "apply", "MediaAsset", "m1", sha, "pdfpig:1/oet-text:1", settings);

        Assert.Equal($"pdf.extract|apply|MediaAsset:m1|{sha}|pdfpig:1/oet-text:1|{settings}", key);
        Assert.True(key.Length <= RemoteJobKeys.MaxIdempotencyKeyLength);
    }

    [Fact]
    public void IdempotencyKey_IsHashedDeterministicallyWhenTooLong_AndStillDeduplicates()
    {
        var sha = new string('a', 64);
        var settings = new string('b', 64);
        var longId = new string('x', 80);

        var first = RemoteJobKeys.IdempotencyKey("companion.index-prep", "apply", "MediaAsset", longId, sha, "pdfpig:1.7.0-custom-5/oet-text:1/companion-chunker:1", settings);
        var second = RemoteJobKeys.IdempotencyKey("companion.index-prep", "apply", "MediaAsset", longId, sha, "pdfpig:1.7.0-custom-5/oet-text:1/companion-chunker:1", settings);
        var different = RemoteJobKeys.IdempotencyKey("companion.index-prep", "apply", "MediaAsset", longId + "y", sha, "pdfpig:1.7.0-custom-5/oet-text:1/companion-chunker:1", settings);

        Assert.True(first.Length <= RemoteJobKeys.MaxIdempotencyKeyLength);
        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void SettingsHash_IgnoresInsertionOrder_AndChangesWithAnyValue()
    {
        var a = RemoteJobKeys.SettingsHash([new("b", "2"), new("a", "1")]);
        var b = RemoteJobKeys.SettingsHash([new("a", "1"), new("b", "2")]);
        var c = RemoteJobKeys.SettingsHash([new("a", "1"), new("b", "3")]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(RemoteIds.IsSha256Hex(a));
    }

    [Fact]
    public void KindRegistry_HasTheFourKindsWithTheDocumentedWeights()
    {
        Assert.Equal(
            new[] { "pdf.extract", "companion.index-prep", "media.audio-extract", "media.speaking-join" },
            RemoteJobKinds.Names.ToArray());
        Assert.Equal(1, RemoteJobKinds.Find(RemoteJobKinds.PdfExtract)!.Limits.Weight);
        Assert.Equal(2, RemoteJobKinds.Find(RemoteJobKinds.MediaAudioExtract)!.Limits.Weight);
        Assert.Null(RemoteJobKinds.Find("unknown.kind"));
        Assert.Null(RemoteJobKinds.Find(null));
    }

    [Fact]
    public void ShadowRuns_HaveASmallerResultCap_ButOnlyForPdfExtract()
    {
        var pdf = RemoteJobKinds.Find(RemoteJobKinds.PdfExtract)!;
        Assert.Equal(RemoteJobKinds.ShadowMaxResultBytes, RemoteJobKinds.LimitsFor(pdf, RemoteJobPurpose.Shadow).MaxResultBytes);
        Assert.Equal(pdf.Limits.MaxResultBytes, RemoteJobKinds.LimitsFor(pdf, RemoteJobPurpose.Apply).MaxResultBytes);

        var companion = RemoteJobKinds.Find(RemoteJobKinds.CompanionIndexPrep)!;
        Assert.Equal(companion.Limits.MaxResultBytes, RemoteJobKinds.LimitsFor(companion, RemoteJobPurpose.Shadow).MaxResultBytes);
    }

    [Fact]
    public void FlagKeyFor_MapsEveryPurpose_AndCanaryNeedsOnlyTheMasterSwitch()
    {
        Assert.Equal(RemoteJobFlagKeys.PdfExtract, RemoteJobKinds.FlagKeyFor(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply));
        Assert.Equal(RemoteJobFlagKeys.PdfExtractShadow, RemoteJobKinds.FlagKeyFor(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow));
        Assert.Equal(RemoteJobFlagKeys.CompanionIndexPrep, RemoteJobKinds.FlagKeyFor(RemoteJobKinds.CompanionIndexPrep, RemoteJobPurpose.Apply));
        Assert.Null(RemoteJobKinds.FlagKeyFor(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Canary));
    }

    [Fact]
    public void FlagSnapshot_FailsClosed_EverythingOffUntilTheMasterAndTheKindAreOn()
    {
        Assert.False(RemoteFlagSnapshot.AllOff.Master);
        Assert.False(RemoteFlagSnapshot.AllOff.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply));

        var kindOnly = new RemoteFlagSnapshot(new HashSet<string> { RemoteJobFlagKeys.PdfExtract });
        Assert.False(kindOnly.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply));

        var both = new RemoteFlagSnapshot(new HashSet<string> { RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract });
        Assert.True(both.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply));
        Assert.False(both.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow));
        Assert.True(both.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Canary));
        Assert.False(both.FreezeApplies);
        Assert.False(both.FleetService);
    }

    [Fact]
    public void EngineVersion_ComesFromThePdfEngine_AndMediaKindsStayDisabledUntilPinned()
    {
        var options = new RemoteJobsOptions();
        Assert.Matches(new Regex(@"^pdfpig:[^/]+/oet-text:\d+$", RegexOptions.CultureInvariant), RemoteJobKinds.EngineVersion(RemoteJobKinds.PdfExtract, options)!);
        Assert.EndsWith("/companion-chunker:1", RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, options)!, StringComparison.Ordinal);
        Assert.Null(RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaAudioExtract, options));

        options.Kinds["media.audio-extract"] = new RemoteKindOptions { EngineVersion = " ffmpeg:7.0/audio:1 " };
        Assert.Equal("ffmpeg:7.0/audio:1", RemoteJobKinds.EngineVersion(RemoteJobKinds.MediaAudioExtract, options));
    }

    [Fact]
    public void Options_AreClampedToRangesTheStateMachineCanHonour()
    {
        var wild = new RemoteJobsOptions
        {
            LeaseSeconds = 1,
            HeartbeatEverySeconds = 100000,
            MaxAttempts = 0,
            ReleaseLimit = -5,
            BackoffJitterPercent = 900,
            CurrentProtocol = 0,
            MinProtocol = 7,
            VerifySampleRate = 4,
            ClaimRatePerMinute = 0,
        };

        var normal = wild.Normalized();

        Assert.Equal(30, normal.LeaseSeconds);
        Assert.Equal(300, normal.HeartbeatEverySeconds);
        Assert.Equal(1, normal.MaxAttempts);
        Assert.Equal(0, normal.ReleaseLimit);
        Assert.Equal(100, normal.BackoffJitterPercent);
        Assert.Equal(1, normal.CurrentProtocol);
        Assert.Equal(1, normal.MinProtocol);
        Assert.Equal(1d, normal.VerifySampleRate);
        Assert.Equal(1, normal.ClaimRatePerMinute);
    }

    [Fact]
    public void Options_DefaultsMatchTheProtocol()
    {
        var options = new RemoteJobsOptions().Normalized();

        Assert.Equal(120, options.LeaseSeconds);
        Assert.Equal(20, options.HeartbeatEverySeconds);
        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(5, options.ReleaseLimit);
        Assert.Equal(60, options.ClaimRatePerMinute);
        Assert.False(options.FairShareGate);
        Assert.Equal(0d, options.VerifySampleRate);
        Assert.Empty(options.FleetAllowedCidrs);
    }
}
