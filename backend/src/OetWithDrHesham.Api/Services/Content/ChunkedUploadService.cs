using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Chunked Upload Service — Slice 2.
//
// Lifecycle:
//   1. StartAsync → creates an AdminUploadSession with State=Started,
//      parts go to uploads/staging/{adminId}/{sessionId}/*.bin
//   2. UploadPartAsync(sessionId, partNumber, stream) → writes one part.
//   3. CompleteAsync(sessionId) → stream-hashes all parts in order, writes
//      the final file to uploads/published/{sha}.{ext} (dedup: skip write
//      if already exists), creates a MediaAsset row, marks session Completed.
//   4. AbortAsync(sessionId) → deletes staging, marks session Aborted.
//
// The staging cleanup worker deletes Expired sessions + their parts.
// ═════════════════════════════════════════════════════════════════════════════

public interface IChunkedUploadService
{
    Task<AdminUploadSession> StartAsync(ChunkedUploadStart args, CancellationToken ct);
    Task<AdminUploadSession> UploadPartAsync(string adminUserId, string sessionId, int partNumber, Stream body, CancellationToken ct);
    Task<ChunkedUploadCommitResult> CompleteAsync(string adminUserId, string sessionId, CancellationToken ct);
    Task AbortAsync(string adminUserId, string sessionId, CancellationToken ct);
}

public sealed record ChunkedUploadStart(
    string AdminUserId,
    string OriginalFilename,
    string DeclaredMimeType,
    long DeclaredSizeBytes,
    string IntendedRole);

public sealed record ChunkedUploadCommitResult(
    string MediaAssetId,
    string Sha256,
    long SizeBytes,
    bool Deduplicated);

