using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The <c>companion.index-prep</c> producer and consumer (OET-RWP/1 section 6.2) on real PostgreSQL: the indexer becomes
/// enqueue-and-poll, a parked result is consumed only after full re-validation, and any doubt means the unchanged local path.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteCompanionPrepPostgresTests
{
    private const string SourceKey = "material:file-1";

    private static MediaAsset Asset() => new()
    {
        Id = "media-1",
        OriginalFilename = "handout.pdf",
        MimeType = "application/pdf",
        Format = "pdf",
        SizeBytes = 1234,
        StoragePath = "media/sample.pdf",
        Sha256 = RemoteTestData.Sha,
    };

    private static readonly string[] Pages =
    [
        new string('a', 300) + " first page about the opening of a referral letter.",
        new string('b', 300) + " second page about the ordering of the paragraphs.",
    ];

    private static async Task<CompanionPrepPlan> PlanAsync(RemotePgHarness h, bool headroom = true)
    {
        await using var db = h.NewContext();
        var placement = new RemotePlacement(db, h.Settings, new FixedHeadroom { Value = headroom }, h.Time);
        var producer = new RemoteCompanionIndexPrepProducer(
            db,
            h.Flags,
            h.Queue(db),
            placement,
            new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()),
            h.Storage,
            h.Settings,
            NullLogger<RemoteCompanionIndexPrepProducer>.Instance);
        return await producer.PlanAsync(Asset(), SourceKey, CancellationToken.None);
    }

    private static async Task CompleteAsync(RemotePgHarness h, CompanionPrepPlan plan, bool fullyCommitted)
    {
        await using var db = h.NewContext();
        var placement = new RemotePlacement(db, h.Settings, new FixedHeadroom(), h.Time);
        var producer = new RemoteCompanionIndexPrepProducer(
            db, h.Flags, h.Queue(db), placement, new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()), h.Storage, h.Settings,
            NullLogger<RemoteCompanionIndexPrepProducer>.Instance);
        await producer.CompleteAsync(plan, fullyCommitted, CancellationToken.None);
    }

    private static async Task<string> NodeOfferingCompanionAsync(RemotePgHarness h)
    {
        var node = await h.AddNodeAsync(allowedKinds: new[] { RemoteJobKinds.PdfExtract, RemoteJobKinds.CompanionIndexPrep });
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, h.Options)!;
        var kinds = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["kind"] = RemoteJobKinds.CompanionIndexPrep,
                ["schemaVersions"] = new[] { 1 },
                ["engineVersion"] = engine,
            },
        });
        await h.SqlAsync("""UPDATE "RemoteWorkers" SET "KindsJson" = CAST(@k AS jsonb) WHERE "Id" = @id;""", ("k", kinds), ("id", node));
        return node;
    }

    private static async Task<string> EnqueueCompanionAsync(RemotePgHarness h)
        => (await h.EnqueueAsync(kind: RemoteJobKinds.CompanionIndexPrep, settingsHash: CompanionIndexPrepSettings.Hash(50))).JobId;

    private static Task SetJobAsync(
        RemotePgHarness h,
        string jobId,
        string state,
        string? outcome = null,
        string? resultJson = null,
        string? summaryJson = null,
        string? failureCode = null)
        => h.SqlAsync(
            """
            UPDATE "RemoteJobs" SET "State" = @s, "ApplyOutcome" = @o, "ResultJson" = @r,
                "ResultSummaryJson" = CAST(@sum AS jsonb), "FailureCode" = @f
            WHERE "Id" = @id;
            """,
            ("s", state), ("o", outcome), ("r", resultJson), ("sum", summaryJson), ("f", failureCode), ("id", jobId));

    private static string ParkedResult(RemoteJobRow job, Action<Dictionary<string, object?>>? tamper = null)
    {
        var chunks = CompanionChunker.Build(Pages);
        var prep = chunks.Select(c => new CompanionPrepChunk(c.Heading, c.PageNumber ?? 1, c.Text)).ToList();
        var body = new Dictionary<string, object?>
        {
            ["schema"] = "companion.index-prep.result/" + job.SchemaVersion,
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["needsOcr"] = false,
            ["needsOcrReason"] = null,
            ["pageCount"] = Pages.Length,
            ["embeddedChars"] = string.Join("\n\n", Pages).Trim().Length,
            ["version"] = CompanionChunker.ChecksumVersion(Pages),
            ["chunkCount"] = prep.Count,
            ["chunksSha256"] = CompanionIndexPrepValidator.ChunksSha256(prep),
            ["chunks"] = prep.Select(c => new Dictionary<string, object?>
            {
                ["heading"] = c.Heading,
                ["pageNumber"] = c.PageNumber,
                ["text"] = c.Text,
            }).ToArray(),
        };
        tamper?.Invoke(body);
        return JsonSerializer.Serialize(body);
    }

    [PostgreSqlFact]
    public async Task Plan_WithTheFlagOff_IsAlwaysLocal()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await NodeOfferingCompanionAsync(h);
        h.Flags.Set(RemoteJobFlagKeys.Master); // the companion flag stays off

        var plan = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Local, plan.Action);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Plan_EnqueuesOnceAndPolls_WhenAHealthyNodeCanTakeTheWork()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await NodeOfferingCompanionAsync(h);
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);

        var first = await PlanAsync(h);
        var second = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Pending, first.Action);
        Assert.Equal(CompanionPrepAction.Pending, second.Action);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs" WHERE "Kind" = 'companion.index-prep' AND "State" = 'Queued';"""));
    }

    [PostgreSqlFact]
    public async Task Plan_WithNoNodeAvailable_IsLocal_AndEnqueuesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);

        var plan = await PlanAsync(h, headroom: true);

        Assert.Equal(CompanionPrepAction.Local, plan.Action);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobs";"""));
    }

    [PostgreSqlFact]
    public async Task Plan_AValidatedParkedResult_IsReady_AndIsConsumedOnlyAfterACleanWrite()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);
        var jobId = await EnqueueCompanionAsync(h);
        var job = await h.JobAsync(jobId);
        await SetJobAsync(h, jobId, "Succeeded", "Deferred", ParkedResult(job), failureCode: null);

        var plan = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Ready, plan.Action);
        Assert.Equal(jobId, plan.JobId);
        Assert.Equal(CompanionChunker.ChecksumVersion(Pages), plan.Version);
        Assert.Equal(CompanionChunker.Build(Pages).Count, plan.Chunks!.Count);
        Assert.Equal("Page 1", plan.Chunks![0].Heading);
        Assert.Equal(1, plan.Chunks[0].PageNumber);

        // a write with warnings keeps the parked result so the embeddings can be retried without extracting again
        await CompleteAsync(h, plan, fullyCommitted: false);
        Assert.Equal("Deferred", await h.ScalarAsync<string>("""SELECT "ApplyOutcome" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId)));
        Assert.NotNull(await h.ScalarAsync<string>("""SELECT "ResultJson" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId)));

        await CompleteAsync(h, plan, fullyCommitted: true);
        Assert.Equal("Applied", await h.ScalarAsync<string>("""SELECT "ApplyOutcome" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId)));
        Assert.Null(await h.ScalarAsync<string>("""SELECT "ResultJson" FROM "RemoteJobs" WHERE "Id" = @id;""", ("id", jobId)));
    }

    [PostgreSqlFact]
    public async Task Plan_ARealResultWhosePartsNoLongerAddUp_IsNeverTrusted()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);
        var jobId = await EnqueueCompanionAsync(h);
        var job = await h.JobAsync(jobId);
        await SetJobAsync(h, jobId, "Succeeded", "Deferred", ParkedResult(job, body => body["chunksSha256"] = new string('f', 64)));

        var plan = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Local, plan.Action);
    }

    [PostgreSqlFact]
    public async Task Plan_AConsumedResult_RebuildsTheDraftsFromTheCorpus_WithoutAnotherExtraction()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);
        var jobId = await EnqueueCompanionAsync(h);
        var version = CompanionChunker.ChecksumVersion(Pages);
        await SetJobAsync(h, jobId, "Succeeded", "Applied", null, "{\"version\":\"" + version + "\"}");

        // no corpus rows yet: nothing to rebuild from, so the local path decides
        Assert.Equal(CompanionPrepAction.Local, (await PlanAsync(h)).Action);

        var sourceId = Guid.NewGuid();
        await h.SqlAsync(
            """INSERT INTO "CompanionSources" ("Id", "SourceKey", "Version") VALUES (@id, @key, @version);""",
            ("id", sourceId), ("key", SourceKey), ("version", version));
        var ordinal = 0;
        foreach (var chunk in CompanionChunker.Build(Pages))
        {
            await h.SqlAsync(
                """
                INSERT INTO "CompanionChunks" ("SourceId", "Ordinal", "Heading", "Text", "PageNumber")
                VALUES (@source, @ordinal, @heading, @text, @page);
                """,
                ("source", sourceId), ("ordinal", ordinal++), ("heading", chunk.Heading), ("text", chunk.Text), ("page", chunk.PageNumber));
        }

        var plan = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Ready, plan.Action);
        Assert.Null(plan.JobId);
        Assert.Equal(version, plan.Version);
        Assert.Equal(CompanionChunker.Build(Pages).Select(c => c.Text).ToArray(), plan.Chunks!.Select(c => c.Text).ToArray());
    }

    [PostgreSqlFact]
    public async Task Plan_NeedsOcrAndDeterministicRejectionsGoLocal_ButAPoisonSuspectNeverDoes()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);
        var jobId = await EnqueueCompanionAsync(h);

        await SetJobAsync(h, jobId, "Succeeded", "NoOp");
        Assert.Equal(CompanionPrepAction.Local, (await PlanAsync(h)).Action);

        await SetJobAsync(h, jobId, "Failed", failureCode: "content_rejected");
        Assert.Equal(CompanionPrepAction.Local, (await PlanAsync(h)).Action);

        await SetJobAsync(h, jobId, "Failed", failureCode: "extract_exception");
        var skipped = await PlanAsync(h);
        Assert.Equal(CompanionPrepAction.Skip, skipped.Action);
        Assert.Contains("extract_exception", skipped.Note, StringComparison.Ordinal);

        await SetJobAsync(h, jobId, "Quarantined");
        Assert.Equal(CompanionPrepAction.Skip, (await PlanAsync(h)).Action);

        await SetJobAsync(h, jobId, "FallbackLocal");
        Assert.Equal(CompanionPrepAction.Local, (await PlanAsync(h)).Action);
    }

    [PostgreSqlFact]
    public async Task Plan_AParkedResultThatRetentionAlreadyCleared_AsksForAFreshRun()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await NodeOfferingCompanionAsync(h);
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.CompanionIndexPrep);
        var jobId = await EnqueueCompanionAsync(h);
        await SetJobAsync(h, jobId, "Succeeded", "Deferred", resultJson: null);

        var plan = await PlanAsync(h);

        Assert.Equal(CompanionPrepAction.Pending, plan.Action);
        Assert.Equal("Queued", await h.StateOfAsync(jobId));
    }
}
