using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Services;

public sealed class AiCreditReservationPriorityTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly FakeAiPackageCreditService _credits = new();
    private readonly AiCreditReservationService _sut;
    private readonly string _operationId;

    public AiCreditReservationPriorityTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o =>
            o.UseInMemoryDatabase(dbName)
             .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IAiPackageCreditService>(_credits);
        _provider = services.BuildServiceProvider(validateScopes: true);
        _sut = new AiCreditReservationService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditReservationService>.Instance);

        _operationId = Guid.NewGuid().ToString("N");
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.AiOperations.Add(new AiOperation
        {
            Id = _operationId,
            Module = "writing",
            FeatureCode = AiFeatureCodes.WritingGrade,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            State = AiOperationState.Leased,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task ReserveAsync_DeductsAndPersistsReservation()
    {
        var result = await _sut.ReserveAsync(
            "user-1", _operationId, "writing", 1, "op-1:attempt-1", default);

        Assert.True(result.Granted);
        Assert.False(string.IsNullOrWhiteSpace(result.ReservationId));
        Assert.Equal(1, _credits.CheckCalls);
        Assert.Equal(1, _credits.DeductCalls);
        Assert.Equal("user-1", _credits.LastDeductUserId);
        Assert.Equal("writing", _credits.LastDeductSubtest);
        Assert.Equal("op-1:attempt-1", _credits.LastDeductReference);
        Assert.Equal(1, _credits.LastDeductQuantity);

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCreditReservations.AsNoTracking()
            .SingleAsync();
        Assert.Equal(result.ReservationId, row.Id);
        Assert.Equal(AiCreditReservationState.Reserved, row.State);
        Assert.Equal("dedicated", row.BucketKind);
        Assert.Equal(1, row.Units);
        Assert.Equal(_operationId, row.OperationId);
    }

    [Fact]
    public async Task ReserveAsync_IsIdempotentOnBusinessReference()
    {
        var first = await _sut.ReserveAsync("user-1", _operationId, "writing", 1, "same-ref", default);
        var second = await _sut.ReserveAsync("user-1", _operationId, "writing", 1, "same-ref", default);

        Assert.True(first.Granted);
        Assert.True(second.Granted);
        Assert.Equal(first.ReservationId, second.ReservationId);
        Assert.Equal(1, _credits.DeductCalls);
    }

    [Fact]
    public async Task ReserveAsync_FailsClosedWhenCheckDenies()
    {
        _credits.CheckResult = new AiPackageDebitResult(false, "no_ai_package_credits", "none", null);

        var result = await _sut.ReserveAsync("user-1", _operationId, "writing", 1, "denied-ref", default);

        Assert.False(result.Granted);
        Assert.Equal("no_ai_package_credits", result.DenyReason);
        Assert.Equal(0, _credits.DeductCalls);

        using var scope = _provider.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCreditReservations.CountAsync());
    }

    [Fact]
    public async Task CommitAsync_MarksCommittedWithoutRefund()
    {
        var reserved = await _sut.ReserveAsync("user-1", _operationId, "writing", 1, "commit-ref", default);
        await _sut.CommitAsync(reserved.ReservationId!, default);

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCreditReservations.AsNoTracking()
            .SingleAsync();
        Assert.Equal(AiCreditReservationState.Committed, row.State);
        Assert.Equal(0, _credits.RefundCalls);
    }

    [Fact]
    public async Task ReleaseAsync_RefundsThenMarksReleased()
    {
        var reserved = await _sut.ReserveAsync("user-1", _operationId, "writing", 1, "release-ref", default);
        await _sut.ReleaseAsync(reserved.ReservationId!, default);

        Assert.Equal(1, _credits.RefundCalls);
        Assert.Equal("release-ref", _credits.LastRefundOriginalReference);
        Assert.Equal("release-ref:release", _credits.LastRefundReference);

        using var scope = _provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AiCreditReservations.AsNoTracking()
            .SingleAsync();
        Assert.Equal(AiCreditReservationState.Released, row.State);
    }

    private sealed class FakeAiPackageCreditService : IAiPackageCreditService
    {
        public int CheckCalls { get; private set; }
        public int DeductCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public string? LastDeductUserId { get; private set; }
        public string? LastDeductSubtest { get; private set; }
        public string? LastDeductReference { get; private set; }
        public int LastDeductQuantity { get; private set; }
        public string? LastRefundOriginalReference { get; private set; }
        public string? LastRefundReference { get; private set; }
        public AiPackageDebitResult CheckResult { get; set; } = new(true, null, null, null);
        public AiPackageDebitResult DeductResult { get; set; } =
            new(true, null, null, "debit-1", BalanceSource: "dedicated");

        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, CancellationToken ct)
            => CheckGradingCreditAsync(userId, subtest, 1, ct);

        public Task<AiPackageDebitResult> CheckGradingCreditAsync(string userId, string subtest, int quantity, CancellationToken ct)
        {
            CheckCalls++;
            return Task.FromResult(CheckResult);
        }

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, CancellationToken ct)
            => DeductGradingCreditAsync(userId, subtest, referenceId, 1, ct);

        public Task<AiPackageDebitResult> DeductGradingCreditAsync(string userId, string subtest, string referenceId, int quantity, CancellationToken ct)
        {
            DeductCalls++;
            LastDeductUserId = userId;
            LastDeductSubtest = subtest;
            LastDeductReference = referenceId;
            LastDeductQuantity = quantity;
            return Task.FromResult(DeductResult);
        }

        public Task<bool> RefundAsync(string userId, string originalReferenceId, string refundReferenceId, string description, CancellationToken ct)
        {
            RefundCalls++;
            LastRefundOriginalReference = originalReferenceId;
            LastRefundReference = refundReferenceId;
            return Task.FromResult(true);
        }

        public Task<AiPackageCreditSnapshot> GetSnapshotAsync(string userId, int transactionLimit, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<AiPackageCreditSnapshot> GrantPackageAsync(string userId, BillingAddOn addOn, int quantity, string stripeSessionId, string? quoteId, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
            => throw new NotImplementedException();
        public Task<bool> GrantCourseGiftCreditsAsync(string userId, string planCode, string planName, int credits, string referenceId, DateTimeOffset? expiresAt, CancellationToken ct, string? sourceReferenceId = null, DateTimeOffset? validFrom = null)
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
        public Task UpdateGrantExpiryAsync(string userId, string subscriptionId, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task UpdateGrantWindowAsync(string userId, string subscriptionId, DateTimeOffset? validFrom, DateTimeOffset? expiresAt, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<bool> HasObjectivePracticeAllowanceAsync(string userId, string subtest, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
