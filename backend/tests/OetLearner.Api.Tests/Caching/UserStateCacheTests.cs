using Microsoft.Extensions.Options;
using OetLearner.Api.Services.Caching;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Caching;

/// <summary>
/// Hit / miss / expiry / invalidation behaviour of the per-process short-lived user-state cache
/// (owner-approved 2026-10-05; docs/ops/user-state-cache.md). The clock is injected, so nothing
/// here sleeps.
/// </summary>
public sealed class UserStateCacheTests
{
    private const string Kind = UserStateCacheKinds.JwtAccount;
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static (UserStateCache Cache, MutableTimeProvider Clock) Build(Action<UserStateCacheOptions>? configure = null)
    {
        var options = new UserStateCacheOptions();
        configure?.Invoke(options);
        var clock = new MutableTimeProvider(Start);
        return (new UserStateCache(Options.Create(options), clock), clock);
    }

    private static void Fill(UserStateCache cache, string subject, object value, string kind = Kind, string variant = "-", DateTimeOffset? notAfter = null)
        => cache.Set(kind, subject, variant, value, cache.BeginRead(subject), notAfter);

    // Two subjects that cannot share a version stripe (a collision only ever costs an extra miss,
    // but the "other subject survives" assertions below need a guaranteed distinct stripe).
    private static (string A, string B) DistinctStripeSubjects(UserStateCache cache)
    {
        const string a = "user:alpha";
        var stripeOfA = cache.BeginRead(a).Stripe;
        for (var i = 0; i < 1000; i++)
        {
            var b = $"user:beta-{i}";
            if (cache.BeginRead(b).Stripe != stripeOfA)
            {
                return (a, b);
            }
        }

        throw new InvalidOperationException("No subject with a different stripe found.");
    }

