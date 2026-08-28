using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Records <see cref="AiUsageRecord"/> rows for AI calls that do NOT go through
/// <see cref="IAiGatewayService"/> — direct OCR / STT / Anthropic calls. Wraps
/// the scoped <see cref="IAiUsageRecorder"/> behind a singleton-safe scope so it
/// can be injected into singleton providers (Whisper ASR selectors, etc.).
/// <para>
/// Every method is fail-soft: a telemetry failure must never break the learner
/// or admin call that triggered it. Exceptions are logged, not propagated.
/// </para>
/// </summary>
public interface IDirectAiCallRecorder
{
    /// <summary>Returns the persisted usage-row id, or null when the row could
    /// not be committed (this recorder is fail-soft). Anything durable that
    /// points at a usage row must gate on a non-null result.</summary>
    Task<string?> RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiUsage? usage,
        int latencyMs,
        string? policyTrace,
        decimal costEstimateUsd,
        CancellationToken ct,
        AiCacheTokenBreakdown? cacheTokens = null,
        string? operationId = null,
        int? attemptNumber = null);

    /// <summary>Returns the persisted usage-row id, or null when the row could
    /// not be committed.</summary>
    Task<string?> RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        string? policyTrace,
        CancellationToken ct,
        string? operationId = null,
        int? attemptNumber = null);

    /// <summary>
    /// W2 — opens a durable <see cref="AiOperation"/> for a direct
    /// (non-gateway) AI call. MUST be awaited, and the returned lease MUST be
    /// honoured, <b>before</b> any provider transport: the whole point is that
    /// no paid call exists without a control-plane row, and that a duplicate,
    /// a conflicting concurrent caller, or a disabled feature policy is
    /// discovered before money is spent, not after.
    /// <para>
    /// Never throws: a control-plane outage returns
    /// <see cref="DirectAiOperationDisposition.Unavailable"/>, which callers
    /// must treat as "try again later" — the one disposition it must never be
    /// is "send anyway".
    /// </para>
    /// </summary>
    Task<DirectAiOperationLease> BeginOperationAsync(DirectAiOperationRequest request, CancellationToken ct);

    /// <summary>
    /// W2 — closes the operation opened by
    /// <see cref="BeginOperationAsync"/> after the usage row and the caller's
    /// own domain writes have been persisted. Fail-soft: a reconciliation
    /// failure is logged and never re-invokes a provider.
    /// <paramref name="resultRef"/> must only be supplied when the referenced
    /// row really committed.
    /// <para>
    /// W3 — pass the SAME <see cref="DirectAiOperationLease.BudgetReservation"/>
    /// the granted lease carried, so the platform budget hold this call made is
    /// committed (on <see cref="AiOperationState.Completed"/>/
    /// <see cref="AiOperationState.ProviderSucceeded"/>) or released (every
    /// other terminal state) exactly once. Omit only for a lease that was never
    /// granted (nothing was reserved) — <see cref="DirectAiOperationReconciler"/>
    /// already threads this through automatically for the failure path.
    /// </para>
    /// </summary>
    Task CompleteOperationAsync(
        string operationId,
        AiOperationState state,
        string? resultRef,
        string? providerId,
        string? model,
        CancellationToken ct,
        AiBudgetReservation? budgetReservation = null);
}

