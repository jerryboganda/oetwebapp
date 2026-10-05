using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// The <c>companion.index-prep</c> producer and consumer (OET-RWP/1 section 6.2), used by <c>CompanionDocumentIndexer</c>. It turns the
/// indexer into enqueue-and-poll for material PDFs: the first reindex enqueues jobs, later ones consume the parked, VALIDATED results.
/// Embeddings (an AI call), the corpus guard and the hash-gated commit never leave the API, and a result is only ever consumed after
/// the same size, hash, character and chunk-integrity checks the completion endpoint applies. With the flag off, a provider other than
/// PdfPig, no healthy node, or any doubt, the answer is <c>Local</c> and the indexer extracts in-process as it always did.
/// </summary>
public sealed class RemoteCompanionIndexPrepProducer(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemotePlacement placement,
    IRuntimeSettingsProvider runtimeSettings,
    IFileStorage storage,
    RemoteJobsSettings settings,
    ILogger<RemoteCompanionIndexPrepProducer> logger) : IRemoteCompanionIndexPrep
{
    public const string Enqueuer = "system:companion-index";

    public async Task<CompanionPrepPlan> PlanAsync(MediaAsset asset, string sourceKey, CancellationToken ct)
    {
        try
        {
            return await PlanCoreAsync(asset, sourceKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The remote path may never make a reindex worse: any failure means "extract locally".
            logger.LogWarning(ex, "Remote companion index planning failed for asset {AssetId}; extracting locally.", asset.Id);
            return CompanionPrepPlan.UseLocal;
        }
    }

    private async Task<CompanionPrepPlan> PlanCoreAsync(MediaAsset asset, string sourceKey, CancellationToken ct)
    {
        var flagSnapshot = await flags.GetAsync(ct);
        if (!flagSnapshot.KindEnabled(RemoteJobKinds.CompanionIndexPrep, RemoteJobPurpose.Apply)) return CompanionPrepPlan.UseLocal;

        var options = settings.Current;
        var spec = RemoteJobKinds.Find(RemoteJobKinds.CompanionIndexPrep);
        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.CompanionIndexPrep, options);
        if (spec is null || engine is null) return CompanionPrepPlan.UseLocal;
        if (!string.Equals(asset.Format, "pdf", StringComparison.OrdinalIgnoreCase)) return CompanionPrepPlan.UseLocal;
        if (asset.SizeBytes > spec.Limits.MaxInputBytes) return CompanionPrepPlan.UseLocal;

        var current = (await runtimeSettings.GetAsync(ct)).PdfExtraction;
        var provider = (current.Provider ?? "auto").Trim().ToLowerInvariant();
        if (provider is not ("auto" or "pdfpig")) return CompanionPrepPlan.UseLocal;

        var sha = await EnsureSha256Async(asset, ct);
        if (sha is null) return CompanionPrepPlan.UseLocal;

        var settingsHash = CompanionIndexPrepSettings.Hash(current.MinTextLengthForSuccess);
        var key = RemoteJobKeys.IdempotencyKey(
            RemoteJobKinds.CompanionIndexPrep, RemoteJobPurpose.Apply, "MediaAsset", asset.Id, sha, engine, settingsHash);
        var job = await queue.FindByKeyAsync(key, ct);

        if (job is null)
        {
            var decision = await placement.DecideAsync(RemoteJobKinds.CompanionIndexPrep, spec.Limits.Weight, ct);
            if (decision != RemotePlacementDecision.Remote) return CompanionPrepPlan.UseLocal;

            await EnqueueAsync(asset, sha, engine, settingsHash, current.MinTextLengthForSuccess, spec, force: false, ct);
            return new CompanionPrepPlan(CompanionPrepAction.Pending, "queued for remote extraction");
        }

        switch (job.State)
        {
            case RemoteJobState.Queued:
            case RemoteJobState.Leased:
                return new CompanionPrepPlan(CompanionPrepAction.Pending, "remote extraction in progress");

            case RemoteJobState.Succeeded:
                return await ConsumeAsync(job, asset, sourceKey, sha, engine, settingsHash, current.MinTextLengthForSuccess, spec, ct);

            case RemoteJobState.Failed:
                return RemoteFailCodes.AllowsLocalFallback(job.FailureCode)
                    ? CompanionPrepPlan.UseLocal
                    : new CompanionPrepPlan(CompanionPrepAction.Skip, $"remote extraction failed ({job.FailureCode}); an admin must requeue or force it local");

            case RemoteJobState.Quarantined:
                // Never auto-run a poison PDF on the primary.
                return new CompanionPrepPlan(CompanionPrepAction.Skip, "remote extraction is quarantined; an admin must requeue or force it local");

            default:
                // FallbackLocal, Cancelled: the local path owns it.
                return CompanionPrepPlan.UseLocal;
        }
    }

    private async Task<CompanionPrepPlan> ConsumeAsync(
        RemoteJobRow job,
        MediaAsset asset,
        string sourceKey,
        string sha,
        string engine,
        string settingsHash,
        int minTextLength,
        RemoteKindSpec spec,
        CancellationToken ct)
    {
        switch (job.ApplyOutcome)
        {
            case "NoOp":
                // needsOcr (OCR tiers live only on the primary) or an unusable result: extract locally.
                return CompanionPrepPlan.UseLocal;

            case "Deferred" when job.ResultJson is not null:
            {
                if (!CompanionIndexPrepValidator.TryParse(job.ResultJson, out var parsed, out _) || parsed is null
                    || !CompanionIndexPrepValidator.Validate(job, parsed).IsOk)
                {
                    logger.LogWarning("Parked companion result of job {JobId} failed re-validation; extracting locally.", job.Id);
                    return CompanionPrepPlan.UseLocal;
                }

                var drafts = parsed.Chunks.Select(chunk => new CompanionChunkDraft(chunk.Heading, chunk.Text, PageNumber: chunk.PageNumber)).ToList();
                return new CompanionPrepPlan(CompanionPrepAction.Ready, null, job.Id, parsed.Version, drafts);
            }

            case "Deferred":
            {
                // The retention window cleared the parked result before it was consumed: ask for a fresh run.
                await EnqueueAsync(asset, sha, engine, settingsHash, minTextLength, spec, force: true, ct);
                return new CompanionPrepPlan(CompanionPrepAction.Pending, "queued for remote extraction");
            }

            case "Applied":
            {
                // Consumed earlier: the corpus rows ARE the result. Rebuild the drafts from them so the source's metadata
                // (title, entitlement scope) is refreshed and missing embeddings are retried, with no new extraction.
                var rebuilt = await RebuildFromCorpusAsync(job, sourceKey, ct);
                return rebuilt ?? CompanionPrepPlan.UseLocal;
            }

            default:
                return CompanionPrepPlan.UseLocal;
        }
    }

    private async Task<CompanionPrepPlan?> RebuildFromCorpusAsync(RemoteJobRow job, string sourceKey, CancellationToken ct)
    {
        string? version = null;
        if (job.ResultSummaryJson is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(job.ResultSummaryJson);
                if (document.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String) version = v.GetString();
            }
            catch (JsonException)
            {
                // No usable summary: fall through to the local path.
            }
        }

        if (version is null) return null;

        var source = await db.CompanionSources.AsNoTracking()
            .Where(s => s.SourceKey == sourceKey && s.Version == version)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync(ct);
        if (source is null) return null;

        var rows = await db.CompanionChunks.AsNoTracking()
            .Where(c => c.SourceId == source.Id)
            .OrderBy(c => c.Ordinal)
            .Select(c => new { c.Heading, c.Text, c.PageNumber, c.TimestampSeconds })
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        var drafts = rows.Select(r => new CompanionChunkDraft(r.Heading ?? string.Empty, r.Text, r.PageNumber, r.TimestampSeconds)).ToList();
        return new CompanionPrepPlan(CompanionPrepAction.Ready, null, null, version, drafts);
    }

    public async Task CompleteAsync(CompanionPrepPlan plan, bool fullyCommitted, CancellationToken ct)
    {
        if (plan.JobId is null || !fullyCommitted) return;

        await RemoteDb.ExecuteAsync(
            db,
            """
            UPDATE "RemoteJobs" SET "ResultJson" = NULL, "ApplyOutcome" = 'Applied', "UpdatedAt" = clock_timestamp()
            WHERE "Id" = @id AND "State" = 'Succeeded' AND "ApplyOutcome" = 'Deferred';
            """,
            parameters => parameters.AddWithValue("id", plan.JobId),
            ct);
    }

    private async Task EnqueueAsync(
        MediaAsset asset,
        string sha,
        string engine,
        string settingsHash,
        int minTextLength,
        RemoteKindSpec spec,
        bool force,
        CancellationToken ct)
    {
        var inputsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "pdf",
                ["sizeBytes"] = asset.SizeBytes,
                ["sha256"] = sha,
                ["contentType"] = "application/pdf",
                ["storageKey"] = asset.StoragePath,
            },
        });

        await queue.EnqueueAsync(
            new RemoteEnqueueRequest(
                RemoteJobKinds.CompanionIndexPrep,
                spec.SchemaVersion,
                RemoteJobPurpose.Apply,
                "MediaAsset",
                asset.Id,
                sha,
                engine,
                settingsHash,
                CompanionIndexPrepSettings.ParamsJson(minTextLength),
                inputsJson,
                spec.Limits,
                Enqueuer),
            force,
            ct);
    }

    private async Task<string?> EnsureSha256Async(MediaAsset asset, CancellationToken ct)
    {
        if (RemoteIds.IsSha256Hex(asset.Sha256)) return asset.Sha256;

        try
        {
            await using var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
            var (_, sha) = await StreamingSha256.ComputeAsync([stream], null, ct);
            var mediaId = asset.Id;
            await db.MediaAssets
                .Where(m => m.Id == mediaId && m.Sha256 == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Sha256, _ => sha), ct);
            return sha;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not hash media asset {AssetId}; it stays on the local indexing path.", asset.Id);
            return null;
        }
    }
}
