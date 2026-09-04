using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingCreditReserveCommitTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public SpeakingCreditReserveCommitTests()
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
    public async Task ReserveThenCommit_IsIdempotentOnBusinessReference()
    {
        var credits = new StubPackageCredits();
        var svc = new OetLearner.Api.Services.Ai.AiCreditReservationService(_db, credits, TimeProvider.System);
        var first = await svc.ReserveSpeakingAsync("user-1", "op-1", "exam:e1:cardA", default);
        var second = await svc.ReserveSpeakingAsync("user-1", "op-2", "exam:e1:cardA", default);

        Assert.Equal(first.ReservationId, second.ReservationId);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(1, credits.DeductCalls);

        await svc.CommitByBusinessReferenceAsync("exam:e1:cardA", default);
        await svc.CommitByBusinessReferenceAsync("exam:e1:cardA", default);

        var row = await _db.AiCreditReservations.SingleAsync();
        Assert.Equal(AiCreditReservationState.Committed, row.State);
    }

    private sealed class StubPackageCredits : IAiPackageCreditService
    {
        public int DeductCalls { get; private set; }

        public Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
            => Task.FromResult(new AiPackageCreditSnapshot(
                userId, FlexibleCredits: 2, WritingOnlyCredits: 0, SpeakingOnlyCredits: 1,
                ListeningTestsRemaining: 0, ReadingTestsRemaining: 0, MockExamsRemaining: 0,
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(30), ExpiredBecausePassed: false, PassedAt: null,
                Transactions: Array.Empty<AiPackageCreditTransactionDto>(),
                SpeakingUnlimited: false, SharedCredits: 0));

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
        {
            DeductCalls++;
            return Task.FromResult(new AiPackageDebitResult(true, null, null, "debit-ref-1", Bypassed: false));
        }

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
            => DeductGradingCreditAsync(userId, subtest, referenceId, ct);

        public Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct)
            => Task.FromResult(true);

        public Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<bool> GrantCourseGiftCreditsAsync(string userId, string planCode, string planName, int credits, string referenceId, DateTimeOffset? expiresAt, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductObjectivePracticeAsync(string userId, string subtest, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageDebitResult> DeductMockAsync(string userId, string referenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> AdjustAsync(string userId, AiPackageCreditAdjustmentRequest request, string adminId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> RecordExamOutcomeAsync(string userId, LearnerExamOutcomeRequest request, string adminId, string adminName, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<int> ReverseGrantsAsync(string userId, string sourceReferenceId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task RecalculateObjectiveAllowancesAsync(string userId, CancellationToken ct)
            => throw new NotImplementedException();
        public Task ParkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
            => Task.CompletedTask;
        public Task UnparkSubscriptionLotsAsync(string userId, string subscriptionId, CancellationToken ct)
            => Task.CompletedTask;
        public Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateGrantWindowAsync(string userId, string subscriptionId, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
