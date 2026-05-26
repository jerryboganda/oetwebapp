using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain.Classes;

// ApiException lives in the parent OetLearner.Api.Services namespace; our
// own namespace (OetLearner.Api.Services.Classes) implicitly imports it,
// but make the reference explicit for clarity.

namespace OetLearner.Api.Services.Classes;

/// <summary>
/// Submission + aggregation for learner-facing class feedback. Idempotent on
/// (sessionId, userId) — a learner can resubmit and the existing row is
/// updated rather than duplicated. See plan §9.5.
/// </summary>
public sealed class ClassFeedbackService(
    LearnerDbContext db,
    TimeProvider timeProvider) : IClassFeedbackService
{
    public async Task<ClassFeedbackDto> SubmitAsync(
        string sessionId,
        string userId,
        ClassFeedbackSubmitRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        if (request.Rating < 1 || request.Rating > 5)
        {
            throw ApiException.Validation("class_feedback_rating_invalid", "Rating must be between 1 and 5.");
        }

        var session = await db.LiveClassSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        // Only learners who actually enrolled (active, attended, no-show, refunded)
        // can submit feedback. This guards against feedback brigading.
        var hasEnrollment = await db.LiveClassEnrollments.AsNoTracking()
            .AnyAsync(enrollment => enrollment.ClassSessionId == sessionId && enrollment.UserId == userId, ct);
        if (!hasEnrollment)
        {
            throw ApiException.Forbidden("class_feedback_forbidden", "Only enrolled learners can submit feedback.");
        }

        var now = timeProvider.GetUtcNow();
        var existing = await db.ClassFeedbacks.FirstOrDefaultAsync(
            f => f.ClassSessionId == sessionId && f.UserId == userId, ct);
        var trimmedComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        if (existing is not null)
        {
            // Idempotent update — same (sessionId, userId) → same row, no new
            // record. This also satisfies the Idempotency-Key contract: the
            // unique index on (ClassSessionId, UserId) is the natural key here,
            // so re-posting with the same header is safe and produces the same
            // server state.
            existing.Rating = request.Rating;
            existing.Comment = trimmedComment;
            existing.RecommendToFriend = request.RecommendToFriend ?? existing.RecommendToFriend;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return MapFeedback(existing);
        }

        var feedback = new ClassFeedback
        {
            Id = $"CFB-{Guid.NewGuid():N}",
            ClassSessionId = sessionId,
            UserId = userId,
            Rating = request.Rating,
            Comment = trimmedComment,
            RecommendToFriend = request.RecommendToFriend ?? false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ClassFeedbacks.Add(feedback);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race with another concurrent insert — re-read and return the
            // existing row to keep the operation idempotent.
            var raced = await db.ClassFeedbacks.AsNoTracking()
                .FirstOrDefaultAsync(f => f.ClassSessionId == sessionId && f.UserId == userId, ct);
            if (raced is null)
            {
                throw;
            }

            return MapFeedback(raced);
        }

        _ = idempotencyKey; // header is informational; natural key drives idempotency.
        return MapFeedback(feedback);
    }

    public async Task<ClassFeedbackAggregateDto> GetForSessionAsync(string sessionId, int recentLimit, CancellationToken ct)
    {
        var limit = Math.Clamp(recentLimit, 0, 100);
        var entries = await db.ClassFeedbacks.AsNoTracking()
            .Where(f => f.ClassSessionId == sessionId)
            .ToListAsync(ct);
        return AggregateInternal(entries, limit);
    }

    public async Task<ClassFeedbackAggregateDto> GetForTutorAsync(string tutorUserId, int recentLimit, CancellationToken ct)
    {
        var limit = Math.Clamp(recentLimit, 0, 100);

        // Sessions owned by the tutor are those whose LiveClass.TutorProfileId
        // points to a PrivateSpeakingTutorProfile.ExpertUserId == tutorUserId.
        var sessionIds = await db.LiveClassSessions.AsNoTracking()
            .Include(session => session.LiveClass)
            .ThenInclude(liveClass => liveClass.TutorProfile)
            .Where(session => session.LiveClass.TutorProfile != null
                && session.LiveClass.TutorProfile!.ExpertUserId == tutorUserId)
            .Select(session => session.Id)
            .ToListAsync(ct);

        if (sessionIds.Count == 0)
        {
            return new ClassFeedbackAggregateDto(0, 0d, 0d, []);
        }

        var entries = await db.ClassFeedbacks.AsNoTracking()
            .Where(f => sessionIds.Contains(f.ClassSessionId))
            .ToListAsync(ct);
        return AggregateInternal(entries, limit);
    }

    private static ClassFeedbackAggregateDto AggregateInternal(List<ClassFeedback> entries, int limit)
    {
        if (entries.Count == 0)
        {
            return new ClassFeedbackAggregateDto(0, 0d, 0d, []);
        }

        var avg = Math.Round(entries.Average(f => (double)f.Rating), 2);
        var recommendCount = entries.Count(f => f.RecommendToFriend);
        var recommendPercent = Math.Round((double)recommendCount / entries.Count * 100d, 1);
        var recent = entries
            .Where(f => !string.IsNullOrWhiteSpace(f.Comment))
            .OrderByDescending(f => f.CreatedAt)
            .Take(limit)
            .Select(MapFeedback)
            .ToList();
        return new ClassFeedbackAggregateDto(entries.Count, avg, recommendPercent, recent);
    }

    private static ClassFeedbackDto MapFeedback(ClassFeedback feedback)
        => new(
            feedback.Id,
            feedback.ClassSessionId,
            feedback.UserId,
            feedback.Rating,
            feedback.Comment,
            feedback.RecommendToFriend,
            feedback.CreatedAt,
            feedback.UpdatedAt);
}
