using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

/// <summary>
/// W1 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// schema-only control-plane tables: <see cref="AiOperation"/> (the durable
/// state machine), <see cref="AiOperationAttempt"/> (the exactly-one-attempt
/// audit trail), <see cref="AiBudgetPeriod"/> (platform spend reservation),
/// and <see cref="AiCreditReservation"/> (learner credit reservation).
///
/// <para>
/// No runtime service reads or writes these tables yet — see
/// <c>Domain/AiOperationEntities.cs</c> for the full rationale. This partial
/// only wires the DbSets and Fluent configuration so a later wave can start
/// using them without another migration.
/// </para>
/// </summary>
public partial class LearnerDbContext
{
    public DbSet<AiOperation> AiOperations => Set<AiOperation>();
    public DbSet<AiOperationAttempt> AiOperationAttempts => Set<AiOperationAttempt>();
    public DbSet<AiBudgetPeriod> AiBudgetPeriods => Set<AiBudgetPeriod>();
    public DbSet<AiCreditReservation> AiCreditReservations => Set<AiCreditReservation>();

    partial void OnModelCreatingAiControlPlane(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiOperation>(entity =>
        {
            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("UX_AiOperations_IdempotencyKey");

            entity.HasIndex(x => new { x.State, x.NextAttemptAt })
                .HasDatabaseName("IX_AiOperations_State_NextAttemptAt");

            entity.HasIndex(x => x.LeaseExpiresAt)
                .HasDatabaseName("IX_AiOperations_LeaseExpiresAt");
        });

        modelBuilder.Entity<AiOperationAttempt>(entity =>
        {
            // Composite PK is the authoritative "exactly one attempt per
            // (operation, attempt number)" structural guarantee — see the
            // class doc comment. This table stays a plain heap table (never
            // partitioned), so the PK alone enforces it everywhere.
            entity.HasKey(x => new { x.OperationId, x.AttemptNumber });

            // Owned by the operation: deleting an operation (rare — never
            // done by any current runtime path) removes its attempt log.
            entity.HasOne(x => x.Operation)
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiBudgetPeriod>(entity =>
        {
            entity.HasIndex(x => new { x.Scope, x.PeriodKey })
                .IsUnique()
                .HasDatabaseName("UX_AiBudgetPeriods_Scope_PeriodKey");
        });

        modelBuilder.Entity<AiCreditReservation>(entity =>
        {
            entity.HasIndex(x => x.BusinessReference)
                .IsUnique()
                .HasDatabaseName("UX_AiCreditReservations_BusinessReference");

            // Restrict, not Cascade: a credit reservation is a financial
            // record (mirrors the append-only AiCreditLedgerEntry pattern)
            // and must never silently disappear because its operation row
            // was removed.
            entity.HasOne(x => x.Operation)
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Deliberately no HasIndex(...) on AiUsageRecord here: the
        // partition-safe uniqueness guarantee (UX_AiUsageRecords_Operation_Attempt)
        // is created conditionally by migration
        // 20261103090000_ExtendAiUsageRecordProvenance, only when AiUsageRecords
        // is not a partitioned parent (relkind <> 'p'). No migration creates a
        // plain (non-unique) index on these columns, so it is kept out of the
        // EF model too — a later wave can add a query index (with a
        // migration) when one is actually needed. Keeping that constraint
        // out of the EF model avoids a permanent mismatch between the
        // (never-regenerated) model snapshot and a table shape that can
        // legitimately differ per environment.

        // Optimistic concurrency via Postgres xmin — matches the existing
        // ConfigureXminToken convention used for Subscription/Invoice/etc.
        // Two workers racing to reserve against the same budget period must
        // not silently overwrite each other's reservation.
        if (Database.IsNpgsql())
        {
            ConfigureXminToken<AiBudgetPeriod>(modelBuilder);
        }
    }
}
