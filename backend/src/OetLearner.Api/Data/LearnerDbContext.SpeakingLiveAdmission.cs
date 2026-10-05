using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

// Live AI Speaking admission control tables. Keys and indexes are declared on the entities (attributes),
// so no OnModelCreating hook is needed.
public partial class LearnerDbContext
{
    public DbSet<SpeakingLiveAdmission> SpeakingLiveAdmissions => Set<SpeakingLiveAdmission>();
    public DbSet<SpeakingLiveAdmissionSettings> SpeakingLiveAdmissionSettings => Set<SpeakingLiveAdmissionSettings>();
}
