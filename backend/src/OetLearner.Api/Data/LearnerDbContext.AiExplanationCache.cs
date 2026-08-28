using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W3 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// DbSet + Fluent configuration for the reusable, cross-learner explanation
/// cache (owner directive 2026-08-28 AI/Cloud API plan, point 8). See
/// <see cref="AiExplanationCacheEntry"/> and
/// <c>Services/Ai/AiExplanationCacheService.cs</c>.
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiExplanationCacheEntry> AiExplanationCacheEntries => Set<AiExplanationCacheEntry>();

    partial void OnModelCreatingAiExplanationCache(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiExplanationCacheEntry>(entity =>
        {
            entity.HasIndex(x => x.CacheKey)
                .IsUnique()
                .HasDatabaseName("UX_AiExplanationCacheEntries_CacheKey");

            // Admin/cleanup lookups by question, and to bound how many rows a
            // single question can ever accumulate (bounded by the number of
            // distinct wrong-answer/language combinations actually seen).
            entity.HasIndex(x => new { x.Module, x.QuestionId })
                .HasDatabaseName("IX_AiExplanationCacheEntries_Module_QuestionId");
        });
    }
}
