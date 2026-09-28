using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner directive 2026-09-29 — the Writing subscription selector must:
/// force Claude/Codex in manual modes, prefer Claude in auto, fail over to
/// Codex at the failover threshold, fail over on a recorded quota signal, and
/// return to Claude once the marker clears (weekly reset).
/// </summary>
public sealed class WritingSubscriptionSelectorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public WritingSubscriptionSelectorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private WritingSubscriptionSelector BuildSelector(
        RuntimeSettingsRow row,
        WritingQuotaSnapshot snapshot)
    {
        var settings = new StubSettingsProvider(row);
        var quota = new StubQuotaService(snapshot);
        var services = new ServiceCollection()
            .AddSingleton(_options)
            .AddDbContext<LearnerDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        return new WritingSubscriptionSelector(quota, settings, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WritingSubscriptionSelector>.Instance);
    }

    private static WritingQuotaSnapshot Snapshot(double? util, DateTimeOffset? resetsAt = null)
        => new(util, resetsAt, util is null ? "unknown" : "estimated", 0, 0, DateTimeOffset.UtcNow);

    [Fact]
    public async Task AutoMode_BelowThreshold_RoutesClaude()
    {
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "auto", WritingAiWarnPct = 80, WritingAiFailoverPct = 90 };
        var selector = BuildSelector(row, Snapshot(40));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
        Assert.False(decision.IsFallback);
        Assert.Equal("auto_primary", decision.Reason);
    }

    [Fact]
    public async Task AutoMode_AtFailoverThreshold_RoutesClaudeApi()
    {
        // Level 2: crossing the weekly failover threshold moves new requests to the
        // Claude API (pay-as-you-go), NOT straight to Codex — Codex is level 3.
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "auto", WritingAiFailoverPct = 90 };
        var selector = BuildSelector(row, Snapshot(92));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.ClaudeApi, decision.ProviderCode);
        Assert.True(decision.IsFallback);
        Assert.Equal("auto_threshold_failover", decision.Reason);
    }

    [Fact]
    public async Task AutoMode_WarnBand_StillRoutesClaude()
    {
        // 80–90% warns but does NOT fail over.
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "auto", WritingAiWarnPct = 80, WritingAiFailoverPct = 90 };
        var selector = BuildSelector(row, Snapshot(85));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
        Assert.False(decision.IsFallback);
    }

    [Fact]
    public async Task ForcedClaude_IgnoresQuota()
    {
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "claude" };
        var selector = BuildSelector(row, Snapshot(99));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
        Assert.Equal("forced_claude", decision.Reason);
    }

    [Fact]
    public async Task ForcedCodex_IgnoresQuota()
    {
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "codex" };
        var selector = BuildSelector(row, Snapshot(0));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Codex, decision.ProviderCode);
        Assert.Equal("forced_codex", decision.Reason);
    }

    [Fact]
    public async Task AutoMode_RecordedQuotaSignal_FailsOverToClaudeApi_UntilMarkerClears()
    {
        var row = new RuntimeSettingsRow
        {
            Id = "default",
            WritingAiProviderMode = "auto",
            WritingAiClaudeQuotaExceededUntil = DateTimeOffset.UtcNow.AddDays(2),
        };
        var selector = BuildSelector(row, Snapshot(10)); // low estimate, but hard signal wins
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.ClaudeApi, decision.ProviderCode);
        Assert.Equal("auto_quota_signal", decision.Reason);
    }

    [Fact]
    public async Task AutoMode_ExpiredQuotaMarker_ReturnsToClaude()
    {
        var row = new RuntimeSettingsRow
        {
            Id = "default",
            WritingAiProviderMode = "auto",
            WritingAiClaudeQuotaExceededUntil = DateTimeOffset.UtcNow.AddHours(-1), // already reset
        };
        var selector = BuildSelector(row, Snapshot(10));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
        Assert.False(decision.IsFallback);
    }

    [Fact]
    public async Task AutoMode_UnknownUtilisation_DefaultsToClaude()
    {
        // No cap configured → utilisation unknown → never block the primary.
        var row = new RuntimeSettingsRow { Id = "default", WritingAiProviderMode = "auto" };
        var selector = BuildSelector(row, Snapshot(null));
        var decision = await selector.DecideAsync(default);
        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
    }

    // ── Stubs ────────────────────────────────────────────────────────────────

    private sealed class StubQuotaService(WritingQuotaSnapshot snapshot) : IWritingSubscriptionQuotaService
    {
        public Task<WritingQuotaSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(snapshot);
    }

    private sealed class StubSettingsProvider(RuntimeSettingsRow row) : IRuntimeSettingsProvider
    {
        public RuntimeSettingsSnapshot? CurrentSnapshot => null;
        public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default) => Task.FromResult(row);
        public Task<EffectiveSettings> GetAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public void Invalidate() { }
        public string Protect(string plain) => plain;
        public string? Unprotect(string? cipher) => cipher;
    }
}
