using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<AssessmentScoreConversionTable> AssessmentScoreConversionTables => Set<AssessmentScoreConversionTable>();
    public DbSet<AssessmentScoreConversionRow> AssessmentScoreConversionRows => Set<AssessmentScoreConversionRow>();
    public DbSet<AssessmentMarkingPolicyVersion> AssessmentMarkingPolicyVersions => Set<AssessmentMarkingPolicyVersion>();
    public DbSet<AssessmentRationale> AssessmentRationales => Set<AssessmentRationale>();
    public DbSet<AssessmentReMarkJob> AssessmentReMarkJobs => Set<AssessmentReMarkJob>();

    partial void OnModelCreatingAssessmentGovernance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentScoreConversionTable>(entity =>
        {
            entity.Property(x => x.Assessment).HasMaxLength(16);
            entity.Property(x => x.ScopeKey).HasMaxLength(64);
            entity.Property(x => x.VersionKey).HasMaxLength(64);
            entity.HasMany(x => x.Rows)
                .WithOne(x => x.Table)
                .HasForeignKey(x => x.TableId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssessmentScoreConversionRow>(entity =>
        {
            entity.Property(x => x.Grade).HasMaxLength(16);
            entity.HasCheckConstraint("CK_AssessmentScoreConversionRow_RawScore", "\"RawScore\" BETWEEN 0 AND 42");
            entity.HasCheckConstraint("CK_AssessmentScoreConversionRow_ConvertedScore", "\"ConvertedScore\" BETWEEN 0 AND 500");
        });

        modelBuilder.Entity<AssessmentMarkingPolicyVersion>(entity =>
        {
            entity.Property(x => x.PolicyJson).HasColumnType("text");
        });

        modelBuilder.Entity<AssessmentRationale>(entity =>
        {
            entity.Property(x => x.SourceSentence).HasColumnType("text");
            entity.Property(x => x.RationaleText).HasColumnType("text");
        });

        modelBuilder.Entity<AssessmentReMarkJob>(entity =>
        {
            entity.Property(x => x.OriginalKeySnapshotJson).HasColumnType("text");
            entity.Property(x => x.NewKeySnapshotJson).HasColumnType("text");
            entity.Property(x => x.AffectedAttemptIdsJson).HasColumnType("text");
        });
    }
}
