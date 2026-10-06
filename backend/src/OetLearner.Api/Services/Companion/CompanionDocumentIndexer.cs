using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionDocumentIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Ingests published PDF materials — handouts, model answers, exercises,
/// explanations — into the companion corpus (Manifest 1.B(b)).
///
/// <para>
/// The extractor existed and the files existed; there was simply no route
/// between them. <c>MaterialFile</c> had no extraction path at all, so every
/// handout Dr Hesham has ever published was invisible to the companion, and a
/// learner asking "what does the model answer for the referral letter look
/// like?" got general knowledge instead of the actual course material.
/// </para>
///
/// <para><b>Page numbers are the point.</b> Chunking is one chunk per page, and
/// <see cref="CompanionChunk.PageNumber"/> is populated, so a citation can say
/// <i>which page of which handout</i>. That is why
/// <see cref="IPdfTextExtractor.ExtractPagesAsync"/> exists: the flat extraction
/// concatenates pages irreversibly, and a citation that cannot name a location
/// is not verifiable by the learner.</para>
///
/// <para><b>Entitlement is inherited, not invented.</b> A file in a folder
/// restricted to a single plan takes that plan code as its required scope, and
/// its Full/Crash package scope is derived from the same plan code through
/// <see cref="PackageScopePolicy"/> — the identical policy the Video Library
/// uses, so materials and videos cannot disagree about what a package includes.
/// Everything else requires the Materials module. A folder restricted to several
/// plans cannot be expressed as one scope, so it falls back to the module and
/// says so in the warnings rather than guessing which plan wins.</para>
///
/// <para><b>Audio is skipped, and so is video.</b> Audio materials would need
/// ASR, which the owner has put out of scope along with the video library. They
/// are counted and reported so the NOT INGESTED list is accurate rather than
/// silent.</para>
/// </summary>
public sealed class CompanionDocumentIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    IPdfTextExtractor extractor,
    IFileStorage storage,
    ILogger<CompanionDocumentIndexer> logger,
    IRemoteCompanionIndexPrep? remote = null) : ICompanionDocumentIndexer
{
    /// <summary>
    /// Cap per run. Extraction is CPU-bound and may call a paid OCR provider, so
    /// a first reindex over a large library must not become an unbounded job.
    /// Documents already indexed and unchanged cost nothing, so successive runs
    /// walk through the backlog.
    /// </summary>
    private const int MaxDocumentsPerRun = 200;

    /// <summary>How long a file that produced no chunks is deferred behind the rest of the backlog (per process).</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromHours(1);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> RecentFailures = new(StringComparer.Ordinal);

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();
        var sourcesWritten = 0;
        var chunksWritten = 0;
        var chunksUnchanged = 0;
        var chunksEmbedded = 0;

        var files = await db.MaterialFiles
            .AsNoTracking()
            .Where(f => f.Status == ContentStatus.Published)
            .Join(db.MediaAssets.AsNoTracking(), f => f.MediaAssetId, a => a.Id, (f, a) => new { File = f, Asset = a })
            .GroupJoin(db.MaterialFolders.AsNoTracking(), x => x.File.FolderId, f => f.Id,
                (x, folders) => new { x.File, x.Asset, Folder = folders.FirstOrDefault() })
            .ToListAsync(ct);

        var audio = files.Count(x => !string.Equals(x.File.Kind, "pdf", StringComparison.OrdinalIgnoreCase));
        if (audio > 0)
        {
            warnings.Add(
                $"{audio} published non-PDF material(s) were not ingested: they need speech recognition, which is out " +
                "of scope. They belong on the NOT INGESTED list.");
        }

        // Walk the backlog instead of re-extracting the same oldest files on every run: a file never indexed (or edited since)
        // goes first, then the least recently indexed. WriteAsync stamps the source row on every pass, so successive runs
        // advance through any number of PDFs. (Ordering by File.UpdatedAt alone never reached anything beyond the first
        // MaxDocumentsPerRun, because nothing here ever updates it.)
        // Grouped in memory: the SQLite desktop provider cannot translate Max over a DateTimeOffset, and one row per material
        // version is a small set.
        var lastIndexed = await db.CompanionSources
            .AsNoTracking()
            .Where(s => s.SourceKey.StartsWith("material:"))
            .Select(s => new { s.SourceKey, s.UpdatedAt })
            .ToListAsync(ct);
        var lastIndexedBySource = lastIndexed
            .GroupBy(x => x.SourceKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(x => x.UpdatedAt), StringComparer.Ordinal);

        var pdfs = CompanionIndexSelection.Take(
                files.Where(x => string.Equals(x.File.Kind, "pdf", StringComparison.OrdinalIgnoreCase)),
                x => x.File.UpdatedAt,
                x => lastIndexedBySource.TryGetValue($"material:{x.File.Id}", out var at) ? at : (DateTimeOffset?)null,
                MaxDocumentsPerRun,
                x => RecentFailures.TryGetValue(x.File.Id, out var failedAt) && DateTimeOffset.UtcNow - failedAt < FailureBackoff)
            .ToList();
        var pendingRemote = 0;

        if (pdfs.Count == 0) return new CompanionIndexResult(0, 0, 0, 0, warnings);

        // Restricted folders: resolve the plan audiences once rather than per file.
        var folderIds = pdfs.Select(x => x.Folder?.Id).Where(id => id is not null).Distinct().ToList();
        var audiences = await db.MaterialFolderAudiences
            .AsNoTracking()
            .Where(a => folderIds.Contains(a.FolderId) && a.TargetType == "plan")
            .ToListAsync(ct);

        var plansByFolder = audiences
            .GroupBy(a => a.FolderId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.TargetId).Distinct().ToList());

        foreach (var item in pdfs)
        {
            ct.ThrowIfCancellationRequested();

            var sourceKey = $"material:{item.File.Id}";

            // Optional remote preparation (enqueue-and-poll). Without it, or on any doubt, the answer is Local: the path below.
            CompanionPrepPlan? plan = null;
            if (remote is not null)
            {
                plan = await remote.PlanAsync(item.Asset, sourceKey, ct);
                if (plan.Action == CompanionPrepAction.Pending)
                {
                    pendingRemote++;
                    continue;
                }

                if (plan.Action == CompanionPrepAction.Skip)
                {
                    warnings.Add($"Material \"{item.File.Title}\": {plan.Note}");
                    // Nothing is written, so no source row is stamped: without this the file would stay "never indexed" at the
                    // head of CompanionIndexSelection on every run and enough of them would pin the whole window.
                    RecentFailures[item.File.Id] = DateTimeOffset.UtcNow;
                    continue;
                }
            }

            IReadOnlyList<CompanionChunkDraft> chunks;
            string version;
            if (plan is { Action: CompanionPrepAction.Ready })
            {
                chunks = plan.Chunks!;
                version = plan.Version!;
            }
            else
            {
                var pages = await ExtractAsync(item.Asset, warnings, ct);
                if (pages.Count == 0)
                {
                    RecentFailures[item.File.Id] = DateTimeOffset.UtcNow;
                    continue;
                }

                chunks = CompanionChunker.Build(pages);
                if (chunks.Count == 0)
                {
                    RecentFailures[item.File.Id] = DateTimeOffset.UtcNow;
                    continue;
                }

                version = CompanionChunker.ChecksumVersion(pages);
            }

            var folder = item.Folder;
            var restrictedPlans = folder is { AudienceMode: MaterialAudienceMode.Restricted }
                && plansByFolder.TryGetValue(folder.Id, out var plans)
                    ? plans
                    : [];

            string? scope;
            string? packageScope = null;

            if (restrictedPlans.Count == 1)
            {
                scope = restrictedPlans[0];
                var resolved = PackageScopePolicy.Resolve(
                    productCategory: null, planCode: scope, profession: folder?.ProfessionId);
                packageScope = string.Equals(resolved, VideoVisibilityScopes.Shared, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : resolved;
            }
            else
            {
                if (restrictedPlans.Count > 1)
                {
                    warnings.Add(
                        $"Material \"{item.File.Title}\" is restricted to {restrictedPlans.Count} plans, which one " +
                        "entitlement scope cannot express. It was indexed behind the Materials module instead, which " +
                        "is broader than the folder restriction — narrow the folder or split it if that matters.");
                }

                scope = ModuleKeys.MaterialsLibrary;
            }

            var warningsBeforeWrite = warnings.Count;
            var result = await CompanionIndexWriter.WriteAsync(
                db, embeddings, logger, sourceKey, version,
                source =>
                {
                    source.SourceType = "material_pdf";
                    source.Title = item.File.Title;
                    source.AuthorityClass = CompanionAuthorityClass.CourseMaterial;
                    source.State = CompanionSourceState.Approved;
                    source.ExamTypeCode = "OET";
                    source.ProfessionId = string.Equals(folder?.ScopeKind, "profession", StringComparison.OrdinalIgnoreCase)
                        ? folder?.ProfessionId
                        : null;
                    source.SubtestCode = string.IsNullOrWhiteSpace(item.File.SubtestCode)
                        ? null
                        : item.File.SubtestCode.ToLowerInvariant();
                    source.IsProprietary = true;
                    source.RequiredEntitlementScope = scope;
                    source.PackageScope = packageScope;
                    source.StorageLocator = item.Asset.StoragePath;
                    source.ApprovedAt ??= DateTimeOffset.UtcNow;
                },
                chunks, embed, warnings, ct);

            sourcesWritten += result.SourcesWritten;
            chunksWritten += result.ChunksWritten;
            chunksUnchanged += result.ChunksUnchanged;
            chunksEmbedded += result.ChunksEmbedded;

            // A successful write counts every chunk as written or unchanged and stamps the source row. Zero of both means the
            // writer rejected the source (corpus guard) and stamped nothing, so defer it like any other file that yielded nothing.
            if (result.SourcesWritten == 0 && result.ChunksWritten == 0 && result.ChunksUnchanged == 0)
            {
                RecentFailures[item.File.Id] = DateTimeOffset.UtcNow;
            }
            else
            {
                RecentFailures.TryRemove(item.File.Id, out _);
            }

            // A clean write consumes the parked remote result; a write with warnings (for example a failed embedding pass)
            // leaves it so the next pass can retry without extracting again.
            if (plan is { Action: CompanionPrepAction.Ready } && remote is not null)
            {
                await remote.CompleteAsync(plan, fullyCommitted: warnings.Count == warningsBeforeWrite, ct);
            }
        }

        if (pendingRemote > 0)
        {
            warnings.Add(
                $"{pendingRemote} material PDF(s) are being prepared on a helper; run the reindex again shortly to index them " +
                "(they are picked up automatically once the result is in).");
        }

        return new CompanionIndexResult(sourcesWritten, chunksWritten, chunksUnchanged, chunksEmbedded, warnings);
    }

    private async Task<IReadOnlyList<string>> ExtractAsync(
        MediaAsset asset,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
            var pages = await extractor.ExtractPagesAsync(stream, ct);

            if (pages.Count == 0)
            {
                // Almost always a scan with no text layer and no OCR configured.
                // Worth a warning rather than a silent skip: the operator sees a
                // material they believe is indexed and it is not.
                warnings.Add($"No extractable text in \"{asset.OriginalFilename}\"; it was not indexed.");
            }

            return pages;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read material asset {AssetId} for indexing.", asset.Id);
            warnings.Add($"Material asset {asset.Id} could not be read ({ex.GetType().Name}); skipped.");
            return [];
        }
    }

    /// <summary>
    /// One chunk per page, merging pages too short to stand alone and splitting pages too long to be returned whole. The
    /// algorithm lives in <see cref="CompanionChunker"/> so a fleet helper can run the identical code; this stays as the
    /// stable entry point the existing tests call.
    /// </summary>
    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks(IReadOnlyList<string> pages) => CompanionChunker.Build(pages);
}
