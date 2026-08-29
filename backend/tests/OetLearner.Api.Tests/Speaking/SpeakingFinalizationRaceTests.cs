using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingFinalizationRaceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public SpeakingFinalizationRaceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF;";
            pragma.ExecuteNonQuery();
        }
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF;";
            pragma.ExecuteNonQuery();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Enqueue_TimerEndpointAndResultPage_ShareOneOperation()
    {
        var sessionId = await SeedSessionAsync();
        var svc = new SpeakingCanonicalAssessmentService(
            _db,
            classic: null!,
            v11: null!,
            TimeProvider.System,
            NullLogger<SpeakingCanonicalAssessmentService>.Instance);

        var first = await svc.EnqueueAsync(sessionId, default);
        var second = await svc.EnqueueAsync(sessionId, default);
        var third = await svc.EnqueueAsync(sessionId, default);

        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(first.OperationId, third.OperationId);
        Assert.True(second.AlreadyExisted);
        Assert.True(third.AlreadyExisted);
        Assert.Equal(1, await _db.AiOperations.CountAsync());
    }

    [Fact]
    public void IdentityHash_IsStableForSameInputs_AndChangesWithTranscript()
    {
        var a = SpeakingCanonicalAssessmentService.HashIdentity("s1", "c1", "t1", "r1", "p1");
        var b = SpeakingCanonicalAssessmentService.HashIdentity("s1", "c1", "t1", "r1", "p1");
        var c = SpeakingCanonicalAssessmentService.HashIdentity("s1", "c1", "t2", "r1", "p1");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private async Task<string> SeedSessionAsync()
    {
        var session = new SpeakingSession
        {
            Id = "sess-1",
            UserId = "user-s1",
            RolePlayCardId = "card-1",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.SpeakingSessions.Add(session);
        await _db.SaveChangesAsync();
        return session.Id;
    }
}
