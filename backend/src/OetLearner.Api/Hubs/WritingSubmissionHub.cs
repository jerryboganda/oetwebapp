using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Data;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub that pushes <c>WritingGradeReady</c> events to the learner
/// who submitted the letter. Clients subscribe to a per-submission group via
/// <see cref="SubscribeToSubmission"/>; the evaluation pipeline broadcasts to
/// that group when grading lands.
///
/// Wire-up: mapped at <c>/hubs/writing-submissions</c> in <c>Program.cs</c>.
///
/// Client → Server methods:
///   SubscribeToSubmission(submissionId) — join the per-submission group.
///   UnsubscribeFromSubmission(submissionId) — leave the group.
///
/// Server → Client events:
///   GradeReady(submissionId, gradeId) — grade row persisted; client should
///     refetch /v1/writing/submissions/{id}/grade.
///   GradeFailed(submissionId, errorCode, message) — terminal failure.
///   GradeProgress(submissionId, stage) — pipeline stage transition.
/// </summary>
[Authorize(Policy = "LearnerOnly")]
public sealed class WritingSubmissionHub : Hub
{
    public const string GradeReadyEvent = "GradeReady";
    public const string GradeFailedEvent = "GradeFailed";
    public const string GradeProgressEvent = "GradeProgress";

    public static string SubmissionGroup(Guid submissionId) => $"writing-submission:{submissionId}";
    public static string UserGroup(string userId) => $"writing-user:{userId}";

    private readonly LearnerDbContext _db;
    private readonly ILogger<WritingSubmissionHub> _logger;

    public WritingSubmissionHub(LearnerDbContext db, ILogger<WritingSubmissionHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task SubscribeToSubmission(Guid submissionId)
    {
        if (submissionId == Guid.Empty)
        {
            throw new HubException("submission_id_required");
        }
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("user_id_required");
        }
        var ownsSubmission = await _db.WritingSubmissions.AsNoTracking()
            .AnyAsync(s => s.Id == submissionId && s.UserId == userId, Context.ConnectionAborted);
        if (!ownsSubmission)
        {
            _logger.LogWarning("User {UserId} attempted to subscribe to Writing submission {SubmissionId} they do not own.", userId, submissionId);
            throw new HubException("submission_not_found");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            SubmissionGroup(submissionId),
            Context.ConnectionAborted);
    }

    public async Task UnsubscribeFromSubmission(Guid submissionId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            SubmissionGroup(submissionId),
            Context.ConnectionAborted);
    }
}

/// <summary>
/// Strongly typed payload pushed via <see cref="WritingSubmissionHub.GradeReadyEvent"/>.
/// </summary>
public sealed record WritingGradeReadyHubPayload(
    Guid SubmissionId,
    Guid GradeId,
    int EstimatedBand,
    string BandLabel,
    DateTimeOffset GradedAt);
