namespace Fleet.Core.Domain;

public sealed class InvalidTransitionException : InvalidOperationException
{
    public InvalidTransitionException(string from, string to)
        : base("Transition " + from + " -> " + to + " is not allowed.")
    {
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}

/// <summary>
/// The only legal moves of an <c>enroll</c> operation (OET-RWP/1 section 8.1, RW-140).
/// <see cref="EnrollmentState.Failed"/> is entered from any state that has a Failed edge and is
/// left by <em>retry</em> (back to the state the failure interrupted) or by cancel; the state
/// to resume is recorded by the caller (operations.resume_state).
/// </summary>
public static class EnrollmentStateMachine
{
    private static readonly IReadOnlyDictionary<EnrollmentState, EnrollmentState[]> Edges =
        new Dictionary<EnrollmentState, EnrollmentState[]>
        {
            [EnrollmentState.Created] = [EnrollmentState.HostKeyPending, EnrollmentState.Cancelled, EnrollmentState.Failed],
            [EnrollmentState.HostKeyPending] = [EnrollmentState.HostKeyConfirmed, EnrollmentState.Cancelled, EnrollmentState.Failed],
            [EnrollmentState.HostKeyConfirmed] = [EnrollmentState.Bootstrapping, EnrollmentState.Cancelled],
            [EnrollmentState.Bootstrapping] = [EnrollmentState.Provisioned, EnrollmentState.Failed, EnrollmentState.Cancelled],
            [EnrollmentState.Provisioned] = [EnrollmentState.ImageAwaitingSync, EnrollmentState.ImagePulling],
            [EnrollmentState.ImageAwaitingSync] = [EnrollmentState.ImagePulling, EnrollmentState.Cancelled],
            [EnrollmentState.ImagePulling] = [EnrollmentState.AgentStarting, EnrollmentState.Failed],
            [EnrollmentState.AgentStarting] = [EnrollmentState.Verifying, EnrollmentState.Failed],
            [EnrollmentState.Verifying] = [EnrollmentState.Canary, EnrollmentState.Failed],
            [EnrollmentState.Canary] = [EnrollmentState.Active, EnrollmentState.Failed],
            [EnrollmentState.Active] = [],
            // Failed -> <resume state> is validated by CanRetry, not by this table.
            [EnrollmentState.Failed] = [EnrollmentState.Cancelled],
            [EnrollmentState.Cancelled] = [],
        };

    public static bool CanTransition(EnrollmentState from, EnrollmentState to) =>
        Edges.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    public static IReadOnlyList<EnrollmentState> NextStates(EnrollmentState from) =>
        Edges.TryGetValue(from, out var next) ? next : Array.Empty<EnrollmentState>();

    /// <summary>True when an operation stopped in <see cref="EnrollmentState.Failed"/> may be retried into <paramref name="resumeState"/>.</summary>
    public static bool CanRetry(EnrollmentState resumeState) =>
        resumeState != EnrollmentState.Failed && CanTransition(resumeState, EnrollmentState.Failed);

    public static bool IsTerminal(EnrollmentState state) =>
        state is EnrollmentState.Active or EnrollmentState.Cancelled;

    /// <summary>States in which the runner has nothing to do until the owner (or a sync) acts.</summary>
    public static bool WaitsForOwner(EnrollmentState state) =>
        state is EnrollmentState.HostKeyPending
            or EnrollmentState.HostKeyConfirmed
            or EnrollmentState.ImageAwaitingSync
            or EnrollmentState.Failed;

    /// <summary>States the background runner drives forward by itself.</summary>
    public static bool IsRunnable(EnrollmentState state) =>
        state is EnrollmentState.Created
            or EnrollmentState.Bootstrapping
            or EnrollmentState.Provisioned
            or EnrollmentState.ImagePulling
            or EnrollmentState.AgentStarting
            or EnrollmentState.Verifying
            or EnrollmentState.Canary;

    /// <summary>States from which the owner may cancel (the Cancelled edges of the table).</summary>
    public static bool CanCancel(EnrollmentState state) => CanTransition(state, EnrollmentState.Cancelled);

    public static EnrollmentState Require(EnrollmentState from, EnrollmentState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidTransitionException(from.ToString(), to.ToString());
        }

        return to;
    }

    public static bool TryParse(string? value, out EnrollmentState state) =>
        Enum.TryParse(value, ignoreCase: false, out state) && Enum.IsDefined(state);
}
