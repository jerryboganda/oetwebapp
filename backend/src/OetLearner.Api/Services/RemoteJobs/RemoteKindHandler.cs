using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.RemoteJobs;

public enum RemoteValidationStatus
{
    Ok,

    /// <summary>Structural failure (schema, hash, size, echoes): 422 <c>result_invalid</c>, a node strike, the job is requeued.</summary>
    Invalid,

    /// <summary>The result's <c>engineVersion</c> differs from the job: 422 <c>engine_version_mismatch</c> + strike.</summary>
    EngineMismatch,

    /// <summary>Declared outputs differ from what was uploaded: 422 <c>output_hash_mismatch</c> + strike.</summary>
    OutputMismatch,

    /// <summary>Content-class rejection (disallowed characters): the job fails non-retryably, NO strike, 200 <c>rejected</c>.</summary>
    ContentRejected,
}

/// <summary>Outcome of the pure, database-free validation of a result (OET-RWP/1 section 4.5.2 step 3).</summary>
public sealed record RemoteResultValidation(
    RemoteValidationStatus Status,
    string? Message = null,
    object? Parsed = null,
    string? SummaryJson = null)
{
    public bool IsOk => Status == RemoteValidationStatus.Ok;

    public static RemoteResultValidation Ok(object parsed, string summaryJson)
        => new(RemoteValidationStatus.Ok, null, parsed, summaryJson);

    public static RemoteResultValidation Invalid(string message)
        => new(RemoteValidationStatus.Invalid, message);

    public static RemoteResultValidation EngineMismatch(string message)
        => new(RemoteValidationStatus.EngineMismatch, message);

    public static RemoteResultValidation OutputMismatch(string message)
        => new(RemoteValidationStatus.OutputMismatch, message);

    public static RemoteResultValidation ContentRejected(string message)
        => new(RemoteValidationStatus.ContentRejected, message);
}

public enum RemoteApplyKind
{
    Applied,
    NoOp,
    Shadow,
    Deferred,
    Discarded,
}

/// <summary>What the applier did, in the vocabulary of OET-RWP/1 section 4.5.2 step 6.</summary>
public sealed record RemoteApplyOutcome(RemoteApplyKind Kind, string? Reason = null, object? Detail = null)
{
    public static RemoteApplyOutcome Applied(object? detail = null) => new(RemoteApplyKind.Applied, null, detail);

    public static RemoteApplyOutcome NoOp(string reason, object? detail = null) => new(RemoteApplyKind.NoOp, reason, detail);

    public static RemoteApplyOutcome Shadow(object? detail = null) => new(RemoteApplyKind.Shadow, null, detail);

    public static RemoteApplyOutcome Deferred(object? detail = null) => new(RemoteApplyKind.Deferred, null, detail);

    public static RemoteApplyOutcome Discarded(string reason, object? detail = null) => new(RemoteApplyKind.Discarded, reason, detail);

    /// <summary>The value stored in <c>RemoteJobs.ApplyOutcome</c> and returned as <c>outcome</c>.</summary>
    public string WireName => Kind.ToString();
}

/// <summary>An output row of the settled fence, as validated against the result's own manifest.</summary>
public sealed record RemoteOutputRow(string Name, long SizeBytes, string Sha256, string StorageKey);

/// <summary>Everything an applier needs, running INSIDE the completion transaction on the shared <c>DbContext</c>.</summary>
public sealed record RemoteApplyContext(
    LearnerDbContext Db,
    RemoteJobRow Job,
    object Parsed,
    string ResultJson,
    string NodeId,
    long Fence,
    IReadOnlyList<RemoteOutputRow> Outputs,
    RemoteJobsOptions Options,
    TimeProvider Time);

/// <summary>
/// Per-kind logic of the remote-job boundary: pure validation of a result, and an applier that commits it. A new
/// job kind adds one handler and one producer; the lease, fence, replay and audit machinery is shared.
/// </summary>
public interface IRemoteKindHandler
{
    string Kind { get; }

    /// <summary>
    /// Pure validation (no database): echoes of schema, engine version and input fingerprint, size and character
    /// rules, and recomputation of every hash the API can verify without trusting the node.
    /// </summary>
    RemoteResultValidation Validate(
        RemoteJobRow job,
        string resultJson,
        IReadOnlyList<RemoteOutputRow> outputs,
        RemoteJobsOptions options);

    /// <summary>
    /// Commits the validated result. Runs in the completion transaction: it must not call an AI provider, push to a
    /// hub, or do anything slow. Concurrency conflicts on domain rows are retried inside; a fourth conflict throws
    /// <see cref="RemoteApplyConflictException"/> (answered <c>503 apply_conflict</c>, retryable).
    /// </summary>
    Task<RemoteApplyOutcome> ApplyAsync(RemoteApplyContext context, CancellationToken ct);
}

/// <summary>Thrown by an applier after exhausting its reload-and-merge retries.</summary>
public sealed class RemoteApplyConflictException(string message) : Exception(message);
