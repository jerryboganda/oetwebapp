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
    ILogger<CompanionDocumentIndexer> logger) : ICompanionDocumentIndexer
{
    /// <summary>
    /// Longest page kept whole. Beyond this the page is split, because a single
    /// chunk larger than the retriever's per-source verbatim cap can only ever be
    /// returned truncated — the tail would be unreachable no matter what a
    /// learner asked.
    /// </summary>
    private const int MaxChunkChars = 1100;

    /// <summary>
    /// Pages shorter than this are merged forward. A PDF of mostly slide titles
    /// otherwise produces hundreds of two-word chunks that match everything
    /// weakly and crowd out real evidence.
    /// </summary>
    private const int MinChunkChars = 200;

    /// <summary>
    /// Cap per run. Extraction is CPU-bound and may call a paid OCR provider, so
    /// a first reindex over a large library must not become an unbounded job.
    /// Documents already indexed and unchanged cost nothing, so successive runs
    /// walk through the backlog.
    /// </summary>
    private const int MaxDocumentsPerRun = 200;

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

        var pdfs = files
            .Where(x => string.Equals(x.File.Kind, "pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.File.UpdatedAt)
            .Take(MaxDocumentsPerRun)
            .ToList();

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

            var pages = await ExtractAsync(item.Asset, warnings, ct);
            if (pages.Count == 0) continue;

            var chunks = BuildChunks(pages);
            if (chunks.Count == 0) continue;

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

            var result = await CompanionIndexWriter.WriteAsync(
                db, embeddings, logger, $"material:{item.File.Id}", ChecksumVersion(pages),
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
    /// One chunk per page, merging pages too short to stand alone and splitting
    /// pages too long to be returned whole.
    /// </summary>
    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks(IReadOnlyList<string> pages)
    {
        var chunks = new List<CompanionChunkDraft>();
        var carried = new StringBuilder();
        var carriedFrom = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index].Trim();
            if (page.Length == 0) continue;

            var pageNumber = index + 1;

            if (carried.Length == 0) carriedFrom = pageNumber;
            if (carried.Length > 0) carried.AppendLine();
            carried.Append(page);

            if (carried.Length < MinChunkChars && index < pages.Count - 1) continue;

            Flush(chunks, carried.ToString(), carriedFrom);
            carried.Clear();
        }

        if (carried.Length > 0) Flush(chunks, carried.ToString(), carriedFrom);

        return chunks;
    }

    private static void Flush(List<CompanionChunkDraft> chunks, string text, int pageNumber)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        if (text.Length <= MaxChunkChars)
        {
            chunks.Add(new CompanionChunkDraft($"Page {pageNumber}", text, PageNumber: pageNumber));
            return;
        }

        // Split on line boundaries so a rule or worked example is not cut in
        // half, and fall back to a hard cut only for a single line with no
        // boundary to use.
        var part = 1;
        var builder = new StringBuilder();

        void Emit()
        {
            var body = builder.ToString().Trim();
            builder.Clear();
            if (body.Length == 0) return;

            chunks.Add(new CompanionChunkDraft($"Page {pageNumber} ({part})", body, PageNumber: pageNumber));
            part++;
        }

        foreach (var line in text.Split('\n'))
        {
            // A line longer than the whole cap has no boundary to split on, so it
            // is cut into pieces. Whatever is already buffered goes out first,
            // otherwise the pieces would appear before the text that preceded them.
            var remaining = line;
            while (remaining.Length > MaxChunkChars)
            {
                Emit();
                chunks.Add(new CompanionChunkDraft(
                    $"Page {pageNumber} ({part})", remaining[..MaxChunkChars], PageNumber: pageNumber));
                part++;
                remaining = remaining[MaxChunkChars..];
            }

            if (builder.Length > 0 && builder.Length + remaining.Length + 1 > MaxChunkChars) Emit();
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(remaining);
        }

        Emit();
    }

    /// <summary>
    /// Version stamp derived from the extracted content, so re-uploading the same
    /// file is a no-op while a genuinely revised handout supersedes its previous
    /// version instead of sitting alongside it. Materials carry no version field
    /// of their own.
    /// </summary>
    private static string ChecksumVersion(IReadOnlyList<string> pages) =>
        CompanionIndexWriter.Sha256(string.Join("\n", pages))[..16].ToLowerInvariant();
}
