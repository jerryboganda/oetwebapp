using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingReadinessScore> WritingReadinessScores => Set<WritingReadinessScore>();
    public DbSet<WritingDraftV2> WritingDraftsV2 => Set<WritingDraftV2>();
    public DbSet<WritingPathwayItem> WritingPathwayItems => Set<WritingPathwayItem>();
    public DbSet<WritingCaseNoteHighlight> WritingCaseNoteHighlights => Set<WritingCaseNoteHighlight>();

    partial void OnModelCreatingWritingReadiness(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingReadinessScore>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            e.HasIndex(x => x.ComputedAt);
        });

        modelBuilder.Entity<WritingDraftV2>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ScenarioId, x.Mode }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.LastSavedAt })
                .IsDescending(false, true);
        });

        modelBuilder.Entity<WritingPathwayItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PathwayId, x.OrderIndex });
            e.HasIndex(x => new { x.PathwayId, x.Status });
        });

        modelBuilder.Entity<WritingCaseNoteHighlight>(e =>
        {
            e.HasKey(x => x.Id);
            // One highlight set per learner per scenario (upserted as marks change).
            e.HasIndex(x => new { x.UserId, x.ScenarioId }).IsUnique();
            e.Property(x => x.HighlightsJson).HasColumnType("jsonb").HasDefaultValue("{}");
        });
    }
}
