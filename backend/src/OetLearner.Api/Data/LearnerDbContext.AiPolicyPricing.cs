using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// DbSets + Fluent configuration for the versioned feature-policy registry
/// (<see cref="AiFeaturePolicy"/>) and the effective-dated pricing rate card
/// (<see cref="AiModelPrice"/>). Split from
/// <c>LearnerDbContext.AiControlPlane.cs</c> (W1) to keep both partials under
/// the repo's 500-line file guideline and because these two tables are
/// policy/pricing configuration, not operation state.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiFeaturePolicy> AiFeaturePolicies => Set<AiFeaturePolicy>();
    public DbSet<AiModelPrice> AiModelPrices => Set<AiModelPrice>();

    partial void OnModelCreatingAiPolicyPricing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiFeaturePolicy>(entity =>
        {
            // Attribute-declared [Index(FeatureCode, PolicyVersion, IsUnique)]
            // on the entity covers the uniqueness constraint; OperationClass
            // stores as its default int representation, matching AiOperation.
        });

        modelBuilder.Entity<AiModelPrice>(entity =>
        {
            // Attribute-declared [Index(ProviderId, Model, EffectiveFrom, IsUnique)]
            // on the entity covers the uniqueness constraint.
        });
    }
}
