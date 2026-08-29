using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingDuplicateTurnTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public SpeakingDuplicateTurnTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateClientTurnId_ReturnsOriginalAndDoesNotInsertSecond()
    {
        var svc = new SpeakingPatientTurnService(_db, TimeProvider.System);
        var first = await svc.PersistAsync("sess-1", "turn-1", "patient", "hello", new { text = "hello" }, default);
        var second = await svc.PersistAsync("sess-1", "turn-1", "patient", "hello-again", new { text = "hello-again" }, default);
        var replay = await svc.TryReplayAsync("sess-1", "turn-1", default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.SpeakingPatientTurns.CountAsync());
        Assert.NotNull(replay);
        Assert.True(replay!.IsDuplicate);
        Assert.Equal(first.ResponseJson, replay.ResponseJson);
    }
}
