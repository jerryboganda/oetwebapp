using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<RecallDocument> RecallDocuments => Set<RecallDocument>();

    partial void OnModelCreatingRecallDocuments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecallDocument>(e =>
        {
            e.Property(x => x.DescriptionMarkdown).HasColumnType("text");
            e.HasOne(x => x.MediaAsset)
                .WithMany()
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
