using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

public class PeerReviewService(LearnerDbContext db)
{
    public async Task<PeerReviewRequest> SubmitForReviewAsync(
        string userId, string subtestCode, string contentId, string submissionText, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var request = new PeerReviewRequest
        {
            Id = $"prr-{Guid.NewGuid():N}",
            SubmitterUserId = userId,
            AttemptId = contentId,
            SubtestCode = subtestCode,
            Status = "open",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.PeerReviewRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<List<PeerReviewRequest>> GetAvailableReviewsAsync(string userId, CancellationToken ct)
    {
        return await db.PeerReviewRequests
            .Where(r => r.Status == "open" && r.SubmitterUserId != userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<PeerReviewRequest?> ClaimReviewAsync(string userId, string requestId, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([requestId], ct);
        if (request == null) return null;
        if (request.Status != "open") return null;
        if (request.SubmitterUserId == userId) return null;

        request.ReviewerUserId = userId;
        request.Status = "claimed";
        request.ClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<PeerReviewFeedback?> SubmitFeedbackAsync(
        string userId, string requestId, string feedbackText, int rating, CancellationToken ct)
    {
        var request = await db.PeerReviewRequests.FindAsync([requestId], ct);
        if (request == null) return null;
        if (request.ReviewerUserId != userId) return null;
        if (request.Status != "claimed") return null;
        if (rating < 1 || rating > 5) return null;

        var feedback = new PeerReviewFeedback
        {
            Id = $"prf-{Guid.NewGuid():N}",
            PeerReviewRequestId = requestId,
            ReviewerUserId = userId,
            Comments = feedbackText,
            OverallRating = rating,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.PeerReviewFeedbacks.Add(feedback);
        request.Status = "completed";
        request.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return feedback;
    }

    public async Task<List<PeerReviewRequest>> GetMySubmissionsAsync(string userId, CancellationToken ct)
    {
        return await db.PeerReviewRequests
            .Where(r => r.SubmitterUserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<List<PeerReviewRequest>> GetMyReviewsAsync(string userId, CancellationToken ct)
    {
        return await db.PeerReviewRequests
            .Where(r => r.ReviewerUserId == userId && (r.Status == "claimed" || r.Status == "completed"))
            .OrderByDescending(r => r.ClaimedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<PeerReviewRequest?> GetRequestByIdAsync(string requestId, CancellationToken ct)
    {
        return await db.PeerReviewRequests.FindAsync([requestId], ct);
    }

    public async Task<PeerReviewFeedback?> GetFeedbackForRequestAsync(string requestId, CancellationToken ct)
    {
        return await db.PeerReviewFeedbacks
            .Where(f => f.PeerReviewRequestId == requestId)
            .FirstOrDefaultAsync(ct);
    }
}
