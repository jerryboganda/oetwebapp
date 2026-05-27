using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingTutorReview> WritingTutorReviews => Set<WritingTutorReview>();
    public DbSet<WritingTutorReviewAssignment> WritingTutorReviewAssignments => Set<WritingTutorReviewAssignment>();
    public DbSet<WritingTutorCalibration> WritingTutorCalibrations => Set<WritingTutorCalibration>();

    partial void OnModelCreatingWritingTutor(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingTutorReview>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PerCriterionCommentsJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.Property(x => x.ScoreOverrideJson).HasColumnType("jsonb");
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => new { x.TutorId, x.Status });
        });

        modelBuilder.Entity<WritingTutorReviewAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TutorId, x.Status });
            e.HasIndex(x => x.SubmissionId);
            e.HasIndex(x => x.DueAt);
        });

        modelBuilder.Entity<WritingTutorCalibration>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TutorId).IsUnique();
        });
    }
}
