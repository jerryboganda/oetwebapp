using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>
/// Jev reranker for the companion knowledge-retrieval surface
/// (<c>CompanionRetriever</c>). The retriever's hybrid vector+keyword score
/// decides WHICH candidates reach this class — always downstream of the
/// entitlement prefilter — and jev only REORDERS them by semantic relevance
/// to the actual query: the textbook re-rank pattern for hybrid retrieval,
/// where embedding similarity misses "same words, different intent".
///
/// <para>
/// Fail-soft twice over (service + local catch): a rerank outage returns
/// null and the retriever keeps its original ordering — retrieval can never
/// degrade because the judgment layer is down.
/// </para>
/// </summary>
public interface IJevCompanionReranker
{
    /// <summary>Scores each candidate's relevance to the query on the
    /// rerank scale (0..3). Returns null when the rerank did not run (flag
    /// off, disabled, unavailable, or crashed) — callers keep their order.
    /// Candidates not present in the result keep their original score.</summary>
    Task<IReadOnlyDictionary<Guid, double>?> RerankAsync(
        string query,
        IReadOnlyList<(Guid Id, string Text)> candidates,
        string? userId,
        CancellationToken ct);
}

public sealed class JevCompanionReranker(
    ITypeSafeJudgmentService judgments,
    IOptions<TypeSafeOptions> options,
    ILogger<JevCompanionReranker> logger) : IJevCompanionReranker
{
    /// <summary>Hard cap on reranked candidates per call: every candidate is
    /// one parallel question over shared state, so this bounds context and
    /// tokens. Candidates beyond the cap keep their hybrid ordering.</summary>
    public const int MaxCandidatesPerCall = 16;

    /// <summary>Per-candidate text truncation — the judgment is "does this
    /// chunk bear on the query", which the head of the chunk answers.</summary>
    private const int MaxCandidateChars = 600;

    public async Task<IReadOnlyDictionary<Guid, double>?> RerankAsync(
        string query,
        IReadOnlyList<(Guid Id, string Text)> candidates,
        string? userId,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (!opts.CompanionRerankEnabled) return null;
        if (candidates.Count == 0) return null;

        var batch = candidates.Take(MaxCandidatesPerCall).ToList();

        try
        {
            var result = await judgments.AskAsync(new JevJudgmentRequest
            {
                StateJson = System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    query,
                    candidates = batch.Select((c, i) => new
                    {
                        index = i,
                        text = c.Text.Length <= MaxCandidateChars ? c.Text : c.Text[..MaxCandidateChars],
                    }),
                }),
                Questions = batch.Select((c, i) => new JevQuestion
                {
                    Id = $"cand_{i}",
                    Kind = JevQuestionKind.Score,
                    Instructions = $"How well does candidate `state.candidates[{i}].text` answer or bear on `state.query`? Judge relevance to the question asked — not general quality or truth.",
                    ScoreLevels =
                    [
                        "Unrelated: the candidate does not touch the question's topic.",
                        "Tangential: same general topic but does not address the question.",
                        "Relevant: partially addresses the question; useful context.",
                        "Direct: directly answers or substantially addresses the question.",
                    ],
                }).ToList(),
            }, new JevCallMetadata
            {
                FeatureCode = AiFeatureCodes.JevCompanionRerank,
                UserId = userId,
                ResourceType = "companion_retrieval",
            }, ct);

            if (!result.IsOk) return null;

            var scores = new Dictionary<Guid, double>();
            foreach (var (c, i) in batch.Select((c, i) => (c, i)))
            {
                if (result.Answers!.TryGetValue($"cand_{i}", out var answer) && answer.Score is not null)
                {
                    scores[c.Id] = answer.Score.Score;
                }
            }

            return scores;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Jev companion rerank crashed; keeping hybrid ordering.");
            return null;
        }
    }

    /// <summary>
    /// Code-owned blend of the rerank and hybrid scores — rerank dominant,
    /// hybrid as tiebreak so equal-relevance candidates keep their original
    /// (embedding+keyword) order. Pure function so the weighting is testable.
    /// </summary>
    public static float Blend(float hybridScore, double rerankScore) =>
        (float)(rerankScore * 3.0) + (hybridScore * 0.25f);
}
