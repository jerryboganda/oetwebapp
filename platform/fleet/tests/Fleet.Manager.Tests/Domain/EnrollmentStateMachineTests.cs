using Fleet.Core.Domain;

namespace Fleet.Manager.Tests.Domain;

/// <summary>RW-140: only the transitions of OET-RWP/1 section 8.1 exist, and failure reasons come from the stable enum.</summary>
public sealed class EnrollmentStateMachineTests
{
    private static readonly (EnrollmentState From, EnrollmentState To)[] Expected =
    {
        (EnrollmentState.Created, EnrollmentState.HostKeyPending),
        (EnrollmentState.Created, EnrollmentState.Cancelled),
        (EnrollmentState.Created, EnrollmentState.Failed),
        (EnrollmentState.HostKeyPending, EnrollmentState.HostKeyConfirmed),
        (EnrollmentState.HostKeyPending, EnrollmentState.Cancelled),
        (EnrollmentState.HostKeyPending, EnrollmentState.Failed),
        (EnrollmentState.HostKeyConfirmed, EnrollmentState.Bootstrapping),
        (EnrollmentState.HostKeyConfirmed, EnrollmentState.Cancelled),
        (EnrollmentState.Bootstrapping, EnrollmentState.Provisioned),
        (EnrollmentState.Bootstrapping, EnrollmentState.Failed),
        (EnrollmentState.Bootstrapping, EnrollmentState.Cancelled),
        (EnrollmentState.Provisioned, EnrollmentState.ImageAwaitingSync),
        (EnrollmentState.Provisioned, EnrollmentState.ImagePulling),
        (EnrollmentState.ImageAwaitingSync, EnrollmentState.ImagePulling),
        (EnrollmentState.ImageAwaitingSync, EnrollmentState.Cancelled),
        (EnrollmentState.ImagePulling, EnrollmentState.AgentStarting),
        (EnrollmentState.ImagePulling, EnrollmentState.Failed),
        (EnrollmentState.AgentStarting, EnrollmentState.Verifying),
        (EnrollmentState.AgentStarting, EnrollmentState.Failed),
        (EnrollmentState.Verifying, EnrollmentState.Canary),
        (EnrollmentState.Verifying, EnrollmentState.Failed),
        (EnrollmentState.Canary, EnrollmentState.Active),
        (EnrollmentState.Canary, EnrollmentState.Failed),
        (EnrollmentState.Failed, EnrollmentState.Cancelled),
    };

    [Fact]
    public void Every_state_pair_is_allowed_exactly_when_the_table_says_so()
    {
        foreach (var from in Enum.GetValues<EnrollmentState>())
        {
            foreach (var to in Enum.GetValues<EnrollmentState>())
            {
                var expected = Expected.Contains((from, to));
                Assert.True(
                    expected == EnrollmentStateMachine.CanTransition(from, to),
                    $"{from} -> {to} should be {(expected ? "allowed" : "forbidden")}");
            }
        }
    }

    [Fact]
    public void Require_throws_for_a_forbidden_move()
    {
        var error = Assert.Throws<InvalidTransitionException>(() => EnrollmentStateMachine.Require(EnrollmentState.Created, EnrollmentState.Active));
        Assert.Equal("Created", error.From);
        Assert.Equal("Active", error.To);
        Assert.Equal(EnrollmentState.HostKeyPending, EnrollmentStateMachine.Require(EnrollmentState.Created, EnrollmentState.HostKeyPending));
    }

    [Fact]
    public void A_failed_operation_can_only_resume_into_a_state_that_has_a_failed_edge()
    {
        foreach (var state in Enum.GetValues<EnrollmentState>())
        {
            Assert.Equal(
                EnrollmentStateMachine.CanTransition(state, EnrollmentState.Failed),
                EnrollmentStateMachine.CanRetry(state));
        }

        Assert.False(EnrollmentStateMachine.CanRetry(EnrollmentState.Failed));
        Assert.False(EnrollmentStateMachine.CanRetry(EnrollmentState.Provisioned));
        Assert.True(EnrollmentStateMachine.CanRetry(EnrollmentState.ImagePulling));
    }

