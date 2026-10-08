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
}
