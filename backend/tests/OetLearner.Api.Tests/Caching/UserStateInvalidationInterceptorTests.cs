using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Caching;

namespace OetLearner.Api.Tests.Caching;

/// <summary>
/// The invalidation matrix: which committed EF saves evict which cached subject. Every row of
/// the table in docs/ops/user-state-cache.md is one test here.
/// </summary>
public sealed class UserStateInvalidationInterceptorTests : IAsyncLifetime
{
    private const string AuthAccountId = "auth-inv-1";
    private const string LearnerId = "learner-inv-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly UserStateCache _cache = new(Options.Create(new UserStateCacheOptions()));
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new UserStateInvalidationInterceptor(_cache))
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = AuthAccountId,
            Email = "inv@example.test",
            NormalizedEmail = "INV@EXAMPLE.TEST",
            PasswordHash = "not-used",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        db.Users.Add(new LearnerUser
        {
            Id = LearnerId,
            AuthAccountId = AuthAccountId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Invalidation Learner",
            Email = "inv@example.test",
            AccountStatus = "active",
            CreatedAt = Now,
            LastActiveAt = Now,
        });
        db.RefreshTokenRecords.Add(NewToken(Guid.NewGuid(), Guid.NewGuid()));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static RefreshTokenRecord NewToken(Guid id, Guid familyId) => new()
    {
        Id = id,
        ApplicationUserAccountId = AuthAccountId,
        TokenHash = $"hash-{id:N}",
        FamilyId = familyId,
        ExpiresAt = Now.AddDays(30),
        CreatedAt = Now,
    };

    private static string Account => UserStateCacheSubjects.AuthAccount(AuthAccountId);

    private static string Learner => UserStateCacheSubjects.Learner(LearnerId);

    private void Prime(string subject, string kind = UserStateCacheKinds.JwtAccount)
        => _cache.Set(kind, subject, "-", new object(), _cache.BeginRead(subject), notAfter: null);

    private bool IsCached(string subject, string kind = UserStateCacheKinds.JwtAccount)
        => _cache.TryGet(kind, subject, "-", out object? _);

    private LearnerDbContext NewDb() => new(_options);

    [Fact]
    public async Task Suspending_a_learner_evicts_the_account_and_the_learner_subjects()
    {
        Prime(Account);
        Prime(Learner, UserStateCacheKinds.Entitlement);
        await using var db = NewDb();
        var learner = await db.Users.SingleAsync(user => user.Id == LearnerId);

        learner.AccountStatus = "suspended";
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
        Assert.False(IsCached(Learner, UserStateCacheKinds.Entitlement));
    }

    [Fact]
    public async Task Changing_the_learner_access_expiry_evicts_them()
    {
        Prime(Account);
        await using var db = NewDb();
        var learner = await db.Users.SingleAsync(user => user.Id == LearnerId);

        learner.AccessExpiresAt = Now.AddDays(1);
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task Changing_the_active_profession_or_current_plan_evicts_the_learner_entitlement()
    {
        Prime(Learner, UserStateCacheKinds.Entitlement);
        await using var db = NewDb();
        var learner = await db.Users.SingleAsync(user => user.Id == LearnerId);

        learner.ActiveProfessionId = "medicine";
        await db.SaveChangesAsync();

        Assert.False(IsCached(Learner, UserStateCacheKinds.Entitlement));
    }

    [Fact]
    public async Task Ordinary_learner_activity_writes_do_not_evict()
    {
        Prime(Account);
        Prime(Learner, UserStateCacheKinds.Entitlement);
        await using var db = NewDb();
        var learner = await db.Users.SingleAsync(user => user.Id == LearnerId);

        // The streak / activity counters change on almost every practice write.
        learner.LastActiveAt = Now.AddMinutes(5);
        learner.CurrentStreak += 1;
        learner.TotalPracticeMinutes += 10;
        await db.SaveChangesAsync();

        Assert.True(IsCached(Account));
        Assert.True(IsCached(Learner, UserStateCacheKinds.Entitlement));
    }

    [Fact]
    public async Task Deleting_or_changing_the_role_of_an_account_evicts_it()
    {
        Prime(Account);
        await using (var db = NewDb())
        {
            var account = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == AuthAccountId);
            account.Role = ApplicationUserRoles.Expert;
            await db.SaveChangesAsync();
        }

        Assert.False(IsCached(Account));

        Prime(Account);
        await using (var db = NewDb())
        {
            var account = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == AuthAccountId);
            account.DeletedAt = Now;
            await db.SaveChangesAsync();
        }

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task A_password_change_evicts_the_account()
    {
        Prime(Account);
        await using var db = NewDb();
        var account = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == AuthAccountId);

        account.PasswordHash = "new-hash";
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task A_sign_in_that_only_touches_login_counters_does_not_evict()
    {
        Prime(Account);
        await using var db = NewDb();
        var account = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == AuthAccountId);

        account.LastLoginAt = Now;
        account.FailedSignInCount = 0;
        await db.SaveChangesAsync();

        Assert.True(IsCached(Account));
    }

    [Fact]
    public async Task Revoking_a_refresh_token_evicts_the_account()
    {
        Prime(Account);
        await using var db = NewDb();
        var token = await db.RefreshTokenRecords.SingleAsync(t => t.ApplicationUserAccountId == AuthAccountId);

        token.RevokedAt = Now;
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task Logout_all_which_revokes_every_family_evicts_the_account()
    {
        await using (var seed = NewDb())
        {
            seed.RefreshTokenRecords.Add(NewToken(Guid.NewGuid(), Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        Prime(Account);
        await using var db = NewDb();
        foreach (var token in await db.RefreshTokenRecords.Where(t => t.ApplicationUserAccountId == AuthAccountId).ToListAsync())
        {
            token.RevokedAt = Now;
        }

        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task Issuing_a_new_refresh_token_does_not_evict()
    {
        Prime(Account);
        await using var db = NewDb();

        db.RefreshTokenRecords.Add(NewToken(Guid.NewGuid(), Guid.NewGuid()));
        await db.SaveChangesAsync();

        Assert.True(IsCached(Account));
    }

    [Fact]
    public async Task A_refresh_token_that_only_records_last_use_does_not_evict()
    {
        Prime(Account);
        await using var db = NewDb();
        var token = await db.RefreshTokenRecords.SingleAsync(t => t.ApplicationUserAccountId == AuthAccountId);

        token.LastUsedAt = Now;
        await db.SaveChangesAsync();

        Assert.True(IsCached(Account));
    }

    [Fact]
    public async Task A_subscription_change_evicts_only_that_learners_entitlement()
    {
        const string otherLearner = "learner-inv-other";
        // A subject on a different version stripe, so a stripe collision cannot make this flaky.
        var unrelated = UserStateCacheSubjects.Learner(otherLearner);
        for (var i = 0; _cache.BeginRead(unrelated).Stripe == _cache.BeginRead(Learner).Stripe; i++)
        {
            unrelated = UserStateCacheSubjects.Learner($"{otherLearner}-{i}");
        }

        Prime(Learner, UserStateCacheKinds.Entitlement);
        Prime(unrelated, UserStateCacheKinds.Entitlement);
        await using var db = NewDb();

        db.Subscriptions.Add(new Subscription
        {
            Id = "sub-inv-1",
            UserId = LearnerId,
            PlanId = "plan-inv",
            Status = SubscriptionStatus.Active,
            StartedAt = Now.AddDays(-1),
            ChangedAt = Now,
        });
        await db.SaveChangesAsync();

        Assert.False(IsCached(Learner, UserStateCacheKinds.Entitlement));
        Assert.True(IsCached(unrelated, UserStateCacheKinds.Entitlement));
    }

    [Fact]
    public async Task A_freeze_record_or_a_module_override_evicts_the_learner()
    {
        Prime(Learner, UserStateCacheKinds.FreezeStatus);
        await using (var db = NewDb())
        {
            db.AccountFreezeRecords.Add(new AccountFreezeRecord
            {
                Id = "frz-inv-1",
                UserId = LearnerId,
                Status = FreezeStatus.Active,
                IsCurrent = true,
                RequestedAt = Now,
                UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        Assert.False(IsCached(Learner, UserStateCacheKinds.FreezeStatus));

        Prime(Learner, UserStateCacheKinds.Entitlement);
        await using (var db = NewDb())
        {
            db.UserModuleOverrides.Add(new UserModuleOverride
            {
                Id = "ovr-inv-1",
                UserId = LearnerId,
                ModuleKey = "Mocks",
                Enabled = false,
                UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        Assert.False(IsCached(Learner, UserStateCacheKinds.Entitlement));
    }

    [Fact]
    public async Task An_expert_deactivation_evicts_the_account()
    {
        await using (var seed = NewDb())
        {
            seed.ExpertUsers.Add(new ExpertUser
            {
                Id = "expert-inv-1",
                AuthAccountId = AuthAccountId,
                Role = ApplicationUserRoles.Expert,
                DisplayName = "Invalidation Expert",
                Email = "expert-inv@example.test",
                IsActive = true,
                CreatedAt = Now,
            });
            await seed.SaveChangesAsync();
        }

        Prime(Account);
        await using var db = NewDb();
        var expert = await db.ExpertUsers.SingleAsync(e => e.Id == "expert-inv-1");

        expert.IsActive = false;
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
    }

    [Fact]
    public async Task A_billing_plan_edit_evicts_every_subject()
    {
        Prime(Account);
        Prime(Learner, UserStateCacheKinds.Entitlement);
        await using var db = NewDb();

        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan-inv-edit",
            Code = "plan-inv-edit",
            Name = "Edited plan",
            DashboardModulesJson = "[]",
        });
        await db.SaveChangesAsync();

        Assert.False(IsCached(Account));
        Assert.False(IsCached(Learner, UserStateCacheKinds.Entitlement));
        Assert.Equal(1, _cache.Snapshot().GlobalInvalidations);
    }

    [Fact]
    public async Task A_failed_save_evicts_nothing()
    {
        Prime(Account);
        await using var db = NewDb();

        // Same primary key as the seeded account: the INSERT fails, nothing is committed.
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = AuthAccountId,
            Email = "dup@example.test",
            NormalizedEmail = "DUP@EXAMPLE.TEST",
            PasswordHash = "not-used",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.True(IsCached(Account));
    }

    [Fact]
    public async Task An_empty_save_evicts_nothing()
    {
        Prime(Account);
        await using var db = NewDb();

        await db.SaveChangesAsync();

        Assert.True(IsCached(Account));
    }
}
