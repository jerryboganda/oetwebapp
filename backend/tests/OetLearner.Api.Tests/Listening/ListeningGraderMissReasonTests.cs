using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening Module — Wave 1 of the OET Listening gap-fill plan. Exercises
/// the new <c>ListeningMissReason</c> classification emitted by
/// <c>ListeningGradingService.Evaluate</c> after a Part A short-answer miss.
///
/// Two surfaces are tested:
/// <list type="bullet">
///   <item>The pure <c>ClassifyMiss</c> / <c>StringsMatch</c> helpers
///   (table-driven via <c>[Theory]</c> — no DB).</item>
///   <item>The end-to-end <c>GradeAsync</c> path on an in-memory DbContext —
///   asserts <c>ListeningAnswer.MissReason</c> is persisted on the wrong
///   answer row and that the existing scoring path is unchanged.</item>
/// </list>
/// </summary>
public class ListeningGraderMissReasonTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ListeningQuestion ShortAnswerQ(
        string id, string canonical, string? variantsJson = null, bool caseSensitive = false)
        => new()
        {
            Id = id,
            PaperId = "p",
            ListeningPartId = "part",
            ListeningExtractId = "extract",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "?",
            CorrectAnswerJson = $"\"{canonical}\"",
            AcceptedSynonymsJson = variantsJson,
            CaseSensitive = caseSensitive,
        };

    // ─────────────────────────────────────────────────────────────────────
    // Pure classifier tests
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "cholesterol", ListeningMissReason.Empty)]
    [InlineData("   ", "cholesterol", ListeningMissReason.Empty)]
    [InlineData("cholestrol", "cholesterol", ListeningMissReason.SpellingError)]
    [InlineData("cholestrl", "cholesterol", ListeningMissReason.SpellingError)]
    [InlineData("aspirine", "aspirin", ListeningMissReason.SpellingError)]
    [InlineData("five", "5", ListeningMissReason.WrongNumber)]
    [InlineData("12", "5", ListeningMissReason.WrongNumber)]
    [InlineData("two aspirin tablets", "aspirin", ListeningMissReason.ExtraInfo)]
    [InlineData("aspirin tablets daily", "aspirin", ListeningMissReason.ExtraInfo)]
    [InlineData("cardiologist", "blood test", ListeningMissReason.Paraphrase)]
    public void Classifier_assigns_expected_reason(string user, string canonical, ListeningMissReason expected)
    {
        var q = ShortAnswerQ("q", canonical);
        var candidates = new List<string> { canonical };
        var got = ListeningGradingService.ClassifyMiss(
            user, candidates, q, paperAnswerMap: null,
            normalisation: ListeningGradingService.DefaultNormalisation);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void Classifier_detects_wrong_section_using_paper_answer_map()
    {
        var q1 = ShortAnswerQ("q1", "ibuprofen");
        var q2 = ShortAnswerQ("q2", "paracetamol");
        var map = ListeningGradingService.BuildPaperAnswerMap(
            new[] { q1, q2 }, ListeningGradingService.DefaultNormalisation);

        // Learner typed q2's canonical answer into q1's gap.
        var reason = ListeningGradingService.ClassifyMiss(
            "paracetamol", new List<string> { "ibuprofen" }, q1, map,
            ListeningGradingService.DefaultNormalisation);

        Assert.Equal(ListeningMissReason.WrongSection, reason);
    }

    [Fact]
    public void Classifier_returns_match_label_when_evaluate_matches()
    {
        var q = ShortAnswerQ("q", "ibuprofen");
        var ans = new ListeningAnswer { UserAnswerJson = "\"ibuprofen\"" };

        var (isCorrect, _, miss) = ListeningGradingService.Evaluate(
            q, ans, paperAnswerMap: null,
            normalisation: ListeningGradingService.DefaultNormalisation);

        Assert.True(isCorrect);
        Assert.Equal(ListeningMissReason.Match, miss);
    }

    [Fact]
    public void StringsMatch_fuzzy_levenshtein_one_accepts_single_typo()
    {
        Assert.True(ListeningGradingService.StringsMatch(
            "cholestrol", "cholesterol", caseSensitive: false,
            normalisation: "fuzzy_levenshtein_1"));
        // Two-edit gap is rejected by the strict pass-threshold matcher even
        // though it would still be classified as SpellingError downstream.
        Assert.False(ListeningGradingService.StringsMatch(
            "cholestrl", "cholesterol", caseSensitive: false,
            normalisation: "fuzzy_levenshtein_1"));
    }

    [Fact]
    public void StringsMatch_trim_collapse_default_normalisation()
    {
        Assert.True(ListeningGradingService.StringsMatch(
            "  the   aspirin   ", "the aspirin", caseSensitive: false,
            normalisation: ListeningGradingService.DefaultNormalisation));
    }

    // ─────────────────────────────────────────────────────────────────────
    // End-to-end grader persistence
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GradeAsync_persists_miss_reason_on_wrong_short_answer()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-miss",
            SubtestCode = "listening",
            Title = "T",
            Slug = "t",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-miss",
            PaperId = "paper-miss",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "extract-miss",
            ListeningPartId = "part-miss",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-miss",
            PaperId = "paper-miss",
            ListeningPartId = "part-miss",
            ListeningExtractId = "extract-miss",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Prescribed ____",
            CorrectAnswerJson = "\"cholesterol\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att-miss",
            UserId = "learner-1",
            PaperId = "paper-miss",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 1,
            Mode = ListeningAttemptMode.Exam,
            LastQuestionVersionMapJson = "{\"q-miss\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-miss",
            ListeningAttemptId = "att-miss",
            ListeningQuestionId = "q-miss",
            UserAnswerJson = "\"cholestrol\"",
        });
        await db.SaveChangesAsync();

        var result = await new ListeningGradingService(db).GradeAsync("att-miss", CancellationToken.None);

        Assert.Equal(0, result.RawScore);

        var reloaded = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-miss");
        Assert.False(reloaded.IsCorrect);
        Assert.Equal(ListeningMissReason.SpellingError, reloaded.MissReason);
    }

    [Fact]
    public async Task GradeAsync_persists_match_reason_on_correct_short_answer()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-ok",
            SubtestCode = "listening",
            Title = "T",
            Slug = "t",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-ok",
            PaperId = "paper-ok",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "ex-ok",
            ListeningPartId = "part-ok",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-ok",
            PaperId = "paper-ok",
            ListeningPartId = "part-ok",
            ListeningExtractId = "ex-ok",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "?",
            CorrectAnswerJson = "\"yes\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att-ok",
            UserId = "learner-1",
            PaperId = "paper-ok",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 1,
            Mode = ListeningAttemptMode.Exam,
            LastQuestionVersionMapJson = "{\"q-ok\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-ok",
            ListeningAttemptId = "att-ok",
            ListeningQuestionId = "q-ok",
            UserAnswerJson = "\"yes\"",
        });
        await db.SaveChangesAsync();

        await new ListeningGradingService(db).GradeAsync("att-ok", CancellationToken.None);

        var reloaded = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-ok");
        Assert.True(reloaded.IsCorrect);
        Assert.Equal(ListeningMissReason.Match, reloaded.MissReason);
    }

    [Fact]
    public async Task GradeAsync_persists_wrong_section_when_learner_typed_other_questions_answer()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-ws",
            SubtestCode = "listening",
            Title = "T",
            Slug = "t",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-ws",
            PaperId = "paper-ws",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 2,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "ex-ws",
            ListeningPartId = "part-ws",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.AddRange(
            new ListeningQuestion
            {
                Id = "q-a",
                PaperId = "paper-ws",
                ListeningPartId = "part-ws",
                ListeningExtractId = "ex-ws",
                QuestionNumber = 1,
                DisplayOrder = 1,
                Points = 1,
                QuestionType = ListeningQuestionType.ShortAnswer,
                Stem = "Medication ____",
                CorrectAnswerJson = "\"ibuprofen\"",
                AcceptedSynonymsJson = "[]",
                CaseSensitive = false,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new ListeningQuestion
            {
                Id = "q-b",
                PaperId = "paper-ws",
                ListeningPartId = "part-ws",
                ListeningExtractId = "ex-ws",
                QuestionNumber = 2,
                DisplayOrder = 2,
                Points = 1,
                QuestionType = ListeningQuestionType.ShortAnswer,
                Stem = "Alternative ____",
                CorrectAnswerJson = "\"paracetamol\"",
                AcceptedSynonymsJson = "[]",
                CaseSensitive = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att-ws",
            UserId = "learner-1",
            PaperId = "paper-ws",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 2,
            Mode = ListeningAttemptMode.Exam,
            LastQuestionVersionMapJson = "{\"q-a\":1,\"q-b\":1}",
        });
        db.ListeningAnswers.AddRange(
            new ListeningAnswer
            {
                Id = "ans-a",
                ListeningAttemptId = "att-ws",
                ListeningQuestionId = "q-a",
                UserAnswerJson = "\"paracetamol\"", // q-b's canonical
            },
            new ListeningAnswer
            {
                Id = "ans-b",
                ListeningAttemptId = "att-ws",
                ListeningQuestionId = "q-b",
                UserAnswerJson = "\"paracetamol\"",
            });
        await db.SaveChangesAsync();

        await new ListeningGradingService(db).GradeAsync("att-ws", CancellationToken.None);

        var ansA = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-a");
        var ansB = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-b");

        Assert.False(ansA.IsCorrect);
        Assert.Equal(ListeningMissReason.WrongSection, ansA.MissReason);
        Assert.True(ansB.IsCorrect);
        Assert.Equal(ListeningMissReason.Match, ansB.MissReason);
    }

    [Fact]
    public async Task GradeAsync_honours_policy_fuzzy_levenshtein_one_normalisation()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        // Singleton policy with fuzzy normalisation enabled.
        db.ListeningPolicies.Add(new ListeningPolicy
        {
            Id = "global",
            ShortAnswerNormalisation = "fuzzy_levenshtein_1",
            UpdatedAt = now,
        });
        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-fuzz",
            SubtestCode = "listening",
            Title = "T",
            Slug = "t",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-fuzz",
            PaperId = "paper-fuzz",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "ex-fuzz",
            ListeningPartId = "part-fuzz",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-fuzz",
            PaperId = "paper-fuzz",
            ListeningPartId = "part-fuzz",
            ListeningExtractId = "ex-fuzz",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "?",
            CorrectAnswerJson = "\"asthma\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att-fuzz",
            UserId = "learner-1",
            PaperId = "paper-fuzz",
            StartedAt = now,
            LastActivityAt = now,
            MaxRawScore = 1,
            Mode = ListeningAttemptMode.Exam,
            LastQuestionVersionMapJson = "{\"q-fuzz\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-fuzz",
            ListeningAttemptId = "att-fuzz",
            ListeningQuestionId = "q-fuzz",
            UserAnswerJson = "\"astma\"", // single deletion ('h') vs. canonical "asthma"
        });
        await db.SaveChangesAsync();

        var result = await new ListeningGradingService(db).GradeAsync("att-fuzz", CancellationToken.None);

        // Under fuzzy_levenshtein_1 the single-edit miss is accepted.
        Assert.Equal(1, result.RawScore);
        var reloaded = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-fuzz");
        Assert.True(reloaded.IsCorrect);
        Assert.Equal(ListeningMissReason.Match, reloaded.MissReason);
    }
}
