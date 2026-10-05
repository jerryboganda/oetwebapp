using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Admin;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Admin;

/// <summary>
/// <c>GET /v1/admin/ops/snapshot</c> (owner programme 5 Oct 2026): the read-only load snapshot. Pins the job queue
/// roll-up (due-and-waiting vs scheduled vs processing vs stuck, history ignored), the live Speaking admission
/// counts, the Postgres-only connection block degrading cleanly elsewhere, and the remote-worker placeholder.
/// </summary>
public sealed class AdminOpsSnapshotServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly LearnerDbContext _db;
    private readonly MutableTimeProvider _clock = new(Now);
    private readonly AdminOpsSnapshotService _service;

    public AdminOpsSnapshotServiceTests()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"ops-snapshot-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);
        var admission = new SpeakingLiveAdmissionService(
            _db, Options.Create(new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 1 }), _clock);
        _service = new AdminOpsSnapshotService(_db, admission, _clock);
    }

    public void Dispose() => _db.Dispose();

    private static BackgroundJobItem Job(
        string id, JobType type, AsyncState state, DateTimeOffset availableAt, DateTimeOffset? lastTransition = null)
        => new()
        {
            Id = id,
            Type = type,
            State = state,
            CreatedAt = availableAt,
            AvailableAt = availableAt,
            LastTransitionAt = lastTransition ?? availableAt,
        };

    [Fact]
    public async Task TheJobBlock_CountsDueQueuedProcessingStuckAndScheduledByType_AndIgnoresHistory()
    {
        _db.BackgroundJobs.AddRange(
            Job("j1", JobType.WritingEvaluation, AsyncState.Queued, Now.AddSeconds(-120)),
            Job("j2", JobType.WritingEvaluation, AsyncState.Queued, Now.AddSeconds(-30)),
            Job("j3", JobType.WritingEvaluation, AsyncState.Processing, Now.AddMinutes(-20), Now.AddMinutes(-15)),
            Job("j4", JobType.SpeakingTranscription, AsyncState.Processing, Now.AddMinutes(-2), Now.AddMinutes(-1)),
            Job("j5", JobType.StudyPlanRegeneration, AsyncState.Queued, Now.AddMinutes(10)),
            Job("j6", JobType.WritingEvaluation, AsyncState.Completed, Now.AddHours(-3)),
            Job("j7", JobType.WritingEvaluation, AsyncState.Failed, Now.AddHours(-2)));
        await _db.SaveChangesAsync();

        var snapshot = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(Now, snapshot.GeneratedAt);
        Assert.Equal(2, snapshot.Jobs.TotalQueued);
        Assert.Equal(2, snapshot.Jobs.TotalProcessing);
        Assert.Equal(1, snapshot.Jobs.Scheduled);
        Assert.Equal(120, snapshot.Jobs.OldestQueuedAgeSeconds);
        Assert.Equal(2, snapshot.Jobs.ByType.Count);

        var writing = snapshot.Jobs.ByType[0];
        Assert.Equal(nameof(JobType.WritingEvaluation), writing.Type);
        Assert.Equal(2, writing.Queued);
        Assert.Equal(1, writing.Processing);
        Assert.Equal(1, writing.StuckProcessing);
        Assert.Equal(120, writing.OldestQueuedAgeSeconds);

        var speaking = snapshot.Jobs.ByType[1];
        Assert.Equal(nameof(JobType.SpeakingTranscription), speaking.Type);
        Assert.Equal(0, speaking.Queued);
        Assert.Equal(1, speaking.Processing);
        Assert.Equal(0, speaking.StuckProcessing);
        Assert.Null(speaking.OldestQueuedAgeSeconds);
    }

    [Fact]
    public async Task AnEmptyQueue_IsAnEmptyBlock_NotAnError()
    {
        var snapshot = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(0, snapshot.Jobs.TotalQueued);
        Assert.Equal(0, snapshot.Jobs.TotalProcessing);
        Assert.Equal(0, snapshot.Jobs.Scheduled);
        Assert.Null(snapshot.Jobs.OldestQueuedAgeSeconds);
        Assert.Empty(snapshot.Jobs.ByType);
    }

    [Fact]
    public async Task TheSpeakingBlock_CarriesTheDatabaseDerivedAdmissionCounts()
    {
        var admission = new SpeakingLiveAdmissionService(
            _db, Options.Create(new SpeakingLiveAdmissionOptions { DefaultMaxConcurrent = 1 }), _clock);
        await admission.AdmitOrQueueAsync("u-a", SpeakingLiveAdmissionKinds.Exam, "exam-a", true, CancellationToken.None);
        await admission.AdmitOrQueueAsync("u-b", SpeakingLiveAdmissionKinds.Practice, "practice-b", true, CancellationToken.None);

        var snapshot = await _service.GetAsync(CancellationToken.None);

        var counts = snapshot.Speaking.Admission;
        Assert.NotNull(counts);
        Assert.True(counts!.Enabled);
        Assert.Equal(1, counts.MaxConcurrent);
        Assert.Equal(1, counts.Admitted);
        Assert.Equal(1, counts.Waiting);
        Assert.Equal(0, counts.Free);
    }

    [Fact]
    public async Task OffPostgres_TheConnectionBlockSaysSo_AndTheRemoteWorkerBlockIsAPlaceholder()
    {
        var snapshot = await _service.GetAsync(CancellationToken.None);

        Assert.False(snapshot.Connections.Available);
        Assert.Null(snapshot.Connections.Total);
        Assert.Null(snapshot.Connections.MaxConnections);
        Assert.Empty(snapshot.Connections.ByApplicationName);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Connections.Note));

        Assert.False(snapshot.RemoteWorkers.Deployed);
        Assert.Equal(0, snapshot.RemoteWorkers.Nodes);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RemoteWorkers.Note));
    }
}
