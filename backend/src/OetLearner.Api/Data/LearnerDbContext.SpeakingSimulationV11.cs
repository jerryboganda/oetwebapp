using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingSimulationV11SpecRelease> SpeakingSimulationV11SpecReleases => Set<SpeakingSimulationV11SpecRelease>();
    public DbSet<SpeakingSimulationV11RubricRelease> SpeakingSimulationV11RubricReleases => Set<SpeakingSimulationV11RubricRelease>();
    public DbSet<SpeakingSimulationV11OwnerApproval> SpeakingSimulationV11OwnerApprovals => Set<SpeakingSimulationV11OwnerApproval>();
    public DbSet<SpeakingSimulationV11Assessment> SpeakingSimulationV11Assessments => Set<SpeakingSimulationV11Assessment>();
    public DbSet<SpeakingSimulationV11PersonaSnapshot> SpeakingSimulationV11PersonaSnapshots => Set<SpeakingSimulationV11PersonaSnapshot>();
    public DbSet<SpeakingSimulationV11Evidence> SpeakingSimulationV11EvidenceRows => Set<SpeakingSimulationV11Evidence>();
    public DbSet<SpeakingSimulationV11CriterionScore> SpeakingSimulationV11CriterionScores => Set<SpeakingSimulationV11CriterionScore>();
    public DbSet<SpeakingSimulationV11TurnMetric> SpeakingSimulationV11TurnMetrics => Set<SpeakingSimulationV11TurnMetric>();
    public DbSet<SpeakingSimulationV11PersonaRuntimeSnapshot> SpeakingSimulationV11PersonaRuntimeSnapshots => Set<SpeakingSimulationV11PersonaRuntimeSnapshot>();
    public DbSet<SpeakingSimulationV11TurnEvidence> SpeakingSimulationV11TurnEvidenceRows => Set<SpeakingSimulationV11TurnEvidence>();
    public DbSet<SpeakingSimulationV11AudioQualityCheck> SpeakingSimulationV11AudioQualityChecks => Set<SpeakingSimulationV11AudioQualityCheck>();
    public DbSet<SpeakingSimulationV11CardTimingSnapshot> SpeakingSimulationV11CardTimingSnapshots => Set<SpeakingSimulationV11CardTimingSnapshot>();

    partial void OnModelCreatingSpeakingSimulationV11(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingSimulationV11SpecRelease>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SpecVersion, x.ReleaseVersion }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<SpeakingSimulationV11RubricRelease>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CriteriaJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.RubricVersion, x.CalibrationVersion }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<SpeakingSimulationV11OwnerApproval>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NumericValue).HasColumnType("numeric(18,2)");
            entity.Property(x => x.EvidenceJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ApprovalKey, x.ScopeKey, x.Status });
        });

        modelBuilder.Entity<SpeakingSimulationV11Assessment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConfidenceScore).HasColumnType("numeric(5,2)");
            entity.Property(x => x.GraphDisclaimer).HasColumnType("text");
            entity.HasIndex(x => new { x.ExamSessionId, x.SpeakingSessionId });
            entity.HasIndex(x => x.RolePlayCardId);
            entity.HasIndex(x => x.Status);
            entity.HasOne<SpeakingExamSession>()
                .WithMany()
                .HasForeignKey(x => x.ExamSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SpeakingSession>()
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RolePlayCard>()
                .WithMany()
                .HasForeignKey(x => x.RolePlayCardId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.PersonaSnapshots)
                .WithOne()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Evidence)
                .WithOne()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.CriterionScores)
                .WithOne()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.TurnMetrics)
                .WithOne()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakingSimulationV11PersonaSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PersonaJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.AssessmentId, x.PersonaRole });
            entity.HasOne<RolePlayCard>()
                .WithMany()
                .HasForeignKey(x => x.RolePlayCardId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingSimulationV11PersonaRuntimeSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AllowedFactsJson).HasColumnType("jsonb");
            entity.Property(x => x.ApprovedCarryFactKeysJson).HasColumnType("jsonb");
            entity.Property(x => x.ProhibitedFactsJson).HasColumnType("jsonb");
            entity.Property(x => x.RevealConditionsJson).HasColumnType("jsonb");
            entity.Property(x => x.CarriedFactsJson).HasColumnType("jsonb");
            entity.Property(x => x.PersonaJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.SpeakingSessionId).IsUnique();
            entity.HasIndex(x => new { x.ExamSessionId, x.CardSlot });
            entity.HasIndex(x => x.MemoryScopeKey).IsUnique();
            entity.HasOne<SpeakingExamSession>()
                .WithMany()
                .HasForeignKey(x => x.ExamSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SpeakingSession>()
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RolePlayCard>()
                .WithMany()
                .HasForeignKey(x => x.RolePlayCardId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SpeakingSimulationV11TurnEvidence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).HasColumnType("text");
            entity.Property(x => x.WordConfidenceJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.SpeakingSessionId, x.SourceTranscriptId, x.TurnNumber }).IsUnique();
            entity.HasIndex(x => x.AssessmentId);
            entity.HasIndex(x => x.SourceRecordingId);
            entity.HasOne<SpeakingSession>()
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SpeakingSimulationV11Assessment>()
                .WithMany()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SpeakingSimulationV11AudioQualityCheck>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DetailsJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.SpeakingSessionId, x.CheckedAt });
            entity.HasIndex(x => x.Status);
            entity.HasOne<SpeakingSession>()
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SpeakingSimulationV11Assessment>()
                .WithMany()
                .HasForeignKey(x => x.AssessmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SpeakingSimulationV11CardTimingSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.SpeakingSessionId, x.CapturedAt });
            entity.HasOne<SpeakingSession>()
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SpeakingExamSession>()
                .WithMany()
                .HasForeignKey(x => x.ExamSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<SpeakingSimulationV11Evidence>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QuoteText).HasColumnType("text");
            entity.Property(x => x.FindingText).HasColumnType("text");
            entity.Property(x => x.ActionSuggestion).HasColumnType("text");
            entity.Property(x => x.ConfidenceScore).HasColumnType("numeric(5,2)");
            entity.HasIndex(x => x.PrimaryCriterionCode);
            entity.HasIndex(x => x.GeneratedAt);
            entity.HasIndex(x => x.SourceTranscriptId);
            entity.HasIndex(x => x.SourceRecordingId);
        });

        modelBuilder.Entity<SpeakingSimulationV11CriterionScore>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RawScore).HasColumnType("numeric(8,2)");
            entity.Property(x => x.WeightedScore).HasColumnType("numeric(8,2)");
            entity.Property(x => x.Rationale).HasColumnType("text");
            entity.HasIndex(x => new { x.AssessmentId, x.CriterionCode }).IsUnique();
        });

        modelBuilder.Entity<SpeakingSimulationV11TurnMetric>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EstimatedCostUsd).HasColumnType("numeric(18,6)");
            entity.HasIndex(x => new { x.AssessmentId, x.TurnNumber }).IsUnique();
            entity.HasIndex(x => x.GeneratedAt);
        });
    }
}
