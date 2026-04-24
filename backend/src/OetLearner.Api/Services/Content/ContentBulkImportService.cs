using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Bulk Import Service — Slice 5.
//
// Admin uploads a ZIP matching the Project Real Content/ folder conventions.
// Service:
//   1. Streams the ZIP into staging, extracts relative paths.
//   2. Delegates to the convention parser for a proposed manifest.
//   3. Returns the manifest + staging session id for admin review.
//   4. On approve: creates ContentPaper / ContentPaperAsset / MediaAsset rows,
//      promoting each staged file to content-addressed published storage
//      (dedup on SHA-256).
//
// No DB writes happen in step 1-3. Only step 4.
// ═════════════════════════════════════════════════════════════════════════════

public interface IContentBulkImportService
{
    Task<BulkImportSession> StagePayloadAsync(
        string adminId, Stream zipStream, string filename, CancellationToken ct);

    Task<BulkImportCommitResult> CommitAsync(
        string adminId, string sessionId,
        IReadOnlyList<BulkImportApproval> approvals, CancellationToken ct);
}

public sealed record BulkImportSession(
    string SessionId,
    string AdminId,
    DateTimeOffset ExpiresAt,
    ImportManifest Manifest,
    IReadOnlyDictionary<string, string> RelativeToStagingKey);

public sealed record BulkImportApproval(
    string ProposalId,
    bool Approve,
    string? OverrideTitle,
    string? OverrideProfessionId,
    bool? OverrideAppliesToAllProfessions,
    string? OverrideCardType,
    string? OverrideLetterType,
    string? OverrideSourceProvenance);

public sealed record BulkImportCommitResult(
    int CreatedPaperCount,
    int CreatedAssetCount,
    int DeduplicatedAssetCount,
    List<string> Warnings);

