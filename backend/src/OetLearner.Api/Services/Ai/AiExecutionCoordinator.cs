using Microsoft.Extensions.Options;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// OBSERVE-MODE coordinator. Every call:
/// <list type="number">
///   <item>gates on the feature policy (Production refuses blank/unknown/
///   explicitly-disabled/not-yet-effective/expired policies BEFORE any
///   provider work);</item>
///   <item>computes the canonical <see cref="AiIdempotencyKeyBuilder"/> key
///   AND the stable <see cref="AiOperationResourceSlot"/>;</item>
///   <item>persists exactly one <see cref="AiOperation"/> row BEFORE calling
///   the core executor, letting the database decide ownership through two
///   unique indexes (see <see cref="IAiOperationStore"/>);</item>
///   <item>calls <see cref="IAiGatewayCoreExecutor.CompleteAsync"/> exactly
///   once when this instance won the insert;</item>
///   <item>reconciles the operation to a terminal state, stamping
///   <see cref="AiOperation.ResultRef"/> only when the usage row really
///   committed.</item>
/// </list>
///
/// <para>
/// <b>Dependency direction.</b> This type injects
/// <see cref="IAiGatewayCoreExecutor"/>, never <see cref="IAiGatewayService"/>.
/// Production resolves <see cref="IAiGatewayService"/> to
/// <see cref="CoordinatedAiGatewayService"/>, which calls this coordinator;
/// if the coordinator resolved <see cref="IAiGatewayService"/> it would call
/// the facade again and recurse forever. <c>AiGatewayCompositionTests</c>
/// asserts structurally that this constructor has no
/// <see cref="IAiGatewayService"/> parameter.
/// </para>
///
/// <para>
/// <b>Concurrency.</b> There is no read-then-insert anywhere on this path.
/// Two callers racing the identical canonical action both INSERT; the
/// idempotency-key unique index lets exactly one win and the loser is
/// classified from the real SQLSTATE 23505 by constraint name. Two callers
/// racing DIFFERENT payloads against the SAME supplied resource also both
/// INSERT; the partial unique index on
/// <see cref="AiOperation.ResourceSlotKey"/> lets exactly one win and the
/// loser gets <see cref="AiOperationConflictException"/>. The loser never
/// calls a provider in either case. Durable cross-process leasing and
/// requeueing of a stalled winner remains W4's job.
/// </para>
///
/// <para>
/// <b>Bounded replay</b> (see <see cref="AiOperationReplayPolicy"/>). The
/// idempotency key is derived from stable request content, so without a bound
/// the FIRST operation for a canonical action would own that key forever: a
/// terminal failure or a month-old completion would refuse every future
/// identical request. Deduplication is therefore state- AND time-bounded — an
/// in-flight or recently-completed twin still resolves to the existing
/// operation with no second provider call, while a safe failure (or a
/// completion older than the configured replay window) lets this caller open a
/// NEW operation by bumping the replay discriminator through the existing
/// <see cref="AiOperation.ResourceVersion"/> dimension, exactly as
/// <see cref="DirectAiCallRecorder"/> already does.
/// <see cref="AiOperationState.Indeterminate"/> is never replayed.
/// </para>
///
/// <para>
/// <b>Observe mode</b> (W2 scope): no budget/credit reservation is made yet
/// (<see cref="AiOperation.CreditReservationId"/>/<see cref="AiOperation.BudgetReservationId"/>
/// stay null — W3 populates them). The coordinator only predicts and logs the
/// effective pricing delta for observability.
/// </para>
/// </summary>
public interface IAiExecutionCoordinator
{
    Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct);
}

