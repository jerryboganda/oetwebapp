using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<InterlocutorTrainingModule> InterlocutorTrainingModules => Set<InterlocutorTrainingModule>();
    public DbSet<InterlocutorTrainingProgress> InterlocutorTrainingProgress => Set<InterlocutorTrainingProgress>();

    partial void OnModelCreatingInterlocutorTraining(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InterlocutorTrainingProgress>(e =>
        {
            e.HasOne(x => x.Module)
                .WithMany()
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
