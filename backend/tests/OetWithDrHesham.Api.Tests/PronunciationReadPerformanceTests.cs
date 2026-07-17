using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests;

public sealed class PronunciationReadPerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

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
        await SeedAsync(db);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ProfileRead_BoundsEveryQueryAndPreservesProfileShape()
    {
        await using var db = new LearnerDbContext(_options);
        _commands.Commands.Clear();

        var profile = JsonSerializer.SerializeToElement(
            await CreateService(db).GetProfileAsync("learner", CancellationToken.None));

        Assert.Equal(3, _commands.Commands.Count);
        Assert.All(_commands.Commands, AssertBounded);
        Assert.Equal(10, profile.GetProperty("totalAssessments").GetInt32());
        Assert.Equal(59.5, profile.GetProperty("overallScore").GetDouble());
        Assert.Equal(10, profile.GetProperty("progressOverTime").GetArrayLength());
        Assert.Equal(10, profile.GetProperty("phonemeProgress").GetArrayLength());
        Assert.Equal(5, profile.GetProperty("weakPhonemes").GetArrayLength());
        Assert.Equal(
            "phoneme-14",
            profile.GetProperty("phonemeProgress")[0].GetProperty("phonemeCode").GetString());
        Assert.Equal(
            "phoneme-00",
            profile.GetProperty("weakPhonemes")[0].GetProperty("phonemeCode").GetString());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MyProgressRead_UsesOneBoundedNoTrackingQueryAndPreservesShape()
    {
        await using var db = new LearnerDbContext(_options);
        _commands.Commands.Clear();

        var progress = JsonSerializer.SerializeToElement(
            await CreateService(db).GetMyProgressAsync("learner", CancellationToken.None));

        Assert.Single(_commands.Commands);
        AssertBounded(_commands.Commands[0]);
        Assert.Equal(10, progress.GetArrayLength());
        Assert.Equal("phoneme-14", progress[0].GetProperty("phonemeCode").GetString());
        Assert.Equal(54.4, progress[0].GetProperty("averageScore").GetDouble());
        Assert.True(progress[0].TryGetProperty("attemptCount", out _));
        Assert.True(progress[0].TryGetProperty("lastPracticedAt", out _));
        Assert.True(progress[0].TryGetProperty("nextDueAt", out _));
        Assert.True(progress[0].TryGetProperty("intervalDays", out _));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static async Task SeedAsync(LearnerDbContext db)
    {
        for (var index = 0; index < 15; index++)
        {
            var practicedAt = StartedAt.AddDays(index);
            db.LearnerPronunciationProgress.Add(new LearnerPronunciationProgress
            {
                Id = Guid.NewGuid(),
                UserId = "learner",
                PhonemeCode = $"phoneme-{index:D2}",
                AverageScore = 40.44 + index,
                AttemptCount = index + 1,
                LastPracticedAt = practicedAt,
                NextDueAt = practicedAt.AddDays(2),
                IntervalDays = 2,
            });
            db.PronunciationAssessments.Add(new PronunciationAssessment
            {
                Id = $"assessment-{index:D2}",
                UserId = "learner",
                AccuracyScore = 50 + index,
                FluencyScore = 50 + index,
                CompletenessScore = 50 + index,
                ProsodyScore = 50 + index,
                OverallScore = 50 + index,
                ProjectedSpeakingScaled = OetScoring.PronunciationProjectedScaled(50 + index),
                CreatedAt = practicedAt,
            });
        }

        db.LearnerPronunciationProgress.Add(new LearnerPronunciationProgress
        {
            Id = Guid.NewGuid(),
            UserId = "learner",
            PhonemeCode = "_speech_overall",
            AverageScore = 10,
            AttemptCount = 100,
            LastPracticedAt = StartedAt.AddDays(20),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static PronunciationService CreateService(LearnerDbContext db)
        => new(
            db,
            null!,
            null!,
            null!,
            null!,
            null!,
            Options.Create(new PronunciationOptions { Provider = "mock" }),
            NullLogger<PronunciationService>.Instance);

    private static void AssertBounded(string command)
        => Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);

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
