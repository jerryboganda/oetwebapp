using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<RolePlayCard> RolePlayCards => Set<RolePlayCard>();
    public DbSet<InterlocutorScript> InterlocutorScripts => Set<InterlocutorScript>();

    partial void OnModelCreatingRolePlayCards(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RolePlayCard>(e =>
        {
            e.HasOne(x => x.ContentItem)
                .WithMany()
                .HasForeignKey(x => x.ContentItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InterlocutorScript>(e =>
        {
            e.HasOne(x => x.RolePlayCard)
                .WithOne()
                .HasForeignKey<InterlocutorScript>(x => x.RolePlayCardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
