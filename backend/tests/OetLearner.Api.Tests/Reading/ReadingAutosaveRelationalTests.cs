using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests.Reading;

/// <summary>
/// Reading autosave on a RELATIONAL provider (SQLite), where <c>SaveAnswerAsync</c> bumps the
/// attempt with one targeted UPDATE (<c>RowVersion = RowVersion + 1</c>) instead of a tracked
/// <c>RowVersion++</c>. The rest of the Reading suite runs on the EF in-memory provider, which has
/// no <c>ExecuteUpdate</c> and so never exercises this path. Overlap is simulated deterministically:
/// a second context bumps the row between the first service's read and its next save, which is
/// exactly the interleaving that used to surface as a 409 after the answer had been written.
/// </summary>
public sealed class ReadingAutosaveRelationalTests : IAsyncLifetime
{
    private const string UserId = "learner-autosave";
    private const string AttemptId = "att-autosave";
    private const string PaperId = "paper-autosave";
    private const string Question1 = "q1-autosave";
    private const string Question2 = "q2-autosave";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = PaperId,
            SubtestCode = "reading",
            Title = "Autosave paper",
            Slug = "autosave-paper",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            SourceProvenance = "Test",
            TagsCsv = "access:free",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingParts.Add(new ReadingPart
        {
            Id = "part-a-autosave",
            PaperId = PaperId,
            PartCode = ReadingPartCode.A,
            TimeLimitMinutes = 15,
            MaxRawScore = 20,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ReadingQuestions.AddRange(
            NewQuestion(Question1, 1, now),
            NewQuestion(Question2, 2, now));
        // Learning mode: no Part A / Part B-C gating, a long answer window, no policy needed.
        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = AttemptId,
            UserId = UserId,
            PaperId = PaperId,
            StartedAt = now.AddMinutes(-1),
            LastActivityAt = now.AddMinutes(-1),
            MaxRawScore = 42,
            Mode = ReadingAttemptMode.Learning,
            Status = ReadingAttemptStatus.InProgress,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static ReadingQuestion NewQuestion(string id, int order, DateTimeOffset now) => new()
    {
        Id = id,
        ReadingPartId = "part-a-autosave",
        DisplayOrder = order,
        QuestionType = ReadingQuestionType.ShortAnswer,
        Stem = $"Question {order}",
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static ReadingAttemptService BuildService(LearnerDbContext db)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new ReadingPolicyService(db, cache);
        var grader = new ReadingGradingService(db, policy, NullLogger<ReadingGradingService>.Instance);
        var entitlements = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        return new ReadingAttemptService(db, policy, grader, entitlements, NullLogger<ReadingAttemptService>.Instance);
    }

    private async Task<ReadingAttempt> LoadAttemptAsync()
    {
        await using var db = new LearnerDbContext(_options);
        return await db.ReadingAttempts.AsNoTracking().SingleAsync(a => a.Id == AttemptId);
    }

    [Fact]
    public async Task Autosave_bumps_the_attempt_in_one_targeted_update_and_stores_the_answer()
    {
        await using var db = new LearnerDbContext(_options);
        var service = BuildService(db);
        var before = await LoadAttemptAsync();

        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"first\"", 1_500, CancellationToken.None);

        var after = await LoadAttemptAsync();
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
        Assert.True(after.LastActivityAt > before.LastActivityAt);

        await using var verify = new LearnerDbContext(_options);
        var answer = await verify.ReadingAnswers.AsNoTracking().SingleAsync(a => a.ReadingQuestionId == Question1);
        Assert.Equal("\"first\"", answer.UserAnswerJson);
        Assert.Equal(1_500, answer.TotalElapsedMs);
    }

    [Fact]
    public async Task Overlapping_autosaves_no_longer_conflict_and_keep_both_answers()
    {
        await using var dbA = new LearnerDbContext(_options);
        var serviceA = BuildService(dbA);
        await serviceA.SaveAnswerAsync(UserId, AttemptId, Question1, "\"first\"", 1_000, CancellationToken.None);
        var afterFirst = await LoadAttemptAsync();

        // A second tab / a retry / a heartbeat bumps the row after service A read it. Context A
        // still tracks the attempt with the OLD RowVersion, so the previous tracked
        // `RowVersion++` save would now throw DbUpdateConcurrencyException (a 409) here.
        await using (var dbB = new LearnerDbContext(_options))
        {
            await dbB.ReadingAttempts
                .Where(a => a.Id == AttemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.RowVersion, a => a.RowVersion + 1));
        }

        await serviceA.SaveAnswerAsync(UserId, AttemptId, Question2, "\"second\"", 2_000, CancellationToken.None);

        var after = await LoadAttemptAsync();
        Assert.Equal(afterFirst.RowVersion + 2, after.RowVersion);

        await using var verify = new LearnerDbContext(_options);
        var answers = await verify.ReadingAnswers.AsNoTracking()
            .Where(a => a.ReadingAttemptId == AttemptId)
            .OrderBy(a => a.ReadingQuestionId)
            .ToListAsync();
        Assert.Equal(new[] { Question1, Question2 }, answers.Select(a => a.ReadingQuestionId).ToArray());
        Assert.Equal(new[] { 1_000, 2_000 }, answers.Select(a => a.TotalElapsedMs ?? 0).ToArray());
    }