public sealed class AiExecutionCoordinator(
    IAiOperationStore store,
    IAiGatewayCoreExecutor core,
    IAiFeaturePolicyRegistry? featurePolicyRegistry = null,
    IAiPricingResolver? pricingResolver = null,
    Microsoft.Extensions.Hosting.IHostEnvironment? hostEnvironment = null,
    ILogger<AiExecutionCoordinator>? logger = null,
    IOptions<AiExecutionCoordinationOptions>? coordinationOptions = null) : IAiExecutionCoordinator
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private const int MaxPollAttempts = 40; // ~4s bounded wait — see class remarks.

    private readonly TimeSpan _replayWindow = AiOperationReplayPolicy.Clamp(
        coordinationOptions?.Value is { } o
            ? TimeSpan.FromSeconds(Math.Max(0, o.ReplayWindowSeconds))
            : AiOperationReplayPolicy.DefaultReplayWindow);

    private readonly int _maxReplayRounds = AiOperationReplayPolicy.ClampRounds(
        coordinationOptions?.Value?.MaxReplayRounds ?? AiOperationReplayPolicy.DefaultMaxReplayRounds);

    public async Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.GatewayRequest);

        var featureCode = string.IsNullOrWhiteSpace(request.GatewayRequest.FeatureCode)
            ? AiFeatureCodes.Unclassified
            : request.GatewayRequest.FeatureCode!;

        var lookup = await ResolvePolicyAsync(featureCode, ct);
        var module = request.Module
            ?? lookup?.Policy?.Module
            ?? DeriveModuleFallback(featureCode);
        var operationClass = lookup?.Policy?.OperationClass ?? AiOperationClass.InteractiveLearning;

        // Replay discriminator. Round 0 uses the caller's own resource version,
        // so the common case is byte-identical to the pre-fix key. Only a
        // provably-safe stale predecessor bumps it (see AiOperationReplayPolicy).
        var replayVersion = request.ResourceVersion;
        AiOperation? lastExisting = null;
        var lastIdempotencyKey = string.Empty;

        for (var round = 0; round < _maxReplayRounds; round++)
        {
            var idempotencyKey = AiIdempotencyKeyBuilder.Build(
                feature: featureCode,
                module: module,
                userId: request.GatewayRequest.UserId,
                resourceId: request.ResourceId,
                resourceVersion: replayVersion,
                requestHash: request.RequestHash,
                promptVersion: request.PromptVersion,
                rulebookVersion: request.RulebookVersion,
                modelRoute: request.ModelRoute);
            lastIdempotencyKey = idempotencyKey;

            var resourceSlotKey = AiOperationResourceSlot.Build(
                feature: featureCode,
                module: module,
                userId: request.GatewayRequest.UserId,
                resourceId: request.ResourceId,
                resourceType: request.ResourceType,
                resourceVersion: replayVersion,
                promptVersion: request.PromptVersion,
                rulebookVersion: request.RulebookVersion);

            var now = DateTimeOffset.UtcNow;
            var operation = new AiOperation
            {
                Id = Guid.NewGuid().ToString("N"),
                Module = module,
                FeatureCode = featureCode,
                UserId = request.GatewayRequest.UserId,
                TenantId = request.GatewayRequest.TenantId,
                ResourceId = request.ResourceId,
                ResourceType = request.ResourceType,
                ResourceVersion = replayVersion,
                RequestHash = request.RequestHash,
                PromptVersion = request.PromptVersion,
                RulebookVersion = request.RulebookVersion,
                IdempotencyKey = idempotencyKey,
                ResourceSlotKey = resourceSlotKey,
                State = AiOperationState.Queued,
                OperationClass = operationClass,
                AttemptLimit = Math.Max(1, request.AttemptLimit),
                CreatedAt = now,
                UpdatedAt = now,
            };

            var insert = await store.TryInsertAsync(operation, ct);
            if (insert.Outcome == AiOperationInsertOutcome.Inserted)
            {
                return await RunOwnedOperationAsync(operation, request, ct);
            }

            if (insert.Outcome == AiOperationInsertOutcome.ResourceSlotConflict)
            {
                // A DIFFERENT payload owns this business slot. Auto-bumping here
                // would let two divergent payloads both run against one
                // resource, which is exactly what the slot index exists to stop.
                throw new AiOperationConflictException(idempotencyKey);
            }

            var resolution = await ResolveExistingAsync(idempotencyKey, request.RequestHash, ct);
            lastExisting = resolution.Existing;

            if (resolution.Decision != AiOperationReplayDecision.CreateNewAttempt)
            {
                // Never a second provider call.
                return new AiOperationExecutionResult
                {
                    Operation = resolution.Existing,
                    WasDuplicate = true,
                    GatewayResult = null,
                };
            }

            logger?.LogInformation(
                "AI operation replay: feature {FeatureCode} resource {ResourceId} found a replayable predecessor {OperationId} in state {State}; opening a new attempt at replay version {ReplayVersion}.",
                featureCode, request.ResourceId, resolution.Existing.Id, resolution.Existing.State,
                AiOperationReplayPolicy.NextVersion(replayVersion));

            replayVersion = AiOperationReplayPolicy.NextVersion(replayVersion);
        }

        // Bounded walk exhausted: every replay slot we are willing to try is
        // already taken. Report the last predecessor truthfully rather than
        // looping — the caller still gets "the work exists, here it is".
        logger?.LogWarning(
            "AI operation replay for feature {FeatureCode} resource {ResourceId} exhausted {Rounds} rounds; no provider call was made.",
            featureCode, request.ResourceId, _maxReplayRounds);

        return lastExisting is null
            ? throw new AiOperationInFlightException(lastIdempotencyKey)
            : new AiOperationExecutionResult
            {
                Operation = lastExisting,
                WasDuplicate = true,
                GatewayResult = null,
            };
    }

    /// <summary>
    /// Policy gate, applied BEFORE the operation row and long before any
    /// provider resolution. Production fails closed on every non-usable
    /// status; Development/Test stay permissive so the pre-W2 suite keeps
    /// working. A lookup outage is treated as Unknown (fail closed in
    /// Production) and the exception message is never persisted.
    /// </summary>
    private async Task<AiFeaturePolicyLookup?> ResolvePolicyAsync(string featureCode, CancellationToken ct)
    {
        if (featurePolicyRegistry is null) return null;

        AiFeaturePolicyLookup lookup;
        try
        {
            lookup = await featurePolicyRegistry.LookupAsync(featureCode, ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "AI feature policy lookup failed for {FeatureCode}.", featureCode);
            lookup = new AiFeaturePolicyLookup(featureCode, AiFeaturePolicyStatus.Unknown, null);
        }

        if (!lookup.IsUsable && hostEnvironment?.IsProduction() == true)
        {
            throw new AiFeaturePolicyRefusedException(featureCode, lookup.Reason);
        }

        return lookup;
    }

    private async Task<AiOperationExecutionResult> RunOwnedOperationAsync(
        AiOperation operation,
        AiOperationRequest request,
        CancellationToken ct)
    {
        var gatewayRequest = request.GatewayRequest with
        {
            OperationId = operation.Id,
            ResourceId = request.GatewayRequest.ResourceId ?? request.ResourceId,
            ResourceType = request.GatewayRequest.ResourceType ?? request.ResourceType,
            ResourceVersion = request.GatewayRequest.ResourceVersion ?? request.ResourceVersion,
        };

        AiGatewayResult gatewayResult;
        try
        {
            gatewayResult = await core.CompleteAsync(gatewayRequest, ct);
        }
        catch (Exception ex)
        {
            // Not every escaping exception proves the provider never received
            // or acted on the request: a client-side TimeoutException or a
            // mid-flight HttpRequestException may already have been accepted
            // and billed. Reuse the same classifier DirectAiOperationReconciler
            // applies to direct (non-gateway) callers so both control-plane
            // entry points draw an identical, truthful line between "safe to
            // replay" (Cancelled/FailedTerminal) and "must never be
            // auto-repeated" (Indeterminate). CancellationToken.None: a
            // cancelled caller must not leave its own operation dangling as
            // in-flight after a call that may already have reached the
            // provider.
            var terminalState = DirectAiOperationReconciler.ClassifyFailure(ex, ct);
            await MarkTerminalAsync(operation.Id, terminalState, resultRef: null, CancellationToken.None);
            throw;
        }

        // ResultRef must never point at a usage id the recorder failed to
        // commit — a dangling pointer is worse than no pointer.
        var resultRef = gatewayResult.UsagePersisted ? gatewayResult.UsageRecordId : null;
        if (!gatewayResult.UsagePersisted)
        {
            logger?.LogWarning(
                "AI operation {OperationId} completed but no usage row committed; leaving ResultRef null for reconciliation.",
                operation.Id);
        }

        await MarkTerminalAsync(
            operation.Id,
            AiOperationState.Completed,
            resultRef: resultRef,
            ct: CancellationToken.None,
            selectedProviderId: gatewayResult.ResolvedProvider,
            selectedModel: gatewayResult.ResolvedModel);

        await ObserveEffectivePricingAsync(operation, gatewayResult);

        operation.State = AiOperationState.Completed;
        operation.ResultRef = resultRef;
        operation.SelectedProviderId = gatewayResult.ResolvedProvider;
        operation.SelectedModel = gatewayResult.ResolvedModel;
        return new AiOperationExecutionResult
        {
            Operation = operation,
            WasDuplicate = false,
            GatewayResult = gatewayResult,
        };
    }

    /// <summary>
    /// Observe mode only: predict what the effective, date-bounded rate card
    /// would have charged for this call and log the delta against the flat
    /// legacy estimate the gateway used. No budget or credit reservation is
    /// made (that is W3) and nothing here can fail the call.
    /// </summary>
    private async Task ObserveEffectivePricingAsync(AiOperation operation, AiGatewayResult result)
    {
        if (pricingResolver is null || result.Usage is null) return;
        if (string.IsNullOrWhiteSpace(result.ResolvedProvider) || string.IsNullOrWhiteSpace(result.ResolvedModel)) return;

        try
        {
            var pricing = await pricingResolver.ResolveAsync(
                result.ResolvedProvider, result.ResolvedModel, DateTimeOffset.UtcNow, CancellationToken.None);
            if (pricing is null) return;

            var effective = pricing.ComputeCostUsd(
                normalInputTokens: result.Usage.PromptTokens,
                normalOutputTokens: result.Usage.CompletionTokens,
                cacheWriteTokens: 0,
                cacheReadTokens: 0);

            logger?.LogInformation(
                "AI operation {OperationId} feature {FeatureCode}: effective cost {EffectiveCost} ({PricingVersion}) vs legacy estimate {EstimatedCost}, delta {Delta}.",
                operation.Id, operation.FeatureCode, effective, pricing.PricingVersion,
                result.EstimatedCostUsd, effective - result.EstimatedCostUsd);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "AI operation {OperationId}: effective pricing observation failed (call outcome unaffected).",
                operation.Id);
        }
    }

    private async Task MarkTerminalAsync(
        string operationId,
        AiOperationState state,
        string? resultRef,
        CancellationToken ct,
        string? selectedProviderId = null,
        string? selectedModel = null)
    {
        try
        {
            var moved = await store.TryMarkTerminalAsync(
                operationId, state, resultRef, selectedProviderId, selectedModel, ct);
            if (!moved)
            {
                logger?.LogWarning(
                    "AI operation {OperationId} was already terminal when reconciling to {State}; existing outcome preserved.",
                    operationId, state);
            }
        }
        catch (Exception ex)
        {
            // Reconciliation must never mask the core's own success/failure
            // outcome and must NEVER trigger a second provider call. The row
            // reads stale until a future wave's reconciler sweeps it; the
            // AiUsageRecord/AiOperationAttempt rows remain the source of truth.
            logger?.LogError(ex, "AiExecutionCoordinator failed to reconcile operation {OperationId} to {State}.", operationId, state);
        }
    }

    /// <summary>
    /// Resolves the operation that already owns <paramref name="idempotencyKey"/>
    /// and classifies what this caller may do next. A non-terminal winner is
    /// waited on (bounded) — the loser must never call a provider while the
    /// winner is mid-flight. A terminal winner is classified by
    /// <see cref="AiOperationReplayPolicy"/>.
    /// </summary>
    private async Task<ReplayResolution> ResolveExistingAsync(string idempotencyKey, string? requestHash, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            var existing = await store.FindByIdempotencyKeyAsync(idempotencyKey, ct);

            if (existing is null)
            {
                // Extremely narrow window: the winner's insert has not yet
                // committed as visible to this read. Treat as still in flight
                // and keep polling rather than fabricating a result.
                await Task.Delay(PollInterval, ct);
                continue;
            }

            if (!string.Equals(existing.RequestHash ?? string.Empty, requestHash ?? string.Empty, StringComparison.Ordinal))
            {
                // Defensive: the idempotency key already hashes RequestHash,
                // so a genuine collision must carry a matching hash.
                throw new AiOperationConflictException(idempotencyKey);
            }

            var decision = AiOperationReplayPolicy.Decide(
                existing.State, existing.UpdatedAt, DateTimeOffset.UtcNow, _replayWindow);

            if (decision != AiOperationReplayDecision.WaitInFlight)
            {
                return new ReplayResolution(existing, decision);
            }

            await Task.Delay(PollInterval, ct);
        }

        throw new AiOperationInFlightException(idempotencyKey);
    }

    private readonly record struct ReplayResolution(AiOperation Existing, AiOperationReplayDecision Decision);

    private static string DeriveModuleFallback(string featureCode)
        => string.IsNullOrWhiteSpace(featureCode) || !featureCode.Contains('.')
            ? "unclassified"
            : featureCode.Split('.', 2)[0];
}
