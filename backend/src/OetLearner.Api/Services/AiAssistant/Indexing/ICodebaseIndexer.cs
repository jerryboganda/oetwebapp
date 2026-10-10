namespace OetLearner.Api.Services.AiAssistant.Indexing;

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
    DateTimeOffset? LastCompleted)
{
    /// <summary>Is a source checkout actually mounted and readable?</summary>
    /// <remarks>
    /// Distinguishes "indexed nothing" from "there is nothing here to index". Before this existed
    /// both reported zero files, so a deployment with no source mounted was indistinguishable from
    /// a working index that simply had no matches — which is how the admin chatbot ended up
    /// answering codebase questions from five consecutive empty searches.
    /// </remarks>
    public bool SourceAvailable { get; init; }

    /// <summary>Where the source was resolved from, or precisely why it could not be.</summary>
    public string? SourceRootReason { get; init; }

    /// <summary>The resolved source root, or null when none is available.</summary>
    public string? SourceRoot { get; init; }
}
