using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

/// <summary>What the document indexer should do with one material PDF once the remote path has been consulted.</summary>
public enum CompanionPrepAction
{
    /// <summary>Extract and chunk in-process, exactly as before.</summary>
    Local,

    /// <summary>A remote job owns this PDF and has not finished: leave it for the next reindex (enqueue-and-poll).</summary>
    Pending,

    /// <summary>Nothing may be done (a quarantined or non-locally-recoverable job an admin must act on); a warning explains why.</summary>
    Skip,

    /// <summary>The chunks are known: write them (embeddings and the hash-gated commit stay on the API).</summary>
    Ready,
}

/// <summary>The remote path's answer for one PDF. <see cref="Chunks"/> and <see cref="Version"/> are set only for <see cref="CompanionPrepAction.Ready"/>.</summary>
public sealed record CompanionPrepPlan(
    CompanionPrepAction Action,
    string? Note = null,
    string? JobId = null,
    string? Version = null,
    IReadOnlyList<CompanionChunkDraft>? Chunks = null)
{
    public static readonly CompanionPrepPlan UseLocal = new(CompanionPrepAction.Local);
}

/// <summary>
/// Optional remote preparation of the document index (<c>companion.index-prep</c>, OET-RWP/1 section 6.2): a helper extracts the pages
/// and runs <see cref="CompanionChunker"/>; the API keeps embeddings, the corpus guard and <c>CompanionIndexWriter</c>'s hash-gated
/// commit. Not registered at all unless the configured provider is PostgreSQL, in which case the indexer behaves exactly as before.
/// </summary>
public interface IRemoteCompanionIndexPrep
{
    /// <summary>Decides, for one material PDF, whether to use a finished remote result, wait for one, or extract locally.</summary>
    Task<CompanionPrepPlan> PlanAsync(MediaAsset asset, string sourceKey, CancellationToken ct);

    /// <summary>
    /// Called after a <see cref="CompanionPrepAction.Ready"/> plan was written. When the write was clean (no warnings, so every chunk
    /// that needed a vector has one) the parked result is cleared and the job marked consumed; otherwise it stays so a later pass can
    /// retry the embeddings without re-extracting.
    /// </summary>
    Task CompleteAsync(CompanionPrepPlan plan, bool fullyCommitted, CancellationToken ct);
}
