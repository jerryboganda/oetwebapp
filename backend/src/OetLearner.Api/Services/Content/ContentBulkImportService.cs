using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

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
    int CreatedReferenceCount,
    List<string> Warnings);

internal sealed record BulkImportSessionSnapshot(
    string SessionId,
    string AdminId,
    DateTimeOffset ExpiresAt,
    ImportManifest Manifest,
    Dictionary<string, string> RelativeToStagingKey);

public sealed class ContentBulkImportService(
    LearnerDbContext db,
    IFileStorage storage,
    IContentConventionParser parser,
    IContentPaperService paperService,
    IOptions<StorageOptions> options,
    IUploadScanner? scanner,
    ILogger<ContentBulkImportService> logger) : IContentBulkImportService
{
    private readonly ContentUploadOptions _opts = options.Value.ContentUpload;
    private static readonly Dictionary<string, BulkImportSession> _sessions = new(StringComparer.Ordinal);
    private static readonly object _sessionLock = new();
    private const long MaxTextEntryBytes = 2L * 1024 * 1024;

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
            await ScanStagedFileAsync(zipKey, filename, ct);
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

                    ValidateZipEntrySizeByType(relativePath, entry.Length);

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
                    await WriteValidatedZipEntryAsync(stagingKey, relativePath, entryStream, entry.Length, ct);
                    await ScanStagedFileAsync(stagingKey, relativePath, ct);
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

        await PersistSessionSnapshotAsync(session, ct);
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
        session ??= await TryLoadSessionSnapshotAsync(adminId, sessionId, ct);
        if (session is null || session.AdminId != adminId)
            throw new InvalidOperationException("Bulk import session not found.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Bulk import session expired.");

        var approvalByProposal = approvals.ToDictionary(a => a.ProposalId);
        var warnings = new List<string>();
        int papersCreated = 0, assetsCreated = 0, dedup = 0, referencesCreated = 0;
        var speakingSharedMediaByKind = new Dictionary<string, (string MediaId, string Title)>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in session.Manifest.References)
        {
            if (!approvalByProposal.TryGetValue(reference.ProposalId, out var approval) || !approval.Approve)
                continue;

            if (!session.RelativeToStagingKey.TryGetValue(reference.SourceRelativePath, out var stagingKey))
            {
                warnings.Add($"Missing staged file for {reference.SourceRelativePath}");
                continue;
            }

            var title = approval.OverrideTitle ?? reference.Title;
            var profession = approval.OverrideProfessionId ?? reference.ProfessionId;
            var (created, mediaId, deduped) = await CommitReferenceAsync(reference, stagingKey, title, profession, adminId, warnings, ct);
            if (created) referencesCreated++;
            if (deduped) dedup++;
            if (created
                && mediaId is not null
                && reference.Target == ImportReferenceTargets.SpeakingSharedResource
                && !string.IsNullOrWhiteSpace(reference.SharedResourceKind))
            {
                speakingSharedMediaByKind[reference.SharedResourceKind] = (mediaId, title);
            }
        }

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
            var displayOrder = 0;
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
                    Title: asset.SuggestedTitle, DisplayOrder: displayOrder++, MakePrimary: true), adminId, ct);
                assetsCreated++;
            }

            if (proposal.SubtestCode == "speaking")
            {
                if (speakingSharedMediaByKind.TryGetValue(SpeakingSharedResourceKinds.AssessmentCriteria, out var criteria))
                {
                    await paperService.AttachAssetAsync(paper.Id, new ContentPaperAssetAttach(
                        Role: PaperAssetRole.AssessmentCriteria,
                        MediaAssetId: criteria.MediaId,
                        Part: null,
                        Title: criteria.Title,
                        DisplayOrder: displayOrder++,
                        MakePrimary: true), adminId, ct);
                    assetsCreated++;
                }

                if (speakingSharedMediaByKind.TryGetValue(SpeakingSharedResourceKinds.WarmUpQuestions, out var warmUp))
                {
                    await paperService.AttachAssetAsync(paper.Id, new ContentPaperAssetAttach(
                        Role: PaperAssetRole.WarmUpQuestions,
                        MediaAssetId: warmUp.MediaId,
                        Part: null,
                        Title: warmUp.Title,
                        DisplayOrder: displayOrder++,
                        MakePrimary: true), adminId, ct);
                    assetsCreated++;
                }
            }
        }

        // Clean up staging regardless.
        try
        {
            storage.DeletePrefix($"{_opts.StagingSubpath}/bulk/{adminId}/{sessionId}");
        }
        catch { /* best-effort */ }
        lock (_sessionLock) _sessions.Remove(sessionId);

        return new BulkImportCommitResult(papersCreated, assetsCreated, dedup, referencesCreated, warnings);
    }

    private async Task PersistSessionSnapshotAsync(BulkImportSession session, CancellationToken ct)
    {
        var snapshot = new BulkImportSessionSnapshot(
            session.SessionId,
            session.AdminId,
            session.ExpiresAt,
            session.Manifest,
            new Dictionary<string, string>(session.RelativeToStagingKey, StringComparer.OrdinalIgnoreCase));
        var key = SessionSnapshotKey(session.AdminId, session.SessionId);
        await using var stream = await storage.OpenWriteAsync(key, ct);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: ct);
    }

    private async Task<BulkImportSession?> TryLoadSessionSnapshotAsync(string adminId, string sessionId, CancellationToken ct)
    {
        var key = SessionSnapshotKey(adminId, sessionId);
        if (!storage.Exists(key)) return null;
        await using var stream = await storage.OpenReadAsync(key, ct);
        var snapshot = await JsonSerializer.DeserializeAsync<BulkImportSessionSnapshot>(stream, cancellationToken: ct);
        if (snapshot is null) return null;
        return new BulkImportSession(
            snapshot.SessionId,
            snapshot.AdminId,
            snapshot.ExpiresAt,
            snapshot.Manifest,
            snapshot.RelativeToStagingKey);
    }

    private string SessionSnapshotKey(string adminId, string sessionId)
        => $"{_opts.StagingSubpath}/bulk/{adminId}/{sessionId}/__session.json";

    private async Task<(bool Created, string? MediaId, bool Deduplicated)> CommitReferenceAsync(
        ProposedReference reference,
        string stagingKey,
        string title,
        string? professionId,
        string adminId,
        List<string> warnings,
        CancellationToken ct)
    {
        switch (reference.Target)
        {
            case ImportReferenceTargets.SpeakingSharedResource:
                return await CommitSpeakingSharedResourceAsync(reference, stagingKey, title, professionId, adminId, ct);
            case ImportReferenceTargets.RulebookReferencePdf:
                return await CommitRulebookReferencePdfAsync(reference, stagingKey, adminId, warnings, ct);
            case ImportReferenceTargets.ScoringPolicyBody:
                await CommitScoringPolicyAsync(stagingKey, adminId, ct);
                return (true, null, false);
            case ImportReferenceTargets.ResultTemplate:
                return await CommitResultTemplateAsync(reference, stagingKey, title, professionId, adminId, warnings, ct);
            default:
                warnings.Add($"Unknown reference target {reference.Target} for {reference.SourceRelativePath}");
                return (false, null, false);
        }
    }

    private async Task<(bool Created, string? MediaId, bool Deduplicated)> CommitSpeakingSharedResourceAsync(
        ProposedReference reference,
        string stagingKey,
        string title,
        string? professionId,
        string adminId,
        CancellationToken ct)
    {
        var (mediaId, deduplicated) = await PromoteToPublishedAsync(stagingKey, reference.SourceRelativePath, adminId, ct);
        var now = DateTimeOffset.UtcNow;
        var id = $"sss_{Guid.NewGuid():N}";
        db.SpeakingSharedResources.Add(new SpeakingSharedResource
        {
            Id = id,
            Kind = reference.SharedResourceKind ?? SpeakingSharedResourceKinds.WarmUpQuestions,
            Title = title,
            ProfessionId = professionId,
            MediaAssetId = mediaId,
            Status = ContentStatus.Draft,
            UploadedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent("BulkImportSpeakingSharedResourceImported", "SpeakingSharedResource", id, $"kind={reference.SharedResourceKind};media={mediaId}", adminId);
        await db.SaveChangesAsync(ct);
        return (true, mediaId, deduplicated);
    }

    private async Task<(bool Created, string? MediaId, bool Deduplicated)> CommitRulebookReferencePdfAsync(
        ProposedReference reference,
        string stagingKey,
        string adminId,
        List<string> warnings,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference.Kind) || string.IsNullOrWhiteSpace(reference.ProfessionId))
        {
            warnings.Add($"Rulebook reference missing kind/profession: {reference.SourceRelativePath}");
            return (false, null, false);
        }

        var published = await db.RulebookVersions
            .Include(x => x.Sections)
            .Include(x => x.Rules)
            .Where(x => x.Kind == reference.Kind && x.Profession == reference.ProfessionId && x.Status == RulebookStatus.Published)
            .OrderByDescending(x => x.PublishedAt)
            .FirstOrDefaultAsync(ct);
        if (published is null)
        {
            warnings.Add($"No published rulebook found for {reference.Kind}/{reference.ProfessionId}; skipped reference PDF {reference.SourceRelativePath}.");
            return (false, null, false);
        }

        var (mediaId, deduplicated) = await PromoteToPublishedAsync(stagingKey, reference.SourceRelativePath, adminId, ct);
        var now = DateTimeOffset.UtcNow;
        var draftId = $"rb_{reference.Kind}_{reference.ProfessionId}_{Guid.NewGuid():N}";
        db.RulebookVersions.Add(new RulebookVersion
        {
            Id = draftId,
            Kind = published.Kind,
            Profession = published.Profession,
            Version = $"{published.Version}-pdf-draft-{now:yyyyMMddHHmmss}",
            Status = RulebookStatus.Draft,
            AuthoritySource = published.AuthoritySource,
            ReferencePdfAssetId = mediaId,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = adminId,
            Sections = published.Sections.Select(section => new RulebookSectionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                RulebookVersionId = draftId,
                Code = section.Code,
                Title = section.Title,
                OrderIndex = section.OrderIndex,
            }).ToList(),
            Rules = published.Rules.Select(rule => new RulebookRuleRow
            {
                Id = Guid.NewGuid().ToString("N"),
                RulebookVersionId = draftId,
                Code = rule.Code,
                SectionCode = rule.SectionCode,
                Title = rule.Title,
                Body = rule.Body,
                Severity = rule.Severity,
                AppliesToJson = rule.AppliesToJson,
                TurnStage = rule.TurnStage,
                ExemplarPhrasesJson = rule.ExemplarPhrasesJson,
                ForbiddenPatternsJson = rule.ForbiddenPatternsJson,
                CheckId = rule.CheckId,
                ParamsJson = rule.ParamsJson,
                ExamplesJson = rule.ExamplesJson,
                OrderIndex = rule.OrderIndex,
            }).ToList(),
        });
        AddAuditEvent("BulkImportRulebookReferencePdfDraftImported", "RulebookVersion", draftId, $"source={published.Id};media={mediaId}", adminId);
        await db.SaveChangesAsync(ct);
        return (true, mediaId, deduplicated);
    }

    private async Task CommitScoringPolicyAsync(string stagingKey, string adminId, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(stagingKey, ct);
        using var reader = new StreamReader(stream);
        var bodyMarkdown = await reader.ReadToEndAsync(ct);
        var policyJson = ScoringPolicyValidation.CanonicalDefaultPolicyJson;
        var validationError = ScoringPolicyValidation.ValidateCanonicalPolicyJson(policyJson);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var now = DateTimeOffset.UtcNow;
        var id = $"scr_{Guid.NewGuid():N}";
        db.ScoringPolicies.Add(new ScoringPolicy
        {
            Id = id,
            BodyMarkdown = bodyMarkdown,
            PolicyJson = policyJson,
            IsActive = false,
            UpdatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent("BulkImportScoringPolicyDraftImported", "ScoringPolicy", id, "Inactive scoring policy draft imported from bulk ZIP.", adminId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<(bool Created, string? MediaId, bool Deduplicated)> CommitResultTemplateAsync(
        ProposedReference reference,
        string stagingKey,
        string title,
        string? professionId,
        string adminId,
        List<string> warnings,
        CancellationToken ct)
    {
        var (mediaId, deduplicated) = await PromoteToPublishedAsync(stagingKey, reference.SourceRelativePath, adminId, ct);
        var key = reference.TemplateKey ?? $"real-content-{Guid.NewGuid():N}";
        if (await db.ResultTemplateAssets.AnyAsync(x => x.TemplateKey == key, ct))
        {
            key = $"{key}-{Guid.NewGuid():N}"[..Math.Min(128, key.Length + 33)];
            warnings.Add($"Result template key already existed; using {key}.");
        }

        var now = DateTimeOffset.UtcNow;
        var id = $"rtpl_{Guid.NewGuid():N}";
        db.ResultTemplateAssets.Add(new ResultTemplateAsset
        {
            Id = id,
            TemplateKey = key,
            Title = title,
            ProfessionId = professionId,
            MediaAssetId = mediaId,
            IsActive = false,
            SortOrder = reference.SortOrder ?? 0,
            UploadedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent("BulkImportResultTemplateImported", "ResultTemplateAsset", id, key, adminId);
        await db.SaveChangesAsync(ct);
        return (true, mediaId, deduplicated);
    }

    private void AddAuditEvent(string action, string resourceType, string? resourceId, string? details, string adminId)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }

    private async Task<(string mediaId, bool deduplicated)> PromoteToPublishedAsync(
        string stagingKey, ProposedAsset asset, string adminId, CancellationToken ct)
        => await PromoteToPublishedAsync(stagingKey, asset.SourceRelativePath, adminId, ct);

    private async Task<(string mediaId, bool deduplicated)> PromoteToPublishedAsync(
        string stagingKey, string sourceRelativePath, string adminId, CancellationToken ct)
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

        var ext = Path.GetExtension(sourceRelativePath).TrimStart('.').ToLowerInvariant();
        var publishedKey = ContentAddressed.PublishedKey(_opts.PublishedSubpath, sha, ext);
        if (!storage.Exists(publishedKey))
            storage.Move(stagingKey, publishedKey, overwrite: false);

        var mime = GuessMime(ext);
        var mediaId = Guid.NewGuid().ToString("N");
        var media = new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = Path.GetFileName(sourceRelativePath),
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
        "webp" => "image/webp",
        "txt" => "text/plain",
        _ => "application/octet-stream",
    };

    private async Task<long> WriteValidatedZipEntryAsync(
        string key,
        string relativePath,
        Stream source,
        long maxBytes,
        CancellationToken ct)
    {
        await using var destination = await storage.OpenWriteAsync(key, ct);
        var buffer = new byte[81920];
        var header = new byte[16];
        var headerLength = 0;
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;

            if (headerLength < header.Length)
            {
                var toCopy = Math.Min(read, header.Length - headerLength);
                Array.Copy(buffer, 0, header, headerLength, toCopy);
                headerLength += toCopy;
            }

            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"ZIP entry {relativePath} exceeded its declared size.");

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        ValidateZipEntryMagic(relativePath, header.AsSpan(0, headerLength), total);
        return total;
    }

    private void ValidateZipEntrySizeByType(string relativePath, long length)
    {
        var ext = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
        var limit = ext switch
        {
            "pdf" => _opts.MaxPdfBytes,
            "mp3" or "m4a" or "mp4" or "wav" or "ogg" => _opts.MaxAudioBytes,
            "jpg" or "jpeg" or "png" or "webp" => _opts.MaxImageBytes,
            "txt" => MaxTextEntryBytes,
            _ => (long?)null,
        };

        if (limit.HasValue && length > limit.Value)
            throw new InvalidOperationException($"ZIP entry {relativePath} exceeds the {ext} upload limit of {limit.Value} bytes.");
    }

    private static void ValidateZipEntryMagic(string relativePath, ReadOnlySpan<byte> header, long length)
    {
        if (length <= 0)
            throw new InvalidOperationException($"ZIP entry {relativePath} is empty.");

        var ext = Path.GetExtension(relativePath).TrimStart('.').ToLowerInvariant();
        var valid = ext switch
        {
            "pdf" => StartsWithAscii(header, "%PDF-"),
            "mp3" => StartsWithAscii(header, "ID3") || HasMp3FrameSync(header),
            "m4a" or "mp4" => header.Length >= 8 && StartsWithAscii(header[4..], "ftyp"),
            "wav" => header.Length >= 12 && StartsWithAscii(header, "RIFF") && StartsWithAscii(header[8..], "WAVE"),
            "ogg" => StartsWithAscii(header, "OggS"),
            "png" => header.Length >= 8
                && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "jpg" or "jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "webp" => header.Length >= 12 && StartsWithAscii(header, "RIFF") && StartsWithAscii(header[8..], "WEBP"),
            "txt" => !header.Contains((byte)0x00),
            _ => false,
        };

        if (!valid)
            throw new InvalidOperationException($"ZIP entry {relativePath} failed file signature validation.");
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> bytes, string value)
    {
        if (bytes.Length < value.Length) return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (bytes[i] != value[i]) return false;
        }
        return true;
    }

    private static bool HasMp3FrameSync(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;

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

    private async Task ScanStagedFileAsync(string key, string filename, CancellationToken ct)
    {
        if (scanner is null)
        {
            return;
        }

        await using var stream = await storage.OpenReadAsync(key, ct);
        var (clean, reason) = await scanner.ScanAsync(stream, filename, ct);
        if (!clean)
        {
            throw new InvalidOperationException(
                $"ZIP import file {filename} failed security scanning: {reason ?? "scanner rejected the file"}");
        }
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
