using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using Pgvector;

namespace OetLearner.Api.Services.Companion;

/// <summary>One chunk an indexer wants published, before it becomes a row.</summary>
public readonly record struct CompanionChunkDraft(
    string Heading,
    string Text,
    int? PageNumber = null,
    int? TimestampSeconds = null);

/// <summary>
/// The shared write half of every companion indexer: upsert the source, retire
/// its older versions, reconcile chunks by ordinal, embed what changed.
///
/// <para>
/// Extracted once there were more than two indexers, because the interesting
/// parts of that sequence are the ones easiest to get subtly wrong in a copy:
/// comparing the content hash <i>before</i> overwriting it (otherwise a changed
/// chunk keeps the embedding of its old text and retrieval silently rots),
/// treating "unchanged" as hash-equal <b>and</b> not-awaiting-an-embedding, and
/// superseding sibling versions rather than leaving two approved copies
/// answering side by side. Each indexer now supplies only what is actually
/// specific to it: the metadata and the chunks.
/// </para>
///
/// <para>
/// <b>The corpus guard runs here.</b> Screening in one place means a future
/// indexer cannot forget it — Manifest §5 is a property of the corpus boundary,
/// not of any one ingester. A contaminated source is rejected whole; a
/// half-indexed one is worse than an absent one because the operator believes
/// it is present.
/// </para>
/// </summary>
internal static class CompanionIndexWriter
{
    public static async Task<CompanionIndexResult> WriteAsync(
        LearnerDbContext db,
        IEmbeddingService embeddings,
        ILogger logger,
        string sourceKey,
        string version,
        Action<CompanionSource> configure,
        IReadOnlyList<CompanionChunkDraft> chunks,
        bool embed,
        List<string> warnings,
        CancellationToken ct)
    {
        if (chunks.Count == 0)
        {
            warnings.Add($"Source {sourceKey} produced no chunks; nothing indexed.");
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        foreach (var draft in chunks)
        {
            var marker = CompanionCorpusGuard.FindDisqualifyingMarker(draft.Heading)
                         ?? CompanionCorpusGuard.FindDisqualifyingMarker(draft.Text);
            if (marker is null) continue;

            var warning =
                $"Source {sourceKey} REJECTED: contains acceptance-test marker \"{marker}\". " +
                "This looks like Final Testing pack material, not teaching content. Indexing it " +
                "would invalidate every acceptance run. Nothing from this source was indexed.";
            warnings.Add(warning);
            logger.LogError("Companion corpus guard rejected {SourceKey}: marker {Marker}.", sourceKey, marker);
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        var now = DateTimeOffset.UtcNow;

        var source = await db.CompanionSources
            .FirstOrDefaultAsync(s => s.SourceKey == sourceKey && s.Version == version, ct);

        var sourcesWritten = 0;
        if (source is null)
        {
            source = new CompanionSource
            {
                Id = Guid.NewGuid(),
                SourceKey = sourceKey,
                Version = version,
                CreatedAt = now,
            };
            db.CompanionSources.Add(source);
            sourcesWritten++;
        }

        configure(source);
        source.SourceKey = sourceKey;
        source.Version = version;
        source.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        // Retire every other version of this source. Without it a version bump
        // leaves BOTH approved, neither superseded and neither date-bounded, so
        // the retriever returns them side by side and the model reconciles two
        // conflicting rules on its own (Manifest §4).
        var superseded = await db.CompanionSources
            .Where(s => s.SourceKey == sourceKey && s.Id != source.Id)
            .Where(s => s.SupersededBySourceId == null)
            .ToListAsync(ct);

        foreach (var old in superseded)
        {
            old.SupersededBySourceId = source.Id;
            old.State = CompanionSourceState.Superseded;
            old.UpdatedAt = now;
            warnings.Add(
                $"{sourceKey} version {old.Version} superseded by {version}; " +
                "it stays auditable but is no longer retrievable.");
        }

        if (superseded.Count > 0) await db.SaveChangesAsync(ct);

        var existing = await db.CompanionChunks
            .Where(c => c.SourceId == source.Id)
            .ToDictionaryAsync(c => c.Ordinal, ct);

        var pending = new List<CompanionChunk>();
        var written = 0;
        var unchanged = 0;

        for (var ordinal = 0; ordinal < chunks.Count; ordinal++)
        {
            ct.ThrowIfCancellationRequested();

            var draft = chunks[ordinal];
            var hash = Sha256(draft.Text);

            if (existing.TryGetValue(ordinal, out var chunk))
            {
                // "Unchanged" has to account for a chunk that is textually
                // identical but has never been embedded — otherwise the first
                // embedding pass after a keyword-only index would skip it
                // forever. Equally it must NOT demand an embedding on SQLite,
                // where the vector column does not exist and every chunk would
                // look permanently dirty.
                var needsEmbedding = embed && chunk.Embedding is null && db.Database.IsNpgsql();
                if (chunk.ContentHash == hash && !needsEmbedding)
                {
                    unchanged++;
                    continue;
                }

                var textChanged = chunk.ContentHash != hash;
                chunk.Heading = draft.Heading;
                chunk.Text = draft.Text;
                chunk.PageNumber = draft.PageNumber;
                chunk.TimestampSeconds = draft.TimestampSeconds;
                chunk.ContentHash = hash;
                chunk.UpdatedAt = now;
                if (textChanged) chunk.Embedding = null;
                pending.Add(chunk);
            }
            else
            {
                chunk = new CompanionChunk
                {
                    Id = Guid.NewGuid(),
                    SourceId = source.Id,
                    Ordinal = ordinal,
                    Heading = draft.Heading,
                    Text = draft.Text,
                    PageNumber = draft.PageNumber,
                    TimestampSeconds = draft.TimestampSeconds,
                    ContentHash = hash,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.CompanionChunks.Add(chunk);
                pending.Add(chunk);
            }

            written++;
        }

        // Chunks left over from a longer previous version of this source.
        var stale = existing.Where(kv => kv.Key >= chunks.Count).Select(kv => kv.Value).ToList();
        if (stale.Count > 0) db.CompanionChunks.RemoveRange(stale);

        await db.SaveChangesAsync(ct);

        var embedded = 0;
        if (embed && pending.Count > 0)
        {
            embedded = await EmbedAsync(db, embeddings, logger, pending, warnings, ct);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Companion index {SourceKey}@{Version}: {Written} chunks written, {Unchanged} unchanged, {Embedded} embedded.",
            sourceKey, version, written, unchanged, embedded);

        return new CompanionIndexResult(sourcesWritten, written, unchanged, embedded, warnings);
    }

    private static async Task<int> EmbedAsync(
        LearnerDbContext db,
        IEmbeddingService embeddings,
        ILogger logger,
        List<CompanionChunk> chunks,
        List<string> warnings,
        CancellationToken ct)
    {
        // The embedding provider is optional. Without it retrieval still works
        // through the keyword path, exactly as CodebaseRetriever degrades.
        if (!db.Database.IsNpgsql()) return 0;

        try
        {
            var vectors = await embeddings.EmbedBatchAsync(chunks.Select(c => c.Text).ToList(), ct);
            var embedded = 0;

            for (var i = 0; i < chunks.Count && i < vectors.Count; i++)
            {
                chunks[i].Embedding = new Vector(vectors[i]);
                embedded++;
            }

            return embedded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Companion embedding pass failed; chunks remain keyword-searchable.");
            warnings.Add($"Embedding failed ({ex.GetType().Name}); chunks indexed without vectors.");
            return 0;
        }
    }

    internal static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
