using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingDrill> WritingDrills => Set<WritingDrill>();
    public DbSet<WritingDrillAttempt> WritingDrillAttempts => Set<WritingDrillAttempt>();
    public DbSet<WritingCaseNoteDrill> WritingCaseNoteDrills => Set<WritingCaseNoteDrill>();
    public DbSet<WritingCaseNoteDrillSentence> WritingCaseNoteDrillSentences => Set<WritingCaseNoteDrillSentence>();
    public DbSet<WritingCaseNoteDrillAttempt> WritingCaseNoteDrillAttempts => Set<WritingCaseNoteDrillAttempt>();

    partial void OnModelCreatingWritingDrills(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingDrill>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AppliesToProfessionsJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.AppliesToLetterTypesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.AlternativesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.Property(x => x.GradingConfigJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasIndex(x => new { x.DrillType, x.Status });
            e.HasIndex(x => x.TargetSubSkill);
            e.HasIndex(x => x.TargetCanonRuleId);
        });

        modelBuilder.Entity<WritingDrillAttempt>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.DrillId, x.AttemptedAt })
                .IsDescending(false, false, true)
                .HasDatabaseName("IX_WritingDrillAttempts_User_Drill_Time");
            e.HasIndex(x => new { x.UserId, x.NextDueAt });
        });

        modelBuilder.Entity<WritingCaseNoteDrill>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Profession, x.LetterType, x.Status });
        });

        modelBuilder.Entity<WritingCaseNoteDrillSentence>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.DrillId, x.Ordinal }).IsUnique();
        });

        modelBuilder.Entity<WritingCaseNoteDrillAttempt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ResponsesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            e.HasIndex(x => new { x.UserId, x.DrillId, x.AttemptedAt })
                .IsDescending(false, false, true);
        });
    }
}
