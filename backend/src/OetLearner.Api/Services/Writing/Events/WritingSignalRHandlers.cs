using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Data;
using OetLearner.Api.Hubs;

namespace OetLearner.Api.Services.Writing.Events;

public sealed class WritingGradeReadyHubEventHandler(
    IHubContext<WritingSubmissionHub> hubContext,
    LearnerDbContext db,
    TimeProvider clock) : IWritingEventHandler<WritingGradeReady>
{
    public async Task HandleAsync(WritingGradeReady @event, CancellationToken ct)
    {
        // The grade persists the instant grading finishes, but a normal candidate's result is only released
        // once its 15-minute window has elapsed: a held result must not signal readiness (the client's
        // countdown and poll deliver the release). Allowlisted accounts are released at once.
        if (!await WritingResultRelease.IsReleasedAsync(db, @event.UserId, @event.SubmissionId, clock.GetUtcNow(), ct))
        {
            return;
        }

        var payload = new WritingGradeReadyHubPayload(@event.SubmissionId, @event.GradeId, @event.OccurredAt);

        await hubContext.Clients
            .Group(WritingSubmissionHub.SubmissionGroup(@event.SubmissionId))
            .SendAsync(WritingSubmissionHub.GradeReadyEvent, payload, ct);
    }
}
