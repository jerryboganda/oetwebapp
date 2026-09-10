using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Billing;
// Both namespaces declare `AiCreditReservationService`; only the Ai one exposes
// ReserveWritingAsync, which is what this fixture drives.
using AiCreditReservationService = OetLearner.Api.Services.Ai.AiCreditReservationService;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Funding-rule vectors for the CreditLedger unification (TA, #193).
/// The Dedicated → Flexible → Shared priority (2 AI credits per activity
/// from any pool, FINAL 2026-09-06) must live in exactly one
/// place (the ledger); these lock the reservation service's observable
/// behavior across the refactor.
/// </summary>
public sealed class AiCreditReservationFundingTests
{
    private static LearnerDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static BillingAddOn AddOn(string code, int durationDays, int grantCredits, string grantJson) => new()
    {
        Id = $"addon_{code}",
        Code = code,
        Name = code,
        Price = 1m,
        Currency = "GBP",
        Interval = "one_time",
        Status = BillingAddOnStatus.Active,
        DurationDays = durationDays,
        GrantCredits = grantCredits,
        GrantEntitlementsJson = grantJson,
        AddonKind = "ai_package",
        AppliesToAllPlans = true,
        IsStackable = true,
        QuantityStep = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AiCreditReservationService NewReservations(LearnerDbContext db, IAiPackageCreditService ledger)
        => new(db, ledger, TimeProvider.System);

    [Fact]
    public async Task ReserveWriting_DedicatedPool_RecordsWritingBucket_TwoUnits()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""),
            1, "cs-w", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.False(ticket.AlreadyExisted);
        Assert.Equal("writing", ticket.BucketKind);
        Assert.Equal(2, ticket.Units);
    }

    [Fact]
    public async Task ReserveWriting_SharedOnlyPool_RecordsSharedBucket_TwoUnits()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_shared_one", 30, 1, """{"package_type":"full","shared_credits":2}"""),
            1, "cs-shared", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("shared", ticket.BucketKind);
        Assert.Equal(2, ticket.Units);
    }

    [Fact]
    public async Task ReserveWriting_FlexibleOnlyPool_RecordsFlexibleBucket_TwoUnits()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_flex_one", 30, 1, """{"package_type":"full","flexible_credits":2}"""),
            1, "cs-flex", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("flexible_ws", ticket.BucketKind);
        Assert.Equal(2, ticket.Units);
    }

    [Fact]
    public async Task ReserveWriting_DedicatedBeatsShared_PriorityHeld()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""),
            1, "cs-w", null, CancellationToken.None);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_shared_one", 30, 1, """{"package_type":"full","shared_credits":2}"""),
            1, "cs-shared", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("writing", ticket.BucketKind);
        Assert.Equal(2, ticket.Units);
    }

    [Fact]
    public async Task ReserveWriting_NoCredits_DeniesWithInsufficient()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            NewReservations(db, ledger).ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None));

        Assert.Equal("ai_credits_insufficient", ex.Code);
        Assert.Equal(402, ex.StatusCode);
    }

    [Fact]
    public async Task ReserveWriting_SameReferenceTwice_DeditsOnce()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":2}"""),
            1, "cs-w", null, CancellationToken.None);
        var svc = NewReservations(db, ledger);

        var first = await svc.ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);
        var second = await svc.ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.ReservationId, second.ReservationId);
        var snapshot = await ledger.GetSnapshotAsync("learner-1", 0, CancellationToken.None);
        Assert.Equal(0, snapshot.WritingOnlyCredits);
    }

    // Owner clarification (Writing Rule Enforcement Addendum Rev5, 10 Sep
    // 2026, §12): "If Writing Credits are Unlimited and valid, authorise the
    // attempt immediately with no decrement... a zero balance in another
    // pool must not block the attempt." Reproduces the production
    // contradiction the addendum reports (dashboard shows Writing Credits =
    // Unlimited; Submit for Grading throws "No AI credits remaining" /
    // "Not enough credits"): AiPackageCreditAccount.ExpiredBecausePassed is a
    // cached, account-wide flag on the credit LEDGER that is independent of
    // an active Mastery subscription (WritingUnlimited's real source —
    // HasActiveUnlimitedGradingAsync checks SubscriptionItems, not the
    // ledger account). Before the fix, ReserveWritingAsync checked that
    // stale ledger flag before ever checking WritingUnlimited, so a valid
    // Mastery subscriber with an expired/never-granted credit ledger row
    // would be denied despite an active Unlimited entitlement.
    [Fact]
    public async Task ReserveWriting_UnlimitedGrading_AuthorisesEvenWithStaleAccountExpiredFlag()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        var now = DateTimeOffset.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-mastery",
            UserId = "learner-1",
            PlanId = "plan-free",
            Status = SubscriptionStatus.Active,
            StartedAt = now,
            ChangedAt = now,
            NextRenewalAt = now.AddDays(180),
            ExpiresAt = now.AddDays(180),
            PriceAmount = 0,
            Currency = "GBP",
            Interval = "one_time",
        });
        db.SubscriptionItems.Add(new SubscriptionItem
        {
            Id = "item-mastery",
            SubscriptionId = "sub-mastery",
            ItemCode = "pkg_oet_mastery",
            ItemType = "addon",
            Status = SubscriptionItemStatus.Active,
            StartsAt = now,
            EndsAt = now.AddDays(180),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        // Force the credit-ledger account row into existence (normally
        // lazily created on first read) so it can be put into the stale
        // state below.
        await ledger.GetSnapshotAsync("learner-1", 0, CancellationToken.None);

        // Simulate the exact staleness the addendum describes: the credit
        // ledger's cached "expired" flag is stuck true (e.g. never renewed
        // alongside the subscription) even though the Mastery subscription
        // itself is live.
        var account = await db.AiPackageCreditAccounts.SingleAsync(a => a.UserId == "learner-1");
        account.ExpiredBecausePassed = true;
        await db.SaveChangesAsync(CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("writing", ticket.BucketKind);
        Assert.Equal(0, ticket.Units);
    }

    [Fact]
    public async Task ReserveSpeaking_DedicatedPool_RecordsSpeakingBucket_TwoUnits()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_speaking_single", 30, 1, """{"package_type":"speaking","speaking_only_credits":2}"""),
            1, "cs-s", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveSpeakingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.False(ticket.AlreadyExisted);
        Assert.Equal("speaking", ticket.BucketKind);
        Assert.Equal(2, ticket.Units);
    }
}
