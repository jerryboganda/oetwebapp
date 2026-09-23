using OetLearner.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Data;

// Free Mocks (2026-09-22): per-profession free-sample designation + the
// per-learner claim. Hand-authored migrations
// 20270102090100_AddFreeSampleDesignationsAndClaims and (23 Sep 2026, two
// successful results per subtest) 20270102090200_AddFreeSampleUses
// (ModelSnapshot updated).
public partial class LearnerDbContext
{
    public DbSet<FreeSampleDesignation> FreeSampleDesignations => Set<FreeSampleDesignation>();
    public DbSet<FreeSampleClaim> FreeSampleClaims => Set<FreeSampleClaim>();
    public DbSet<FreeSampleUse> FreeSampleUses => Set<FreeSampleUse>();

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
        modelBuilder.Entity<FreeSampleUse>()
            .HasOne<FreeSampleClaim>()
            .WithMany()
            .HasForeignKey(x => x.ClaimId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FreeSampleUse>()
            .HasIndex(x => x.ResourceId)
            .IsUnique()
            .HasDatabaseName("IX_FreeSampleUses_ResourceId");
    }
}
