using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The fenced, single-transaction completion (OET-RWP/1 section 4.5) and the data plane that feeds it (sections 4.3 and 4.4), on
/// real PostgreSQL. RW-045, RW-046, RW-064, RW-070 to RW-074, RW-079, RW-081, RW-082, RW-089, RW-090, RW-111.
/// </summary>
[Collection(PostgreSqlExclusiveCollection.Name)]
public sealed class RemoteCompletionPostgresTests
{
    private static async Task<(string Node, RemoteJobRow Job)> LeasedPdfJobAsync(RemotePgHarness h, long size = 1234)
    {
        await h.SeedPdfAsync(size: size);
        var node = await h.AddNodeAsync();
        await h.EnqueueAsync(size: size);
        var job = await h.ClaimOneAsync(node);
        return (node, job);
    }

    private static string ExpectedFlat() => string.Join("\n\n", RemotePgHarness.SamplePages).Trim();

    private static async Task<string?> AssetTextAsync(RemotePgHarness h, string key = "asset-1")
    {
        var json = await h.PaperJsonAsync();
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
    }

    // ── success, replay, conflict ────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Complete_AppliesTheResultAndSettlesTheJobInOneTransaction()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job)));

        Assert.Null(outcome.Error);
        Assert.Equal("succeeded", outcome.Status);
        Assert.Equal("Applied", outcome.Outcome);
        Assert.False(outcome.Replayed);

        var row = await h.JobAsync(job.Id);
        Assert.Equal("Succeeded", row.State);
        Assert.Equal(1, row.SettledFence);
        Assert.Equal(node, row.SettledBy);
        Assert.Equal("Applied", row.ApplyOutcome);
        Assert.Null(row.LeaseOwner);
        Assert.NotNull(row.CompletedAt);
        Assert.NotNull(row.ResultSummaryJson);

