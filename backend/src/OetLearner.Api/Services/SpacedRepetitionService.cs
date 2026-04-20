using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>SM-2 spaced repetition algorithm for review item scheduling.</summary>
public class SpacedRepetitionService(LearnerDbContext db, ISpacedRepetitionScheduler scheduler)
{
    public async Task<object> GetDueItemsAsync(string userId, int limit, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await db.ReviewItems
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today)
            .OrderBy(r => r.DueDate)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(MapItem).ToList();
    }

    public async Task<object> GetReviewSummaryAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var total = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active", ct);
        var due = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today, ct);
        var dueToday = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate == today, ct);
        var mastered = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "mastered", ct);
        var upcoming = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate > today && r.DueDate <= today.AddDays(7), ct);

        return new { total, due, dueToday, mastered, upcoming };
    }

    public async Task<object> CreateReviewItemAsync(string userId, CreateReviewItemRequest request, CancellationToken ct)
    {
        var existing = await db.ReviewItems.FirstOrDefaultAsync(
            r => r.UserId == userId && r.SourceType == request.SourceType && r.SourceId == request.SourceId, ct);

        if (existing != null)
            return MapItem(existing);

        var item = new ReviewItem
        {
            Id = $"ri-{Guid.NewGuid():N}",
            UserId = userId,
            ExamTypeCode = request.ExamTypeCode,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SubtestCode = request.SubtestCode ?? string.Empty,
            CriterionCode = request.CriterionCode,
            QuestionJson = request.QuestionJson,
            AnswerJson = request.AnswerJson,
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = 0,
            ConsecutiveCorrect = 0,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "active"
        };
        db.ReviewItems.Add(item);
        await db.SaveChangesAsync(ct);
        return MapItem(item);
    }

    public async Task<object> SubmitReviewAsync(string userId, string itemId, int quality, CancellationToken ct)
    {
        // quality: 0-5 (SM-2 scale; 0-2 = fail, 3-5 = pass)
        if (quality < 0 || quality > 5)
            throw ApiException.Validation("INVALID_QUALITY", "Quality must be 0-5.");

        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        ApplySm2(item, quality);
        item.LastReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new { itemId, dueDate = item.DueDate, intervalDays = item.IntervalDays, easeFactor = item.EaseFactor };
    }

    public async Task<object> DeleteReviewItemAsync(string userId, string itemId, CancellationToken ct)
    {
        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        db.ReviewItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return new { deleted = true };
    }

    // ── SM-2 algorithm (delegated to shared scheduler) ──────────────────

    private void ApplySm2(ReviewItem item, int quality)
    {
        var update = scheduler.Schedule(
            new Sm2State(item.EaseFactor, item.IntervalDays, item.ReviewCount, item.ConsecutiveCorrect),
            quality);

        item.EaseFactor = update.EaseFactor;
        item.IntervalDays = update.IntervalDays;
        item.ReviewCount = update.ReviewCount;
        item.ConsecutiveCorrect = update.CorrectAnswer ? item.ConsecutiveCorrect + 1 : 0;
        item.DueDate = update.NextReviewDate;
    }

    private static object MapItem(ReviewItem r) => new
    {
        id = r.Id,
        sourceType = r.SourceType,
        sourceId = r.SourceId,
        subtestCode = r.SubtestCode,
        criterionCode = r.CriterionCode,
        questionJson = r.QuestionJson,
        answerJson = r.AnswerJson,
        easeFactor = r.EaseFactor,
        intervalDays = r.IntervalDays,
        reviewCount = r.ReviewCount,
        consecutiveCorrect = r.ConsecutiveCorrect,
        dueDate = r.DueDate,
        lastReviewedAt = r.LastReviewedAt,
        status = r.Status
    };
}

public record CreateReviewItemRequest(
    string ExamTypeCode,
    string SourceType,
    string SourceId,
    string? SubtestCode,
    string? CriterionCode,
    string QuestionJson,
    string AnswerJson);
