using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingSharedResource> SpeakingSharedResources => Set<SpeakingSharedResource>();

    partial void OnModelCreatingSpeakingSharedResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingSharedResource>(e =>
        {
            e.HasOne(x => x.MediaAsset)
                .WithMany()
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
