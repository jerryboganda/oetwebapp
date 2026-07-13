using System.Collections;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class SponsorServicePerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private readonly FixedClock _clock = new(Now);
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Dashboard_UsesThreeAggregateProjectionQueriesRegardlessOfCohortSize()
    {
        await SeedSponsorAsync();
        await using (var seed = new LearnerDbContext(_options))
        {
            for (var index = 0; index < 100; index++)
            {
                var learnerId = $"learner-{index:D3}";
                var status = index < 60 ? "Active" : index < 80 ? "Pending" : "Revoked";
                var sponsorship = CreateSponsorship(index, learnerId, status);
                seed.Sponsorships.Add(sponsorship);
                seed.PaymentTransactions.Add(CreateTransaction(index, learnerId, 10m, Now.AddDays(-3)));
            }
            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();
        var result = await CreateService(db).GetDashboardAsync("sponsor", default);

        Assert.Equal("Performance Sponsor", GetProperty<string>(result, "sponsorName"));
        Assert.Equal("Performance Org", GetProperty<string>(result, "organizationName"));
        Assert.Equal(80, GetProperty<int>(result, "learnersSponsored"));
        Assert.Equal(60, GetProperty<int>(result, "activeSponsorships"));
        Assert.Equal(20, GetProperty<int>(result, "pendingSponsorships"));
        Assert.Equal(1_000m, GetProperty<decimal>(result, "totalSpend"));
        Assert.Equal("GBP", GetProperty<string>(result, "currency"));
        Assert.Equal(3, _sql.Commands.Count);
        Assert.Contains(_sql.Commands, command => command.Contains("COUNT("));
        Assert.Contains(_sql.Commands, command => command.Contains("sum(", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Billing_AggregatesAllRowsButProjectsOnlyFiftyInvoicesInFourQueries()
    {
        await SeedSponsorAsync();
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.Sponsorships.Add(CreateSponsorship(0, "learner", "Active"));
            seed.PaymentTransactions.AddRange(Enumerable.Range(0, 120).Select(index =>
                CreateTransaction(index, "learner", 1m, Now.AddMinutes(-index))));
            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();
        var result = await CreateService(db).GetBillingAsync("sponsor", default);

        Assert.Equal(1, GetProperty<int>(result, "totalSponsorships"));
        Assert.Equal(120m, GetProperty<decimal>(result, "totalSpend"));
        Assert.Equal(120m, GetProperty<decimal>(result, "currentMonthSpend"));
        Assert.Equal("GBP", GetProperty<string>(result, "currency"));
        Assert.Equal("monthly", GetProperty<string>(result, "billingCycle"));

        var invoices = GetItems(result, "invoices").Cast<SponsorInvoice>().ToList();
        Assert.Equal(50, invoices.Count);
        Assert.Equal(Now, invoices[0].CreatedAt);
        Assert.Equal(Now.AddMinutes(-49), invoices[^1].CreatedAt);
        Assert.Equal(4, _sql.Commands.Count);
        Assert.Single(_sql.Commands.Where(command =>
            command.Contains("sum(", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private async Task SeedSponsorAsync()
    {
        await using var db = new LearnerDbContext(_options);
        db.SponsorAccounts.Add(new SponsorAccount
        {
            Id = "sponsor-account",
            AuthAccountId = "sponsor",
            Name = "Performance Sponsor",
            Type = "institution",
            ContactEmail = "billing@example.test",
            OrganizationName = "Performance Org",
            Status = "active",
            CreatedAt = Now.AddYears(-1),
        });
        await db.SaveChangesAsync();
    }

    private static Sponsorship CreateSponsorship(int index, string learnerId, string status)
        => new()
        {
            Id = Guid.NewGuid(),
            SponsorUserId = "sponsor",
            LearnerUserId = learnerId,
            LearnerEmail = $"{learnerId}@example.test",
            Status = status,
            CreatedAt = Now.AddMonths(-1),
            RevokedAt = status == "Revoked" ? Now.AddDays(-2) : null,
        };

    private static PaymentTransaction CreateTransaction(
        int index,
        string learnerId,
        decimal amount,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            LearnerUserId = learnerId,
            Gateway = "stripe",
            GatewayTransactionId = $"pi_{learnerId}_{index:D3}",
            TransactionType = "subscription_payment",
            Status = "completed",
            Amount = amount,
            Currency = "GBP",
            ProductType = "plan",
            ProductId = "plan-pro",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    private SponsorService CreateService(LearnerDbContext db)
        => new(db, NullLogger<SponsorService>.Instance, _clock);

    private static T GetProperty<T>(object source, string name)
    {
        var property = source.GetType().GetProperty(name)
            ?? throw new Xunit.Sdk.XunitException($"Missing property '{name}'.");
        return Assert.IsType<T>(property.GetValue(source));
    }

    private static List<object> GetItems(object source, string name)
    {
        var value = source.GetType().GetProperty(name)?.GetValue(source);
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().ToList();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
