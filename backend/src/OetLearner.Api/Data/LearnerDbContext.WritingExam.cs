using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingAttemptEvent> WritingAttemptEvents => Set<WritingAttemptEvent>();
    public DbSet<WritingFeedbackAnnotation> WritingFeedbackAnnotations => Set<WritingFeedbackAnnotation>();
    public DbSet<WritingModeration> WritingModerations => Set<WritingModeration>();
    public DbSet<WritingResultVisibilityConfig> WritingResultVisibilityConfigs => Set<WritingResultVisibilityConfig>();

    partial void OnModelCreatingWritingExam(ModelBuilder modelBuilder)
    {
        // Extra jsonb columns added to the existing WritingScenario by the exam
        // closure. EF merges these with the config in OnModelCreatingWritingScenarios.
        modelBuilder.Entity<WritingScenario>(e =>
        {
            e.Property(x => x.FixedInstructionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.RetakePolicyJson).HasColumnType("jsonb");
            e.HasIndex(x => x.InternalCode);
            e.HasIndex(x => x.SourceContentPaperId);
        });

        modelBuilder.Entity<WritingAttemptEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => new { x.UserId, x.SessionId });
            e.HasIndex(x => new { x.SessionId, x.Timestamp });
        });

        modelBuilder.Entity<WritingFeedbackAnnotation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => x.ReviewId);
        });

        modelBuilder.Entity<WritingModeration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FirstScoreJson).HasColumnType("jsonb");
            e.Property(x => x.SecondScoreJson).HasColumnType("jsonb");
            e.Property(x => x.FinalScoreJson).HasColumnType("jsonb");
            e.HasIndex(x => x.SubmissionId).IsUnique();
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<WritingResultVisibilityConfig>(e =>
        {
            e.HasKey(x => x.Id);
            // One global default ("global") + at most one row per scenario override.
            e.HasIndex(x => x.ScenarioId).IsUnique();
        });
    }
}
