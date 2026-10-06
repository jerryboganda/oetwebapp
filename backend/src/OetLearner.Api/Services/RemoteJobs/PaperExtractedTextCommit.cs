using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.RemoteJobs;

public enum PaperTextCommitStatus
{
    Written,
    AlreadyCached,
    Unreadable,
    PaperGone,
}

/// <summary>
/// The single writer of one asset's text into <c>ContentPaper.ExtractedTextJson</c> for the remote path (OET-RWP/1 section
/// 6.1.5): re-read the row, merge ONE key, then compare-and-swap on BOTH the stored text and <c>RowVersion</c> (bumped by
/// one), retrying the whole read-merge-write up to three times. Comparing the stored text catches writers that rewrite the
/// column without bumping <c>RowVersion</c> (the Speaking/Writing/Listening structure writers do), so a concurrent authoring
/// write is never silently lost. <c>UpdatedAt</c> is touched only when the value really changed.
/// </summary>
public static class PaperExtractedTextCommit
{
    public const int MaxAttempts = 3;

    public static async Task<PaperTextCommitStatus> CommitAsync(
        LearnerDbContext db,
        string paperId,
        string assetKey,
        string text,
        bool replaceExisting,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var paper = await db.ContentPapers
                .AsNoTracking()
                .Where(p => p.Id == paperId)
                .Select(p => new { p.ExtractedTextJson, p.RowVersion })
                .FirstOrDefaultAsync(ct);
            if (paper is null) return PaperTextCommitStatus.PaperGone;

            var merge = ExtractedTextMerger.Merge(paper.ExtractedTextJson, assetKey, text, replaceExisting);
            if (merge.Status == ExtractedTextMergeStatus.Unreadable) return PaperTextCommitStatus.Unreadable;
            if (merge.Status == ExtractedTextMergeStatus.AlreadyCached) return PaperTextCommitStatus.AlreadyCached;

            var oldJson = paper.ExtractedTextJson;
            var oldVersion = paper.RowVersion;
            var newVersion = oldVersion + 1;
            var newJson = merge.Json!;

            int rows;
            if (db.Database.IsRelational())
            {
                rows = await db.ContentPapers
                    .Where(p => p.Id == paperId && p.RowVersion == oldVersion && p.ExtractedTextJson == oldJson)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.ExtractedTextJson, _ => newJson)
                        .SetProperty(p => p.RowVersion, _ => newVersion)
                        .SetProperty(p => p.UpdatedAt, _ => now), ct);
            }
            else
            {
                // In-memory provider (unit tests): no ExecuteUpdate; a tracked compare-and-save is equivalent there.
                var tracked = await db.ContentPapers.FirstOrDefaultAsync(
                    p => p.Id == paperId && p.RowVersion == oldVersion && p.ExtractedTextJson == oldJson, ct);
                if (tracked is null)
                {
                    rows = 0;
                }
                else
                {
                    tracked.ExtractedTextJson = newJson;
                    tracked.RowVersion = newVersion;
                    tracked.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                    db.Entry(tracked).State = EntityState.Detached;
                    rows = 1;
                }
            }

            if (rows == 1) return PaperTextCommitStatus.Written;
        }

        throw new RemoteApplyConflictException($"ContentPaper {paperId} kept changing during the merge.");
    }
}
