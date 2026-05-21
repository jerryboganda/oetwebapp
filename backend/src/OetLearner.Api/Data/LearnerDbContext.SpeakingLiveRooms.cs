using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<SpeakingLiveRoom> SpeakingLiveRooms => Set<SpeakingLiveRoom>();
    public DbSet<SpeakingLiveRoomToken> SpeakingLiveRoomTokens => Set<SpeakingLiveRoomToken>();

    partial void OnModelCreatingSpeakingLiveRooms(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeakingLiveRoom>(e =>
        {
            e.HasOne(x => x.SpeakingSession)
                .WithMany()
                .HasForeignKey(x => x.SpeakingSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeakingLiveRoomToken>(e =>
        {
            e.HasOne(x => x.LiveRoom)
                .WithMany()
                .HasForeignKey(x => x.LiveRoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
