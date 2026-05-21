using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;

namespace OetLearner.Api.Tests;

/// <summary>
/// Focused tests for <see cref="AiAccountQuotaResetWorker"/>. Verifies the
/// set-based reset only touches rows whose <c>PeriodMonthKey</c> differs
/// from the current UTC month, and that idempotent re-runs do nothing.
/// </summary>
public sealed class AiAccountQuotaResetWorkerTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dp = new();

    public AiAccountQuotaResetWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RunOnce_ZeroesCounters_OnlyForRowsFromPriorMonth()
    {
        await SeedProviderAsync();
        await SeedAccountAsync(label: "old-april", periodKey: "2026-04", used: 750);
        await SeedAccountAsync(label: "old-march", periodKey: "2026-03", used: 250);
        await SeedAccountAsync(label: "current-may", periodKey: "2026-05", used: 42);

        var worker = NewWorker(DateTimeOffset.Parse("2026-05-08T12:00:00Z"));
        var resetCount = await worker.RunOnceAsync(default);

        Assert.Equal(2, resetCount);

        await using var verify = new LearnerDbContext(_options);
        var rows = await verify.AiProviderAccounts.AsNoTracking().ToListAsync();
        Assert.All(rows, r => Assert.Equal("2026-05", r.PeriodMonthKey));

        var april = rows.Single(r => r.Label == "old-april");
        var march = rows.Single(r => r.Label == "old-march");
        var may = rows.Single(r => r.Label == "current-may");
        Assert.Equal(0, april.RequestsUsedThisMonth);
        Assert.Equal(0, march.RequestsUsedThisMonth);
        Assert.Equal(42, may.RequestsUsedThisMonth);
    }

    [Fact]
    public async Task RunOnce_SecondInvocation_DoesNothing()
    {
        await SeedProviderAsync();
        await SeedAccountAsync(label: "stale", periodKey: "2026-04", used: 999);

        var worker = NewWorker(DateTimeOffset.Parse("2026-05-08T12:00:00Z"));
        var first = await worker.RunOnceAsync(default);
        var second = await worker.RunOnceAsync(default);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task RunOnce_EmptyTable_ReturnsZero()
    {
        var worker = NewWorker(DateTimeOffset.Parse("2026-05-08T12:00:00Z"));
        var resetCount = await worker.RunOnceAsync(default);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task RunOnce_InMemoryProvider_UsesTrackedFallback()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");

        await using (var seed = new LearnerDbContext(options))
        {
            seed.AiProviders.Add(new AiProvider
            {
                Id = "provider-copilot",
                Code = "copilot",
                Name = "GitHub Copilot",
                Dialect = AiProviderDialect.Copilot,
                BaseUrl = "https://models.github.ai/inference",
                EncryptedApiKey = string.Empty,
                ApiKeyHint = string.Empty,
                DefaultModel = "openai/gpt-4o-mini",
                IsActive = true,
                FailoverPriority = 100,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            seed.AiProviderAccounts.AddRange(
                new AiProviderAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProviderId = "provider-copilot",
                    Label = "stale",
                    EncryptedApiKey = protector.Protect("pat-stale"),
                    ApiKeyHint = "...test",
                    MonthlyRequestCap = 1000,
                    RequestsUsedThisMonth = 7,
                    Priority = 0,
                    IsActive = true,
                    PeriodMonthKey = "2026-04",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                new AiProviderAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProviderId = "provider-copilot",
                    Label = "current",
                    EncryptedApiKey = protector.Protect("pat-current"),
                    ApiKeyHint = "...test",
                    MonthlyRequestCap = 1000,
                    RequestsUsedThisMonth = 3,
                    Priority = 1,
                    IsActive = true,
                    PeriodMonthKey = "2026-05",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var worker = NewWorker(DateTimeOffset.Parse("2026-05-08T12:00:00Z"), options);
        var resetCount = await worker.RunOnceAsync(default);

        Assert.Equal(1, resetCount);
        await using var verify = new LearnerDbContext(options);
        var stale = await verify.AiProviderAccounts.SingleAsync(a => a.Label == "stale");
        var current = await verify.AiProviderAccounts.SingleAsync(a => a.Label == "current");
        Assert.Equal("2026-05", stale.PeriodMonthKey);
        Assert.Equal(0, stale.RequestsUsedThisMonth);
        Assert.Equal(3, current.RequestsUsedThisMonth);
    }

    private AiAccountQuotaResetWorker NewWorker(DateTimeOffset now)
        => NewWorker(now, _options);

    private static AiAccountQuotaResetWorker NewWorker(DateTimeOffset now, DbContextOptions<LearnerDbContext> options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new LearnerDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new AiAccountQuotaResetWorker(
            scopeFactory,
            new FixedClock(now),
            NullLogger<AiAccountQuotaResetWorker>.Instance);
    }

    private async Task SeedProviderAsync()
    {
        await using var db = new LearnerDbContext(_options);
        if (await db.AiProviders.AnyAsync()) return;
        db.AiProviders.Add(new AiProvider
        {
            Id = "provider-copilot",
            Code = "copilot",
            Name = "GitHub Copilot",
            Dialect = AiProviderDialect.Copilot,
            BaseUrl = "https://models.github.ai/inference",
            EncryptedApiKey = string.Empty,
            ApiKeyHint = string.Empty,
            DefaultModel = "openai/gpt-4o-mini",
            IsActive = true,
            FailoverPriority = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAccountAsync(string label, string periodKey, int used)
    {
        await using var db = new LearnerDbContext(_options);
        var protector = _dp.CreateProtector("AiProvider.PlatformKey.v1");
        db.AiProviderAccounts.Add(new AiProviderAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = "provider-copilot",
            Label = label,
            EncryptedApiKey = protector.Protect("pat-" + label),
            ApiKeyHint = "…test",
            MonthlyRequestCap = 1000,
            RequestsUsedThisMonth = used,
            Priority = 0,
            IsActive = true,
            PeriodMonthKey = periodKey,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