        Assert.Equal(ExpectedFlat(), await AssetTextAsync(h));
        Assert.Equal(4, await h.PaperVersionAsync()); // seeded at 3: the single writer bumps RowVersion by one
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Applied"));
    }

    [PostgreSqlFact]
    public async Task Complete_TheSameResultAgain_ReplaysTheStoredOutcome_WithoutRunningTheApplierTwice()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        var body = RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job));
        await h.CompleteAsync(node, job, body);

        var replay = await h.CompleteAsync(node, job, body);

        Assert.Null(replay.Error);
        Assert.Equal("succeeded", replay.Status);
        Assert.Equal("Applied", replay.Outcome);
        Assert.True(replay.Replayed);
        Assert.Equal(4, await h.PaperVersionAsync());
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Applied"));
    }

    [PostgreSqlFact]
    public async Task Complete_ADifferentResultForTheSameFence_IsAConflict_AndChangesNothing()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job)));
        var other = new[]
        {
            "A completely different first page that is long enough to clear the minimum text threshold.",
            "A completely different second page, also long enough to be real extracted text content.",
        };

        var conflict = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job, other)));

        Assert.Equal(409, conflict.Error!.StatusCode);
        Assert.Equal("result_conflict", conflict.Error.Code);
        Assert.Equal(ExpectedFlat(), await AssetTextAsync(h));
        Assert.Equal(4, await h.PaperVersionAsync());
    }

    // ── zombies and expiry ───────────────────────────────────────────────────

    [PostgreSqlFact]
    public async Task Complete_AZombieOfAnEarlierFence_CannotWriteAnything()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (zombie, job) = await LeasedPdfJobAsync(h);
        await h.ExpireLeaseAsync(job.Id);
        await using (var db = h.NewContext())
        {
            await h.Sweeper(db).ReapExpiredAsync(h.Settings.Current, CancellationToken.None);
        }

        await h.MakeDueAsync(job.Id);
        var successor = await h.AddNodeAsync();
        var reclaimed = await h.ClaimOneAsync(successor);
        Assert.Equal(2, reclaimed.FenceToken);

        var late = await h.CompleteAsync(zombie, job, RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job)));

        Assert.Equal(409, late.Error!.StatusCode);
        Assert.Equal("lease_lost", late.Error.Code);
        Assert.Equal("superseded", late.Error.Reason);
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));
        Assert.Null(await AssetTextAsync(h));
        Assert.Equal(3, await h.PaperVersionAsync());

        // the legitimate holder completes normally
        var ok = await h.CompleteAsync(successor, reclaimed, RemotePgHarness.CompleteBody(2, RemotePgHarness.PdfResultJson(reclaimed)));
        Assert.Equal("Applied", ok.Outcome);
        Assert.Equal(ExpectedFlat(), await AssetTextAsync(h));
    }

    [PostgreSqlFact]
    public async Task Complete_AnExpiredLeaseThatNoReaperHasSeenYet_IsStillRefused()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        await h.ExpireLeaseAsync(job.Id);

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job)));

        Assert.Equal("lease_lost", outcome.Error!.Code);
        Assert.Equal("expired", outcome.Error.Reason);
        Assert.Equal("Leased", await h.StateOfAsync(job.Id));
        Assert.Null(await AssetTextAsync(h));
    }

    // ── the applier decides, the transaction contains it ─────────────────────

    private sealed class ThrowingHandler : IRemoteKindHandler
    {
        public string Kind => RemoteJobKinds.PdfExtract;

        public RemoteResultValidation Validate(
            RemoteJobRow job,
            string resultJson,
            IReadOnlyList<RemoteOutputRow> outputs,
            RemoteJobsOptions options)
            => RemoteResultValidation.Ok(new object(), "{}");

        public Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct)
            => throw new InvalidOperationException("the applier failed after the job was settled");
    }

    [PostgreSqlFact]
    public async Task Complete_AnApplierThatThrows_RollsTheSettlementBack_AndTheCompletionCanBeRetried()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        var body = RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job));

        var failed = await h.CompleteAsync(node, job, body, new IRemoteKindHandler[] { new ThrowingHandler() });

        Assert.Equal(503, failed.Error!.StatusCode);
        Assert.Equal("apply_failed", failed.Error.Code);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Leased", row.State);          // the compare-and-set was rolled back with the applier
        Assert.Null(row.SettledFence);
        Assert.Null(row.ResultSha256);
        Assert.Null(await AssetTextAsync(h));

        var retried = await h.CompleteAsync(node, job, body);
        Assert.Equal("Applied", retried.Outcome);
        Assert.Equal(ExpectedFlat(), await AssetTextAsync(h));
    }

    [PostgreSqlFact]
    public async Task Complete_AnAssetThatChangedSinceTheJobWasCreated_IsDiscardedAsStale_NotApplied()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        await h.SqlAsync("""UPDATE "MediaAssets" SET "Sha256" = @sha WHERE "Id" = 'media-1';""", ("sha", new string('9', 64)));
        var body = RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job));

        var outcome = await h.CompleteAsync(node, job, body);

        Assert.Equal(409, outcome.Error!.StatusCode);
        Assert.Equal("stale_input", outcome.Error.Code);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Cancelled", row.State);
        Assert.Equal("Discarded", row.ApplyOutcome);
        Assert.Equal("stale_input", row.FailureCode);
        Assert.Null(await AssetTextAsync(h));
        Assert.Equal(3, await h.PaperVersionAsync());
        Assert.Equal(1, await h.AuditCountAsync("RemoteJob.Discarded"));

        // a retried completion replays the same discard
        var replay = await h.CompleteAsync(node, job, body);
        Assert.Equal("stale_input", replay.Error!.Code);
    }

    [PostgreSqlFact]
    public async Task Complete_NeedsOcr_IsANoOp_TheLocalPathKeepsTheAsset()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schema"] = "pdf.extract.result/1",
            ["engineVersion"] = job.EngineVersion,
            ["inputSha256"] = job.InputSha256,
            ["mode"] = "flat",
            ["needsOcr"] = true,
            ["needsOcrReason"] = "below_min_text",
            ["pageCount"] = 3,
            ["embeddedChars"] = 4,
            ["textSha256"] = null,
            ["pagesSha256"] = null,
            ["pageSha256s"] = Array.Empty<string>(),
            ["pages"] = null,
        });

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(1, json));

        Assert.Equal("succeeded", outcome.Status);
        Assert.Equal("NoOp", outcome.Outcome);
        Assert.Null(await AssetTextAsync(h));
        Assert.Equal(3, await h.PaperVersionAsync());
    }

    [PostgreSqlFact]
    public async Task Complete_StillWorksForAnInFlightLease_WhenTheMasterSwitchIsTurnedOff()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        h.Flags.Set(); // master (and every kind flag) off after the lease was granted

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job)));

        Assert.Equal("Applied", outcome.Outcome);
        Assert.Equal(ExpectedFlat(), await AssetTextAsync(h));
    }

    [PostgreSqlFact]
    public async Task Complete_WithApplicationsFrozen_RefusesToWrite_AndWithdrawsTheJob()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        h.Flags.Set(RemoteJobFlagKeys.Master, RemoteJobFlagKeys.PdfExtract, RemoteJobFlagKeys.FreezeApplies);

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job)));

        Assert.Equal(503, outcome.Error!.StatusCode);
        Assert.Equal("applies_frozen", outcome.Error.Code);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Cancelled", row.State);
        Assert.Equal("applies_frozen", row.FailureCode);
        Assert.Null(await AssetTextAsync(h));
    }

    // ── untrusted results ────────────────────────────────────────────────────

    private static async Task<int> StrikesAsync(RemotePgHarness h, string node)
        => await h.ScalarAsync<int>("""SELECT "IntegrityStrikes" FROM "RemoteWorkers" WHERE "Id" = @id;""", ("id", node));

    [PostgreSqlFact]
    public async Task Complete_AHashThatDoesNotMatchTheBody_IsRejectedWithAStrike_AndTheJobIsRequeued()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        var body = RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job), sha: new string('0', 64));

        var outcome = await h.CompleteAsync(node, job, body);

        Assert.Equal(422, outcome.Error!.StatusCode);
        Assert.Equal("result_hash_mismatch", outcome.Error.Code);
        Assert.Equal("Queued", await h.StateOfAsync(job.Id));
        Assert.Equal(1, await StrikesAsync(h, node));
        Assert.Null(await AssetTextAsync(h));
    }

    [PostgreSqlFact]
    public async Task Complete_AStructurallyInvalidResult_IsAStrike_ButForbiddenCharactersAreNot()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);

        var invalid = await h.CompleteAsync(
            node, job, RemotePgHarness.CompleteBody(1, RemotePgHarness.PdfResultJson(job, tamper: b => b["pageCount"] = 9)));
        Assert.Equal("result_invalid", invalid.Error!.Code);
        Assert.Equal(1, await StrikesAsync(h, node));
        Assert.Equal("Queued", await h.StateOfAsync(job.Id));

        await h.MakeDueAsync(job.Id);
        var again = await h.ClaimOneAsync(node);
        var dirty = new[]
        {
            "Clean first page with plenty of ordinary characters to pass the minimum text threshold.",
            "Second page has a control character \u0001 inside it but is otherwise ordinary text here.",
        };
        var rejected = await h.CompleteAsync(
            node, again, RemotePgHarness.CompleteBody(again.FenceToken, RemotePgHarness.PdfResultJson(again, dirty)));

        Assert.Null(rejected.Error);
        Assert.Equal("rejected", rejected.Status);
        Assert.Equal(RemoteFailCodes.ContentRejected, rejected.Code);
        Assert.Equal(1, await StrikesAsync(h, node)); // a content rejection is not the node's fault
        var row = await h.JobAsync(again.Id);
        Assert.Equal("Failed", row.State);
        Assert.Equal(RemoteFailCodes.ContentRejected, row.FailureCode);
        Assert.Null(await AssetTextAsync(h));

        var replay = await h.CompleteAsync(
            node, again, RemotePgHarness.CompleteBody(again.FenceToken, RemotePgHarness.PdfResultJson(again, dirty)));
        Assert.Equal("rejected", replay.Status);
        Assert.True(replay.Replayed);
    }

    [PostgreSqlFact]
    public async Task Complete_DeclaredOutputsOnAKindThatTakesNone_AreRejected()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);
        var resultJson = RemotePgHarness.PdfResultJson(job);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fence = 1,
            resultSha256 = RemoteIds.Sha256Hex(resultJson),
            resultJson,
            outputs = new[] { new { name = "audio.m4a", sizeBytes = 3, sha256 = RemoteTestData.Sha } },
        });

        var outcome = await h.CompleteAsync(node, job, body);

        Assert.Equal(422, outcome.Error!.StatusCode);
        Assert.Equal("result_invalid", outcome.Error.Code);
        Assert.Null(await AssetTextAsync(h));
    }

    [PostgreSqlFact]
    public async Task ThreeStrikesInTheWindow_QuarantineTheNode_AndReleaseItsLeasesWithARefund()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var (node, job) = await LeasedPdfJobAsync(h);

        var results = new List<RemoteStrikeResult>();
        for (var i = 0; i < 3; i++)
        {
            await using var db = h.NewContext();
            results.Add(await RemoteNodeOps.AddStrikeAsync(db, node, "test", h.Settings.Current, DateTimeOffset.UtcNow, CancellationToken.None));
        }

        Assert.Equal(new[] { 1, 2, 3 }, results.Select(r => r.Strikes).ToArray());
        Assert.Equal(new[] { false, false, true }, results.Select(r => r.Quarantined).ToArray());
        Assert.Equal("Quarantined", (await h.NodeAsync(node)).Status);

        var row = await h.JobAsync(job.Id);
        Assert.Equal("Queued", row.State);
        Assert.Equal(0, row.Attempt);        // refunded: the node was at fault, not the job
        Assert.Equal(1, row.ReleaseCount);
        Assert.Equal("node_quarantined", row.FailureCode);
        Assert.Equal(1, await h.AuditCountAsync("RemoteWorker.Quarantine"));
    }

    // ── data plane: inputs ───────────────────────────────────────────────────

    private sealed class ProbingStorage : IFileStorage
    {
        private readonly IFileStorage _inner;
        private readonly Func<string> _probe;

        public ProbingStorage(IFileStorage inner, Func<string> probe)
        {
            _inner = inner;
            _probe = probe;
        }

        /// <summary>The state of the database connection at the instant the storage object was opened.</summary>
        public string? ObservedConnectionState { get; private set; }

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => _inner.WriteAsync(key, source, ct);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => _inner.OpenReadAsync(key, ct);

        public async Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
        {
            ObservedConnectionState = _probe();
            var stream = await _inner.OpenReadAsync(key, ct);
            return new FileStorageReadResult(stream, stream.Length);
        }

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => _inner.OpenWriteAsync(key, ct);

        public Task<bool> ExistsAsync(string key, CancellationToken ct) => _inner.ExistsAsync(key, ct);

        public Task<bool> DeleteAsync(string key, CancellationToken ct) => _inner.DeleteAsync(key, ct);

        public Task<long> LengthAsync(string key, CancellationToken ct) => _inner.LengthAsync(key, ct);

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct) => _inner.MoveAsync(sourceKey, destKey, overwrite, ct);

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => _inner.DeletePrefixAsync(prefix, ct);

        public string? TryResolveLocalPath(string key) => _inner.TryResolveLocalPath(key);

        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => _inner.ResolveReadUrl(key, ttl);
    }

    private static async Task<byte[]> StoreSamplePdfAsync(RemotePgHarness h, int size)
    {
        var bytes = new byte[size];
        new Random(7).NextBytes(bytes);
        await h.Storage.WriteAsync("media/sample.pdf", new MemoryStream(bytes), CancellationToken.None);
        return bytes;
    }

    [PostgreSqlFact]
    public async Task OpenInput_HoldsNoDatabaseConnectionWhileTheBytesAreOpened()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var bytes = await StoreSamplePdfAsync(h, 2048);
        var (node, job) = await LeasedPdfJobAsync(h, size: 2048);
        await using var db = h.NewContext();
        var probe = new ProbingStorage(h.Storage, () => db.Database.GetDbConnection().State.ToString());

        var opened = await h.IO(db, probe).OpenInputAsync(node, job.Id, job.FenceToken, "pdf", headOnly: false, CancellationToken.None);

        Assert.Null(opened.Error);
        Assert.NotNull(opened.Stream);
        Assert.Equal(2048, opened.Length);
        Assert.Equal(RemoteTestData.Sha, opened.Sha256);
        Assert.Equal("Closed", probe.ObservedConnectionState);

        await using var stream = opened.Stream!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [PostgreSqlFact]
    public async Task OpenInput_AHeadRequestOpensNoStream_AndAnUnknownNameIs404()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await StoreSamplePdfAsync(h, 512);
        var (node, job) = await LeasedPdfJobAsync(h, size: 512);
        await using var db = h.NewContext();

        var head = await h.IO(db).OpenInputAsync(node, job.Id, job.FenceToken, "pdf", headOnly: true, CancellationToken.None);
        var unknown = await h.IO(db).OpenInputAsync(node, job.Id, job.FenceToken, "nope", headOnly: false, CancellationToken.None);

        Assert.Null(head.Error);
        Assert.Null(head.Stream);
        Assert.Equal(512, head.Length);
        Assert.Equal("input_not_found", unknown.Error!.Code);
    }

    [PostgreSqlFact]
    public async Task OpenInput_AManifestThatNoLongerMatchesTheObject_CancelsTheJobSoItIsReEnqueued()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await StoreSamplePdfAsync(h, 2048);
        var (node, job) = await LeasedPdfJobAsync(h, size: 999); // the manifest says 999, the object has 2048
        await using var db = h.NewContext();

        var opened = await h.IO(db).OpenInputAsync(node, job.Id, job.FenceToken, "pdf", headOnly: false, CancellationToken.None);

        Assert.Equal(409, opened.Error!.StatusCode);
        Assert.Equal("stale_input", opened.Error.Code);
        Assert.Null(opened.Stream);
        var row = await h.JobAsync(job.Id);
        Assert.Equal("Cancelled", row.State);
        Assert.Equal("stale_input", row.FailureCode);
    }

    [PostgreSqlFact]
    public async Task OpenInput_AnExpiredLeaseCannotReadTheInput()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        await StoreSamplePdfAsync(h, 512);
        var (node, job) = await LeasedPdfJobAsync(h, size: 512);
        await h.ExpireLeaseAsync(job.Id);
        await using var db = h.NewContext();

        var opened = await h.IO(db).OpenInputAsync(node, job.Id, job.FenceToken, "pdf", headOnly: false, CancellationToken.None);

        Assert.Equal("lease_lost", opened.Error!.Code);
        Assert.Equal("expired", opened.Error.Reason);
        Assert.Null(opened.Stream);
    }

    // ── data plane: outputs ──────────────────────────────────────────────────

    private static async Task<RemoteOutputPut> PutAsync(
        RemotePgHarness h,
        string node,
        string jobId,
        string name,
        byte[] bytes,
        string? declaredSha = null,
        long? declaredLength = null,
        long fence = 1)
    {
        await using var db = h.NewContext();
        return await h.IO(db).PutOutputAsync(
            node,
            jobId,
            fence,
            name,
            declaredSha ?? RemoteIds.Sha256Hex(bytes),
            declaredLength ?? bytes.LongLength,
            new MemoryStream(bytes),
            CancellationToken.None);
    }

    [PostgreSqlFact]
    public async Task PutOutput_StoresTheObjectUnderThePerJobKey_VerifiesItsHash_AndReplacesByName()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var jobId = await h.InsertLeasedJobAsync(node);
        var first = new byte[] { 1, 2, 3, 4 };
        var second = new byte[] { 9, 8, 7 };

        var stored = await PutAsync(h, node, jobId, "audio.m4a", first);

        Assert.Null(stored.Error);
        Assert.Equal(4, stored.SizeBytes);
        Assert.Equal(RemoteIds.Sha256Hex(first), stored.Sha256);
        Assert.False(stored.Replaced);
        Assert.True(await h.Storage.ExistsAsync(RemoteInputOutputService.OutputKey(jobId, 1, "audio.m4a"), CancellationToken.None));
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));

        var replaced = await PutAsync(h, node, jobId, "audio.m4a", second);
        Assert.Null(replaced.Error);
        Assert.True(replaced.Replaced);
        Assert.Equal(1, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
        Assert.Equal(
            RemoteIds.Sha256Hex(second),
            await h.ScalarAsync<string>("""SELECT "Sha256" FROM "RemoteJobOutputs" WHERE "Name" = 'audio.m4a';"""));
    }

    [PostgreSqlFact]
    public async Task PutOutput_ABodyThatDoesNotMatchItsDeclaredHash_LeavesNoObjectAndNoRow()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var jobId = await h.InsertLeasedJobAsync(node);

        var result = await PutAsync(h, node, jobId, "bad.m4a", new byte[] { 1, 2, 3 }, declaredSha: new string('0', 64));

        Assert.Equal(422, result.Error!.StatusCode);
        Assert.Equal("output_hash_mismatch", result.Error.Code);
        Assert.False(await h.Storage.ExistsAsync(RemoteInputOutputService.OutputKey(jobId, 1, "bad.m4a"), CancellationToken.None));
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));

        var shortBody = await PutAsync(h, node, jobId, "short.m4a", new byte[] { 1, 2, 3 }, declaredLength: 10);
        Assert.Equal("output_hash_mismatch", shortBody.Error!.Code);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    [PostgreSqlFact]
    public async Task PutOutput_IsRefusedForAKindThatTakesNoOutputs_ABadName_AndAnExpiredLease()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync();
        var pdfJob = await h.InsertLeasedJobAsync(node, RemoteJobKinds.PdfExtract);
        var mediaJob = await h.InsertLeasedJobAsync(node);

        var pdf = await PutAsync(h, node, pdfJob, "x.bin", new byte[] { 1 });
        Assert.Equal(403, pdf.Error!.StatusCode);
        Assert.Equal("outputs_not_permitted", pdf.Error.Code);

        var badName = await PutAsync(h, node, mediaJob, "../escape", new byte[] { 1 });
        Assert.Equal(400, badName.Error!.StatusCode);

        await h.ExpireLeaseAsync(mediaJob);
        var expired = await PutAsync(h, node, mediaJob, "late.m4a", new byte[] { 1 });
        Assert.Equal("lease_lost", expired.Error!.Code);
        Assert.Equal(0, await h.CountAsync("""SELECT COUNT(*)::int FROM "RemoteJobOutputs";"""));
    }

    // ── a canary that really extracts ────────────────────────────────────────

    private static async Task<IReadOnlyList<string>> OraclePagesAsync()
    {
        var extractor = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        await using var stream = RemoteCanary.OpenRead();
        return await extractor.ExtractPagesAsync(stream, CancellationToken.None);
    }

    [PostgreSqlFact]
    public async Task Canary_AMatchingKnownAnswer_SetsTheNodesCanaryFlag_AndTheEmbeddedInputIsServed()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(RemoteNodeStatus.Probation);
        await using (var db = h.NewContext())
        {
            var (jobId, error) = await h.Nodes(db).EnqueueCanaryAsync(node, "owner", "owner", CancellationToken.None);
            Assert.Null(error);
            Assert.NotNull(jobId);
        }

        var job = await h.ClaimOneAsync(node);
        Assert.Equal(RemoteJobPurpose.Canary, job.Purpose);

        await using (var db = h.NewContext())
        {
            var opened = await h.IO(db).OpenInputAsync(node, job.Id, job.FenceToken, "pdf", headOnly: false, CancellationToken.None);
            Assert.Null(opened.Error);
            Assert.Equal(RemoteCanary.PdfSize, opened.Length);
            Assert.Equal(RemoteCanary.PdfSha256, opened.Sha256);
            await using var stream = opened.Stream!;
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal(RemoteCanary.PdfBytes, copy.ToArray());
        }

        var pages = await OraclePagesAsync();
        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job, pages, includePages: false)));

        Assert.Null(outcome.Error);
        Assert.Equal("Applied", outcome.Outcome);
        var after = await h.NodeAsync(node);
        Assert.True(after.LastCanaryOk);
        Assert.NotNull(after.LastCanaryAt);
        Assert.Equal(RemoteNodeStatus.Probation, after.Status); // passing the canary does not itself activate the node
    }

    [PostgreSqlFact]
    public async Task Canary_AMismatch_QuarantinesTheNodeAtOnce()
    {
        await using var h = await RemotePgHarness.CreateAsync();
        var node = await h.AddNodeAsync(RemoteNodeStatus.Probation);
        await using (var db = h.NewContext())
        {
            await h.Nodes(db).EnqueueCanaryAsync(node, "owner", "owner", CancellationToken.None);
        }

        var job = await h.ClaimOneAsync(node);
        var wrongPages = new[] { new string('x', 200) };

        var outcome = await h.CompleteAsync(node, job, RemotePgHarness.CompleteBody(job.FenceToken, RemotePgHarness.PdfResultJson(job, wrongPages, includePages: false)));

        Assert.Null(outcome.Error);
        var after = await h.NodeAsync(node);
        Assert.False(after.LastCanaryOk);
        Assert.Equal(RemoteNodeStatus.Quarantined, after.Status);
        Assert.Equal(1, await h.AuditCountAsync("RemoteWorker.Quarantine"));
    }
}
