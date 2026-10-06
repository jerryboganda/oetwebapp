namespace OetLearner.Api.Services.Companion;

/// <summary>
/// Which material PDFs a bounded reindex run should take (a pure function, so the fairness property is testable).
///
/// <para>
/// The old selection was <c>OrderBy(File.UpdatedAt).Take(200)</c>. Nothing in the indexer ever updates <c>File.UpdatedAt</c>, so every
/// run re-selected the SAME oldest 200 files and a library larger than the cap was never walked past them, contradicting the
/// comment that "successive runs walk through the backlog". The order here is: files never indexed or edited since their last
/// index first (oldest edit first), then everything else, least recently indexed first; a file that recently produced nothing
/// is deferred behind the rest. Because the indexer stamps each source row
/// every time it writes, repeated runs advance through any number of files.
/// </para>
/// </summary>
public static class CompanionIndexSelection
{
    public static IEnumerable<T> Take<T>(
        IEnumerable<T> files,
        Func<T, DateTimeOffset> fileUpdatedAt,
        Func<T, DateTimeOffset?> lastIndexedAt,
        int limit,
        Func<T, bool>? recentlyFailed = null)
    {
        return files
            .Select(file => (File: file, Updated: fileUpdatedAt(file), Indexed: lastIndexedAt(file), Failed: recentlyFailed?.Invoke(file) ?? false))
            // A file that just yielded nothing (a scan with no OCR, an unreadable object) goes to the back for a while, or a few
            // hundred of them would occupy every run and nothing behind them would ever be indexed.
            .OrderBy(entry => entry.Failed ? 1 : 0)
            .ThenBy(entry => NeedsIndexing(entry.Updated, entry.Indexed) ? 0 : 1)
            .ThenBy(entry => entry.Indexed ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Updated)
            .Take(Math.Max(0, limit))
            .Select(entry => entry.File);
    }

    /// <summary>True for a file that was never indexed or was edited after its last index.</summary>
    public static bool NeedsIndexing(DateTimeOffset fileUpdatedAt, DateTimeOffset? lastIndexedAt)
        => lastIndexedAt is null || fileUpdatedAt > lastIndexedAt.Value;
}
