using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OetWithDrHesham.Api.Data;

namespace OetWithDrHesham.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Retrieves relevant code chunks using a hybrid approach:
/// 1. Vector similarity via pgvector's cosine distance operator (&lt;=&gt;)
/// 2. BM25-style keyword matching via PostgreSQL full-text search (ts_rank)
///
/// Final score = 0.7 * vectorScore + 0.3 * keywordScore
///
/// Gracefully falls back to keyword-only search when pgvector is not available.
///
/// Docker/Migration Notes:
/// - PostgreSQL needs pgvector extension: CREATE EXTENSION IF NOT EXISTS vector;
/// - The vector search SQL uses pgvector's &lt;=&gt; (cosine distance) operator:
///   SELECT *, 1 - (embedding &lt;=&gt; @queryEmbedding) AS score
///   FROM "AiCodebaseChunks" ORDER BY embedding &lt;=&gt; @queryEmbedding LIMIT @maxResults
/// </summary>
public sealed class CodebaseRetriever : ICodebaseRetriever
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<CodebaseRetriever> _logger;

    private const float VectorWeight = 0.7f;
    private const float KeywordWeight = 0.3f;
    private const int DefaultMaxResults = 10;

    public CodebaseRetriever(
        IServiceScopeFactory scopeFactory,
        IEmbeddingService embeddingService,
        ILogger<CodebaseRetriever> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<RetrievalResult>> RetrieveAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<RetrievalResult>();

        maxResults = maxResults <= 0 ? DefaultMaxResults : Math.Min(maxResults, 50);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        // Try vector search first
        List<RetrievalResult>? vectorResults = null;
        try
        {
            vectorResults = await VectorSearchAsync(db, query, maxResults, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed (pgvector may not be installed). Falling back to keyword search.");
        }

        // Always do keyword search for hybrid scoring
        List<RetrievalResult> keywordResults;
        try
        {
            keywordResults = await KeywordSearchAsync(db, query, maxResults * 2, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keyword search failed. Falling back to simple LIKE search.");
            keywordResults = await SimpleLikeSearchAsync(db, query, maxResults, ct);
        }

        // If vector search succeeded, combine results
        if (vectorResults != null && vectorResults.Count > 0)
        {
            return CombineResults(vectorResults, keywordResults, maxResults);
        }

        // Keyword-only fallback
        return keywordResults.Take(maxResults).ToList();
    }

    private async Task<List<RetrievalResult>> VectorSearchAsync(
        LearnerDbContext db, string query, int maxResults, CancellationToken ct)
    {
        var queryEmbedding = await _embeddingService.EmbedAsync(query, ct);
        var embeddingStr = "[" + string.Join(",", queryEmbedding) + "]";

        // Use raw SQL with pgvector's cosine distance operator
        var sql = @"
            SELECT ""Id"", ""FilePath"", ""StartLine"", ""EndLine"", ""Content"", ""Symbol"",
                   1 - (""Embedding"" <=> @queryEmbedding::vector) AS ""Score""
            FROM ""AiCodebaseChunks""
            ORDER BY ""Embedding"" <=> @queryEmbedding::vector
            LIMIT @maxResults";

        var results = new List<RetrievalResult>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var embParam = command.CreateParameter();
            embParam.ParameterName = "@queryEmbedding";
            embParam.Value = embeddingStr;
            command.Parameters.Add(embParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "@maxResults";
            limitParam.Value = maxResults;
            command.Parameters.Add(limitParam);

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new RetrievalResult(
                    FilePath: reader.GetString(reader.GetOrdinal("FilePath")),
                    StartLine: reader.GetInt32(reader.GetOrdinal("StartLine")),
                    EndLine: reader.GetInt32(reader.GetOrdinal("EndLine")),
                    Content: reader.GetString(reader.GetOrdinal("Content")),
                    Symbol: reader.IsDBNull(reader.GetOrdinal("Symbol")) ? null : reader.GetString(reader.GetOrdinal("Symbol")),
                    Score: reader.GetFloat(reader.GetOrdinal("Score"))
                ));
            }
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }

        return results;
    }

    private async Task<List<RetrievalResult>> KeywordSearchAsync(
        LearnerDbContext db, string query, int maxResults, CancellationToken ct)
    {
        // Use PostgreSQL full-text search with ts_rank
        var tsQuery = BuildTsQuery(query);

        var sql = @"
            SELECT ""FilePath"", ""StartLine"", ""EndLine"", ""Content"", ""Symbol"",
                   ts_rank(to_tsvector('english', ""Content""), to_tsquery('english', @tsQuery)) AS ""Score""
            FROM ""AiCodebaseChunks""
            WHERE to_tsvector('english', ""Content"") @@ to_tsquery('english', @tsQuery)
            ORDER BY ""Score"" DESC
            LIMIT @maxResults";

        var results = new List<RetrievalResult>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var queryParam = command.CreateParameter();
            queryParam.ParameterName = "@tsQuery";
            queryParam.Value = tsQuery;
            command.Parameters.Add(queryParam);

            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "@maxResults";
            limitParam.Value = maxResults;
            command.Parameters.Add(limitParam);

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new RetrievalResult(
                    FilePath: reader.GetString(reader.GetOrdinal("FilePath")),
                    StartLine: reader.GetInt32(reader.GetOrdinal("StartLine")),
                    EndLine: reader.GetInt32(reader.GetOrdinal("EndLine")),
                    Content: reader.GetString(reader.GetOrdinal("Content")),
                    Symbol: reader.IsDBNull(reader.GetOrdinal("Symbol")) ? null : reader.GetString(reader.GetOrdinal("Symbol")),
                    Score: reader.GetFloat(reader.GetOrdinal("Score"))
                ));
            }
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }

        return results;
    }

    private async Task<List<RetrievalResult>> SimpleLikeSearchAsync(
        LearnerDbContext db, string query, int maxResults, CancellationToken ct)
    {
        // Ultimate fallback: simple LIKE search via EF Core
        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length > 2)
            .Take(5)
            .ToList();

        if (keywords.Count == 0)
            return new List<RetrievalResult>();

        var dbQuery = db.AiCodebaseChunks.AsQueryable();

        // Filter by any keyword match
        foreach (var keyword in keywords)
        {
            var kw = keyword; // Capture for closure
            dbQuery = dbQuery.Where(c => c.Content.Contains(kw) || (c.SymbolName != null && c.SymbolName.Contains(kw)));
        }

        var chunks = await dbQuery
            .OrderByDescending(c => c.IndexedAt)
            .Take(maxResults)
            .ToListAsync(ct);

        return chunks.Select((c, i) => new RetrievalResult(
            c.FilePath, c.StartLine, c.EndLine, c.Content, c.SymbolName,
            Score: 1.0f - (i * 0.05f) // Decreasing score by position
        )).ToList();
    }

    private static List<RetrievalResult> CombineResults(
        List<RetrievalResult> vectorResults,
        List<RetrievalResult> keywordResults,
        int maxResults)
    {
        // Normalize scores to [0, 1]
        var maxVectorScore = vectorResults.Count > 0 ? vectorResults.Max(r => r.Score) : 1f;
        var maxKeywordScore = keywordResults.Count > 0 ? keywordResults.Max(r => r.Score) : 1f;

        if (maxVectorScore <= 0) maxVectorScore = 1f;
        if (maxKeywordScore <= 0) maxKeywordScore = 1f;

        var combined = new Dictionary<string, (RetrievalResult result, float score)>();

        // Add vector results
        foreach (var r in vectorResults)
        {
            var key = $"{r.FilePath}:{r.StartLine}-{r.EndLine}";
            var normalizedScore = VectorWeight * (r.Score / maxVectorScore);
            combined[key] = (r, normalizedScore);
        }

        // Merge keyword results
        foreach (var r in keywordResults)
        {
            var key = $"{r.FilePath}:{r.StartLine}-{r.EndLine}";
            var keywordScore = KeywordWeight * (r.Score / maxKeywordScore);

            if (combined.TryGetValue(key, out var existing))
            {
                combined[key] = (existing.result, existing.score + keywordScore);
            }
            else
            {
                combined[key] = (r, keywordScore);
            }
        }

        return combined.Values
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.result with { Score = x.score })
            .ToList();
    }

    /// <summary>
    /// Builds a PostgreSQL tsquery string from a natural language query.
    /// Joins terms with &amp; (AND) for relevance.
    /// </summary>
    private static string BuildTsQuery(string query)
    {
        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1)
            .Select(t => t.Replace("'", "").Replace("\\", "").Replace("&", "").Replace("|", "").Replace("!", "").Replace("(", "").Replace(")", ""))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(10)
            .ToList();

        if (terms.Count == 0)
            return "''";

        return string.Join(" | ", terms);
    }
}
