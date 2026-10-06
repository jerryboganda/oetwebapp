namespace Fleet.Core.Domain;

/// <summary>Manager-side host lifecycle (OET-RWP/1 section 8.1).</summary>
public enum HostLifecycle
{
    Enrolling,
    Active,
    Draining,
    Disabled,
    Removing,
    Removed,
    Failed,
}

/// <summary>States of an <c>enroll</c> operation (OET-RWP/1 section 8.1 table).</summary>
public enum EnrollmentState
{
    Created,
    HostKeyPending,
    HostKeyConfirmed,
    Bootstrapping,
    Provisioned,
    ImageAwaitingSync,
    ImagePulling,
    AgentStarting,
    Verifying,
    Canary,
    Active,
    Failed,
    Cancelled,
}

/// <summary>States of every non-enroll operation (they share the operations/operation_steps mechanics).</summary>
public enum GenericOperationState
{
    Queued,
    Running,

    /// <summary>A repair that needs the owner to supply a temporary root-capable key.</summary>
    AwaitingOwner,
    Succeeded,
    Failed,
    Cancelled,
}

public enum OperationKind
{
    Enroll,
    Drain,
    Disable,
    Enable,
    Remove,
    RotateToken,
    Rollout,
    Repair,
}

public enum OperationStepState
{
    Pending,
    Running,
    Done,
    Skipped,
    Failed,
}

public static class OperationKinds
{
    public static string ToWire(OperationKind kind) => kind switch
    {
        OperationKind.Enroll => "enroll",
        OperationKind.Drain => "drain",
        OperationKind.Disable => "disable",
        OperationKind.Enable => "enable",
        OperationKind.Remove => "remove",
        OperationKind.RotateToken => "rotate-token",
        OperationKind.Rollout => "rollout",
        OperationKind.Repair => "repair",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? wire, out OperationKind kind)
    {
        switch (wire)
        {
            case "enroll": kind = OperationKind.Enroll; return true;
            case "drain": kind = OperationKind.Drain; return true;
            case "disable": kind = OperationKind.Disable; return true;
            case "enable": kind = OperationKind.Enable; return true;
            case "remove": kind = OperationKind.Remove; return true;
            case "rotate-token": kind = OperationKind.RotateToken; return true;
            case "rollout": kind = OperationKind.Rollout; return true;
            case "repair": kind = OperationKind.Repair; return true;
            default: kind = default; return false;
        }
    }
}

/// <summary>The thirteen enrollment steps S1..S13 of OET-RWP/1 section 8.2 (numeric value = sequence).</summary>
public enum EnrollStep
{
    Preflight = 1,
    FleetUser = 2,
    InstallKey = 3,
    Docker = 4,
    Firewall = 5,
    HostBaseline = 6,
    HardenSsh = 7,
    DiscardOwnerKey = 8,
    Image = 9,
    AgentStart = 10,
    Verify = 11,
    Canary = 12,
    Activate = 13,
}

public static class EnrollSteps
{
    public static readonly IReadOnlyList<EnrollStep> All = Enum.GetValues<EnrollStep>();

    public static int Seq(EnrollStep step) => (int)step;

    public static string Name(EnrollStep step) => step switch
    {
        EnrollStep.Preflight => "preflight",
        EnrollStep.FleetUser => "fleet-user",
        EnrollStep.InstallKey => "install-key",
        EnrollStep.Docker => "docker",
        EnrollStep.Firewall => "firewall",
        EnrollStep.HostBaseline => "host-baseline",
        EnrollStep.HardenSsh => "harden-ssh",
        EnrollStep.DiscardOwnerKey => "discard-owner-key",
        EnrollStep.Image => "image",
        EnrollStep.AgentStart => "agent-start",
        EnrollStep.Verify => "verify",
        EnrollStep.Canary => "canary",
        EnrollStep.Activate => "activate",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    public static bool TryParse(string? name, out EnrollStep step)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Name(candidate), name, StringComparison.Ordinal))
            {
                step = candidate;
                return true;
            }
        }

        step = default;
        return false;
    }

    /// <summary>S1..S7 run with the temporary owner credential (S8 destroys it).</summary>
    public static bool UsesOwnerCredential(EnrollStep step) => step is >= EnrollStep.Preflight and <= EnrollStep.HardenSsh;

    /// <summary>
    /// The operation state that must be in force while <paramref name="step"/> runs. S13
    /// (activate) runs in <see cref="EnrollmentState.Canary"/> and moves the operation to
    /// <see cref="EnrollmentState.Active"/> when it succeeds.
    /// </summary>
    public static EnrollmentState StateFor(EnrollStep step) => step switch
    {
        <= EnrollStep.DiscardOwnerKey => EnrollmentState.Bootstrapping,
        EnrollStep.Image => EnrollmentState.ImagePulling,
        EnrollStep.AgentStart => EnrollmentState.AgentStarting,
        EnrollStep.Verify => EnrollmentState.Verifying,
        _ => EnrollmentState.Canary,
    };
}
