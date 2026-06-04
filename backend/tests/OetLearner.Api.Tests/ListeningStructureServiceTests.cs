using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for <see cref="ListeningStructureService"/> — the publish-gate
/// validator that enforces the canonical OET Listening shape
/// (Part A = 24, Part B = 6 across six independent sub-sections B1..B6 with one
/// item each, Part C = 12 → 42 items). Any sub-section may use any of the three
/// content types; audio for a sub-section comes from an uploaded ContentPaperAsset
/// (Role=Audio, Part=&lt;code&gt;) or a TTS-synthesised extract.
/// </summary>
public class ListeningStructureServiceTests
{
    private static readonly string[] PartBCodes = { "B1", "B2", "B3", "B4", "B5", "B6" };

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
            SourceProvenance = "source=unit-test; legal=original-authoring-attested",
            ExtractedTextJson = extractedTextJson ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    /// <summary>
    /// Build a canonical JSON paper. Part A splits A1/A2, Part B uses the six
    /// independent sub-section codes B1..B6 (one item each, capped at 6), Part C
    /// splits C1/C2. <paramref name="partBOptionCount"/> lets a test break the
    /// MCQ option shape.
    /// </summary>
    private static string BuildQuestionsJson(int partA, int partB, int partC, int partBOptionCount = 3)
    {
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < partA; i++)
        {
            list.Add(new
            {
                id = $"a-{i}",
                number = num,
                partCode = i < partA / 2 ? "A1" : "A2",
                type = "short_answer",
                text = "q",
                correctAnswer = "x",
                skillTag = "note_completion",
                transcriptExcerpt = "x",
                transcriptEvidenceStartMs = num * 1000,
                transcriptEvidenceEndMs = num * 1000 + 500,
                difficultyLevel = 3,
            });
            num++;
        }
        for (var i = 0; i < partB; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = i < 6 ? PartBCodes[i] : "B6", type = "multiple_choice_3", text = "q",
                options = Enumerable.Range(0, partBOptionCount).Select(x => $"opt-{x}").ToArray(),
                correctAnswer = "opt-0", skillTag = "detail", transcriptExcerpt = "opt-0",
                transcriptEvidenceStartMs = num * 1000, transcriptEvidenceEndMs = num * 1000 + 500,
                difficultyLevel = 3,
                optionDistractorCategory = Enumerable.Range(0, partBOptionCount).Select(index => index == 0 ? null : "reused_keyword").ToArray() });
        for (var i = 0; i < partC; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = i < partC / 2 ? "C1" : "C2", type = "multiple_choice_3", text = "q",
                options = new[] { "1", "2", "3" }, correctAnswer = "1", skillTag = "attitude",
                transcriptExcerpt = "1", transcriptEvidenceStartMs = num * 1000,
                transcriptEvidenceEndMs = num * 1000 + 500, difficultyLevel = 3,
                optionDistractorCategory = new string?[] { null, "too_weak", "opposite_meaning" } });
        // One extract per sub-section, each with a non-overlapping audio window.
        var extracts = new List<object>
        {
            new { partCode = "A1", displayOrder = 1, kind = "consultation", title = "A1", audioStartMs = 0, audioEndMs = 60_000, difficultyRating = 3 },
            new { partCode = "A2", displayOrder = 2, kind = "consultation", title = "A2", audioStartMs = 60_000, audioEndMs = 120_000, difficultyRating = 3 },
        };
        for (var i = 0; i < 6; i++)
            extracts.Add(new { partCode = PartBCodes[i], displayOrder = 3 + i, kind = "workplace", title = PartBCodes[i],
                audioStartMs = 120_000 + i * 10_000, audioEndMs = 120_000 + (i + 1) * 10_000, difficultyRating = 3 });
        extracts.Add(new { partCode = "C1", displayOrder = 9, kind = "presentation", title = "C1", audioStartMs = 180_000, audioEndMs = 240_000, difficultyRating = 3 });
        extracts.Add(new { partCode = "C2", displayOrder = 10, kind = "presentation", title = "C2", audioStartMs = 240_000, audioEndMs = 300_000, difficultyRating = 3 });
        return JsonSerializer.Serialize(new { listeningQuestions = list, listeningExtracts = extracts });
    }

    /// <summary>Index of the first Part B item in BuildQuestionsJson's question
    /// list for a 24/6/12 paper (Part A occupies 0..23).</summary>
    private const int FirstPartBIndex = 24;

    [Fact]
    public async Task CanonicalShape_24_6_12_IsPublishReady()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
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
    public async Task PartB_NotSplitIntoSixSubSections_BlocksPublish()
    {
        var (db, svc) = Build();
        // Six Part B items all coded the SAME sub-section (B1) — the per-sub-section
        // split rule must flag this even though the aggregate total is 6.
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        for (var i = 0; i < 6; i++) questions[FirstPartBIndex + i]["partCode"] = "B1";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_part_b_split" && i.Severity == "error");
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
    public async Task MissingSourceProvenance_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));
        paper.SourceProvenance = null;
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, issue => issue.Code == "listening_source_provenance" && issue.Severity == "error");
    }

    [Fact]
    public async Task SourceProvenanceWithoutLegalAttestation_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));
        paper.SourceProvenance = "source=unit-test-only";
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, issue => issue.Code == "listening_source_provenance" && issue.Severity == "error");
    }

    [Fact]
    public async Task SourceProvenanceWithNonLegalKey_BlocksPublish()
    {
        var (db, svc) = Build();
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12));
        paper.SourceProvenance = "source=unit-test; illegal=original-authoring-attested";
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, issue => issue.Code == "listening_source_provenance" && issue.Severity == "error");
    }

    [Fact]
    public async Task JsonCanonicalCounts_PedagogicalGatesAdvisory_AudioTimingStillBlocks()
    {
        var (db, svc) = Build();
        using var doc = JsonDocument.Parse(BuildQuestionsJson(24, 6, 12));
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        var extracts = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningExtracts").GetRawText())!;
        questions[0]["skillTag"] = "";
        questions[1]["transcriptEvidenceStartMs"] = null;
        questions[2]["difficultyLevel"] = null;
        questions[FirstPartBIndex]["optionDistractorCategory"] = new string?[] { null, null, null };
        extracts[0]["audioEndMs"] = 0;
        extracts[1]["difficultyRating"] = null;
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions, listeningExtracts = extracts }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        // Owner decision: pedagogical authoring gates are advisory (warning),
        // not publish blockers, for the uploaded-audio Listening flow.
        Assert.Contains(report.Issues, issue => issue.Code == "listening_skill_tags" && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue => issue.Code == "listening_transcript_evidence" && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue => issue.Code == "listening_question_difficulty" && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue => issue.Code == "listening_distractor_categories" && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue => issue.Code == "listening_extract_difficulty" && issue.Severity == "warning");
        // Audio cue timing remains a hard publish blocker (extract[0] window invalid).
        Assert.Contains(report.Issues, issue => issue.Code == "listening_extract_timing" && issue.Severity == "error");
        Assert.False(report.IsPublishReady);
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
        // Part B MCQ items have 2 options instead of the required 3.
        var paper = await AddPaperAsync(db, BuildQuestionsJson(24, 6, 12, partBOptionCount: 2));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task LegacyPartCodes_A_And_C_AreCountedCorrectly()
    {
        var (db, svc) = Build();
        // Legacy flat "A" / "C" codes (no A1/A2 granularity) still count; Part B
        // uses the six sub-section codes.
        var list = new List<object>();
        var num = 1;
        for (var i = 0; i < 24; i++)
            list.Add(new { id = $"a-{i}", number = num++, partCode = "A", type = "short_answer", text = "q", correctAnswer = "x", skillTag = "note_completion", transcriptExcerpt = "x", transcriptEvidenceStartMs = num * 1000, transcriptEvidenceEndMs = num * 1000 + 500, difficultyLevel = 3 });
        for (var i = 0; i < 6; i++)
            list.Add(new { id = $"b-{i}", number = num++, partCode = PartBCodes[i], type = "multiple_choice_3", text = "q",
                options = new[] { "1", "2", "3" }, correctAnswer = "1", skillTag = "detail", transcriptExcerpt = "1", transcriptEvidenceStartMs = num * 1000, transcriptEvidenceEndMs = num * 1000 + 500, difficultyLevel = 3, optionDistractorCategory = new string?[] { null, "too_weak", "reused_keyword" } });
        for (var i = 0; i < 12; i++)
            list.Add(new { id = $"c-{i}", number = num++, partCode = "C", type = "multiple_choice_3", text = "q", options = new[] { "1", "2", "3" }, correctAnswer = "1", skillTag = "attitude", transcriptExcerpt = "1", transcriptEvidenceStartMs = num * 1000, transcriptEvidenceEndMs = num * 1000 + 500, difficultyLevel = 3, optionDistractorCategory = new string?[] { null, "too_weak", "opposite_meaning" } });
        var extracts = new List<object>
        {
            new { partCode = "A", displayOrder = 1, kind = "consultation", title = "A", audioStartMs = 0, audioEndMs = 120_000, difficultyRating = 3 },
        };
        for (var i = 0; i < 6; i++)
            extracts.Add(new { partCode = PartBCodes[i], displayOrder = 3 + i, kind = "workplace", title = PartBCodes[i],
                audioStartMs = 120_000 + i * 10_000, audioEndMs = 120_000 + (i + 1) * 10_000, difficultyRating = 3 });
        extracts.Add(new { partCode = "C", displayOrder = 9, kind = "presentation", title = "C", audioStartMs = 180_000, audioEndMs = 300_000, difficultyRating = 3 });
        var json = JsonSerializer.Serialize(new { listeningQuestions = list, listeningExtracts = extracts });
        var paper = await AddPaperAsync(db, json);

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
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
            list.Add(new { id = $"b-{i}", number = num++, partCode = PartBCodes[i], type = "multiple_choice_3", text = "q", options = new[] { "1", "2", "3" }, correctAnswer = "1" });
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
        // Paper has empty ExtractedTextJson — JSON path would block publish, but
        // the relational tables carry the canonical 24/6/12 = 42 split (Part B as
        // six independent B1..B6 sub-sections), so the relational path succeeds.
        var seed = await SeedCanonicalRelationalAsync(db);
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        Assert.Equal("relational", report.Source);
        Assert.Equal(24, report.Counts.PartACount);
        Assert.Equal(6, report.Counts.PartBCount);
        Assert.Equal(12, report.Counts.PartCCount);
        Assert.Equal(42, report.Counts.TotalItems);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task JsonPartB_WithCorrectAnswerLetter_IsPublishReady()
    {
        var (db, svc) = Build();
        using var doc = JsonDocument.Parse(BuildQuestionsJson(24, 6, 12));
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        var extracts = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningExtracts").GetRawText())!;
        questions[FirstPartBIndex]["correctAnswer"] = "A";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions, listeningExtracts = extracts }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task JsonSubSection_WithFillInBlankType_IsPublishReady()
    {
        var (db, svc) = Build();
        // New contract: any sub-section may use any of the 3 content types. A
        // Part B sub-section authored as a free-text (fill-in-blank → short_answer
        // wire) item must NOT trip an MCQ-shape gate.
        using var doc = JsonDocument.Parse(BuildQuestionsJson(24, 6, 12));
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        var extracts = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningExtracts").GetRawText())!;
        questions[FirstPartBIndex]["type"] = "short_answer";
        questions[FirstPartBIndex]["options"] = Array.Empty<string>();
        questions[FirstPartBIndex]["correctAnswer"] = "free text answer";
        questions[FirstPartBIndex]["optionDistractorCategory"] = Array.Empty<string?>();
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions, listeningExtracts = extracts }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_mcq_shape");
    }

    [Fact]
    public async Task JsonPartB_WithInvalidDistractorCategory_IsAdvisoryWarning()
    {
        var (db, svc) = Build();
        using var doc = JsonDocument.Parse(BuildQuestionsJson(24, 6, 12));
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        var extracts = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningExtracts").GetRawText())!;
        questions[FirstPartBIndex]["optionDistractorCategory"] = new string?[] { null, "not_a_real_category", "reused_keyword" };
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions, listeningExtracts = extracts }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        // Distractor-category validity is advisory; it no longer blocks publish.
        Assert.Contains(report.Issues, issue => issue.Code == "listening_distractor_categories_invalid" && issue.Severity == "warning");
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "listening_distractor_categories_invalid" && issue.Severity == "error");
    }

    [Fact]
    public async Task JsonMcq_WithCorrectAnswerOutsideOptions_BlocksPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[FirstPartBIndex]["correctAnswer"] = "not-an-option";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task JsonMcq_WithDuplicateCorrectOptionText_BlocksPublish()
    {
        var (db, svc) = Build();
        var json = BuildQuestionsJson(24, 6, 12);
        using var doc = JsonDocument.Parse(json);
        var questions = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
            doc.RootElement.GetProperty("listeningQuestions").GetRawText())!;
        questions[FirstPartBIndex]["options"] = new[] { "A", "A", "C" };
        questions[FirstPartBIndex]["correctAnswer"] = "A";
        var paper = await AddPaperAsync(db, JsonSerializer.Serialize(new { listeningQuestions = questions }));

        var report = await svc.ValidatePaperAsync(paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_mcq_shape" && i.Severity == "error");
    }

    [Fact]
    public async Task RelationalMcq_WithCorrectAnswerMismatch_BlocksPublish()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Break one C1 MCQ item: its CorrectAnswerJson points at option "B" but
        // option "A" is the one flagged IsCorrect → invalid MCQ shape.
        var c1Question = seed.Questions.First(q =>
            seed.Parts[ListeningPartCode.C1].Id == q.ListeningPartId);
        c1Question.CorrectAnswerJson = "\"B\"";
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_mcq_shape" && i.Severity == "error");
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

    [Fact]
    public async Task UploadedAudioForSubSection_ExemptsTtsWindowChecks()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Strip the TTS audio window AND content sha from the B1 extract — it
        // would normally trip listening_extract_timing AND
        // listening_audio_source_missing. Then attach an uploaded primary Audio
        // asset for part "B1": the alternative audio source, which both satisfies
        // the audio-source gate and exempts the sub-section from cue-window checks
        // (uploaded files carry no start/end window).
        seed.Extracts[ListeningPartCode.B1].AudioStartMs = null;
        seed.Extracts[ListeningPartCode.B1].AudioEndMs = null;
        seed.Extracts[ListeningPartCode.B1].AudioContentSha = null;
        db.Set<ContentPaperAsset>().Add(new ContentPaperAsset
        {
            Id = Guid.NewGuid().ToString("N"),
            PaperId = seed.Paper.Id,
            Role = PaperAssetRole.Audio,
            Part = "B1",
            MediaAssetId = Guid.NewGuid().ToString("N"),
            IsPrimary = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_extract_timing");
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_audio_source_missing");
    }

    [Fact]
    public async Task SubSectionWithNoAudioSource_BlocksPublish()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Remove the B1 extract's TTS window AND its content sha; with no uploaded
        // audio asset either, the sub-section has no audio source at all.
        seed.Extracts[ListeningPartCode.B1].AudioStartMs = null;
        seed.Extracts[ListeningPartCode.B1].AudioEndMs = null;
        seed.Extracts[ListeningPartCode.B1].AudioContentSha = null;
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_audio_source_missing" && i.Severity == "error");
    }

    // ─────────────────────────────────────────────────────────────────────
    // WS-7c — three additive Listening publish rules
    //   listening_extract_cue_overlap
    //   listening_preview_window_missing
    //   listening_results_calc
    // All three fire on the relational source. The seeded fixture below is the
    // canonical 24/6/12 = 42 well-formed paper (no ListeningPolicy row, so the
    // preview windows resolve to ListeningPolicyDefaults); each failure test
    // perturbs exactly one input.
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RelationalSeed(
        ContentPaper Paper,
        IReadOnlyDictionary<ListeningPartCode, ListeningPart> Parts,
        IReadOnlyDictionary<ListeningPartCode, ListeningExtract> Extracts,
        IReadOnlyList<ListeningQuestion> Questions);

    /// <summary>
    /// Seed a canonical, fully-valid relational Listening paper (Part A = 24,
    /// Part B = six independent sub-sections B1..B6 with one item each, Part C =
    /// 12) into <paramref name="db"/> WITHOUT saving, so a test can mutate the
    /// returned entities before <c>SaveChangesAsync</c>. Every field any publish
    /// rule inspects is populated to a passing value, and every sub-section's
    /// extract carries a TTS AudioContentSha so the audio-source gate passes.
    /// </summary>
    private static async Task<RelationalSeed> SeedCanonicalRelationalAsync(LearnerDbContext db)
    {
        var paper = await AddPaperAsync(db, null);
        var now = DateTimeOffset.UtcNow;

        ListeningPart AddPart(ListeningPartCode code, int max)
        {
            var part = new ListeningPart
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paper.Id,
                PartCode = code,
                MaxRawScore = max,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Set<ListeningPart>().Add(part);
            return part;
        }

        var parts = new Dictionary<ListeningPartCode, ListeningPart>
        {
            [ListeningPartCode.A1] = AddPart(ListeningPartCode.A1, 12),
            [ListeningPartCode.A2] = AddPart(ListeningPartCode.A2, 12),
            [ListeningPartCode.B1] = AddPart(ListeningPartCode.B1, 1),
            [ListeningPartCode.B2] = AddPart(ListeningPartCode.B2, 1),
            [ListeningPartCode.B3] = AddPart(ListeningPartCode.B3, 1),
            [ListeningPartCode.B4] = AddPart(ListeningPartCode.B4, 1),
            [ListeningPartCode.B5] = AddPart(ListeningPartCode.B5, 1),
            [ListeningPartCode.B6] = AddPart(ListeningPartCode.B6, 1),
            [ListeningPartCode.C1] = AddPart(ListeningPartCode.C1, 6),
            [ListeningPartCode.C2] = AddPart(ListeningPartCode.C2, 6),
        };

        ListeningExtract AddExtract(ListeningPart part, int startMs, int endMs)
        {
            var isB = part.PartCode is ListeningPartCode.B1 or ListeningPartCode.B2 or ListeningPartCode.B3
                or ListeningPartCode.B4 or ListeningPartCode.B5 or ListeningPartCode.B6;
            var extract = new ListeningExtract
            {
                Id = Guid.NewGuid().ToString("N"),
                ListeningPartId = part.Id,
                DisplayOrder = 1,
                Kind = isB
                    ? ListeningExtractKind.Workplace
                    : part.PartCode is ListeningPartCode.C1 or ListeningPartCode.C2
                        ? ListeningExtractKind.Presentation
                        : ListeningExtractKind.Consultation,
                Title = $"Extract {part.PartCode}",
                SpeakersJson = "[]",
                TranscriptSegmentsJson = "[]",
                AudioStartMs = startMs,
                AudioEndMs = endMs,
                // TTS audio source so the audio-source gate passes.
                AudioContentSha = new string('a', 64),
                DifficultyRating = 3,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Set<ListeningExtract>().Add(extract);
            return extract;
        }

        var extracts = new Dictionary<ListeningPartCode, ListeningExtract>
        {
            [ListeningPartCode.A1] = AddExtract(parts[ListeningPartCode.A1], 0, 60_000),
            [ListeningPartCode.A2] = AddExtract(parts[ListeningPartCode.A2], 60_000, 120_000),
            [ListeningPartCode.B1] = AddExtract(parts[ListeningPartCode.B1], 120_000, 130_000),
            [ListeningPartCode.B2] = AddExtract(parts[ListeningPartCode.B2], 130_000, 140_000),
            [ListeningPartCode.B3] = AddExtract(parts[ListeningPartCode.B3], 140_000, 150_000),
            [ListeningPartCode.B4] = AddExtract(parts[ListeningPartCode.B4], 150_000, 160_000),
            [ListeningPartCode.B5] = AddExtract(parts[ListeningPartCode.B5], 160_000, 170_000),
            [ListeningPartCode.B6] = AddExtract(parts[ListeningPartCode.B6], 170_000, 180_000),
            [ListeningPartCode.C1] = AddExtract(parts[ListeningPartCode.C1], 180_000, 240_000),
            [ListeningPartCode.C2] = AddExtract(parts[ListeningPartCode.C2], 240_000, 300_000),
        };

        var questions = new List<ListeningQuestion>();
        var qNum = 1;
        void AddQuestions(ListeningPartCode code, int count, ListeningQuestionType qType)
        {
            var part = parts[code];
            for (var i = 0; i < count; i++)
            {
                var q = new ListeningQuestion
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PaperId = paper.Id,
                    ListeningPartId = part.Id,
                    ListeningExtractId = extracts[code].Id,
                    QuestionNumber = qNum++,
                    DisplayOrder = i + 1,
                    Points = 1,
                    QuestionType = qType,
                    Stem = "stem",
                    CorrectAnswerJson = qType == ListeningQuestionType.MultipleChoice3 ? "\"A\"" : "\"x\"",
                    SkillTag = qType == ListeningQuestionType.MultipleChoice3 ? "detail" : "note_completion",
                    TranscriptEvidenceText = "evidence",
                    TranscriptEvidenceStartMs = qNum * 1000,
                    TranscriptEvidenceEndMs = qNum * 1000 + 500,
                    DifficultyLevel = 3,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Set<ListeningQuestion>().Add(q);
                questions.Add(q);
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
                            DistractorCategory = k == 0 ? null : ListeningDistractorCategory.ReusedKeyword,
                        });
                    }
                }
            }
        }

        AddQuestions(ListeningPartCode.A1, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(ListeningPartCode.A2, 12, ListeningQuestionType.ShortAnswer);
        AddQuestions(ListeningPartCode.B1, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.B2, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.B3, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.B4, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.B5, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.B6, 1, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.C1, 6, ListeningQuestionType.MultipleChoice3);
        AddQuestions(ListeningPartCode.C2, 6, ListeningQuestionType.MultipleChoice3);

        return new RelationalSeed(paper, parts, extracts, questions);
    }

    [Fact]
    public async Task CanonicalRelationalPaper_PassesAllThreeWs7cRules()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.True(report.IsPublishReady, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Message}")));
        Assert.Equal("relational", report.Source);
        // None of the three new rules should fire on the canonical paper.
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_extract_cue_overlap");
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_preview_window_missing");
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_results_calc");
    }

    [Fact]
    public async Task OverlappingExtractCues_BlockPublish()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Add a second A1 extract whose window starts before the first A1
        // extract's window [0, 60_000) ends → an overlapping (non-monotonic)
        // cue ordering within Part A1's extracts. The validator only loads
        // extracts that at least one question links to, so repoint an A1
        // question (questions[0..11] are A1) onto the new extract.
        var a1Part = seed.Parts[ListeningPartCode.A1];
        var overlappingExtract = new ListeningExtract
        {
            Id = Guid.NewGuid().ToString("N"),
            ListeningPartId = a1Part.Id,
            DisplayOrder = 2,
            Kind = ListeningExtractKind.Consultation,
            Title = "A1 overlap",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            AudioStartMs = 30_000,   // < first A1 extract end (60_000) → overlap
            AudioEndMs = 90_000,
            AudioContentSha = new string('a', 64),
            DifficultyRating = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ListeningExtract>().Add(overlappingExtract);
        seed.Questions[0].ListeningExtractId = overlappingExtract.Id;
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_extract_cue_overlap" && i.Severity == "error");
    }

    [Fact]
    public async Task MissingPreviewWindow_IsAdvisoryWarning()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Author a policy row that zeroes the A1 preview window. The effective
        // resolution (policy column ?? default) then yields 0 for a present
        // part, which must block publish.
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            PreviewWindowMsA1 = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        // Preview-window timing is advisory under the uploaded-audio flow.
        Assert.Contains(report.Issues, i => i.Code == "listening_preview_window_missing" && i.Severity == "warning");
        Assert.DoesNotContain(report.Issues, i => i.Code == "listening_preview_window_missing" && i.Severity == "error");
    }

    [Fact]
    public async Task AuthoredPointsNotSummingTo42_BlocksPublish()
    {
        var (db, svc) = Build();
        var seed = await SeedCanonicalRelationalAsync(db);
        // Knock one item's Points to 0 → authored points sum to 41, not 42.
        seed.Questions[0].Points = 0;
        await db.SaveChangesAsync();

        var report = await svc.ValidatePaperAsync(seed.Paper.Id, default);

        Assert.False(report.IsPublishReady);
        Assert.Contains(report.Issues, i => i.Code == "listening_results_calc" && i.Severity == "error");
    }
}
