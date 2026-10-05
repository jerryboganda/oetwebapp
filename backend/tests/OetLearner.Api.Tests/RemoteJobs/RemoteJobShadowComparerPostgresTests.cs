using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The ai-worker's shadow comparison and verify sampling (OET-RWP/1 sections 6.1.5 and 9.3) over real PostgreSQL: the scan window
/// must advance past jobs the sampler does not select, throttle re-extraction, and never let one unreadable asset block the line.
/// Jobs are inserted already finished (Succeeded + ApplyOutcome) so each test controls ids, ages and summaries exactly.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteJobShadowComparerPostgresTests
{
    private const string AssetKey = "media/sample.pdf";

    private sealed class Rig(ServiceProvider provider, RemoteJobShadowComparer comparer) : IDisposable
    {
        public RemoteJobShadowComparer Comparer { get; } = comparer;

        public void Dispose() => provider.Dispose();
    }

    private static Rig NewRig(RemotePgHarness h, IFileStorage? storage = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => h.NewContext());
        services.AddSingleton<IFileStorage>(storage ?? h.Storage);
        services.AddSingleton(new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance));
        services.AddSingleton<IRemoteJobFlags>(h.Flags);
        var provider = services.BuildServiceProvider();
        var comparer = new RemoteJobShadowComparer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            h.Settings,
            TimeProvider.System,
            NullLogger<RemoteJobShadowComparer>.Instance);
        return new Rig(provider, comparer);
    }

    /// <summary>A media asset whose stored object is the canary PDF (real text, so the in-process oracle yields real hashes).</summary>
    private static async Task SeedAssetAsync(RemotePgHarness h, string mediaId = "media-1", string storageKey = AssetKey, string assetId = "asset-1")
    {
        await h.SeedPdfAsync(mediaId: mediaId, sha: RemoteCanary.PdfSha256, size: RemoteCanary.PdfSize, paperId: "paper-" + mediaId, assetId: assetId);
        await h.SqlAsync("""UPDATE "MediaAssets" SET "StoragePath" = @path WHERE "Id" = @id;""", ("path", storageKey), ("id", mediaId));
        if (!await h.Storage.ExistsAsync(storageKey, CancellationToken.None))
        {
            await h.Storage.WriteAsync(storageKey, new MemoryStream(RemoteCanary.PdfBytes), CancellationToken.None);
        }
    }

    private static string SummaryJson(CanaryExpected expected, bool tamper = false)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["needsOcr"] = false,
            ["pageCount"] = expected.PageCount,
            ["embeddedChars"] = expected.EmbeddedChars,
            ["textSha256"] = tamper ? new string('0', 64) : expected.TextSha256,
            ["pagesSha256"] = expected.PagesSha256,
        });

    private static Task InsertJobAsync(
        RemotePgHarness h,
        string id,
        string purpose,
        string outcome,
        string summaryJson,
        double ageSeconds,
        string? settledBy = null,
        string mediaId = "media-1")
        => h.SqlAsync(
            """
            INSERT INTO "RemoteJobs"
                ("Id", "Kind", "SchemaVersion", "Purpose", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256", "EngineVersion",
                 "SettingsHash", "ParamsJson", "InputsJson", "LimitsJson", "Weight", "State", "Attempt", "MaxAttempts", "FenceToken",
                 "NextAttemptAt", "EnqueuedBy", "ApplyOutcome", "ResultSummaryJson", "CompletedAt", "SettledFence", "SettledBy",
                 "CreatedAt", "UpdatedAt")
            VALUES
                (@id, 'pdf.extract', 1, @purpose, 'MediaAsset', @media, @key, @sha, 'engine:1',
                 @sha, CAST(@params AS jsonb), '[]'::jsonb, '{}'::jsonb, 1, 'Succeeded', 1, 3, 1,
                 clock_timestamp(), 'test', @outcome, CAST(@summary AS jsonb), clock_timestamp() - make_interval(secs => @age), 1, @node,
                 clock_timestamp() - make_interval(secs => @age + 5), clock_timestamp() - make_interval(secs => @age));
            """,
            ("id", id), ("purpose", purpose), ("media", mediaId), ("key", "key-" + id), ("sha", RemoteCanary.PdfSha256),
            ("params", RemotePgHarness.PdfParams), ("outcome", outcome), ("summary", summaryJson), ("age", ageSeconds), ("node", settledBy));

    private static List<string> IdsWhere(bool sampled, double rate, int count, string prefix)
    {
        var ids = new List<string>();
        for (var i = 0; ids.Count < count && i < 200_000; i++)
        {
            var id = prefix + i;
            if (PdfParity.IsSampled(id, rate) == sampled) ids.Add(id);
        }

        Assert.Equal(count, ids.Count);
        return ids;
    }

    private static Task<string?> ComparisonAsync(RemotePgHarness h, string id)
        => h.ScalarAsync<string>("""SELECT "ResultSummaryJson"->>'comparison' FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", id));

    private static Task<int> CountComparisonAsync(RemotePgHarness h, string comparison)
        => h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "ResultSummaryJson"->>'comparison' = @c;""", ("c", comparison));

    private static async Task<string> OracleFlatAsync()
    {
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        await using var stream = RemoteCanary.OpenRead();
        var pages = await extractor.ExtractPagesAsync(stream, CancellationToken.None);
        return string.Join("\n\n", pages).Trim();
    }

    // ── verify sampling ──────────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Verify_AdvancesPastUnsampledJobs_SoANewerSampledJobIsStillChecked()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.VerifySampleRate = 0.05;
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        var summary = SummaryJson(await RemoteCanary.GetExpectedAsync());

        // More unsampled jobs than one scan window, all OLDER than the one job the sampler selects.
        var unsampled = IdsWhere(sampled: false, 0.05, RemoteJobShadowComparer.ScanWindow + 10, "rj_u_");
        var sampled = IdsWhere(sampled: true, 0.05, 1, "rj_s_").Single();
        for (var i = 0; i < unsampled.Count; i++)
        {
            await InsertJobAsync(h, unsampled[i], "apply", "Applied", summary, ageSeconds: 5000 - i, settledBy: node);
        }

        await InsertJobAsync(h, sampled, "apply", "Applied", summary, ageSeconds: 1, settledBy: node);
        using var rig = NewRig(h);

        // Pass 1 sees only unsampled rows: it records them instead of re-reading them forever, and re-extracts nothing.
        Assert.Equal(0, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Equal(RemoteJobShadowComparer.ScanWindow, await CountComparisonAsync(h, "not_sampled"));
        Assert.Null(await ComparisonAsync(h, sampled));

        // Pass 2 reaches the sampled job.
        Assert.Equal(1, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Equal("match", await ComparisonAsync(h, sampled));
        Assert.Equal(unsampled.Count, await CountComparisonAsync(h, "not_sampled"));

        // Nothing is left to read.
        Assert.Equal(0, await rig.Comparer.RunOnceAsync(CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task Verify_RateZero_ChecksNothingAndMarksNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        await InsertJobAsync(h, "rj_zero_1", "apply", "Applied", SummaryJson(await RemoteCanary.GetExpectedAsync()), 10, node);
        using var rig = NewRig(h);

        Assert.Equal(0, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Null(await ComparisonAsync(h, "rj_zero_1"));
    }

    [PostgreSqlFact]
    public async Task Verify_AtMostBatchSizeSampledJobsAreReExtractedPerPass()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.VerifySampleRate = 1;
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        var summary = SummaryJson(await RemoteCanary.GetExpectedAsync());
        for (var i = 0; i < RemoteJobShadowComparer.BatchSize + 2; i++)
        {
            await InsertJobAsync(h, "rj_batch_" + i, "apply", "Applied", summary, ageSeconds: 100 - i, settledBy: node);
        }

        using var rig = NewRig(h);

        Assert.Equal(RemoteJobShadowComparer.BatchSize, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Equal(RemoteJobShadowComparer.BatchSize, await CountComparisonAsync(h, "match"));
        Assert.Equal(2, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Equal(RemoteJobShadowComparer.BatchSize + 2, await CountComparisonAsync(h, "match"));
    }

    [PostgreSqlFact]
    public async Task Verify_ASampledMismatch_AuditsStrikesTheNode_AndTheInProcessTextReplacesTheCachedOne()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Options.VerifySampleRate = 1;
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        await InsertJobAsync(h, "rj_bad_1", "apply", "Applied", SummaryJson(await RemoteCanary.GetExpectedAsync(), tamper: true), 10, node);
        using var rig = NewRig(h);

        Assert.Equal(1, await rig.Comparer.RunOnceAsync(CancellationToken.None));

        Assert.Equal("mismatch", await ComparisonAsync(h, "rj_bad_1"));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.VerifyMismatch"));
        Assert.Equal(1, (await h.NodeAsync(node)).IntegrityStrikes);
        using var document = JsonDocument.Parse((await h.PaperJsonAsync("paper-media-1"))!);
        Assert.Equal(await OracleFlatAsync(), document.RootElement.GetProperty("asset-1").GetString());
    }

    // ── shadow comparison ────────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Shadow_AMatchIsRecorded_AMismatchIsAudited_AndNeitherStrikesTheNodeOrTouchesThePaper()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtractShadow);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        var expected = await RemoteCanary.GetExpectedAsync();
        await InsertJobAsync(h, "rj_sh_ok", "shadow", "Shadow", SummaryJson(expected), 20, node);
        await InsertJobAsync(h, "rj_sh_bad", "shadow", "Shadow", SummaryJson(expected, tamper: true), 10, node);
        using var rig = NewRig(h);

        Assert.Equal(2, await rig.Comparer.RunOnceAsync(CancellationToken.None));

        Assert.Equal("match", await ComparisonAsync(h, "rj_sh_ok"));
        Assert.Equal("mismatch", await ComparisonAsync(h, "rj_sh_bad"));
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.ShadowMismatch"));
        Assert.Equal(0, await h.AuditCountAsync("RemoteJob.VerifyMismatch"));
        Assert.Equal(0, (await h.NodeAsync(node)).IntegrityStrikes);
        Assert.Equal("{}", await h.PaperJsonAsync("paper-media-1"));
    }

    [PostgreSqlFact]
    public async Task Shadow_WhenTheFlagIsOff_NothingIsCompared()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        await InsertJobAsync(h, "rj_sh_off", "shadow", "Shadow", SummaryJson(await RemoteCanary.GetExpectedAsync()), 10, node);
        using var rig = NewRig(h);

        Assert.Equal(0, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Null(await ComparisonAsync(h, "rj_sh_off"));
    }

    [PostgreSqlFact]
    public async Task Shadow_AJobWhoseAssetChangedOrWhoseSummaryIsUnreadable_IsRecordedStaleOrUnreadable()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtractShadow);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        var summary = SummaryJson(await RemoteCanary.GetExpectedAsync());
        await InsertJobAsync(h, "rj_changed", "shadow", "Shadow", summary, 30, node, mediaId: "media-none");
        await InsertJobAsync(h, "rj_unreadable", "shadow", "Shadow", "{\"note\":\"no fields\"}", 20, node);
        using var rig = NewRig(h);

        Assert.Equal(2, await rig.Comparer.RunOnceAsync(CancellationToken.None));

        Assert.Equal("stale", await ComparisonAsync(h, "rj_changed"));
        Assert.Equal("unreadable", await ComparisonAsync(h, "rj_unreadable"));
    }

    // ── an unreadable asset must not block the line ──────────────────────────

    [PostgreSqlFact]
    public async Task Shadow_AVanishedStorageObject_IsRecordedStale()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtractShadow);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        await SeedAssetAsync(h, mediaId: "media-gone", storageKey: "media/gone.pdf", assetId: "asset-gone");
        var storage = new FaultInjectingStorage(h.Storage)
        {
            Fault = (operation, key) => operation == "read" && key == "media/gone.pdf" ? new FileNotFoundException("gone", key) : null,
        };
        await InsertJobAsync(h, "rj_gone", "shadow", "Shadow", SummaryJson(await RemoteCanary.GetExpectedAsync()), 10, node, mediaId: "media-gone");
        using var rig = NewRig(h, storage);

        Assert.Equal(1, await rig.Comparer.RunOnceAsync(CancellationToken.None));

        Assert.Equal("stale", await ComparisonAsync(h, "rj_gone"));
    }

    [PostgreSqlFact]
    public async Task Shadow_JobsWithAnUnreadableAsset_RotateToTheBack_SoNewerJobsAreStillCompared()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtractShadow);
        var node = await h.AddNodeAsync();
        await SeedAssetAsync(h);
        await SeedAssetAsync(h, mediaId: "media-bad", storageKey: "media/bad.pdf", assetId: "asset-bad");
        var storage = new FaultInjectingStorage(h.Storage)
        {
            Fault = (operation, key) => operation == "read" && key == "media/bad.pdf" ? new IOException("transient storage failure") : null,
        };
        var summary = SummaryJson(await RemoteCanary.GetExpectedAsync());

        // BatchSize unreadable jobs at the head of the line, one readable job behind them.
        for (var i = 0; i < RemoteJobShadowComparer.BatchSize; i++)
        {
            await InsertJobAsync(h, "rj_blocked_" + i, "shadow", "Shadow", summary, ageSeconds: 900 - (i * 10), settledBy: node, mediaId: "media-bad");
        }

        await InsertJobAsync(h, "rj_readable", "shadow", "Shadow", summary, ageSeconds: 5, settledBy: node);
        using var rig = NewRig(h, storage);

        // Pass 1 spends its whole batch on the unreadable jobs (no verdict, so they are retried later) ...
        Assert.Equal(0, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Null(await ComparisonAsync(h, "rj_readable"));

        // ... but they moved behind the readable one, which pass 2 compares.
        Assert.Equal(1, await rig.Comparer.RunOnceAsync(CancellationToken.None));
        Assert.Equal("match", await ComparisonAsync(h, "rj_readable"));
        for (var i = 0; i < RemoteJobShadowComparer.BatchSize; i++)
        {
            Assert.Null(await ComparisonAsync(h, "rj_blocked_" + i));
        }
    }
}
