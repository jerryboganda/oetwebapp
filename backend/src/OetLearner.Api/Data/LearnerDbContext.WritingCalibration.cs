using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingCalibrationLetter> WritingCalibrationLetters => Set<WritingCalibrationLetter>();
    public DbSet<WritingCalibrationRun> WritingCalibrationRuns => Set<WritingCalibrationRun>();
    public DbSet<WritingCalibrationResult> WritingCalibrationResults => Set<WritingCalibrationResult>();

    partial void OnModelCreatingWritingCalibration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingCalibrationLetter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DrAhmedGradeJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasIndex(x => x.AuthorTier);
            e.HasIndex(x => x.ScenarioId);
        });

        modelBuilder.Entity<WritingCalibrationRun>(e =>
        {
            e.HasKey(x => x.Id);
            // Latest run lookup: ORDER BY RunDate DESC LIMIT 1.
            e.HasIndex(x => x.RunDate);
            e.HasIndex(x => x.ModelVersion);
        });

        modelBuilder.Entity<WritingCalibrationResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AiGradeJson).HasColumnType("jsonb").HasDefaultValue("{}");
            // Run report query: WHERE RunId = ? ORDER BY AbsErrorRaw DESC
            e.HasIndex(x => new { x.RunId, x.AbsErrorRaw });
            e.HasIndex(x => x.CalibrationLetterId);
        });
    }
}
