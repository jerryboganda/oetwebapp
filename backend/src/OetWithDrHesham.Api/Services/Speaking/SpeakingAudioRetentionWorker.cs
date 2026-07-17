using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - background sweeper that
/// physically deletes learner speaking audio once it is older than
/// <see cref="SpeakingComplianceOptions.AudioRetentionDays"/>. Mirrors
/// <see cref="OetWithDrHesham.Api.Services.Conversation.ConversationAudioRetentionWorker"/>.
///
/// Sweeps in batches of 500 to keep memory bounded. The blob is removed
/// via <see cref="IFileStorage"/> and the <c>AudioObjectKey</c> column is
/// cleared. Other attempt fields (transcript, analysis, scores) are retained
/// - only the raw recording is reaped.
///
/// Phase 7 of the OET Speaking module plan (B.8) extended this worker
/// with a second sweep that walks <see cref="SpeakingRecording"/> rows
/// (the new typed schema introduced in Phase 1+) and physically deletes
/// blobs whose <c>RetentionExpiresAt</c> has elapsed. Each deletion
/// emits an <see cref="AuditEvent"/> row so the compliance audit trail
/// captures every reaper-initiated removal.
/// </summary>
public sealed class SpeakingAudioRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpeakingAudioRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaking audio retention sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Shutdown - exit loop quietly.
            }
        }
    }

    /// <summary>
    /// Internal entry point used by tests. Returns the number of
    /// attempts that had their audio cleared. Also drives the Phase 7
    /// <see cref="SpeakingRecording"/> sweep but doesn't surface that
    /// count separately to preserve the existing test contract — use
    /// <see cref="SweepSpeakingRecordingsOnceAsync"/> for the new path.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var clearedAttempts = await SweepLegacyAttemptsAsync(ct);

        // Phase 7 sweep — never throws on partial failure so the
        // legacy attempt sweep above stays the authoritative count.
        try
        {
            await SweepSpeakingRecordingsOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SpeakingRecording retention sweep failed");
        }

        return clearedAttempts;
    }

    /// <summary>Legacy sweep over <see cref="Attempt.AudioObjectKey"/>.
    /// Preserved verbatim from the original worker so existing tests
    /// (<c>SpeakingAudioRetentionWorkerTests</c>) continue to pass.</summary>
    private async Task<int> SweepLegacyAttemptsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<SpeakingComplianceOptions>>().Value;

        if (options.AudioRetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(options.AudioRetentionDays);

        // We retain audio while the attempt is still active. Sweep on
        // SubmittedAt when present, otherwise StartedAt. Only speaking.
        List<Attempt> due;
        try
        {
            due = await db.Attempts
                .Where(a => a.SubtestCode == "speaking"
                    && a.AudioObjectKey != null
                    && ((a.SubmittedAt != null && a.SubmittedAt < cutoff)
                        || (a.SubmittedAt == null && a.StartedAt < cutoff)))
                .OrderBy(a => a.SubmittedAt ?? a.StartedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            // The SQLite provider (bundled desktop backend) cannot translate
            // DateTimeOffset comparisons/ordering — same limitation handled by
            // the /health/ready stuck-jobs probe. Narrow on the translatable
            // predicates server-side, apply the cutoff and ordering in memory.
            var candidates = await db.Attempts
                .Where(a => a.SubtestCode == "speaking" && a.AudioObjectKey != null)
                .ToListAsync(ct);
            due = candidates
                .Where(a => (a.SubmittedAt ?? a.StartedAt) < cutoff)
                .OrderBy(a => a.SubmittedAt ?? a.StartedAt)
                .Take(BatchSize)
                .ToList();
        }

        if (due.Count == 0)
        {
            return 0;
        }

        var clearedAttempts = 0;
        var deletedBlobs = 0;
        foreach (var attempt in due)
        {
            var key = attempt.AudioObjectKey!;
            var canClearPointer = true;
            try
            {
                if (await storage.ExistsAsync(key, ct))
                {
                    await storage.DeleteAsync(key, ct);
                    deletedBlobs++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete speaking audio blob {Key} for attempt {AttemptId}",
                    key, attempt.Id);
                canClearPointer = false;
            }

            if (!canClearPointer)
            {
                continue;
            }

            attempt.AudioObjectKey = null;
            clearedAttempts++;
        }

        if (clearedAttempts == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Speaking audio retention sweep cleared {Count} attempts, deleted {Blobs} blobs.",
            clearedAttempts, deletedBlobs);

        return clearedAttempts;
    }

    /// <summary>
    /// Phase 7 sweep: walks <see cref="SpeakingRecording"/> rows whose
    /// <c>RetentionExpiresAt</c> has elapsed AND that are not already
    /// archived. Deletes the underlying blob (best-effort) and writes:
    ///   * <c>IsArchived = true</c> on the recording row.
    ///   * An <see cref="AuditEvent"/> row with action
    ///     <c>SpeakingRecordingExpiredByRetention</c> so the GDPR audit
    ///     trail captures every reaper-initiated deletion.
    /// Returns the number of rows archived in this sweep.
    /// </summary>
    public async Task<int> SweepSpeakingRecordingsOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<SpeakingComplianceOptions>>().Value;

        if (options.RetentionDaysDefault <= 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;

        // Pull due rows. The retention worker only sweeps rows that
        // explicitly carry a RetentionExpiresAt — sessions that have
        // never been wired through the new lifecycle still rely on the
        // legacy Attempt.AudioObjectKey sweep above.
        List<SpeakingRecording> due;
        try
        {
            due = await db.SpeakingRecordings
                .Where(r => r.RetentionExpiresAt != null
                    && r.RetentionExpiresAt <= now
                    && !r.IsArchived)
                .OrderBy(r => r.RetentionExpiresAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }
        catch (InvalidOperationException) when (db.Database.IsSqlite())
        {
            // SQLite provider limitation — see SweepLegacyAttemptsAsync.
            var candidates = await db.SpeakingRecordings
                .Where(r => r.RetentionExpiresAt != null && !r.IsArchived)
                .ToListAsync(ct);
            due = candidates
                .Where(r => r.RetentionExpiresAt <= now)
                .OrderBy(r => r.RetentionExpiresAt)
                .Take(BatchSize)
                .ToList();
        }

        if (due.Count == 0)
        {
            return 0;
        }

        var archivedCount = 0;
        foreach (var recording in due)
        {
            var blobDeleted = false;
            string? storageKey = null;

            try
            {
                var mediaAsset = await db.MediaAssets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == recording.MediaAssetId, ct);
                if (mediaAsset is not null && !string.IsNullOrWhiteSpace(mediaAsset.StoragePath))
                {
                    storageKey = mediaAsset.StoragePath;
                    if (await storage.ExistsAsync(storageKey, ct))
                    {
                        blobDeleted = await storage.DeleteAsync(storageKey, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete blob for SpeakingRecording {RecordingId}", recording.Id);
            }

            recording.IsArchived = true;
            // Snapshot the actual archival timestamp.
            recording.RetentionExpiresAt ??= now;

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = "system",
                ActorName = "SpeakingAudioRetentionWorker",
                Action = "SpeakingRecordingExpiredByRetention",
                ResourceType = "SpeakingRecording",
                ResourceId = recording.Id,
                Details = JsonSerializer.Serialize(new
                {
                    sessionId = recording.SpeakingSessionId,
                    blobDeleted,
                    storageKey,
                    source = recording.Source.ToString(),
                    sha256 = recording.Sha256,
                }),
            });

            archivedCount++;
        }

        if (archivedCount == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "SpeakingRecording retention sweep archived {Count} rows.", archivedCount);
        return archivedCount;
    }
}
