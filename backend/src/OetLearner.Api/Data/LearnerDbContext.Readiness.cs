using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<ReadinessHistory> ReadinessHistories => Set<ReadinessHistory>();

    partial void OnModelCreatingReadiness(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReadinessSnapshot>().HasIndex(x => new { x.UserId, x.ComputedAt });
        modelBuilder.Entity<ReadinessSnapshot>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<ReadinessSnapshot>().Property(x => x.OverallRisk).HasDefaultValue("Unknown");
        modelBuilder.Entity<ReadinessSnapshot>().Property(x => x.ConfidenceLevel).HasDefaultValue("Low");

        modelBuilder.Entity<ReadinessHistory>().HasIndex(x => new { x.UserId, x.WeekStartDate }).IsUnique();
        modelBuilder.Entity<ReadinessHistory>().HasIndex(x => x.RecordedAt);
        modelBuilder.Entity<ReadinessHistory>().Property(x => x.Risk).HasDefaultValue("Unknown");
    }
}
