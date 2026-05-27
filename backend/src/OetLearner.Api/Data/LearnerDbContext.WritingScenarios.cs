using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingScenario> WritingScenarios => Set<WritingScenario>();
    public DbSet<WritingScenarioStructuredSentence> WritingScenarioStructuredSentences => Set<WritingScenarioStructuredSentence>();
    public DbSet<WritingScenarioEmbedding> WritingScenarioEmbeddings => Set<WritingScenarioEmbedding>();

    partial void OnModelCreatingWritingScenarios(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingScenario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TopicsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.CaseNotesStructuredJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Profession, x.LetterType });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.IsDiagnostic);
        });

        modelBuilder.Entity<WritingScenarioStructuredSentence>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ScenarioId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<WritingScenarioEmbedding>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ScenarioId).IsUnique();
            // pgvector column — see WritingExemplarEmbedding notes. Only mapped
            // under Npgsql; the SQLite / in-memory test providers ignore it.
            if (Database.IsNpgsql())
            {
                e.Property(x => x.Embedding).HasColumnType("vector(1536)");
            }
            else
            {
                e.Ignore(x => x.Embedding);
            }
        });
    }
}
