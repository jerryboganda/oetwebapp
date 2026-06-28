using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingSubmission> WritingSubmissions => Set<WritingSubmission>();
    public DbSet<WritingGrade> WritingGrades => Set<WritingGrade>();
    public DbSet<WritingScoreAppeal> WritingScoreAppeals => Set<WritingScoreAppeal>();

    partial void OnModelCreatingWritingSubmissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingSubmission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CaseNoteHighlightsJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasIndex(x => new { x.UserId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_WritingSubmissions_User_CreatedAt");
            e.HasIndex(x => x.Status)
                .HasFilter("\"Status\" IN ('queued','grading')")
                .HasDatabaseName("IX_WritingSubmissions_Status_Pending");
            e.HasIndex(x => x.LetterContentHash);
            e.HasIndex(x => new { x.UserId, x.ScenarioId });
            e.HasIndex(x => x.OriginalSubmissionId);
        });

        modelBuilder.Entity<WritingGrade>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PerCriterionFeedbackJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(x => x.TopThreePrioritiesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(x => x.SubmissionId).IsUnique();
            e.HasIndex(x => x.AppealedByGradeId);
            e.HasIndex(x => x.TutorReviewId);
        });

        modelBuilder.Entity<WritingScoreAppeal>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => x.OriginalGradeId);
        });
    }
}
