using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<ScoringPolicy> ScoringPolicies => Set<ScoringPolicy>();

    partial void OnModelCreatingScoringPolicy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScoringPolicy>(e =>
        {
            e.Property(x => x.BodyMarkdown).HasColumnType("text");
            e.Property(x => x.PolicyJson).HasColumnType("text");
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("\"IsActive\" = TRUE");
        });
    }
}
