using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionCorpusBuilder
{
    /// <summary>
    /// Runs every registered indexer and returns the combined result.
    /// </summary>
    /// <param name="professions">
    /// Limits the profession-scoped rulebooks. Profession-independent sources
    /// (taxonomy, criteria, platform, support, vocabulary) are always rebuilt —
    /// see the remark on partial reindexing below.
    /// </param>
    Task<CompanionIndexResult> BuildAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct);
}

/// <summary>
/// Runs the whole corpus, so there is exactly one list of what the knowledge
/// base is made of.
///
/// <para>
/// Before this, the reindex endpoint summed the indexers by hand and the startup
/// bootstrap called only the rulebook one. That divergence is not cosmetic: a
/// fresh environment bootstrapped a corpus containing rulebooks and nothing
/// else, so the Speaking taxonomy, the assessment criteria, the platform map and
/// the support rules were all missing until somebody happened to hit the admin
/// endpoint — and nothing anywhere said so. Adding an indexer now means adding
/// it here, once.
/// </para>
///
/// <para>
/// <b>Partial reindexes still rebuild everything else.</b> An operator narrowing
/// to one profession is reindexing a rulebook they just edited; silently
/// dropping the profession-independent sources from that run would leave the
/// corpus in a state nobody asked for.
/// </para>
/// </summary>
public sealed class CompanionCorpusBuilder(
    ICompanionRulebookIndexer rulebooks,
    ICompanionSpeakingTaxonomyIndexer taxonomy,
    ICompanionSpeakingCriteriaIndexer criteria,
    ICompanionPlatformMapIndexer platformMap,
    ICompanionSupportKnowledgeIndexer support,
    ICompanionVocabularyIndexer vocabulary,
    ICompanionOfficialFactsIndexer officialFacts,
    ICompanionDocumentIndexer documents,
    ILogger<CompanionCorpusBuilder> logger) : ICompanionCorpusBuilder
{
    public async Task<CompanionIndexResult> BuildAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct)
    {
        var results = new List<CompanionIndexResult>
        {
            await rulebooks.IndexAsync(professions, embed, ct),
            await taxonomy.IndexAsync(embed, ct),
            await criteria.IndexAsync(embed, ct),
            await platformMap.IndexAsync(embed, ct),
            await support.IndexAsync(embed, ct),
            await vocabulary.IndexAsync(embed, ct),
            await officialFacts.IndexAsync(embed, ct),
            await documents.IndexAsync(embed, ct),
        };

        var combined = new CompanionIndexResult(
            results.Sum(r => r.SourcesWritten),
            results.Sum(r => r.ChunksWritten),
            results.Sum(r => r.ChunksUnchanged),
            results.Sum(r => r.ChunksEmbedded),
            results.SelectMany(r => r.Warnings).ToList());

        logger.LogInformation(
            "Companion corpus build: {Sources} sources, {Chunks} chunks written, {Unchanged} unchanged, " +
            "{Embedded} embedded, {Warnings} warning(s).",
            combined.SourcesWritten, combined.ChunksWritten, combined.ChunksUnchanged,
            combined.ChunksEmbedded, combined.Warnings.Count);

        return combined;
    }
}