public sealed class ChunkedUploadService(
    LearnerDbContext db,
    IFileStorage storage,
    IOptions<StorageOptions> options,
    IUploadContentValidator? validator,
    IUploadScanner? scanner,
    ILogger<ChunkedUploadService> logger) : IChunkedUploadService
{
    private readonly ContentUploadOptions _opts = options.Value.ContentUpload;

    public async Task<AdminUploadSession> StartAsync(ChunkedUploadStart args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.AdminUserId))
            throw new ArgumentException("AdminUserId required.", nameof(args));
        if (string.IsNullOrWhiteSpace(args.OriginalFilename))
            throw new ArgumentException("OriginalFilename required.", nameof(args));
        if (args.DeclaredSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(args.DeclaredSizeBytes), "DeclaredSizeBytes must be greater than zero.");
        if (_opts.ChunkSizeBytes <= 0)
            throw new InvalidOperationException("Configured chunk size must be greater than zero.");

        var limit = ResolveSizeLimitForRole(args.IntendedRole);
        if (args.DeclaredSizeBytes > limit)
            throw ApiException.Validation(
                "upload_too_large",
                $"File is too large ({args.DeclaredSizeBytes / (1024 * 1024)} MB). The maximum allowed size for this slot is {limit / (1024 * 1024)} MB.");

        var ext = Path.GetExtension(args.OriginalFilename).TrimStart('.').ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var session = new AdminUploadSession
        {
            Id = Guid.NewGuid().ToString("N"),
            AdminUserId = args.AdminUserId,
            OriginalFilename = Path.GetFileName(args.OriginalFilename),
            Extension = ext,
            DeclaredMimeType = args.DeclaredMimeType,
            DeclaredSizeBytes = args.DeclaredSizeBytes,
            ReceivedBytes = 0,
            TotalParts = (int)Math.Max(1, Math.Ceiling((double)args.DeclaredSizeBytes / _opts.ChunkSizeBytes)),
            PartsReceived = 0,
            IntendedRole = args.IntendedRole,
            State = AdminUploadState.Started,
            CreatedAt = now,
            ExpiresAt = now.AddHours(_opts.StagingTtlHours),
        };
        db.AdminUploadSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<AdminUploadSession> UploadPartAsync(string adminUserId, string sessionId, int partNumber, Stream body, CancellationToken ct)
    {
        if (partNumber < 1) throw new ArgumentOutOfRangeException(nameof(partNumber));
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AdminUserId == adminUserId, ct)
            ?? throw ApiException.NotFound("upload_session_not_found", "Upload session not found. Reload the page and start the upload again.");
        if (session.State is AdminUploadState.Completed or AdminUploadState.Aborted or AdminUploadState.Expired)
            throw ApiException.Conflict("upload_session_closed", $"This upload session is {session.State} and cannot accept more parts. Reload the page and try again.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
            throw ApiException.Conflict("upload_session_expired", "This upload session has expired. Reload the page and start the upload again.");
        if (partNumber > session.TotalParts)
            throw ApiException.Validation("upload_part_out_of_range", $"Part {partNumber} exceeds the expected total of {session.TotalParts} parts. Reload the page so the latest uploader can chunk the file correctly, then try again.");

        var key = ContentAddressed.StagingPartKey(
            _opts.StagingSubpath, session.AdminUserId, session.Id, partNumber);
        if (await storage.ExistsAsync(key, ct))
            throw ApiException.Conflict("upload_part_duplicate", $"Part {partNumber} has already been uploaded.");

        var remaining = session.DeclaredSizeBytes - session.ReceivedBytes;
        if (remaining <= 0)
            throw ApiException.Conflict("upload_already_complete", "This upload session has already received all declared bytes.");

        var allowedBytes = Math.Min(_opts.ChunkSizeBytes, remaining);
        long wrote;
        try
        {
            wrote = await WriteLimitedAsync(key, body, allowedBytes, ct);
        }
        catch
        {
            await storage.DeleteAsync(key, ct);
            throw;
        }

        if (wrote <= 0)
        {
            await storage.DeleteAsync(key, ct);
            throw ApiException.Validation("upload_part_empty", "Upload part was empty.");
        }

        session.ReceivedBytes += wrote;
        session.PartsReceived += 1;
        if (session.State == AdminUploadState.Started) session.State = AdminUploadState.Uploading;
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ChunkedUploadCommitResult> CompleteAsync(string adminUserId, string sessionId, CancellationToken ct)
    {
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AdminUserId == adminUserId, ct)
            ?? throw ApiException.NotFound("upload_session_not_found", "Upload session not found. Reload the page and start the upload again.");
        if (session.State == AdminUploadState.Completed && !string.IsNullOrEmpty(session.MediaAssetId))
        {
            // Idempotent replay.
            return new ChunkedUploadCommitResult(session.MediaAssetId!, session.Sha256!, session.ReceivedBytes, Deduplicated: true);
        }
        if (session.PartsReceived == 0)
            throw ApiException.Validation("upload_no_parts", "No parts were uploaded for this session.");
        if (session.PartsReceived != session.TotalParts)
            throw ApiException.Validation("upload_incomplete", $"Upload is incomplete: received {session.PartsReceived} of {session.TotalParts} parts. Reload the page and try again.");
        if (session.ReceivedBytes != session.DeclaredSizeBytes)
            throw ApiException.Validation("upload_size_mismatch", $"Upload size mismatch: received {session.ReceivedBytes} of {session.DeclaredSizeBytes} declared bytes. Reload the page and try again.");

        // Enumerate parts in order. Streams must be disposed even on failure.
        var sessionPrefix = ContentAddressed.StagingSessionPrefix(
            _opts.StagingSubpath, session.AdminUserId, session.Id);

        var partKeys = new List<string>(session.TotalParts);
        foreach (var partNumber in Enumerable.Range(1, session.TotalParts))
        {
            var partKey = ContentAddressed.StagingPartKey(
                _opts.StagingSubpath, session.AdminUserId, session.Id, partNumber);
            if (await storage.ExistsAsync(partKey, ct))
            {
                partKeys.Add(partKey);
            }
        }

        if (partKeys.Count != session.TotalParts)
            throw ApiException.Conflict("upload_parts_missing", "One or more staged parts are missing for this session. Reload the page and start the upload again.");

        // Magic-byte validation: read the first part's header and verify the
        // declared extension matches the actual content. Cheap — first chunk
        // only; we do NOT rescan the whole stitched file.
        if (validator is not null)
        {
            await using var peek = await storage.OpenReadAsync(partKeys[0], ct);
            var result = await validator.ValidateAsync(peek, session.Extension, ct);
            if (!result.Accepted)
            {
                await storage.DeletePrefixAsync(sessionPrefix, ct);
                session.State = AdminUploadState.Aborted;
                await db.SaveChangesAsync(ct);
                throw ApiException.Validation(
                    "upload_rejected_content",
                    $"Upload rejected: {result.Reason}");
            }
        }

        // Stage-stitched file lands under staging with a deterministic name,
        // so dedup detection runs against the SHA before the published move.
        var assembledStagingKey = $"{sessionPrefix}/__assembled.{session.Extension}";
        long total;
        string sha;
        {
            await using var output = await storage.OpenWriteAsync(assembledStagingKey, ct);
            var partStreams = new List<Stream>(partKeys.Count);
            try
            {
                foreach (var partKey in partKeys)
                {
                    partStreams.Add(await storage.OpenReadAsync(partKey, ct));
                }

                var result = await StreamingSha256.ComputeAsync(partStreams, output, ct);
                total = result.bytes;
                sha = result.sha256;
            }
            finally
            {
                foreach (var partStream in partStreams)
                {
                    await partStream.DisposeAsync();
                }
            }
        }

        // Dedup: if a media asset with this SHA already exists, reuse it.
        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        if (existing is not null)
        {
            logger.LogInformation("Chunked upload {Session} deduplicated against existing asset {Existing}.",
                session.Id, existing.Id);
            await storage.DeletePrefixAsync(sessionPrefix, ct);
            session.State = AdminUploadState.Completed;
            session.ReceivedBytes = total;
            session.Sha256 = sha;
            session.MediaAssetId = existing.Id;
            session.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return new ChunkedUploadCommitResult(existing.Id, sha, total, Deduplicated: true);
        }

        // Scanner hook. No-op by default; ClamAV or similar in production.
        if (scanner is not null)
        {
            await using var scanStream = await storage.OpenReadAsync(assembledStagingKey, ct);
            var (clean, reason) = await scanner.ScanAsync(scanStream, session.OriginalFilename, ct);
            if (!clean)
            {
                // Two very different failures arrive on the same channel:
                //   • a genuine detection — clamd answered "… FOUND". The file
                //     is malicious: purge it and reject with 400.
                //   • a scanner *infrastructure* failure — unreachable, timed
                //     out, or a misconfigured endpoint/provider, surfaced
                //     fail-closed by ClamAvUploadScanner with a "scan_"-coded
                //     reason. That is NOT the uploaded file's fault, so a 400
                //     that blames the file is wrong (and sent production
                //     debugging chasing the file instead of the scanner). Keep
                //     the staged parts so the client can retry the commit once
                //     the scanner recovers, and return a retryable 503. Still
                //     fail-closed: an unscanned file is never published.
                if (IsGenuineDetection(reason))
                {
                    logger.LogWarning("Chunked upload {Session} rejected by scanner: {Reason}", session.Id, reason);
                    await storage.DeletePrefixAsync(sessionPrefix, ct);
                    session.State = AdminUploadState.Aborted;
                    await db.SaveChangesAsync(ct);
                    throw ApiException.Validation("upload_quarantined", $"Upload quarantined by scanner: {reason}");
                }

                logger.LogError(
                    "Chunked upload {Session} could not be virus-scanned ({Reason}); staged parts retained for retry, returning 503.",
                    session.Id, reason);
                throw ApiException.ServiceUnavailable(
                    "scan_unavailable",
                    "The upload could not be virus-scanned because the scanner is temporarily unavailable. Please try again in a moment.");
            }
        }

        // Promote to published/{sha[..2]}/{sha[2..4]}/{sha}.{ext}.
        var publishedKey = ContentAddressed.PublishedKey(_opts.PublishedSubpath, sha, session.Extension);
        if (await storage.ExistsAsync(publishedKey, ct))
        {
            // Two uploads with the same SHA raced — acceptable. Drop ours.
            await storage.DeleteAsync(assembledStagingKey, ct);
        }
        else
        {
            await storage.MoveAsync(assembledStagingKey, publishedKey, overwrite: false, ct);
        }
        await storage.DeletePrefixAsync(sessionPrefix, ct);

        var mediaId = Guid.NewGuid().ToString("N");
        var media = new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = session.OriginalFilename,
            MimeType = session.DeclaredMimeType,
            Format = session.Extension,
            SizeBytes = total,
            StoragePath = publishedKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = sha,
            MediaKind = ClassifyKind(session.DeclaredMimeType, session.Extension),
            UploadedBy = session.AdminUserId,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        };
        db.MediaAssets.Add(media);

        session.State = AdminUploadState.Completed;
        session.ReceivedBytes = total;
        session.Sha256 = sha;
        session.MediaAssetId = mediaId;
        session.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return new ChunkedUploadCommitResult(mediaId, sha, total, Deduplicated: false);
    }

    public async Task AbortAsync(string adminUserId, string sessionId, CancellationToken ct)
    {
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.AdminUserId == adminUserId, ct);
        if (session is null) return;
        await storage.DeletePrefixAsync(ContentAddressed.StagingSessionPrefix(
            _opts.StagingSubpath, session.AdminUserId, session.Id), ct);
        session.State = AdminUploadState.Aborted;
        await db.SaveChangesAsync(ct);
    }

    private long ResolveSizeLimitForRole(string role) => role switch
    {
        "Audio" => _opts.MaxAudioBytes,
        "QuestionPaper" or "AudioScript" or "AnswerKey" or "CaseNotes" or "ModelAnswer"
            or "RoleCard" or "AssessmentCriteria" or "WarmUpQuestions" or "Supplementary"
            => _opts.MaxPdfBytes,
        // Video Library asset slots: captions are small VTT text files,
        // thumbnails small images, attachments PDF-sized documents.
        "VideoCaption" => 2L * 1024 * 1024,
        "VideoThumbnail" => 5L * 1024 * 1024,
        "VideoAttachment" => _opts.MaxPdfBytes,
        _ => _opts.MaxPdfBytes,
    };

    private async Task<long> WriteLimitedAsync(string key, Stream source, long maxBytes, CancellationToken ct)
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
                throw ApiException.Validation(
                    "upload_part_too_large",
                    $"This upload part exceeds the {maxBytes / (1024 * 1024)} MB chunk limit. Reload the page so the latest uploader can split the file into chunks, then try again.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    // A clamd INSTREAM detection always ends in "FOUND" (e.g.
    // "stream: Win.Test.EICAR_HDB-1 FOUND"), and ClamAvUploadScanner only
    // returns the raw clamd response as the reason in that case. Every other
    // not-clean reason it produces is a fail-closed infrastructure code
    // ("scan_unreachable", "scan_timeout", "scan_endpoint_not_allowed",
    // "scan_provider_not_clamav", "scan_fail_open_forbidden", "scan_error: …").
    // Only a real detection is the uploaded file's fault.
    private static bool IsGenuineDetection(string? reason)
        => reason is not null && reason.Contains("FOUND", StringComparison.Ordinal);

    private static string ClassifyKind(string mime, string ext)
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "image";
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || ext is "mp3" or "wav" or "m4a" or "ogg" or "webm") return "audio";
        return "document";
    }
}
