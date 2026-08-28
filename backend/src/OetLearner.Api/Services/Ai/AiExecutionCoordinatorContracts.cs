using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Everything <see cref="AiExecutionCoordinator"/> needs to authorize, key,
/// persist, and delegate one logical AI operation. The caller builds the
/// <see cref="GatewayRequest"/> exactly as it would for a direct
/// <see cref="IAiGatewayService"/> call (grounded prompt, model, feature
/// code, …) — the coordinator stamps the resolved operation id onto a copy
/// before calling the gateway; it never mutates the caller's instance.
/// </summary>
public sealed class AiOperationRequest
{
    /// <summary>Coarse module grouping. When null, resolved from the feature
    /// policy (see <see cref="IAiFeaturePolicyRegistry"/>) or, failing that,
    /// the feature code's first dot-segment.</summary>
    public string? Module { get; init; }

    /// <summary>The domain row this operation is scoring/processing (e.g. a
    /// submission or answer id). Opaque to the coordinator.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Discriminator for <see cref="ResourceId"/>.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Version of the resource this operation targets.</summary>
    public int? ResourceVersion { get; init; }

    /// <summary>SHA-256 (hex) of the normalized request payload — caller's
    /// responsibility to compute so the coordinator stays decoupled from any
    /// particular request DTO shape. Required for conflict detection: a
    /// reused idempotency key with a different hash is refused.</summary>
    public string? RequestHash { get; init; }

    /// <summary>Prompt-template version for this operation.</summary>
    public string? PromptVersion { get; init; }

    /// <summary>Rulebook version stamped at operation creation time.</summary>
    public string? RulebookVersion { get; init; }

    /// <summary>Model-route hint for the idempotency key (e.g.
    /// <c>anthropic/claude-sonnet-5</c>). Optional — a caller that does not
    /// yet know the resolved route leaves this null; the operation still gets
    /// a stable key from the other eight dimensions.</summary>
    public string? ModelRoute { get; init; }

    /// <summary>Maximum PHYSICAL provider turns this one operation may make
    /// (the gateway's bounded tool loop makes one billed call per turn). Not a
    /// retry budget — W4 owns retries.</summary>
    public int AttemptLimit { get; init; } = 3;

    /// <summary>Fully-built gateway request. <see cref="AiGatewayRequest.FeatureCode"/>,
    /// <see cref="AiGatewayRequest.UserId"/>, and <see cref="AiGatewayRequest.TenantId"/>
    /// are read from this instance — the coordinator does not duplicate them
    /// as separate properties.</summary>
    public required AiGatewayRequest GatewayRequest { get; init; }
}

/// <summary>
/// Builds the stable business-identity slot persisted as
/// <see cref="AiOperation.ResourceSlotKey"/> and enforced by the UNIQUE
/// partial index <c>UX_AiOperations_ResourceSlotKey</c>.
///
/// <para>
/// Deliberately EXCLUDES <see cref="AiOperationRequest.RequestHash"/> and
/// <see cref="AiOperationRequest.ModelRoute"/>: the whole point is that two
/// requests with DIFFERENT payloads (or routes) for the SAME resource collide
/// at the database, which is what turns the old racy read-then-insert check
/// into a real guarantee.
/// </para>
///
/// <para>
/// Returns null when <see cref="AiOperationRequest.ResourceId"/> is blank.
/// A null slot never participates in the unique index, so free-form
/// interactive calls — which have no stable caller resource — are never
/// conflated with one another.
/// </para>
/// </summary>
public static class AiOperationResourceSlot
{
    public static string? Build(
        string feature,
        string module,
        string? userId,
        string? resourceId,
        string? resourceType,
        int? resourceVersion,
        string? promptVersion,
        string? rulebookVersion)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return null;

