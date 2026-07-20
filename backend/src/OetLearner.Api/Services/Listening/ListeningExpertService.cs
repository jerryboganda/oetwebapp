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
    ListeningExpertFeedbackDto? ExistingFeedback,
    // Part A note bodies (A1/A2) so the tutor can review the candidate's gap
    // answers in the exact note layout. Empty when the paper has no Part A notes.
    IReadOnlyList<ListeningExpertPartANote>? PartANotes = null);

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
    string? TranscriptEvidence,
    // WORK-STREAM 7a — surface the distractor taxonomy + Part C speaker-attitude
    // to the tutor/expert review. Mirrors the learner review DTO field shapes in
    // lib/listening-api.ts so the expert and learner views read identically.
    // All three are null for items where the data was not authored / not applicable.
    string? SelectedDistractorCategory,
    string? SpeakerAttitude,
    IReadOnlyList<ListeningExpertOptionAnalysisItem>? OptionAnalysis,
    // Part A AI marking (Claude Sonnet 4.6) — advisory only. The tutor remains the
    // human authority; these surface the AI's per-gap judgement alongside the
    // deterministic IsCorrect. Null for MCQ items or not-yet-scored answers.
    string? AiVerdict = null,
    string? AiRationale = null);

/// <summary>Part A consultation note (`notesBody` in the `____` grammar) for one
/// sub-part, so the dedicated tutor view can render the candidate's answers in
/// context using the same renderer the learner sees.</summary>
public sealed record ListeningExpertPartANote(string PartCode, string NotesBody);

/// <summary>
/// Per-option distractor breakdown for an MCQ (Part B / Part C) item. Mirrors
/// the learner review's option-analysis shape (key/text/isCorrect/category/why)
/// so the tutor can explain why each distractor is wrong. Null collection for
/// short-answer items or items with no authored per-option metadata.
/// </summary>
public sealed record ListeningExpertOptionAnalysisItem(
    string Key,
    string Text,
    bool IsCorrect,
    string? DistractorCategory,
    string? WhyWrong);

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

