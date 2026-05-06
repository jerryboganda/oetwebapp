using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ExpertMessagingService(LearnerDbContext db, NotificationService notifications, ILogger<ExpertMessagingService> logger)
{
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 4000;

    public async Task<IReadOnlyList<ExpertMessageThreadResponse>> GetThreadsAsync(string expertId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertId, ct);

        var threads = await db.Set<ExpertMessageThread>()
            .AsNoTracking()
            .Where(t => t.ExpertId == expertId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

        var threadIds = threads.Select(t => t.Id).ToList();
        var replyCounts = threadIds.Count == 0
            ? new Dictionary<string, int>()
            : (await db.Set<ExpertMessageReply>()
                .AsNoTracking()
                .Where(r => threadIds.Contains(r.ThreadId))
                .GroupBy(r => r.ThreadId)
                .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
                .ToDictionary(x => x.ThreadId, x => x.Count);

        return threads.Select(t => new ExpertMessageThreadResponse(
            t.Id,
            t.Title,
            t.Status,
            t.LinkedReviewRequestId,
            t.LinkedCalibrationCaseId,
            t.LinkedLearnerId,
            replyCounts.GetValueOrDefault(t.Id, 0),
            t.CreatedAt,
            t.UpdatedAt)).ToList();
    }

    public async Task<ExpertMessageThreadDetailResponse> GetThreadDetailAsync(string threadId, string expertId, CancellationToken ct)
    {
        await EnsureExpertAsync(expertId, ct);

        var thread = await db.Set<ExpertMessageThread>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == threadId && t.ExpertId == expertId, ct)
            ?? throw ApiException.NotFound("thread_not_found", "Message thread not found.");

        var replies = await db.Set<ExpertMessageReply>()
            .AsNoTracking()
            .Where(r => r.ThreadId == threadId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ExpertMessageReplyResponse(
                r.Id,
                r.AuthorId,
                r.AuthorRole,
                r.AuthorName,
                r.Body,
                r.CreatedAt))
            .ToListAsync(ct);

        return new ExpertMessageThreadDetailResponse(
            thread.Id,
            thread.Title,
            thread.Status,
            thread.LinkedReviewRequestId,
            thread.LinkedCalibrationCaseId,
            thread.LinkedLearnerId,
            replies,
            thread.CreatedAt,
            thread.UpdatedAt);
    }

    public async Task<ExpertMessageThreadDetailResponse> CreateThreadAsync(string expertId, CreateMessageThreadRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(expertId, ct);

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaxTitleLength)
            throw ApiException.Validation("invalid_title", $"Title is required and must be ≤ {MaxTitleLength} characters.");

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxBodyLength)
            throw ApiException.Validation("invalid_body", $"Body is required and must be ≤ {MaxBodyLength} characters.");

        var now = DateTimeOffset.UtcNow;
        var thread = new ExpertMessageThread
        {
            Id = Guid.NewGuid().ToString("N")[..32],
            ExpertId = expertId,
            Title = request.Title.Trim(),
            Status = "open",
            LinkedReviewRequestId = request.LinkedReviewRequestId,
            LinkedCalibrationCaseId = request.LinkedCalibrationCaseId,
            LinkedLearnerId = request.LinkedLearnerId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Set<ExpertMessageThread>().Add(thread);

        var reply = new ExpertMessageReply
        {
            Id = Guid.NewGuid().ToString("N")[..32],
            ThreadId = thread.Id,
            AuthorId = expertId,
            AuthorRole = "expert",
            AuthorName = expert.DisplayName,
            Body = request.Body.Trim(),
            CreatedAt = now
        };
        db.Set<ExpertMessageReply>().Add(reply);

        await db.SaveChangesAsync(ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "expert_message_thread",
            thread.Id,
            now.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["threadId"] = thread.Id,
                ["message"] = $"Expert {expert.DisplayName} started a new message thread: {thread.Title}"
            },
            ct);

        logger.LogInformation("Expert {ExpertId} created message thread {ThreadId}", expertId, thread.Id);

        return await GetThreadDetailAsync(thread.Id, expertId, ct);
    }

    public async Task<ExpertMessageReplyResponse> PostReplyAsync(string threadId, string expertId, CreateMessageReplyRequest request, CancellationToken ct)
    {
        var expert = await EnsureExpertAsync(expertId, ct);

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > MaxBodyLength)
            throw ApiException.Validation("invalid_body", $"Body is required and must be ≤ {MaxBodyLength} characters.");

        var thread = await db.Set<ExpertMessageThread>()
            .FirstOrDefaultAsync(t => t.Id == threadId && t.ExpertId == expertId, ct)
            ?? throw ApiException.NotFound("thread_not_found", "Message thread not found.");

        if (thread.Status == "closed")
            throw ApiException.Validation("thread_closed", "Cannot reply to a closed thread.");

        var now = DateTimeOffset.UtcNow;
        var reply = new ExpertMessageReply
        {
            Id = Guid.NewGuid().ToString("N")[..32],
            ThreadId = threadId,
            AuthorId = expertId,
            AuthorRole = "expert",
            AuthorName = expert.DisplayName,
            Body = request.Body.Trim(),
            CreatedAt = now
        };
        db.Set<ExpertMessageReply>().Add(reply);

        thread.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await notifications.CreateForAdminsAsync(
            NotificationEventKey.AdminReviewOpsAction,
            "expert_message_reply",
            threadId,
            now.UtcDateTime.Ticks.ToString(),
            new Dictionary<string, object?>
            {
                ["threadId"] = threadId,
                ["message"] = $"Expert {expert.DisplayName} replied to thread: {thread.Title}"
            },
            ct);

        return new ExpertMessageReplyResponse(reply.Id, reply.AuthorId, reply.AuthorRole, reply.AuthorName, reply.Body, reply.CreatedAt);
    }

    private async Task<ExpertUser> EnsureExpertAsync(string expertId, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == expertId && e.IsActive, ct)
            ?? throw ApiException.Forbidden("expert_not_active", "Expert profile not found or inactive.");
        return expert;
    }
}
