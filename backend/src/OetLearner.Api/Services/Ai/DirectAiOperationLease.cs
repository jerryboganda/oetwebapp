using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Whether a direct (non-gateway) AI caller may invoke its provider.
/// Anything other than <see cref="Proceed"/> means <b>do not send</b>.
/// </summary>
public enum DirectAiOperationDisposition
{
    /// <summary>This caller owns the operation and must perform the work.</summary>
    Proceed = 0,

    /// <summary>The same canonical action is already recorded (in flight or
    /// terminal). The provider must not be called again.</summary>
    Duplicate = 1,

    /// <summary>A different payload already owns this resource slot. The
    /// provider must not be called; bump the resource version instead.</summary>
    Conflict = 2,

    /// <summary>The feature policy refuses this call (unclassified, unknown,
    /// disabled, not yet effective, or expired). Zero provider calls.</summary>
    PolicyRefused = 3,

    /// <summary>The control plane could not record the operation. Because an
    /// untracked paid call is exactly the incident being remediated, the
    /// caller must treat this as temporarily unavailable and retry later
    /// rather than sending.</summary>
    Unavailable = 4,
}

/// <summary>
/// Result of <see cref="IDirectAiCallRecorder.BeginOperationAsync"/> — the
/// permission slip a direct Anthropic/OCR caller needs before touching the
/// network, plus the operation identity its usage row must carry.
/// </summary>
public sealed record DirectAiOperationLease(
    DirectAiOperationDisposition Disposition,
    string? OperationId,
    int? AttemptNumber,
    AiOperationState? ExistingState,
    string? ExistingResultRef,
    string Reason)
{
    /// <summary>True only when the caller may invoke the provider.</summary>
    public bool CanProceed => Disposition == DirectAiOperationDisposition.Proceed;

    /// <summary>
    /// W3 — the platform budget hold made for this lease, if any (null for a
    /// blocked lease, or when no <see cref="IAiBudgetService"/> is wired). The
    /// caller does not need to inspect this directly: pass the lease itself
    /// through to <see cref="DirectAiOperationReconciler.RunAsync{T}"/> and to
    /// <see cref="IDirectAiCallRecorder.CompleteOperationAsync"/>'s
    /// <c>budgetReservation</c> parameter on the success path, exactly like
    /// <see cref="OperationId"/>/<see cref="AttemptNumber"/> are already
    /// threaded through.
    /// </summary>
    public AiBudgetReservation? BudgetReservation { get; init; }

    public static DirectAiOperationLease Granted(string operationId, int attemptNumber, AiBudgetReservation? budgetReservation = null)
        => new(DirectAiOperationDisposition.Proceed, operationId, attemptNumber, null, null, "granted")
        {
            BudgetReservation = budgetReservation,
        };

    public static DirectAiOperationLease Blocked(
        DirectAiOperationDisposition disposition,
        string reason,
        string? operationId = null,
        AiOperationState? existingState = null,
        string? existingResultRef = null)
        => new(disposition, operationId, null, existingState, existingResultRef, reason);
}

/// <summary>
/// Identity of one direct AI call, supplied by the caller before transport.
/// Carries only stable identifiers and pre-computed hashes — never a raw
/// prompt, and never provider output.
/// </summary>
public sealed record DirectAiOperationRequest
{
    public required string FeatureCode { get; init; }
    public required string Module { get; init; }
    public string? UserId { get; init; }
    public string? TenantId { get; init; }

    /// <summary>Stable business row this call processes. When blank, no
    /// resource slot is claimed (see <see cref="AiOperationResourceSlot"/>).</summary>
    public string? ResourceId { get; init; }
    public string? ResourceType { get; init; }

    /// <summary>Version of the resource this call targets, and the attempt
    /// discriminator for the operation's identity. Direct Listening paths pass
    /// their durable <c>AiAttemptCount + 1</c>, so a W0 bounded retry is a NEW
    /// operation rather than a duplicate of the failed one.</summary>
    public int? ResourceVersion { get; init; }

    /// <summary>Deterministic digest of the request inputs. Never a raw prompt.</summary>
    public required string RequestHash { get; init; }

    public string? PromptVersion { get; init; }
    public string? RulebookVersion { get; init; }
    public string? ModelRoute { get; init; }

    /// <summary>Fallback routing class when no DB policy row supplies one.</summary>
    public AiOperationClass OperationClass { get; init; } = AiOperationClass.AdminBatch;

    /// <summary>
    /// When true, a duplicate whose existing operation ended in a FAILED
    /// terminal state is treated as legitimate re-work: the recorder bumps
    /// <see cref="ResourceVersion"/> and re-inserts, so a transient failure
    /// cannot permanently wedge a human-triggered admin run. A duplicate that
    /// is still in flight, or that COMPLETED, is never re-leased — that would
    /// be the duplicate charge this remediation exists to prevent.
    /// <para>
    /// Callers that maintain their own durable attempt counter (the W0
    /// Listening scorer) leave this false so their bounded-retry policy stays
    /// the single source of truth.
    /// </para>
    /// </summary>
    public bool AllowRetryAfterFailure { get; init; }
}
