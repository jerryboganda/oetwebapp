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
    public DbSet<CompanionMemoryEntry> CompanionMemoryEntries => Set<CompanionMemoryEntry>();
    public DbSet<ErrorDnaEntry> ErrorDnaEntries => Set<ErrorDnaEntry>();
    public DbSet<CompanionJourney> CompanionJourneys => Set<CompanionJourney>();
    public DbSet<CompanionAvailability> CompanionAvailabilities => Set<CompanionAvailability>();
    public DbSet<AiCreditCost> AiCreditCosts => Set<AiCreditCost>();
    public DbSet<CompanionHandoff> CompanionHandoffs => Set<CompanionHandoff>();

    /// <summary>Per-user Sami (AI Learning Companion) access overrides, with provenance.
    /// Resolved by <see cref="Services.Companion.CompanionAccessResolver"/>.</summary>
    public DbSet<CompanionUserAccess> CompanionUserAccesses => Set<CompanionUserAccess>();

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

        // Companion memory spine (SAMI Wave 1): three layers on one namespaced
        // table. Current reads filter SupersededAt == null; history stays for
        // the journey view and audit. The (user, layer, kind, subtest) index
        // serves "the current value of this kind of fact for this learner".
        modelBuilder.Entity<CompanionMemoryEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Layer, x.Kind, x.Subtest });
            e.HasIndex(x => new { x.UserId, x.JourneyId });
            e.HasIndex(x => x.ConfirmedAt);
        });

        // Error DNA: upsert matching is by (user, patternKey); spaced review
        // scans by (user, nextReviewAt).
        modelBuilder.Entity<ErrorDnaEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.PatternKey }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.NextReviewAt });
            e.HasIndex(x => new { x.UserId, x.Category });
        });

        modelBuilder.Entity<CompanionJourney>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.IsActive });
        });

        modelBuilder.Entity<CompanionAvailability>(e =>
        {
            e.HasKey(x => x.UserId);
        });

        modelBuilder.Entity<AiCreditCost>(e =>
        {
            e.HasKey(x => x.ActionCode);
        });

        modelBuilder.Entity<CompanionHandoff>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        // Per-user companion access override (SAMI §9): at most one row per learner
        // per module, which is what makes the admin upsert a plain
        // "update the row if it exists". The single-column UserId index mirrors the
        // sibling override tables (UserModuleOverrides, PlanModuleOverrides) so the
        // "what is set for this learner" read is served the same way.
        modelBuilder.Entity<CompanionUserAccess>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.ModuleKey }).IsUnique();
        });
    }
}
