namespace OetWithDrHesham.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Indexes the codebase by chunking files, computing embeddings, and
/// storing them in the database for later retrieval.
/// </summary>
public interface ICodebaseIndexer
{
    /// <summary>Index all supported files in the repository.</summary>
    Task IndexFullAsync(CancellationToken ct);

    /// <summary>Index or re-index a single file.</summary>
    Task IndexFileAsync(string filePath, CancellationToken ct);

    /// <summary>Get the current indexing status.</summary>
    Task<IndexingStatus> GetStatusAsync(CancellationToken ct);
}

/// <summary>Snapshot of the indexing progress.</summary>
public record IndexingStatus(
    bool IsRunning,
    int TotalFiles,
    int IndexedFiles,
    DateTimeOffset? LastCompleted);
