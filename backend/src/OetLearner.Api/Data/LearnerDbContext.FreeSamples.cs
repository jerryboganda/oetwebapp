using OetLearner.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Data;

// Free Mocks (2026-09-22): per-profession free-sample designation + the
// per-learner once-only claim. Hand-authored migration
// 20270102090000_AddFreeSampleDesignationsAndClaims (snapshot left as-is).
public partial class LearnerDbContext
{
    public DbSet<FreeSampleDesignation> FreeSampleDesignations => Set<FreeSampleDesignation>();
    public DbSet<FreeSampleClaim> FreeSampleClaims => Set<FreeSampleClaim>();

    partial void OnModelCreatingFreeSamples(ModelBuilder modelBuilder)
    {
        // Index names match the migration so a future EF diff stays quiet.
        modelBuilder.Entity<FreeSampleDesignation>()
            .HasIndex(x => new { x.Subtest, x.Profession })
            .IsUnique()
            .HasDatabaseName("IX_FreeSampleDesignations_Subtest_Profession");
        modelBuilder.Entity<FreeSampleClaim>()
            .HasIndex(x => new { x.UserId, x.Subtest })
            .IsUnique()
            .HasDatabaseName("IX_FreeSampleClaims_UserId_Subtest");
    }
}
