using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingAiAssessment> SpeakingAiAssessments => Set<SpeakingAiAssessment>();
    public DbSet<SpeakingTutorAssessment> SpeakingTutorAssessments => Set<SpeakingTutorAssessment>();
    public DbSet<SpeakingTimestampedComment> SpeakingTimestampedComments => Set<SpeakingTimestampedComment>();

    partial void OnModelCreatingSpeakingAssessments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingAiAssessment>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakingTutorAssessment>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakingTimestampedComment>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
