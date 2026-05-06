using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ListeningStructureService"/> — the publish-gate
/// validator that enforces the canonical OET Listening shape
/// (Part A = 24, Part B = 6, Part C = 12 → 42 items).
/// </summary>
public class ListeningStructureServiceTests
{
    private static (LearnerDbContext db, ListeningStructureService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ListeningStructureService(db));
    }

    private static async Task<ContentPaper> AddPaperAsync(LearnerDbContext db, string? extractedTextJson)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Paper Test",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = extractedTextJson ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    private static string BuildQuestionsJson(int partA, int partB, int partC, int partBOptionCount = 3)
    {
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < partA; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = i < partA / 2 ? "A1" : "A2", type = "short_answer", text = "q", correctAnswer = "x" });
        for (var i = 0; i < partB; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = "B", type = "multiple_choice_3", text = "q",
                options = Enumerable.Range(0, partBOptionCount).Select(x => $"opt-{x}").ToArray(),
                correctAnswer = "opt-0" });
        for (var i = 0; i < partC; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = i < partC / 2 ? "C1" : "C2", type = "multiple_choice_3", text = "q",
                options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        return JsonSerializer.Serialize(new { listeningQuestions = list });
    }

    [Fact]
    public async Task CanonicalShape_24_6_12_IsPublishReady()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
        Assert.Equal(42, report.Counts.TotalItems);
        Assert.Empty(report.Issues);
    }

    [Theory]
    [InlineData(23, 6, 12, "listening_part_a_count")]
    [InlineData(24, 5, 12, "listening_part_b_count")]
    [InlineData(24, 6, 11, "listening_part_c_count")]
    [InlineData(25, 6, 12, "listening_part_a_count")]
    public async Task NonCanonicalCounts_BlockPublish(int a, int b, int c, string expectedCode)
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(a, b, c));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == expectedCode && i.Severity == "error");
    }

    [Fact]
    public async Task EmptyExtractedText_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, null);

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_no_items" && i.Severity == "error");
        Assert.Equal(0, report.Counts.TotalItems);
    }

    [Fact]
    public async Task MalformedJson_BlocksPublish_WithExplicitIssue()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, "{ not valid json ");

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_invalid_json" && i.Severity == "error");
    }

    [Fact]
    public async Task PartB_WithWrongOptionCount_BlocksPublish()
    {
        var (db, svc) = Build();
        // Part B items have 2 options instead of the required 3.
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12, partBOptionCount: 2));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task LegacyPartCodes_A_And_C_AreCountedCorrectly()
    {
        var (db, svc) = Build();
        // Legacy flat "A" / "C" codes (no A1/A2 granularity) still count.
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < 24; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = "A", type = "short_answer", text = "q", correctAnswer = "x" });
        for (var i = 0; i < 6; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = "B", type = "multiple_choice_3", text = "q",
                options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        for (var i = 0; i < 12; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = "C", type = "multiple_choice_3", text = "q", options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        var json = JsonSerializer.Serialize(new { listeningQuestions = list });
        var paper = await AddPaperAsync(db, json);

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
    }

    [Fact]
    public async Task CanonicalCounts_WithBlankAnswers_BlockPublish()
    {
        var (db, svc) = Build();
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < 24; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = i < 12 ? "A1" : "A2", type = "short_answer", text = "q", correctAnswer = i == 0 ? "" : "x" });
        for (var i = 0; i < 6; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = "B", type = "multiple_choice_3", text = "q", options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        for (var i = 0; i < 12; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = i < 6 ? "C1" : "C2", type = "multiple_choice_3", text = "q", options = new[] { "1", "2", "3" }, correctAnswer = "1" });
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = list }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_blank_answers" && i.Severity == "error");
    }

    [Fact]
    public async Task CanonicalCounts_WithDuplicateNumbers_BlockPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[1]["number"] = 1;
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_duplicate_question_numbers" && i.Severity == "error");
    }

    [Fact]
    public async Task ValidatePaperAsync_Throws_WhenPaperMissing()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ValidatePaperAsync("does-not-exist", default));
    }

    [Fact]
    public async Task RelationalQuestions_PreferredOverEmptyJson_PublishReady()
    {
        var (db, svc) = Build();
        // Paper has empty ExtractedTextJson — JSON path would block publish,
        // but the relational ListeningQuestion table has the canonical
        // 24/6/12 = 42 split, so the relational path should succeed.
        var paper = await AddPaperAsync(db, null);

        ListeningPart AddPart(ListeningPartCode code, int max)
        {
            var part = new ListeningPart
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paper.Id,
                PartCode = code,
                MaxRawScore = max,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Set<ListeningPart>().Add(part);
            return part;
        }

        var partA1 = AddPart(ListeningPartCode.A1, 12);
        var partA2 = AddPart(ListeningPartCode.A2, 12);
        var partB = AddPart(ListeningPartCode.B, 6);
        var partC1 = AddPart(ListeningPartCode.C1, 6);
        var partC2 = AddPart(ListeningPartCode.C2, 6);

        var qNum = 1;
        void AddQuestions(ListeningPart part, int count, ListeningQuestionType qType)
        {
            for (var i = 0; i < count; i++)
            {
                var q = new ListeningQuestion
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PaperId = paper.Id,
                    ListeningPartId = part.Id,
                    QuestionNumber = qNum++,
                    DisplayOrder = i + 1,
                    Points = 1,
                    QuestionType = qType,
                    Stem = "stem",
                    CorrectAnswerJson = qType == ListeningQuestionType.MultipleChoice3 ? "\"A\"" : "\"x\"",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Set<ListeningQuestion>().Add(q);
                if (qType == ListeningQuestionType.MultipleChoice3)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        db.Set<ListeningQuestionOption>().Add(new ListeningQuestionOption
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            ListeningQuestionId = q.Id,
                            OptionKey = ((char)('A' + k)).ToString(),
                            Text = $"opt-{k}",
                            DisplayOrder = k + 1,
                            IsCorrect = k == 0,
                        });
                    }
                }
            }
        }

        AddQuestions(partA1, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(partA2, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(partB, 6, ListeningQuestionType.MultipleChoice3);
        AddQuestions(partC1, 6, ListeningQuestionType.MultipleChoice3);
        AddQuestions(partC2, 6, ListeningQuestionType.MultipleChoice3);
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal("relational", report.Source);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
        Assert.Equal(42, report.Counts.TotalItems);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task JsonPartB_WithCorrectAnswerOutsideOptions_BlocksPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[24]["correctAnswer"] = "not-an-option";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task JsonPartB_WithWrongType_BlocksPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[24]["type"] = "short_answer";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task JsonPartB_WithDuplicateCorrectOptionText_BlocksPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[24]["options"] = new[] { "A", "A", "C" };
        questions[24]["correctAnswer"] = "A";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task RelationalPartC_WithCorrectAnswerMismatch_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, null);

        ListeningPart AddPart(ListeningPartCode code, int max)
        {
            var part = new ListeningPart
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paper.Id,
                PartCode = code,
                MaxRawScore = max,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Set<ListeningPart>().Add(part);
            return part;
        }

        var partA1 = AddPart(ListeningPartCode.A1, 12);
        var partA2 = AddPart(ListeningPartCode.A2, 12);
        var partB = AddPart(ListeningPartCode.B, 6);
        var partC1 = AddPart(ListeningPartCode.C1, 6);
        var partC2 = AddPart(ListeningPartCode.C2, 6);

        var qNum = 1;
        void AddQuestions(ListeningPart part, int count, ListeningQuestionType qType, bool makeFirstInvalid = false)
        {
            for (var i = 0; i < count; i++)
            {
                var q = new ListeningQuestion
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PaperId = paper.Id,
                    ListeningPartId = part.Id,
                    QuestionNumber = qNum++,
                    DisplayOrder = i + 1,
                    Points = 1,
                    QuestionType = qType,
                    Stem = "stem",
                    CorrectAnswerJson = qType == ListeningQuestionType.MultipleChoice3 && makeFirstInvalid && i == 0 ? "\"B\"" : qType == ListeningQuestionType.MultipleChoice3 ? "\"A\"" : "\"x\"",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Set<ListeningQuestion>().Add(q);
                if (qType == ListeningQuestionType.MultipleChoice3)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        db.Set<ListeningQuestionOption>().Add(new ListeningQuestionOption
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            ListeningQuestionId = q.Id,
                            OptionKey = ((char)('A' + k)).ToString(),
                            Text = $"opt-{k}",
                            DisplayOrder = k + 1,
                            IsCorrect = k == 0,
                        });
                    }
                }
            }
        }

        AddQuestions(partA1, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(partA2, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(partB, 6, ListeningQuestionType.MultipleChoice3);
        AddQuestions(partC1, 6, ListeningQuestionType.MultipleChoice3, makeFirstInvalid: true);
        AddQuestions(partC2, 6, ListeningQuestionType.MultipleChoice3);
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_c_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task RelationalAbsent_FallsBackToJsonSource()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady);
        Assert.Equal("json", report.Source);
    }
}
