using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiManagement;
using OetWithDrHesham.Api.Services.Entitlements;

namespace OetWithDrHesham.Api.Tests;

public class AiCreditServiceTests
{
    private static (LearnerDbContext db, AiCreditService credits) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new AiCreditService(db));
    }

    [Fact]
    public async Task Grant_IncreasesBalance()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 1000, 0.05m, AiCreditSource.PlanRenewal, "March", null, null, null, default);
        var balance = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(1000, balance.TokensAvailable);
        Assert.Equal(1000, balance.TokensGrantedLifetime);
        Assert.Equal(0, balance.TokensConsumedLifetime);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DebitUsage_DecreasesBalance_AndIsIdempotentByUsageRecord()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 5, 0m, AiCreditSource.Purchase, "Quick Check", "addon:event-1", null, null, default);

        var first = await credits.DebitUsageAsync(
            new AiCreditUsageDebitRequest(
                UserId: "u1",
                UsageRecordId: "usage-1",
                FeatureCode: AiFeatureCodes.WritingGrade,
                Credits: 1,
                CostUsd: 0.004m),
            default);
        var second = await credits.DebitUsageAsync(
            new AiCreditUsageDebitRequest(
                UserId: "u1",
                UsageRecordId: "usage-1",
                FeatureCode: AiFeatureCodes.WritingGrade,
                Credits: 1,
                CostUsd: 0.004m),
            default);

        var balance = await credits.GetBalanceAsync("u1", default);
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(4, balance.TokensAvailable);
        Assert.Equal(1, balance.TokensConsumedLifetime);
        Assert.Single(await db.AiCreditLedger.Where(x => x.Source == AiCreditSource.UsageDebit).ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task DebitUsage_ReturnsFalse_WhenBalanceIsInsufficient()
    {
        var (db, credits) = Build();

        var debited = await credits.DebitUsageAsync(
            new AiCreditUsageDebitRequest(
                UserId: "u1",
                UsageRecordId: "usage-no-credit",
                FeatureCode: AiFeatureCodes.WritingGrade,
                Credits: 1,
                CostUsd: 0.004m),
            default);

        Assert.False(debited);
        Assert.Empty(await db.AiCreditLedger.ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetBalance_NegativeAdminAdjustment_DecreasesAvailableCredits()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 30, 0m, AiCreditSource.Purchase, "OET Mastery", "addon:purchase-1", null, null, default);
        db.AiCreditLedger.Add(new AiCreditLedgerEntry
        {
            Id = "refund-1",
            UserId = "u1",
            TokensDelta = -30,
            CostDeltaUsd = 0m,
            Source = AiCreditSource.AdminAdjustment,
            Description = "Refund reversal",
            ReferenceId = "addon-refund:purchase-1",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var balance = await credits.GetBalanceAsync("u1", default);

        Assert.Equal(0, balance.TokensAvailable);
        Assert.Equal(30, balance.TokensGrantedLifetime);
        Assert.Equal(0, balance.TokensConsumedLifetime);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetBalance_OldUsageDebit_DoesNotSuppressNewPurchaseAfterOldGrantExpires()
    {
        var (db, credits) = Build();
        var now = DateTimeOffset.UtcNow;
        db.AiCreditLedger.AddRange(
            new AiCreditLedgerEntry
            {
                Id = "old-grant",
                UserId = "u1",
                TokensDelta = 5,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                ReferenceId = "addon:old",
                ExpiresAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-30),
            },
            new AiCreditLedgerEntry
            {
                Id = "old-usage",
                UserId = "u1",
                TokensDelta = -3,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.UsageDebit,
                ReferenceId = "usage:old",
                CreatedAt = now.AddDays(-20),
            },
            new AiCreditLedgerEntry
            {
                Id = "new-grant",
                UserId = "u1",
                TokensDelta = 5,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Purchase,
                ReferenceId = "addon:new",
                CreatedAt = now,
            });
        await db.SaveChangesAsync();

        var balance = await credits.GetBalanceAsync("u1", default);

        Assert.Equal(5, balance.TokensAvailable);
        Assert.Equal(3, balance.TokensConsumedLifetime);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SweepExpired_ExpiresOnlyUnusedGrantRemainder()
    {
        var (db, credits) = Build();
        var now = DateTimeOffset.UtcNow;
        db.AiCreditLedger.AddRange(
            new AiCreditLedgerEntry
            {
                Id = "grant-used-before-expiry",
                UserId = "u1",
                TokensDelta = 5,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.Promo,
                ExpiresAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-10),
            },
            new AiCreditLedgerEntry
            {
                Id = "usage-before-expiry",
                UserId = "u1",
                TokensDelta = -2,
                CostDeltaUsd = 0m,
                Source = AiCreditSource.UsageDebit,
                ReferenceId = "usage:before-expiry",
                CreatedAt = now.AddDays(-5),
            });
        await db.SaveChangesAsync();

        var expired = await credits.SweepExpiredAsync(now, default);

        Assert.Equal(1, expired);
        var expiration = await db.AiCreditLedger.SingleAsync(entry => entry.Source == AiCreditSource.Expiration);
        Assert.Equal(-3, expiration.TokensDelta);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SweepExpired_ZeroesOutExpiredGrants()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 500, 0m, AiCreditSource.Promo, "trial",
            null, DateTimeOffset.UtcNow.AddDays(-1), null, default);
        await credits.GrantAsync("u1", 200, 0m, AiCreditSource.PlanRenewal, "Apr",
            null, DateTimeOffset.UtcNow.AddDays(30), null, default);

        // Balance filter already excludes past-expiry grants even before sweep.
        var beforeSweep = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(200, beforeSweep.TokensAvailable);

        var expired = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(1, expired);

        // After sweep, the ExpiredByEntryId flag is set, so subsequent sweeps
        // won't re-run against this grant (idempotent).
        var afterSweep = await credits.GetBalanceAsync("u1", default);
        Assert.Equal(200, afterSweep.TokensAvailable);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SweepExpired_IsIdempotent()
    {
        var (db, credits) = Build();
        await credits.GrantAsync("u1", 100, 0m, AiCreditSource.Promo, null, null,
            DateTimeOffset.UtcNow.AddDays(-1), null, default);
        var first = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        var second = await credits.SweepExpiredAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Grant_RejectsNonPositive()
    {
        var (db, credits) = Build();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            credits.GrantAsync("u1", 0, 0m, AiCreditSource.Promo, null, null, null, null, default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RenewalWorker_UsesMappedQuotaPlanForSeededBillingCodes()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IAiCreditService, AiCreditService>();
        services.AddScoped<IEffectiveEntitlementResolver, EffectiveEntitlementResolver>();
        await using var provider = services.BuildServiceProvider();

        var now = DateTimeOffset.UtcNow;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.AiQuotaPlans.AddRange(
                new AiQuotaPlan
                {
                    Id = "quota-free",
                    Code = "free",
                    Name = "Free",
                    MonthlyTokenCap = 50,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new AiQuotaPlan
                {
                    Id = "quota-pro",
                    Code = "pro",
                    Name = "Pro",
                    MonthlyTokenCap = 1_000_000,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            db.BillingPlans.Add(new BillingPlan
            {
                Id = "plan-premium-monthly",
                Code = "premium-monthly",
                Name = "Premium Monthly",
                EntitlementsJson = "{}",
            });
            var longUserId = "auth_learner_" + new string('a', 32);
            db.Subscriptions.Add(new Subscription
            {
                Id = "sub-premium",
                UserId = longUserId,
                PlanId = "plan-premium-monthly",
                Status = SubscriptionStatus.Active,
                StartedAt = now.AddMonths(-1),
                ChangedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var worker = new AiCreditRenewalWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiCreditRenewalWorker>.Instance);

        var result = await worker.RunOnceAsync(default);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var entry = await db.AiCreditLedger.SingleAsync();
            Assert.Equal((1, 0), result);
            Assert.Equal("auth_learner_" + new string('a', 32), entry.UserId);
            Assert.Equal(1_000_000, entry.TokensDelta);
            Assert.Contains("pro", entry.Description, StringComparison.OrdinalIgnoreCase);
            Assert.True(entry.ReferenceId?.Length > 64);
        }
    }

    [Fact]
    public void AiCreditLedger_ReferenceId_AllowsLongRenewalKeys()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var reference = entity!.FindProperty(nameof(AiCreditLedgerEntry.ReferenceId));
        Assert.NotNull(reference);
        Assert.Equal(128, reference!.GetMaxLength());
        db.Dispose();
    }

    [Fact]
    public void AiCreditLedger_PlanRenewalReference_IsUniqueInModel()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i => i.GetDatabaseName() == "UX_AiCreditLedger_PlanRenewal_ReferenceId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ReferenceId\" IS NOT NULL AND \"Source\" = 0", index.GetFilter());
        Assert.Equal(new[] { nameof(AiCreditLedgerEntry.ReferenceId) }, index.Properties.Select(p => p.Name).ToArray());
        db.Dispose();
    }

    [Fact]
    public void AiCreditLedger_UsageDebitReference_IsUniqueInModel()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i => i.GetDatabaseName() == "UX_AiCreditLedger_UsageDebit_ReferenceId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ReferenceId\" IS NOT NULL AND \"Source\" = 4", index.GetFilter());
        Assert.Equal(new[] { nameof(AiCreditLedgerEntry.ReferenceId), nameof(AiCreditLedgerEntry.Source) }, index.Properties.Select(p => p.Name).ToArray());
        db.Dispose();
    }

    [Fact]
    public void AiCreditLedger_PurchaseReference_IsUniqueInModel()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i => i.GetDatabaseName() == "UX_AiCreditLedger_Purchase_ReferenceId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ReferenceId\" IS NOT NULL AND \"Source\" = 2", index.GetFilter());
        Assert.Equal(new[] { nameof(AiCreditLedgerEntry.UserId), nameof(AiCreditLedgerEntry.ReferenceId), nameof(AiCreditLedgerEntry.Source) }, index.Properties.Select(p => p.Name).ToArray());
        db.Dispose();
    }

    [Fact]
    public void AiCreditLedger_ExpirationReference_IsUniqueInModel()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i => i.GetDatabaseName() == "UX_AiCreditLedger_Expiration_ReferenceId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ReferenceId\" IS NOT NULL AND \"Source\" = 5", index.GetFilter());
        Assert.Equal(new[] { nameof(AiCreditLedgerEntry.Source), nameof(AiCreditLedgerEntry.ReferenceId) }, index.Properties.Select(p => p.Name).ToArray());
        db.Dispose();
    }

    [Fact]
    public void AiCreditLedger_RefundAdjustmentReference_IsUniqueInModel()
    {
        var (db, _) = Build();
        var entity = db.Model.FindEntityType(typeof(AiCreditLedgerEntry));

        Assert.NotNull(entity);
        var index = entity!.GetIndexes().Single(i => i.GetDatabaseName() == "UX_AiCreditLedger_RefundAdjustment_ReferenceId");
        Assert.True(index.IsUnique);
        Assert.Equal("\"ReferenceId\" IS NOT NULL AND \"Source\" = 3 AND (\"ReferenceId\" LIKE 'addon-refund:%' OR \"ReferenceId\" LIKE 'plan-refund:%')", index.GetFilter());
        Assert.Equal(new[] { nameof(AiCreditLedgerEntry.Source), nameof(AiCreditLedgerEntry.UserId), nameof(AiCreditLedgerEntry.ReferenceId) }, index.Properties.Select(p => p.Name).ToArray());
        db.Dispose();
    }

    [Fact]
    public async Task AiCreditLedger_DuplicatePlanRenewalReference_IsRejectedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new LearnerDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.PlanRenewal, "renewal:u1:month:2026-04"));
            await db.SaveChangesAsync();

            db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.PlanRenewal, "renewal:u1:month:2026-04"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task AiCreditLedger_DuplicateNonRenewalReference_IsAllowedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.Promo, "promo:welcome"));
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.Promo, "promo:welcome"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.AiCreditLedger.CountAsync());
    }

    [Fact]
    public async Task AiCreditLedger_DuplicateUsageDebitReference_IsRejectedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.UsageDebit, "usage:usage-row-1"));
        await db.SaveChangesAsync();

        db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.UsageDebit, "usage:usage-row-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AiCreditLedger_DuplicatePurchaseReference_IsRejectedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.Purchase, "addon:quote-1:pkg_quick_check"));
        await db.SaveChangesAsync();

        db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.Purchase, "addon:quote-1:pkg_quick_check"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AiCreditLedger_DuplicateExpirationReference_IsRejectedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.Expiration, "grant-1"));
        await db.SaveChangesAsync();

        db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.Expiration, "grant-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AiCreditLedger_DuplicateRefundAdjustmentReference_IsRejectedByDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.AiCreditLedger.Add(CreateLedgerEntry("entry-1", AiCreditSource.AdminAdjustment, "addon-refund:quote-1:pkg_quick_check"));
        await db.SaveChangesAsync();

        db.AiCreditLedger.Add(CreateLedgerEntry("entry-2", AiCreditSource.AdminAdjustment, "addon-refund:quote-1:pkg_quick_check"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static AiCreditLedgerEntry CreateLedgerEntry(string id, AiCreditSource source, string? referenceId)
        => new()
        {
            Id = id,
            UserId = "u1",
            TokensDelta = source is AiCreditSource.UsageDebit or AiCreditSource.Expiration or AiCreditSource.AdminAdjustment ? -1 : 100,
            CostDeltaUsd = 0m,
            Source = source,
            Description = source.ToString(),
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
