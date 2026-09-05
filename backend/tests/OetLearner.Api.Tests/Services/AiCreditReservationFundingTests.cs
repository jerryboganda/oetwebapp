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
/// The Dedicated → Flexible → Shared-at-2 rule must live in exactly one
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
    public async Task ReserveWriting_DedicatedPool_RecordsWritingBucket_OneUnit()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""),
            1, "cs-w", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.False(ticket.AlreadyExisted);
        Assert.Equal("writing", ticket.BucketKind);
        Assert.Equal(1, ticket.Units);
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
    public async Task ReserveWriting_FlexibleOnlyPool_RecordsFlexibleBucket_OneUnit()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_flex_one", 30, 1, """{"package_type":"full","flexible_credits":1}"""),
            1, "cs-flex", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("flexible_ws", ticket.BucketKind);
        Assert.Equal(1, ticket.Units);
    }

    [Fact]
    public async Task ReserveWriting_DedicatedBeatsShared_PriorityHeld()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""),
            1, "cs-w", null, CancellationToken.None);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_shared_one", 30, 1, """{"package_type":"full","shared_credits":2}"""),
            1, "cs-shared", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveWritingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.Equal("writing", ticket.BucketKind);
        Assert.Equal(1, ticket.Units);
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
            AddOn("pkg_writing_single", 30, 1, """{"package_type":"writing","writing_only_credits":1}"""),
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

    [Fact]
    public async Task ReserveSpeaking_DedicatedPool_RecordsSpeakingBucket_OneUnit()
    {
        await using var db = NewContext();
        var ledger = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await ledger.GrantPackageAsync("learner-1",
            AddOn("pkg_speaking_single", 30, 1, """{"package_type":"speaking","speaking_only_credits":1}"""),
            1, "cs-s", null, CancellationToken.None);

        var ticket = await NewReservations(db, ledger)
            .ReserveSpeakingAsync("learner-1", "op-1", "biz-1", CancellationToken.None);

        Assert.False(ticket.AlreadyExisted);
        Assert.Equal("speaking", ticket.BucketKind);
        Assert.Equal(1, ticket.Units);
    }
}
