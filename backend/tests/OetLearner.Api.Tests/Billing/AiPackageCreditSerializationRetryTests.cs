using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests.Billing;

/// <summary>
/// Production 3 Oct 2026 (writing-prod-qa run 37100838037): concurrent learners got HTTP 500
/// "likely due to a transient failure" (Npgsql 40001) from the eligibility debit and the admin
/// adjust because the SERIALIZABLE ledger transaction was never retried. A serialization failure
/// at COMMIT is the worst case: SaveChanges already flushed the debit inside the doomed
/// transaction, so a naive retry that kept the change tracker would double-debit or crash.
/// Relational (SQLite) so the real transaction path runs; the conflict is injected at commit.
/// </summary>
public sealed class AiPackageCreditSerializationRetryTests
{
    private sealed class FailCommits(int failures, string sqlState = "40001") : DbTransactionInterceptor
    {
        public int Remaining = failures;
        public bool Armed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && Remaining > 0)
            {
                Remaining--;
                throw new PostgresException("could not serialize access due to read/write dependencies among transactions", "ERROR", "ERROR", sqlState);
            }

            return ValueTask.FromResult(result);
        }
    }

    private static (LearnerDbContext Db, SqliteConnection Connection) NewDb(FailCommits interceptor)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options);
        db.Database.EnsureCreated();
        return (db, connection);
    }

    private static BillingAddOn WritingStarter() => new()
    {
        Id = "addon_pkg_writing_starter",
        Code = "pkg_writing_starter",
        Name = "pkg_writing_starter",
        Price = 1m,
        Currency = "GBP",
        Interval = "one_time",
        Status = BillingAddOnStatus.Active,
        DurationDays = 30,
        GrantCredits = 3,
        GrantEntitlementsJson = """{"package_type":"writing","writing_only_credits":6}""",
        AddonKind = "ai_package",
        AppliesToAllPlans = true,
        IsStackable = true,
        QuantityStep = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public async Task SerializationFailureAtCommit_IsRetried_AndDebitsExactlyOnce(string sqlState)
    {
        var interceptor = new FailCommits(1, sqlState);
        var (db, connection) = NewDb(interceptor);
        await using var _ = db;
        await using var __ = connection;
        var service = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await service.GrantPackageAsync("learner-1", WritingStarter(), 1, "cs_writing", null, CancellationToken.None);

        interceptor.Armed = true;
        var debit = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "writing-v2:learner-1:start:1", AiGradingCreditCost.WritingExam, CancellationToken.None);

        Assert.Equal(0, interceptor.Remaining);
        Assert.True(debit.Debited);
        Assert.Equal(2, debit.CreditsUsed);
        await using var fresh = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        Assert.Equal(1, await fresh.AiPackageCreditTransactions.CountAsync(row => row.Reason == AiPackageCreditReason.GradingDeduct));
        Assert.Equal(4, (await fresh.AiPackageCreditAccounts.SingleAsync()).WritingOnlyCredits);

        // The same reference afterwards is the idempotent no-op it always was.
        var again = await service.DeductGradingCreditAsync(
            "learner-1", "writing", "writing-v2:learner-1:start:1", AiGradingCreditCost.WritingExam, CancellationToken.None);
        Assert.Equal("already_debited", again.ErrorCode);
        Assert.Equal(1, await fresh.AiPackageCreditTransactions.CountAsync(row => row.Reason == AiPackageCreditReason.GradingDeduct));
    }

    [Fact]
    public async Task AdminAdjust_SerializationFailure_IsRetried_AndAppliesOnce()
    {
        var interceptor = new FailCommits(2);
        var (db, connection) = NewDb(interceptor);
        await using var _ = db;
        await using var __ = connection;
        var service = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await service.GrantPackageAsync("learner-1", WritingStarter(), 1, "cs_writing", null, CancellationToken.None);

        interceptor.Armed = true;
        var snapshot = await service.AdjustAsync(
            "learner-1", new AiPackageCreditAdjustmentRequest(0, 0, 0, 0, 0, 0, null, "qa", SharedCreditsDelta: 4), "admin-1", CancellationToken.None);

        Assert.Equal(4, snapshot.SharedCredits);
        await using var fresh = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        Assert.Equal(1, await fresh.AiPackageCreditTransactions.CountAsync(row => row.Reason == AiPackageCreditReason.AdminAdjustment));
    }

    [Fact]
    public async Task PersistentSerializationFailure_GivesUpAfterBoundedAttempts_WithoutDebiting()
    {
        var interceptor = new FailCommits(int.MaxValue);
        var (db, connection) = NewDb(interceptor);
        await using var _ = db;
        await using var __ = connection;
        var service = new AiPackageCreditService(db, NullLogger<AiPackageCreditService>.Instance);
        await service.GrantPackageAsync("learner-1", WritingStarter(), 1, "cs_writing", null, CancellationToken.None);

        interceptor.Armed = true;
        await Assert.ThrowsAsync<PostgresException>(() => service.DeductGradingCreditAsync(
            "learner-1", "writing", "ref-1", AiGradingCreditCost.WritingExam, CancellationToken.None));

        Assert.Equal(int.MaxValue - AiPackageCreditService.MaxLedgerAttempts, interceptor.Remaining);
        await using var fresh = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        Assert.Equal(0, await fresh.AiPackageCreditTransactions.CountAsync(row => row.Reason == AiPackageCreditReason.GradingDeduct));
        Assert.Equal(6, (await fresh.AiPackageCreditAccounts.SingleAsync()).WritingOnlyCredits);
    }

    [Fact]
    public void IsSerializationConflict_SeesThroughEfTransientWrapper()
    {
        var inner = new PostgresException("conflict", "ERROR", "ERROR", "40001");
        var wrapped = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", inner);
        Assert.True(AiPackageCreditService.IsSerializationConflict(wrapped));
        Assert.True(AiPackageCreditService.IsSerializationConflict(new DbUpdateException("x", inner)));
        Assert.False(AiPackageCreditService.IsSerializationConflict(new PostgresException("dup", "ERROR", "ERROR", "23505")));
    }
}
