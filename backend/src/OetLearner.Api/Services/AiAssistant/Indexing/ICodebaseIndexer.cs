using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OetLearner.Api.Domain.AiAssistant;

namespace OetLearner.Api.Services.AiAssistant.Indexing;

public sealed class IndexingStatus
{
    public int TotalFiles { get; init; }
    public int IndexedFiles { get; init; }
    public int PendingFiles { get; init; }
    public string? CurrentPath { get; init; }
    public bool IsRunning { get; init; }
}

public sealed class CodebaseSearchHit
{
    public AiCodebaseChunk Chunk { get; init; } = default!;
    public double Score { get; init; }
}

public interface ICodebaseIndexer
{
    Task<IndexingStatus> GetStatusAsync(CancellationToken ct);

    // Full or incremental reindex.
    Task ReindexAsync(bool full, CancellationToken ct);

    // Hybrid (BM25 + vector) retrieval.
    Task<IReadOnlyList<CodebaseSearchHit>> SearchAsync(string query, int topK, CancellationToken ct);
}