public sealed record ListeningExpertMyReviewsPagedResponse(
    IReadOnlyList<ListeningExpertMyReviewSummary> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ListeningExpertMyReviewSummary(
    string FeedbackId,
    string AttemptId,
    string PaperId,
    string PaperTitle,
    string LearnerId,
    string LearnerDisplayName,
    int? RawScoreOverride,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? UpdatedAt);

public interface IListeningExpertService
{
    Task<ListeningExpertAttemptsPagedResponse> GetAttemptsPagedAsync(
        string expertId, int page, int pageSize, string? learnerId, string? paperId, string? search, CancellationToken ct);

    Task<ListeningExpertMyReviewsPagedResponse> GetMyReviewsPagedAsync(
        string expertId, int page, int pageSize, CancellationToken ct);

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
        string expertId, int page, int pageSize, string? learnerId, string? paperId, string? search, CancellationToken ct)
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

        // Free-text learner search: case-insensitive display-name match (ILike
        // on Postgres, with the same provider fallbacks ContentSearchService
        // uses) OR an exact user-id hit. Composes with — and never replaces —
        // the exact `learnerId` filter above.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = ToContainsPattern(term);
            if (db.Database.IsNpgsql())
            {
                query = query.Where(a => a.UserId == term
                    || db.Users.Any(u => u.Id == a.UserId && EF.Functions.ILike(u.DisplayName, pattern, @"\")));
            }
            else if (db.Database.IsInMemory())
            {
                query = query.Where(a => a.UserId == term
                    || db.Users.Any(u => u.Id == a.UserId && u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                query = query.Where(a => a.UserId == term
                    || db.Users.Any(u => u.Id == a.UserId && EF.Functions.Like(u.DisplayName, pattern, @"\")));
            }
        }

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

    // ── My reviews (paginated) ────────────────────────────────────────────────

    public async Task<ListeningExpertMyReviewsPagedResponse> GetMyReviewsPagedAsync(
        string expertId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.ListeningExpertFeedbacks
            .AsNoTracking()
            .Where(f => f.ExpertId == expertId);

        var total = await query.CountAsync(ct);

        var feedbacks = await query
            .OrderByDescending(f => f.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.AttemptId,
                f.RawScoreOverride,
                f.SubmittedAt,
                f.UpdatedAt,
                f.Attempt.PaperId,
                f.Attempt.UserId,
            })
            .ToListAsync(ct);

        if (feedbacks.Count == 0)
            return new ListeningExpertMyReviewsPagedResponse([], total, page, pageSize);

        // Load paper titles and learner names
        var paperIds = feedbacks.Select(f => f.PaperId).Distinct().ToList();
        var paperTitles = await db.Set<ContentPaper>()
            .AsNoTracking()
            .Where(p => paperIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title })
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        var learnerIds = feedbacks.Select(f => f.UserId).Distinct().ToList();
        var learnerNames = await db.Users
            .AsNoTracking()
            .Where(u => learnerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = feedbacks.Select(f => new ListeningExpertMyReviewSummary(
            FeedbackId: f.Id,
            AttemptId: f.AttemptId,
            PaperId: f.PaperId,
            PaperTitle: paperTitles.GetValueOrDefault(f.PaperId, f.PaperId),
            LearnerId: f.UserId,
            LearnerDisplayName: learnerNames.GetValueOrDefault(f.UserId, f.UserId),
            RawScoreOverride: f.RawScoreOverride,
            SubmittedAt: f.SubmittedAt,
            UpdatedAt: f.UpdatedAt
        )).ToList();

        return new ListeningExpertMyReviewsPagedResponse(items, total, page, pageSize);
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

        // Answers joined with questions + their part.
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

        // WORK-STREAM 7a — load MCQ options for the answered questions in one
        // round-trip so the per-option distractor breakdown can be built
        // without an Include-through-Join (which EF can silently drop) or an
        // N+1 per item. Keyed by questionId; grouped client-side.
        var questionIds = answers.Select(x => x.Question.Id).Distinct().ToList();
        var optionsByQuestion = (await db.Set<ListeningQuestionOption>()
                .AsNoTracking()
                .Where(o => questionIds.Contains(o.ListeningQuestionId))
                .ToListAsync(ct))
            .GroupBy(o => o.ListeningQuestionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ListeningQuestionOption>)g.ToList());

        var answerItems = answers.Select(x => new ListeningExpertAnswerItem(
            QuestionId: x.Question.Id,
            QuestionNumber: x.Question.QuestionNumber,
            Stem: x.Question.Stem,
            PartCode: x.Part.PartCode.ToString(),
            UserAnswer: x.Answer.UserAnswerJson,
            CorrectAnswer: x.Question.CorrectAnswerJson,
            IsCorrect: x.Answer.IsCorrect,
            TranscriptEvidence: x.Question.TranscriptEvidenceText,
            SelectedDistractorCategory: x.Answer.SelectedDistractorCategory is null
                ? null
                : DistractorCategoryString(x.Answer.SelectedDistractorCategory.Value),
            SpeakerAttitude: x.Question.SpeakerAttitude is null
                ? null
                : SpeakerAttitudeString(x.Question.SpeakerAttitude.Value),
            OptionAnalysis: BuildOptionAnalysis(
                optionsByQuestion.GetValueOrDefault(x.Question.Id)),
            AiVerdict: x.Answer.AiVerdict,
            AiRationale: x.Answer.AiRationale
        )).ToList();

        // Part A consultation notes (A1/A2) so the tutor can review gap answers in
        // the exact note layout. Joined via part → extract; only Part A carries a
        // notesBody. Ordered A1 then A2.
        var partANotes = (await db.ListeningExtracts.AsNoTracking()
                .Join(db.ListeningParts.AsNoTracking(),
                    e => e.ListeningPartId, p => p.Id, (e, p) => new { e, p })
                .Where(x => x.p.PaperId == attempt.PaperId
                    && x.e.NotesBodyMarkdown != null && x.e.NotesBodyMarkdown != "")
                .Select(x => new { x.e.DisplayOrder, x.e.NotesBodyMarkdown })
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync(ct))
            .Select((x, i) => new ListeningExpertPartANote(i == 0 ? "A1" : "A2", x.NotesBodyMarkdown!))
            .ToList();

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

    // Listening expert review currently uses an OPEN model — every expert can
    // submit feedback on every submitted attempt; the (attemptId, expertId)
    // upsert is the only key. There is no ExpertReviewAssignment row for
    // Listening to gate on. To compensate, EVERY raw-score override emits an
    // AuditEvent with before→after values so it is fully traceable.
    public async Task<ListeningExpertFeedbackDto> SubmitFeedbackAsync(
        string expertId, string attemptId, ListeningExpertFeedbackRequest req, CancellationToken ct)
    {
        // H17: a raw score override must carry a non-empty audit reason.
        if (req.RawScoreOverride.HasValue && string.IsNullOrWhiteSpace(req.ScoreOverrideReason))
        {
            throw new InvalidOperationException(
                "listening_override_reason_required: a Listening raw-score override requires ScoreOverrideReason.");
        }

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
            // B6: tag the JSON shape so the per-question grading-service ARRAY
            // shape and this whole-attempt OBJECT shape cannot silently
            // collide. Future readers MUST inspect `kind` first.
            var priorRaw = attempt.RawScore;
            var priorScaled = attempt.ScaledScore;
            var reasonTrimmed = req.ScoreOverrideReason!.Trim();
            var overrideRecord = new
            {
                kind = "expert_whole_attempt_override_v1",
                overriddenBy = expertId,
                rawScore = req.RawScoreOverride.Value,
                reason = reasonTrimmed,
                at = now,
            };
            attempt.HumanScoreOverridesJson = JsonSerializer.Serialize(overrideRecord, JsonOpts);

            // Recalculate attempt scores from override (MISSION CRITICAL: always via OetScoring)
            attempt.RawScore = req.RawScoreOverride.Value;
            attempt.ScaledScore = OetScoring.OetRawToScaled(req.RawScoreOverride.Value);

            // B1: emit an audit row for every override so the missing
            // ExpertReviewAssignment model is compensated by traceability.
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = expertId,
                ActorName = expertId,
                Action = "listening.score.override",
                ResourceType = "ListeningAttempt",
                ResourceId = attempt.Id,
                Details = JsonSerializer.Serialize(new
                {
                    attemptId = attempt.Id,
                    learnerId = attempt.UserId,
                    paperId = attempt.PaperId,
                    overrideRawScore = req.RawScoreOverride.Value,
                    overrideScaledScore = attempt.ScaledScore,
                    priorRawScore = priorRaw,
                    priorScaledScore = priorScaled,
                    reason = reasonTrimmed,
                }, JsonOpts),
            });

            logger.LogInformation(
                "Expert {ExpertId} applied raw score override {Score} (scaled {Scaled}) to listening attempt {AttemptId}; prior raw={PriorRaw}, scaled={PriorScaled}",
                expertId, req.RawScoreOverride.Value, attempt.ScaledScore, attemptId, priorRaw, priorScaled);
        }

        attempt.RowVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("listening_attempt_concurrent_update",
                "This attempt was modified by another process. Please retry.");
        }
        return MapFeedback(existing);
    }

    // ── Get existing feedback ─────────────────────────────────────────────────

    public async Task<ListeningExpertFeedbackDto?> GetFeedbackAsync(
        string expertId, string attemptId, CancellationToken ct)
    {
        // H16: Filter by expertId — this endpoint is under /expert/ so the
        // calling expert should only retrieve their own feedback row.
        var feedback = await db.ListeningExpertFeedbacks
            .AsNoTracking()
            .Where(f => f.AttemptId == attemptId && f.ExpertId == expertId)
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

    /// <summary>Escapes LIKE wildcards and wraps the term for a contains match
    /// (same escaping as ContentSearchService.ToContainsPattern).</summary>
    private static string ToContainsPattern(string value)
    {
        var escaped = value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    // ── WORK-STREAM 7a: distractor / speaker-attitude surfacing ───────────────

    /// <summary>
    /// Builds the per-option distractor breakdown for an MCQ question. Returns
    /// null for short-answer items (no options) so the tutor UI can skip the
    /// section entirely. Options are ordered by <c>DisplayOrder</c> to match the
    /// order the candidate saw them, and the option key (A/B/C) is taken from the
    /// authored <c>OptionKey</c>.
    /// </summary>
    private static IReadOnlyList<ListeningExpertOptionAnalysisItem>? BuildOptionAnalysis(
        IReadOnlyList<ListeningQuestionOption>? options)
    {
        if (options is null || options.Count == 0) return null;

        return options
            .OrderBy(option => option.DisplayOrder)
            .Select(option => new ListeningExpertOptionAnalysisItem(
                Key: option.OptionKey,
                Text: option.Text,
                IsCorrect: option.IsCorrect,
                DistractorCategory: option.DistractorCategory is null
                    ? null
                    : DistractorCategoryString(option.DistractorCategory.Value),
                WhyWrong: string.IsNullOrWhiteSpace(option.WhyWrongMarkdown)
                    ? null
                    : option.WhyWrongMarkdown))
            .ToList();
    }

    // Snake_case projections mirror ListeningLearnerService so the expert and
    // learner reviews use an identical vocabulary on the wire.
    private static string DistractorCategoryString(ListeningDistractorCategory category) => category switch
    {
        ListeningDistractorCategory.TooStrong => "too_strong",
        ListeningDistractorCategory.TooWeak => "too_weak",
        ListeningDistractorCategory.WrongSpeaker => "wrong_speaker",
        ListeningDistractorCategory.OppositeMeaning => "opposite_meaning",
        ListeningDistractorCategory.ReusedKeyword => "reused_keyword",
        ListeningDistractorCategory.OutOfScope => "out_of_scope",
        _ => category.ToString(),
    };

    private static string SpeakerAttitudeString(ListeningSpeakerAttitude attitude) => attitude switch
    {
        ListeningSpeakerAttitude.Concerned => "concerned",
        ListeningSpeakerAttitude.Optimistic => "optimistic",
        ListeningSpeakerAttitude.Doubtful => "doubtful",
        ListeningSpeakerAttitude.Critical => "critical",
        ListeningSpeakerAttitude.Neutral => "neutral",
        ListeningSpeakerAttitude.Other => "other",
        _ => "other",
    };
}
