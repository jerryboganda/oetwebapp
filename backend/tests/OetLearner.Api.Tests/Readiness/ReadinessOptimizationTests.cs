using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Readiness;

namespace OetLearner.Api.Tests.Readiness;

/// <summary>
/// Backend optimisations of <see cref="ReadinessComputationService"/> (2026-10-05): the 90-day
/// attempt window is applied in the query instead of in memory, and concurrent stale requests of
/// one learner share a single compute. The window tests run on both provider paths: SQLite (which
/// cannot compare DateTimeOffset in SQL and so filters in memory) and the in-memory provider (which
/// runs the same predicate the Postgres query uses), and compare against the previous algorithm.
/// </summary>
public sealed class ReadinessOptimizationTests : IAsyncDisposable
{
    private const string UserId = "u-window";

    private SqliteConnection? _sqlite;

    public async ValueTask DisposeAsync()
    {
        if (_sqlite is not null)
        {
            await _sqlite.DisposeAsync();
        }
    }

    private LearnerDbContext NewDb(string provider)
    {
        DbContextOptions<LearnerDbContext> options;
        if (provider == "sqlite")
        {
            _sqlite = new SqliteConnection("Data Source=:memory:");
            _sqlite.Open();
            options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_sqlite).Options;
        }
        else
        {
            options = new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        }

        var db = new LearnerDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static ReadinessComputationService BuildService(LearnerDbContext db)
        => new(db, new ReadinessForecastCalculator(), new ReadinessBlockerRules());

    private static ContentItem NewContent() => new()
    {
        Id = "readiness-content",
        ContentType = "writing_task",
        SubtestCode = "writing",
        ProfessionId = "medicine",
        Title = "Readiness window task",
        Difficulty = "medium",
        EstimatedDurationMinutes = 45,
        CriteriaFocusJson = "[]",
        ScenarioType = "referral",
        ModeSupportJson = "[\"practice\",\"exam\"]",
        PublishedRevisionId = "readiness-content-r1",
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = DateTimeOffset.UtcNow,
    };

    private static LearnerUser NewUser(string id) => new()
    {
        Id = id,
        Email = $"{id}@example.test",
        DisplayName = "Window Learner",
        Role = "Learner",
        Timezone = "UTC",
        Locale = "en",
    };

    /// <summary>
    /// One completed attempt with a completed evaluation (a data point for the subtest).
    /// <paramref name="completedDaysAgo"/> = null leaves CompletedAt unset.
    /// </summary>
    private static void AddAttempt(LearnerDbContext db, string userId, string id, double? completedDaysAgo, AttemptState state = AttemptState.Completed)
    {
        var completedAt = completedDaysAgo is { } days ? DateTimeOffset.UtcNow.AddDays(-days) : (DateTimeOffset?)null;
        var startedAt = (completedAt ?? DateTimeOffset.UtcNow.AddDays(-1)).AddMinutes(-45);
        db.Attempts.Add(new Attempt
        {
            Id = id,
            UserId = userId,
            ContentId = "readiness-content",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = state,
            StartedAt = startedAt,
            SubmittedAt = completedAt,
            CompletedAt = completedAt,
            ElapsedSeconds = 2700,
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = $"eval-{id}",
            AttemptId = id,
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "350-360",
            GradeRange = "B",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            ModelExplanationSafe = "Practice estimate.",
            LearnerDisclaimer = "Not an official score.",
            GeneratedAt = completedAt ?? startedAt,
            LastTransitionAt = completedAt ?? startedAt,
        });
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("inmemory")]
    public async Task Compute_counts_exactly_the_attempts_the_previous_in_memory_filter_counted(string provider)
    {
        await using var db = NewDb(provider);
        db.Users.AddRange(NewUser(UserId), NewUser("u-other"));
        db.ContentItems.Add(NewContent());
        await db.SaveChangesAsync();

        AddAttempt(db, UserId, "in-10", completedDaysAgo: 10);
        AddAttempt(db, UserId, "in-60", completedDaysAgo: 60);
        AddAttempt(db, UserId, "in-89", completedDaysAgo: 89);
        AddAttempt(db, UserId, "out-91", completedDaysAgo: 91);
        AddAttempt(db, UserId, "out-400", completedDaysAgo: 400);
        AddAttempt(db, UserId, "no-completed-at", completedDaysAgo: null);
        AddAttempt(db, UserId, "still-running", completedDaysAgo: 5, state: AttemptState.InProgress);
        AddAttempt(db, "u-other", "other-learner", completedDaysAgo: 3);
        await db.SaveChangesAsync();

        // The previous algorithm: load every completed attempt of the learner, filter in memory.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var legacyWindow = (await db.Attempts.AsNoTracking()
                .Where(a => a.UserId == UserId && a.State == AttemptState.Completed)
                .Select(a => new { a.Id, a.CompletedAt })
                .ToListAsync())
            .Where(a => a.CompletedAt.HasValue && a.CompletedAt.Value >= cutoff)
            .Select(a => a.Id)
            .OrderBy(id => id)
            .ToList();
        Assert.Equal(new[] { "in-10", "in-60", "in-89" }, legacyWindow);

