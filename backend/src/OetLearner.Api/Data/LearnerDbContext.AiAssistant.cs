using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain.AiAssistant;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    public DbSet<AiChatThread> AiChatThreads => Set<AiChatThread>();
    public DbSet<AiChatMessage> AiChatMessages => Set<AiChatMessage>();
    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<AiRolePermissionMatrix> AiRolePermissionMatrix => Set<AiRolePermissionMatrix>();
    public DbSet<AiCodebaseChunk> AiCodebaseChunks => Set<AiCodebaseChunk>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<AiAuditEvent> AiAuditEvents => Set<AiAuditEvent>();
    // Renamed from AiToolInvocations to avoid clashing with the canonical
    // AI-gateway DbSet of the same name (Domain.AiToolInvocation).
    public DbSet<AiToolInvocation> AiAssistantToolInvocations => Set<AiToolInvocation>();

    /// <summary>
    /// Fluent configuration for the AI Assistant tables. Called from the
    /// main OnModelCreating via partial-method dispatch.
    /// </summary>
    partial void OnModelCreatingAiAssistant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiChatThread>(e =>
        {
            e.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt });
            e.HasIndex(x => x.IsArchived);
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Thread!)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiChatMessage>(e =>
        {
            e.HasIndex(x => new { x.ThreadId, x.CreatedAt });
            e.Property(x => x.Content).HasColumnType("text");
            e.Property(x => x.ToolPayloadJson).HasColumnType("text");
        });

        modelBuilder.Entity<AiProviderConfig>(e =>
        {
            e.HasIndex(x => x.IsDefault);
            e.HasIndex(x => x.Kind);
        });

        modelBuilder.Entity<AiRolePermissionMatrix>(e =>
        {
            e.HasIndex(x => x.RoleKey).IsUnique();
        });

        modelBuilder.Entity<AiCodebaseChunk>(e =>
        {
            e.HasIndex(x => x.RepoRelativePath);
            e.HasIndex(x => x.FileContentHash);
            e.Property(x => x.Content).HasColumnType("text");
            // Vector(1536) column added via a separate Phase 2 raw-SQL migration
            // (requires pgvector extension to be installed by DBA first).
        });

        modelBuilder.Entity<AiUsageLog>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.OccurredAt });
            e.HasIndex(x => new { x.ThreadId, x.OccurredAt });
            e.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
        });

        modelBuilder.Entity<AiAuditEvent>(e =>
        {
            e.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
            e.HasIndex(x => new { x.Action, x.OccurredAt });
            e.Property(x => x.MetadataJson).HasColumnType("text");
        });

        modelBuilder.Entity<AiToolInvocation>(e =>
        {
            // Explicit table name avoids colliding with the canonical AI-gateway
            // AiToolInvocations table (Domain.AiToolInvocation).
            e.ToTable("AiAssistantToolInvocations");
            e.HasIndex(x => new { x.ThreadId, x.CreatedAt });
            e.HasIndex(x => x.MessageId);
            e.Property(x => x.ArgsJson).HasColumnType("text");
            e.Property(x => x.ResultJson).HasColumnType("text");
        });
    }
}
