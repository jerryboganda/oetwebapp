using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain.AiAssistant;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    /// <summary>
    /// Model configuration for the AI-assistant indexing entities.
    ///
    /// <para>
    /// The important line is <see cref="AiCodebaseChunk"/>'s embedding. It was created as a plain
    /// <c>real[]</c> by <c>20260520182040_AddAiAssistantEntities</c> while every sibling embedding
    /// column in this model is <c>vector(1536)</c> and the provider has pgvector enabled. That
    /// mismatch is why <c>CodebaseRetriever</c>'s pgvector distance query could not run and
    /// retrieval silently fell back to keyword-only — the hybrid search was never hybrid.
    /// </para>
    ///
    /// <para>
    /// Declared explicitly here rather than relying on convention so the model and the migration
    /// (<c>20270119100000_CodebaseEmbeddingToVector</c>) agree; EF compares them on every build
    /// via the pending-model-changes check.
    /// </para>
    /// </summary>
    partial void OnModelCreatingAiAssistantIndexing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiCodebaseChunk>(entity =>
        {
            entity.HasKey(e => e.Id);

            // pgvector. Requires pgvector enabled on the connection
            // (DatabaseConfiguration.UseVector) — which it is.
            entity.Property(e => e.Embedding).HasColumnType("vector(1536)");

            // An unchanged chunk skips re-embedding on a re-index, which is what makes a full
            // re-index affordable against a mounted source tree.
            entity.HasIndex(e => e.ContentHash);
            entity.HasIndex(e => e.FilePath);
            entity.HasIndex(e => e.Language);
        });
    }
}