using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// The kill-switch drill, as a test rather than a document.
///
/// <para>
/// The source specification requires evidence that each companion switch
/// actually stops what it claims to stop, and that it can be thrown without a
/// deploy. A drill written up in a runbook rots; this runs on every build.
/// </para>
///
/// <para>
/// The property that matters most is <b>fail-closed</b>. Every one of these
/// switches guards something that reaches a learner's entitlements, performance
/// history or credit balance, so "we could not read the flag" must mean "off",
/// never "carry on". That is the opposite of a content catalogue like
/// <c>StrategyGuideService</c>, which correctly fails open.
/// </para>
/// </summary>
public sealed class CompanionKillSwitchTests : IAsyncDisposable
{
    /// <summary>Every companion switch, with the accessor that reads it.</summary>
    public static TheoryData<string> AllSwitchKeys() =>
    [
        "ai_learning_companion",
        "companion_retrieval",
        "companion_actions",
        "companion_credits",
        "companion_score_display",
    ];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionKillSwitchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [MemberData(nameof(AllSwitchKeys))]
    public async Task MissingFlagRow_ReadsAsOff(string key)
    {
        Assert.False(await ReadAsync(key));
    }

    [Theory]
    [MemberData(nameof(AllSwitchKeys))]
    public async Task DisabledFlag_ReadsAsOff(string key)
    {
        await SetFlagAsync(key, enabled: false);
        Assert.False(await ReadAsync(key));
    }

    [Theory]
    [MemberData(nameof(AllSwitchKeys))]
    public async Task EnabledFlag_ReadsAsOn(string key)
    {
        await SetFlagAsync(key, enabled: true);
        Assert.True(await ReadAsync(key));
    }

    [Theory]
    [MemberData(nameof(AllSwitchKeys))]
    public async Task FlipBackToDisabled_TakesEffectWithoutRestart(string key)
    {
        await SetFlagAsync(key, enabled: true);
        Assert.True(await ReadAsync(key));

        // The drill: an operator flips the row in /admin/flags. No cache to wait
        // out, no deploy — the very next read must already refuse.
        await SetFlagAsync(key, enabled: false);
        Assert.False(await ReadAsync(key));
    }

    [Fact]
    public async Task UnreadableDatabase_FailsClosedForEverySwitch()
    {
        // Disposing the connection makes every query throw, standing in for the
        // database being unreachable mid-incident — the exact moment a switch
        // must not fail open.
        await using var db = new LearnerDbContext(_options);
        var flags = new CompanionFeatureFlags(db, NullLogger<CompanionFeatureFlags>.Instance);
        await _connection.DisposeAsync();

        Assert.False(await flags.IsEnabledAsync(CancellationToken.None));
        Assert.False(await flags.IsRetrievalEnabledAsync(CancellationToken.None));
        Assert.False(await flags.AreActionsEnabledAsync(CancellationToken.None));
        Assert.False(await flags.IsCreditConsumptionEnabledAsync(CancellationToken.None));
        Assert.False(await flags.IsScoreDisplayEnabledAsync(CancellationToken.None));
        Assert.False(await flags.IsPlatformFlagOnAsync("video_library", CancellationToken.None));
    }

    [Fact]
    public async Task NewestRowWins_SoADuplicatedKeyCannotResurrectAKilledSurface()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-1);
        await SetFlagAsync("ai_learning_companion", enabled: true, updatedAt: old);
        await SetFlagAsync("ai_learning_companion", enabled: false, updatedAt: DateTimeOffset.UtcNow);

        Assert.False(await ReadAsync("ai_learning_companion"));
    }

    [Fact]
    public async Task ScoreDisplaySwitch_IsIndependentOfTheMasterSwitch()
    {
        // TV-006 / TV-007: turning the companion on must NOT turn numeric band
        // estimates on with it. They are separately gated on calibration.
        await SetFlagAsync("ai_learning_companion", enabled: true);

        Assert.True(await ReadAsync("ai_learning_companion"));
        Assert.False(await ReadAsync("companion_score_display"));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<bool> ReadAsync(string key)
    {
        await using var db = new LearnerDbContext(_options);
        var flags = new CompanionFeatureFlags(db, NullLogger<CompanionFeatureFlags>.Instance);

        return key switch
        {
            "ai_learning_companion" => await flags.IsEnabledAsync(CancellationToken.None),
            "companion_retrieval" => await flags.IsRetrievalEnabledAsync(CancellationToken.None),
            "companion_actions" => await flags.AreActionsEnabledAsync(CancellationToken.None),
            "companion_credits" => await flags.IsCreditConsumptionEnabledAsync(CancellationToken.None),
            "companion_score_display" => await flags.IsScoreDisplayEnabledAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown companion switch."),
        };
    }

    private async Task SetFlagAsync(string key, bool enabled, DateTimeOffset? updatedAt = null)
    {
        await using var db = new LearnerDbContext(_options);
        db.FeatureFlags.Add(new FeatureFlag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = key,
            Key = key,
            Enabled = enabled,
            Description = "kill-switch drill",
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
