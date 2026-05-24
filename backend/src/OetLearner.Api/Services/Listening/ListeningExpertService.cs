using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ListeningExpertAttemptsPagedResponse(
    IReadOnlyList<ListeningExpertAttemptSummary> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ListeningExpertAttemptSummary(
    string AttemptId,
    string PaperId,
    string PaperTitle,
    string LearnerId,
    string LearnerDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    int? RawScore,
    int MaxRawScore,
    int? ScaledScore,
    bool HasExpertFeedback);

public sealed record ListeningExpertReviewBundle(
    ListeningExpertAttemptMeta Attempt,
    IReadOnlyList<ListeningExpertAnswerItem> Answers,
    ListeningExpertFeedbackDto? ExistingFeedback);

public sealed record ListeningExpertAttemptMeta(
    string AttemptId,
    string PaperId,
    string PaperTitle,
    string LearnerId,
    string LearnerDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    int? RawScore,
    int MaxRawScore,
    int? ScaledScore);

public sealed record ListeningExpertAnswerItem(
    string QuestionId,
    int QuestionNumber,
    string Stem,
    string PartCode,
    string? UserAnswer,
    string CorrectAnswer,
    bool? IsCorrect,
    string? TranscriptEvidence);

public sealed record ListeningExpertFeedbackDto(
    string FeedbackId,
    string ExpertId,
    string OverallFeedbackMarkdown,
    IReadOnlyList<ListeningPerQuestionFeedbackItem>? PerQuestionFeedback,
    IReadOnlyList<string>? RecommendedAreas,
    int? RawScoreOverride,
    string? ScoreOverrideReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ListeningPerQuestionFeedbackItem(int QuestionNumber, string Comment);

public sealed record ListeningExpertFeedbackRequest(
    string OverallFeedback,
    IReadOnlyList<ListeningPerQuestionFeedbackItem>? PerQuestionFeedback,
    IReadOnlyList<string>? RecommendedAreas,
    int? RawScoreOverride,
    string? ScoreOverrideReason);

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

public interface IListeningExpertService
{
    Task<ListeningExpertAttemptsPagedResponse> GetAttemptsPagedAsync(
        string expertId, int page, int pageSize, string? learnerId, string? paperId, CancellationToken ct);

    Task<ListeningExpertReviewBundle> GetReviewBundleAsync(
        string expertId, string attemptId, CancellationToken ct);

    Task<ListeningExpertFeedbackDto> SubmitFeedbackAsync(
        string expertId, string attemptId, ListeningExpertFeedbackRequest req, CancellationToken ct);

    Task<ListeningExpertFeedbackDto?> GetFeedbackAsync(
        string expertId, string attemptId, CancellationToken ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ListeningExpertService(LearnerDbContext db, ILogger<ListeningExpertService> logger)
    : IListeningExpertService
{
    private const int MaxPageSize = 100;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Paginated attempt list ────────────────────────────────────────────────

    public async Task<ListeningExpertAttemptsPagedResponse> GetAttemptsPagedAsync(
        string expertId, int page, int pageSize, string? learnerId, string? paperId, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.ListeningAttempts
            .AsNoTracking()
            .Where(a => a.Status == ListeningAttemptStatus.Submitted);

        if (!string.IsNullOrWhiteSpace(learnerId))
            query = query.Where(a => a.UserId == learnerId);

        if (!string.IsNullOrWhiteSpace(paperId))
            query = query.Where(a => a.PaperId == paperId);

        var total = await query.CountAsync(ct);

        var attempts = await query
            .OrderByDescending(a => a.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (attempts.Count == 0)
            return new ListeningExpertAttemptsPagedResponse([], total, page, pageSize);

        // Load paper titles
        var paperIds = attempts.Select(a => a.PaperId).Distinct().ToList();
        var paperTitles = await db.Set<ContentPaper>()
            .AsNoTracking()
            .Where(p => paperIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        // Load learner display names
        var learnerIds = attempts.Select(a => a.UserId).Distinct().ToList();
        var learnerNames = await db.Users
            .AsNoTracking()
            .Where(u => learnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // Load which attempts already have expert feedback
        var attemptIds = attempts.Select(a => a.Id).ToList();
        var feedbackAttemptIds = await db.ListeningExpertFeedbacks
            .AsNoTracking()
            .Where(f => attemptIds.Contains(f.AttemptId))
            .Select(f => f.AttemptId)
            .ToHashSetAsync(ct);

        var items = attempts.Select(a => new ListeningExpertAttemptSummary(
            AttemptId: a.Id,
            PaperId: a.PaperId,
            PaperTitle: paperTitles.GetValueOrDefault(a.PaperId, a.PaperId),
            LearnerId: a.UserId,
            LearnerDisplayName: learnerNames.GetValueOrDefault(a.UserId, a.UserId),
            StartedAt: a.StartedAt,
            SubmittedAt: a.SubmittedAt,
            RawScore: a.RawScore,
            MaxRawScore: a.MaxRawScore,
            ScaledScore: a.ScaledScore,
            HasExpertFeedback: feedbackAttemptIds.Contains(a.Id)
        )).ToList();

        return new ListeningExpertAttemptsPagedResponse(items, total, page, pageSize);
    }

    // ── Full review bundle ────────────────────────────────────────────────────

    public async Task<ListeningExpertReviewBundle> GetReviewBundleAsync(
        string expertId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.ListeningAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new KeyNotFoundException($"Listening attempt '{attemptId}' not found.");

        // Paper title
        var paper = await db.Set<ContentPaper>()
            .AsNoTracking()
            .Where(p => p.Id == attempt.PaperId)
            .Select(p => new { p.Id, p.Title })
            .FirstOrDefaultAsync(ct);

        // Learner display name
        var learner = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == attempt.UserId)
            .Select(u => new { u.Id, u.DisplayName })
            .FirstOrDefaultAsync(ct);

        // Answers joined with questions
        var answers = await db.ListeningAnswers
            .AsNoTracking()
            .Where(a => a.ListeningAttemptId == attemptId)
            .Join(db.ListeningQuestions.AsNoTracking(),
                a => a.ListeningQuestionId,
                q => q.Id,
                (a, q) => new { Answer = a, Question = q })
            .Join(db.ListeningParts.AsNoTracking(),
                x => x.Question.ListeningPartId,
                p => p.Id,
                (x, part) => new { x.Answer, x.Question, Part = part })
            .OrderBy(x => x.Question.QuestionNumber)
            .ToListAsync(ct);

        var answerItems = answers.Select(x => new ListeningExpertAnswerItem(
            QuestionId: x.Question.Id,
            QuestionNumber: x.Question.QuestionNumber,
            Stem: x.Question.Stem,
            PartCode: x.Part.PartCode.ToString(),
            UserAnswer: x.Answer.UserAnswerJson,
            CorrectAnswer: x.Question.CorrectAnswerJson,
            IsCorrect: x.Answer.IsCorrect,
            TranscriptEvidence: x.Question.TranscriptEvidenceText
        )).ToList();

        // Existing feedback (latest by SubmittedAt)
        var existing = await db.ListeningExpertFeedbacks
            .AsNoTracking()
            .Where(f => f.AttemptId == attemptId)
            .OrderByDescending(f => f.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var meta = new ListeningExpertAttemptMeta(
            AttemptId: attempt.Id,
            PaperId: attempt.PaperId,
            PaperTitle: paper?.Title ?? attempt.PaperId,
            LearnerId: attempt.UserId,
            LearnerDisplayName: learner?.DisplayName ?? attempt.UserId,
            StartedAt: attempt.StartedAt,
            SubmittedAt: attempt.SubmittedAt,
            RawScore: attempt.RawScore,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore);

        return new ListeningExpertReviewBundle(meta, answerItems, existing is null ? null : MapFeedback(existing));
    }

    // ── Submit / update feedback ──────────────────────────────────────────────

    public async Task<ListeningExpertFeedbackDto> SubmitFeedbackAsync(
        string expertId, string attemptId, ListeningExpertFeedbackRequest req, CancellationToken ct)
    {
        var attempt = await db.ListeningAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new KeyNotFoundException($"Listening attempt '{attemptId}' not found.");

        // Upsert — one feedback row per (attemptId, expertId)
        var existing = await db.ListeningExpertFeedbacks
            .FirstOrDefaultAsync(f => f.AttemptId == attemptId && f.ExpertId == expertId, ct);

        var now = DateTimeOffset.UtcNow;
        string perQJson = req.PerQuestionFeedback is not null
            ? JsonSerializer.Serialize(req.PerQuestionFeedback, JsonOpts)
            : "null";
        string areasJson = req.RecommendedAreas is not null
            ? JsonSerializer.Serialize(req.RecommendedAreas, JsonOpts)
            : "null";

        if (existing is null)
        {
            existing = new ListeningExpertFeedback
            {
                Id = Guid.NewGuid().ToString(),
                AttemptId = attemptId,
                ExpertId = expertId,
                OverallFeedbackMarkdown = req.OverallFeedback,
                PerQuestionFeedbackJson = req.PerQuestionFeedback is not null ? perQJson : null,
                RecommendedAreasJson = req.RecommendedAreas is not null ? areasJson : null,
                RawScoreOverride = req.RawScoreOverride,
                ScoreOverrideReason = req.ScoreOverrideReason,
                SubmittedAt = now,
            };
            db.ListeningExpertFeedbacks.Add(existing);
        }
        else
        {
            existing.OverallFeedbackMarkdown = req.OverallFeedback;
            existing.PerQuestionFeedbackJson = req.PerQuestionFeedback is not null ? perQJson : null;
            existing.RecommendedAreasJson = req.RecommendedAreas is not null ? areasJson : null;
            existing.RawScoreOverride = req.RawScoreOverride;
            existing.ScoreOverrideReason = req.ScoreOverrideReason;
            existing.UpdatedAt = now;
        }

        // Apply raw score override to attempt if provided
        if (req.RawScoreOverride.HasValue)
        {
            var overrideRecord = new
            {
                overriddenBy = expertId,
                rawScore = req.RawScoreOverride.Value,
                reason = req.ScoreOverrideReason ?? string.Empty,
                at = now,
            };
            attempt.HumanScoreOverridesJson = JsonSerializer.Serialize(overrideRecord, JsonOpts);

            // Recalculate attempt scores from override (MISSION CRITICAL: always via OetScoring)
            attempt.RawScore = req.RawScoreOverride.Value;
            attempt.ScaledScore = OetScoring.OetRawToScaled(req.RawScoreOverride.Value);

            logger.LogInformation(
                "Expert {ExpertId} applied raw score override {Score} (scaled {Scaled}) to listening attempt {AttemptId}",
                expertId, req.RawScoreOverride.Value, attempt.ScaledScore, attemptId);
        }

        await db.SaveChangesAsync(ct);
        return MapFeedback(existing);
    }

    // ── Get existing feedback ─────────────────────────────────────────────────

    public async Task<ListeningExpertFeedbackDto?> GetFeedbackAsync(
        string expertId, string attemptId, CancellationToken ct)
    {
        var feedback = await db.ListeningExpertFeedbacks
            .AsNoTracking()
            .Where(f => f.AttemptId == attemptId)
            .OrderByDescending(f => f.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        return feedback is null ? null : MapFeedback(feedback);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ListeningExpertFeedbackDto MapFeedback(ListeningExpertFeedback f)
    {
        var perQ = f.PerQuestionFeedbackJson is not null
            ? TryDeserialize<List<ListeningPerQuestionFeedbackItem>>(f.PerQuestionFeedbackJson)
            : null;
        var areas = f.RecommendedAreasJson is not null
            ? TryDeserialize<List<string>>(f.RecommendedAreasJson)
            : null;

        return new ListeningExpertFeedbackDto(
            FeedbackId: f.Id,
            ExpertId: f.ExpertId,
            OverallFeedbackMarkdown: f.OverallFeedbackMarkdown,
            PerQuestionFeedback: perQ,
            RecommendedAreas: areas,
            RawScoreOverride: f.RawScoreOverride,
            ScoreOverrideReason: f.ScoreOverrideReason,
            SubmittedAt: f.SubmittedAt,
            UpdatedAt: f.UpdatedAt);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return default; }
    }
}
