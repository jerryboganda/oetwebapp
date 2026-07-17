using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<LibraryVideo> LibraryVideos => Set<LibraryVideo>();
    public DbSet<VideoCategory> VideoCategories => Set<VideoCategory>();
    public DbSet<VideoCategoryItem> VideoCategoryItems => Set<VideoCategoryItem>();
    public DbSet<VideoCaptionTrack> VideoCaptionTracks => Set<VideoCaptionTrack>();
    public DbSet<VideoAttachment> VideoAttachments => Set<VideoAttachment>();
    public DbSet<LearnerVideoLibraryProgress> LearnerVideoLibraryProgress => Set<LearnerVideoLibraryProgress>();
    public DbSet<LearnerVideoBookmark> LearnerVideoBookmarks => Set<LearnerVideoBookmark>();
    public DbSet<VideoPlaybackSession> VideoPlaybackSessions => Set<VideoPlaybackSession>();
    public DbSet<VideoAttestationChallenge> VideoAttestationChallenges => Set<VideoAttestationChallenge>();
    public DbSet<VideoPlaybackEvent> VideoPlaybackEvents => Set<VideoPlaybackEvent>();

    partial void OnModelCreatingVideoLibrary(ModelBuilder modelBuilder)
    {
        // LibraryVideo → MediaAsset custom thumbnail: Restrict (SHA-deduped
        // shared asset; never cascade-delete media rows).
        modelBuilder.Entity<LibraryVideo>()
            .HasOne(x => x.CustomThumbnailMediaAsset)
            .WithMany()
            .HasForeignKey(x => x.CustomThumbnailMediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LibraryVideo>()
            .Property(x => x.AccessTier)
            .HasDefaultValue("premium");

        // VideoCategoryItem is owned by both sides — deleting either the
        // category or the video removes the membership row.
        modelBuilder.Entity<VideoCategoryItem>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VideoCategoryItem>()
            .HasOne(x => x.Video)
            .WithMany()
            .HasForeignKey(x => x.VideoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VideoCaptionTrack>()
            .HasOne(x => x.Video)
            .WithMany()
            .HasForeignKey(x => x.VideoId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VideoCaptionTrack>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VideoAttachment>()
            .HasOne(x => x.Video)
            .WithMany()
            .HasForeignKey(x => x.VideoId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VideoAttachment>()
            .HasOne(x => x.MediaAsset)
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hot JSON columns as jsonb on Postgres only (SQLite/InMemory test
        // providers keep TEXT). Mirrors the hot-JSON convention in
        // LearnerDbContext.OnModelCreating.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<LibraryVideo>().Property(x => x.ProfessionIdsJson).HasColumnType("jsonb");
            modelBuilder.Entity<LibraryVideo>().Property(x => x.ChaptersJson).HasColumnType("jsonb");
            modelBuilder.Entity<VideoPlaybackEvent>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        }

        modelBuilder.Entity<LibraryVideo>().Property(x => x.ProfessionIdsJson).HasDefaultValue("[]");
        modelBuilder.Entity<LibraryVideo>().Property(x => x.ChaptersJson).HasDefaultValue("[]");
        modelBuilder.Entity<VideoPlaybackEvent>().Property(x => x.PayloadJson).HasDefaultValue("{}");
    }
}
