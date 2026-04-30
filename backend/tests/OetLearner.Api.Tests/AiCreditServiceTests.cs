using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

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

    private static AiCreditLedgerEntry CreateLedgerEntry(string id, AiCreditSource source, string? referenceId)
        => new()
        {
            Id = id,
            UserId = "u1",
            TokensDelta = 100,
            CostDeltaUsd = 0m,
            Source = source,
            Description = source.ToString(),
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
