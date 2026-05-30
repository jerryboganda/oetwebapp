using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingModerationCase> SpeakingModerationCases => Set<SpeakingModerationCase>();

    partial void OnModelCreatingSpeakingModeration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingModerationCase>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SpeakingSessionId).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
