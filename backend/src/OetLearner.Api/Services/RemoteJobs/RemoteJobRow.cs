using Npgsql;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>
/// A full <c>RemoteJobs</c> row read through raw SQL (the claim and completion statements return rows
/// directly; mapping is by column name so the SELECT/RETURNING list may be reordered safely).
/// </summary>
public sealed class RemoteJobRow
{
    public string Id { get; init; } = default!;
    public string Kind { get; init; } = default!;
    public int SchemaVersion { get; init; }
    public string Purpose { get; init; } = default!;
    public string ResourceType { get; init; } = default!;
    public string ResourceId { get; init; } = default!;
    public string IdempotencyKey { get; init; } = default!;
    public string InputSha256 { get; init; } = default!;
    public string EngineVersion { get; init; } = default!;
    public string SettingsHash { get; init; } = default!;
    public string ParamsJson { get; init; } = "{}";
    public string InputsJson { get; init; } = "[]";
    public string LimitsJson { get; init; } = "{}";
    public int Weight { get; init; }
    public int Priority { get; init; }
    public string? TargetNodeId { get; init; }
    public string State { get; init; } = default!;
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; }
    public int ReleaseCount { get; init; }
    public long FenceToken { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public DateTimeOffset? DeadlineAt { get; init; }
    public DateTimeOffset? LeasedAt { get; init; }
    public string? ClaimNonce { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public DateTimeOffset NextAttemptAt { get; init; }
    public DateTimeOffset? FallbackAfter { get; init; }
    public string EnqueuedBy { get; init; } = default!;
    public string? ResultSha256 { get; init; }
    public string? ResultSummaryJson { get; init; }
    public string? ResultJson { get; init; }
    public string? ApplyOutcome { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? SettledFence { get; init; }
    public string? SettledBy { get; init; }
    public string? SettledCode { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public string? LastFailedNodeId { get; init; }
    public string? MetricsJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    private static readonly string[] ColumnNames =
    [
        "Id", "Kind", "SchemaVersion", "Purpose", "ResourceType", "ResourceId", "IdempotencyKey", "InputSha256",
        "EngineVersion", "SettingsHash", "ParamsJson", "InputsJson", "LimitsJson", "Weight", "Priority",
        "TargetNodeId", "State", "Attempt", "MaxAttempts", "ReleaseCount", "FenceToken", "LeaseOwner",
        "LeaseExpiresAt", "DeadlineAt", "LeasedAt", "ClaimNonce", "LastHeartbeatAt", "NextAttemptAt",
        "FallbackAfter", "EnqueuedBy", "ResultSha256", "ResultSummaryJson", "ResultJson", "ApplyOutcome",
        "CompletedAt", "SettledFence", "SettledBy", "SettledCode", "FailureCode", "FailureMessage",
        "LastFailedNodeId", "MetricsJson", "CreatedAt", "UpdatedAt",
    ];

    /// <summary>
    /// The quoted column list for a SELECT or RETURNING clause, qualified by <paramref name="alias"/>
    /// (for example <c>j."Id", j."Kind", ...</c>).
    /// </summary>
    public static string Columns(string alias)
        => string.Join(", ", ColumnNames.Select(name => $"{alias}.\"{name}\""));

    public static RemoteJobRow Read(NpgsqlDataReader r) => new()
    {
        Id = RemoteDb.Str(r, "Id"),
        Kind = RemoteDb.Str(r, "Kind"),
        SchemaVersion = RemoteDb.Int(r, "SchemaVersion"),
        Purpose = RemoteDb.Str(r, "Purpose"),
        ResourceType = RemoteDb.Str(r, "ResourceType"),
        ResourceId = RemoteDb.Str(r, "ResourceId"),
        IdempotencyKey = RemoteDb.Str(r, "IdempotencyKey"),
        InputSha256 = RemoteDb.Str(r, "InputSha256"),
        EngineVersion = RemoteDb.Str(r, "EngineVersion"),
        SettingsHash = RemoteDb.Str(r, "SettingsHash"),
        ParamsJson = RemoteDb.Str(r, "ParamsJson"),
        InputsJson = RemoteDb.Str(r, "InputsJson"),
        LimitsJson = RemoteDb.Str(r, "LimitsJson"),
        Weight = RemoteDb.Small(r, "Weight"),
        Priority = RemoteDb.Small(r, "Priority"),
        TargetNodeId = RemoteDb.StrN(r, "TargetNodeId"),
        State = RemoteDb.Str(r, "State"),
        Attempt = RemoteDb.Int(r, "Attempt"),
        MaxAttempts = RemoteDb.Int(r, "MaxAttempts"),
        ReleaseCount = RemoteDb.Int(r, "ReleaseCount"),
        FenceToken = RemoteDb.Long(r, "FenceToken"),
        LeaseOwner = RemoteDb.StrN(r, "LeaseOwner"),
        LeaseExpiresAt = RemoteDb.TsN(r, "LeaseExpiresAt"),
        DeadlineAt = RemoteDb.TsN(r, "DeadlineAt"),
        LeasedAt = RemoteDb.TsN(r, "LeasedAt"),
        ClaimNonce = RemoteDb.StrN(r, "ClaimNonce"),
        LastHeartbeatAt = RemoteDb.TsN(r, "LastHeartbeatAt"),
        NextAttemptAt = RemoteDb.Ts(r, "NextAttemptAt"),
        FallbackAfter = RemoteDb.TsN(r, "FallbackAfter"),
        EnqueuedBy = RemoteDb.Str(r, "EnqueuedBy"),
        ResultSha256 = RemoteDb.StrN(r, "ResultSha256"),
        ResultSummaryJson = RemoteDb.StrN(r, "ResultSummaryJson"),
        ResultJson = RemoteDb.StrN(r, "ResultJson"),
        ApplyOutcome = RemoteDb.StrN(r, "ApplyOutcome"),
        CompletedAt = RemoteDb.TsN(r, "CompletedAt"),
        SettledFence = RemoteDb.LongN(r, "SettledFence"),
        SettledBy = RemoteDb.StrN(r, "SettledBy"),
        SettledCode = RemoteDb.StrN(r, "SettledCode"),
        FailureCode = RemoteDb.StrN(r, "FailureCode"),
        FailureMessage = RemoteDb.StrN(r, "FailureMessage"),
        LastFailedNodeId = RemoteDb.StrN(r, "LastFailedNodeId"),
        MetricsJson = RemoteDb.StrN(r, "MetricsJson"),
        CreatedAt = RemoteDb.Ts(r, "CreatedAt"),
        UpdatedAt = RemoteDb.Ts(r, "UpdatedAt"),
    };
}