        var canonical = string.Join(
            '|',
            Normalize(feature),
            Normalize(module),
            Normalize(userId),
            Normalize(resourceId),
            Normalize(resourceType),
            resourceVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(promptVersion),
            Normalize(rulebookVersion));

        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        // 32 hex chars (128 bits) keeps the column comfortably inside its
        // 64-char budget while leaving collision probability negligible.
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

/// <summary>Outcome of <see cref="IAiExecutionCoordinator.ExecuteAsync"/>.</summary>
public sealed class AiOperationExecutionResult
{
    public required AiOperation Operation { get; init; }

    /// <summary>True when this call resolved to an already-existing
    /// operation instead of invoking a provider (same canonical action,
    /// replayed/duplicated request). <see cref="GatewayResult"/> is null in
    /// that case — the original caller already received the completion; this
    /// result only proves no second provider call happened.</summary>
    public bool WasDuplicate { get; init; }

    /// <summary>Terminal state of the existing operation when
    /// <see cref="WasDuplicate"/> is true; the freshly reached state
    /// otherwise. Mirrors <c>Operation.State</c> and is surfaced explicitly so
    /// callers do not have to re-read the entity.</summary>
    public AiOperationState State => Operation.State;

    /// <summary>Durable result pointer of the operation, when one exists.
    /// Only ever set from a usage row that actually committed.</summary>
    public string? ResultRef => Operation.ResultRef;

    /// <summary>Null exactly when <see cref="WasDuplicate"/> is true.</summary>
    public AiGatewayResult? GatewayResult { get; init; }
}

/// <summary>
/// Thrown when a duplicate request resolves to an operation that is already
/// terminal but whose original domain/gateway payload cannot be reconstructed
/// (W2 stores only a <see cref="AiOperation.ResultRef"/> pointer, not the
/// completion body). Returning "success with a null result" would be a lie
/// the caller could persist; charging the provider again would be a duplicate
/// spend. So the caller is told the truth — the work is done, here is where it
/// lives — and can fetch it from its own domain store.
/// Error code <c>ai_operation_duplicate_result_unavailable</c>.
/// </summary>
public sealed class AiOperationDuplicateResultUnavailableException : InvalidOperationException
{
    public AiOperationDuplicateResultUnavailableException(
        string operationId, AiOperationState state, string? resultRef)
        : base($"AI operation '{operationId}' already reached state '{state}'; its result is not reconstructable from the control plane (ai_operation_duplicate_result_unavailable).")
    {
        OperationId = operationId;
        State = state;
        ResultRef = resultRef;
        ErrorCode = "ai_operation_duplicate_result_unavailable";
    }

    public string OperationId { get; }
    public AiOperationState State { get; }
    public string? ResultRef { get; }
    public string ErrorCode { get; }
}

/// <summary>Thrown when the same idempotency key is reused with a different
/// <see cref="AiOperationRequest.RequestHash"/> — the caller is asking for a
/// different logical action under an identity that already means something
/// else. Error code <c>ai_operation_conflict</c>.</summary>
public sealed class AiOperationConflictException : InvalidOperationException
{
    public AiOperationConflictException(string idempotencyKey)
        : base($"AI operation idempotency key '{idempotencyKey}' is already bound to a different request (ai_operation_conflict).")
    {
        IdempotencyKey = idempotencyKey;
        ErrorCode = "ai_operation_conflict";
    }

    public string IdempotencyKey { get; }
    public string ErrorCode { get; }
}

/// <summary>Thrown when a concurrent caller is racing the same canonical
/// action and this instance lost the atomic insert but the winner has not
/// reached a terminal state within the bounded wait. Retryable — the caller
/// should back off and retry rather than treat this as a hard failure. Error
/// code <c>ai_operation_in_flight</c>. Durable leasing/queueing across
/// process restarts is out of scope for W2 (owned by W4).</summary>
public sealed class AiOperationInFlightException : InvalidOperationException
{
    public AiOperationInFlightException(string idempotencyKey)
        : base($"AI operation idempotency key '{idempotencyKey}' is still in flight on another caller (ai_operation_in_flight).")
    {
        IdempotencyKey = idempotencyKey;
        ErrorCode = "ai_operation_in_flight";
    }

    public string IdempotencyKey { get; }
    public string ErrorCode { get; }
}
