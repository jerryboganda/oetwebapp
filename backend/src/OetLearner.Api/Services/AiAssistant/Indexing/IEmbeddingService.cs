namespace OetLearner.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Generates vector embeddings for text content. Used by the RAG indexing
/// and retrieval pipeline.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embed a single text string.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct);

    /// <summary>Embed multiple texts in batches.</summary>
    Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
