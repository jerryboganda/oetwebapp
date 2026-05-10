using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Reading;
using OetLearner.Api.Tests.Infrastructure;

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
        var entitlements = new ContentEntitlementService(db, new EffectiveEntitlementResolver(db));
        var attempt = new ReadingAttemptService(db, policy, grader, entitlements, NullLogger<ReadingAttemptService>.Instance);
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
            TagsCsv = "access:free",
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
    public async Task Validator_rejects_non_official_text_topology()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var parts = await db.ReadingParts.Where(p => p.PaperId == "p1").ToListAsync();
        var partA = parts.First(p => p.PartCode == ReadingPartCode.A);
        var partB = parts.First(p => p.PartCode == ReadingPartCode.B);
        var partC = parts.First(p => p.PartCode == ReadingPartCode.C);
        var tA = await structure.UpsertTextAsync(new ReadingTextUpsert(null, partA.Id, 1, "Text A", "BMJ", "<p>A</p>", 10, null), "admin", default);
        var tB = await structure.UpsertTextAsync(new ReadingTextUpsert(null, partB.Id, 1, "Text B", "NHS", "<p>B</p>", 20, null), "admin", default);
        var tC = await structure.UpsertTextAsync(new ReadingTextUpsert(null, partC.Id, 1, "Text C", "Lancet", "<p>C</p>", 300, null), "admin", default);

        for (var i = 1; i <= 20; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(null, partA.Id, tA.Id, i, 1, ReadingQuestionType.ShortAnswer, $"A{i}", "[]", $"\"a{i}\"", null, false, null, null), "admin", default);
        for (var i = 1; i <= 6; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(null, partB.Id, tB.Id, i, 1, ReadingQuestionType.MultipleChoice3, $"B{i}", "[\"a\",\"b\",\"c\"]", "\"A\"", null, false, null, null), "admin", default);
        for (var i = 1; i <= 16; i++)
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(null, partC.Id, tC.Id, i, 1, ReadingQuestionType.MultipleChoice4, $"C{i}", "[\"a\",\"b\",\"c\",\"d\"]", "\"A\"", null, false, null, null), "admin", default);
        foreach (var q in await db.ReadingQuestions.ToListAsync()) q.ReviewState = ReadingReviewState.Published;
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "part_A_text_count");
        Assert.Contains(report.Issues, i => i.Code == "part_B_text_count");
        Assert.Contains(report.Issues, i => i.Code == "part_C_text_count");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_rejects_wrong_question_type_for_part()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partB = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.B);
        var firstB = await db.ReadingQuestions.FirstAsync(q => q.ReadingPartId == partB.Id && q.DisplayOrder == 1);
        firstB.QuestionType = ReadingQuestionType.MultipleChoice4;
        firstB.OptionsJson = "[\"a\",\"b\",\"c\",\"d\"]";
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "part_B_question_type" && i.TargetId == firstB.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_rejects_part_a_questions_without_text_links()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var partAQuestions = await db.ReadingQuestions
            .Where(question => question.ReadingPartId == partA.Id)
            .ToListAsync();
        foreach (var question in partAQuestions)
        {
            question.ReadingTextId = null;
        }
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, issue => issue.Code == "part_A_no_texts" && issue.Severity == "error");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_rejects_individual_part_a_question_missing_text_link()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var question = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .FirstAsync();
        question.ReadingTextId = null;
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, issue =>
            issue.Code == "part_A_question_text_required" && issue.TargetId == question.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validator_rejects_non_official_time_limit()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partC = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.C);
        partC.TimeLimitMinutes = 30;
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "part_C_time_limit" && i.TargetId == partC.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Manifest_import_replaces_structure_and_preserves_text_links()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "source");
        await SeedPaperAsync(db, "target");
        await structure.EnsureCanonicalPartsAsync("source", default);
        await structure.EnsureCanonicalPartsAsync("target", default);
        await FullyAuthorPaperAsync(db, structure, "source");

        var manifest = await structure.ExportManifestAsync("source", default);
        var result = await structure.ImportManifestAsync("target", manifest, true, "admin", default);

        Assert.True(result.Report.IsPublishReady, string.Join(", ", result.Report.Issues.Select(i => i.Message)));
        Assert.Equal(20, result.Report.Counts.PartACount);
        Assert.Equal(6, result.Report.Counts.PartBCount);
        Assert.Equal(16, result.Report.Counts.PartCCount);
        Assert.Equal(42, result.Report.Counts.TotalPoints);

        var imported = await structure.ExportManifestAsync("target", default);
        var partA = imported.Parts.Single(p => p.PartCode == ReadingPartCode.A);
        Assert.Equal(20, partA.Questions.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, partA.Questions
            .Select(q => q.ReadingTextDisplayOrder ?? 0)
            .Distinct()
            .OrderBy(x => x));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Reading_home_safe_drills_include_unanswered_skills_and_prefer_unattempted_paper()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await SeedPaperAsync(db, "p2");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await structure.EnsureCanonicalPartsAsync("p2", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        await FullyAuthorPaperAsync(db, structure, "p2");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        await attemptSvc.SubmitAsync("u1", started.AttemptId, default);

        var readyPapers = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == "p1" || p.Id == "p2")
            .OrderBy(p => p.Id)
            .ToListAsync();
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == "p1" || p.PaperId == "p2")
            .Include(p => p.Questions)
            .ToListAsync();
        var partsByPaper = parts.GroupBy(p => p.PaperId).ToDictionary(g => g.Key, g => g.ToList());
        var attempts = await db.ReadingAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .Where(a => a.UserId == "u1")
            .ToListAsync();
        var titles = readyPapers.ToDictionary(p => p.Id, p => p.Title);
        var resolvedPolicy = await policy.ResolveForUserAsync("u1", default);

        var method = typeof(LearnerEndpoints).GetMethod(
            "BuildReadingSafeDrills",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var actions = (IReadOnlyList<object>)method!.Invoke(null, new object[]
        {
            readyPapers,
            attempts,
            partsByPaper,
            titles,
            resolvedPolicy,
        })!;
        var json = JsonSerializer.Serialize(actions, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("missed or unanswered item", json);
        Assert.Contains("/reading/paper/p2", json);
        Assert.DoesNotContain("CorrectAnswerJson", json);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Reading_home_does_not_project_subset_practice_as_oet_result()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var scopedQuestions = await db.ReadingQuestions
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.B)
            .OrderBy(q => q.DisplayOrder)
            .Take(2)
            .Select(q => new { q.Id, q.CorrectAnswerJson })
            .ToListAsync();
        var submittedScope = JsonSerializer.Serialize(new
        {
            kind = "drill",
            minutes = 8,
            questionIds = scopedQuestions.Select(q => q.Id).ToArray(),
        });
        var submitted = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.Drill, submittedScope, default);
        await attemptSvc.SaveAnswerAsync("u1", submitted.AttemptId, scopedQuestions[0].Id, scopedQuestions[0].CorrectAnswerJson, default);
        await attemptSvc.SubmitAsync("u1", submitted.AttemptId, default);

        var activeScope = JsonSerializer.Serialize(new
        {
            kind = "mini-test",
            minutes = 5,
            questionIds = new[] { scopedQuestions[1].Id },
        });
        var active = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.MiniTest, activeScope, default);

        var method = typeof(LearnerEndpoints).GetMethod(
            "GetStructuredReadingHomeAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task<object>)method!.Invoke(null, new object[]
        {
            "u1",
            db,
            policy,
            new ContentEntitlementService(db, new EffectiveEntitlementResolver(db)),
            CancellationToken.None,
        })!;
        var home = await task;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(home, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Empty(doc.RootElement.GetProperty("recentResults").EnumerateArray());

        var paper = doc.RootElement.GetProperty("papers")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "p1");
        Assert.Equal(JsonValueKind.Null, paper.GetProperty("lastAttempt").ValueKind);

        var activeAttempt = doc.RootElement.GetProperty("activeAttempts")
            .EnumerateArray()
            .Single(item => item.GetProperty("attemptId").GetString() == active.AttemptId);
        Assert.Equal("MiniTest", activeAttempt.GetProperty("mode").GetString());
        Assert.Equal(1, activeAttempt.GetProperty("totalQuestions").GetInt32());
        Assert.Equal(
            activeAttempt.GetProperty("partADeadlineAt").GetDateTimeOffset(),
            activeAttempt.GetProperty("partBCDeadlineAt").GetDateTimeOffset());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Manifest_json_serializes_reading_enums_as_strings()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "source");
        await structure.EnsureCanonicalPartsAsync("source", default);
        await FullyAuthorPaperAsync(db, structure, "source");

        var manifest = await structure.ExportManifestAsync("source", default);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"partCode\":\"A\"", json);
        Assert.Contains("\"questionType\":\"ShortAnswer\"", json);
        var roundTrip = JsonSerializer.Deserialize<ReadingStructureManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(ReadingPartCode.A, roundTrip!.Parts[0].PartCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Manifest_replace_blocks_when_attempts_exist()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "source");
        await SeedPaperAsync(db, "target");
        await structure.EnsureCanonicalPartsAsync("source", default);
        await structure.EnsureCanonicalPartsAsync("target", default);
        await FullyAuthorPaperAsync(db, structure, "source");
        await FullyAuthorPaperAsync(db, structure, "target");
        var manifest = await structure.ExportManifestAsync("source", default);
        await attemptSvc.StartAsync("u1", "target", default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            structure.ImportManifestAsync("target", manifest, true, "admin", default));

        Assert.Contains("learner attempts", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Authoring_writes_reject_wrong_subtest_and_cross_part_text_link()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "reading");
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "listening",
            SubtestCode = "listening",
            Title = "Listening Sample",
            Slug = "listening-sample",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 40,
            Status = ContentStatus.Published,
            SourceProvenance = "Test",
            TagsCsv = "access:free",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var wrongSubtest = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            structure.EnsureCanonicalPartsAsync("listening", default));
        Assert.Contains("expected 'reading'", wrongSubtest.Message);

        await structure.EnsureCanonicalPartsAsync("reading", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "reading" && p.PartCode == ReadingPartCode.A);
        var partB = await db.ReadingParts.FirstAsync(p => p.PaperId == "reading" && p.PartCode == ReadingPartCode.B);
        var textA = await structure.UpsertTextAsync(new ReadingTextUpsert(
            null, partA.Id, 1, "Part A text", "Source", "<p>Text</p>", 20, null), "admin", default);

        var crossPart = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partB.Id, textA.Id, 1, 1, ReadingQuestionType.MultipleChoice3,
                "Question", "[\"a\",\"b\",\"c\"]", "\"A\"", null, false, null, null), "admin", default));
        Assert.Contains("same part", crossPart.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Authoring_upserts_reject_foreign_existing_row_ids()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "paper-a");
        await SeedPaperAsync(db, "paper-b");
        await structure.EnsureCanonicalPartsAsync("paper-a", default);
        await structure.EnsureCanonicalPartsAsync("paper-b", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "paper-a" && p.PartCode == ReadingPartCode.A);
        var partB = await db.ReadingParts.FirstAsync(p => p.PaperId == "paper-b" && p.PartCode == ReadingPartCode.A);
        var textA = await structure.UpsertTextAsync(new ReadingTextUpsert(
            null, partA.Id, 1, "Paper A text", "Source", "<p>Text</p>", 20, null), "admin", default);
        var questionA = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, textA.Id, 1, 1, ReadingQuestionType.ShortAnswer,
            "Paper A question", "[]", "\"answer\"", null, false, null, null), "admin", default);

        var textMove = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            structure.UpsertTextAsync(new ReadingTextUpsert(
                textA.Id, partB.Id, 1, "Moved text", "Source", "<p>Text</p>", 20, null), "admin", default));
        Assert.Contains("different part", textMove.Message);

        var questionMove = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                questionA.Id, partB.Id, null, 1, 1, ReadingQuestionType.ShortAnswer,
                "Moved question", "[]", "\"answer\"", null, false, null, null), "admin", default));
        Assert.Contains("different part", questionMove.Message);

        var unchangedText = await db.ReadingTexts.FirstAsync(t => t.Id == textA.Id);
        var unchangedQuestion = await db.ReadingQuestions.FirstAsync(q => q.Id == questionA.Id);
        Assert.Equal(partA.Id, unchangedText.ReadingPartId);
        Assert.Equal(partA.Id, unchangedQuestion.ReadingPartId);
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
        var partAQuestions = await db.ReadingQuestions
            .Include(q => q.Part)
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync();
        foreach (var q in partAQuestions)
        {
            await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, q.Id, q.CorrectAnswerJson, default);
        }
        await ResumeExamPartBCAsync(db, attemptSvc, "u1", started.AttemptId);
        var partBCQuestions = await db.ReadingQuestions
            .Include(q => q.Part)
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode != ReadingPartCode.A)
            .OrderBy(q => q.Part!.PartCode)
            .ThenBy(q => q.DisplayOrder)
            .Take(10)
            .ToListAsync();
        foreach (var q in partBCQuestions)
        {
            await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, q.Id, q.CorrectAnswerJson, default);
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
        var partAQuestions = await db.ReadingQuestions
            .Include(q => q.Part)
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .ToListAsync();
        foreach (var q in partAQuestions)
            await attemptSvc.SaveAnswerAsync("u2", started.AttemptId, q.Id, q.CorrectAnswerJson, default);
        await ResumeExamPartBCAsync(db, attemptSvc, "u2", started.AttemptId);
        var partBCQuestions = await db.ReadingQuestions
            .Include(q => q.Part)
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode != ReadingPartCode.A)
            .OrderBy(q => q.Part!.PartCode)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync();
        foreach (var q in partBCQuestions)
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
    public async Task PartA_short_answer_rejects_synonyms_even_when_policy_enabled()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 1, ReadingQuestionType.ShortAnswer,
            "What does ORT stand for?", "[]", "\"ORT\"",
            "[\"oral rehydration therapy\",\"oral-rehydration\"]", false, null, null), "admin", default);

        // Synonym acceptance is OFF by default (OET-faithful). This test
        // explicitly opts in via the policy snapshot — non-standard mode.
        var snapshot = (await policy.ResolveForUserAsync("u1", default)) with
        {
            ShortAnswerAcceptSynonyms = true,
        };

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "a1", UserId = "u1", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = 42,
            PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "aa1", ReadingAttemptId = "a1", ReadingQuestionId = q.Id,
            UserAnswerJson = "\"oral rehydration therapy\"",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("a1", default);
        Assert.Equal(0, result.RawScore);
        Assert.DoesNotContain(result.Answers, x => x.IsCorrect);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PartA_short_answer_stays_strict_in_drill_mode()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var question = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 8, 1, ReadingQuestionType.ShortAnswer,
            "What does ORT stand for?", "[]", "\"ORT\"",
            "[\"oral rehydration therapy\",\"oral-rehydration\"]", false, null, null), "admin", default);

        var snapshot = (await policy.ResolveForUserAsync("u1", default)) with
        {
            ShortAnswerAcceptSynonyms = true,
        };

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "drill-a1",
            UserId = "u1",
            PaperId = "p1",
            Mode = ReadingAttemptMode.Drill,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = 1,
            ScopeJson = JsonSerializer.Serialize(new { kind = "drill", questionIds = new[] { question.Id } }),
            PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "drill-answer-a1",
            ReadingAttemptId = "drill-a1",
            ReadingQuestionId = question.Id,
            UserAnswerJson = "\"oral rehydration therapy\"",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("drill-a1", default);

        Assert.Equal(0, result.RawScore);
        Assert.DoesNotContain(result.Answers, answer => answer.IsCorrect);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PartA_matching_rejects_array_answers_even_when_partial_credit_enabled()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        var partA = await db.ReadingParts.FirstAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);

        var q = await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
            null, partA.Id, null, 1, 1, ReadingQuestionType.MatchingTextReference,
            "Match statement to text", "[]", "\"A\"",
            null, false, null, null), "admin", default);
        var snapshot = (await policy.ResolveForUserAsync("u1", default)) with { MatchingAllowPartialCredit = true };

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "a2", UserId = "u1", PaperId = "p1",
            StartedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress, MaxRawScore = 42, PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        db.ReadingAnswers.Add(new ReadingAnswer
        {
            Id = "aa2", ReadingAttemptId = "a2", ReadingQuestionId = q.Id,
            UserAnswerJson = "[\"A\"]",
            AnsweredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("a2", default);
        Assert.Equal(0, result.RawScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task PartA_short_answer_rejects_fuzzy_single_edit_policy()
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
        Assert.Equal(0, result.RawScore);
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
    public async Task Start_blocks_non_published_paper()
    {
        var (db, _, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1", ContentStatus.Draft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attemptSvc.StartAsync("u1", "p1", default));

        Assert.Equal("Paper not found.", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Start_blocks_cross_profession_paper()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        var paper = await db.ContentPapers.FirstAsync(p => p.Id == "p1");
        paper.AppliesToAllProfessions = false;
        paper.ProfessionId = "nursing";
        db.Users.Add(new LearnerUser
        {
            Id = "u1",
            DisplayName = "Learner One",
            Email = "u1@example.test",
            ActiveProfessionId = "medicine",
            AccountStatus = "active",
            Timezone = "UTC",
            Locale = "en-AU",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attemptSvc.StartAsync("u1", "p1", default));

        Assert.Equal("Paper not found.", ex.Message);
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
    public async Task Exam_rejects_BC_answers_until_part_a_break_is_resumed()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var partBQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.B);

        var early = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default));
        Assert.Equal("part_bc_not_open", early.Code);

        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-16);
        attempt.PartBCTimerPausedAt = attempt.StartedAt.AddMinutes(15);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddHours(1);
        await db.SaveChangesAsync();

        var paused = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default));
        Assert.Equal("part_bc_break_not_resumed", paused.Code);

        await attemptSvc.ResumePartABreakAsync("u1", started.AttemptId, default);
        await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default);
        Assert.Single(await db.ReadingAnswers.Where(a => a.ReadingAttemptId == started.AttemptId).ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Exam_allows_BC_answer_and_submit_after_part_a_break_window_expires_without_resume()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var partBQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.B);
        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-65);
        attempt.PartBCTimerPausedAt = attempt.StartedAt.AddMinutes(15);
        attempt.PartABreakUsed = false;
        attempt.PartBCPausedSeconds = 0;
        attempt.DeadlineAt = attempt.StartedAt
            .AddMinutes(70)
            .AddSeconds(10);
        await db.SaveChangesAsync();

        await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default);
        var result = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);

        Assert.Equal(1, result.RawScore);
        Assert.Equal(ReadingAttemptStatus.Submitted, attempt.Status);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Optional_break_extends_only_BC_deadline_and_never_reopens_part_a()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        attempt.PartBCTimerPausedAt = attempt.StartedAt.AddMinutes(15);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddHours(1);
        await db.SaveChangesAsync();

        var resumed = await attemptSvc.ResumePartABreakAsync("u1", started.AttemptId, default);
        Assert.Equal(attempt.StartedAt.AddMinutes(15), resumed.PartADeadlineAt, TimeSpan.FromSeconds(1));
        Assert.InRange(resumed.PartBCPausedSeconds, 299, 301);
        Assert.Equal(resumed.PartBCDeadlineAt.AddSeconds(10), resumed.DeadlineAt, TimeSpan.FromSeconds(1));

        var partAQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A);
        var locked = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partAQuestion.Id, partAQuestion.CorrectAnswerJson, default));
        Assert.Equal("part_a_locked", locked.Code);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Answer_window_rejects_BC_save_before_grace_deadline()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-71);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddSeconds(5);
        await db.SaveChangesAsync();
        var partBQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.B);

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default));
        Assert.Equal("answer_window_closed", ex.Code);
        Assert.Equal(ReadingAttemptStatus.InProgress, attempt.Status);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Submit_grace_still_grades_after_answer_window_closes()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartAsync("u1", "p1", default);
        var partBQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.B);
        await ResumeExamPartBCAsync(db, attemptSvc, "u1", started.AttemptId);
        await attemptSvc.SaveAnswerAsync("u1", started.AttemptId, partBQuestion.Id, partBQuestion.CorrectAnswerJson, default);

        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == started.AttemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-61);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddSeconds(5);
        await db.SaveChangesAsync();

        var result = await attemptSvc.SubmitAsync("u1", started.AttemptId, default);
        Assert.Equal(1, result.RawScore);
        Assert.Equal(ReadingAttemptStatus.Submitted, attempt.Status);
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
        var totalExpected = TimeSpan.FromMinutes(86).Add(TimeSpan.FromSeconds(10));
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
    public async Task PartA_grading_remains_strict_after_live_policy_softening_attempt()
    {
        var (db, structure, policy, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var firstQuestion = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstAsync(q => q.Part!.PaperId == "p1"
                && q.Part.PartCode == ReadingPartCode.A
                && q.QuestionType == ReadingQuestionType.ShortAnswer);
        var started = await attemptSvc.StartAsync("u1", "p1", default);

        firstQuestion.AcceptedSynonymsJson = "[\"synonym answer\"]";
        await db.SaveChangesAsync();

        var global = await policy.GetGlobalAsync(default);
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

    [Fact]
    public async Task Admin_reading_analytics_aggregates_parts_skills_and_hardest_questions()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partA = await db.ReadingParts.SingleAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.A);
        var partB = await db.ReadingParts.SingleAsync(p => p.PaperId == "p1" && p.PartCode == ReadingPartCode.B);
        var qA = await db.ReadingQuestions.FirstAsync(q => q.ReadingPartId == partA.Id && q.DisplayOrder == 1);
        var qB = await db.ReadingQuestions.FirstAsync(q => q.ReadingPartId == partB.Id && q.DisplayOrder == 1);
        qA.SkillTag = "Skimming";
        qB.SkillTag = "Inference";

        db.ReadingAttempts.AddRange(
            new ReadingAttempt
            {
                Id = "a1",
                UserId = "u1",
                PaperId = "p1",
                StartedAt = DateTimeOffset.UtcNow.AddDays(-31),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Status = ReadingAttemptStatus.Submitted,
                RawScore = 30,
                ScaledScore = OetScoring.OetRawToScaled(30),
                MaxRawScore = 42,
                PolicySnapshotJson = "{}",
            },
            new ReadingAttempt
            {
                Id = "a2",
                UserId = "u2",
                PaperId = "p1",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-80),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-20),
                Status = ReadingAttemptStatus.Submitted,
                RawScore = 20,
                ScaledScore = OetScoring.OetRawToScaled(20),
                MaxRawScore = 42,
                PolicySnapshotJson = "{}",
            },
            new ReadingAttempt
            {
                Id = "a4",
                UserId = "u4",
                PaperId = "p1",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-25),
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-25),
                Status = ReadingAttemptStatus.Submitted,
                RawScore = 1,
                ScaledScore = null,
                MaxRawScore = 1,
                PolicySnapshotJson = "{}",
                Mode = ReadingAttemptMode.Drill,
                ScopeJson = JsonSerializer.Serialize(new { kind = "drill", questionIds = new[] { qA.Id }, label = "Skimming drill" }),
            },
            new ReadingAttempt
            {
                Id = "a5",
                UserId = "u5",
                PaperId = "p1",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-28),
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-27),
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-27),
                Status = ReadingAttemptStatus.Submitted,
                RawScore = 1,
                ScaledScore = null,
                MaxRawScore = 1,
                PolicySnapshotJson = "{}",
                Mode = ReadingAttemptMode.Drill,
                ScopeJson = "[]",
            },
            new ReadingAttempt
            {
                Id = "a3",
                UserId = "u3",
                PaperId = "p1",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                Status = ReadingAttemptStatus.InProgress,
                MaxRawScore = 42,
                PolicySnapshotJson = "{}",
            });

        db.ReadingAnswers.AddRange(
            new ReadingAnswer { Id = "ans-1", ReadingAttemptId = "a1", ReadingQuestionId = qA.Id, UserAnswerJson = "\"ans1\"", IsCorrect = true, PointsEarned = 1, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-6) },
            new ReadingAnswer { Id = "ans-2", ReadingAttemptId = "a1", ReadingQuestionId = qB.Id, UserAnswerJson = "\"A\"", IsCorrect = false, PointsEarned = 0, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-6) },
            new ReadingAnswer { Id = "ans-3", ReadingAttemptId = "a2", ReadingQuestionId = qA.Id, UserAnswerJson = "\"wrong\"", IsCorrect = false, PointsEarned = 0, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-21) },
            new ReadingAnswer { Id = "ans-4", ReadingAttemptId = "a2", ReadingQuestionId = qB.Id, UserAnswerJson = "\"A\"", IsCorrect = false, PointsEarned = 0, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-21) },
            new ReadingAnswer { Id = "ans-5", ReadingAttemptId = "a4", ReadingQuestionId = qA.Id, UserAnswerJson = "\"ans1\"", IsCorrect = true, PointsEarned = 1, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-26) },
            new ReadingAnswer { Id = "ans-6", ReadingAttemptId = "a5", ReadingQuestionId = qA.Id, UserAnswerJson = "\"ans1\"", IsCorrect = true, PointsEarned = 1, AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-27) });
        await db.SaveChangesAsync();

        var analytics = await ReadingAnalyticsAdminEndpoints.BuildAnalyticsAsync(db, 30, default);

        Assert.Equal(1, analytics.Summary.TotalPapers);
        Assert.Equal(1, analytics.Summary.ExamReadyPapers);
        Assert.Equal(5, analytics.Summary.TotalAttempts);
        Assert.Equal(4, analytics.Summary.SubmittedAttempts);
        Assert.Equal(1, analytics.Summary.ActiveAttempts);
        Assert.Equal(50, analytics.Summary.PassRatePercent);
        Assert.Contains(analytics.PartBreakdown, p => p.PartCode == "B" && p.Opportunities == 12 && p.AccuracyPercent == 0);
        Assert.Contains(analytics.SkillBreakdown, s => s.Label == "Inference" && s.AccuracyPercent == 0);
        Assert.Contains(analytics.SkillBreakdown, s => s.Label == "Skimming" && s.Opportunities == 4 && s.CorrectCount == 3);
        Assert.Equal(qB.Id, analytics.HardestQuestions.First().QuestionId);
        Assert.Equal(2, analytics.HardestQuestions.First().Opportunities);
        Assert.Contains(analytics.ActionInsights, insight => insight.Id == "part_b");
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

        var textsA = new List<ReadingText>();
        var textsB = new List<ReadingText>();
        var textsC = new List<ReadingText>();
        for (var i = 1; i <= 4; i++)
        {
            textsA.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partA.Id, i, $"Text A{i}", "BMJ", "<p>text</p>", 10, null), "admin", default));
        }
        for (var i = 1; i <= 6; i++)
        {
            textsB.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partB.Id, i, $"Extract B{i}", "NHS", "<p>text</p>", 20, null), "admin", default));
        }
        for (var i = 1; i <= 2; i++)
        {
            textsC.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partC.Id, i, $"Text C{i}", "Lancet", "<p>text</p>", 300, null), "admin", default));
        }

        // Part A: Q1-7 matching, Q8-14 short answer, Q15-20 sentence completion.
        for (var i = 1; i <= 7; i++)
        {
            var answer = ((char)('A' + ((i - 1) % 4))).ToString();
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.MatchingTextReference,
                $"PA-Q{i}", "[]", $"\"{answer}\"", null, false, null, null), "admin", default);
        }
        for (var i = 8; i <= 14; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.ShortAnswer,
                $"PA-Q{i}", "[]", $"\"ans{i}\"", null, false, null, null), "admin", default);
        }
        for (var i = 15; i <= 20; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.SentenceCompletion,
                $"PA-Q{i}", "[]", $"\"ans{i}\"", null, false, null, null), "admin", default);
        }
        // Part B: 6 MCQ3
        for (var i = 1; i <= 6; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partB.Id, textsB[i - 1].Id, i, 1, ReadingQuestionType.MultipleChoice3,
                $"PB-Q{i}", "[\"a\",\"b\",\"c\"]", "\"B\"", null, false, null, null), "admin", default);
        }
        // Part C: 16 MCQ4
        for (var i = 1; i <= 16; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partC.Id, textsC[(i - 1) / 8].Id, i, 1, ReadingQuestionType.MultipleChoice4,
                $"PC-Q{i}", "[\"a\",\"b\",\"c\",\"d\"]", "\"C\"", null, false, null, null), "admin", default);
        }

        // Phase 4 — fast-forward all newly authored questions to Published so
        // tests that exercise the publish gate keep working without having
        // to drive the full review-state lifecycle for every question.
        var partIds = parts.Select(p => p.Id).ToList();
        var questions = await db.ReadingQuestions
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync();
        foreach (var q in questions)
        {
            q.ReviewState = ReadingReviewState.Published;
        }
        await db.SaveChangesAsync();
    }

    private static async Task ResumeExamPartBCAsync(
        LearnerDbContext db,
        ReadingAttemptService attemptSvc,
        string userId,
        string attemptId)
    {
        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == attemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-16);
        attempt.PartABreakUsed = false;
        attempt.PartBCPausedSeconds = 0;
        attempt.PartBCTimerPausedAt = attempt.StartedAt.AddMinutes(15);
        attempt.DeadlineAt = DateTimeOffset.UtcNow.AddHours(1);
        await db.SaveChangesAsync();
        await attemptSvc.ResumePartABreakAsync(userId, attemptId, default);
    }

    // ════════════════════════════════════════════════════════════════════
    // Phase 3 — Practice Mode + Error Bank
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Learning_mode_attempt_can_run_alongside_exam_attempt()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var examStart = await attemptSvc.StartAsync("u1", "p1", default);
        // Concurrency cap should NOT block a Learning-mode attempt.
        var learningStart = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.Learning, scopeJson: null, default);

        Assert.NotEqual(examStart.AttemptId, learningStart.AttemptId);
        var learning = await db.ReadingAttempts.FindAsync(learningStart.AttemptId);
        Assert.NotNull(learning);
        Assert.Equal(ReadingAttemptMode.Learning, learning!.Mode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Learning_mode_does_not_hard_lock_part_a_after_15_minutes()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var started = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.Learning, scopeJson: null, default);

        // Backdate StartedAt by 30 minutes — would lock Part A in Exam mode.
        var attempt = await db.ReadingAttempts.FindAsync(started.AttemptId);
        Assert.NotNull(attempt);
        attempt!.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await db.SaveChangesAsync();

        var partAQuestion = await db.ReadingQuestions
            .Where(q => q.Part!.PaperId == "p1" && q.Part.PartCode == ReadingPartCode.A)
            .FirstAsync();

        // Exam mode would throw "part_a_locked"; Learning mode should allow it.
        await attemptSvc.SaveAnswerAsync(
            "u1", started.AttemptId, partAQuestion.Id, "\"A\"", default);

        var saved = await db.ReadingAnswers
            .CountAsync(a => a.ReadingAttemptId == started.AttemptId);
        Assert.Equal(1, saved);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Error_bank_records_wrong_answers_on_submit_and_resolves_on_correct_retry()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        // First attempt — submit a wrong answer for one Part B question.
        var first = await attemptSvc.StartAsync("u1", "p1", default);
        var pbQ = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.B)
            .FirstAsync();
        await ResumeExamPartBCAsync(db, attemptSvc, "u1", first.AttemptId);
        await attemptSvc.SaveAnswerAsync("u1", first.AttemptId, pbQ.Id, "\"X\"", default);
        await attemptSvc.SubmitAsync("u1", first.AttemptId, default);

        var entry = await db.ReadingErrorBankEntries
            .FirstOrDefaultAsync(e => e.UserId == "u1" && e.ReadingQuestionId == pbQ.Id);
        Assert.NotNull(entry);
        Assert.False(entry!.IsResolved);
        Assert.Equal(1, entry.TimesWrong);
        Assert.Equal(ReadingPartCode.B, entry.PartCode);

        // Second attempt — answer correctly. Helper authored Part B as "B".
        var second = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.Learning, scopeJson: null, default);
        await attemptSvc.SaveAnswerAsync("u1", second.AttemptId, pbQ.Id, "\"B\"", default);
        await attemptSvc.SubmitAsync("u1", second.AttemptId, default);

        var resolved = await db.ReadingErrorBankEntries
            .FirstOrDefaultAsync(e => e.UserId == "u1" && e.ReadingQuestionId == pbQ.Id);
        Assert.NotNull(resolved);
        Assert.True(resolved!.IsResolved);
        Assert.Equal("answered_correctly", resolved.ResolvedReason);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Error_bank_increments_times_wrong_on_repeated_misses()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var pbQ = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.B)
            .FirstAsync();

        for (var i = 0; i < 2; i++)
        {
            var run = await attemptSvc.StartInModeAsync(
                "u1", "p1", ReadingAttemptMode.Learning, scopeJson: null, default);
            await attemptSvc.SaveAnswerAsync("u1", run.AttemptId, pbQ.Id, "\"X\"", default);
            await attemptSvc.SubmitAsync("u1", run.AttemptId, default);
        }

        var entry = await db.ReadingErrorBankEntries
            .FirstAsync(e => e.UserId == "u1" && e.ReadingQuestionId == pbQ.Id);
        Assert.False(entry.IsResolved);
        Assert.Equal(2, entry.TimesWrong);
        await db.DisposeAsync();
    }

    // ── Phase 3b: Drill / MiniTest / ErrorBank subset attempts ───────────

    [Fact]
    public async Task Drill_attempt_grades_only_in_scope_questions_and_skips_scaled_score()
    {
        var (db, structure, _, grader, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        // Pick three Part-A questions to scope a drill against. Authored
        // correct answer for Part A = "ans{i}".
        var partAQuestions = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .Take(3)
            .Select(q => new { q.Id, q.CorrectAnswerJson })
            .ToListAsync();
        var partAIds = partAQuestions.Select(q => q.Id).ToList();

        var scope = JsonSerializer.Serialize(new { kind = "drill", questionIds = partAIds });
        var run = await attemptSvc.StartInModeAsync("u1", "p1", ReadingAttemptMode.Drill, scope, default);

        // Answer two correctly, one wrong.
        var idx = 0;
        foreach (var q in partAQuestions)
        {
            var answer = idx == 2 ? "\"WRONG\"" : q.CorrectAnswerJson;
            await attemptSvc.SaveAnswerAsync("u1", run.AttemptId, q.Id, answer, default);
            idx++;
        }
        var result = await grader.GradeAttemptAsync(run.AttemptId, default);

        // Grader must only count the 3 in-scope questions and skip OET 0-500
        // conversion (scaled is null because this is practice-only).
        Assert.Equal(3, result.MaxRawScore);
        Assert.Equal(2, result.RawScore);
        Assert.Equal(2, result.CorrectCount);
        Assert.Equal(1, result.IncorrectCount);
        Assert.Equal(0, result.UnansweredCount);
        Assert.Null(result.ScaledScore);
        Assert.Equal("—", result.GradeLetter);

        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == run.AttemptId);
        Assert.Null(attempt.ScaledScore);
        Assert.Equal(3, attempt.MaxRawScore);
        Assert.Equal(2, attempt.RawScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Subset_practice_start_requires_question_scope()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() => attemptSvc.StartInModeAsync(
            "u1",
            "p1",
            ReadingAttemptMode.Drill,
            JsonSerializer.Serialize(new { kind = "drill", minutes = 8 }),
            default));

        Assert.Equal("scope_question_ids_required", ex.Code);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Legacy_subset_practice_without_scope_never_receives_scaled_score()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        var snapshot = await policy.ResolveForUserAsync("u1", default);

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "legacy-drill",
            UserId = "u1",
            PaperId = "p1",
            Mode = ReadingAttemptMode.Drill,
            ScopeJson = null,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = ReadingStructureService.CanonicalMaxRawScore,
            PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("legacy-drill", default);

        Assert.Equal(0, result.RawScore);
        Assert.Equal(0, result.MaxRawScore);
        Assert.Null(result.ScaledScore);
        Assert.Equal("—", result.GradeLetter);

        var attempt = await db.ReadingAttempts.SingleAsync(a => a.Id == "legacy-drill");
        Assert.Null(attempt.ScaledScore);
        Assert.Equal(0, attempt.MaxRawScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Submitted_legacy_subset_practice_with_stale_scaled_score_is_cleaned_up()
    {
        var (db, structure, policy, grader, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        var snapshot = await policy.ResolveForUserAsync("u1", default);

        db.ReadingAttempts.Add(new ReadingAttempt
        {
            Id = "legacy-submitted-drill",
            UserId = "u1",
            PaperId = "p1",
            Mode = ReadingAttemptMode.Drill,
            ScopeJson = null,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastActivityAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = ReadingAttemptStatus.Submitted,
            RawScore = 30,
            MaxRawScore = ReadingStructureService.CanonicalMaxRawScore,
            ScaledScore = 350,
            PolicySnapshotJson = JsonSerializer.Serialize(snapshot),
        });
        await db.SaveChangesAsync();

        var result = await grader.GradeAttemptAsync("legacy-submitted-drill", default);

        Assert.Equal(0, result.RawScore);
        Assert.Equal(0, result.MaxRawScore);
        Assert.Null(result.ScaledScore);
        Assert.Equal("—", result.GradeLetter);

        var attempt = await db.ReadingAttempts.SingleAsync(a => a.Id == "legacy-submitted-drill");
        Assert.Equal(0, attempt.RawScore);
        Assert.Equal(0, attempt.MaxRawScore);
        Assert.Null(attempt.ScaledScore);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drill_sampler_returns_only_questions_for_requested_part()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var sample = await ReadingPracticeSampler.SampleAsync(
            db, "p1", ReadingPartCode.B, skillTag: null, count: 4, default);
        Assert.Equal(4, sample.Count);

        var parts = await db.ReadingQuestions
            .Where(q => sample.Contains(q.Id))
            .Select(q => q.Part!.PartCode)
            .ToListAsync();
        Assert.All(parts, p => Assert.Equal(ReadingPartCode.B, p));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MiniTest_sampler_returns_mixed_part_subset()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var sample = await ReadingPracticeSampler.SampleMixedAsync(db, "p1", count: 12, default);
        Assert.NotEmpty(sample);
        Assert.True(sample.Count <= 12);

        var distinctParts = await db.ReadingQuestions
            .Where(q => sample.Contains(q.Id))
            .Select(q => q.Part!.PartCode)
            .Distinct()
            .ToListAsync();
        // A mini-test must touch at least 2 of the 3 parts so the learner
        // gets a balanced warm-up rather than a single-part run.
        Assert.True(distinctParts.Count >= 2);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ErrorBank_subset_does_not_reset_part_a_lock_warning()
    {
        // Phase 3b ErrorBank attempts must use Drill-style timer (>=15min)
        // so a learner can retest a Part-A miss without the canonical
        // 15-minute hard lock kicking in. We confirm the deadline horizon.
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partAQid = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.A)
            .Select(q => q.Id)
            .FirstAsync();
        var scope = JsonSerializer.Serialize(new
        {
            kind = "error-bank",
            questionIds = new[] { partAQid },
        });
        var run = await attemptSvc.StartInModeAsync(
            "u1", "p1", ReadingAttemptMode.ErrorBank, scope, default);
        var now = DateTimeOffset.UtcNow;
        Assert.True(run.DeadlineAt - now >= TimeSpan.FromMinutes(14),
            $"Expected ErrorBank attempt to give >=15min, got {run.DeadlineAt - now}");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task MiniTest_save_rejects_answers_during_submit_grace()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var questionId = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.A)
            .Select(q => q.Id)
            .FirstAsync();
        var scope = JsonSerializer.Serialize(new
        {
            kind = "mini-test",
            minutes = 5,
            questionIds = new[] { questionId },
        });
        var run = await attemptSvc.StartInModeAsync("u1", "p1", ReadingAttemptMode.MiniTest, scope, default);

        var attempt = await db.ReadingAttempts.FirstAsync(a => a.Id == run.AttemptId);
        var now = DateTimeOffset.UtcNow;
        attempt.StartedAt = now.AddMinutes(-5).AddSeconds(-1);
        attempt.DeadlineAt = now.AddSeconds(9);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", run.AttemptId, questionId, "\"ans1\"", default));

        Assert.Equal("answer_window_closed", ex.Code);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drill_attempt_rejects_same_paper_question_outside_scope()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var partAIds = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .Take(2)
            .Select(q => q.Id)
            .ToListAsync();
        var scope = JsonSerializer.Serialize(new
        {
            kind = "drill",
            questionIds = new[] { partAIds[0] },
        });
        var run = await attemptSvc.StartInModeAsync("u1", "p1", ReadingAttemptMode.Drill, scope, default);

        var ex = await Assert.ThrowsAsync<ReadingAttemptException>(() =>
            attemptSvc.SaveAnswerAsync("u1", run.AttemptId, partAIds[1], "\"ans2\"", default));

        Assert.Equal("question_out_of_scope", ex.Code);
        Assert.Equal(0, await db.ReadingAnswers.CountAsync(a => a.ReadingAttemptId == run.AttemptId));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drill_review_endpoint_returns_scope_only_without_oet_scaled_grade()
    {
        using var factory = new TestWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var structure = new ReadingStructureService(db);
        var attemptSvc = scope.ServiceProvider.GetRequiredService<IReadingAttemptService>();
        var paperId = "review-drill-paper";
        var userId = "mock-user-001";

        await SeedPaperAsync(db, paperId);
        await structure.EnsureCanonicalPartsAsync(paperId, default);
        await FullyAuthorPaperAsync(db, structure, paperId);

        var scopedQuestions = await db.ReadingQuestions
            .Where(q => q.Part!.PaperId == paperId && q.Part.PartCode == ReadingPartCode.A)
            .OrderBy(q => q.DisplayOrder)
            .Take(2)
            .Select(q => new { q.Id, q.CorrectAnswerJson })
            .ToListAsync();
        var scopedIds = scopedQuestions.Select(q => q.Id).ToArray();
        var scopeJson = JsonSerializer.Serialize(new
        {
            kind = "drill",
            minutes = 5,
            questionIds = scopedIds,
        });

        var run = await attemptSvc.StartInModeAsync(
            userId, paperId, ReadingAttemptMode.Drill, scopeJson, default);
        await attemptSvc.SaveAnswerAsync(userId, run.AttemptId, scopedQuestions[0].Id, scopedQuestions[0].CorrectAnswerJson, default);
        var submitResult = await attemptSvc.SubmitAsync(userId, run.AttemptId, default);
        Assert.Null(submitResult.ScaledScore);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        var reviewResponse = await client.GetAsync($"/v1/reading-papers/attempts/{run.AttemptId}/review");
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();

        var attemptJson = review.GetProperty("attempt");
        Assert.Equal("Drill", attemptJson.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, attemptJson.GetProperty("scaledScore").ValueKind);
        Assert.Equal("—", attemptJson.GetProperty("gradeLetter").GetString());

        var items = review.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Contains(item.GetProperty("questionId").GetString(), scopedIds));

        var partBreakdown = review.GetProperty("partBreakdown").EnumerateArray().ToList();
        Assert.Single(partBreakdown);
        Assert.Equal("A", partBreakdown[0].GetProperty("partCode").GetString());
        Assert.Equal(2, partBreakdown[0].GetProperty("maxRawScore").GetInt32());

        var attemptResponse = await client.GetAsync($"/v1/reading-papers/attempts/{run.AttemptId}");
        attemptResponse.EnsureSuccessStatusCode();
        var attemptDto = await attemptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, attemptDto.GetProperty("totalQuestions").GetInt32());
        Assert.Equal(
            attemptDto.GetProperty("partADeadlineAt").GetDateTimeOffset(),
            attemptDto.GetProperty("partBCDeadlineAt").GetDateTimeOffset());
    }

    // ════════════════════════════════════════════════════════════════════
    // Phase 4 — Distractor Categories + Authoring Review States
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Distractor_category_is_recorded_on_wrong_mcq_answer()
    {
        var (db, structure, _, grader, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        // Tag option "A" on a Part C MCQ4 question (correct = "C") with Opposite.
        var partCQ = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.C)
            .OrderBy(q => q.DisplayOrder)
            .FirstAsync();
        var review = new ReadingReviewService(db);
        await review.SetDistractorsAsync(partCQ.Id,
            new Dictionary<string, ReadingDistractorCategory>
            {
                ["A"] = ReadingDistractorCategory.Opposite,
                ["B"] = ReadingDistractorCategory.NotInText,
            }, "admin", default);

        // Learner picks A (the tagged-Opposite distractor) → wrong.
        var run = await attemptSvc.StartAsync("u1", "p1", default);
        await ResumeExamPartBCAsync(db, attemptSvc, "u1", run.AttemptId);
        await attemptSvc.SaveAnswerAsync("u1", run.AttemptId, partCQ.Id, "\"A\"", default);
        await attemptSvc.SubmitAsync("u1", run.AttemptId, default);

        var saved = await db.ReadingAnswers.FirstAsync(a =>
            a.ReadingAttemptId == run.AttemptId && a.ReadingQuestionId == partCQ.Id);
        Assert.False(saved.IsCorrect);
        Assert.Equal(ReadingDistractorCategory.Opposite, saved.SelectedDistractorCategory);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Review_state_transitions_follow_state_machine()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");
        // FullyAuthorPaperAsync fast-forwards to Published; reset to Draft for this test.
        var q = await db.ReadingQuestions.FirstAsync(x => x.Part!.PartCode == ReadingPartCode.A);
        q.ReviewState = ReadingReviewState.Draft;
        await db.SaveChangesAsync();

        var review = new ReadingReviewService(db);

        // Draft → AcademicReview is allowed
        var t1 = await review.TransitionStateAsync(new ReadingReviewTransitionArgs(
            q.Id, ReadingReviewState.AcademicReview, "admin-1", "Reviewer", "Looks good.", false), default);
        Assert.Equal(ReadingReviewState.Draft, t1.FromState);
        Assert.Equal(ReadingReviewState.AcademicReview, t1.ToState);

        // AcademicReview → Published is NOT allowed (must traverse the chain)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            review.TransitionStateAsync(new ReadingReviewTransitionArgs(
                q.Id, ReadingReviewState.Published, "admin-1", null, null, false), default));

        // Admin override DOES allow rollback to Draft from any state
        var rollback = await review.TransitionStateAsync(new ReadingReviewTransitionArgs(
            q.Id, ReadingReviewState.Draft, "admin-1", null, "Emergency rollback.", true), default);
        Assert.Equal(ReadingReviewState.Draft, rollback.ToState);

        var history = await review.GetHistoryAsync(q.Id, default);
        Assert.Equal(2, history.Count);
        Assert.Equal(ReadingReviewState.Draft, history[0].ToState); // newest first
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Validate_paper_blocks_publish_when_questions_not_published()
    {
        var (db, structure, _, _, _) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        // Knock one question back to Draft.
        var q = await db.ReadingQuestions.FirstAsync(x => x.Part!.PartCode == ReadingPartCode.B);
        q.ReviewState = ReadingReviewState.Draft;
        await db.SaveChangesAsync();

        var report = await structure.ValidatePaperAsync("p1", default);
        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "question_not_published" && i.TargetId == q.Id);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Paper_analytics_reports_distractor_histogram_and_risk_labels()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        // Tag a Part C question's wrong options.
        var partCQ = await db.ReadingQuestions
            .Where(q => q.Part!.PartCode == ReadingPartCode.C)
            .OrderBy(q => q.DisplayOrder)
            .FirstAsync();
        var review = new ReadingReviewService(db);
        await review.SetDistractorsAsync(partCQ.Id,
            new Dictionary<string, ReadingDistractorCategory>
            {
                ["A"] = ReadingDistractorCategory.Opposite,
            }, "admin", default);

        // 6 learners all pick A (the tagged distractor) → 6 wrong attempts.
        for (var i = 0; i < 6; i++)
        {
            var userId = $"u{i}";
            var run = await attemptSvc.StartAsync(userId, "p1", default);
            await ResumeExamPartBCAsync(db, attemptSvc, userId, run.AttemptId);
            await attemptSvc.SaveAnswerAsync(userId, run.AttemptId, partCQ.Id, "\"A\"", default);
            await attemptSvc.SubmitAsync(userId, run.AttemptId, default);
        }

        var analytics = new ReadingAnalyticsService(db);
        var data = await analytics.GetPaperAnalyticsAsync("p1", default);

        Assert.Equal(6, data.SubmittedAttempts);
        Assert.Contains(data.DistractorHistogram, h =>
            h.QuestionId == partCQ.Id
            && h.Category == ReadingDistractorCategory.Opposite
            && h.OptionKey == "A"
            && h.SelectedCount == 6);
        Assert.Contains(data.RiskLabels, r => r.QuestionId == partCQ.Id && r.Code == "too_hard");
        Assert.Contains(data.HardestQuestions, h => h.QuestionId == partCQ.Id && h.CorrectRate == 0);
        await db.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Phase 6 — AI PDF extraction → admin approval
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Extraction_create_then_approve_imports_manifest_and_blocks_publish_until_review()
    {
        var (db, structure, policy, _, _) = Build();
        await SeedPaperAsync(db, "p1", ContentStatus.Draft);
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var ai = new OetLearner.Api.Services.Reading.StubReadingExtractionAi();
        var svc = new OetLearner.Api.Services.Reading.ReadingExtractionService(db, ai, structure, policy);

        var draft = await svc.CreateDraftAsync("p1", mediaAssetId: null, "admin", default);
        Assert.Equal(OetLearner.Api.Domain.ReadingExtractionStatus.Pending, draft.Status);
        Assert.True(draft.IsStub);
        Assert.False(string.IsNullOrEmpty(draft.ExtractedManifestJson));

        // Approve — the manifest is applied to the paper.
        var approved = await svc.ApproveDraftAsync(draft.Id, "admin", default);
        Assert.Equal(OetLearner.Api.Domain.ReadingExtractionStatus.Approved, approved.Status);

        // Structure is now in place, but every question is still in Draft
        // review state — publish gate must block.
        var report = await structure.ValidatePaperAsync("p1", default);
        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "question_not_published");
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Extraction_reject_marks_draft_and_does_not_apply_manifest()
    {
        var (db, structure, policy, _, _) = Build();
        await SeedPaperAsync(db, "p1", ContentStatus.Draft);
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var ai = new OetLearner.Api.Services.Reading.StubReadingExtractionAi();
        var svc = new OetLearner.Api.Services.Reading.ReadingExtractionService(db, ai, structure, policy);

        var draft = await svc.CreateDraftAsync("p1", mediaAssetId: null, "admin", default);
        var rejected = await svc.RejectDraftAsync(draft.Id, "admin", "Looks wrong", default);
        Assert.Equal(OetLearner.Api.Domain.ReadingExtractionStatus.Rejected, rejected.Status);
        Assert.Equal("Looks wrong", rejected.Notes);

        // No structure was applied → no questions exist.
        var qCount = await db.ReadingQuestions
            .CountAsync(q => q.Part!.PaperId == "p1");
        Assert.Equal(0, qCount);

        // Re-approving a rejected draft fails.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.ApproveDraftAsync(draft.Id, "admin", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Extraction_respects_kill_switch_when_policy_disables_ai()
    {
        var (db, structure, policy, _, _) = Build();
        await SeedPaperAsync(db, "p1", ContentStatus.Draft);
        await structure.EnsureCanonicalPartsAsync("p1", default);

        // Flip the kill switch off.
        var current = await policy.GetGlobalAsync(default);
        current.AiExtractionEnabled = false;
        await policy.UpsertGlobalAsync(current, "admin", default);

        var ai = new OetLearner.Api.Services.Reading.StubReadingExtractionAi();
        var svc = new OetLearner.Api.Services.Reading.ReadingExtractionService(db, ai, structure, policy);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.CreateDraftAsync("p1", mediaAssetId: null, "admin", default));
        await db.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Course pathway integration
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pathway_starts_at_not_started_then_advances_through_drilling_to_mock_ready()
    {
        var (db, structure, _, _, attemptSvc) = Build();
        await SeedPaperAsync(db, "p1");
        await structure.EnsureCanonicalPartsAsync("p1", default);
        await FullyAuthorPaperAsync(db, structure, "p1");

        var pathway = new OetLearner.Api.Services.Reading.ReadingPathwayService(db);

        // Stage 1: brand-new learner.
        var s1 = await pathway.GetPathwayAsync("u1", default);
        Assert.Equal("not_started", s1.Stage);
        Assert.Equal("start_diagnostic", s1.NextAction.Kind);
        Assert.Equal(0, s1.SubmittedExamAttempts);
        Assert.Contains(s1.Milestones, m => m.Code == "first_attempt" && !m.Achieved);

        // Stage 2: one weak Exam attempt — best-scaled is computed from a
        // freshly-graded attempt. We answer everything wrong so scaled is 0.
        var run = await attemptSvc.StartAsync("u1", "p1", default);
        var qs = await db.ReadingQuestions.AsNoTracking()
            .Include(q => q.Part)
            .Where(q => q.Part!.PaperId == "p1")
            .OrderBy(q => q.Part!.PartCode)
            .ThenBy(q => q.DisplayOrder)
            .ToListAsync();
        foreach (var q in qs.Where(q => q.Part!.PartCode == ReadingPartCode.A))
        {
            await attemptSvc.SaveAnswerAsync("u1", run.AttemptId, q.Id, "\"WRONG\"", default);
        }
        await ResumeExamPartBCAsync(db, attemptSvc, "u1", run.AttemptId);
        foreach (var q in qs.Where(q => q.Part!.PartCode != ReadingPartCode.A))
        {
            await attemptSvc.SaveAnswerAsync("u1", run.AttemptId, q.Id, "\"WRONG\"", default);
        }
        await attemptSvc.SubmitAsync("u1", run.AttemptId, default);

        var s2 = await pathway.GetPathwayAsync("u1", default);
        Assert.True(s2.Stage == "drilling" || s2.Stage == "mini_tests" || s2.Stage == "mock_ready",
            $"expected post-attempt stage, got {s2.Stage}");
        Assert.True(s2.SubmittedExamAttempts >= 1);

        await db.DisposeAsync();
    }
}

file static class ReadingGradingResultExtensions
{
    public static int PointsEarnedSum(this ReadingGradingResult r) => r.Answers.Sum(a => a.PointsEarned);
}
