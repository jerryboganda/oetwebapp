using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingDrillItem> SpeakingDrillItems => Set<SpeakingDrillItem>();
    public DbSet<SpeakingDrillAttempt> SpeakingDrillAttempts => Set<SpeakingDrillAttempt>();

    partial void OnModelCreatingSpeakingDrills(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingDrillItem>(e =>
        {
            e.HasOne(x => x.ContentItem)
                .WithMany()
                .HasForeignKey(x => x.ContentItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingDrillAttempt>(e =>
        {
            e.HasOne(x => x.DrillItem)
                .WithMany()
                .HasForeignKey(x => x.DrillItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
