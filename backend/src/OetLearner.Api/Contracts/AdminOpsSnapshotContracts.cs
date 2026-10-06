namespace OetLearner.Api.Contracts;

// GET /v1/admin/ops/snapshot: one cheap, read-only view of the load on the primary VPS, for the owner and the
// future fleet manager. Counts only: no learner data, no job payloads, no connection strings.

/// <summary>Queue depth of one background job type. <c>Queued</c> counts jobs that are due now (waiting for a
/// worker); a job scheduled for later is counted in <see cref="AdminOpsJobsSnapshot.Scheduled"/> only.
/// <c>StuckProcessing</c> is processing for more than 10 minutes (the same threshold as <c>/health/ready</c>).</summary>
public sealed record AdminOpsJobTypeRow(
    string Type,
    int Queued,
    int Processing,
    int StuckProcessing,
    int? OldestQueuedAgeSeconds);

public sealed record AdminOpsJobsSnapshot(
    int TotalQueued,
    int TotalProcessing,
    int Scheduled,
    int? OldestQueuedAgeSeconds,
    IReadOnlyList<AdminOpsJobTypeRow> ByType);

/// <summary>Database connections of one <c>application_name</c> and <c>state</c> (from <c>pg_stat_activity</c>).
/// <c>ApplicationName</c> is the process's Npgsql <c>Application Name</c> (<c>oet-api-blue</c>, <c>oet-api-green</c>,
/// <c>oet-ai-worker</c> in <c>docker-compose.production.yml</c>); <c>(unset)</c> for any connection that sets none
/// (the database's own workers, an admin shell).</summary>
public sealed record AdminOpsConnectionRow(string ApplicationName, string? State, int Count);

/// <summary><c>Available</c> is false off Postgres or when the catalog cannot be read; <c>Note</c> says why.</summary>
public sealed record AdminOpsConnectionsSnapshot(
    bool Available,
    int? Total,
    int? MaxConnections,
    IReadOnlyList<AdminOpsConnectionRow> ByApplicationName,
    string? Note);

public sealed record AdminOpsSpeakingSnapshot(SpeakingLiveAdmissionCounts? Admission);

/// <summary>Placeholder until the remote worker fleet ships: a stable shape so a dashboard can bind to it now.</summary>
public sealed record AdminOpsRemoteWorkersSnapshot(bool Deployed, int Nodes, string Note);

public sealed record AdminOpsSnapshot(
    DateTimeOffset GeneratedAt,
    AdminOpsJobsSnapshot Jobs,
    AdminOpsConnectionsSnapshot Connections,
    AdminOpsSpeakingSnapshot Speaking,
    AdminOpsRemoteWorkersSnapshot RemoteWorkers);
