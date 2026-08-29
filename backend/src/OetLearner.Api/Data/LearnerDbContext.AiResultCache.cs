using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W5 — DbSets for <see cref="AiResultCacheEntry"/> and
/// <see cref="ListeningQnaTurn"/>.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiResultCacheEntry> AiResultCaches => Set<AiResultCacheEntry>();
    public DbSet<ListeningQnaTurn> ListeningQnaTurns => Set<ListeningQnaTurn>();
    public DbSet<ReadingQnaTurn> ReadingQnaTurns => Set<ReadingQnaTurn>();

    partial void OnModelCreatingAiResultCache(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiResultCacheEntry>(entity =>
        {
            entity.HasIndex(x => x.CacheKey)
                .IsUnique()
                .HasDatabaseName("UX_AiResultCaches_CacheKey");

            entity.HasIndex(x => x.ExpiresAt)
                .HasDatabaseName("IX_AiResultCaches_ExpiresAt");

            entity.HasIndex(x => new { x.FeatureCode, x.Module })
                .HasDatabaseName("IX_AiResultCaches_Feature_Module");
        });

        modelBuilder.Entity<ListeningQnaTurn>(entity =>
        {
            entity.HasIndex(x => new { x.SessionId, x.ClientTurnId })
                .IsUnique()
                .HasDatabaseName("UX_ListeningQnaTurns_Session_ClientTurn");

            entity.HasIndex(x => new { x.UserId, x.AttemptId, x.QuestionId })
                .HasDatabaseName("IX_ListeningQnaTurns_User_Attempt_Question");
        });

        modelBuilder.Entity<ReadingQnaTurn>(entity =>
        {
            entity.HasIndex(x => new { x.SessionId, x.ClientTurnId })
                .IsUnique()
                .HasDatabaseName("UX_ReadingQnaTurns_Session_ClientTurn");

            entity.HasIndex(x => new { x.UserId, x.AttemptId, x.PassageId })
                .HasDatabaseName("IX_ReadingQnaTurns_User_Attempt_Passage");
        });
    }
}