    [Fact]
    public void Entry_is_served_until_the_ttl_and_then_expires()
    {
        var (cache, clock) = Build();
        var value = new object();
        Fill(cache, "acct:1", value);

        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? hit));
        Assert.Same(value, hit);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? expired));
        Assert.Null(expired);
    }

    [Fact]
    public void Ttl_is_clamped_to_thirty_seconds()
    {
        var (cache, clock) = Build(o => o.TtlSeconds = 999);
        Fill(cache, "acct:1", new object());

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? _));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Assert.Equal(30, cache.Snapshot().TtlSeconds);
    }

    [Fact]
    public void Ttl_is_clamped_to_at_least_one_second()
    {
        var (cache, _) = Build(o => o.TtlSeconds = 0);

        Assert.Equal(1, cache.Snapshot().TtlSeconds);
    }

    [Fact]
    public void NotAfter_caps_an_entry_below_the_ttl()
    {
        var (cache, clock) = Build();
        Fill(cache, "acct:1", new object(), notAfter: Start.AddSeconds(4));

        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? _));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
    }

    [Fact]
    public void NotAfter_in_the_past_stores_nothing()
    {
        var (cache, _) = Build();

        Fill(cache, "acct:1", new object(), notAfter: Start.AddSeconds(-1));

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Assert.Equal(0, cache.Snapshot().Entries);
    }

    [Fact]
    public void Invalidate_evicts_only_the_named_subject()
    {
        var (cache, _) = Build();
        var (a, b) = DistinctStripeSubjects(cache);
        Fill(cache, a, new object());
        Fill(cache, b, new object());

        cache.Invalidate(a);

        Assert.False(cache.TryGet(Kind, a, "-", out object? _));
        Assert.True(cache.TryGet(Kind, b, "-", out object? _));
    }

    [Fact]
    public void Invalidate_evicts_every_kind_and_variant_of_the_subject()
    {
        var (cache, _) = Build();
        Fill(cache, "user:7", new object(), UserStateCacheKinds.Entitlement, string.Empty);
        Fill(cache, "user:7", new object(), UserStateCacheKinds.FreezeStatus, string.Empty);
        Fill(cache, "user:7", new object(), UserStateCacheKinds.JwtAccount, "family-a");
        Fill(cache, "user:7", new object(), UserStateCacheKinds.JwtAccount, "family-b");

        cache.Invalidate("user:7");

        Assert.False(cache.TryGet(UserStateCacheKinds.Entitlement, "user:7", string.Empty, out object? _));
        Assert.False(cache.TryGet(UserStateCacheKinds.FreezeStatus, "user:7", string.Empty, out object? _));
        Assert.False(cache.TryGet(UserStateCacheKinds.JwtAccount, "user:7", "family-a", out object? _));
        Assert.False(cache.TryGet(UserStateCacheKinds.JwtAccount, "user:7", "family-b", out object? _));
    }

    [Fact]
    public void Kinds_and_variants_of_one_subject_do_not_collide()
    {
        var (cache, _) = Build();
        var entitlement = new object();
        var freeze = new object();
        Fill(cache, "user:7", entitlement, UserStateCacheKinds.Entitlement, string.Empty);
        Fill(cache, "user:7", freeze, UserStateCacheKinds.FreezeStatus, string.Empty);

        Assert.True(cache.TryGet(UserStateCacheKinds.Entitlement, "user:7", string.Empty, out object? e));
        Assert.True(cache.TryGet(UserStateCacheKinds.FreezeStatus, "user:7", string.Empty, out object? f));
        Assert.Same(entitlement, e);
        Assert.Same(freeze, f);
        Assert.False(cache.TryGet(UserStateCacheKinds.FreezeGate, "user:7", string.Empty, out object? _));
    }

    [Fact]
    public void A_value_read_before_an_invalidation_is_never_stored_after_it()
    {
        var (cache, _) = Build();
        var token = cache.BeginRead("acct:1");

        // The write lands (and invalidates) while the reader is still on its way back from the database.
        cache.Invalidate("acct:1");
        cache.Set(Kind, "acct:1", "-", new object(), token, notAfter: null);

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        var stats = Assert.Single(cache.Snapshot().Kinds, k => k.Kind == Kind);
        Assert.Equal(1, stats.RejectedSets);
        Assert.Equal(0, stats.Sets);
    }

    [Fact]
    public void A_global_invalidation_during_the_read_also_rejects_the_fill()
    {
        var (cache, _) = Build();
        var token = cache.BeginRead("acct:1");

        cache.InvalidateAll();
        cache.Set(Kind, "acct:1", "-", new object(), token, notAfter: null);

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
    }

    [Fact]
    public void InvalidateAll_evicts_every_subject()
    {
        var (cache, _) = Build();
        Fill(cache, "acct:1", new object());
        Fill(cache, "user:2", new object(), UserStateCacheKinds.Entitlement, string.Empty);

        cache.InvalidateAll();

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Assert.False(cache.TryGet(UserStateCacheKinds.Entitlement, "user:2", string.Empty, out object? _));
        Assert.Equal(1, cache.Snapshot().GlobalInvalidations);
    }

    [Fact]
    public void An_entry_filled_after_an_invalidation_is_served_normally()
    {
        var (cache, _) = Build();
        cache.Invalidate("acct:1");

        Fill(cache, "acct:1", new object());

        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? _));
    }

    [Fact]
    public void Invalidate_helpers_use_the_prefixed_subjects_and_ignore_empty_ids()
    {
        var (cache, _) = Build();
        Fill(cache, UserStateCacheSubjects.AuthAccount("a1"), new object());
        Fill(cache, UserStateCacheSubjects.Learner("l1"), new object());

        cache.InvalidateAuthAccount("a1");
        cache.InvalidateLearner("l1");
        cache.InvalidateAuthAccount(null);
        cache.InvalidateLearner(string.Empty);

        Assert.False(cache.TryGet(Kind, UserStateCacheSubjects.AuthAccount("a1"), "-", out object? _));
        Assert.False(cache.TryGet(Kind, UserStateCacheSubjects.Learner("l1"), "-", out object? _));
        Assert.Equal(2, cache.Snapshot().Invalidations);
        // The two id spaces never share a subject.
        Assert.NotEqual(UserStateCacheSubjects.AuthAccount("x"), UserStateCacheSubjects.Learner("x"));
    }

    [Fact]
    public void An_entry_from_the_future_is_not_trusted_when_the_clock_moves_backwards()
    {
        var (cache, clock) = Build();
        clock.Advance(TimeSpan.FromMinutes(5));
        Fill(cache, "acct:1", new object());

        clock.Advance(TimeSpan.FromMinutes(-10));

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
    }

    [Fact]
    public void Config_switch_off_serves_and_stores_nothing()
    {
        var (cache, _) = Build(o => o.Enabled = false);

        Fill(cache, "acct:1", new object());

        Assert.False(cache.IsEnabled);
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Assert.Equal(0, cache.Snapshot().Entries);
    }

    [Fact]
    public void Runtime_switch_off_drops_every_entry_and_back_on_does_not_resurrect_them()
    {
        var (cache, _) = Build();
        Fill(cache, "acct:1", new object());

        cache.ApplyRuntimeSwitch(false);
        Assert.False(cache.IsEnabled);
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Fill(cache, "acct:2", new object());
        Assert.Equal(0, cache.Snapshot().Entries);

        cache.ApplyRuntimeSwitch(true);
        Assert.True(cache.IsEnabled);
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _));
        Fill(cache, "acct:3", new object());
        Assert.True(cache.TryGet(Kind, "acct:3", "-", out object? _));
    }

    [Fact]
    public void A_stored_type_is_only_returned_for_the_requested_type()
    {
        var (cache, _) = Build();
        Fill(cache, "acct:1", "a string value");

        Assert.False(cache.TryGet(Kind, "acct:1", "-", out JwtAccountState? _));
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out string? text));
        Assert.Equal("a string value", text);
    }

    [Fact]
    public void Counters_report_hits_misses_sets_and_invalidations_per_kind()
    {
        var (cache, _) = Build();
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _)); // miss
        Fill(cache, "acct:1", new object());                             // set
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? _));  // hit
        Assert.True(cache.TryGet(Kind, "acct:1", "-", out object? _));  // hit
        cache.Invalidate("acct:1");
        Assert.False(cache.TryGet(Kind, "acct:1", "-", out object? _)); // miss (invalidated)

        var snapshot = cache.Snapshot();
        var stats = Assert.Single(snapshot.Kinds, k => k.Kind == Kind);
        Assert.Equal(2, stats.Hits);
        Assert.Equal(2, stats.Misses);
        Assert.Equal(1, stats.Sets);
        Assert.Equal(1, snapshot.Invalidations);
        Assert.True(snapshot.ConfigEnabled);
        Assert.True(snapshot.RuntimeEnabled);
        Assert.Equal(UserStateCache.FeatureFlagKey, snapshot.FeatureFlagKey);
    }

    [Fact]
    public void Reaching_the_entry_bound_clears_the_cache_on_the_next_sweep()
    {
        var (cache, _) = Build(o => o.MaxEntries = 10);

        // The sweep runs every 1024 fills; with the bound at 10 it empties the cache.
        for (var i = 0; i < 1024; i++)
        {
            Fill(cache, $"acct:{i}", new object());
        }

        Assert.Equal(0, cache.Snapshot().Entries);
    }

    [Fact]
    public void A_sweep_drops_expired_entries_and_keeps_live_ones()
    {
        var (cache, clock) = Build();
        for (var i = 0; i < 1000; i++)
        {
            Fill(cache, $"old:{i}", new object());
        }

        clock.Advance(TimeSpan.FromSeconds(20)); // every "old" entry is now expired
        Fill(cache, "live:1", new object());
        for (var i = 0; i < 23; i++)
        {
            Fill(cache, $"fresh:{i}", new object()); // the 1024th fill triggers the sweep
        }

        Assert.True(cache.TryGet(Kind, "live:1", "-", out object? _));
        Assert.Equal(24, cache.Snapshot().Entries);
    }
}
