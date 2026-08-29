using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W3 leftover of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// DbSets + Fluent configuration for circuit breakers, audited budget overrides,
/// and the 50/75/90/100 budget-alert ladder.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiCircuitState> AiCircuitStates => Set<AiCircuitState>();
    public DbSet<AiBudgetOverride> AiBudgetOverrides => Set<AiBudgetOverride>();
    public DbSet<AiBudgetAlert> AiBudgetAlerts => Set<AiBudgetAlert>();

    partial void OnModelCreatingAiCircuits(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiCircuitState>(entity =>
        {
            entity.HasIndex(x => new { x.Kind, x.Key })
                .IsUnique()
                .HasDatabaseName("UX_AiCircuitStates_Kind_Key");
        });

        modelBuilder.Entity<AiBudgetOverride>(entity =>
        {
            entity.HasIndex(x => new { x.Scope, x.ExpiresAt })
                .HasDatabaseName("IX_AiBudgetOverrides_Scope_ExpiresAt");
        });

        modelBuilder.Entity<AiBudgetAlert>(entity =>
        {
            entity.HasIndex(x => new { x.Scope, x.PeriodKey, x.ThresholdPct })
                .IsUnique()
                .HasDatabaseName("UX_AiBudgetAlerts_Scope_PeriodKey_ThresholdPct");
        });
    }
}
