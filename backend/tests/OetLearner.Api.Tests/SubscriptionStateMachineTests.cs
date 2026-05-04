using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Slice D — Subscription state machine unit tests. These intentionally
/// exercise <see cref="SubscriptionStateMachine"/> in isolation (no DI, no
/// HTTP) so the legal-transition table cannot regress unnoticed.
/// </summary>
public class SubscriptionStateMachineTests
{
    [Theory]
    // Trial → ...
    [InlineData(SubscriptionStatus.Trial, SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.Trial, SubscriptionStatus.Cancelled, true)]
    [InlineData(SubscriptionStatus.Trial, SubscriptionStatus.Expired, true)]
    [InlineData(SubscriptionStatus.Trial, SubscriptionStatus.PastDue, false)]
    [InlineData(SubscriptionStatus.Trial, SubscriptionStatus.Suspended, false)]
    // Active → ...
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Suspended, true)] // dispute pending
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Cancelled, true)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Expired, true)]
    [InlineData(SubscriptionStatus.Active, SubscriptionStatus.Trial, false)] // can't go back to trial
    // PastDue → ...
    [InlineData(SubscriptionStatus.PastDue, SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PastDue, SubscriptionStatus.Cancelled, true)]
    [InlineData(SubscriptionStatus.PastDue, SubscriptionStatus.Suspended, true)]
    [InlineData(SubscriptionStatus.PastDue, SubscriptionStatus.Trial, false)]
    // Cancelled → ...
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionStatus.Expired, true)]
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionStatus.Active, false)] // resubscribe = new sub
    [InlineData(SubscriptionStatus.Cancelled, SubscriptionStatus.Trial, false)]
    // Expired terminal
    [InlineData(SubscriptionStatus.Expired, SubscriptionStatus.Active, false)]
    [InlineData(SubscriptionStatus.Expired, SubscriptionStatus.Cancelled, false)]
    public void IsLegal_ReturnsExpected(SubscriptionStatus from, SubscriptionStatus to, bool expected)
    {
        Assert.Equal(expected, SubscriptionStateMachine.IsLegal(from, to));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public void IsLegal_AllowsSelfTransition_ForIdempotentReplays(SubscriptionStatus state)
    {
        Assert.True(SubscriptionStateMachine.IsLegal(state, state));
    }

    [Fact]
    public void Transition_LegalChange_MutatesStatusAndChangedAt()
    {
        var sub = NewSubscription(SubscriptionStatus.Active);
        var before = sub.ChangedAt;
        var changed = SubscriptionStateMachine.Transition(sub, SubscriptionStatus.PastDue, "dunning");
        Assert.True(changed);
        Assert.Equal(SubscriptionStatus.PastDue, sub.Status);
        Assert.True(sub.ChangedAt >= before);
    }

    [Fact]
    public void Transition_SelfTransition_ReturnsFalseAndDoesNotMutate()
    {
        var sub = NewSubscription(SubscriptionStatus.Active);
        var before = sub.ChangedAt;
        var changed = SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Active, "webhook-replay");
        Assert.False(changed);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(before, sub.ChangedAt);
    }

    [Fact]
    public void Transition_IllegalChange_ThrowsConflictWithStructuredCode()
    {
        var sub = NewSubscription(SubscriptionStatus.Cancelled);
        var ex = Assert.Throws<ApiException>(
            () => SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Active, "manual-revive"));
        Assert.Equal("subscription_illegal_transition", ex.ErrorCode);
        // Status must be unchanged on rejection.
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);
    }

    [Fact]
    public void Transition_RequiresReason()
    {
        var sub = NewSubscription(SubscriptionStatus.Active);
        Assert.Throws<ArgumentException>(
            () => SubscriptionStateMachine.Transition(sub, SubscriptionStatus.Cancelled, "  "));
    }

    [Fact]
    public void AllowedFrom_UnknownState_ReturnsEmptySet()
    {
        // Defensive: a future enum value not yet in the table must not throw.
        var set = SubscriptionStateMachine.AllowedFrom((SubscriptionStatus)999);
        Assert.Empty(set);
    }

    private static Subscription NewSubscription(SubscriptionStatus status) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = "user-1",
        PlanId = "basic-monthly",
        Status = status,
        NextRenewalAt = DateTimeOffset.UtcNow.AddMonths(1),
        StartedAt = DateTimeOffset.UtcNow.AddDays(-30),
        ChangedAt = DateTimeOffset.UtcNow.AddDays(-1),
        PriceAmount = 0m,
        Currency = "AUD",
        Interval = "monthly",
    };
}
