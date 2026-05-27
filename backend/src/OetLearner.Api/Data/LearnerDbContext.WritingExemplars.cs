using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingExemplar> WritingExemplars => Set<WritingExemplar>();
    public DbSet<WritingExemplarAnnotation> WritingExemplarAnnotations => Set<WritingExemplarAnnotation>();
    public DbSet<WritingExemplarEmbedding> WritingExemplarEmbeddings => Set<WritingExemplarEmbedding>();

    partial void OnModelCreatingWritingExemplars(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingExemplar>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AnnotationsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(x => new { x.Profession, x.LetterType });
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ScenarioId);
        });

        modelBuilder.Entity<WritingExemplarAnnotation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ExemplarId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<WritingExemplarEmbedding>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExemplarId).IsUnique();
            // pgvector column — only mapped under Npgsql because the SQLite /
            // in-memory test providers do not understand the `vector(n)` type.
            // The legacy EmbeddingJson column remains the source of truth; the
            // Vector mirror is populated by WritingExemplarEmbeddingService at
            // write time and by BackfillFromJsonAsync for older rows.
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
