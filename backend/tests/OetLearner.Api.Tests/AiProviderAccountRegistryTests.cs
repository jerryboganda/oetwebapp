using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Focused tests for <see cref="AiProviderAccountRegistry"/>. Backed by
/// SQLite in-memory so <c>ExecuteUpdateAsync</c> runs against a real
/// relational engine (the EF InMemory provider does not enforce the
/// gating predicates atomically the way a relational engine does).
/// </summary>
public sealed class AiProviderAccountRegistryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;
    private readonly EphemeralDataProtectionProvider _dpProvider;
    private readonly FixedClock _clock;

    public AiProviderAccountRegistryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var seed = new LearnerDbContext(_options))
        {
            seed.Database.EnsureCreated();
        }
        _dpProvider = new EphemeralDataProtectionProvider();
        _clock = new FixedClock(DateTimeOffset.Parse("2026-05-08T12:00:00Z"));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PickAndReserve_HappyPath_ReturnsHighestPriorityAndIncrementsCounter()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "high-pri", apiKey: "pat-A", priority: 0, cap: 100, used: 0);
        await SeedAccountAsync(db, "copilot", label: "low-pri", apiKey: "pat-B", priority: 5, cap: 100, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);

        Assert.NotNull(slot);
        Assert.Equal("high-pri", slot!.Label);
        Assert.Equal("pat-A", slot.ApiKey);
        Assert.Equal(1, slot.RequestsUsedAfterPick);

        var stored = await db.AiProviderAccounts.AsNoTracking()
            .FirstAsync(a => a.Label == "high-pri");
        Assert.Equal(1, stored.RequestsUsedThisMonth);
    }

    [Fact]
    public async Task PickAndReserve_SkipSet_SkipsExhaustedAccountsInSameTurn()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        var idA = await SeedAccountAsync(db, "copilot", label: "A", apiKey: "pat-A", priority: 0, cap: 100, used: 0);
        await SeedAccountAsync(db, "copilot", label: "B", apiKey: "pat-B", priority: 1, cap: 100, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync(
            "copilot",
            skipAccountIds: new[] { idA },
            default);

        Assert.NotNull(slot);
        Assert.Equal("B", slot!.Label);
    }

    [Fact]
    public async Task PickAndReserve_AtCap_RefusesAccount()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "exhausted", apiKey: "pat-X", priority: 0, cap: 10, used: 10);
        await SeedAccountAsync(db, "copilot", label: "fresh", apiKey: "pat-Y", priority: 1, cap: 10, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);

        Assert.NotNull(slot);
        Assert.Equal("fresh", slot!.Label);
    }

    [Fact]
    public async Task PickAndReserve_QuarantineExpired_AllowsAccountAgain()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "back-online", apiKey: "pat-Z", priority: 0,
            cap: 100, used: 0,
            exhaustedUntil: _clock.GetUtcNow().AddMinutes(-1));

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);

        Assert.NotNull(slot);
        Assert.Equal("back-online", slot!.Label);
    }

    [Fact]
    public async Task PickAndReserve_QuarantineActive_SkipsAccount()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "quarantined", apiKey: "pat-Q", priority: 0,
            cap: 100, used: 0,
            exhaustedUntil: _clock.GetUtcNow().AddMinutes(15));

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);

        Assert.Null(slot);
    }

    [Fact]
    public async Task PickAndReserve_Inactive_SkipsAccount()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "off", apiKey: "pat-O", priority: 0,
            cap: 100, used: 0, isActive: false);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);

        Assert.Null(slot);
    }

    [Fact]
    public async Task PickAndReserve_NoProviderRegistered_ReturnsNull()
    {
        await using var db = new LearnerDbContext(_options);
        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var slot = await registry.PickAndReserveAsync("copilot", null, default);
        Assert.Null(slot);
    }

    [Fact]
    public async Task RecordOutcome_RateLimited_SetsExhaustedUntil()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        var id = await SeedAccountAsync(db, "copilot", label: "x", apiKey: "pat-R", priority: 0, cap: 100, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        await registry.RecordOutcomeAsync(id, AiProviderAccountOutcome.RateLimited,
            quarantineFor: TimeSpan.FromMinutes(30), default);

        var stored = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.NotNull(stored.ExhaustedUntil);
        Assert.True(stored.IsActive); // 429 does not deactivate
    }

    [Fact]
    public async Task RecordOutcome_Unauthorized_DeactivatesAccount()
    {
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        var id = await SeedAccountAsync(db, "copilot", label: "x", apiKey: "pat-U", priority: 0, cap: 100, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        await registry.RecordOutcomeAsync(id, AiProviderAccountOutcome.Unauthorized,
            quarantineFor: null, default);

        var stored = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Id == id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task PickAndReserve_RoundTripsCounterDeterministicallyAcrossManyCalls()
    {
        // Sequential picks against a single-account pool exhaust the cap
        // exactly N times then return null, proving the increment is
        // applied (and gating predicate enforced) on every call.
        await using var db = new LearnerDbContext(_options);
        await SeedProviderAsync(db, "copilot");
        await SeedAccountAsync(db, "copilot", label: "only", apiKey: "pat-S", priority: 0, cap: 5, used: 0);

        var registry = new AiProviderAccountRegistry(db, _dpProvider, _clock);
        var picked = 0;
        for (var i = 0; i < 8; i++)
        {
            var slot = await registry.PickAndReserveAsync("copilot", null, default);
            if (slot is null) break;
            picked++;
        }
        Assert.Equal(5, picked);

        var stored = await db.AiProviderAccounts.AsNoTracking().FirstAsync(a => a.Label == "only");
        Assert.Equal(5, stored.RequestsUsedThisMonth);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private async Task SeedProviderAsync(LearnerDbContext db, string code)
    {
        if (await db.AiProviders.AnyAsync(p => p.Code == code)) return;
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            Name = code,
            Dialect = AiProviderDialect.Copilot,
            BaseUrl = "https://models.github.ai/inference",
            EncryptedApiKey = string.Empty,
            ApiKeyHint = string.Empty,
            DefaultModel = "openai/gpt-4o-mini",
            IsActive = true,
            FailoverPriority = 100,
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> SeedAccountAsync(
        LearnerDbContext db,
        string providerCode,
        string label,
        string apiKey,
        int priority,
        int? cap,
        int used,
        bool isActive = true,
        DateTimeOffset? exhaustedUntil = null)
    {
        var providerId = await db.AiProviders.AsNoTracking()
            .Where(p => p.Code == providerCode)
            .Select(p => p.Id)
            .FirstAsync();

        var protector = _dpProvider.CreateProtector("AiProvider.PlatformKey.v1");
        var account = new AiProviderAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            ProviderId = providerId,
            Label = label,
            EncryptedApiKey = protector.Protect(apiKey),
            ApiKeyHint = "…" + apiKey[^Math.Min(4, apiKey.Length)..],
            MonthlyRequestCap = cap,
            RequestsUsedThisMonth = used,
            Priority = priority,
            IsActive = isActive,
            ExhaustedUntil = exhaustedUntil,
            PeriodMonthKey = "2026-05",
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        };
        db.AiProviderAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
