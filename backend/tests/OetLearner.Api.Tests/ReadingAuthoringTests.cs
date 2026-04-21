using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests;

/// <summary>
/// Reading Authoring — unit tests covering the structure service, validator,
/// policy resolver, grading strategies, and the full
/// start → autosave → submit → grade lifecycle.
///
/// Everything runs on the in-memory EF provider for speed. Canonical
/// raw→scaled conversion (30/42 ≡ 350) is asserted explicitly.
/// </summary>
public class ReadingAuthoringTests
{
    private static (LearnerDbContext db, ReadingStructureService structure,
                    ReadingPolicyService policy, ReadingGradingService grader,
                    ReadingAttemptService attempt)
        Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var structure = new ReadingStructureService(db);
        var policy = new ReadingPolicyService(db, cache);
        var grader = new ReadingGradingService(db, policy, NullLogger<ReadingGradingService>.Instance);
        var attempt = new ReadingAttemptService(db, policy, grader, NullLogger<ReadingAttemptService>.Instance);
        return (db, structure, policy, grader, attempt);
    }

    private static async Task SeedPaperAsync(LearnerDbContext db, string paperId, ContentStatus status = ContentStatus.Published)
    {
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "reading",
            Title = "Reading Sample 1",
            Slug = "reading-sample-1",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = status,
            SourceProvenance = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Structure service + validator
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnsureCanonicalParts_creates_A_B_C_once()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var parts = await db.ReadingParts.Where(p => p.PaperId == "p1").ToListAsync();
        Assert.Equal(3, parts.Count);
        Assert.Contains(parts, p => p.PartCode == ReadingPartCode.A && p.MaxRawScore == 20);
        Assert.Contains(parts, p => p.PartCode == ReadingPartCode.B && p.MaxRawScore == 6);
        Assert.Contains(parts, p => p.PartCode == ReadingPartCode.C && p.MaxRawScore == 16);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_fails_empty_paper()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var report = await structure.ValidatePaperAsync("p1", default);
        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code.StartsWith("part_") && i.Code.Contains("_item_count"));
        Assert.Contains(report.Issues, i => i.Code == "total_points_mismatch");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_passes_fully_authored_paper()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        var report = await structure.ValidatePaperAsync("p1", default);
        Assert.True(report.IsPublishReady, string.Join(", ", report.Issues.Select(i => i.Message)));
        Assert.Equal(20, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(16, report.Counts.PartCCount);
        Assert.Equal(42, report.Counts.TotalPoints);
        await db.DisposeAsync();
    }

    [Fact]
    public void ValidatePayload_rejects_mismatched_mcq_shape()
    {
        // MCQ3 with 4 options should fail
        Assert.Throws<InvalidOperationException>(() =>
            ReadingStructureService.ValidateQuestionPayload(
                ReadingQuestionType.MultipleChoice3,
                optionsJson: "[\"a\",\"b\",\"c\",\"d\"]",
                correctAnswerJson: "\"A\"",
                synonymsJson: null));
    }

    [Fact]
    public void ValidatePayload_rejects_invalid_mcq_answer_letter()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReadingStructureService.ValidateQuestionPayload(
                ReadingQuestionType.MultipleChoice3,
                "[\"a\",\"b\",\"c\"]",
                "\"Z\"", null));
    }

    [Fact]
    public void ValidatePayload_accepts_shortanswer_with_synonyms()
    {
        ReadingStructureService.ValidateQuestionPayload(
            ReadingQuestionType.ShortAnswer,
            "[]",
            "\"ORT\"",
            "[\"oral rehydration\",\"oral rehydration therapy\"]");
    }

    // ════════════════════════════════════════════════════════════════════
    // Grading — canonical scoring anchor
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Canonical_anchor_30_of_42_scales_to_exactly_350()
    {
        var (db, structure, policy, grader, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        // Answer first 30 questions correctly; remaining 12 blank.
        var questions = await db.ReadingQuestions.OrderBy(q => q.ReadingPartId).ThenBy(q => q.DisplayOrder).ToListAsync();
        var correctAnswers = 0;
        foreach (var q in questions)
        {
            if (correctAnswers >= 30) break;
            var correctJson = q.CorrectAnswerJson;
            await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, q.Id, correctJson, default);
            correctAnswers++;
        }

        var result = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);
        Assert.Equal(30, result.RawScore);
        Assert.Equal(42, result.MaxRawScore);
        Assert.Equal(350, result.ScaledScore); // MISSION CRITICAL anchor
        Assert.Equal("B", result.GradeLetter);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Full_42_of_42_scales_to_500()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u2", "p1", default);
        var questions = await db.ReadingQuestions.ToListAsync();
        foreach (var q in questions)
            await attemptSvc.SaveAnswerAsync("u2", started.AttemptId, q.Id, q.CorrectAnswerJson, default);

        var result = await attemptSvc.SubmitAsync("u2", started.AttemptId, default);
        Assert.Equal(42, result.RawScore);
        Assert.Equal(500, result.ScaledScore);
        Assert.Equal("A", result.GradeLetter);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Zero_correct_scales_to_zero()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u3", "p1", default);
        var result = await attemptSvc.SubmitAsync("u3", started.AttemptId, default);
        Assert.Equal(0, result.RawScore);
        Assert.Equal(0, result.ScaledScore);
        Assert.Equal(42, result.UnansweredCount);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ShortAnswer_respects_synonyms()
    {
        var (db, structure, _, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 1, ReadingQuestionType.ShortAnswer,
            "What does ORT stand for?", "[]", "\"ORT\"",
            "[\"oral rehydration therapy\",\"oral-rehydration\"]", false, null, null), "admin", default);

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "a1", UserId = "u1", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "aa1", ReadingAttemptId = "a1", ReadingQuestionId = q.Id,
            UserAnswerJson = "\"Oral Rehydration Therapy\"",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("a1", default);
        Assert.Equal(1, result.RawScore);
        Assert.Single(result.Answers, x => x.IsCorrect);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Matching_partial_credit_awards_proportionally()
    {
        var (db, structure, _, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);

        // Matching question worth 4 points, correct answer = {1,2,3,4}.
        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 4, ReadingQuestionType.MatchingTextReference,
            "Match statements to texts", "[]", "[\"1\",\"2\",\"3\",\"4\"]",
            null, false, null, null), "admin", default);

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "a2", UserId = "u1", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress, MaxRawScore = 42, PolicySnapshotJson = "{}",
        });
        // User answers {1, 2, 5} — 2 hits out of 4 correct answers → half credit.
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "aa2", ReadingAttemptId = "a2", ReadingQuestionId = q.Id,
            UserAnswerJson = "[\"1\",\"2\",\"5\"]",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("a2", default);
        Assert.Equal(2, result.RawScore); // 4 points × 2/4
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Fuzzy_levenshtein_1_accepts_single_edit_short_answer()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 1, ReadingQuestionType.ShortAnswer,
            "Which drug was used?", "[]", "\"aspirin\"", null, false, null, null), "admin", default);
        var snapshot = (await policy.ResolveForUserAsync("u1", default)) with
        {
            ShortAnswerNormalisation = "fuzzy_levenshtein_1",
            ShortAnswerAcceptSynonyms = false,
        };

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "fuzzy-a1", UserId = "u1", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = 42,
            PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "fuzzy-ans1",
            ReadingAttemptId = "fuzzy-a1",
            ReadingQuestionId = q.Id,
            UserAnswerJson = "\"asprin\"",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("fuzzy-a1", default);
        Assert.Equal(1, result.RawScore);
        await db.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Attempt lifecycle
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Start_blocks_when_AllowMultipleConcurrentAttempts_false_and_one_open()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        await attemptSvc.StartAsync("u1", "p1", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attemptSvc.StartAsync("u1", "p1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Start_blocks_incomplete_structure_even_when_published()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.StartAsync("u1", "p1", default));
        Assert.Equal("reading_structure_invalid", ex.Code);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PartA_hard_lock_rejects_late_answer()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-16);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddMinutes(45);
        await db.SaveChangesAsync();
        var partAQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A);

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partAQuestion.Id, partAQuestion.CorrectAnswerJson, default));
        Assert.Equal("part_a_locked", ex.Code);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Start_blocks_when_user_override_BlockAttempts_true()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        await policy.UpsertUserOverrideAsync("u1", new ReadingUserPolicyOverride
        {
            UserId = "u1", BlockAttempts = true, Reason = "Under investigation",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        }, "admin", default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attemptSvc.StartAsync("u1", "p1", default));
        Assert.Contains("investigation", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Submit_is_idempotent_on_replay()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var first = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);
        var replay = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);
        Assert.Equal(first.RawScore, replay.RawScore);
        Assert.Equal(first.ScaledScore, replay.ScaledScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Extra_time_entitlement_extends_deadline()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        // 25% extra time
        await policy.UpsertUserOverrideAsync("u1", new ReadingUserPolicyOverride
        {
            UserId = "u1", ExtraTimeEntitlementPct = 25,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        }, "admin", default);

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        // Default 15 + 45 = 60 min; 25% extra → 19 + 57 = 76 min
        Assert.Equal(19, started.PartATimerMinutes);
        Assert.Equal(57, started.PartBCTimerMinutes);
        var totalExpected = TimeSpan.FromMinutes(76).Add(TimeSpan.FromSeconds(10));
        var actualDelta = started.DeadlineAt - started.StartedAt;
        Assert.True(Math.Abs((actualDelta - totalExpected).TotalSeconds) < 5);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Policy_snapshot_captured_on_attempt_start()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var reload = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        var snap = JsonSerializer.Deserialize<ReadingResolvedPolicy>(reload.PolicySnapshotJson);
        Assert.NotNull(snap);
        Assert.Equal(15, snap!.PartATimerMinutes);
        Assert.Equal(45, snap.PartBCTimerMinutes);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Grading_uses_policy_snapshot_not_live_policy()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var firstQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A);
        firstQuestion.AcceptedSynonymsJson = "[\"synonym answer\"]";
        await db.SaveChangesAsync();

        var global = await policy.GetGlobalAsync(default);
        global.ShortAnswerAcceptSynonyms = false;
        await policy.UpsertGlobalAsync(global, "admin", default);
        var started = await attemptSvc.StartAsync("u1", "p1", default);

        global.ShortAnswerAcceptSynonyms = true;
        await policy.UpsertGlobalAsync(global, "admin", default);

        await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, firstQuestion.Id, "\"synonym answer\"", default);
        var result = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);

        Assert.Equal(0, result.RawScore);
        Assert.False(result.Answers.Single(a => a.QuestionId == firstQuestion.Id).IsCorrect);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Unknown_type_fallback_awards_zero_by_default()
    {
        var (db, structure, _, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);

        // Write a "valid" MCQ3 question then corrupt its stored type after the fact
        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 1, ReadingQuestionType.MultipleChoice3,
            "X", "[\"a\",\"b\",\"c\"]", "\"A\"", null, false, null, null), "admin", default);
        var rowRaw = await db.ReadingQuestions.FirstAsync(x => x.Id == q.Id);
        rowRaw.QuestionType = (ReadingQuestionType)999; // corrupt
        await db.SaveChangesAsync();

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "aX", UserId = "u", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress, MaxRawScore = 42, PolicySnapshotJson = "{}",
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "ax", ReadingAttemptId = "aX", ReadingQuestionId = q.Id,
            UserAnswerJson = "\"A\"", AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("aX", default);
        Assert.Equal(0, result.PointsEarnedSum()); // grader returns zero for corrupt type
        await db.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private static async Task FullyAuthorPaperAsync(LearnerDbContext db, ReadingStructureService structure, string paperId)
    {
        var parts = await db.ReadingParts.Where(p => p.PaperId == paperId).ToListAsync();
        var partA = parts.First(p => p.PartCode == ReadingPartCode.A);
        var partB = parts.First(p => p.PartCode == ReadingPartCode.B);
        var partC = parts.First(p => p.PartCode == ReadingPartCode.C);

        // At least one text per part with a source
        var tA = await structure.UpsertTextAsync(new ReadingTextUpsert(
            null, partA.Id, 1, "Text 1", "BMJ", "<p>text</p>", 10, null), "admin", default);
        var tB = await structure.UpsertTextAsync(new ReadingTextUpsert(
            null, partB.Id, 1, "Text B", "NHS", "<p>text</p>", 20, null), "admin", default);
        var tC = await structure.UpsertTextAsync(new ReadingTextUpsert(
            null, partC.Id, 1, "Text C", "Lancet", "<p>text</p>", 300, null), "admin", default);

        // Part A: 20 short-answer questions
        for (var i = 1; i <= 20; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, tA.Id, i, 1, ReadingQuestionType.ShortAnswer,
                $"PA-Q{i}", "[]", $"\"ans{i}\"", null, false, null, null), "admin", default);
        }
        // Part B: 6 MCQ3
        for (var i = 1; i <= 6; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partB.Id, tB.Id, i, 1, ReadingQuestionType.MultipleChoice3,
                $"PB-Q{i}", "[\"a\",\"b\",\"c\"]", "\"B\"", null, false, null, null), "admin", default);
        }
        // Part C: 16 MCQ4
        for (var i = 1; i <= 16; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partC.Id, tC.Id, i, 1, ReadingQuestionType.MultipleChoice4,
                $"PC-Q{i}", "[\"a\",\"b\",\"c\",\"d\"]", "\"C\"", null, false, null, null), "admin", default);
        }
    }
}

file static class ReadingGradingResultExtensions
{
    public static int PointsEarnedSum(this ReadingGradingResult r) => r.Answers.Sum(a => a.PointsEarned);
}
