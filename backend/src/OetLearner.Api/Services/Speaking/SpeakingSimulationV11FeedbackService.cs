using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed record SpeakingSimulationV11FeedbackRequest(int Rating, string? Comment);

public sealed record SpeakingSimulationV11FeedbackResponse(
    string FeedbackId,
    string SpeakingSessionId,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt);

public sealed class SpeakingSimulationV11FeedbackService(
    LearnerDbContext db,
    TimeProvider timeProvider)
{
    private const int MaxCommentLength = 2000;
    private const string FeedbackScope = "speaking_v11_feedback";

    public async Task<SpeakingSimulationV11FeedbackResponse> SubmitAsync(
        string userId,
        string sessionId,
        SpeakingSimulationV11FeedbackRequest request,
        CancellationToken ct)
    {
        if (request.Rating is < 1 or > 5)
        {
            throw ApiException.Validation(
                "speaking_v11_feedback_rating_invalid",
                "Rating must be between 1 and 5.");
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment?.Length > MaxCommentLength)
        {
            throw ApiException.Validation(
                "speaking_v11_feedback_comment_too_long",
                $"Feedback comments cannot exceed {MaxCommentLength} characters.");
        }

        var session = await db.SpeakingSessions.AsNoTracking()
            .Where(x => x.Id == sessionId)
            .Select(x => new { x.UserId, x.State })
            .SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound(
                "speaking_v11_feedback_session_not_found",
                "Speaking session not found.");

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden(
                "speaking_v11_feedback_forbidden",
                "You can only submit feedback for your own speaking session.");
        }

        if (session.State != SpeakingSessionState.Finished)
        {
            throw ApiException.Conflict(
                "speaking_v11_feedback_session_not_finished",
                "Feedback can be submitted after the speaking session is finished.");
        }

        var isV11 = await db.SpeakingSimulationV11Assessments.AsNoTracking()
            .AnyAsync(x => x.SpeakingSessionId == sessionId, ct);
        if (!isV11)
        {
            throw ApiException.Conflict(
                "speaking_v11_feedback_assessment_required",
                "Feedback is available after a v1.1 speaking assessment exists.");
        }

        var feedbackId = BuildFeedbackId(userId, sessionId);
        var payloadJson = JsonSerializer.Serialize(new
        {
            speakingSessionId = sessionId,
            rating = request.Rating,
            comment,
        });
        var existing = await db.IdempotencyRecords
            .SingleOrDefaultAsync(x => x.Scope == FeedbackScope && x.Key == feedbackId, ct);
        if (existing is not null)
        {
            existing.ResponseJson = payloadJson;
            await db.SaveChangesAsync(ct);
            return Project(existing, sessionId, request.Rating, comment);
        }

        var row = new IdempotencyRecord
        {
            Id = feedbackId,
            Scope = FeedbackScope,
            Key = feedbackId,
            ResponseJson = payloadJson,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.IdempotencyRecords.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return Project(row, sessionId, request.Rating, comment);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(row).State = EntityState.Detached;
            var concurrent = await db.IdempotencyRecords
                .SingleAsync(x => x.Scope == FeedbackScope && x.Key == feedbackId, ct);
            concurrent.ResponseJson = payloadJson;
            await db.SaveChangesAsync(ct);
            return Project(concurrent, sessionId, request.Rating, comment);
        }
    }

    private static string BuildFeedbackId(string userId, string sessionId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{userId}:{sessionId}")))
            .ToLowerInvariant();
        return $"SPV11FB-{hash[..48]}";
    }

    private static SpeakingSimulationV11FeedbackResponse Project(
        IdempotencyRecord row,
        string sessionId,
        int rating,
        string? comment)
        => new(
            row.Id,
            sessionId,
            rating,
            comment,
            row.CreatedAt);
}
