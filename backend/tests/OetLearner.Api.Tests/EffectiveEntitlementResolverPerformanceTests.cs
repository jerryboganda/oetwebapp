using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

public sealed class EffectiveEntitlementResolverPerformanceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly CommandCounter commands = new();
    private DbContextOptions<LearnerDbContext> options = default!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        commands.Reset();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task ResolveAsync_GoldenOutput_IsBatchedMemoizedAndInvalidatedAfterSave()
    {
        var now = DateTimeOffset.UtcNow;
        var nearExpiry = now.AddDays(30);
        var farExpiry = now.AddDays(60);

        await using var db = new LearnerDbContext(options);
        db.BillingPlans.AddRange(
            new BillingPlan
            {
                Id = "perf-plan-a",
                Code = "premium-monthly",
                Name = "Primary course",
                DashboardModulesJson = "[\"Recalls\",\"MaterialsLibrary\"]",
                WritingAddonsEnabled = true,
                SpeakingPracticeAccessEnabled = false,
                ProductCategory = "FULL COURSE",
            },
            new BillingPlan
            {
                Id = "perf-plan-b",
                Code = "portfolio-b",
                Name = "Second course",
                DashboardModulesJson = "[\"VideoLibrary\",\"Mocks\"]",
                SpeakingAddonsEnabled = true,
                SpeakingPracticeAccessEnabled = true,
                TutorBookDiscountEnabled = true,
                ProductCategory = "BUNDLE",
            },
            new BillingPlan
            {
                Id = "perf-plan-expired",
                Code = "expired-course",
                Name = "Expired course",
                DashboardModulesJson = "[\"Listening\"]",
                WritingAddonsEnabled = true,
            },
            new BillingPlan
            {
                Id = "perf-plan-dangling",
                Code = "dangling-version",
                Name = "Dangling version",
                DashboardModulesJson = "[\"Writing\"]",
            },
            new BillingPlan
            {
                Id = "perf-plan-tutor",
                Code = "tutor-book",
                Name = "Tutor Book",
                DashboardModulesJson = "[\"TutorBook\",\"AudioScripts\",\"Updates\"]",
            });
        db.BillingPlanVersions.AddRange(
            Version("perf-version-a", "perf-plan-a", "premium-monthly"),
            Version("perf-version-b", "perf-plan-b", "portfolio-b"),
            Version("perf-version-expired", "perf-plan-expired", "expired-course"));
        db.Subscriptions.AddRange(
            Subscription(
                "perf-sub-missing",
                "perf-golden-user",
                "perf-plan-missing",
                now,
                now.AddDays(90)),
            Subscription(
                "perf-sub-a",
                "perf-golden-user",
                "perf-plan-a",
                now.AddMinutes(-1),
                nearExpiry,
                "perf-version-a",
                writing: 2,
                speaking: 3,
                ai: 4),
            Subscription(
                "perf-sub-b",
                "perf-golden-user",
                "perf-plan-b",
                now.AddMinutes(-2),
                farExpiry,
                "perf-version-b",
                SubscriptionStatus.Trial,
                writing: 5,
                speaking: 7,
                ai: 11,
                basicEnglish: true),
            Subscription(
                "perf-sub-expired",
                "perf-golden-user",
                "perf-plan-expired",
                now.AddMinutes(-3),
                now.AddDays(-1),
                "perf-version-expired",
                writing: 100,
                speaking: 100,
                ai: 100),
            Subscription(
                "perf-sub-dangling",
                "perf-golden-user",
                "perf-plan-dangling",
                now.AddMinutes(-4),
                now.AddDays(90),
                "perf-version-missing",
                writing: 100,
                speaking: 100,
                ai: 100),
            Subscription(
                "perf-sub-tutor",
                "perf-golden-user",
                "perf-plan-tutor",
                now.AddMinutes(-5),
                null,
                tutorBook: true));
        db.SubscriptionItems.AddRange(
            AddOn("perf-addon-a", "perf-sub-a", "addon-alpha", now.AddDays(-1)),
            AddOn("perf-addon-booster-1", "perf-sub-a", "booster", now.AddDays(-1)),
            AddOn("perf-addon-booster-2", "perf-sub-a", "booster", now.AddHours(-1)),
            AddOn("perf-addon-b", "perf-sub-b", "addon-beta", now.AddDays(-1)),
            AddOn("perf-addon-expired", "perf-sub-a", "expired-addon", now.AddDays(-2), now.AddDays(-1)),
            AddOn("perf-addon-future", "perf-sub-a", "future-addon", now.AddDays(1)),
            AddOn("perf-addon-on-expired-course", "perf-sub-expired", "excluded-addon", now.AddDays(-1)));
        db.AccountFreezeRecords.Add(new AccountFreezeRecord
        {
            Id = "perf-freeze",
            UserId = "perf-golden-user",
            Status = FreezeStatus.Active,
            IsCurrent = true,
            RequestedAt = now,
            UpdatedAt = now,
        });
        db.UserModuleOverrides.AddRange(
            new UserModuleOverride
            {
                Id = "perf-override-disable",
                UserId = "perf-golden-user",
                ModuleKey = "Mocks",
                Enabled = false,
                UpdatedAt = now,
            },
            new UserModuleOverride
            {
                Id = "perf-override-enable",
                UserId = "perf-golden-user",
                ModuleKey = "Reading",
                Enabled = true,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();

        var resolver = new EffectiveEntitlementResolver(db);
        commands.Reset();

        var snapshot = await resolver.ResolveAsync("perf-golden-user", default);
        var initialCommandCount = commands.Count;

        var expectedGolden = new
        {
            UserId = "perf-golden-user",
            HasEligibleSubscription = true,
            IsTrial = false,
            Tier = "paid",
            SubscriptionId = "perf-sub-a",
            SubscriptionStatus = SubscriptionStatus.Active,
            PlanId = "perf-plan-a",
            PlanVersionId = "perf-version-a",
            PlanCode = "premium-monthly",
            AiQuotaPlanCode = "pro",
            AiQuotaPlanCodeSource = "fallback",
            ActiveAddOnCodes = new[] { "addon-alpha", "addon-beta", "booster" },
            IsFrozen = true,
            Trace = new[]
            {
                "subscription.latest.Active",
                "plan.missing",
                "fail_low.plan.missing",
                "freeze.active",
                "tutorbook.permanent",
                "packages.2",
                "module_overrides.2",
            },
            EnabledModules = new[]
            {
                "TutorBook",
                "AudioScripts",
                "Updates",
                "Recalls",
                "MaterialsLibrary",
                "VideoLibrary",
                "Reading",
            },
            DisabledModules = new[] { "Mocks" },
            WritingAddonsEnabled = true,
            SpeakingAddonsEnabled = true,
            SpeakingPracticeAccessEnabled = true,
            TutorBookDiscountEnabled = true,
            WritingAssessmentsRemaining = 7,
            SpeakingSessionsRemaining = 10,
            AiCreditsRemaining = 15,
            TutorBookUnlocked = true,
            BasicEnglishUnlocked = true,
            ExpiresAt = (DateTimeOffset?)farExpiry,
            ProductCategory = "FULL COURSE",
        };
        var actualGolden = new
        {
            snapshot.UserId,
            snapshot.HasEligibleSubscription,
            snapshot.IsTrial,
            snapshot.Tier,
            snapshot.SubscriptionId,
            snapshot.SubscriptionStatus,
            snapshot.PlanId,
            snapshot.PlanVersionId,
            snapshot.PlanCode,
            snapshot.AiQuotaPlanCode,
            snapshot.AiQuotaPlanCodeSource,
            ActiveAddOnCodes = snapshot.ActiveAddOnCodes.ToArray(),
            snapshot.IsFrozen,
            Trace = snapshot.Trace.ToArray(),
            EnabledModules = snapshot.EnabledModules.ToArray(),
            DisabledModules = snapshot.DisabledModules.ToArray(),
            snapshot.WritingAddonsEnabled,
            snapshot.SpeakingAddonsEnabled,
            snapshot.SpeakingPracticeAccessEnabled,
            snapshot.TutorBookDiscountEnabled,
            snapshot.WritingAssessmentsRemaining,
            snapshot.SpeakingSessionsRemaining,
            snapshot.AiCreditsRemaining,
            snapshot.TutorBookUnlocked,
            snapshot.BasicEnglishUnlocked,
            snapshot.ExpiresAt,
            snapshot.ProductCategory,
        };

        Assert.Equal(
            JsonSerializer.Serialize(expectedGolden),
            JsonSerializer.Serialize(actualGolden));
        Assert.Equal(6, initialCommandCount);
        Assert.DoesNotContain(
            commands.CommandTexts,
            sql => sql.Contains("lower(", StringComparison.OrdinalIgnoreCase));

        var memoized = await resolver.ResolveAsync("perf-golden-user", default);
        Assert.Same(snapshot, memoized);
        Assert.Equal(initialCommandCount, commands.Count);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("perf-golden-user", cancellation.Token));
        Assert.Equal(initialCommandCount, commands.Count);

        var primary = await db.Subscriptions.SingleAsync(item => item.Id == "perf-sub-a");
        primary.WritingAssessmentsRemaining = 9;
        await db.SaveChangesAsync();
        commands.Reset();

        var afterMutation = await resolver.ResolveAsync("perf-golden-user", default);

        Assert.Equal(14, afterMutation.WritingAssessmentsRemaining);
        Assert.Equal(6, commands.Count);
    }

    [Fact]
    public async Task ResolveAsync_CommandCount_IsIndependentOfSubscriptionCount()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var seedDb = new LearnerDbContext(options))
        {
            SeedPackage(seedDb, "perf-single", 0, now);
            for (var index = 0; index < 16; index++)
            {
                SeedPackage(seedDb, "perf-many", index, now);
            }

            await seedDb.SaveChangesAsync();
        }

        var singleCount = await ResolveAndCountCommandsAsync("perf-single");
        var manyCount = await ResolveAndCountCommandsAsync("perf-many");

        Assert.Equal(singleCount, manyCount);
        Assert.Equal(5, manyCount);
    }

    [Fact]
    public void LegacyPlanFallback_PostgresTranslationUsesILikeWithoutLower()
    {
        var predicateFactory = typeof(EffectiveEntitlementResolver).GetMethod(
            "BuildCaseInsensitivePlanPredicate",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(predicateFactory);
        var predicate = Assert.IsAssignableFrom<Expression<Func<BillingPlan, bool>>>(
            predicateFactory.Invoke(null, new object[] { new[] { "PREMIUM-MONTHLY" } }));

        var postgresOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation_only;Username=none;Password=none",
                npgsql => npgsql.UseVector())
            .Options;
        using var db = new LearnerDbContext(postgresOptions);

        var sql = db.BillingPlans.AsNoTracking()
            .Where(predicate)
            .ToQueryString();

        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lower(", sql, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> ResolveAndCountCommandsAsync(string userId)
    {
        await using var db = new LearnerDbContext(options);
        var resolver = new EffectiveEntitlementResolver(db);
        commands.Reset();

        var snapshot = await resolver.ResolveAsync(userId, default);

        Assert.True(snapshot.HasEligibleSubscription);
        Assert.Empty(db.ChangeTracker.Entries());
        return commands.Count;
    }

    private static void SeedPackage(
        LearnerDbContext db,
        string userId,
        int index,
        DateTimeOffset now)
    {
        var suffix = $"{userId}-{index}";
        var planId = $"plan-{suffix}";
        var versionId = $"version-{suffix}";
        var subscriptionId = $"subscription-{suffix}";
        db.BillingPlans.Add(new BillingPlan
        {
            Id = planId,
            Code = $"code-{suffix}",
            Name = suffix,
            DashboardModulesJson = "[\"Recalls\"]",
        });
        db.BillingPlanVersions.Add(Version(versionId, planId, $"code-{suffix}"));
        db.Subscriptions.Add(Subscription(
            subscriptionId,
            userId,
            planId,
            now.AddMinutes(-index),
            now.AddDays(30 + index),
            versionId,
            writing: 1,
            speaking: 1,
            ai: 1));
        db.SubscriptionItems.Add(AddOn(
            $"addon-{suffix}",
            subscriptionId,
            $"addon-code-{index}",
            now.AddDays(-1)));
    }

    private static BillingPlanVersion Version(string id, string planId, string code) => new()
    {
        Id = id,
        PlanId = planId,
        Code = code,
        Name = code,
        VersionNumber = 1,
    };

    private static Subscription Subscription(
        string id,
        string userId,
        string planId,
        DateTimeOffset changedAt,
        DateTimeOffset? expiresAt,
        string? planVersionId = null,
        SubscriptionStatus status = SubscriptionStatus.Active,
        int writing = 0,
        int speaking = 0,
        int ai = 0,
        bool tutorBook = false,
        bool basicEnglish = false) => new()
    {
        Id = id,
        UserId = userId,
        PlanId = planId,
        PlanVersionId = planVersionId,
        Status = status,
        StartedAt = changedAt.AddDays(-1),
        ChangedAt = changedAt,
        ExpiresAt = expiresAt,
        WritingAssessmentsRemaining = writing,
        SpeakingSessionsRemaining = speaking,
        AiCreditsRemaining = ai,
        TutorBookUnlocked = tutorBook,
        BasicEnglishUnlocked = basicEnglish,
    };

    private static SubscriptionItem AddOn(
        string id,
        string subscriptionId,
        string code,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt = null) => new()
    {
        Id = id,
        SubscriptionId = subscriptionId,
        ItemCode = code,
        Status = SubscriptionItemStatus.Active,
        StartsAt = startsAt,
        EndsAt = endsAt,
        CreatedAt = startsAt,
        UpdatedAt = startsAt,
    };

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public List<string> CommandTexts { get; } = [];

        public void Reset()
        {
            Count = 0;
            CommandTexts.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
