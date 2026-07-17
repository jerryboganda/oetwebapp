using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Tests;

public sealed class ContentAccessServiceTests : IAsyncLifetime
{
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

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_LoadsDifferentTrackCountsWithOneAggregateCommand()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentPrograms.AddRange(
            CreateProgram("program-one", displayOrder: 1),
            CreateProgram("program-two", displayOrder: 2));
        db.ContentTracks.AddRange(
            CreateTrack("track-one-a", "program-one"),
            CreateTrack("track-one-b", "program-one"),
            CreateTrack("track-two-a", "program-two"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _sql.Commands.Clear();

        var result = JsonSerializer.SerializeToElement(await CreateService(db).BrowseProgramsWithAccessAsync(
            "user-without-subscription",
            type: null,
            language: null,
            page: 1,
            pageSize: 10,
            CancellationToken.None));

        var items = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(["program-one", "program-two"], items.Select(item => item.GetProperty("Id").GetString()));
        Assert.Equal([2, 1], items.Select(item => item.GetProperty("trackCount").GetInt32()));

        var trackCommand = Assert.Single(_sql.Commands.Where(command =>
            command.Contains("ContentTracks", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("GROUP BY", trackCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT", trackCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_ReturnsZeroForPublishedProgramWithoutTracks()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentPrograms.Add(CreateProgram("program-empty", displayOrder: 1));
        await db.SaveChangesAsync();

        var result = JsonSerializer.SerializeToElement(await CreateService(db).BrowseProgramsWithAccessAsync(
            "user-without-subscription",
            type: null,
            language: null,
            page: 1,
            pageSize: 10,
            CancellationToken.None));

        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal("program-empty", item.GetProperty("Id").GetString());
        Assert.Equal(0, item.GetProperty("trackCount").GetInt32());
    }

    [Fact]
    public async Task BrowseProgramsWithAccessAsync_PreservesEmptyPageWithoutTrackQuery()
    {
        await using var db = new LearnerDbContext(_options);
        db.ContentPrograms.Add(CreateProgram("program-first-page", displayOrder: 1));
        await db.SaveChangesAsync();
        _sql.Commands.Clear();

        var result = JsonSerializer.SerializeToElement(await CreateService(db).BrowseProgramsWithAccessAsync(
            "user-without-subscription",
            type: null,
            language: null,
            page: 2,
            pageSize: 10,
            CancellationToken.None));

        Assert.Empty(result.GetProperty("items").EnumerateArray());
        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.DoesNotContain(_sql.Commands, command =>
            command.Contains("ContentTracks", StringComparison.OrdinalIgnoreCase));
    }

    private static ContentAccessService CreateService(LearnerDbContext db)
        => new(db, new ContentHierarchyService(db));

    private static ContentProgram CreateProgram(string id, int displayOrder)
        => new()
        {
            Id = id,
            Code = id,
            Title = id,
            Status = ContentStatus.Published,
            DisplayOrder = displayOrder
        };

    private static ContentTrack CreateTrack(string id, string programId)
        => new()
        {
            Id = id,
            ProgramId = programId,
            Title = id,
            Status = ContentStatus.Published
        };

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
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
