using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Admin-side authoring surface. Keeps the GrammarLesson row in sync with
/// its child tables (GrammarContentBlocks, GrammarExercises) and writes
/// audit events on every mutation.
/// </summary>
public interface IGrammarAuthoringService
{
    Task<string> CreateLessonAsync(string adminId, string adminName, AdminGrammarLessonFullCreateRequest req, CancellationToken ct);
    Task UpdateLessonAsync(string adminId, string adminName, string lessonId, AdminGrammarLessonFullUpdateRequest req, CancellationToken ct);
    Task ArchiveLessonAsync(string adminId, string adminName, string lessonId, CancellationToken ct);
    Task<object> GetLessonDetailAsync(string lessonId, CancellationToken ct);
    Task<object> ListLessonsAsync(string? topicId, string? examTypeCode, string? status, string? search, int page, int pageSize, CancellationToken ct);

    Task<string> CreateTopicAsync(string adminId, string adminName, AdminGrammarTopicCreateRequest req, CancellationToken ct);
    Task UpdateTopicAsync(string adminId, string adminName, string topicId, AdminGrammarTopicUpdateRequest req, CancellationToken ct);
    Task ArchiveTopicAsync(string adminId, string adminName, string topicId, CancellationToken ct);
    Task<object> ListTopicsAsync(string? examTypeCode, string? status, CancellationToken ct);
    Task<object> GetTopicDetailAsync(string topicId, CancellationToken ct);

    Task<AdminGrammarImportResult> BulkImportAsync(string adminId, string adminName, AdminGrammarImportRequest req, CancellationToken ct);
}

