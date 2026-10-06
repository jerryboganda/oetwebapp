using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

// Remote-worker boundary (OET-RWP/1). Hand-authored migration
// 20270110090000_AddRemoteWorkersAndJobs creates the tables, constraints and partial indexes
// (ADR 0001). The matching entries in LearnerDbContextModelSnapshot are hand-written and must stay
// identical to the configuration below: every property, MaxLength, jsonb mapping and index name, or
// `dotnet ef migrations has-pending-model-changes` (speaking-ci.yml / migrations-check) fails. Change
// this file and the snapshot together. The entities live in the model for every provider so
// SQLite/InMemory hosts build; the claim/CAS/reaper SQL is Postgres-only and is registered only when
// the configured provider is Npgsql.
public partial class LearnerDbContext
{
    public DbSet<RemoteWorker> RemoteWorkers => Set<RemoteWorker>();
    public DbSet<RemoteCredential> RemoteCredentials => Set<RemoteCredential>();
    public DbSet<RemoteJob> RemoteJobs => Set<RemoteJob>();
    public DbSet<RemoteJobOutput> RemoteJobOutputs => Set<RemoteJobOutput>();

    partial void OnModelCreatingRemoteJobs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RemoteWorker>(entity =>
        {
            entity.HasIndex(x => x.NodeRef).IsUnique().HasDatabaseName("UX_RemoteWorkers_NodeRef");
            entity.HasIndex(x => x.Status).HasDatabaseName("IX_RemoteWorkers_Status");
            entity.Property(x => x.KindLimitsJson).HasColumnType("jsonb");
            entity.Property(x => x.PressureJson).HasColumnType("jsonb");
            entity.Property(x => x.PollJson).HasColumnType("jsonb");
            entity.Property(x => x.DesiredAgentJson).HasColumnType("jsonb");
            entity.Property(x => x.KindsJson).HasColumnType("jsonb");
            entity.Property(x => x.LastCapacityJson).HasColumnType("jsonb");
            entity.Property(x => x.LastLoadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<RemoteCredential>(entity =>
        {
            entity.HasIndex(x => x.NodeId).HasDatabaseName("IX_RemoteCredentials_Node");
        });

        modelBuilder.Entity<RemoteJob>(entity =>
        {
            entity.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RemoteJobs_IdempotencyKey");
            entity.HasIndex(x => new { x.ResourceType, x.ResourceId }).HasDatabaseName("IX_RemoteJobs_Resource");
            entity.HasIndex(x => new { x.State, x.UpdatedAt }).HasDatabaseName("IX_RemoteJobs_State_Updated");
            entity.Property(x => x.ParamsJson).HasColumnType("jsonb");
            entity.Property(x => x.InputsJson).HasColumnType("jsonb");
            entity.Property(x => x.LimitsJson).HasColumnType("jsonb");
            entity.Property(x => x.ResultSummaryJson).HasColumnType("jsonb");
            entity.Property(x => x.MetricsJson).HasColumnType("jsonb");
            entity.Property(x => x.ResultJson).HasColumnType("text");
        });

        modelBuilder.Entity<RemoteJobOutput>(entity =>
        {
            entity.HasKey(x => new { x.JobId, x.Fence, x.Name });
        });
    }
}
