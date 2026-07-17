using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Readiness;

namespace OetWithDrHesham.Api.Tests.Readiness;

public sealed class ReadinessComputationServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public ReadinessComputationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private async Task<LearnerDbContext> NewDbAsync(bool seedGoal = true, string userId = "u-test")
    {
        var db = new LearnerDbContext(_options);
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            Email = $"{userId}@example.test",
            DisplayName = "Test Learner",
            Role = "Learner",
            Timezone = "UTC",
            Locale = "en"
        });
        if (seedGoal)
        {
            db.Goals.Add(new LearnerGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProfessionId = "doctor",
                TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
                StudyHoursPerWeek = 8,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return db;
    }

    private static ReadinessComputationService BuildService(LearnerDbContext db)
        => new(db, new ReadinessForecastCalculator(), new ReadinessBlockerRules());

    [Fact]
    public async Task Compute_NoData_ReturnsUnknownRisk()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Equal("Unknown", snapshot.OverallRisk);
        Assert.Equal(0, snapshot.DataPointCount);
        Assert.Null(snapshot.TargetDateProbability);
    }

    [Fact]
    public async Task Compute_NoData_PersistsSnapshotRow()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        await service.ComputeAsync("u-test", CancellationToken.None);

        var rows = await db.ReadinessSnapshots.CountAsync();
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Compute_AppendsHistoryRow_OncePerIsoWeek()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        await service.ComputeAsync("u-test", CancellationToken.None);
        await service.ComputeAsync("u-test", CancellationToken.None);

        var rows = await db.ReadinessHistories.Where(h => h.UserId == "u-test").CountAsync();
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Compute_VersionIncrementsOnSubsequentComputations()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var first = await service.ComputeAsync("u-test", CancellationToken.None);
        var firstVersion = first.Version;
        var firstId = first.Id;
        var second = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Equal(firstId, second.Id);
        Assert.True(second.Version > firstVersion, $"Expected second.Version > {firstVersion}, got {second.Version}");
    }

    [Fact]
    public async Task Compute_UsesGoalTargetDate_InPayload()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Contains(DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)).ToString("yyyy-MM-dd"), snapshot.PayloadJson);
    }

    [Fact]
    public async Task Compute_RecommendedHours_HonorsGoalFloor()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.True(snapshot.RecommendedStudyHoursPerWeek >= 8, $"Expected >= 8, got {snapshot.RecommendedStudyHoursPerWeek}");
        Assert.True(snapshot.RecommendedStudyHoursPerWeek <= 25);
    }

    [Fact]
    public async Task Compute_VocabularyReadinessZero_WithNoActivity()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Equal(0m, snapshot.VocabularyReadiness);
    }

    [Fact]
    public async Task Compute_VocabularyReadiness_RisesWithMastery()
    {
        await using var db = await NewDbAsync();
        for (int i = 0; i < 100; i++)
        {
            db.LearnerVocabularies.Add(new LearnerVocabulary
            {
                Id = Guid.NewGuid(),
                UserId = "u-test",
                TermId = $"term-{i}",
                Mastery = "mastered",
                ReviewCount = 5,
                CorrectCount = 4,
                LastReviewedAt = DateTimeOffset.UtcNow.AddDays(-3),
                AddedAt = DateTimeOffset.UtcNow.AddDays(-30)
            });
        }
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.True(snapshot.VocabularyReadiness > 0m);
    }

    [Fact]
    public async Task GetOrComputeAsync_ReturnsCached_WhenNotExpired()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);
        var first = await service.ComputeAsync("u-test", CancellationToken.None);

        var second = await service.GetOrComputeAsync("u-test", CancellationToken.None);

        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task GetOrComputeAsync_Recomputes_WhenExpired()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);
        var first = await service.ComputeAsync("u-test", CancellationToken.None);
        var firstVersion = first.Version;
        first.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        var second = await service.GetOrComputeAsync("u-test", CancellationToken.None);

        Assert.True(second.Version > firstVersion);
    }

    [Fact]
    public async Task ForceRefreshAsync_AlwaysRecomputes()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);
        var first = await service.ComputeAsync("u-test", CancellationToken.None);
        var firstVersion = first.Version;

        var refreshed = await service.ForceRefreshAsync("u-test", CancellationToken.None);

        Assert.True(refreshed.Version > firstVersion);
    }

    [Fact]
    public async Task Compute_SnapshotExpiresAt_24HoursFromNow()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        var delta = snapshot.ExpiresAt - snapshot.ComputedAt;
        Assert.InRange(delta.TotalHours, 23.5, 24.5);
    }

    [Fact]
    public async Task Compute_ConfidenceLow_WhenFewDataPoints()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Equal("Low", snapshot.ConfidenceLevel);
    }

    [Fact]
    public async Task Compute_PayloadHasBlockersArray()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Contains("\"blockers\"", snapshot.PayloadJson);
    }

    [Fact]
    public async Task Compute_NoMock_GeneratesNoMockBlocker()
    {
        await using var db = await NewDbAsync();
        var service = BuildService(db);

        var snapshot = await service.ComputeAsync("u-test", CancellationToken.None);

        Assert.Contains("no-mock", snapshot.PayloadJson);
    }
}
