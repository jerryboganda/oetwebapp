using System.Data;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;

namespace OetLearner.Api.Services.Companion;

/// <summary>One packed piece of evidence, ready to cite.</summary>
public sealed record CompanionEvidence(
    Guid ChunkId,
    Guid SourceId,
    string SourceKey,
    string SourceTitle,
    CompanionAuthorityClass Authority,
    string? ProfessionId,
    string? SubtestCode,
    string? Heading,
    string Text,
    int? PageNumber,
    int? TimestampSeconds,
    bool IsProprietary,
    /// <summary>
    /// Watermark planted on this source, when it has one. Carried through to the
    /// output screen: emitting it verbatim means source text reached the answer
    /// unparaphrased, which is a security event rather than a formatting slip.
    /// </summary>
    string? CanaryTag,
    float Score);

public sealed record CompanionRetrievalResult(
    IReadOnlyList<CompanionEvidence> Evidence,
    bool VectorSearchUsed,
    bool Truncated,
    /// <summary>Set when sources of different authority disagree; surfaced, never blended.</summary>
    bool AuthorityConflict,
    IReadOnlyList<string> Trace);

public interface ICompanionRetriever
{
    Task<CompanionRetrievalResult> RetrieveAsync(
        string query,
        CompanionTurnContext context,
        int maxResults,
        CancellationToken ct);
}

