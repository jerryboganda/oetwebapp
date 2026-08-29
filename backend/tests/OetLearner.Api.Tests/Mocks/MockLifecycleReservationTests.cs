using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests.Mocks;

public sealed class MockLifecycleReservationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public MockLifecycleReservationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Debit_IsIdempotentOnMockAttempt_AndCommitIsStable()
    {
        _db.MockEntitlementLedgers.Add(new MockEntitlementLedger
        {
            Id = "led-1",
            UserId = "user-m1",
            AddOnId = "addon-1",
            MockType = MockEntitlementKeys.MockFull,
            ConsumedAt = DateTimeOffset.UtcNow,
            MockAttemptId = "mock-attempt-1",
            ReservationState = MockEntitlementReservationStates.Reserved,
        });
        await _db.SaveChangesAsync();

        var svc = new MockEntitlementService(_db, new FreeResolver(), NullLogger<MockEntitlementService>.Instance);
        var first = await svc.DebitAsync("user-m1", "mockFull", "mock-attempt-1", default);
        var second = await svc.DebitAsync("user-m1", "mockFull", "mock-attempt-1", default);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, await _db.MockEntitlementLedgers.CountAsync());

        await svc.CommitAsync("user-m1", "mockFull", "mock-attempt-1", default);
        await svc.CommitAsync("user-m1", "mockFull", "mock-attempt-1", default);
        Assert.Equal(
            MockEntitlementReservationStates.Committed,
            (await _db.MockEntitlementLedgers.SingleAsync()).ReservationState);
    }

    private sealed class FreeResolver : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
            => Task.FromResult(new EffectiveEntitlementSnapshot(
                UserId: userId,
                HasEligibleSubscription: false,
                IsTrial: false,
                Tier: "free",
                SubscriptionId: null,
                SubscriptionStatus: null,
                PlanId: null,
                PlanVersionId: null,
                PlanCode: null,
                AiQuotaPlanCode: null,
                AiQuotaPlanCodeSource: null,
                ActiveAddOnCodes: Array.Empty<string>(),
                IsFrozen: false,
                Trace: Array.Empty<string>()));
    }
}
