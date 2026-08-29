using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W10 — provider-benchmark runs that gate route switches.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiProviderBenchmarkRun> AiProviderBenchmarkRuns => Set<AiProviderBenchmarkRun>();

    partial void OnModelCreatingAiProviderBenchmarks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiProviderBenchmarkRun>(entity =>
        {
            entity.HasIndex(x => new { x.FeatureCode, x.ProviderCode, x.RecordedAt })
                .HasDatabaseName("IX_AiProviderBenchmarkRuns_Feature_Provider_Recorded");
        });
    }
}
