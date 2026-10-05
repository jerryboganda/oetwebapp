using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Caching;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Caching;

/// <summary>
/// The three consumers of <see cref="UserStateCache"/> behind the request path: the entitlement
/// resolver, the learner freeze-status DTO and the learner write gate. "Tracked" contexts carry
/// the invalidation interceptor (a write made inside this process); "other process" contexts do
/// not (the idle blue/green slot, the ai-worker, a bulk SQL write), so for those only the TTL
/// bounds staleness. The cache clock is injected, so nothing sleeps.
/// </summary>
public sealed class UserStateCacheConsumerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset SeedTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CommandCounter _commands = new();
    private MutableTimeProvider _clock = default!;
    private UserStateCache _cache = default!;
    private DbContextOptions<LearnerDbContext> _tracked = default!;
    private DbContextOptions<LearnerDbContext> _otherProcess = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using (var schema = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options))
        {
            await schema.Database.EnsureCreatedAsync();
        }

        // Created only now, after the (slow) schema build, so the cache clock starts at
        // "right now" and expiry instants derived from it are exact.
        _clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        _cache = new UserStateCache(Options.Create(new UserStateCacheOptions()), _clock);
        _tracked = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new UserStateInvalidationInterceptor(_cache), _commands)
            .Options;
        _otherProcess = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ── Entitlement resolver ────────────────────────────────────────────────

    private async Task SeedSubscriptionAsync(string userId, DateTimeOffset? expiresAt = null, int aiCredits = 0)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = new LearnerDbContext(_tracked);
        db.BillingPlans.Add(new BillingPlan
        {
            Id = $"plan-{userId}",
            Code = $"plan-{userId}",
            Name = "Cache test plan",
            DashboardModulesJson = "[\"Reading\"]",
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{userId}",
            UserId = userId,
            PlanId = $"plan-{userId}",
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now.AddDays(-1),
            ExpiresAt = expiresAt,
            AiCreditsRemaining = aiCredits,
        });
        await db.SaveChangesAsync();
    }

    private async Task<EffectiveEntitlementSnapshot> ResolveInNewRequestAsync(string userId)
    {
        await using var db = new LearnerDbContext(_tracked);
        return await new EffectiveEntitlementResolver(db, null, _cache).ResolveAsync(userId, CancellationToken.None);
    }

    private async Task SetAiCreditsAsync(DbContextOptions<LearnerDbContext> options, string userId, int credits)
    {
        await using var db = new LearnerDbContext(options);
        var subscription = await db.Subscriptions.SingleAsync(s => s.Id == $"sub-{userId}");
        subscription.AiCreditsRemaining = credits;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Entitlement_snapshot_is_shared_across_requests_and_a_tracked_save_evicts_it()
    {
        await SeedSubscriptionAsync("user-ent-1", aiCredits: 5);

        var first = await ResolveInNewRequestAsync("user-ent-1");
        Assert.True(first.HasEligibleSubscription);
        Assert.Equal(5, first.AiCreditsRemaining);

        _commands.Reset();
        var second = await ResolveInNewRequestAsync("user-ent-1");
        Assert.Same(first, second);
        Assert.Equal(0, _commands.Count);

        await SetAiCreditsAsync(_tracked, "user-ent-1", 42);

        var third = await ResolveInNewRequestAsync("user-ent-1");
        Assert.Equal(42, third.AiCreditsRemaining);
    }

    [Fact]
    public async Task A_write_made_by_another_process_is_visible_after_at_most_the_ttl()
    {
        await SeedSubscriptionAsync("user-ent-2", aiCredits: 5);
        await ResolveInNewRequestAsync("user-ent-2");

        // No interceptor on this context: the cache is not told.
        await SetAiCreditsAsync(_otherProcess, "user-ent-2", 9);

        var stale = await ResolveInNewRequestAsync("user-ent-2");
        Assert.Equal(5, stale.AiCreditsRemaining);

        _clock.Advance(TimeSpan.FromSeconds(16));

        var fresh = await ResolveInNewRequestAsync("user-ent-2");
        Assert.Equal(9, fresh.AiCreditsRemaining);
    }

    [Fact]
    public async Task Entitlement_snapshot_is_never_served_past_the_subscription_expiry()
    {
        // Derived from the cache clock (which also tracks "now" in real time here), so the
        // expiry is exactly 5 cache-seconds away.
        await SeedSubscriptionAsync("user-ent-3", expiresAt: _clock.GetUtcNow().AddSeconds(5));
        var first = await ResolveInNewRequestAsync("user-ent-3");
        Assert.True(first.HasEligibleSubscription);

        _clock.Advance(TimeSpan.FromSeconds(3));
        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-3");
        Assert.Equal(0, _commands.Count); // still inside both the TTL and the expiry

        _clock.Advance(TimeSpan.FromSeconds(3)); // 6 s: past the expiry, well inside the 15 s TTL
        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-3");
        Assert.True(_commands.Count > 0, "The expiry instant must end the cached entry before the TTL does.");
    }

    [Fact]
    public async Task Entitlement_snapshot_is_never_served_past_a_scheduled_add_on_item_start()
    {
        await SeedSubscriptionAsync("user-ent-3b");
        await using (var db = new LearnerDbContext(_tracked))
        {
            var now = DateTimeOffset.UtcNow;
            db.SubscriptionItems.Add(new SubscriptionItem
            {
                Id = "item-ent-3b",
                SubscriptionId = "sub-user-ent-3b",
                ItemType = "addon",
                ItemCode = "scheduled-addon",
                Quantity = 1,
                Status = SubscriptionItemStatus.Active,
                // Derived from the cache clock, so the start is exactly 5 cache-seconds away.
                StartsAt = _clock.GetUtcNow().AddSeconds(5),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var first = await ResolveInNewRequestAsync("user-ent-3b");
        Assert.DoesNotContain("scheduled-addon", first.ActiveAddOnCodes);

        _clock.Advance(TimeSpan.FromSeconds(3));
        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-3b");
        Assert.Equal(0, _commands.Count); // still inside both the TTL and the add-on start

        _clock.Advance(TimeSpan.FromSeconds(3)); // 6 s: past the add-on start, well inside the 15 s TTL
        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-3b");
        Assert.True(_commands.Count > 0, "A scheduled add-on start must end the cached entry before the TTL does.");
    }

    [Fact]
    public async Task Naming_a_user_in_Invalidate_drops_the_shared_entry_too()
    {
        await SeedSubscriptionAsync("user-ent-4");
        await ResolveInNewRequestAsync("user-ent-4");

        await using (var db = new LearnerDbContext(_tracked))
        {
            new EffectiveEntitlementResolver(db, null, _cache).Invalidate("user-ent-4");
        }

        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-4");
        Assert.True(_commands.Count > 0);
    }

    [Fact]
    public async Task The_parameterless_Invalidate_used_by_the_tracker_hooks_does_not_flush_the_shared_cache()
    {
        await SeedSubscriptionAsync("user-ent-5");
        await ResolveInNewRequestAsync("user-ent-5");

        await using (var db = new LearnerDbContext(_tracked))
        {
            new EffectiveEntitlementResolver(db, null, _cache).Invalidate();
        }

        _commands.Reset();
        await ResolveInNewRequestAsync("user-ent-5");
        Assert.Equal(0, _commands.Count);
    }

    [Fact]
    public async Task Entitlement_snapshot_is_not_cached_when_the_runtime_switch_is_off()
    {
        await SeedSubscriptionAsync("user-ent-6");
        _cache.ApplyRuntimeSwitch(false);

        var first = await ResolveInNewRequestAsync("user-ent-6");
        _commands.Reset();
        var second = await ResolveInNewRequestAsync("user-ent-6");

        Assert.NotSame(first, second);
        Assert.True(_commands.Count > 0);
    }

    [Fact]
    public async Task A_resolver_built_without_the_shared_cache_behaves_exactly_as_before()
    {
        await SeedSubscriptionAsync("user-ent-7");

        await using var first = new LearnerDbContext(_tracked);
        var one = await new EffectiveEntitlementResolver(first).ResolveAsync("user-ent-7", CancellationToken.None);
        await using var second = new LearnerDbContext(_tracked);
        var two = await new EffectiveEntitlementResolver(second).ResolveAsync("user-ent-7", CancellationToken.None);

        Assert.NotSame(one, two);
        Assert.Equal(0, _cache.Snapshot().Entries);
    }

    // ── Learner freeze status + write gate ──────────────────────────────────

    private static LearnerService NewLearnerService(LearnerDbContext db, UserStateCache? cache)
        => new(db, null!, null!, null!, null!, null!, null!, null!, userStateCache: cache);

    private async Task SeedLearnerAsync(string userId)
    {
        await using var db = new LearnerDbContext(_tracked);
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Cache Learner",
            Email = $"{userId}@example.test",
            Timezone = "Australia/Sydney",
            Locale = "en-AU",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            CreatedAt = SeedTime.AddDays(-30),
            LastActiveAt = SeedTime,
        });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = "medicine",
            TargetExamDate = new DateOnly(2026, 8, 15),
            OverallGoal = "Cache goal",
            WeakSubtestsJson = "[]",
            StudyHoursPerWeek = 10,
            TargetCountry = "Australia",
            TargetOrganization = "AHPRA",
            DraftStateJson = "{}",
            UpdatedAt = SeedTime,
            ExamFamilyCode = "oet",
        });
        db.Settings.Add(new LearnerSettings { Id = Guid.NewGuid(), UserId = userId });
        db.Wallets.Add(new Wallet
        {
            Id = $"wallet-{userId}",
            UserId = userId,
            CreditBalance = 0,
            LastUpdatedAt = SeedTime,
        });
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> GetMeInNewRequestAsync(string userId)
    {
        await using var db = new LearnerDbContext(_tracked);
        return JsonSerializer.SerializeToElement(
            await NewLearnerService(db, _cache).GetMeAsync(userId, CancellationToken.None));
    }

    private static AccountFreezeRecord ActiveFreeze(string id, string userId) => new()
    {
        Id = id,
        UserId = userId,
        Status = FreezeStatus.Active,
        IsCurrent = true,
        RequestedAt = SeedTime,
        UpdatedAt = SeedTime,
    };

    [Fact]
    public async Task Freeze_status_is_served_from_the_cache_and_a_tracked_freeze_evicts_it()
    {
        await SeedLearnerAsync("user-frz-1");

        _commands.Reset();
        var first = await GetMeInNewRequestAsync("user-frz-1");
        var coldCommands = _commands.Count;
        Assert.Equal(JsonValueKind.Null, first.GetProperty("freeze").GetProperty("currentFreeze").ValueKind);

        _commands.Reset();
        var second = await GetMeInNewRequestAsync("user-frz-1");
        // Only the profile read remains: the freeze policy / record / entitlement / history reads are cached.
        Assert.Equal(1, _commands.Count);
        Assert.True(coldCommands > _commands.Count);
        Assert.Equal(first.GetProperty("freeze").GetRawText(), second.GetProperty("freeze").GetRawText());

        await using (var writer = new LearnerDbContext(_tracked))
        {
            writer.AccountFreezeRecords.Add(ActiveFreeze("frz-user-frz-1", "user-frz-1"));
            await writer.SaveChangesAsync();
        }

        var third = await GetMeInNewRequestAsync("user-frz-1");
        Assert.NotEqual(JsonValueKind.Null, third.GetProperty("freeze").GetProperty("currentFreeze").ValueKind);
    }

    [Fact]
    public async Task Freeze_status_of_one_learner_is_never_served_to_another()
    {
        await SeedLearnerAsync("user-frz-a");
        await SeedLearnerAsync("user-frz-b");
        await using (var writer = new LearnerDbContext(_tracked))
        {
            writer.AccountFreezeRecords.Add(ActiveFreeze("frz-user-frz-a", "user-frz-a"));
            await writer.SaveChangesAsync();
        }

        var frozen = await GetMeInNewRequestAsync("user-frz-a");
        var other = await GetMeInNewRequestAsync("user-frz-b");

        Assert.NotEqual(JsonValueKind.Null, frozen.GetProperty("freeze").GetProperty("currentFreeze").ValueKind);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("freeze").GetProperty("currentFreeze").ValueKind);
        Assert.Equal("user-frz-b", other.GetProperty("freeze").GetProperty("userId").GetString());
    }

    [Fact]
    public async Task Freeze_status_is_read_directly_when_no_cache_is_supplied()
    {
        await SeedLearnerAsync("user-frz-2");

        await using (var db = new LearnerDbContext(_tracked))
        {
            await NewLearnerService(db, null).GetMeAsync("user-frz-2", CancellationToken.None);
        }

        _commands.Reset();
        await using (var db = new LearnerDbContext(_tracked))
        {
            await NewLearnerService(db, null).GetMeAsync("user-frz-2", CancellationToken.None);
        }

        Assert.True(_commands.Count > 1);
    }

    private async Task<string> StartOnboardingAsync(string userId)
    {
        await using var db = new LearnerDbContext(_tracked);
        try
        {
            await NewLearnerService(db, _cache).StartOnboardingAsync(userId, CancellationToken.None);
            return "allowed";
        }
        catch (ApiException ex)
        {
            return ex.ErrorCode;
        }
    }

    [Fact]
    public async Task The_write_gate_sees_an_in_process_freeze_immediately()
    {
        await SeedLearnerAsync("user-gate-1");
        Assert.Equal("allowed", await StartOnboardingAsync("user-gate-1")); // fills "no freeze"

        await using (var writer = new LearnerDbContext(_tracked))
        {
            writer.AccountFreezeRecords.Add(ActiveFreeze("frz-user-gate-1", "user-gate-1"));
            await writer.SaveChangesAsync();
        }

        Assert.Equal("account_frozen", await StartOnboardingAsync("user-gate-1"));
    }

    [Fact]
    public async Task The_write_gate_follows_another_process_unfreezing_within_the_ttl()
    {
        await SeedLearnerAsync("user-gate-2");
        await using (var writer = new LearnerDbContext(_tracked))
        {
            writer.AccountFreezeRecords.Add(ActiveFreeze("frz-user-gate-2", "user-gate-2"));
            await writer.SaveChangesAsync();
        }

        Assert.Equal("account_frozen", await StartOnboardingAsync("user-gate-2")); // fills the active record

        await using (var otherProcess = new LearnerDbContext(_otherProcess))
        {
            var record = await otherProcess.AccountFreezeRecords.SingleAsync(r => r.Id == "frz-user-gate-2");
            record.Status = FreezeStatus.Cancelled;
            record.IsCurrent = false;
            await otherProcess.SaveChangesAsync();
        }

        Assert.Equal("account_frozen", await StartOnboardingAsync("user-gate-2")); // still within the TTL
        _clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal("allowed", await StartOnboardingAsync("user-gate-2"));
    }

    [Fact]
    public async Task The_write_gate_reads_every_time_when_the_runtime_switch_is_off()
    {
        await SeedLearnerAsync("user-gate-3");
        Assert.Equal("allowed", await StartOnboardingAsync("user-gate-3"));
        _cache.ApplyRuntimeSwitch(false);

        await using (var otherProcess = new LearnerDbContext(_otherProcess))
        {
            otherProcess.AccountFreezeRecords.Add(ActiveFreeze("frz-user-gate-3", "user-gate-3"));
            await otherProcess.SaveChangesAsync();
        }

        Assert.Equal("account_frozen", await StartOnboardingAsync("user-gate-3"));
    }

    // ── Kill-switch worker ──────────────────────────────────────────────────

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public WorkerHarness()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var services = new ServiceCollection();
            services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(dbName));
            _provider = services.BuildServiceProvider();
            Cache = new UserStateCache(Options.Create(new UserStateCacheOptions()));
            Worker = new UserStateCacheSwitchWorker(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Cache,
                NullLogger<UserStateCacheSwitchWorker>.Instance);
        }

        public UserStateCache Cache { get; }

        public UserStateCacheSwitchWorker Worker { get; }

        public async Task SetFlagAsync(string id, bool enabled, DateTimeOffset updatedAt)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.FeatureFlags.Add(new FeatureFlag
            {
                Id = id,
                Name = "User state cache",
                Key = UserStateCache.FeatureFlagKey,
                Enabled = enabled,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
            });
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    [Fact]
    public async Task Switch_worker_defaults_to_on_when_no_flag_row_exists()
    {
        await using var harness = new WorkerHarness();
        harness.Cache.ApplyRuntimeSwitch(false);

        await harness.Worker.RefreshOnceAsync(CancellationToken.None);

        Assert.True(harness.Cache.IsEnabled);
    }

    [Fact]
    public async Task Switch_worker_turns_the_cache_off_and_on_from_the_feature_flag()
    {
        await using var harness = new WorkerHarness();
        await harness.SetFlagAsync("flag-1", enabled: false, DateTimeOffset.UtcNow);

        await harness.Worker.RefreshOnceAsync(CancellationToken.None);
        Assert.False(harness.Cache.IsEnabled);

        // A newer row for the same key wins (the Admin UI can leave duplicates behind).
        await harness.SetFlagAsync("flag-2", enabled: true, DateTimeOffset.UtcNow.AddMinutes(1));
        await harness.Worker.RefreshOnceAsync(CancellationToken.None);
        Assert.True(harness.Cache.IsEnabled);
    }

    [Fact]
    public async Task Switch_worker_drops_the_cached_entries_when_it_turns_the_cache_off()
    {
        await using var harness = new WorkerHarness();
        harness.Cache.Set(UserStateCacheKinds.JwtAccount, "acct:1", "-", new object(), harness.Cache.BeginRead("acct:1"), notAfter: null);
        await harness.SetFlagAsync("flag-1", enabled: false, DateTimeOffset.UtcNow);

        await harness.Worker.RefreshOnceAsync(CancellationToken.None);

        Assert.Equal(0, harness.Cache.Snapshot().Entries);
    }

    [Fact]
    public async Task Switch_worker_keeps_the_last_value_when_the_flag_cannot_be_read()
    {
        var cache = new UserStateCache(Options.Create(new UserStateCacheOptions()));
        cache.ApplyRuntimeSwitch(false);
        var worker = new UserStateCacheSwitchWorker(
            new ThrowingScopeFactory(),
            cache,
            NullLogger<UserStateCacheSwitchWorker>.Instance);

        await worker.RefreshOnceAsync(CancellationToken.None);

        Assert.False(cache.IsEnabled);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("database unavailable");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