public sealed class ContentBulkImportService(
    LearnerDbContext db,
    IFileStorage storage,
    IContentConventionParser parser,
    IContentPaperService paperService,
    IOptions<StorageOptions> options,
    ILogger<ContentBulkImportService> logger) : IContentBulkImportService
{
    private readonly ContentUploadOptions _opts = options.Value.ContentUpload;
    private static readonly Dictionary<string, BulkImportSession> _sessions = new(StringComparer.Ordinal);
    private static readonly object _sessionLock = new();

    public async Task<BulkImportSession> StagePayloadAsync(
        string adminId, Stream zipStream, string filename, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adminId)) throw new ArgumentException("adminId required");

        var sessionId = Guid.NewGuid().ToString("N");
        var stagingPrefix = $"{_opts.StagingSubpath}/bulk/{adminId}/{sessionId}";
        var zipKey = $"{stagingPrefix}/__source.zip";

        try
        {
            await WriteLimitedAsync(zipKey, zipStream, _opts.MaxZipBytes, "ZIP exceeds configured byte limit.", ct);
        }
        catch
        {
            storage.DeletePrefix(stagingPrefix);
            throw;
        }

        // Extract all entries into staging using ZIP relative paths. We track
        // relative-path → staging-key so commit can later resolve each proposed
        // asset back to its staged file.
        var relativeToStaging = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relatives = new List<string>();

        try
        {
            await using (var s = await storage.OpenReadAsync(zipKey, ct))
            using (var archive = new ZipArchive(s, ZipArchiveMode.Read, leaveOpen: false))
            {
                var entriesSeen = 0;
                long totalUncompressed = 0;

                foreach (var entry in archive.Entries)
                {
                    var relativePath = NormalizeZipEntryPath(entry);
                    if (relativePath is null) continue;

                    entriesSeen += 1;
                    if (entriesSeen > _opts.MaxZipEntries)
                        throw new InvalidOperationException($"ZIP contains more than {_opts.MaxZipEntries} file entries.");

                    if (entry.Length > _opts.MaxZipEntryBytes)
                        throw new InvalidOperationException($"ZIP entry {relativePath} exceeds {_opts.MaxZipEntryBytes} byte limit.");

                    totalUncompressed += entry.Length;
                    if (totalUncompressed > _opts.MaxZipUncompressedBytes)
                        throw new InvalidOperationException($"ZIP uncompressed size exceeds {_opts.MaxZipUncompressedBytes} byte limit.");

                    if (entry.Length > 0 && entry.CompressedLength <= 0)
                        throw new InvalidOperationException($"ZIP entry {relativePath} has an invalid compressed length.");

                    if (entry.CompressedLength > 0)
                    {
                        var ratio = (double)entry.Length / entry.CompressedLength;
                        if (ratio > _opts.MaxZipCompressionRatio)
                            throw new InvalidOperationException($"ZIP entry {relativePath} exceeds the allowed compression ratio.");
                    }

                    var stagingKey = $"{stagingPrefix}/{relativePath}";
                    using var entryStream = entry.Open();
                    await WriteLimitedAsync(stagingKey, entryStream, entry.Length, $"ZIP entry {relativePath} exceeded its declared size.", ct);
                    relativeToStaging[relativePath] = stagingKey;
                    relatives.Add(relativePath);
                }
            }
        }
        catch
        {
            storage.DeletePrefix(stagingPrefix);
            throw;
        }

        var manifest = parser.Parse(relatives);
        var session = new BulkImportSession(
            SessionId: sessionId,
            AdminId: adminId,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(_opts.StagingTtlHours),
            Manifest: manifest,
            RelativeToStagingKey: relativeToStaging);

        lock (_sessionLock) { _sessions[sessionId] = session; }
        logger.LogInformation("Bulk import staged {Files} files into {Papers} proposed papers ({Issues} issues).",
            relatives.Count, manifest.Papers.Count, manifest.Issues.Count);
        return session;
    }

    public async Task<BulkImportCommitResult> CommitAsync(
        string adminId, string sessionId,
        IReadOnlyList<BulkImportApproval> approvals, CancellationToken ct)
    {
        BulkImportSession? session;
        lock (_sessionLock) _sessions.TryGetValue(sessionId, out session);
        if (session is null || session.AdminId != adminId)
            throw new InvalidOperationException("Bulk import session not found.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Bulk import session expired.");

        var approvalByProposal = approvals.ToDictionary(a => a.ProposalId);
        var warnings = new List<string>();
        int papersCreated = 0, assetsCreated = 0, dedup = 0;

        foreach (var proposal in session.Manifest.Papers)
        {
            if (!approvalByProposal.TryGetValue(proposal.ProposalId, out var approval) || !approval.Approve)
                continue;

            var title = approval.OverrideTitle ?? proposal.Title;
            var applyAll = approval.OverrideAppliesToAllProfessions ?? proposal.AppliesToAllProfessions;
            var prof = approval.OverrideProfessionId ?? (applyAll ? null : proposal.ProfessionId);
            var card = approval.OverrideCardType ?? proposal.CardType;
            var letter = approval.OverrideLetterType ?? proposal.LetterType;
            var provenance = approval.OverrideSourceProvenance ?? proposal.SourceProvenance
                ?? ContentDefaults.DefaultSourceProvenance;

            ContentPaper paper;
            try
            {
                paper = await paperService.CreateAsync(new ContentPaperCreate(
                    SubtestCode: proposal.SubtestCode,
                    Title: title,
                    Slug: null,
                    ProfessionId: prof,
                    AppliesToAllProfessions: applyAll,
                    Difficulty: "standard",
                    EstimatedDurationMinutes: proposal.SubtestCode == "listening" ? 40
                        : proposal.SubtestCode == "reading" ? 60 : 45,
                    CardType: card,
                    LetterType: letter,
                    Priority: 0,
                    TagsCsv: null,
                    SourceProvenance: provenance), adminId, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Slug", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipped duplicate paper: {title}");
                continue;
            }
            papersCreated++;

            // Promote each staged file to a MediaAsset, then attach.
            foreach (var asset in proposal.Assets)
            {
                if (!session.RelativeToStagingKey.TryGetValue(asset.SourceRelativePath, out var stagingKey))
                {
                    warnings.Add($"Missing staged file for {asset.SourceRelativePath}");
                    continue;
                }

                var (mediaId, didDedup) = await PromoteToPublishedAsync(stagingKey, asset, adminId, ct);
                if (didDedup) dedup++;

                await paperService.AttachAssetAsync(paper.Id, new ContentPaperAssetAttach(
                    Role: asset.Role, MediaAssetId: mediaId, Part: asset.Part,
                    Title: asset.SuggestedTitle, DisplayOrder: 0, MakePrimary: true), adminId, ct);
                assetsCreated++;
            }
        }

        // Clean up staging regardless.
        try
        {
            storage.DeletePrefix($"{_opts.StagingSubpath}/bulk/{adminId}/{sessionId}");
        }
        catch { /* best-effort */ }
        lock (_sessionLock) _sessions.Remove(sessionId);

        return new BulkImportCommitResult(papersCreated, assetsCreated, dedup, warnings);
    }

    private async Task<(string mediaId, bool deduplicated)> PromoteToPublishedAsync(
        string stagingKey, ProposedAsset asset, string adminId, CancellationToken ct)
    {
        // Stream-hash the staged file.
        string sha;
        long size;
        await using (var s = await storage.OpenReadAsync(stagingKey, ct))
        using (var hasher = SHA256.Create())
        {
            var bytes = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await s.ReadAsync(bytes, ct);
                if (read == 0) break;
                hasher.TransformBlock(bytes, 0, read, null, 0);
                total += read;
            }
            hasher.TransformFinalBlock([], 0, 0);
            sha = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
            size = total;
        }

        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        if (existing is not null) return (existing.Id, deduplicated: true);

        var ext = Path.GetExtension(asset.SourceRelativePath).TrimStart('.').ToLowerInvariant();
        var publishedKey = ContentAddressed.PublishedKey(_opts.PublishedSubpath, sha, ext);
        if (!storage.Exists(publishedKey))
            storage.Move(stagingKey, publishedKey, overwrite: false);

        var mime = GuessMime(ext);
        var mediaId = Guid.NewGuid().ToString("N");
        var media = new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = Path.GetFileName(asset.SourceRelativePath),
            MimeType = mime,
            Format = ext,
            SizeBytes = size,
            StoragePath = publishedKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = sha,
            MediaKind = mime.StartsWith("audio/") ? "audio" : mime.StartsWith("image/") ? "image" : "document",
            UploadedBy = adminId,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(media);
        await db.SaveChangesAsync(ct);
        return (mediaId, deduplicated: false);
    }

    private static string GuessMime(string ext) => ext switch
    {
        "pdf" => "application/pdf",
        "mp3" => "audio/mpeg",
        "m4a" => "audio/mp4",
        "wav" => "audio/wav",
        "ogg" => "audio/ogg",
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        _ => "application/octet-stream",
    };

    private async Task<long> WriteLimitedAsync(
        string key,
        Stream source,
        long maxBytes,
        string errorMessage,
        CancellationToken ct)
    {
        await using var destination = await storage.OpenWriteAsync(key, ct);
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(errorMessage);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private static string? NormalizeZipEntryPath(ZipArchiveEntry entry)
    {
        if (IsZipSymlink(entry))
        {
            throw new InvalidOperationException($"Rejected zip symlink entry: {entry.FullName}");
        }

        var fullName = entry.FullName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(fullName) || fullName.EndsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        if (fullName.StartsWith("/", StringComparison.Ordinal) || fullName.Contains(":", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Rejected suspicious zip entry: {entry.FullName}");
        }

        var segments = fullName.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment == "."
                || segment == ".."))
        {
            throw new InvalidOperationException($"Rejected suspicious zip entry: {entry.FullName}");
        }

        return string.Join('/', segments);
    }

    private static bool IsZipSymlink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymlink = 0xA000;
        return ((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymlink;
    }
}