public sealed class GrammarAuthoringService(
    LearnerDbContext db,
    ILogger<GrammarAuthoringService> logger) : IGrammarAuthoringService
{
    // ── Topics ──────────────────────────────────────────────────────────

    public async Task<string> CreateTopicAsync(string adminId, string adminName, AdminGrammarTopicCreateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ExamTypeCode)) throw ApiException.Validation("INVALID", "ExamTypeCode is required.");
        if (string.IsNullOrWhiteSpace(req.Slug)) throw ApiException.Validation("INVALID", "Slug is required.");
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.Validation("INVALID", "Name is required.");

        var dup = await db.GrammarTopics.AnyAsync(t => t.ExamTypeCode == req.ExamTypeCode && t.Slug == req.Slug, ct);
        if (dup) throw ApiException.Conflict("TOPIC_EXISTS", $"Topic slug '{req.Slug}' already exists for {req.ExamTypeCode}.");

        var now = DateTimeOffset.UtcNow;
        var id = $"GTP-{Guid.NewGuid():N}"[..16];
        db.GrammarTopics.Add(new GrammarTopic
        {
            Id = id,
            ExamTypeCode = req.ExamTypeCode.ToLowerInvariant(),
            Slug = req.Slug.ToLowerInvariant(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            IconEmoji = req.IconEmoji?.Trim(),
            LevelHint = req.LevelHint ?? "all",
            SortOrder = req.SortOrder ?? 0,
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await WriteAuditAsync(adminId, adminName, "Created", "GrammarTopic", id, $"Created topic {req.Name}", ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task UpdateTopicAsync(string adminId, string adminName, string topicId, AdminGrammarTopicUpdateRequest req, CancellationToken ct)
    {
        var topic = await db.GrammarTopics.FirstOrDefaultAsync(t => t.Id == topicId, ct)
            ?? throw ApiException.NotFound("TOPIC_NOT_FOUND", $"Topic '{topicId}' not found.");

        if (req.Slug is not null) topic.Slug = req.Slug.ToLowerInvariant();
        if (req.Name is not null) topic.Name = req.Name.Trim();
        if (req.Description is not null) topic.Description = req.Description.Trim();
        if (req.IconEmoji is not null) topic.IconEmoji = req.IconEmoji.Trim();
        if (req.LevelHint is not null) topic.LevelHint = req.LevelHint;
        if (req.SortOrder is not null) topic.SortOrder = req.SortOrder.Value;
        if (req.Status is not null) topic.Status = req.Status;
        topic.UpdatedAt = DateTimeOffset.UtcNow;

        await WriteAuditAsync(adminId, adminName, "Updated", "GrammarTopic", topicId, $"Updated topic {topic.Name}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveTopicAsync(string adminId, string adminName, string topicId, CancellationToken ct)
    {
        var topic = await db.GrammarTopics.FirstOrDefaultAsync(t => t.Id == topicId, ct)
            ?? throw ApiException.NotFound("TOPIC_NOT_FOUND", $"Topic '{topicId}' not found.");
        topic.Status = "archived";
        topic.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync(adminId, adminName, "Archived", "GrammarTopic", topicId, $"Archived topic {topic.Name}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> ListTopicsAsync(string? examTypeCode, string? status, CancellationToken ct)
    {
        var q = db.GrammarTopics.AsQueryable();
        if (!string.IsNullOrWhiteSpace(examTypeCode)) q = q.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        var topics = await q.OrderBy(t => t.ExamTypeCode).ThenBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        return topics.Select(t => new
        {
            id = t.Id,
            examTypeCode = t.ExamTypeCode,
            slug = t.Slug,
            name = t.Name,
            description = t.Description,
            iconEmoji = t.IconEmoji,
            levelHint = t.LevelHint,
            sortOrder = t.SortOrder,
            status = t.Status,
            createdAt = t.CreatedAt,
            updatedAt = t.UpdatedAt,
        }).ToList();
    }

    public async Task<object> GetTopicDetailAsync(string topicId, CancellationToken ct)
    {
        var topic = await db.GrammarTopics.FirstOrDefaultAsync(t => t.Id == topicId, ct)
            ?? throw ApiException.NotFound("TOPIC_NOT_FOUND", $"Topic '{topicId}' not found.");

        var lessons = await db.GrammarLessons
            .Where(l => l.TopicId == topicId)
            .OrderBy(l => l.SortOrder)
            .Select(l => new { id = l.Id, title = l.Title, publishState = l.PublishState, status = l.Status, sortOrder = l.SortOrder })
            .ToListAsync(ct);

        return new
        {
            id = topic.Id,
            examTypeCode = topic.ExamTypeCode,
            slug = topic.Slug,
            name = topic.Name,
            description = topic.Description,
            iconEmoji = topic.IconEmoji,
            levelHint = topic.LevelHint,
            sortOrder = topic.SortOrder,
            status = topic.Status,
            lessons,
        };
    }

    // ── Lessons ─────────────────────────────────────────────────────────

    public async Task<string> CreateLessonAsync(string adminId, string adminName, AdminGrammarLessonFullCreateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw ApiException.Validation("INVALID", "Title is required.");

        var now = DateTimeOffset.UtcNow;
        var id = $"GRM-{Guid.NewGuid():N}"[..16];
        var lesson = new GrammarLesson
        {
            Id = id,
            ExamTypeCode = (req.ExamTypeCode ?? "oet").ToLowerInvariant(),
            TopicId = req.TopicId,
            Title = req.Title.Trim(),
            Description = (req.Description ?? "").Trim(),
            Level = req.Level ?? "intermediate",
            Category = req.Category ?? "",
            EstimatedMinutes = req.EstimatedMinutes ?? 15,
            SortOrder = req.SortOrder ?? 0,
            PrerequisiteLessonId = req.PrerequisiteLessonId,
            PrerequisiteLessonIds = JsonSerializer.Serialize(req.PrerequisiteLessonIds ?? new List<string>()),
            SourceProvenance = req.SourceProvenance ?? "",
            Status = "draft",
            PublishState = "draft",
            Version = 1,
            ContentHtml = "", // legacy — empty
            ExercisesJson = "[]", // legacy — empty
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GrammarLessons.Add(lesson);
        await db.SaveChangesAsync(ct);

        await ReplaceContentBlocksAsync(id, req.ContentBlocks, ct);
        await ReplaceExercisesAsync(id, req.Exercises, ct);

        await WriteAuditAsync(adminId, adminName, "Created", "GrammarLesson", id, $"Created grammar lesson: {req.Title}", ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task UpdateLessonAsync(string adminId, string adminName, string lessonId, AdminGrammarLessonFullUpdateRequest req, CancellationToken ct)
    {
        var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        if (req.TopicId is not null) lesson.TopicId = req.TopicId;
        if (req.Title is not null) lesson.Title = req.Title.Trim();
        if (req.Description is not null) lesson.Description = req.Description.Trim();
        if (req.Level is not null) lesson.Level = req.Level;
        if (req.Category is not null) lesson.Category = req.Category;
        if (req.EstimatedMinutes is not null) lesson.EstimatedMinutes = req.EstimatedMinutes.Value;
        if (req.SortOrder is not null) lesson.SortOrder = req.SortOrder.Value;
        if (req.PrerequisiteLessonId is not null) lesson.PrerequisiteLessonId = req.PrerequisiteLessonId;
        if (req.PrerequisiteLessonIds is not null)
            lesson.PrerequisiteLessonIds = JsonSerializer.Serialize(req.PrerequisiteLessonIds);
        if (req.SourceProvenance is not null) lesson.SourceProvenance = req.SourceProvenance;
        if (req.Status is not null) lesson.Status = req.Status;
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        lesson.Version += 1;

        if (req.ContentBlocks is not null) await ReplaceContentBlocksAsync(lessonId, req.ContentBlocks, ct);
        if (req.Exercises is not null) await ReplaceExercisesAsync(lessonId, req.Exercises, ct);

        await WriteAuditAsync(adminId, adminName, "Updated", "GrammarLesson", lessonId, $"Updated grammar lesson: {lesson.Title}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveLessonAsync(string adminId, string adminName, string lessonId, CancellationToken ct)
    {
        var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");
        lesson.Status = "archived";
        lesson.PublishState = "archived";
        lesson.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync(adminId, adminName, "Archived", "GrammarLesson", lessonId, $"Archived grammar lesson: {lesson.Title}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> GetLessonDetailAsync(string lessonId, CancellationToken ct)
    {
        var lesson = await db.GrammarLessons.FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        var topic = string.IsNullOrWhiteSpace(lesson.TopicId) ? null
            : await db.GrammarTopics.FirstOrDefaultAsync(t => t.Id == lesson.TopicId, ct);

        var blocks = await db.GrammarContentBlocks
            .Where(b => b.LessonId == lessonId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        var exercises = await db.GrammarExercises
            .Where(e => e.LessonId == lessonId)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct);

        return new
        {
            id = lesson.Id,
            examTypeCode = lesson.ExamTypeCode,
            topicId = lesson.TopicId,
            topicSlug = topic?.Slug,
            topicName = topic?.Name,
            title = lesson.Title,
            description = lesson.Description,
            level = lesson.Level,
            category = lesson.Category,
            estimatedMinutes = lesson.EstimatedMinutes,
            sortOrder = lesson.SortOrder,
            prerequisiteLessonId = lesson.PrerequisiteLessonId,
            prerequisiteLessonIds = lesson.PrerequisiteLessonIds,
            sourceProvenance = lesson.SourceProvenance,
            status = lesson.Status,
            publishState = lesson.PublishState,
            version = lesson.Version,
            publishedAt = lesson.PublishedAt,
            createdAt = lesson.CreatedAt,
            updatedAt = lesson.UpdatedAt,
            contentBlocks = blocks.Select(b => new { id = b.Id, sortOrder = b.SortOrder, type = b.Type, contentMarkdown = b.ContentMarkdown, content = b.ContentJson }).ToList(),
            exercises = exercises.Select(e => new
            {
                id = e.Id,
                sortOrder = e.SortOrder,
                type = e.Type,
                promptMarkdown = e.PromptMarkdown,
                options = e.OptionsJson,
                correctAnswer = e.CorrectAnswerJson,
                acceptedAnswers = e.AcceptedAnswersJson,
                explanationMarkdown = e.ExplanationMarkdown,
                difficulty = e.Difficulty,
                points = e.Points,
            }).ToList(),
        };
    }

    public async Task<object> ListLessonsAsync(string? topicId, string? examTypeCode, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var q = db.GrammarLessons.AsQueryable();
        if (!string.IsNullOrWhiteSpace(topicId)) q = q.Where(l => l.TopicId == topicId);
        if (!string.IsNullOrWhiteSpace(examTypeCode)) q = q.Where(l => l.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.PublishState == status || l.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(l => l.Title.Contains(search) || l.Description.Contains(search));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(l => l.SortOrder).ThenBy(l => l.Title)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                id = l.Id,
                examTypeCode = l.ExamTypeCode,
                topicId = l.TopicId,
                title = l.Title,
                description = l.Description,
                level = l.Level,
                estimatedMinutes = l.EstimatedMinutes,
                sortOrder = l.SortOrder,
                status = l.Status,
                publishState = l.PublishState,
                updatedAt = l.UpdatedAt,
            })
            .ToListAsync(ct);

        return new { total, page, pageSize, items };
    }

    public async Task<AdminGrammarImportResult> BulkImportAsync(string adminId, string adminName, AdminGrammarImportRequest req, CancellationToken ct)
    {
        int created = 0, skipped = 0;
        var errors = new List<string>();
        for (int i = 0; i < req.Lessons.Count; i++)
        {
            try
            {
                await CreateLessonAsync(adminId, adminName, req.Lessons[i], ct);
                created++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"#{i + 1}: {ex.Message}");
            }
        }
        return new AdminGrammarImportResult(created, skipped, errors);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task ReplaceContentBlocksAsync(string lessonId, List<AdminGrammarContentBlockDto>? blocks, CancellationToken ct)
    {
        var existing = await db.GrammarContentBlocks.Where(b => b.LessonId == lessonId).ToListAsync(ct);
        db.GrammarContentBlocks.RemoveRange(existing);

        if (blocks is null || blocks.Count == 0) return;

        int i = 0;
        foreach (var blk in blocks)
        {
            var id = string.IsNullOrWhiteSpace(blk.Id) ? $"GCB-{Guid.NewGuid():N}"[..16] : blk.Id!;
            db.GrammarContentBlocks.Add(new GrammarContentBlock
            {
                Id = id,
                LessonId = lessonId,
                SortOrder = blk.SortOrder > 0 ? blk.SortOrder : ++i,
                Type = string.IsNullOrWhiteSpace(blk.Type) ? "prose" : blk.Type,
                ContentMarkdown = GrammarContentSanitiser.Sanitise(blk.ContentMarkdown ?? ""),
                ContentJson = blk.Content?.GetRawText() ?? "{}",
            });
        }
    }

    private async Task ReplaceExercisesAsync(string lessonId, List<AdminGrammarExerciseDto>? exercises, CancellationToken ct)
    {
        var existing = await db.GrammarExercises.Where(e => e.LessonId == lessonId).ToListAsync(ct);
        db.GrammarExercises.RemoveRange(existing);

        if (exercises is null || exercises.Count == 0) return;

        int i = 0;
        foreach (var ex in exercises)
        {
            var id = string.IsNullOrWhiteSpace(ex.Id) ? $"GEX-{Guid.NewGuid():N}"[..16] : ex.Id!;
            var type = string.IsNullOrWhiteSpace(ex.Type) ? "mcq" : ex.Type;
            db.GrammarExercises.Add(new GrammarExercise
            {
                Id = id,
                LessonId = lessonId,
                SortOrder = ex.SortOrder > 0 ? ex.SortOrder : ++i,
                Type = type,
                PromptMarkdown = GrammarContentSanitiser.Sanitise(ex.PromptMarkdown ?? ""),
                OptionsJson = ex.Options?.GetRawText() ?? "[]",
                CorrectAnswerJson = ex.CorrectAnswer.GetRawText(),
                AcceptedAnswersJson = ex.AcceptedAnswers?.GetRawText() ?? "[]",
                ExplanationMarkdown = ex.ExplanationMarkdown ?? "",
                Difficulty = ex.Difficulty ?? "intermediate",
                Points = ex.Points ?? 1,
            });
        }
    }

    private Task WriteAuditAsync(string adminId, string adminName, string action, string resourceType, string resourceId, string details, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
        return Task.CompletedTask;
    }
}
