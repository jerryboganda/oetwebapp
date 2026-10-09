using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>AI Pipeline Control Center: saved per-stage provider order, its revision history,
/// and the operator-entered credit grants behind the usage dashboard.</summary>
public partial class LearnerDbContext
{
    public DbSet<AiPipelineStage> AiPipelineStages => Set<AiPipelineStage>();
    public DbSet<AiPipelineStageRevision> AiPipelineStageRevisions => Set<AiPipelineStageRevision>();
    public DbSet<AiCreditGrant> AiCreditGrants => Set<AiCreditGrant>();

    // AI Pipeline Control Center phase 2: pins GrantUsd to the numeric(12,2) the
    // hand-authored 20270118090000_AddAiCreditGrants migration created, so the
    // model, the snapshot and the real table agree instead of drifting to the
    // decimal default precision.
    partial void OnModelCreatingAiPipeline(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiCreditGrant>(entity =>
        {
            entity.Property(x => x.GrantUsd).HasPrecision(12, 2);
        });
    }
}
