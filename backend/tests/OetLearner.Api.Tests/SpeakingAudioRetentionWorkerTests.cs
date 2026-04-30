using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - SpeakingAudioRetentionWorker
// must (a) clear AudioObjectKey for speaking attempts older than the
// configured retention window, (b) leave fresh attempts and non-speaking
// attempts untouched, and (c) be a no-op when retention is disabled.
[Collection("AuthFlows")]
public class SpeakingAudioRetentionWorkerTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public SpeakingAudioRetentionWorkerTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SweepOnce_ClearsExpiredSpeakingAudio_LeavesOthersUntouched()
    {
        const string staleId = "sa-retention-stale";
        const string freshId = "sa-retention-fresh";
        const string writingId = "sa-retention-writing";

        await SeedAsync(async db =>
        {
            // Old speaking attempt - should be reaped.
            db.Attempts.Add(new Attempt
            {
                Id = staleId,
                UserId = "mock-user-001",
                ContentId = "st-001",
                SubtestCode = "speaking",
                Context = "self",
                Mode = "self",
                State = AttemptState.Submitted,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-400),
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-400),
                AudioObjectKey = "audio/stale-retention.wav",
            });

            // Fresh speaking attempt - must survive.
            db.Attempts.Add(new Attempt
            {
                Id = freshId,
                UserId = "mock-user-001",
                ContentId = "st-001",
                SubtestCode = "speaking",
                Context = "self",
                Mode = "self",
                State = AttemptState.Submitted,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-3),
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-3),
                AudioObjectKey = "audio/fresh-retention.wav",
            });

            // Old writing attempt - must be ignored (worker is speaking-only).
            db.Attempts.Add(new Attempt
            {
                Id = writingId,
                UserId = "mock-user-001",
                ContentId = "wr-001",
                SubtestCode = "writing",
                Context = "self",
                Mode = "self",
                State = AttemptState.Submitted,
                StartedAt = DateTimeOffset.UtcNow.AddDays(-400),
                SubmittedAt = DateTimeOffset.UtcNow.AddDays(-400),
                AudioObjectKey = "audio/writing-shouldnt-touch.wav",
            });

            await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        var worker = ActivatorUtilities.CreateInstance<SpeakingAudioRetentionWorker>(
            scope.ServiceProvider);

        var sweptCount = await worker.SweepOnceAsync(CancellationToken.None);
        Assert.True(sweptCount >= 1);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var stale = await db.Attempts.AsNoTracking().FirstAsync(a => a.Id == staleId);
        var fresh = await db.Attempts.AsNoTracking().FirstAsync(a => a.Id == freshId);
        var writing = await db.Attempts.AsNoTracking().FirstAsync(a => a.Id == writingId);

        Assert.Null(stale.AudioObjectKey);
        Assert.Equal("audio/fresh-retention.wav", fresh.AudioObjectKey);
        Assert.Equal("audio/writing-shouldnt-touch.wav", writing.AudioObjectKey);

        // Cleanup so subsequent tests start clean.
        await CleanupAsync(staleId, freshId, writingId);
    }

    private async Task SeedAsync(Func<LearnerDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await action(db);
    }

    private async Task CleanupAsync(params string[] attemptIds)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var rows = await db.Attempts.Where(a => attemptIds.Contains(a.Id)).ToListAsync();
        if (rows.Count > 0)
        {
            db.Attempts.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
    }
}