        var snapshot = await BuildService(db).ComputeAsync(UserId, CancellationToken.None);

        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        var evidence = payload.RootElement.GetProperty("evidence");
        Assert.Equal(legacyWindow.Count, evidence.GetProperty("practiceQuestions").GetInt32());
        var writing = payload.RootElement.GetProperty("subTests").EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "writing");
        Assert.Equal(legacyWindow.Count, writing.GetProperty("dataPoints").GetInt32());
        Assert.Equal(legacyWindow.Count, snapshot.DataPointCount);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("inmemory")]
    public async Task Compute_with_no_attempts_in_the_window_has_no_data_points(string provider)
    {
        await using var db = NewDb(provider);
        db.Users.Add(NewUser(UserId));
        db.ContentItems.Add(NewContent());
        await db.SaveChangesAsync();
        AddAttempt(db, UserId, "old-1", completedDaysAgo: 120);
        AddAttempt(db, UserId, "old-2", completedDaysAgo: 365);
        await db.SaveChangesAsync();

        var snapshot = await BuildService(db).ComputeAsync(UserId, CancellationToken.None);

        Assert.Equal(0, snapshot.DataPointCount);
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        Assert.Equal(0, payload.RootElement.GetProperty("evidence").GetProperty("practiceQuestions").GetInt32());
    }

    [Fact]
    public async Task Concurrent_stale_requests_of_one_learner_share_a_single_compute()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        const string userId = "u-single-flight";
        int baseVersion;
        await using (var seed = new LearnerDbContext(options))
        {
            seed.Users.Add(NewUser(userId));
            await seed.SaveChangesAsync();
            var first = await BuildService(seed).ComputeAsync(userId, CancellationToken.None);
            baseVersion = first.Version;
            first.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5); // stale
            await seed.SaveChangesAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = new LearnerDbContext(options);
            return await BuildService(db).GetOrComputeAsync(userId, CancellationToken.None);
        })));

        // One compute (version + 1); every other caller found the fresh row after the gate.
        await using var verify = new LearnerDbContext(options);
        var stored = await verify.ReadinessSnapshots.AsNoTracking().SingleAsync(s => s.UserId == userId);
        Assert.Equal(baseVersion + 1, stored.Version);
        Assert.All(results, result => Assert.Equal(baseVersion + 1, result.Version));
        Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_fresh_snapshot_is_returned_without_taking_the_gate_or_recomputing()
    {
        await using var db = NewDb("inmemory");
        db.Users.Add(NewUser("u-fresh"));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var computed = await service.ComputeAsync("u-fresh", CancellationToken.None);

        var again = await service.GetOrComputeAsync("u-fresh", CancellationToken.None);

        Assert.Equal(computed.Version, again.Version);
    }
}
