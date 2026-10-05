using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>What the extraction worker should do with a paper after the remote producer looked at it.</summary>
public enum RemoteExtractionDecision
{
    /// <summary>Run the existing in-process extraction for this paper (exactly today's behaviour).</summary>
    Local,

    /// <summary>Remote jobs own every uncached PDF of this paper for now: skip the local pass this tick.</summary>
    Remote,

    /// <summary>Nothing may be done this tick (waiting for headroom, or a terminal job an admin must act on).</summary>
    Skip,
}

public interface IRemotePdfExtractionProducer
{
    /// <summary>
    /// Decides, per paper, whether its uncached PDFs are extracted remotely. With the flags off, no healthy node, or any
    /// doubt, the answer is <see cref="RemoteExtractionDecision.Local"/>: the local path ALWAYS works as the fallback.
    /// </summary>
    Task<RemoteExtractionDecision> HandlePaperAsync(string paperId, CancellationToken ct);
}

/// <summary>
/// The <c>pdf.extract</c> producer used by <c>ContentTextExtractionWorker</c> (OET-RWP/1 section 6.1.2). It enqueues idempotent
/// jobs for assets that have no cache entry (apply) and, in shadow mode, for assets that already do (hash-only comparison
/// against a fresh in-process extraction). It never extracts anything itself, never touches provider keys, and the seven
/// synchronous call sites of <c>IPdfTextExtractor</c> are untouched. Only the PdfPig tier is ever remote: the
/// <c>azure</c>/<c>noop</c> providers, assets over 100 MiB and assets without a SHA-256 stay local.
/// </summary>
public sealed class RemotePdfExtractionProducer(
    LearnerDbContext db,
    IRemoteJobFlags flags,
    IRemoteJobQueue queue,
    RemotePlacement placement,
    IRuntimeSettingsProvider runtimeSettings,
    IFileStorage storage,
    RemoteJobsSettings settings,
    IPrimaryHeadroom headroom,
    RemoteLocalWaitTracker waits,
    TimeProvider timeProvider,
    ILogger<RemotePdfExtractionProducer> logger) : IRemotePdfExtractionProducer
{
    public const string Enqueuer = "system:content-extraction";

    private enum AssetPlan
    {
        Pending,
        EnqueueNeeded,
        LocalNeeded,
        FallbackLocal,
        Skip,
    }

    private sealed record PdfAsset(string AssetId, string MediaId, string Format, string? Sha256, long SizeBytes, string StoragePath);

    public async Task<RemoteExtractionDecision> HandlePaperAsync(string paperId, CancellationToken ct)
    {
        try
        {
            return await HandleCoreAsync(paperId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any failure of the remote path degrades to today's behaviour.
            logger.LogWarning(ex, "Remote PDF extraction planning failed for paper {PaperId}; extracting locally.", paperId);
            return RemoteExtractionDecision.Local;
        }
    }

    private async Task<RemoteExtractionDecision> HandleCoreAsync(string paperId, CancellationToken ct)
    {
        var flagSnapshot = await flags.GetAsync(ct);
        var applyOn = flagSnapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply);
        var shadowOn = flagSnapshot.KindEnabled(RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow);
        if (!applyOn && !shadowOn) return RemoteExtractionDecision.Local;

        var options = settings.Current;
        var current = (await runtimeSettings.GetAsync(ct)).PdfExtraction;
        var provider = (current.Provider ?? "auto").Trim().ToLowerInvariant();
        if (provider is not ("auto" or "pdfpig")) return RemoteExtractionDecision.Local;

        var engine = RemoteJobKinds.EngineVersion(RemoteJobKinds.PdfExtract, options);
        var spec = RemoteJobKinds.Find(RemoteJobKinds.PdfExtract);
        if (engine is null || spec is null) return RemoteExtractionDecision.Local;

        var extractedJson = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == paperId)
            .Select(p => p.ExtractedTextJson)
            .FirstOrDefaultAsync(ct);
        var cachedKeys = extractedJson is null ? null : ExtractedTextMerger.ReadKeys(extractedJson);
        if (cachedKeys is null) return RemoteExtractionDecision.Local;

        var assets = await (
                from link in db.ContentPaperAssets.AsNoTracking()
                where link.PaperId == paperId
                join media in db.MediaAssets.AsNoTracking() on link.MediaAssetId equals media.Id
                select new PdfAsset(link.Id, media.Id, media.Format, media.Sha256, media.SizeBytes, media.StoragePath))
            .ToListAsync(ct);
        var pdfs = assets.Where(a => string.Equals(a.Format, "pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pdfs.Count == 0) return RemoteExtractionDecision.Local;

        if (shadowOn) await EnqueueShadowAsync(pdfs, current.MinTextLengthForSuccess, provider, engine, spec, ct);
        if (!applyOn) return RemoteExtractionDecision.Local;

        var uncached = pdfs.Where(a => !cachedKeys.Contains(a.AssetId)).ToList();
        if (uncached.Count == 0) return RemoteExtractionDecision.Local;

        var now = timeProvider.GetUtcNow();
        var hardAfter = TimeSpan.FromMinutes(options.FallbackHardAfterMinutes);
        var plans = new List<(PdfAsset Asset, AssetPlan Plan, string Key, RemoteJobRow? Job)>();

        foreach (var asset in uncached)
        {
            var sha = await EnsureSha256Async(asset, spec.Limits.MaxInputBytes, ct);
            if (sha is null || asset.SizeBytes > spec.Limits.MaxInputBytes)
            {
                plans.Add((asset, AssetPlan.LocalNeeded, string.Empty, null));
                continue;
            }

            var key = RemoteJobKeys.IdempotencyKey(
                RemoteJobKinds.PdfExtract, RemoteJobPurpose.Apply, "MediaAsset", asset.MediaId, sha, engine,
                PdfExtractSettings.Hash("flat", provider, current.MinTextLengthForSuccess, replaceExisting: false));
            var job = await queue.FindByKeyAsync(key, ct);
            plans.Add((asset with { Sha256 = sha }, PlanFor(job), key, job));
        }

        // One paper is either all-remote or all-local: a mixed pass would extract the remote assets twice.
        if (plans.Any(p => p.Plan == AssetPlan.LocalNeeded)) return RemoteExtractionDecision.Local;

        var fallbacks = plans.Where(p => p.Plan == AssetPlan.FallbackLocal).ToList();
        if (fallbacks.Count > 0)
        {
            var allowed = headroom.HasHeadroom() || fallbacks.All(p => now - p.Job!.CreatedAt >= hardAfter);
            if (allowed) return RemoteExtractionDecision.Local;
            await TouchForRotationAsync(paperId, ct);
            return RemoteExtractionDecision.Skip;
        }

        var toEnqueue = plans.Where(p => p.Plan == AssetPlan.EnqueueNeeded).ToList();
        if (toEnqueue.Count > 0)
        {
            var decision = await placement.DecideAsync(RemoteJobKinds.PdfExtract, spec.Limits.Weight, ct);
            if (decision == RemotePlacementDecision.Local) return RemoteExtractionDecision.Local;
            if (decision == RemotePlacementDecision.Wait)
            {
                if (waits.HardDeadlinePassed("pdf:" + paperId, hardAfter)) return RemoteExtractionDecision.Local;
                await TouchForRotationAsync(paperId, ct);
                return RemoteExtractionDecision.Skip;
            }

            foreach (var (asset, _, _, _) in toEnqueue)
            {
                await queue.EnqueueAsync(
                    BuildRequest(asset, asset.Sha256!, RemoteJobPurpose.Apply, current.MinTextLengthForSuccess, provider, engine, spec, withFallback: true),
                    force: false,
                    ct);
            }

            waits.Clear("pdf:" + paperId);
        }

        await TouchForRotationAsync(paperId, ct);
        return plans.Any(p => p.Plan is AssetPlan.Pending or AssetPlan.EnqueueNeeded)
            ? RemoteExtractionDecision.Remote
            : RemoteExtractionDecision.Skip;
    }

    private static AssetPlan PlanFor(RemoteJobRow? job)
    {
        if (job is null) return AssetPlan.EnqueueNeeded;
        return job.State switch
        {
            RemoteJobState.Queued or RemoteJobState.Leased => AssetPlan.Pending,
            RemoteJobState.FallbackLocal => AssetPlan.FallbackLocal,
            // Succeeded with the asset still uncached is a NoOp (needsOcr / already cached): the local path owns it.
            RemoteJobState.Succeeded => AssetPlan.LocalNeeded,
            RemoteJobState.Cancelled => AssetPlan.LocalNeeded,
            // Deterministic conditions the local path already handles; anything else is surfaced to an admin.
            RemoteJobState.Failed => RemoteFailCodes.AllowsLocalFallback(job.FailureCode) ? AssetPlan.LocalNeeded : AssetPlan.Skip,
            // Poison suspects are NEVER auto-executed locally (a crashing PDF would recreate the in-process crash loop).
            RemoteJobState.Quarantined => AssetPlan.Skip,
            _ => AssetPlan.LocalNeeded,
        };
    }

    private async Task EnqueueShadowAsync(
        IReadOnlyList<PdfAsset> pdfs,
        int minTextLength,
        string provider,
        string engine,
        RemoteKindSpec spec,
        CancellationToken ct)
    {
        const int MaxPerTick = 5;
        var enqueued = 0;
        foreach (var asset in pdfs)
        {
            if (enqueued >= MaxPerTick) return;
            if (asset.Sha256 is null || asset.SizeBytes > spec.Limits.MaxInputBytes) continue;
            if (!await placement.HasEligibleNodeAsync(RemoteJobKinds.PdfExtract, spec.Limits.Weight, ct)) return;

            var key = RemoteJobKeys.IdempotencyKey(
                RemoteJobKinds.PdfExtract, RemoteJobPurpose.Shadow, "MediaAsset", asset.MediaId, asset.Sha256, engine,
                PdfExtractSettings.Hash("flat", provider, minTextLength, replaceExisting: false));
            if (await queue.FindByKeyAsync(key, ct) is not null) continue;

            await queue.EnqueueAsync(
                BuildRequest(asset, asset.Sha256, RemoteJobPurpose.Shadow, minTextLength, provider, engine, spec, withFallback: false),
                force: false,
                ct);
            enqueued++;
        }
    }

    private static RemoteEnqueueRequest BuildRequest(
        PdfAsset asset,
        string sha256,
        string purpose,
        int minTextLength,
        string provider,
        string engine,
        RemoteKindSpec spec,
        bool withFallback)
    {
        var includePages = purpose == RemoteJobPurpose.Apply;
        var paramsJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["mode"] = "flat",
            ["minTextLength"] = minTextLength,
            ["provider"] = provider,
            ["includePages"] = includePages,
            ["replaceExisting"] = false,
            ["purpose"] = purpose,
        });
        var inputsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object>
            {
                ["name"] = "pdf",
                ["sizeBytes"] = asset.SizeBytes,
                ["sha256"] = sha256,
                ["contentType"] = "application/pdf",
                ["storageKey"] = asset.StoragePath,
            },
        });

        return new RemoteEnqueueRequest(
            RemoteJobKinds.PdfExtract,
            spec.SchemaVersion,
            purpose,
            "MediaAsset",
            asset.MediaId,
            sha256,
            engine,
            PdfExtractSettings.Hash("flat", provider, minTextLength, replaceExisting: false),
            paramsJson,
            inputsJson,
            RemoteJobKinds.LimitsFor(spec, purpose),
            Enqueuer,
            WithFallback: withFallback);
    }

    /// <summary>
    /// The asset's SHA-256, backfilled with a streaming hash when the row predates content addressing (several creation sites
    /// leave it null). Null when it cannot be determined: the asset then stays on the local path.
    /// </summary>
    private async Task<string?> EnsureSha256Async(PdfAsset asset, long maxBytes, CancellationToken ct)
    {
        if (RemoteIds.IsSha256Hex(asset.Sha256)) return asset.Sha256;
        if (asset.SizeBytes > maxBytes) return null;

        try
        {
            await using var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
            var (_, sha) = await StreamingSha256.ComputeAsync([stream], null, ct);
            var mediaId = asset.MediaId;
            await db.MediaAssets
                .Where(m => m.Id == mediaId && m.Sha256 == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Sha256, _ => sha), ct);
            return sha;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not hash media asset {MediaId}; it stays on the local extraction path.", asset.MediaId);
            return null;
        }
    }

    /// <summary>
    /// Keeps the worker's oldest-first window rotating exactly as the local pass does: the local pass bumps
    /// <c>UpdatedAt</c> of every paper it visits, so a paper that remote jobs own must be bumped too or the same papers
    /// would be revisited forever and later papers never reached.
    /// </summary>
    private Task TouchForRotationAsync(string paperId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        return db.ContentPapers
            .Where(p => p.Id == paperId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, _ => now), ct);
    }
}
