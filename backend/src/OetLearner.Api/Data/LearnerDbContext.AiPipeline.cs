using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>AI Pipeline Control Center: saved per-stage provider order and its revision history.</summary>
public partial class LearnerDbContext
{
    public DbSet<AiPipelineStage> AiPipelineStages => Set<AiPipelineStage>();
    public DbSet<AiPipelineStageRevision> AiPipelineStageRevisions => Set<AiPipelineStageRevision>();
}
