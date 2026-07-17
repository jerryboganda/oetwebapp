using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Billing;

namespace OetWithDrHesham.Api.Tests;

public sealed class AiUsageAnalyticsPerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CommandCounter _commands = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task LearnerSummary_UsesOneTotalsScanAndPreservesOutput()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.AiUsageRecords.AddRange(
                Usage("usage-1", "learner", "writing.grade", "openai", 100, 20, 0.12m, AiCallOutcome.Success, Now.AddDays(-1)),
                Usage("usage-2", "learner", "writing.grade", "openai", 80, 20, 0.08m, AiCallOutcome.ProviderError, Now),
                Usage("usage-3", "learner", "vocabulary.gloss", "anthropic", 30, 10, 0.02m, AiCallOutcome.Success, Now));
            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        _commands.Commands.Clear();

        var summary = await new AiUsageAnalyticsService(db).GetLearnerSummaryAsync(
            "learner",
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 7, 13),
            CancellationToken.None);

        Assert.Equal(6, _commands.Commands.Count);
        Assert.Equal(3, _commands.Commands.Count(ReadsAiUsage));
        Assert.Equal(3, summary.TotalCalls);
        Assert.Equal(260, summary.TotalTokens);
        Assert.Equal(0.22m, summary.TotalCostUsd);
        Assert.Equal(1, summary.FailedCalls);
        Assert.Collection(
            summary.ByFeature,
            feature =>
            {
                Assert.Equal("writing.grade", feature.FeatureCode);
                Assert.Equal(2, feature.Calls);
            },
            feature => Assert.Equal("vocabulary.gloss", feature.FeatureCode));
        Assert.Equal(["2026-07-12", "2026-07-13"], summary.Daily.Select(bucket => bucket.Day));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task AdminSummary_UsesAtMostSixQueriesAndCapsTopResultsInSql()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            for (var index = 0; index < 30; index++)
            {
                seed.AiUsageRecords.Add(Usage(
                    $"usage-{index:D2}",
                    $"user-{index:D2}",
                    $"feature-{index:D2}",
                    $"provider-{index:D2}",
                    10 + index,
                    5,
                    0.01m * (index + 1),
                    index % 3 == 0 ? AiCallOutcome.ProviderError : AiCallOutcome.Success,
                    Now,
                    latencyMs: 100 + index));
            }

            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        _commands.Commands.Clear();

        var summary = await new AiUsageAnalyticsService(db).GetAdminSummaryAsync(
            new DateOnly(2026, 7, 13),
            new DateOnly(2026, 7, 13),
            null,
            null,
            CancellationToken.None);

        Assert.Equal(5, _commands.Commands.Count);
        Assert.All(_commands.Commands, command => Assert.True(ReadsAiUsage(command)));
        Assert.Equal(30, summary.TotalCalls);
        Assert.Equal(30, summary.UniqueUsers);
        Assert.Equal(20, summary.ByFeature.Count);
        Assert.Equal(20, summary.ByProvider.Count);
        Assert.Equal(25, summary.TopUsers.Count);
        Assert.Contains(_commands.Commands, command =>
            command.Contains("PeriodDayKey", StringComparison.Ordinal)
            && command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && command.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_commands.Commands, command =>
            command.Contains("UserId", StringComparison.Ordinal)
            && command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static AiUsageRecord Usage(
        string id,
        string userId,
        string feature,
        string provider,
        int promptTokens,
        int completionTokens,
        decimal cost,
        AiCallOutcome outcome,
        DateTimeOffset createdAt,
        int latencyMs = 250)
        => new()
        {
            Id = id,
            UserId = userId,
            FeatureCode = feature,
            ProviderId = provider,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CostEstimateUsd = cost,
            Outcome = outcome,
            LatencyMs = latencyMs,
            CreatedAt = createdAt,
            PeriodMonthKey = createdAt.ToString("yyyy-MM"),
            PeriodDayKey = createdAt.ToString("yyyy-MM-dd"),
        };

    private static bool ReadsAiUsage(string command)
        => command.Contains("AiUsageRecords", StringComparison.Ordinal);

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

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