/// <summary>
/// Entitlement-safe hybrid retrieval over the companion knowledge index.
///
/// <para><b>The ordering is the security control.</b> Step 1 narrows the
/// candidate <i>sources</i> using the server-resolved entitlement snapshot, and
/// every later step operates only on that set. Vector and keyword search never
/// see a source the learner is not entitled to, so no amount of prompt crafting
/// can surface one (F-154 — zero tolerance).</para>
///
/// <para>Modelled on <c>CodebaseRetriever</c>, including its graceful fallback:
/// if pgvector is unavailable the keyword path alone still answers, rather than
/// the companion failing.</para>
/// </summary>
public sealed class CompanionRetriever(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ICompanionExtractionBudget extractionBudget,
    ILogger<CompanionRetriever> logger) : ICompanionRetriever
{
    private const float VectorWeight = 0.7f;
    private const float KeywordWeight = 0.3f;

    /// <summary>
    /// Maximum characters of any single source reproduced in one turn. The
    /// companion teaches the rule; it does not reprint the rulebook.
    /// </summary>
    private const int MaxVerbatimCharsPerSource = 1200;

    /// <summary>Maximum chunks drawn from one source in a single turn.</summary>
    private const int MaxChunksPerSource = 3;

    public async Task<CompanionRetrievalResult> RetrieveAsync(
        string query,
        CompanionTurnContext context,
        int maxResults,
        CancellationToken ct)
    {
        var trace = new List<string>();

        if (!context.RetrievalEnabled)
        {
            trace.Add("retrieval.disabled_by_flag");
            return new CompanionRetrievalResult([], false, false, false, trace);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            trace.Add("retrieval.empty_query");
            return new CompanionRetrievalResult([], false, false, false, trace);
        }

        // ── Step 1: entitlement prefilter — BEFORE any search ────────────────
        var candidates = await ResolveCandidateSourcesAsync(context, ct);
        trace.Add($"prefilter.sources={candidates.Count}");

        if (candidates.Count == 0)
        {
            return new CompanionRetrievalResult([], false, false, false, trace);
        }

        var candidateIds = candidates.Keys.ToArray();

        // ── Step 2: hybrid search, restricted to the candidate set ───────────
        var vectorHits = new Dictionary<Guid, float>();
        var vectorUsed = false;

        try
        {
            vectorHits = await VectorSearchAsync(query, candidateIds, maxResults * 3, ct);
            vectorUsed = vectorHits.Count > 0;
            trace.Add($"vector.hits={vectorHits.Count}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Companion vector search failed; falling back to keyword only.");
            trace.Add("vector.failed_fallback_keyword");
        }

        var keywordHits = await KeywordSearchAsync(query, candidateIds, maxResults * 3, ct);
        trace.Add($"keyword.hits={keywordHits.Count}");

        // ── Step 3: merge and rank ───────────────────────────────────────────
        var merged = MergeScores(vectorHits, keywordHits);
        if (merged.Count == 0)
        {
            return new CompanionRetrievalResult([], vectorUsed, false, false, trace);
        }

        var chunkIds = merged.Keys.ToArray();
        var chunks = await db.CompanionChunks
            .AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .ToListAsync(ct);

        var ranked = chunks
            .Select(c =>
            {
                var source = candidates[c.SourceId];
                var score = merged[c.Id] * AuthorityWeight(source.AuthorityClass);

                // Profession-specific method outranks another profession's rule.
                if (!string.IsNullOrWhiteSpace(source.ProfessionId) &&
                    string.Equals(source.ProfessionId, context.ProfessionId, StringComparison.OrdinalIgnoreCase))
                {
                    score *= 1.25f;
                }

                // Subtest is a nudge, deliberately not a filter. The envelope
                // says where the learner IS, not what they ASKED: someone on a
                // reading page may perfectly well ask where their speaking cards
                // are, and a hard predicate would return nothing for them. A
                // boost makes the on-screen subtest win ties without ever
                // hiding the rest of the corpus.
                var surfaceSubtest = context.Envelope.SubtestCode;
                if (!string.IsNullOrWhiteSpace(source.SubtestCode) &&
                    !string.IsNullOrWhiteSpace(surfaceSubtest) &&
                    string.Equals(source.SubtestCode, surfaceSubtest, StringComparison.OrdinalIgnoreCase))
                {
                    score *= 1.15f;
                }

                return (Chunk: c, Source: source, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // ── Step 4: evidence packing with exfiltration caps ──────────────────
        var evidence = new List<CompanionEvidence>();
        var perSourceChunks = new Dictionary<Guid, int>();
        var perSourceChars = new Dictionary<Guid, int>();
        var exhaustedSources = new HashSet<Guid>();
        var truncated = false;

        foreach (var (chunk, source, score) in ranked)
        {
            if (evidence.Count >= maxResults) { truncated = true; break; }

            perSourceChunks.TryGetValue(source.Id, out var used);
            if (used >= MaxChunksPerSource) { truncated = true; continue; }

            perSourceChars.TryGetValue(source.Id, out var chars);

            // Two independent limits, and the tighter one wins. The per-turn cap
            // stops one greedy request; the rolling cap stops the chapter-walk
            // across turns that actually reconstructs a rulebook (GC-006).
            var budget = MaxVerbatimCharsPerSource - chars;
            if (source.IsProprietary)
            {
                var rolling = extractionBudget.RemainingChars(context.UserId, source.Id) - chars;
                if (rolling < budget) budget = rolling;
            }

            if (source.IsProprietary && budget <= 0)
            {
                truncated = true;
                exhaustedSources.Add(source.Id);
                continue;
            }

            var text = chunk.Text;
            if (source.IsProprietary && text.Length > budget)
            {
                text = text[..Math.Max(0, budget)];
                truncated = true;
            }

            if (string.IsNullOrWhiteSpace(text)) continue;

            perSourceChunks[source.Id] = used + 1;
            perSourceChars[source.Id] = chars + text.Length;

            evidence.Add(new CompanionEvidence(
                chunk.Id, source.Id, source.SourceKey, source.Title, source.AuthorityClass,
                source.ProfessionId, source.SubtestCode, chunk.Heading, text,
                chunk.PageNumber, chunk.TimestampSeconds, source.IsProprietary, source.CanaryTag, score));
        }

        // ── Step 4b: charge the rolling budget ───────────────────────────────
        // Charged after packing, so a learner is only billed for text that
        // actually reached the prompt.
        foreach (var (sourceId, packedChars) in perSourceChars)
        {
            if (candidates[sourceId].IsProprietary)
            {
                extractionBudget.Consume(context.UserId, sourceId, packedChars);
            }
        }

        if (exhaustedSources.Count > 0)
        {
            trace.Add($"extraction.budget_exhausted={exhaustedSources.Count}");
            logger.LogInformation(
                "Companion extraction budget exhausted for {UserId} on {Count} proprietary source(s); " +
                "further verbatim material withheld this window.",
                context.UserId, exhaustedSources.Count);
        }

        // ── Step 5: conflict detection — surface, never blend ────────────────
        var authorities = evidence.Select(e => e.Authority).Distinct().ToList();
        var conflict = authorities.Contains(CompanionAuthorityClass.OfficialCurrentFact)
                       && authorities.Any(a => a is CompanionAuthorityClass.DrHeshamApprovedMethod
                                                 or CompanionAuthorityClass.ProfessionApprovedMethod);

        if (conflict) trace.Add("authority.conflict_detected");
        trace.Add($"evidence.packed={evidence.Count}");

        return new CompanionRetrievalResult(evidence, vectorUsed, truncated, conflict, trace);
    }

    /// <summary>
    /// The entitlement prefilter. Only approved, in-scope, currently-effective
    /// sources survive, and a source demanding a scope the learner lacks is
    /// dropped here — before it can be searched.
    /// </summary>
    private async Task<Dictionary<Guid, CompanionSource>> ResolveCandidateSourcesAsync(
        CompanionTurnContext context,
        CancellationToken ct)
    {
        // Effectivity is resolved against the learner's EXAM date, not today.
        //
        // A candidate sitting the exam in three months must be taught the rules
        // in force *then*; a rule that supersedes on the first of next month is
        // already the right answer for them, and last year's version is not.
        // Resolving against UtcNow would teach whoever asks the rules of the day
        // they happened to ask, which is the whole reason the field exists
        // (Manifest §3 "exam + version/effective date").
        var today = context.ExamDate is { } examDate
            ? new DateTimeOffset(examDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var query = db.CompanionSources
            .AsNoTracking()
            .Where(s => s.State == CompanionSourceState.Approved)
            .Where(s => s.EffectiveFrom == null || s.EffectiveFrom <= today)
            .Where(s => s.EffectiveTo == null || s.EffectiveTo >= today)
            .Where(s => s.SupersededBySourceId == null);

        if (!string.IsNullOrWhiteSpace(context.ExamTypeCode))
        {
            var exam = context.ExamTypeCode;
            query = query.Where(s => s.ExamTypeCode == null || s.ExamTypeCode == exam);
        }

        // Profession scoping: a source with no profession applies to everyone;
        // a profession-scoped source only to that profession.
        var professionId = context.ProfessionId;
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            query = query.Where(s => s.ProfessionId == null || s.ProfessionId == professionId);
        }

        var sources = await query.ToListAsync(ct);

        // Entitlement scope is applied in memory because the comparison is a set
        // membership test against the resolved snapshot, not a column predicate.
        var scopes = new HashSet<string>(context.EntitlementScopes, StringComparer.OrdinalIgnoreCase);

        return sources
            .Where(s => s.RequiredEntitlementScope is null || scopes.Contains(s.RequiredEntitlementScope))
            .Where(s => IsInPackageScope(s, context))
            .ToDictionary(s => s.Id);
    }

    /// <summary>
    /// Package isolation (Manifest 1.B). A source with no scope, or the SHARED
    /// scope, reaches every entitled learner; a FULL_*/CRASH source reaches only
    /// learners whose packages resolve that same scope.
    ///
    /// <para>
    /// This is a <b>separate axis from profession</b>, and both apply. Profession
    /// answers "is this material about their job?" (GC-004, GC-007); package
    /// answers "did they buy this course?". Collapsing them would either leak
    /// Full Course material into Crash accounts or deny Crash learners the method
    /// they paid for, depending on which way it was collapsed.
    /// </para>
    /// </summary>
    private static bool IsInPackageScope(CompanionSource source, CompanionTurnContext context)
    {
        if (string.IsNullOrWhiteSpace(source.PackageScope)) return true;
        if (string.Equals(source.PackageScope, VideoVisibilityScopes.Shared, StringComparison.OrdinalIgnoreCase)) return true;

        return context.PackageScopes.Contains(source.PackageScope);
    }

    private async Task<Dictionary<Guid, float>> VectorSearchAsync(
        string query,
        Guid[] candidateSourceIds,
        int limit,
        CancellationToken ct)
    {
        var results = new Dictionary<Guid, float>();
        if (!db.Database.IsNpgsql()) return results;

        var vector = await embeddings.EmbedAsync(query, ct);
        if (vector.Length == 0) return results;

        var embeddingStr = "[" + string.Join(",", vector) + "]";

        // The SourceId filter is what keeps an unentitled source unreachable even
        // if it would otherwise be the nearest neighbour.
        const string sql = @"
            SELECT ""Id"", 1 - (""Embedding"" <=> @queryEmbedding::vector) AS ""Score""
            FROM ""CompanionChunks""
            WHERE ""Embedding"" IS NOT NULL
              AND ""SourceId"" = ANY(@sourceIds)
            ORDER BY ""Embedding"" <=> @queryEmbedding::vector
            LIMIT @maxResults";

        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
            opened = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var embParam = command.CreateParameter();
            embParam.ParameterName = "@queryEmbedding";
            embParam.Value = embeddingStr;
            command.Parameters.Add(embParam);

            var sourceParam = command.CreateParameter();
            sourceParam.ParameterName = "@sourceIds";
            sourceParam.Value = candidateSourceIds;
            command.Parameters.Add(sourceParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "@maxResults";
            limitParam.Value = limit;
            command.Parameters.Add(limitParam);

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var score = reader.IsDBNull(1) ? 0f : Convert.ToSingle(reader.GetValue(1));
                results[id] = score;
            }
        }
        finally
        {
            if (opened && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return results;
    }

    private async Task<Dictionary<Guid, float>> KeywordSearchAsync(
        string query,
        Guid[] candidateSourceIds,
        int limit,
        CancellationToken ct)
    {
        var terms = Tokenise(query);
        if (terms.Count == 0) return [];

        // Cheap containment match, scored by how many distinct query terms hit.
        // Deliberately provider-agnostic so it works on SQLite in tests too.
        var rows = await db.CompanionChunks
            .AsNoTracking()
            .Where(c => candidateSourceIds.Contains(c.SourceId))
            .Where(c => terms.Any(t => c.Text.ToLower().Contains(t)))
            .Select(c => new { c.Id, c.Text })
            .Take(limit)
            .ToListAsync(ct);

        var results = new Dictionary<Guid, float>();
        foreach (var row in rows)
        {
            var lower = row.Text.ToLowerInvariant();
            var hits = terms.Count(t => lower.Contains(t));
            results[row.Id] = (float)hits / terms.Count;
        }

        return results;
    }

    private static Dictionary<Guid, float> MergeScores(
        Dictionary<Guid, float> vectorHits,
        Dictionary<Guid, float> keywordHits)
    {
        var maxVector = vectorHits.Count > 0 ? vectorHits.Values.Max() : 1f;
        if (maxVector <= 0) maxVector = 1f;

        var merged = new Dictionary<Guid, float>();

        foreach (var (id, score) in vectorHits)
        {
            merged[id] = VectorWeight * (score / maxVector);
        }

        foreach (var (id, score) in keywordHits)
        {
            merged.TryGetValue(id, out var existing);
            merged[id] = existing + KeywordWeight * score;
        }

        return merged;
    }

    /// <summary>
    /// Authority precedence from the source specification: verified official fact
    /// first, then approved methodology, with profession-specific rules ahead of
    /// generic ones. Applied as a ranking multiplier so precedence survives into
    /// what actually reaches the prompt.
    /// </summary>
    private static float AuthorityWeight(CompanionAuthorityClass authority) => authority switch
    {
        CompanionAuthorityClass.OfficialCurrentFact => 1.6f,
        CompanionAuthorityClass.AdminOverride => 1.5f,
        CompanionAuthorityClass.ProfessionApprovedMethod => 1.3f,
        CompanionAuthorityClass.DrHeshamApprovedMethod => 1.2f,
        CompanionAuthorityClass.CandidateEvidence => 1.1f,
        CompanionAuthorityClass.PlatformSupport => 1.0f,
        CompanionAuthorityClass.CourseMaterial => 1.0f,
        _ => 1.0f,
    };

    private static List<string> Tokenise(string query) =>
        query.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', ',', '.', '?', '!', ';', ':', '(', ')', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .Distinct()
            .Take(12)
            .ToList();
}