    [Fact]
    public void Terminal_states_have_no_exit_and_waiting_states_are_not_runnable()
    {
        Assert.Empty(EnrollmentStateMachine.NextStates(EnrollmentState.Active));
        Assert.Empty(EnrollmentStateMachine.NextStates(EnrollmentState.Cancelled));
        Assert.True(EnrollmentStateMachine.IsTerminal(EnrollmentState.Active));
        Assert.True(EnrollmentStateMachine.IsTerminal(EnrollmentState.Cancelled));

        foreach (var state in Enum.GetValues<EnrollmentState>())
        {
            Assert.False(EnrollmentStateMachine.WaitsForOwner(state) && EnrollmentStateMachine.IsRunnable(state), state + " cannot both wait and run");
        }

        Assert.True(EnrollmentStateMachine.WaitsForOwner(EnrollmentState.HostKeyPending));
        Assert.True(EnrollmentStateMachine.WaitsForOwner(EnrollmentState.ImageAwaitingSync));
        Assert.True(EnrollmentStateMachine.IsRunnable(EnrollmentState.Provisioned));
    }

    [Fact]
    public void Cancel_is_possible_exactly_from_the_states_with_a_cancelled_edge()
    {
        foreach (var state in Enum.GetValues<EnrollmentState>())
        {
            Assert.Equal(Expected.Contains((state, EnrollmentState.Cancelled)), EnrollmentStateMachine.CanCancel(state));
        }
    }

    [Fact]
    public void The_thirteen_steps_map_to_consecutive_phases()
    {
        Assert.Equal(13, EnrollSteps.All.Count);
        for (var seq = 1; seq <= 13; seq++)
        {
            var step = (EnrollStep)seq;
            Assert.Equal(seq, EnrollSteps.Seq(step));
            Assert.True(EnrollSteps.TryParse(EnrollSteps.Name(step), out var parsed));
            Assert.Equal(step, parsed);
        }

        Assert.Equal(EnrollmentState.Bootstrapping, EnrollSteps.StateFor(EnrollStep.Preflight));
        Assert.Equal(EnrollmentState.Bootstrapping, EnrollSteps.StateFor(EnrollStep.DiscardOwnerKey));
        Assert.Equal(EnrollmentState.ImagePulling, EnrollSteps.StateFor(EnrollStep.Image));
        Assert.Equal(EnrollmentState.AgentStarting, EnrollSteps.StateFor(EnrollStep.AgentStart));
        Assert.Equal(EnrollmentState.Verifying, EnrollSteps.StateFor(EnrollStep.Verify));
        Assert.Equal(EnrollmentState.Canary, EnrollSteps.StateFor(EnrollStep.Canary));
        Assert.Equal(EnrollmentState.Canary, EnrollSteps.StateFor(EnrollStep.Activate));

        // Only S1..S7 may use the owner credential; S8 destroys it.
        Assert.True(EnrollSteps.UsesOwnerCredential(EnrollStep.HardenSsh));
        Assert.False(EnrollSteps.UsesOwnerCredential(EnrollStep.DiscardOwnerKey));
        Assert.False(EnrollSteps.UsesOwnerCredential(EnrollStep.Image));
    }

    [Fact]
    public void Failure_reasons_are_the_stable_enum()
    {
        Assert.Equal(26, FailureReasons.All.Count);
        Assert.True(FailureReasons.IsKnown(FailureReasons.HostKeyChanged));
        Assert.False(FailureReasons.IsKnown("something_else"));
        Assert.Equal(FailureReasons.InternalError, FailureReasons.Normalize("something_else"));
        Assert.Equal(FailureReasons.CanaryTimeout, FailureReasons.Normalize("canary_timeout"));
        Assert.Contains("existing_oet_workload", FailureReasons.PreflightDetails);
    }

    [Fact]
    public void Operation_kinds_round_trip_through_their_wire_names()
    {
        foreach (var kind in Enum.GetValues<OperationKind>())
        {
            Assert.True(OperationKinds.TryParse(OperationKinds.ToWire(kind), out var parsed));
            Assert.Equal(kind, parsed);
        }

        Assert.False(OperationKinds.TryParse("format-disk", out _));
        Assert.Equal("rotate-token", OperationKinds.ToWire(OperationKind.RotateToken));
    }
}