public sealed class DirectAiCallRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<DirectAiCallRecorder> logger,
    IHostEnvironment? hostEnvironment = null,
    IAiBudgetService? budgetService = null) : IDirectAiCallRecorder
{
    /// <summary>Bounds the "previous attempt failed, so this is re-work" walk
    /// in <see cref="BeginOperationAsync"/>. Small on purpose: it exists to
    /// survive a transient failure, not to become an unbounded retry loop.</summary>
    private const int MaxRetryAfterFailureRounds = AiOperationReplayPolicy.DefaultMaxReplayRounds;

    /// <summary>
    /// Only a predecessor we can prove was NOT billed may be replaced by a new
    /// attempt. <see cref="AiOperationState.Indeterminate"/> is deliberately
    /// excluded: an ambiguous outcome may already have been accepted and
    /// charged, so auto-bumping past it would risk exactly the duplicate spend
    /// this remediation exists to prevent. Shared with the coordinator through
    /// <see cref="AiOperationReplayPolicy.IsSafeFailure"/> so both control-plane
    /// entry points cannot drift apart.
    /// </summary>
    private static bool IsSafeToReplace(AiOperationState state)
        => AiOperationReplayPolicy.IsSafeFailure(state);
    public async Task<string?> RecordSuccessAsync(
        AiUsageContext context,
        string providerId,
        string model,
        AiUsage? usage,
        int latencyMs,
        string? policyTrace,
        decimal costEstimateUsd,
        CancellationToken ct,
        AiCacheTokenBreakdown? cacheTokens = null,
        string? operationId = null,
        int? attemptNumber = null)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IAiUsageRecorder>();
            return await recorder.RecordSuccessAsync(
                context, providerId, model, AiKeySource.Platform, usage,
                latencyMs, retryCount: 0, policyTrace, ct, costEstimateUsd: costEstimateUsd,
                operationId: operationId, attemptNumber: attemptNumber,
                cacheTokens: cacheTokens, providerInvoked: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DirectAiCallRecorder: failed to record success for feature {Feature} provider {Provider}",
                context.FeatureCode, providerId);
            return null;
        }
    }

    public async Task<string?> RecordFailureAsync(
        AiUsageContext context,
        string? providerId,
        string? model,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        int latencyMs,
        string? policyTrace,
        CancellationToken ct,
        string? operationId = null,
        int? attemptNumber = null)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IAiUsageRecorder>();
            return await recorder.RecordFailureAsync(
                context, providerId, model, AiKeySource.Platform, outcome,
                errorCode, errorMessage, latencyMs, retryCount: 0, policyTrace, ct,
                operationId: operationId, attemptNumber: attemptNumber, providerInvoked: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DirectAiCallRecorder: failed to record failure for feature {Feature} provider {Provider}",
                context.FeatureCode, providerId);
            return null;
        }
    }

    public async Task<DirectAiOperationLease> BeginOperationAsync(
        DirectAiOperationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var policyRegistry = sp.GetService<IAiFeaturePolicyRegistry>();
            var operationClass = request.OperationClass;
            if (policyRegistry is not null)
            {
                AiFeaturePolicyLookup lookup;
                try
                {
                    lookup = await policyRegistry.LookupAsync(request.FeatureCode, ct);
                }
                catch (Exception ex)
                {
                    // Fail closed in Production: an unreadable policy is not a
                    // licence to spend. The exception text is never persisted.
                    logger.LogError(ex, "DirectAiCallRecorder: policy lookup failed for {Feature}.", request.FeatureCode);
                    lookup = new AiFeaturePolicyLookup(request.FeatureCode, AiFeaturePolicyStatus.Unknown, null);
                }

                if (!lookup.IsUsable && hostEnvironment?.IsProduction() == true)
                {
                    logger.LogWarning(
                        "DirectAiCallRecorder: refusing {Feature} before any provider call ({Reason}).",
                        request.FeatureCode, lookup.Reason);
                    return DirectAiOperationLease.Blocked(DirectAiOperationDisposition.PolicyRefused, lookup.Reason);
                }

                if (lookup.Policy is { } policy) operationClass = policy.OperationClass;
            }

            var store = sp.GetRequiredService<IAiOperationStore>();

            // A duplicate whose predecessor FAILED is re-work, not a replay, so
            // the version is bumped and re-inserted (bounded). Callers that own
            // their own durable attempt counter opt out via
            // AllowRetryAfterFailure = false and get exactly one shot.
            var maxRounds = request.AllowRetryAfterFailure ? MaxRetryAfterFailureRounds : 1;
            var version = request.ResourceVersion;

            for (var round = 0; round < maxRounds; round++)
            {
                var idempotencyKey = AiIdempotencyKeyBuilder.Build(
                    feature: request.FeatureCode,
                    module: request.Module,
                    userId: request.UserId,
                    resourceId: request.ResourceId,
                    resourceVersion: version,
                    requestHash: request.RequestHash,
                    promptVersion: request.PromptVersion,
                    rulebookVersion: request.RulebookVersion,
                    modelRoute: request.ModelRoute);

                var now = DateTimeOffset.UtcNow;
                var operation = new AiOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Module = request.Module,
                    FeatureCode = request.FeatureCode,
                    UserId = request.UserId,
                    TenantId = request.TenantId,
                    ResourceId = request.ResourceId,
                    ResourceType = request.ResourceType,
                    ResourceVersion = version,
                    RequestHash = request.RequestHash,
                    PromptVersion = request.PromptVersion,
                    RulebookVersion = request.RulebookVersion,
                    IdempotencyKey = idempotencyKey,
                    ResourceSlotKey = AiOperationResourceSlot.Build(
                        feature: request.FeatureCode,
                        module: request.Module,
                        userId: request.UserId,
                        resourceId: request.ResourceId,
                        resourceType: request.ResourceType,
                        resourceVersion: version,
                        promptVersion: request.PromptVersion,
                        rulebookVersion: request.RulebookVersion),
                    State = AiOperationState.Leased,
                    OperationClass = operationClass,
                    AttemptLimit = 1,
                    LeaseOwner = nameof(DirectAiCallRecorder),
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var insert = await store.TryInsertAsync(operation, ct);
                if (insert.Outcome == AiOperationInsertOutcome.Inserted)
                {
                    // W3: reserve platform budget only for the winning lease —
                    // a caller that loses the operation-insert race below never
                    // reaches this branch, so it never holds a reservation.
                    // Same fail-closed rule as the operation store itself: a
                    // denied/unavailable reservation means zero provider calls.
                    var reservation = AiBudgetReservation.Unmetered;
                    if (budgetService is not null)
                    {
                        reservation = await budgetService.ReserveAsync(
                            "global", AiBudgetService.DefaultReservationEstimateUsd, ct);
                        if (!reservation.Granted)
                        {
                            logger.LogWarning(
                                "DirectAiCallRecorder: platform budget exhausted for {Feature} resource {ResourceId} ({Reason}); no provider call made. Closing the operation this caller just won.",
                                request.FeatureCode, request.ResourceId, reservation.DenyReason);
                            await store.TryMarkTerminalAsync(
                                operation.Id, AiOperationState.BlockedBudget, resultRef: null,
                                selectedProviderId: null, selectedModel: null, ct);
                            return DirectAiOperationLease.Blocked(
                                DirectAiOperationDisposition.Unavailable, reservation.DenyReason ?? "global_budget_exhausted", operation.Id);
                        }
                    }

                    return DirectAiOperationLease.Granted(operation.Id, version ?? 1, reservation);
                }

                if (insert.Outcome == AiOperationInsertOutcome.ResourceSlotConflict)
                {
                    logger.LogWarning(
                        "DirectAiCallRecorder: resource-slot conflict for {Feature} resource {ResourceId}; no provider call made.",
                        request.FeatureCode, request.ResourceId);
                    return DirectAiOperationLease.Blocked(DirectAiOperationDisposition.Conflict, "resource_slot_conflict");
                }

                var existing = await store.FindByIdempotencyKeyAsync(idempotencyKey, ct);
                if (request.AllowRetryAfterFailure && existing is not null && IsSafeToReplace(existing.State))
                {
                    version = AiOperationReplayPolicy.NextVersion(version);
                    continue;
                }

                logger.LogInformation(
                    "DirectAiCallRecorder: duplicate operation for {Feature} resource {ResourceId} (state {State}); no provider call made.",
                    request.FeatureCode, request.ResourceId, existing?.State);
                return DirectAiOperationLease.Blocked(
                    DirectAiOperationDisposition.Duplicate, "duplicate_operation",
                    existing?.Id, existing?.State, existing?.ResultRef);
            }

            logger.LogWarning(
                "DirectAiCallRecorder: {Feature} resource {ResourceId} exhausted {Rounds} failed-retry rounds; no provider call made.",
                request.FeatureCode, request.ResourceId, maxRounds);
            return DirectAiOperationLease.Blocked(DirectAiOperationDisposition.Duplicate, "retry_rounds_exhausted");
        }
        catch (Exception ex)
        {
            // A control-plane write failure BEFORE the provider must never
            // degrade into "send anyway" — that is precisely the untracked
            // paid call this remediation exists to prevent.
            logger.LogError(ex,
                "DirectAiCallRecorder: could not open an operation for {Feature}; refusing the provider call.",
                request.FeatureCode);
            return DirectAiOperationLease.Blocked(DirectAiOperationDisposition.Unavailable, "operation_store_unavailable");
        }
    }

    public async Task CompleteOperationAsync(
        string operationId,
        AiOperationState state,
        string? resultRef,
        string? providerId,
        string? model,
        CancellationToken ct,
        AiBudgetReservation? budgetReservation = null)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAiOperationStore>();
            var moved = await store.TryMarkTerminalAsync(operationId, state, resultRef, providerId, model, ct);
            if (!moved)
            {
                logger.LogWarning(
                    "DirectAiCallRecorder: operation {OperationId} was already terminal when reconciling to {State}.",
                    operationId, state);
            }
        }
        catch (Exception ex)
        {
            // Fail-soft by design: the provider call already happened and the
            // usage row is the durable spend record. A stale operation row is
            // reconciled by a sweeper; it must never cause a second call.
            logger.LogError(ex,
                "DirectAiCallRecorder: failed to reconcile operation {OperationId} to {State}.",
                operationId, state);
        }

        // W3: reconcile the budget hold independently of the operation-row
        // reconciliation above — a failure to mark the operation terminal must
        // never also leak the budget reservation. Direct callers don't report a
        // precise dollar cost through this method (only RecordSuccessAsync/
        // RecordFailureAsync know it), so this commits/releases the flat
        // reservation estimate rather than a trued-up figure — still fully
        // concurrency-safe, just not cost-exact for this one path. Completed /
        // ProviderSucceeded are the only states in which a provider response
        // was actually received; every other terminal state made zero or an
        // indeterminate provider call, so releasing (not committing) is correct
        // there too, in the same spirit as an indeterminate call still being
        // billable — this is a deliberately conservative under- rather than
        // over-count, since AiUsageRecord (not this ledger) is the source of
        // truth for actual spend.
        if (budgetReservation is { Granted: true })
        {
            if (budgetService is null) return;

            var wasProviderSuccess = state is AiOperationState.Completed or AiOperationState.ProviderSucceeded;
            if (wasProviderSuccess)
            {
                await budgetService.CommitAsync(budgetReservation, budgetReservation.ReservedUsd, CancellationToken.None);
            }
            else
            {
                await budgetService.ReleaseAsync(budgetReservation, CancellationToken.None);
            }
        }
    }
}
