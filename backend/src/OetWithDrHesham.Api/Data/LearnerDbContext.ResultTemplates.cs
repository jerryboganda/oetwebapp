using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<ResultTemplateAsset> ResultTemplateAssets => Set<ResultTemplateAsset>();

    partial void OnModelCreatingResultTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResultTemplateAsset>(e =>
        {
            e.Property(x => x.Description).HasColumnType("text");
            e.HasOne(x => x.MediaAsset)
                .WithMany()
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
