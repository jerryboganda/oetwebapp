using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

public sealed class WritingEntitlementServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingEntitlementServiceTests()
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
    public async Task CheckAsync_SubscriptionWithoutUnlimitedGrant_UsesExactRemaining()
    {
        var svc = new WritingEntitlementService(
            _db,
            new StubResolver(hasSubscription: true, isTrial: false),
            new StubOptions(),
            new StubCredits(writingUnlimited: false, writingOnly: 3, flexible: 1, shared: 2));

        var result = await svc.CheckAsync("user-1", default);

        Assert.True(result.Allowed);
        Assert.Equal(5, result.Remaining);
        Assert.DoesNotContain("unlimited writing attempts", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_UnlimitedCatalogueGrant_IsUnlimited()
    {
        var svc = new WritingEntitlementService(
            _db,
            new StubResolver(hasSubscription: true, isTrial: false),
            new StubOptions(),
            new StubCredits(writingUnlimited: true, writingOnly: 0, flexible: 0, shared: 0));

        var result = await svc.CheckAsync("user-1", default);

        Assert.True(result.Allowed);
        Assert.Equal(int.MaxValue, result.Remaining);
        Assert.Contains("unlimited writing grant", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_PaidPackageZeroCredits_IsNotUnlimited()
    {
        var svc = new WritingEntitlementService(
            _db,
            new StubResolver(hasSubscription: false, isTrial: false),
            new StubOptions { FreeTierEnabled = false },
            new StubCredits(writingUnlimited: false, writingOnly: 0, flexible: 0, shared: 0));

        var result = await svc.CheckAsync("user-1", default);

        Assert.False(result.Allowed);
        Assert.Equal(0, result.Remaining);
        Assert.Equal("premium_required", result.Reason);
    }

    private sealed class StubResolver(bool hasSubscription, bool isTrial) : IEffectiveEntitlementResolver
    {
        public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
            => Task.FromResult(new EffectiveEntitlementSnapshot(
                userId,
                hasSubscription,
                isTrial,
                hasSubscription ? "paid" : "free",
                null, null, null, null, null, null, null,
                Array.Empty<string>(),
                false,
                Array.Empty<string>()));
    }

    private sealed class StubOptions : IWritingOptionsProvider
    {
        public bool FreeTierEnabled { get; set; }

        public Task<WritingOptions> GetAsync(CancellationToken ct)
            => Task.FromResult(new WritingOptions
            {
                FreeTierEnabled = FreeTierEnabled,
                FreeTierLimit = 1,
                FreeTierWindowDays = 7,
            });

        public Task<WritingOptions> UpdateAsync(WritingOptions update, string? adminId, CancellationToken ct)
            => Task.FromResult(update);
    }

    private sealed class StubCredits(bool writingUnlimited, int writingOnly, int flexible, int shared) : IAiPackageCreditService
    {
        public Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
            => Task.FromResult(new AiPackageCreditSnapshot(
                userId,
                flexible,
                writingOnly,
                SpeakingOnlyCredits: 0,
                ListeningTestsRemaining: 0,
                ReadingTestsRemaining: 0,
                MockExamsRemaining: 0,
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
                ExpiredBecausePassed: false,
                PassedAt: null,
                Transactions: Array.Empty<AiPackageCreditTransactionDto>(),
                WritingUnlimited: writingUnlimited,
                SharedCredits: shared));

        public Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<bool> GrantCourseGiftCreditsAsync(string userId, string planCode, string planName, int credits, string referenceId, DateTimeOffset? expiresAt, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<int> ReverseGrantsAsync(string userId, string sourceReferenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateGrantWindowAsync(string userId, string subscriptionId, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
