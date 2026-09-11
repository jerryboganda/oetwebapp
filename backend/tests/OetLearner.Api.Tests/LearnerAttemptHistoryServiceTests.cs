using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression coverage for Final Developer Modification Brief item 5: the
/// Writing "Past submissions" / attempt-activity view must stay complete
/// even when a learner has 100+ more-recent attempts of other subtests.
/// GetHistoryAsync used to Take(limit) each per-table query BEFORE any
/// subtest filtering — so an unfiltered call, then a client-side filter,
/// could silently drop older Writing rows once Speaking (etc.) filled the
/// page. The fix pushes the subtest filter into each per-table query ahead
/// of its Take(limit).
/// </summary>
public sealed class LearnerAttemptHistoryServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task GetHistoryAsync_SubtestFilterAppliesBeforeLimit_OlderWritingAttemptsSurvive()
    {
        await using var db = new LearnerDbContext(_options);
        const string userId = "learner-writing-truncation";
        var now = DateTimeOffset.UtcNow;

        // 150 recent Speaking attempts — more than the requested limit (100) —
        // so a filter applied only AFTER the per-table Take(limit) would find
        // every Writing row already crowded out.
        for (var i = 0; i < 150; i++)
        {
            db.Attempts.Add(new Attempt
            {
                Id = $"speaking-{i:D3}",
                UserId = userId,
                ContentId = $"speaking-content-{i}",
                SubtestCode = "speaking",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = now.AddMinutes(-i),
            });
        }

        // 5 Writing attempts, all older than every Speaking attempt above.
        var writingIds = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var id = $"writing-{i:D3}";
            writingIds.Add(id);
            db.Attempts.Add(new Attempt
            {
                Id = id,
                UserId = userId,
                ContentId = $"writing-content-{i}",
                SubtestCode = "writing",
                Context = "practice",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = now.AddMinutes(-500 - i),
            });
        }

        await db.SaveChangesAsync();

        var service = new LearnerAttemptHistoryService(db);

        var result = await service.GetHistoryAsync(userId, limit: 100, subtest: "writing", ct: CancellationToken.None);

        Assert.Equal(5, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("writing", item.Subtest));
        Assert.Equal(
            writingIds.OrderBy(id => id, StringComparer.Ordinal),
            result.Items.Select(item => item.AttemptId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetHistoryAsync_NoSubtestFilter_StillReturnsAcrossSubtests()
    {
        await using var db = new LearnerDbContext(_options);
        const string userId = "learner-history-unfiltered";
        var now = DateTimeOffset.UtcNow;

        db.Attempts.Add(new Attempt
        {
            Id = "writing-only",
            UserId = userId,
            ContentId = "content-writing",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-2),
        });
        db.Attempts.Add(new Attempt
        {
            Id = "speaking-only",
            UserId = userId,
            ContentId = "content-speaking",
            SubtestCode = "speaking",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        var service = new LearnerAttemptHistoryService(db);

        var result = await service.GetHistoryAsync(userId, limit: 100, subtest: null, ct: CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item => item.Subtest == "writing");
        Assert.Contains(result.Items, item => item.Subtest == "speaking");
    }
}
