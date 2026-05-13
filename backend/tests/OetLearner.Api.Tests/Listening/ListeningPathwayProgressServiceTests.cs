using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public class ListeningPathwayProgressServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task RecomputeAsync_initializes_twelve_stage_pathway_for_new_user()
    {
        await using var db = NewDb();
        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-1", CancellationToken.None);

        var rows = await db.ListeningPathwayProgress
            .Where(row => row.UserId == "learner-1")
            .ToListAsync();

        Assert.Equal(ListeningPathwayProgressService.PathwayStages.Count, rows.Count);

        var diagnostic = rows.Single(row => row.StageCode == "diagnostic");
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, diagnostic.Status);
        Assert.Null(diagnostic.AttemptId);
        Assert.Null(diagnostic.ScaledScore);

        foreach (var stage in ListeningPathwayProgressService.PathwayStages.Skip(1))
        {
            var row = rows.Single(item => item.StageCode == stage);
            Assert.Equal(ListeningPathwayStageStatus.Locked, row.Status);
            Assert.Null(row.AttemptId);
            Assert.Null(row.ScaledScore);
        }
    }

    [Fact]
    public async Task RecomputeAsync_completes_diagnostic_and_unlocks_next_stage()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-2",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-2", CancellationToken.None);

        var diagnostic = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-2" && row.StageCode == "diagnostic");
        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-2" && row.StageCode == "foundation_partA");

        Assert.Equal(ListeningPathwayStageStatus.Completed, diagnostic.Status);
        Assert.Equal("diagnostic-attempt", diagnostic.AttemptId);
        Assert.NotNull(diagnostic.CompletedAt);
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartA.Status);
    }

    [Fact]
    public async Task RecomputeAsync_tracks_best_in_progress_attempt_score()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-3",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "learning-low",
            userId: "learner-3",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 250,
            submittedAt: now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-3", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-3" && row.StageCode == "foundation_partA");

        Assert.Equal(ListeningPathwayStageStatus.InProgress, foundationPartA.Status);
        Assert.Equal("learning-low", foundationPartA.AttemptId);
        Assert.Equal(250, foundationPartA.ScaledScore);
        Assert.Null(foundationPartA.CompletedAt);

        db.ListeningAttempts.Add(NewAttempt(
            id: "learning-improved",
            userId: "learner-3",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 290,
            submittedAt: now.AddMinutes(2)));
        await db.SaveChangesAsync();

        await service.RecomputeAsync("learner-3", CancellationToken.None);
        foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-3" && row.StageCode == "foundation_partA");

        Assert.Equal(ListeningPathwayStageStatus.InProgress, foundationPartA.Status);
        Assert.Equal("learning-improved", foundationPartA.AttemptId);
        Assert.Equal(290, foundationPartA.ScaledScore);
        Assert.Null(foundationPartA.CompletedAt);
    }

    [Fact]
    public async Task RecomputeAsync_consumes_unscoped_attempt_once_in_pathway_order()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-single-use",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "generic-learning",
            userId: "learner-single-use",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 380,
            submittedAt: now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-single-use", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-single-use" && row.StageCode == "foundation_partA");
        var foundationPartB = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-single-use" && row.StageCode == "foundation_partB");

        Assert.Equal(ListeningPathwayStageStatus.Completed, foundationPartA.Status);
        Assert.Equal("generic-learning", foundationPartA.AttemptId);
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartB.Status);
        Assert.Null(foundationPartB.AttemptId);
        Assert.Null(foundationPartB.ScaledScore);
    }

    [Fact]
    public async Task RecomputeAsync_matches_pathway_stage_scope_exactly()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-scoped",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "scoped-part-a",
            userId: "learner-scoped",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 320,
            submittedAt: now.AddMinutes(1),
            scopeJson: """{"pathwayStage":"foundation_partA"}"""));
        db.ListeningAttempts.Add(NewAttempt(
            id: "scoped-part-b",
            userId: "learner-scoped",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 330,
            submittedAt: now.AddMinutes(2),
            scopeJson: """{"pathwayStage":"foundation_partB"}"""));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-scoped", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-scoped" && row.StageCode == "foundation_partA");
        var foundationPartB = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-scoped" && row.StageCode == "foundation_partB");
        var foundationPartC = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-scoped" && row.StageCode == "foundation_partC");

        Assert.Equal(ListeningPathwayStageStatus.Completed, foundationPartA.Status);
        Assert.Equal("scoped-part-a", foundationPartA.AttemptId);
        Assert.Equal(ListeningPathwayStageStatus.Completed, foundationPartB.Status);
        Assert.Equal("scoped-part-b", foundationPartB.AttemptId);
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartC.Status);
        Assert.Null(foundationPartC.AttemptId);
    }

    [Fact]
    public async Task RecomputeAsync_accepts_legacy_stage_scope_keys()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-legacy-scope",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "legacy-stage-code",
            userId: "learner-legacy-scope",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 320,
            submittedAt: now.AddMinutes(1),
            scopeJson: """{"stageCode":"foundation_partA"}"""));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-legacy-scope", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-legacy-scope" && row.StageCode == "foundation_partA");
        var foundationPartB = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-legacy-scope" && row.StageCode == "foundation_partB");

        Assert.Equal(ListeningPathwayStageStatus.Completed, foundationPartA.Status);
        Assert.Equal("legacy-stage-code", foundationPartA.AttemptId);
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartB.Status);
        Assert.Null(foundationPartB.AttemptId);
    }

    [Fact]
    public async Task RecomputeAsync_invalid_explicit_scope_does_not_match_as_unscoped()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-invalid-scope",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "invalid-scoped-learning",
            userId: "learner-invalid-scope",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 380,
            submittedAt: now.AddMinutes(1),
            scopeJson: """{"pathwayStage":"unknown_future_stage"}"""));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-invalid-scope", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-invalid-scope" && row.StageCode == "foundation_partA");

        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartA.Status);
        Assert.Null(foundationPartA.AttemptId);
        Assert.Null(foundationPartA.ScaledScore);
    }

    [Fact]
    public async Task RecomputeAsync_non_string_explicit_scope_does_not_match_as_unscoped()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-non-string-scope",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "non-string-scoped-learning",
            userId: "learner-non-string-scope",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 380,
            submittedAt: now.AddMinutes(1),
            scopeJson: """{"pathwayStage":123}"""));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-non-string-scope", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-non-string-scope" && row.StageCode == "foundation_partA");

        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartA.Status);
        Assert.Null(foundationPartA.AttemptId);
        Assert.Null(foundationPartA.ScaledScore);
    }

    [Fact]
    public async Task RecomputeAsync_does_not_let_scoped_later_stage_backfill_earlier_stage()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(NewAttempt(
            id: "diagnostic-attempt",
            userId: "learner-later-scope",
            mode: ListeningAttemptMode.Diagnostic,
            scaledScore: null,
            submittedAt: now));
        db.ListeningAttempts.Add(NewAttempt(
            id: "scoped-part-b",
            userId: "learner-later-scope",
            mode: ListeningAttemptMode.Learning,
            scaledScore: 380,
            submittedAt: now.AddMinutes(1),
            scopeJson: """{"pathwayStage":"foundation_partB"}"""));
        await db.SaveChangesAsync();

        var service = new ListeningPathwayProgressService(db, TimeProvider.System);

        await service.RecomputeAsync("learner-later-scope", CancellationToken.None);

        var foundationPartA = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-later-scope" && row.StageCode == "foundation_partA");
        var foundationPartB = await db.ListeningPathwayProgress.SingleAsync(
            row => row.UserId == "learner-later-scope" && row.StageCode == "foundation_partB");

        Assert.Equal(ListeningPathwayStageStatus.Unlocked, foundationPartA.Status);
        Assert.Null(foundationPartA.AttemptId);
        Assert.Equal(ListeningPathwayStageStatus.Locked, foundationPartB.Status);
        Assert.Null(foundationPartB.AttemptId);
    }

    [Fact]
    public async Task RecomputeAsync_serializes_concurrent_initialization_for_same_user()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = new LearnerDbContext(options);
            var service = new ListeningPathwayProgressService(db, TimeProvider.System);
            await service.RecomputeAsync("learner-concurrent", CancellationToken.None);
        }));

        await using var verify = new LearnerDbContext(options);
        var rows = await verify.ListeningPathwayProgress
            .Where(row => row.UserId == "learner-concurrent")
            .ToListAsync();

        Assert.Equal(ListeningPathwayProgressService.PathwayStages.Count, rows.Count);
        Assert.Equal(
            ListeningPathwayProgressService.PathwayStages.Count,
            rows.Select(row => row.StageCode).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ListeningPathwayStageStatus.Unlocked, rows.Single(row => row.StageCode == "diagnostic").Status);
    }

    private static ListeningAttempt NewAttempt(
        string id,
        string userId,
        ListeningAttemptMode mode,
        int? scaledScore,
        DateTimeOffset submittedAt,
        string? scopeJson = null)
        => new()
        {
            Id = id,
            UserId = userId,
            PaperId = $"paper-{id}",
            StartedAt = submittedAt.AddMinutes(-45),
            LastActivityAt = submittedAt,
            SubmittedAt = submittedAt,
            Status = ListeningAttemptStatus.Submitted,
            Mode = mode,
            RawScore = null,
            ScaledScore = scaledScore,
            MaxRawScore = OetLearner.Api.Services.OetScoring.ListeningReadingRawMax,
            ScopeJson = scopeJson,
        };
}