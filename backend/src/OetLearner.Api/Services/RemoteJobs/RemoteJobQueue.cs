using System.Text;
using NpgsqlTypes;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Identity of a unit of remote work (OET-RWP/1 section 6.0, rules 2 and 3).</summary>
public static class RemoteJobKeys
{
    public const int MaxIdempotencyKeyLength = 256;

    /// <summary>
    /// <c>{kind}|{purpose}|{resourceType}:{resourceId}|{inputSha256}|{engineVersion}|{settingsHash}</c>, at most 256
    /// characters. A longer composition is replaced, deterministically, by its SHA-256 so the unique index still
    /// de-duplicates the same unit of work.
    /// </summary>
    public static string IdempotencyKey(
        string kind,
        string purpose,
        string resourceType,
        string resourceId,
        string inputSha256,
        string engineVersion,
        string settingsHash)
    {
        var composed = $"{kind}|{purpose}|{resourceType}:{resourceId}|{inputSha256}|{engineVersion}|{settingsHash}";
        if (composed.Length <= MaxIdempotencyKeyLength) return composed;
        return $"{kind}|{purpose}|{resourceType}:{resourceId}|h:{RemoteIds.Sha256Hex(composed)}";
    }

    /// <summary>SHA-256 (lowercase hex) of <c>k1=v1;k2=v2;...</c> with the keys sorted ascending (ordinal).</summary>
    public static string SettingsHash(IEnumerable<KeyValuePair<string, string>> settings)
    {
        var text = string.Join(";", settings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        return RemoteIds.Sha256Hex(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>A producer's request to enqueue one unit of work.</summary>
public sealed record RemoteEnqueueRequest(
    string Kind,
    int SchemaVersion,
    string Purpose,
    string ResourceType,
    string ResourceId,
    string InputSha256,
    string EngineVersion,
    string SettingsHash,
    string ParamsJson,
    string InputsJson,
    RemoteJobLimits Limits,
    string EnqueuedBy,
    int Priority = 0,
    string? TargetNodeId = null,
    bool WithFallback = true);

public sealed record RemoteEnqueueResult(string JobId, string State, bool Created);

/// <summary>Producer-facing queue operations: idempotent enqueue and lookups.</summary>
public interface IRemoteJobQueue
{
    /// <summary>
    /// Upserts on the idempotency key. An existing <c>Queued</c>/<c>Leased</c> row is returned unchanged; a terminal
    /// row is reset to <c>Queued</c> (fence preserved) only when <paramref name="force"/> is set, else it stands.
    /// </summary>
    Task<RemoteEnqueueResult> EnqueueAsync(RemoteEnqueueRequest request, bool force, CancellationToken ct);

    Task<RemoteJobRow?> FindByKeyAsync(string idempotencyKey, CancellationToken ct);

    Task<RemoteJobRow?> GetAsync(string jobId, CancellationToken ct);
}

public sealed class RemoteJobQueue(
    LearnerDbContext db,
    RemoteJobsSettings settings,
    TimeProvider timeProvider) : IRemoteJobQueue
{
    private const string EnqueueSql = """
        INSERT INTO "RemoteJobs" (
            "Id", "Kind", "SchemaVersion", "Purpose", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256",
            "EngineVersion", "SettingsHash", "ParamsJson", "InputsJson", "LimitsJson", "Weight", "Priority",
            "TargetNodeId", "State", "Attempt", "MaxAttempts", "ReleaseCount", "FenceToken", "NextAttemptAt",
            "FallbackAfter", "EnqueuedBy", "CreatedAt", "UpdatedAt")
        VALUES (
            @id, @kind, @schema, @purpose, @resourceType, @resourceId, @key, @inputSha,
            @engine, @settingsHash, @params, @inputs, @limits, @weight, @priority,
            @target, 'Queued', 0, @maxAttempts, 0, 0, clock_timestamp(),
            CASE WHEN @fallbackMinutes > 0 THEN clock_timestamp() + make_interval(mins => @fallbackMinutes) ELSE NULL END,
            @enqueuedBy, clock_timestamp(), clock_timestamp())
        ON CONFLICT ("IdempotencyKey") DO UPDATE SET
            "State" = 'Queued', "Attempt" = 0, "ReleaseCount" = 0,
            "NextAttemptAt" = clock_timestamp(), "FallbackAfter" = EXCLUDED."FallbackAfter",
            "ParamsJson" = EXCLUDED."ParamsJson", "InputsJson" = EXCLUDED."InputsJson", "LimitsJson" = EXCLUDED."LimitsJson",
            "ResultSha256" = NULL, "ResultSummaryJson" = NULL, "ResultJson" = NULL, "ApplyOutcome" = NULL, "CompletedAt" = NULL,
            "SettledFence" = NULL, "SettledBy" = NULL, "SettledCode" = NULL,
            "FailureCode" = NULL, "FailureMessage" = NULL, "LastFailedNodeId" = NULL,
            "LeaseOwner" = NULL, "LeaseExpiresAt" = NULL, "DeadlineAt" = NULL, "LeasedAt" = NULL, "ClaimNonce" = NULL,
            "LastHeartbeatAt" = NULL, "MetricsJson" = NULL, "UpdatedAt" = clock_timestamp()
        WHERE @force AND "RemoteJobs"."State" IN ('Succeeded', 'Failed', 'Quarantined', 'FallbackLocal', 'Cancelled')
        RETURNING "Id", "State", ("xmax" = 0) AS "Inserted";
        """;

    public async Task<RemoteEnqueueResult> EnqueueAsync(RemoteEnqueueRequest request, bool force, CancellationToken ct)
    {
        var options = settings.Current;
        var key = RemoteJobKeys.IdempotencyKey(
            request.Kind, request.Purpose, request.ResourceType, request.ResourceId,
            request.InputSha256, request.EngineVersion, request.SettingsHash);
        var id = RemoteIds.NewJobId(timeProvider.GetUtcNow());
        var fallbackMinutes = request.WithFallback ? options.FallbackAfterMinutes : 0;

        var inserted = await RemoteDb.QueryFirstAsync(
            db,
            EnqueueSql,
            parameters =>
            {
                parameters.AddWithValue("id", id);
                parameters.AddWithValue("kind", request.Kind);
                parameters.AddWithValue("schema", request.SchemaVersion);
                parameters.AddWithValue("purpose", request.Purpose);
                parameters.AddWithValue("resourceType", request.ResourceType);
                parameters.AddWithValue("resourceId", request.ResourceId);
                parameters.AddWithValue("key", key);
                parameters.AddWithValue("inputSha", request.InputSha256);
                parameters.AddWithValue("engine", request.EngineVersion);
                parameters.AddWithValue("settingsHash", request.SettingsHash);
                parameters.Add(new NpgsqlParameter("params", NpgsqlDbType.Jsonb) { Value = request.ParamsJson });
                parameters.Add(new NpgsqlParameter("inputs", NpgsqlDbType.Jsonb) { Value = request.InputsJson });
                parameters.Add(new NpgsqlParameter("limits", NpgsqlDbType.Jsonb) { Value = request.Limits.ToJson() });
                parameters.AddWithValue("weight", (short)request.Limits.Weight);
                parameters.AddWithValue("priority", (short)request.Priority);
                parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Varchar) { Value = (object?)request.TargetNodeId ?? DBNull.Value });
                parameters.AddWithValue("maxAttempts", options.MaxAttempts);
                parameters.AddWithValue("fallbackMinutes", fallbackMinutes);
                parameters.AddWithValue("enqueuedBy", request.EnqueuedBy);
                parameters.AddWithValue("force", force);
            },
            reader => new RemoteEnqueueResult(
                RemoteDb.Str(reader, "Id"),
                RemoteDb.Str(reader, "State"),
                RemoteDb.Bool(reader, "Inserted")),
            ct);

        if (inserted is not null) return inserted;

        // Not inserted and not reset: the existing row stands (Queued/Leased, or a terminal row the producer
        // chose not to reset). Return it so the caller can decide.
        var existing = await FindByKeyAsync(key, ct)
            ?? throw new InvalidOperationException("Remote job vanished between enqueue and lookup.");
        return new RemoteEnqueueResult(existing.Id, existing.State, false);
    }

    private static readonly string SelectByKeySql =
        "SELECT " + RemoteJobRow.Columns("j") + " FROM \"RemoteJobs\" j WHERE j.\"IdempotencyKey\" = @key;";

    private static readonly string SelectByIdSql =
        "SELECT " + RemoteJobRow.Columns("j") + " FROM \"RemoteJobs\" j WHERE j.\"Id\" = @id;";

    public Task<RemoteJobRow?> FindByKeyAsync(string idempotencyKey, CancellationToken ct)
        => RemoteDb.QueryFirstAsync(
            db,
            SelectByKeySql,
            parameters => parameters.AddWithValue("key", idempotencyKey),
            RemoteJobRow.Read,
            ct);

    public Task<RemoteJobRow?> GetAsync(string jobId, CancellationToken ct)
        => RemoteDb.QueryFirstAsync(
            db,
            SelectByIdSql,
            parameters => parameters.AddWithValue("id", jobId),
            RemoteJobRow.Read,
            ct);
}
