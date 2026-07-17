using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public interface IWritingLessonService
{
    Task<IReadOnlyList<WritingLessonListItemResponse>> ListLessonsAsync(string userId, CancellationToken ct);
    Task<WritingLessonDetailResponse?> GetLessonAsync(string userId, string slug, CancellationToken ct);
    Task<WritingLessonProgressResponse> UpdateProgressAsync(string userId, string slug, WritingLessonProgressRequest request, CancellationToken ct);
}

public sealed class WritingLessonService(LearnerDbContext db, TimeProvider clock) : IWritingLessonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingLessonListItemResponse>> ListLessonsAsync(string userId, CancellationToken ct)
    {
        await EnsureStarterLessonsAsync(ct);
        var lessons = await db.WritingLessons.AsNoTracking()
            .Where(l => l.IsPublished)
            .OrderBy(l => l.OrderIndex)
            .ToListAsync(ct);
        var progress = await LoadProgressAsync(userId, lessons.Select(l => l.Id), ct);

        return lessons.Select(lesson => ToListItem(lesson, progress, IsUnlocked(lesson, progress))).ToList();
    }

    public async Task<WritingLessonDetailResponse?> GetLessonAsync(string userId, string slug, CancellationToken ct)
    {
        await EnsureStarterLessonsAsync(ct);
        var lessons = await db.WritingLessons.AsNoTracking()
            .Where(l => l.IsPublished)
            .OrderBy(l => l.OrderIndex)
            .ToListAsync(ct);
        var lesson = lessons.FirstOrDefault(l => l.Slug == slug);
        if (lesson is null) return null;

        var progress = await LoadProgressAsync(userId, lessons.Select(l => l.Id), ct);
        var index = lessons.FindIndex(l => l.Id == lesson.Id);
        return ToDetail(
            lesson,
            progress,
            IsUnlocked(lesson, progress),
            index > 0 ? lessons[index - 1].Slug : null,
            index >= 0 && index < lessons.Count - 1 ? lessons[index + 1].Slug : null);
    }

    public async Task<WritingLessonProgressResponse> UpdateProgressAsync(string userId, string slug, WritingLessonProgressRequest request, CancellationToken ct)
    {
        await EnsureStarterLessonsAsync(ct);
        var lesson = await db.WritingLessons.FirstOrDefaultAsync(l => l.Slug == slug && l.IsPublished, ct);
        if (lesson is null) throw ApiException.NotFound("writing_lesson_not_found", "Writing lesson was not found.");

        var progressScope = new List<Guid> { lesson.Id };
        if (lesson.PrerequisiteLessonId is Guid prerequisiteLessonId) progressScope.Add(prerequisiteLessonId);
        var existingProgress = await LoadProgressAsync(userId, progressScope, ct);
        if (!IsUnlocked(lesson, existingProgress))
        {
            throw ApiException.Conflict("writing_lesson_locked", "Complete the previous Writing lesson before opening this one.");
        }

        var progress = await db.LearnerWritingLessonProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lesson.Id, ct);
        if (progress is null)
        {
            progress = new LearnerWritingLessonProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LessonId = lesson.Id,
            };
            db.LearnerWritingLessonProgresses.Add(progress);
        }

        if (request.BodyRead.HasValue) progress.BodyRead = request.BodyRead.Value;
        if (request.DrillCompleted.HasValue) progress.DrillCompleted = request.DrillCompleted.Value;
        if (request.QuizScore.HasValue)
        {
            if (request.QuizScore.Value is < 0 or > 5)
            {
                throw ApiException.Validation("writing_lesson_quiz_score_invalid", "Quiz score must be between 0 and 5.");
            }
            progress.QuizScore = request.QuizScore.Value;
            progress.QuizAttempts += 1;
        }

        progress.UpdatedAt = clock.GetUtcNow();
        if (progress.BodyRead && progress.DrillCompleted && progress.QuizScore >= 4)
        {
            progress.CompletedAt ??= progress.UpdatedAt;
        }
        else
        {
            progress.CompletedAt = null;
        }

        await db.SaveChangesAsync(ct);
        return ToProgress(progress);
    }

    private async Task<Dictionary<Guid, LearnerWritingLessonProgress>> LoadProgressAsync(string userId, IEnumerable<Guid> lessonIds, CancellationToken ct)
    {
        var ids = lessonIds.ToList();
        return await db.LearnerWritingLessonProgresses.AsNoTracking()
            .Where(p => p.UserId == userId && ids.Contains(p.LessonId))
            .ToDictionaryAsync(p => p.LessonId, ct);
    }

    private static bool IsUnlocked(WritingLesson lesson, IReadOnlyDictionary<Guid, LearnerWritingLessonProgress> progress)
    {
        if (lesson.PrerequisiteLessonId is null) return true;
        return progress.TryGetValue(lesson.PrerequisiteLessonId.Value, out var prerequisite)
            && prerequisite.CompletedAt is not null;
    }

    private static WritingLessonListItemResponse ToListItem(
        WritingLesson lesson,
        IReadOnlyDictionary<Guid, LearnerWritingLessonProgress> progress,
        bool isUnlocked)
        => new(
            lesson.Id,
            lesson.Slug,
            lesson.Title,
            lesson.SkillCode,
            lesson.OrderIndex,
            lesson.EstimatedMinutes,
            isUnlocked,
            progress.TryGetValue(lesson.Id, out var learnerProgress) ? ToProgress(learnerProgress) : null);

    private static WritingLessonDetailResponse ToDetail(
        WritingLesson lesson,
        IReadOnlyDictionary<Guid, LearnerWritingLessonProgress> progress,
        bool isUnlocked,
        string? previousSlug,
        string? nextSlug)
        => new(
            lesson.Id,
            lesson.Slug,
            lesson.Title,
            lesson.SkillCode,
            lesson.OrderIndex,
            lesson.EstimatedMinutes,
            lesson.BodyMarkdownEn,
            lesson.DrillPrompt,
            ParseQuiz(lesson.QuizJson),
            previousSlug,
            nextSlug,
            isUnlocked,
            progress.TryGetValue(lesson.Id, out var learnerProgress) ? ToProgress(learnerProgress) : null);

    private static WritingLessonProgressResponse ToProgress(LearnerWritingLessonProgress progress)
        => new(progress.BodyRead, progress.DrillCompleted, progress.QuizScore, progress.QuizAttempts, progress.CompletedAt);

    private static IReadOnlyList<WritingLessonQuizQuestionResponse> ParseQuiz(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<WritingLessonQuizQuestionResponse>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task EnsureStarterLessonsAsync(CancellationToken ct)
    {
        var existing = await db.WritingLessons.ToDictionaryAsync(l => l.Slug, ct);
        var previousId = (Guid?)null;
        var changed = false;
        foreach (var starter in StarterLessons())
        {
            if (!existing.TryGetValue(starter.Slug, out var lesson))
            {
                starter.PrerequisiteLessonId = previousId;
                lesson = starter;
                db.WritingLessons.Add(lesson);
                changed = true;
            }
            else if (lesson.PrerequisiteLessonId != previousId)
            {
                lesson.PrerequisiteLessonId = previousId;
                lesson.UpdatedAt = clock.GetUtcNow();
                changed = true;
            }

            previousId = lesson.Id;
        }

        if (!changed) return;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<WritingLesson>().Where(e => e.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private IReadOnlyList<WritingLesson> StarterLessons()
    {
        var now = clock.GetUtcNow();
        var rows = new[]
        {
            ("w1-case-note-analysis", "W1", "Case note analysis", "Mark essential clinical facts, audience-relevant context, and facts to omit."),
            ("w2-purpose-articulation", "W2", "Purpose articulation", "Write the opening purpose in one precise sentence before drafting the body."),
            ("w3-content-selection", "W3", "Content selection", "Choose only the case-note details that support the reader's next action."),
            ("w4-paraphrasing", "W4", "Paraphrasing", "Convert fragmented notes into concise professional prose without adding facts."),
            ("w5-genre-conventions", "W5", "Genre conventions", "Match referral, discharge, transfer, and information-letter conventions."),
            ("w6-style-register", "W6", "Style and register", "Keep tone respectful, clinically useful, and reader-centred."),
            ("w7-language-accuracy", "W7", "Language accuracy", "Control sentence grammar, punctuation, tense, and agreement under time pressure."),
            ("w8-time-management", "W8", "Time management", "Use the 5-minute reading window and 40-minute writing window deliberately."),
        };

        return rows.Select((row, index) => new WritingLesson
        {
            Id = Guid.Parse($"10000000-0000-0000-0000-{(index + 1):000000000000}"),
            Slug = row.Item1,
            SkillCode = row.Item2,
            Title = row.Item3,
            OrderIndex = index + 1,
            EstimatedMinutes = index == 7 ? 20 : 25,
            BodyMarkdownEn = $"## {row.Item3}\n\nStarter lesson shell for {row.Item2}. Replace this editable text with Dr Ahmed-approved Writing teaching content before treating it as final curriculum.\n\n### Focus\n\n{row.Item4}\n\n### Practice\n\nOpen a recent Writing task, identify one sentence or case-note decision connected to {row.Item2}, then revise it against the canon.",
            DrillPrompt = $"Apply {row.Item2} to one paragraph from your latest Writing attempt and save the improved version in your notes.",
            QuizJson = JsonSerializer.Serialize(new[]
            {
                new WritingLessonQuizQuestionResponse("q1", $"What is the main focus of {row.Item2}?", [row.Item4, "Adding more medical facts", "Using longer sentences"]),
            }, JsonOptions),
            IsPublished = true,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
    }
}