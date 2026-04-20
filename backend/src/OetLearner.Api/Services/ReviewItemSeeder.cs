using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Cross-skill retention seeder. MISSION CRITICAL — the ONLY permitted way
/// to create <see cref="ReviewItem"/> rows from anywhere in the codebase.
///
/// Idempotency contract: <c>(UserId, SourceType, SourceId)</c> is unique.
/// Calls that collide with an existing row return the existing id and perform
/// no mutations.
///
/// Spec: <c>docs/REVIEW-MODULE.md</c>.
/// </summary>
public interface IReviewItemSeeder
{
    Task<ReviewItemSeedResult> SeedAsync(
        string userId,
        ReviewItemSeedRequest request,
        CancellationToken ct);

    Task<IReadOnlyList<ReviewItemSeedResult>> SeedBatchAsync(
        string userId,
        IEnumerable<ReviewItemSeedRequest> requests,
        CancellationToken ct);

    // ── Convenience wrappers per source-type ────────────────────────────

    Task<ReviewItemSeedResult> SeedGrammarErrorAsync(string userId, string examTypeCode, string lessonId, string exerciseId, string title, string questionText, string correctAnswer, string explanation, string? exerciseType, CancellationToken ct);

    Task<ReviewItemSeedResult> SeedReadingMissAsync(string userId, string examTypeCode, string paperId, string questionId, string title, string questionText, string correctAnswer, string explanation, string? partCode, CancellationToken ct);

    Task<ReviewItemSeedResult> SeedListeningMissAsync(string userId, string examTypeCode, string attemptId, string questionId, string title, string questionText, string correctAnswer, string? transcriptSnippet, CancellationToken ct);

    Task<ReviewItemSeedResult?> SeedWritingIssueAsync(string userId, string examTypeCode, string evaluationId, string feedbackItemId, string criterionCode, string message, string? severity, string? suggestedFix, string? anchorSnippet, CancellationToken ct);

    Task<ReviewItemSeedResult?> SeedSpeakingIssueAsync(string userId, string examTypeCode, string evaluationId, string feedbackItemId, string criterionCode, string message, string? severity, string? suggestedFix, string? transcriptLineId, string? drillPrompt, CancellationToken ct);

    Task<ReviewItemSeedResult> SeedPronunciationFindingAsync(string userId, string examTypeCode, string attemptId, string phonemeKey, string title, string phoneme, string ruleId, string? tip, double score, CancellationToken ct);

    Task<ReviewItemSeedResult> SeedMockMissAsync(string userId, string examTypeCode, string mockReportId, string sectionCode, string questionId, string subtestCode, string title, string questionText, string correctAnswer, string? explanation, CancellationToken ct);
}

public sealed record ReviewItemSeedRequest(
    string ExamTypeCode,
    string SourceType,
    string SourceId,
    string SubtestCode,
    string? CriterionCode,
    string Title,
    string? PromptKind,
    object QuestionPayload,
    object AnswerPayload,
    object? RichContent);

public sealed record ReviewItemSeedResult(
    string Id,
    bool Created);

