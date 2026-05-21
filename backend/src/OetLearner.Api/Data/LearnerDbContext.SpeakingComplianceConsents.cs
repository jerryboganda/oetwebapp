using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

// Phase 7 of the OET Speaking module roadmap.
//
// Adds the DbSet for the durable versioned consent records introduced in
// `SpeakingComplianceConsentEntities.cs`. The accompanying partial-method
// hook (`OnModelCreatingSpeakingComplianceConsents`) is not declared in
// the main `LearnerDbContext.cs` body yet — the foundation pass will add
// it alongside the Phase 7 EF migration. Until then, the public DbSet
// declaration here is enough for EF Core to pick the entity up, and the
// composite `[Index]` attributes on the entity itself supply the
// query-relevant indexing. The partial-filter index for active rows
// (used by the latest-active lookup) is added in the migration script
// so it never needs to live in the model.

public partial class LearnerDbContext
{
    public DbSet<SpeakingComplianceConsent> SpeakingComplianceConsents
        => Set<SpeakingComplianceConsent>();
}
