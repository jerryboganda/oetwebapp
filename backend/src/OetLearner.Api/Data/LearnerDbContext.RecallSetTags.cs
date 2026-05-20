using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<RecallSetTag> RecallSetTags => Set<RecallSetTag>();

    partial void OnModelCreatingRecallSetTags(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecallSetTag>(e =>
        {
            e.Property(x => x.Description).HasColumnType("text");
        });
    }
}
