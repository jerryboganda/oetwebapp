using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Indexing;

// Postgres + pgvector implementation of the codebase indexer.
// TODO Phase 2: requires `CREATE EXTENSION vector;` (see migration notes).
public sealed class PgVectorCodebaseIndexer : ICodebaseIndexer
{
    public PgVectorCodebaseIndexer()
    {
        // TODO Phase 2: inject OetDbContext, TreeSitterChunker, embedding client.
        // TODO: via IRuntimeSettingsProvider (embedding model + key)
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync (embedding API call)
    }

    public Task<IndexingStatus> GetStatusAsync(CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 2.");

    public Task ReindexAsync(bool full, CancellationToken ct)
    {
        // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 2.");
    }

    public Task<IReadOnlyList<CodebaseSearchHit>> SearchAsync(string query, int topK, CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 2: ivfflat + BM25 hybrid.");
}
