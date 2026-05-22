using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<ChurnRiskSnapshot> ChurnRiskSnapshots => Set<ChurnRiskSnapshot>();
    public DbSet<UsageForecastSnapshot> UsageForecastSnapshots => Set<UsageForecastSnapshot>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<PricingExperiment> PricingExperiments => Set<PricingExperiment>();
    public DbSet<PricingExperimentAssignment> PricingExperimentAssignments => Set<PricingExperimentAssignment>();

    partial void OnModelCreatingChurnAndFx(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChurnRiskSnapshot>(e =>
        {
            e.Property(x => x.RiskScore).HasColumnType("numeric(6,4)");
        });

        modelBuilder.Entity<UsageForecastSnapshot>(e =>
        {
            e.Property(x => x.ForecastCostUsd).HasColumnType("numeric(12,4)");
            e.Property(x => x.Ema30DailyCalls).HasColumnType("numeric(12,3)");
        });

        modelBuilder.Entity<ExchangeRate>(e =>
        {
            e.Property(x => x.Rate).HasColumnType("numeric(18,8)");
        });

        modelBuilder.Entity<PricingExperimentAssignment>(e =>
        {
            e.Property(x => x.ConvertedAmount).HasColumnType("numeric(12,2)");
        });
    }
}
