using System.Collections;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests;

public sealed class MarketplaceServicePerformanceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task GetMySubmissions_UsesProjectionWithoutTrackingAndPreservesPageOutput()
    {
        await SeedAsync(approved: 0, pending: 12, inReview: 0);
        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();

        var result = await new MarketplaceService(db)
            .GetMySubmissionsAsync("contributor-user", page: 2, pageSize: 5, default);

        var items = GetItems(result);
        Assert.Equal(12, GetProperty<int>(result, "total"));
        Assert.Equal(2, GetProperty<int>(result, "page"));
        Assert.Equal(5, GetProperty<int>(result, "pageSize"));
        Assert.Equal(5, items.Count);
        Assert.Equal("Submission 06", GetProperty<string>(items[0], "title"));
        Assert.Equal("pending", GetProperty<string>(items[0], "status"));
        Assert.Equal(3, _sql.Commands.Count);
        Assert.DoesNotContain(
            "ContentPayloadJson",
            _sql.Commands.Single(command => command.Contains("ORDER BY")));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BrowseContent_UsesTwoProjectedQueriesAndPreservesFiltersAndOrdering()
    {
        await SeedAsync(approved: 9, pending: 3, inReview: 2);
        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();

        var result = await new MarketplaceService(db)
            .BrowseContentAsync("oet", "reading", "Submission", page: 1, pageSize: 4, default);

        var items = GetItems(result);
        Assert.Equal(9, GetProperty<int>(result, "total"));
        Assert.Equal(4, items.Count);
        Assert.Equal("Submission 08", GetProperty<string>(items[0], "title"));
        Assert.All(items, item => Assert.Equal("approved", GetProperty<string>(item, "status")));
        Assert.Equal(2, _sql.Commands.Count);
        Assert.DoesNotContain(
            "ContentPayloadJson",
            _sql.Commands.Single(command => command.Contains("ORDER BY")));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetPendingSubmissions_UsesTwoProjectedQueriesAndPreservesModerationOutput()
    {
        await SeedAsync(approved: 4, pending: 5, inReview: 3);
        await using var db = new LearnerDbContext(_options);
        _sql.Commands.Clear();

        var result = await new MarketplaceService(db)
            .GetPendingSubmissionsAsync(page: 1, pageSize: 20, default);

        var items = GetItems(result);
        Assert.Equal(8, GetProperty<int>(result, "total"));
        Assert.Equal(8, items.Count);
        Assert.Equal("pending", GetProperty<string>(items[0], "status"));
        Assert.Equal("Submission 04", GetProperty<string>(items[0], "title"));
        Assert.Equal(2, _sql.Commands.Count);
        Assert.DoesNotContain(
            "ContentPayloadJson",
            _sql.Commands.Single(command => command.Contains("ORDER BY")));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private async Task SeedAsync(int approved, int pending, int inReview)
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentContributors.Add(new ContentContributor
        {
            Id = "contributor",
            UserId = "contributor-user",
            DisplayName = "Contributor",
            VerificationStatus = "verified",
            CreatedAt = Now.AddYears(-1),
        });

        var statuses = Enumerable.Repeat("approved", approved)
            .Concat(Enumerable.Repeat("pending", pending))
            .Concat(Enumerable.Repeat("in_review", inReview))
            .ToArray();

        db.ContentSubmissions.AddRange(statuses.Select((status, index) => new ContentSubmission
        {
            Id = $"submission-{index:D2}",
            ContributorId = "contributor",
            ExamFamilyCode = "oet",
            SubtestCode = "reading",
            Title = $"Submission {index:D2}",
            Description = $"Description {index:D2}",
            ContentPayloadJson = new string('x', 1_000),
            ContentType = "practice_task",
            ProfessionId = "medicine",
            Difficulty = "medium",
            Tags = "sample",
            Status = status,
            ReviewedBy = status == "approved" ? "admin" : null,
            ReviewNotes = status == "approved" ? "Approved" : null,
            SubmittedAt = Now.AddMinutes(index),
            ApprovedAt = status == "approved" ? Now.AddMinutes(index) : null,
            CreatedAt = Now.AddMinutes(index),
        }));
        await db.SaveChangesAsync();
    }

    private static List<object> GetItems(object source)
    {
        var value = source.GetType().GetProperty("items")?.GetValue(source);
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().ToList();
    }

    private static T GetProperty<T>(object source, string name)
    {
        var property = source.GetType().GetProperty(name)
            ?? throw new Xunit.Sdk.XunitException($"Missing property '{name}'.");
        return Assert.IsType<T>(property.GetValue(source));
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

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
