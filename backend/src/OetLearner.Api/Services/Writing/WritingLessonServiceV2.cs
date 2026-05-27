using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingLessonV2QuizQuestion(string Id, string Question, IReadOnlyList<string> Options, int CorrectIndex, string? Explanation);

public sealed record WritingLessonV2View(
    Guid Id,
    string SubSkill,
    int OrderInCourse,
    string Title,
    string BodyMarkdown,
    string? VideoUrl,
    int EstimatedMinutes,
    IReadOnlyList<WritingLessonV2QuizQuestion> QuizQuestions,
    string Status,
    DateTimeOffset? CompletedAt,
    int? QuizScore,
    int QuizAttempts);

public sealed record WritingLessonV2QuizSubmission(IReadOnlyDictionary<string, int> Answers);

public sealed record WritingLessonV2CompletionResult(Guid LessonId, int CorrectCount, int TotalCount, double ScorePercent, DateTimeOffset CompletedAt);

public interface IWritingLessonServiceV2
{
    Task<IReadOnlyList<WritingLessonV2View>> ListAsync(string userId, string? subSkill, CancellationToken ct);
    Task<WritingLessonV2View?> GetAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingLessonV2CompletionResult> SubmitQuizAsync(string userId, Guid id, WritingLessonV2QuizSubmission submission, CancellationToken ct);
    Task<WritingLessonV2View> UpsertAsync(string adminId, WritingLessonV2View lesson, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingLessonListResponseV2> ListLessonsV2Async(string userId, string? subSkill, CancellationToken ct);
    Task<WritingLessonResponseV2?> GetLessonV2Async(string userId, Guid id, CancellationToken ct);
    Task<WritingLessonCompletionResponseV2?> CompleteLessonV2Async(string userId, Guid id, WritingLessonCompleteRequestV2 request, CancellationToken ct);
    Task<WritingLessonListResponseV2> AdminListLessonsAsync(string adminUserId, string? subSkill, string? status, CancellationToken ct);
    Task<WritingLessonResponseV2> AdminCreateLessonAsync(string adminUserId, WritingLessonUpsertRequest request, CancellationToken ct);
    Task<WritingLessonResponseV2?> AdminGetLessonAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingLessonResponseV2?> AdminUpdateLessonAsync(string adminUserId, Guid id, WritingLessonUpsertRequest request, CancellationToken ct);
    Task<bool> AdminDeleteLessonAsync(string adminUserId, Guid id, CancellationToken ct);
}

public sealed class WritingLessonServiceV2(LearnerDbContext db, TimeProvider clock) : IWritingLessonServiceV2
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingLessonV2View>> ListAsync(string userId, string? subSkill, CancellationToken ct)
    {
        var query = db.WritingLessonsV2.AsNoTracking().Where(l => l.Status == "published");
        if (!string.IsNullOrWhiteSpace(subSkill)) query = query.Where(l => l.SubSkill == subSkill);
        var lessons = await query.OrderBy(l => l.SubSkill).ThenBy(l => l.OrderInCourse).ToListAsync(ct);
        var ids = lessons.Select(l => l.Id).ToList();
        var completions = await db.WritingLessonCompletionsV2.AsNoTracking()
            .Where(c => c.UserId == userId && ids.Contains(c.LessonId))
            .ToDictionaryAsync(c => c.LessonId, c => c, ct);
        return lessons.Select(l => ToView(l, completions.GetValueOrDefault(l.Id))).ToList();
    }

