using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<WritingBuddyPair> WritingBuddyPairs => Set<WritingBuddyPair>();
    public DbSet<WritingBuddyMessage> WritingBuddyMessages => Set<WritingBuddyMessage>();
    public DbSet<WritingBuddyCheckIn> WritingBuddyCheckIns => Set<WritingBuddyCheckIn>();

    partial void OnModelCreatingWritingBuddy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WritingBuddyPair>(e =>
        {
            e.HasKey(x => x.Id);
            // Lookup paths used by the service:
            //   GetActivePairAsync  → WHERE (UserA = u OR UserB = u) AND Status = 'active'
            //   RequestMatchAsync   → WHERE Status = 'active' AND Profession = p
            e.HasIndex(x => x.UserAId);
            e.HasIndex(x => x.UserBId);
            e.HasIndex(x => new { x.Profession, x.Status, x.MatchedAtBand });
            // A learner may have at most one active pair at any time.
            e.HasIndex(x => new { x.UserAId, x.UserBId })
                .IsUnique()
                .HasFilter("\"Status\" = 'active'");
        });

        modelBuilder.Entity<WritingBuddyMessage>(e =>
        {
            e.HasKey(x => x.Id);
            // Inbox view: most recent first per pair.
            e.HasIndex(x => new { x.PairId, x.SentAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_WritingBuddyMessage_Pair_SentAt_Desc");
            // Daily rate-limit lookup: WHERE FromUserId = u AND SentAt > now-24h
            e.HasIndex(x => new { x.FromUserId, x.SentAt });
        });

        modelBuilder.Entity<WritingBuddyCheckIn>(e =>
        {
            e.HasKey(x => x.Id);
            // Exactly one check-in row per pair per ISO week.
            e.HasIndex(x => new { x.PairId, x.WeekStartDate }).IsUnique();
            // PostgreSQL jsonb columns for the two half-reports.
            e.Property(x => x.UserAReportJson).HasColumnType("jsonb");
            e.Property(x => x.UserBReportJson).HasColumnType("jsonb");
        });
    }
}
