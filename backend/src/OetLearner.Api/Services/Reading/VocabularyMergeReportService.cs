using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Reading;

public sealed record VocabularyDuplicateCluster(
    string NormalizedWord,
    Guid ProposedSurvivorId,
    IReadOnlyList<Guid> DuplicateIds,
    int Count);

public sealed record VocabularyMergeReport(
    DateTimeOffset GeneratedAtUtc,
    int ClusterCount,
    IReadOnlyList<VocabularyDuplicateCluster> Clusters,
    bool MutatedRows);

public interface IVocabularyMergeReportService
{
    Task<VocabularyMergeReport> BuildReportAsync(CancellationToken ct);
}

/// <summary>
/// Reports duplicate <c>NormalizedWord</c> clusters. Merge + unique index
/// apply only after owner approval via <c>scripts/ops</c>.
/// </summary>
public sealed class VocabularyMergeReportService(LearnerDbContext db) : IVocabularyMergeReportService
{
    public async Task<VocabularyMergeReport> BuildReportAsync(CancellationToken ct)
    {
        var rows = await db.VocabularyWords.AsNoTracking()
            .Where(w => w.NormalizedWord != "")
            .Select(w => new { w.Id, w.NormalizedWord, w.CreatedAt })
            .ToListAsync(ct);

        var clusters = rows
            .GroupBy(w => w.NormalizedWord, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
                return new VocabularyDuplicateCluster(
                    g.Key,
                    ordered[0].Id,
                    ordered.Skip(1).Select(x => x.Id).ToList(),
                    ordered.Count);
            })
            .OrderBy(c => c.NormalizedWord, StringComparer.Ordinal)
            .ToList();

        return new VocabularyMergeReport(DateTimeOffset.UtcNow, clusters.Count, clusters, MutatedRows: false);
    }
}
