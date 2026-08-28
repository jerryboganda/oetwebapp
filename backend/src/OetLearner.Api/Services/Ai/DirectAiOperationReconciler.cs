using System.Net.Http;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// makes "every exit path from a held lease reconciles the operation" a single
/// reusable rule instead of a per-call-site convention.
///
/// <para>
/// The defect this closes: direct (non-gateway) callers opened an
/// <see cref="AiOperation"/> in <see cref="AiOperationState.Leased"/> BEFORE
/// touching a provider — correct — but only closed it on the success path. Any
/// OCR failure, transport failure, schema/manifest parse failure or save
/// failure left the row Leased forever. A permanently-leased row is not just
/// untidy: it is the exact shape the control plane treats as "someone else is
/// working on this", so it blocks every future authorized attempt at that
/// resource version.
/// </para>
///
/// <para>
/// <b>Completion is fail-soft and never swallows the original error.</b> The
/// caller's exception is always re-thrown; reconciliation failures are logged
/// by <see cref="IDirectAiCallRecorder.CompleteOperationAsync"/> itself.
/// </para>
/// </summary>
public static class DirectAiOperationReconciler
{
    /// <summary>
    /// Runs <paramref name="work"/> under an owned lease and guarantees the
    /// operation leaves <see cref="AiOperationState.Leased"/> on every path.
    /// Success is the caller's responsibility to close (it alone knows the
    /// durable <c>ResultRef</c>); this wrapper owns only the failure paths.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        IDirectAiCallRecorder recorder,
        DirectAiOperationLease lease,
        string? providerId,
        Func<Task<T>> work,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(work);

        if (!lease.CanProceed || string.IsNullOrWhiteSpace(lease.OperationId))
        {
            // Nothing was leased, so there is nothing to reconcile. Guarding
            // here keeps call sites from having to branch.
            return await work();
        }

        try
        {
            return await work();
        }
        catch (Exception ex)
        {
            await recorder.CompleteOperationAsync(
                lease.OperationId!,
                ClassifyFailure(ex, ct),
                resultRef: null,
                providerId,
                model: null,
                CancellationToken.None,
                lease.BudgetReservation);
            throw;
        }
    }

    /// <summary>
    /// Terminal state for a failure that happened while the lease was held.
    ///
    /// <list type="bullet">
    ///   <item><b>Cancelled</b> — the caller walked away. Truthful and safe to
    ///   replace with a new attempt later.</item>
    ///   <item><b>Indeterminate</b> — the request may already have been
    ///   accepted and billed by the provider and we cannot prove otherwise
    ///   (client timeout, mid-flight connection/TLS failure). Never
    ///   auto-replayed at the same identity.</item>
    ///   <item><b>FailedTerminal</b> — everything else: a proven pre-send
    ///   transport failure, an HTTP error status (the provider answered, so the
    ///   outcome is known), a schema/manifest parse failure, or a persistence
    ///   failure after a known-good response.</item>
    /// </list>
    ///
    /// <para>
    /// The pre-send safe-list mirrors
    /// <c>ListeningPartAAiScoringService.IsSafePreSendFailure</c>: only
    /// <c>NameResolutionError</c> and <c>ProxyTunnelError</c> prove no byte
    /// reached the provider. <c>ConnectionError</c>/<c>SecureConnectionError</c>
    /// are raised for mid-flight failures too, so they stay ambiguous.
    /// </para>
    /// </summary>
    public static AiOperationState ClassifyFailure(Exception exception, CancellationToken ct)
    {
        // W3: a budget refusal is always pre-provider-call (see
        // AiBudgetService/AiGatewayService's reservation gate) — the precise,
        // purpose-built terminal state, not the generic FailedTerminal catch-
        // all below, so an admin reading AiOperations can tell "we refused to
        // spend" apart from "the provider rejected the request".
        if (exception is AiBudgetExhaustedException) return AiOperationState.BlockedBudget;

        if (exception is OperationCanceledException)
        {
            // A caller-triggered cancellation is a decision, not an outcome.
            // Anything else cancelling (an HttpClient timeout) means the
            // request may be in flight at the provider — ambiguous.
            return ct.IsCancellationRequested ? AiOperationState.Cancelled : AiOperationState.Indeterminate;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not HttpRequestException http) continue;

            // A status code means the provider answered: the outcome is known.
            if (http.StatusCode is not null) return AiOperationState.FailedTerminal;

            return http.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ProxyTunnelError
                ? AiOperationState.FailedTerminal
                : AiOperationState.Indeterminate;
        }

        return AiOperationState.FailedTerminal;
    }
}
