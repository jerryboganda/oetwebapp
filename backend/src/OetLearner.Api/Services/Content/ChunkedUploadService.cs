using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

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
    Task<AdminUploadSession> UploadPartAsync(string sessionId, int partNumber, Stream body, CancellationToken ct);
    Task<ChunkedUploadCommitResult> CompleteAsync(string sessionId, CancellationToken ct);
    Task AbortAsync(string sessionId, CancellationToken ct);
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

        var limit = ResolveSizeLimitForRole(args.IntendedRole);
        if (args.DeclaredSizeBytes > limit)
            throw new InvalidOperationException(
                $"File size {args.DeclaredSizeBytes} exceeds limit {limit} for role {args.IntendedRole}.");

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

    public async Task<AdminUploadSession> UploadPartAsync(string sessionId, int partNumber, Stream body, CancellationToken ct)
    {
        if (partNumber < 1) throw new ArgumentOutOfRangeException(nameof(partNumber));
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Upload session not found.");
        if (session.State is AdminUploadState.Completed or AdminUploadState.Aborted or AdminUploadState.Expired)
            throw new InvalidOperationException($"Upload session is {session.State} and cannot accept parts.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Upload session has expired.");

        var key = ContentAddressed.StagingPartKey(
            _opts.StagingSubpath, session.AdminUserId, session.Id, partNumber);
        var wrote = await storage.WriteAsync(key, body, ct);

        session.ReceivedBytes += wrote;
        session.PartsReceived += 1;
        if (session.State == AdminUploadState.Started) session.State = AdminUploadState.Uploading;
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ChunkedUploadCommitResult> CompleteAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Upload session not found.");
        if (session.State == AdminUploadState.Completed && !string.IsNullOrEmpty(session.MediaAssetId))
        {
            // Idempotent replay.
            return new ChunkedUploadCommitResult(session.MediaAssetId!, session.Sha256!, session.ReceivedBytes, Deduplicated: true);
        }
        if (session.PartsReceived == 0)
            throw new InvalidOperationException("No parts uploaded.");

        // Enumerate parts in order. Streams must be disposed even on failure.
        var sessionPrefix = ContentAddressed.StagingSessionPrefix(
            _opts.StagingSubpath, session.AdminUserId, session.Id);

        var partKeys = Enumerable.Range(1, session.PartsReceived)
            .Select(n => ContentAddressed.StagingPartKey(
                _opts.StagingSubpath, session.AdminUserId, session.Id, n))
            .Where(storage.Exists)
            .ToList();

        if (partKeys.Count == 0)
            throw new InvalidOperationException("No staged parts on disk for this session.");

        // Magic-byte validation: read the first part's header and verify the
        // declared extension matches the actual content. Cheap — first chunk
        // only; we do NOT rescan the whole stitched file.
        if (validator is not null)
        {
            await using var peek = await storage.OpenReadAsync(partKeys[0], ct);
            var result = await validator.ValidateAsync(peek, session.Extension, ct);
            if (!result.Accepted)
            {
                storage.DeletePrefix(sessionPrefix);
                session.State = AdminUploadState.Aborted;
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException(
                    $"Upload rejected by content validator: {result.Reason}");
            }
        }

        // Stage-stitched file lands under staging with a deterministic name,
        // so dedup detection runs against the SHA before the published move.
        var assembledStagingKey = $"{sessionPrefix}/__assembled.{session.Extension}";
        long total;
        string sha;
        {
            await using var output = await storage.OpenWriteAsync(assembledStagingKey, ct);
            var partStreams = partKeys.Select<string, Stream>(k =>
            {
                var stream = storage.OpenReadAsync(k, ct).GetAwaiter().GetResult();
                return stream;
            }).ToList();
            try
            {
                var result = await StreamingSha256.ComputeAsync(partStreams, output, ct);
                total = result.bytes;
                sha = result.sha256;
            }
            finally
            {
                foreach (var s in partStreams) s.Dispose();
            }
        }

        // Dedup: if a media asset with this SHA already exists, reuse it.
        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        if (existing is not null)
        {
            logger.LogInformation("Chunked upload {Session} deduplicated against existing asset {Existing}.",
                session.Id, existing.Id);
            storage.DeletePrefix(sessionPrefix);
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
                storage.DeletePrefix(sessionPrefix);
                session.State = AdminUploadState.Aborted;
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException($"Upload quarantined by scanner: {reason}");
            }
        }

        // Promote to published/{sha[..2]}/{sha[2..4]}/{sha}.{ext}.
        var publishedKey = ContentAddressed.PublishedKey(_opts.PublishedSubpath, sha, session.Extension);
        if (storage.Exists(publishedKey))
        {
            // Two uploads with the same SHA raced — acceptable. Drop ours.
            storage.Delete(assembledStagingKey);
        }
        else
        {
            storage.Move(assembledStagingKey, publishedKey, overwrite: false);
        }
        storage.DeletePrefix(sessionPrefix);

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

    public async Task AbortAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.AdminUploadSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null) return;
        storage.DeletePrefix(ContentAddressed.StagingSessionPrefix(
            _opts.StagingSubpath, session.AdminUserId, session.Id));
        session.State = AdminUploadState.Aborted;
        await db.SaveChangesAsync(ct);
    }

    private long ResolveSizeLimitForRole(string role) => role switch
    {
        "Audio" => _opts.MaxAudioBytes,
        "QuestionPaper" or "AudioScript" or "AnswerKey" or "CaseNotes" or "ModelAnswer"
            or "RoleCard" or "AssessmentCriteria" or "WarmUpQuestions" or "Supplementary"
            => _opts.MaxPdfBytes,
        _ => _opts.MaxPdfBytes,
    };

    private static string ClassifyKind(string mime, string ext)
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "image";
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || ext is "mp3" or "wav" or "m4a" or "ogg" or "webm") return "audio";
        return "document";
    }
}
