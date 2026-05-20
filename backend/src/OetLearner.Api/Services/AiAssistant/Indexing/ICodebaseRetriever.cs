namespace OetLearner.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Retrieves relevant code chunks from the indexed codebase using hybrid
/// vector + keyword search.
/// </summary>
public interface ICodebaseRetriever
{
    /// <summary>
    /// Search the indexed codebase for chunks relevant to the query.
    /// Uses hybrid search: 0.7 * vector similarity + 0.3 * keyword score.
    /// </summary>
    Task<List<RetrievalResult>> RetrieveAsync(string query, int maxResults, CancellationToken ct);
}

/// <summary>A single retrieval result with relevance score.</summary>
public record RetrievalResult(
    string FilePath,
    int StartLine,
    int EndLine,
    string Content,
    string? Symbol,
    float Score);
