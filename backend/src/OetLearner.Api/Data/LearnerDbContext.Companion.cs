using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// AI Learning Companion knowledge index. See docs/ai-learning-companion/.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<CompanionSource> CompanionSources => Set<CompanionSource>();
    public DbSet<CompanionChunk> CompanionChunks => Set<CompanionChunk>();
    public DbSet<CompanionKnowledgeRelease> CompanionKnowledgeReleases => Set<CompanionKnowledgeRelease>();
    public DbSet<CompanionPreference> CompanionPreferences => Set<CompanionPreference>();

    partial void OnModelCreatingCompanion(ModelBuilder modelBuilder)
    {
        // One row per learner, keyed by user id — no surrogate key, because
        // "this learner's preferences" is exactly one thing.
        modelBuilder.Entity<CompanionPreference>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.TeachingStyle).HasConversion<int>();
            e.Property(x => x.Depth).HasConversion<int>();
        });

        modelBuilder.Entity<CompanionSource>(e =>
        {
            e.HasKey(x => x.Id);

            // A source key identifies one logical source; versions of it are
            // separate rows so history stays auditable (source rule: newer
            // approved versions win, old versions remain inspectable).
            e.HasIndex(x => new { x.SourceKey, x.Version }).IsUnique();

            // The retrieval prefilter selects on exactly these columns before
            // any vector search runs, so they are indexed together.
            e.HasIndex(x => new { x.State, x.AuthorityClass, x.ProfessionId, x.SubtestCode });
            e.HasIndex(x => x.RequiredEntitlementScope);
        });

        modelBuilder.Entity<CompanionChunk>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SourceId, x.Ordinal }).IsUnique();

            // Lets an unchanged chunk skip re-embedding on re-ingest.
            e.HasIndex(x => x.ContentHash);
            e.HasIndex(x => x.ReleaseId);

            e.HasOne<CompanionSource>()
                .WithMany()
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // pgvector column, mirroring WritingScenarioEmbedding.Embedding.
            // Only mapped under Npgsql; the SQLite / in-memory test providers
            // build straight from the model and cannot represent vector(n).
            if (Database.IsNpgsql())
            {
                e.Property(x => x.Embedding).HasColumnType("vector(1536)");
            }
            else
            {
                e.Ignore(x => x.Embedding);
            }
        });

        modelBuilder.Entity<CompanionKnowledgeRelease>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ReleaseVersion).IsUnique();
            e.HasIndex(x => x.Status);
        });
    }
}
