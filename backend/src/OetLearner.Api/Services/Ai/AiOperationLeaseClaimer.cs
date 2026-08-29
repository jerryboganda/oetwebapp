using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W4 — durable lease claim over <see cref="AiOperation"/> using the same
/// <c>FOR UPDATE SKIP LOCKED</c> shape as
/// <see cref="BackgroundJobProcessor.PostgresClaimQueuedJobsSql"/>.
///
/// <para>
/// Two workers cannot lease the same row: SKIP LOCKED plus a single UPDATE
/// to <see cref="AiOperationState.Leased"/> (or a lease refresh on
/// <see cref="AiOperationState.ProviderSucceeded"/>) is the lock.
/// Expired leases (<see cref="AiOperation.LeaseExpiresAt"/> in the past)
/// are reclaimable exactly as stuck background jobs are.
/// </para>
/// </summary>
public interface IAiOperationLeaseClaimer
{
    Task<IReadOnlyList<AiLeasedOperation>> ClaimAsync(
        LearnerDbContext db,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken ct);
}

public sealed record AiLeasedOperation(string Id, AiOperationState State, string? ResultRef);

public sealed class AiOperationLeaseClaimer : IAiOperationLeaseClaimer
{
    internal const int DefaultBatchSize = 10;

    internal static string PostgresClaimSql => """
        WITH candidate AS (
            SELECT "Id"
            FROM "AiOperations"
            WHERE (
                    "State" IN (@queued, @retryScheduled)
                    AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= @now)
                )
                OR (
                    "State" = @leased
                    AND "LeaseExpiresAt" IS NOT NULL
                    AND "LeaseExpiresAt" < @now
                )
                OR (
                    "State" = @providerSucceeded
                    AND (
                        "LeaseOwner" IS NULL
                        OR "LeaseExpiresAt" IS NULL
                        OR "LeaseExpiresAt" < @now
                    )
                )
            ORDER BY "CreatedAt"
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
        ),
        claimed AS (
            UPDATE "AiOperations" AS op
            SET "State" = CASE
                    WHEN op."State" = @providerSucceeded THEN op."State"
                    ELSE @leased
                END,
                "LeaseOwner" = @leaseOwner,
                "LeaseExpiresAt" = @leaseExpiresAt,
                "UpdatedAt" = @now
            FROM candidate
            WHERE op."Id" = candidate."Id"
            RETURNING op."Id", op."State", op."ResultRef"
        )
        SELECT "Id", "State", "ResultRef"
        FROM claimed;
        """;

    public async Task<IReadOnlyList<AiLeasedOperation>> ClaimAsync(
        LearnerDbContext db,
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
        {
            return Array.Empty<AiLeasedOperation>();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await ClaimPostgresAsync(db, leaseOwner, now, now + leaseDuration, batchSize, ct);
        await tx.CommitAsync(ct);
        return claimed;
    }

    private static async Task<IReadOnlyList<AiLeasedOperation>> ClaimPostgresAsync(
        LearnerDbContext db,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        int batchSize,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedConnection = connection.State != ConnectionState.Open;
        if (openedConnection)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = PostgresClaimSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "@queued", (int)AiOperationState.Queued);
            Add(command, "@retryScheduled", (int)AiOperationState.RetryScheduled);
            Add(command, "@leased", (int)AiOperationState.Leased);
            Add(command, "@providerSucceeded", (int)AiOperationState.ProviderSucceeded);
            Add(command, "@now", now);
            Add(command, "@leaseExpiresAt", leaseExpiresAt);
            Add(command, "@leaseOwner", leaseOwner);
            Add(command, "@batchSize", batchSize);

            var rows = new List<AiLeasedOperation>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new AiLeasedOperation(
                    reader.GetString(0),
                    (AiOperationState)reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            return rows;
        }
        finally
        {
            if (openedConnection && db.Database.CurrentTransaction is null)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
