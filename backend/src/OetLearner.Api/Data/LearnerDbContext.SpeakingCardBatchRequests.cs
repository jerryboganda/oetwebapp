using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

// Phase 11 (G.11.4) of the OET Speaking module roadmap.
//
// Adds the `SpeakingCardBatchRequest` table to `LearnerDbContext` via the
// existing partial-class pattern (mirrors LearnerDbContext.SpeakingDrills.cs).
// The partial `OnModelCreating` hook is empty — the entity already declares
// its indexes via attributes.
public partial class LearnerDbContext
{
    public DbSet<SpeakingCardBatchRequest> SpeakingCardBatchRequests => Set<SpeakingCardBatchRequest>();
}
