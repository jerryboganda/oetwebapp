using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>W11 — encrypted raw provider payloads with retention sweep.</summary>
public partial class LearnerDbContext
{
    public DbSet<AiRawResponse> AiRawResponses => Set<AiRawResponse>();

    partial void OnModelCreatingAiRawResponses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiRawResponse>(entity =>
        {
            entity.HasIndex(x => x.ExpiresAt)
                .HasDatabaseName("IX_AiRawResponses_ExpiresAt");
            entity.HasIndex(x => x.OperationId)
                .HasDatabaseName("IX_AiRawResponses_OperationId");
        });
    }
}