    [Fact]
    public async Task Repeating_a_save_adds_elapsed_time_once_per_call_and_a_revision_only_when_the_value_changes()
    {
        await using var db = new LearnerDbContext(_options);
        var service = BuildService(db);

        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"a\"", 1_000, CancellationToken.None);
        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"b\"", 500, CancellationToken.None);
        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"b\"", 250, CancellationToken.None); // unchanged value

        await using var verify = new LearnerDbContext(_options);
        var answer = await verify.ReadingAnswers.AsNoTracking().SingleAsync(a => a.ReadingQuestionId == Question1);
        Assert.Equal("\"b\"", answer.UserAnswerJson);
        Assert.Equal(1_750, answer.TotalElapsedMs);
        var revisions = await verify.ReadingAnswerRevisions.AsNoTracking()
            .Where(r => r.ReadingQuestionId == Question1)
            .ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.Equal(3, (await LoadAttemptAsync()).RowVersion);
    }

    [Fact]
    public async Task Autosave_on_an_attempt_that_just_left_InProgress_is_rejected_and_never_touches_the_attempt()
    {
        await using var dbA = new LearnerDbContext(_options);
        var service = BuildService(dbA);
        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"first\"", null, CancellationToken.None);

        // The attempt is submitted by another request after service A loaded it (still InProgress
        // in A's tracker), so only the Status-guarded UPDATE can notice.
        await using (var dbB = new LearnerDbContext(_options))
        {
            await dbB.ReadingAttempts
                .Where(a => a.Id == AttemptId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ReadingAttemptStatus.Submitted));
        }

        var rowVersionBefore = (await LoadAttemptAsync()).RowVersion;
        var error = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            service.SaveAnswerAsync(UserId, AttemptId, Question2, "\"second\"", null, CancellationToken.None));

        // Reported as rejected (never a silent success), and the submitted attempt is untouched.
        Assert.Equal("attempt_not_in_progress", error.Code);
        var after = await LoadAttemptAsync();
        Assert.Equal(ReadingAttemptStatus.Submitted, after.Status);
        Assert.Equal(rowVersionBefore, after.RowVersion);
    }

    [Fact]
    public async Task A_submit_that_read_the_attempt_before_an_autosave_landed_conflicts_instead_of_grading_without_it()
    {
        // Draft protection. A grader / submit loaded the attempt (RowVersion N) before the
        // answer below was saved. The autosave bumps RowVersion AFTER writing the answer, so the
        // grader's own RowVersion-guarded save (ReadingGradingService.GradeAndPersistAsync does
        // `attempt.RowVersion++; SaveChanges`) must conflict rather than commit a score computed
        // without the new answer.
        await using var graderDb = new LearnerDbContext(_options);
        var graderView = await graderDb.ReadingAttempts.SingleAsync(a => a.Id == AttemptId);

        await using (var autosaveDb = new LearnerDbContext(_options))
        {
            await BuildService(autosaveDb).SaveAnswerAsync(UserId, AttemptId, Question1, "\"late answer\"", null, CancellationToken.None);
        }

        graderView.Status = ReadingAttemptStatus.Submitted;
        graderView.RowVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => graderDb.SaveChangesAsync());

        var stored = await LoadAttemptAsync();
        Assert.Equal(ReadingAttemptStatus.InProgress, stored.Status);
    }

    [Fact]
    public async Task Autosave_no_longer_writes_a_per_save_audit_event()
    {
        await using var db = new LearnerDbContext(_options);
        var service = BuildService(db);

        await service.SaveAnswerAsync(UserId, AttemptId, Question1, "\"a\"", null, CancellationToken.None);
        await service.SaveAnswerAsync(UserId, AttemptId, Question2, "\"b\"", null, CancellationToken.None);

        await using var verify = new LearnerDbContext(_options);
        Assert.False(await verify.AuditEvents.AnyAsync(e => e.Action == "ReadingAnswerSaved"));
        Assert.Equal(2, await verify.ReadingAnswerRevisions.CountAsync(r => r.ReadingAttemptId == AttemptId));
    }

    [Fact]
    public async Task Autosave_for_another_learners_attempt_is_still_not_found()
    {
        await using var db = new LearnerDbContext(_options);
        var service = BuildService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAnswerAsync("someone-else", AttemptId, Question1, "\"x\"", null, CancellationToken.None));
    }

    [Fact]
    public async Task Autosave_rejects_a_question_that_belongs_to_a_different_paper()
    {
        await using (var seed = new LearnerDbContext(_options))
        {
            var now = DateTimeOffset.UtcNow;
            seed.ContentPapers.Add(new ContentPaper
            {
                Id = "paper-other",
                SubtestCode = "reading",
                Title = "Other paper",
                Slug = "other-paper",
                AppliesToAllProfessions = true,
                Difficulty = "standard",
                EstimatedDurationMinutes = 60,
                Status = ContentStatus.Published,
                SourceProvenance = "Test",
                TagsCsv = "access:free",
                CreatedAt = now,
                UpdatedAt = now,
            });
            seed.ReadingParts.Add(new ReadingPart
            {
                Id = "part-a-other",
                PaperId = "paper-other",
                PartCode = ReadingPartCode.A,
                TimeLimitMinutes = 15,
                MaxRawScore = 20,
                CreatedAt = now,
                UpdatedAt = now,
            });
            seed.ReadingQuestions.Add(new ReadingQuestion
            {
                Id = "q-other-paper",
                ReadingPartId = "part-a-other",
                DisplayOrder = 1,
                QuestionType = ReadingQuestionType.ShortAnswer,
                Stem = "Other question",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        var error = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            BuildService(db).SaveAnswerAsync(UserId, AttemptId, "q-other-paper", "\"x\"", null, CancellationToken.None));

        Assert.Equal("question_paper_mismatch", error.Code);
    }

    [Fact]
    public async Task Autosave_rejects_malformed_answer_json_without_touching_the_attempt()
    {
        await using var db = new LearnerDbContext(_options);
        var before = await LoadAttemptAsync();

        var error = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            BuildService(db).SaveAnswerAsync(UserId, AttemptId, Question1, "{not json", null, CancellationToken.None));

        Assert.Equal("answer_json_invalid", error.Code);
        Assert.Equal(before.RowVersion, (await LoadAttemptAsync()).RowVersion);
    }
}
