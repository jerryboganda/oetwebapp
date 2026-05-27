using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// 2026-05-27 audit fix — Listening rule L-R02.4 (Part A spelling: any
/// spelling error = the entire word is marked wrong). Contract test that
/// asserts a one-letter typo in a Part A short-answer scores 0 — there is
/// no fuzzy / partial-credit tolerance in exam mode.
///
/// This test paired with [Reading R04.1 / R04.5] which is stricter still.
/// </summary>
public class ListeningPartASpellingTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GradeAsync_marks_part_a_short_answer_with_one_letter_typo_as_wrong()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        var paper = new ContentPaper
        {
            Id = "paper-spelling-1",
            SubtestCode = "listening",
            Title = "Spelling test",
            Slug = "spelling",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = "{}",
        };
        var part = new ListeningPart
        {
            Id = "p-a1",
            PaperId = paper.Id,
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var extract = new ListeningExtract
        {
            Id = "e-a1",
            ListeningPartId = part.Id,
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var q = new ListeningQuestion
        {
            Id = "q-spelling",
            PaperId = paper.Id,
            ListeningPartId = part.Id,
            ListeningExtractId = extract.Id,
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Allergy: ____",
            // Canonical answer is "penicillin".
            CorrectAnswerJson = "\"penicillin\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var attempt = new ListeningAttempt
        {
            Id = "att-spelling",
            UserId = "u-1",
            PaperId = paper.Id,
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Exam,
            MaxRawScore = 1,
            LastQuestionVersionMapJson = "{\"q-spelling\":1}",
        };
        var ans = new ListeningAnswer
        {
            Id = "ans-spelling",
            ListeningAttemptId = attempt.Id,
            ListeningQuestionId = q.Id,
            // One-letter typo: missing the final 'n'.
            UserAnswerJson = "\"penicilli\"",
        };

        db.ContentPapers.Add(paper);
        db.ListeningParts.Add(part);
        db.ListeningExtracts.Add(extract);
        db.ListeningQuestions.Add(q);
        db.ListeningAttempts.Add(attempt);
        db.ListeningAnswers.Add(ans);
        await db.SaveChangesAsync();

        var grader = new ListeningGradingService(db);
        var result = await grader.GradeAsync(attempt.Id, CancellationToken.None);

        Assert.Equal(0, result.RawScore);
    }

    [Fact]
    public async Task GradeAsync_marks_canonical_answer_correct_regardless_of_case_or_whitespace()
    {
        // Same canonical answer "penicillin" but the candidate typed "  Penicillin  ".
        // L-R02.5 says minor variants (case + leading/trailing whitespace) ARE
        // accepted — exact spelling letters must still match.
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-spelling-2",
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
            Id = "p2",
            PaperId = "paper-spelling-2",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "e2",
            ListeningPartId = "p2",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "E",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q2",
            PaperId = "paper-spelling-2",
            ListeningPartId = "p2",
            ListeningExtractId = "e2",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Allergy: ____",
            CorrectAnswerJson = "\"penicillin\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = "att2",
            UserId = "u-1",
            PaperId = "paper-spelling-2",
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.InProgress,
            Mode = ListeningAttemptMode.Exam,
            MaxRawScore = 1,
            LastQuestionVersionMapJson = "{\"q2\":1}",
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans2",
            ListeningAttemptId = "att2",
            ListeningQuestionId = "q2",
            UserAnswerJson = "\"  Penicillin  \"",
        });
        await db.SaveChangesAsync();

        var grader = new ListeningGradingService(db);
        var result = await grader.GradeAsync("att2", CancellationToken.None);

        Assert.Equal(1, result.RawScore);
    }
}
