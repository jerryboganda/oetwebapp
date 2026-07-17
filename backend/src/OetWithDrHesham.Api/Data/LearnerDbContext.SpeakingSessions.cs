using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingSession> SpeakingSessions => Set<SpeakingSession>();
    public DbSet<SpeakingRecording> SpeakingRecordings => Set<SpeakingRecording>();
    public DbSet<SpeakingTranscript> SpeakingTranscripts => Set<SpeakingTranscript>();
    public DbSet<SpeakingResultVisibilityConfig> SpeakingResultVisibilityConfigs => Set<SpeakingResultVisibilityConfig>();

    partial void OnModelCreatingSpeakingSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingSession>(e =>
        {
            e.HasOne(x => x.RolePlayCard)
                .WithMany()
                .HasForeignKey(x => x.RolePlayCardId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingRecording>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.MediaAsset)
                .WithMany()
                .HasForeignKey(x => x.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpeakingTranscript>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakingResultVisibilityConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RolePlayCardId);
        });
    }
}