public sealed class ReviewItemSeeder(LearnerDbContext db, ILogger<ReviewItemSeeder> logger) : IReviewItemSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ReviewItemSeedResult> SeedAsync(string userId, ReviewItemSeedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId required", nameof(userId));
        if (!ReviewSourceTypes.IsValid(request.SourceType))
            throw new ArgumentOutOfRangeException(nameof(request), $"Unknown source type '{request.SourceType}'.");
        if (request.SourceType == ReviewSourceTypes.Vocabulary)
            throw new InvalidOperationException("Vocabulary items are projected from LearnerVocabulary; never seed them directly.");
        if (string.IsNullOrWhiteSpace(request.SourceId))
            throw new ArgumentException("sourceId required", nameof(request));

        var existing = await db.ReviewItems
            .FirstOrDefaultAsync(r => r.UserId == userId
                                      && r.SourceType == request.SourceType
                                      && r.SourceId == request.SourceId, ct);
        if (existing is not null)
        {
            return new ReviewItemSeedResult(existing.Id, Created: false);
        }

        var item = new ReviewItem
        {
            Id = $"ri-{Guid.NewGuid():N}",
            UserId = userId,
            ExamTypeCode = string.IsNullOrWhiteSpace(request.ExamTypeCode) ? "oet" : request.ExamTypeCode,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SubtestCode = string.IsNullOrWhiteSpace(request.SubtestCode) ? "general" : request.SubtestCode,
            CriterionCode = request.CriterionCode,
            Title = Truncate(request.Title, 180),
            PromptKind = request.PromptKind ?? ReviewSourceTypes.PromptKindFor(request.SourceType),
            QuestionJson = JsonSerializer.Serialize(request.QuestionPayload, JsonOptions),
            AnswerJson = JsonSerializer.Serialize(request.AnswerPayload, JsonOptions),
            RichContentJson = request.RichContent is null ? null : JsonSerializer.Serialize(request.RichContent, JsonOptions),
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = 0,
            ConsecutiveCorrect = 0,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "active",
        };

        db.ReviewItems.Add(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent seeder won the race — return the winning row.
            db.ReviewItems.Remove(item);
            var winner = await db.ReviewItems
                .FirstOrDefaultAsync(r => r.UserId == userId
                                          && r.SourceType == request.SourceType
                                          && r.SourceId == request.SourceId, ct);
            if (winner is not null)
            {
                return new ReviewItemSeedResult(winner.Id, Created: false);
            }
            logger.LogWarning(ex, "Unique violation on review seed but winning row not found (user={UserId} src={Source} id={Id}).", userId, request.SourceType, request.SourceId);
            throw;
        }

        return new ReviewItemSeedResult(item.Id, Created: true);
    }

    public async Task<IReadOnlyList<ReviewItemSeedResult>> SeedBatchAsync(
        string userId,
        IEnumerable<ReviewItemSeedRequest> requests,
        CancellationToken ct)
    {
        var results = new List<ReviewItemSeedResult>();
        foreach (var req in requests)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await SeedAsync(userId, req, ct));
        }
        return results;
    }

    // ── Convenience wrappers ────────────────────────────────────────────

    public Task<ReviewItemSeedResult> SeedGrammarErrorAsync(string userId, string examTypeCode, string lessonId, string exerciseId, string title, string questionText, string correctAnswer, string explanation, string? exerciseType, CancellationToken ct)
        => SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: ReviewSourceTypes.GrammarError,
            SourceId: $"{lessonId}:{exerciseId}",
            SubtestCode: "grammar",
            CriterionCode: exerciseType,
            Title: string.IsNullOrWhiteSpace(title) ? "Grammar exercise" : title,
            PromptKind: "grammar",
            QuestionPayload: new { text = questionText, lessonId, exerciseId },
            AnswerPayload: new { text = correctAnswer, explanation },
            RichContent: new { lessonId, exerciseId, exerciseType }), ct);

    public Task<ReviewItemSeedResult> SeedReadingMissAsync(string userId, string examTypeCode, string paperId, string questionId, string title, string questionText, string correctAnswer, string explanation, string? partCode, CancellationToken ct)
        => SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: ReviewSourceTypes.ReadingMiss,
            SourceId: $"{paperId}:{questionId}",
            SubtestCode: "reading",
            CriterionCode: partCode,
            Title: string.IsNullOrWhiteSpace(title) ? "Reading question" : title,
            PromptKind: "reading_miss",
            QuestionPayload: new { text = questionText, paperId, questionId },
            AnswerPayload: new { text = correctAnswer, explanation },
            RichContent: new { paperId, questionId, partCode }), ct);

    public Task<ReviewItemSeedResult> SeedListeningMissAsync(string userId, string examTypeCode, string attemptId, string questionId, string title, string questionText, string correctAnswer, string? transcriptSnippet, CancellationToken ct)
        => SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: ReviewSourceTypes.ListeningMiss,
            SourceId: $"{attemptId}:{questionId}",
            SubtestCode: "listening",
            CriterionCode: null,
            Title: string.IsNullOrWhiteSpace(title) ? "Listening question" : title,
            PromptKind: "listening_miss",
            QuestionPayload: new { text = questionText, attemptId, questionId },
            AnswerPayload: new { text = correctAnswer, transcriptSnippet },
            RichContent: new { attemptId, questionId, transcriptSnippet }), ct);

    public Task<ReviewItemSeedResult?> SeedWritingIssueAsync(string userId, string examTypeCode, string evaluationId, string feedbackItemId, string criterionCode, string message, string? severity, string? suggestedFix, string? anchorSnippet, CancellationToken ct)
        => SeedIssueIfSevereAsync(
            sourceType: ReviewSourceTypes.WritingIssue,
            userId: userId,
            examTypeCode: examTypeCode,
            evaluationId: evaluationId,
            feedbackItemId: feedbackItemId,
            criterionCode: criterionCode,
            subtestCode: "writing",
            promptKind: "writing_issue",
            severity: severity,
            title: TruncateTitle(message, "Writing issue"),
            questionPayload: new { text = message, criterionCode, anchorSnippet, evaluationId },
            answerPayload: new { text = suggestedFix ?? "Apply the suggested fix and re-write the anchored phrase.", severity },
            richContent: new { evaluationId, feedbackItemId, criterionCode, severity, anchorSnippet, suggestedFix },
            ct: ct);

    public Task<ReviewItemSeedResult?> SeedSpeakingIssueAsync(string userId, string examTypeCode, string evaluationId, string feedbackItemId, string criterionCode, string message, string? severity, string? suggestedFix, string? transcriptLineId, string? drillPrompt, CancellationToken ct)
        => SeedIssueIfSevereAsync(
            sourceType: ReviewSourceTypes.SpeakingIssue,
            userId: userId,
            examTypeCode: examTypeCode,
            evaluationId: evaluationId,
            feedbackItemId: feedbackItemId,
            criterionCode: criterionCode,
            subtestCode: "speaking",
            promptKind: "speaking_issue",
            severity: severity,
            title: TruncateTitle(message, "Speaking issue"),
            questionPayload: new { text = message, criterionCode, transcriptLineId, evaluationId },
            answerPayload: new { text = suggestedFix ?? "Re-record the section applying the suggested fix.", drillPrompt, severity },
            richContent: new { evaluationId, feedbackItemId, criterionCode, severity, transcriptLineId, drillPrompt, suggestedFix },
            ct: ct);

    public Task<ReviewItemSeedResult> SeedPronunciationFindingAsync(string userId, string examTypeCode, string attemptId, string phonemeKey, string title, string phoneme, string ruleId, string? tip, double score, CancellationToken ct)
        => SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: ReviewSourceTypes.PronunciationFinding,
            SourceId: $"{attemptId}:{phonemeKey}",
            SubtestCode: "speaking",
            CriterionCode: ruleId,
            Title: string.IsNullOrWhiteSpace(title) ? $"Pronunciation: /{phoneme}/" : title,
            PromptKind: "pronunciation",
            QuestionPayload: new { text = $"Practise /{phoneme}/", phoneme, attemptId },
            AnswerPayload: new { text = tip ?? "See pronunciation drill for guidance.", score },
            RichContent: new { attemptId, phoneme, ruleId, score, tip }), ct);

    public Task<ReviewItemSeedResult> SeedMockMissAsync(string userId, string examTypeCode, string mockReportId, string sectionCode, string questionId, string subtestCode, string title, string questionText, string correctAnswer, string? explanation, CancellationToken ct)
        => SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: ReviewSourceTypes.MockMiss,
            SourceId: $"{mockReportId}:{sectionCode}:{questionId}",
            SubtestCode: string.IsNullOrWhiteSpace(subtestCode) ? "mock" : subtestCode,
            CriterionCode: sectionCode,
            Title: string.IsNullOrWhiteSpace(title) ? $"Mock {subtestCode} question" : title,
            PromptKind: "mock_miss",
            QuestionPayload: new { text = questionText, mockReportId, sectionCode, questionId },
            AnswerPayload: new { text = correctAnswer, explanation },
            RichContent: new { mockReportId, sectionCode, questionId, subtestCode }), ct);

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<ReviewItemSeedResult?> SeedIssueIfSevereAsync(
        string sourceType,
        string userId,
        string examTypeCode,
        string evaluationId,
        string feedbackItemId,
        string criterionCode,
        string subtestCode,
        string promptKind,
        string? severity,
        string title,
        object questionPayload,
        object answerPayload,
        object richContent,
        CancellationToken ct)
    {
        if (!IsSevereEnough(severity))
            return null;

        var result = await SeedAsync(userId, new ReviewItemSeedRequest(
            ExamTypeCode: examTypeCode,
            SourceType: sourceType,
            SourceId: $"{evaluationId}:{feedbackItemId}",
            SubtestCode: subtestCode,
            CriterionCode: criterionCode,
            Title: title,
            PromptKind: promptKind,
            QuestionPayload: questionPayload,
            AnswerPayload: answerPayload,
            RichContent: richContent), ct);
        return result;
    }

    private static bool IsSevereEnough(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return false;
        return severity.Equals("medium", StringComparison.OrdinalIgnoreCase)
            || severity.Equals("high", StringComparison.OrdinalIgnoreCase)
            || severity.Equals("critical", StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateTitle(string message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message)) return fallback;
        var firstLine = message.Split('\n', 2)[0].Trim();
        return Truncate(firstLine, 120) ?? fallback;
    }

    private static string? Truncate(string? s, int max)
    {
        if (s is null) return null;
        return s.Length <= max ? s : s[..max];
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql 23505 = unique violation; SQLite SQLITE_CONSTRAINT_UNIQUE = 19/2067.
        var msg = ex.InnerException?.Message ?? string.Empty;
        return msg.Contains("23505", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
