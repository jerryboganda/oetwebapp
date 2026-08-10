using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingAssessmentReportV11> WritingAssessmentReportsV11 => Set<WritingAssessmentReportV11>();
    public DbSet<WritingAssessmentFactEvidence> WritingAssessmentFacts => Set<WritingAssessmentFactEvidence>();
    public DbSet<WritingAssessmentError> WritingAssessmentErrors => Set<WritingAssessmentError>();
    public DbSet<WritingAssessmentCriterionEvidence> WritingAssessmentCriteria => Set<WritingAssessmentCriterionEvidence>();
    public DbSet<WritingAssessmentReleaseGate> WritingAssessmentReleaseGates => Set<WritingAssessmentReleaseGate>();
    public DbSet<WritingAssessmentPackVersion> WritingAssessmentPackVersions => Set<WritingAssessmentPackVersion>();
    public DbSet<WritingAssessmentModelAnswer> WritingAssessmentModelAnswers => Set<WritingAssessmentModelAnswer>();

    partial void OnModelCreatingWritingAssessmentV11(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingAssessmentReportV11>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ClassificationJson).HasColumnType("jsonb");
            entity.Property(x => x.FeatureRecordJson).HasColumnType("jsonb");
            entity.Property(x => x.TopPrioritiesJson).HasColumnType("jsonb");
            entity.Property(x => x.StrengthsJson).HasColumnType("jsonb");
            entity.Property(x => x.StudyPlanJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.SubmissionId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasOne<WritingSubmission>()
                .WithMany()
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Facts).WithOne().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Errors).WithOne().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Criteria).WithOne().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WritingAssessmentFactEvidence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ReportId, x.Classification });
        });

        modelBuilder.Entity<WritingAssessmentError>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SecondaryCriterionCodesJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ReportId, x.PrimaryCriterionCode });
            entity.HasIndex(x => new { x.ReportId, x.Severity });
        });

        modelBuilder.Entity<WritingAssessmentCriterionEvidence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EvidenceJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ReportId, x.CriterionCode }).IsUnique();
        });

        modelBuilder.Entity<WritingAssessmentReleaseGate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApprovalEvidenceJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ModelVersion, x.CalibrationSetVersion }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<WritingAssessmentPackVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RulesJson).HasColumnType("jsonb");
            entity.Property(x => x.ApprovalEvidenceJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.Profession, x.LetterType, x.VersionKey }).IsUnique();
            entity.HasIndex(x => new { x.Profession, x.LetterType, x.Status });
        });

        modelBuilder.Entity<WritingAssessmentModelAnswer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WhyThisWorksJson).HasColumnType("jsonb");
            entity.Property(x => x.GroundedFactReferencesJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.ReportId).IsUnique();
            entity.HasIndex(x => x.Status);
            entity.HasOne<WritingAssessmentReportV11>()
                .WithMany()
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