    public async Task<WritingLessonV2View?> GetAsync(string userId, Guid id, CancellationToken ct)
    {
        var lesson = await db.WritingLessonsV2.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null) return null;
        var completion = await db.WritingLessonCompletionsV2.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.LessonId == id, ct);
        return ToView(lesson, completion);
    }

    public async Task<WritingLessonV2CompletionResult> SubmitQuizAsync(string userId, Guid id, WritingLessonV2QuizSubmission submission, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var lesson = await db.WritingLessonsV2.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw ApiException.NotFound("writing_lesson_v2_not_found", "Lesson was not found.");
        var quiz = DeserializeQuiz(lesson.QuizQuestionsJson);
        var total = quiz.Count;
        var correct = 0;
        foreach (var q in quiz)
        {
            if (submission.Answers.TryGetValue(q.Id, out var picked) && picked == q.CorrectIndex)
            {
                correct++;
            }
        }
        var score = total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total);
        var now = clock.GetUtcNow();
        var existing = await db.WritingLessonCompletionsV2.FirstOrDefaultAsync(c => c.UserId == userId && c.LessonId == id, ct);
        if (existing is null)
        {
            existing = new WritingLessonCompletionV2
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = id,
                CompletedAt = now,
                QuizScore = score,
                QuizAttempts = 1,
            };
            db.WritingLessonCompletionsV2.Add(existing);
        }
        else
        {
            existing.QuizAttempts += 1;
            if (score >= (existing.QuizScore ?? 0))
            {
                existing.QuizScore = score;
                existing.CompletedAt = now;
            }
        }
        await db.SaveChangesAsync(ct);
        return new WritingLessonV2CompletionResult(id, correct, total, score, existing.CompletedAt);
    }

    public async Task<WritingLessonV2View> UpsertAsync(string adminId, WritingLessonV2View lesson, CancellationToken ct)
    {
        _ = adminId;
        ArgumentNullException.ThrowIfNull(lesson);
        var entity = lesson.Id == Guid.Empty ? null : await db.WritingLessonsV2.FirstOrDefaultAsync(l => l.Id == lesson.Id, ct);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            entity = new WritingLessonV2 { Id = lesson.Id == Guid.Empty ? Guid.NewGuid() : lesson.Id, CreatedAt = now };
            db.WritingLessonsV2.Add(entity);
        }
        entity.SubSkill = lesson.SubSkill;
        entity.OrderInCourse = lesson.OrderInCourse;
        entity.Title = lesson.Title;
        entity.BodyMarkdown = lesson.BodyMarkdown;
        entity.VideoUrl = lesson.VideoUrl;
        entity.EstimatedMinutes = lesson.EstimatedMinutes;
        entity.QuizQuestionsJson = JsonSerializer.Serialize(lesson.QuizQuestions ?? Array.Empty<WritingLessonV2QuizQuestion>(), JsonOptions);
        entity.Status = lesson.Status;
        await db.SaveChangesAsync(ct);
        return ToView(entity, null);
    }

    private static WritingLessonV2View ToView(WritingLessonV2 row, WritingLessonCompletionV2? completion)
        => new(row.Id, row.SubSkill, row.OrderInCourse, row.Title, row.BodyMarkdown, row.VideoUrl, row.EstimatedMinutes,
            DeserializeQuiz(row.QuizQuestionsJson), row.Status, completion?.CompletedAt, completion?.QuizScore, completion?.QuizAttempts ?? 0);

    private static IReadOnlyList<WritingLessonV2QuizQuestion> DeserializeQuiz(string json)
    {
        try { return JsonSerializer.Deserialize<List<WritingLessonV2QuizQuestion>>(json, JsonOptions) ?? new(); }
        catch (JsonException) { return new List<WritingLessonV2QuizQuestion>(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingLessonListResponseV2> ListLessonsV2Async(string userId, string? subSkill, CancellationToken ct)
    {
        var rows = await ListAsync(userId, subSkill, ct);
        var completions = rows
            .Where(l => l.CompletedAt.HasValue)
            .Select(l => new WritingLessonCompletionResponseV2(l.Id, l.CompletedAt!.Value, l.QuizScore ?? 0, l.QuizAttempts))
            .ToList();
        return new WritingLessonListResponseV2(rows.Select(WritingV2ResponseMapper.ToResponse).ToList(), completions);
    }

    public async Task<WritingLessonResponseV2?> GetLessonV2Async(string userId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(userId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingLessonCompletionResponseV2?> CompleteLessonV2Async(string userId, Guid id, WritingLessonCompleteRequestV2 request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lesson = await db.WritingLessonsV2.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lesson is null) return null;
        var quiz = DeserializeQuiz(lesson.QuizQuestionsJson);
        // Map QuizAnswers (positional list) to the Id-keyed dictionary the legacy
        // SubmitQuizAsync expects, then use it to compute the same score.
        var dict = new Dictionary<string, int>();
        var answers = request.QuizAnswers ?? Array.Empty<int>();
        for (var i = 0; i < quiz.Count && i < answers.Count; i++)
        {
            dict[quiz[i].Id] = answers[i];
        }
        await SubmitQuizAsync(userId, id, new WritingLessonV2QuizSubmission(dict), ct);
        var completion = await db.WritingLessonCompletionsV2.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.LessonId == id, ct);
        if (completion is null) return null;
        return new WritingLessonCompletionResponseV2(id, completion.CompletedAt, completion.QuizScore ?? 0, completion.QuizAttempts);
    }

    public async Task<WritingLessonListResponseV2> AdminListLessonsAsync(string adminUserId, string? subSkill, string? status, CancellationToken ct)
    {
        _ = adminUserId;
        var query = db.WritingLessonsV2.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(subSkill)) query = query.Where(l => l.SubSkill == subSkill);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);
        var rows = await query.OrderBy(l => l.SubSkill).ThenBy(l => l.OrderInCourse).ToListAsync(ct);
        var items = rows.Select(l => WritingV2ResponseMapper.ToResponse(ToView(l, null))).ToList();
        return new WritingLessonListResponseV2(items, Array.Empty<WritingLessonCompletionResponseV2>());
    }

    public async Task<WritingLessonResponseV2> AdminCreateLessonAsync(string adminUserId, WritingLessonUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = new WritingLessonV2View(
            Id: Guid.Empty,
            SubSkill: request.SubSkill,
            OrderInCourse: request.OrderInCourse,
            Title: request.Title,
            BodyMarkdown: request.BodyMarkdown,
            VideoUrl: request.VideoUrl,
            EstimatedMinutes: request.EstimatedMinutes,
            QuizQuestions: (request.QuizQuestions ?? Array.Empty<WritingLessonQuizQuestionResponseV2>())
                .Select(q => new WritingLessonV2QuizQuestion(q.Id, q.Question, q.Options, q.CorrectIndex, q.Explanation))
                .ToList(),
            Status: string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status!,
            CompletedAt: null,
            QuizScore: null,
            QuizAttempts: 0);
        var saved = await UpsertAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingLessonResponseV2?> AdminGetLessonAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        _ = adminUserId;
        var lesson = await db.WritingLessonsV2.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        return lesson is null ? null : WritingV2ResponseMapper.ToResponse(ToView(lesson, null));
    }

    public async Task<WritingLessonResponseV2?> AdminUpdateLessonAsync(string adminUserId, Guid id, WritingLessonUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entity = await db.WritingLessonsV2.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (entity is null) return null;
        var view = new WritingLessonV2View(
            Id: id,
            SubSkill: request.SubSkill,
            OrderInCourse: request.OrderInCourse,
            Title: request.Title,
            BodyMarkdown: request.BodyMarkdown,
            VideoUrl: request.VideoUrl,
            EstimatedMinutes: request.EstimatedMinutes,
            QuizQuestions: (request.QuizQuestions ?? Array.Empty<WritingLessonQuizQuestionResponseV2>())
                .Select(q => new WritingLessonV2QuizQuestion(q.Id, q.Question, q.Options, q.CorrectIndex, q.Explanation))
                .ToList(),
            Status: string.IsNullOrWhiteSpace(request.Status) ? entity.Status : request.Status!,
            CompletedAt: null,
            QuizScore: null,
            QuizAttempts: 0);
        var saved = await UpsertAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<bool> AdminDeleteLessonAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        _ = adminUserId;
        var entity = await db.WritingLessonsV2.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (entity is null) return false;
        db.WritingLessonsV2.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
