using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// One stored candidate clip as the audio stage needs it, projected in a single query (no entity graph): which recording, how it is
/// encoded, where it is stored and what its content hash is. Shared by the audio stage and the remote <c>media.speaking-join</c>
/// precompute so BOTH pick exactly the same clips in exactly the same order.
/// </summary>
internal sealed record SpeakingClipRow(
    string Id,
    string MimeType,
    string? StoragePath,
    string? MediaAssetId,
    string? MediaSha256,
    string? RecordingSha256,
    long MediaSizeBytes,
    DateTimeOffset CreatedAt)
{
    private static readonly Regex HexSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The clip's SHA-256: the stored asset's when it has one, else the recording's, else null. Rows created by several Speaking paths
    /// leave both empty, in which case the remote precompute hashes the stored bytes and records the result on the asset.
    /// </summary>
    public string? Sha256
        => MediaSha256 is not null && HexSha256.IsMatch(MediaSha256) ? MediaSha256
            : RecordingSha256 is not null && HexSha256.IsMatch(RecordingSha256) ? RecordingSha256
            : null;
}

internal static class SpeakingAudioClips
{
    /// <summary>
    /// The candidate's clips for a session. Live voice: one short clip per candidate turn, in the order the candidate spoke (archived and
    /// warm-up clips never count, nor do clips of another session). Recorder sessions: the session recording or, failing that, whatever
    /// audio was stored for the session, oldest first.
    /// </summary>
    public static async Task<IReadOnlyList<SpeakingClipRow>> LoadAsync(
        LearnerDbContext db,
        string sessionId,
        IReadOnlyList<SpeakingAudioEvidenceService.CandidateTurn> turns,
        CancellationToken ct)
    {
        var orderedIds = turns
            .Where(t => t.RecordingId is not null)
            .Select(t => t.RecordingId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (orderedIds.Count > 0)
        {
            var rows = await Project(
                    db,
                    db.SpeakingRecordings.AsNoTracking()
                        .Where(r => orderedIds.Contains(r.Id) && r.SpeakingSessionId == sessionId && !r.IsArchived && !r.IsWarmup))
                .ToListAsync(ct);
            var byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
            return orderedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }

        var recorderId = SpeakingSessionRecordingService.RecordingIdFor(sessionId);
        var all = await Project(
                db,
                db.SpeakingRecordings.AsNoTracking()
                    .Where(r => r.SpeakingSessionId == sessionId && !r.IsArchived && !r.IsWarmup))
            .ToListAsync(ct);
        var recorder = all.Where(r => string.Equals(r.Id, recorderId, StringComparison.Ordinal)).ToList();
        return recorder.Count > 0 ? recorder : all.OrderBy(r => r.CreatedAt).ToList();
    }

    private static IQueryable<SpeakingClipRow> Project(LearnerDbContext db, IQueryable<SpeakingRecording> recordings)
        => from r in recordings
           join m in db.MediaAssets.AsNoTracking() on r.MediaAssetId equals m.Id into assets
           from m in assets.DefaultIfEmpty()
           select new SpeakingClipRow(
               r.Id,
               r.MimeType,
               m == null ? null : m.StoragePath,
               m == null ? null : m.Id,
               m == null ? null : m.Sha256,
               r.Sha256,
               m == null ? 0L : m.SizeBytes,
               r.CreatedAt);
}
