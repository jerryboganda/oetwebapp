using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Hubs;

namespace OetLearner.Api.Services.Writing.Events;

public sealed class WritingGradeReadyHubEventHandler(
    IHubContext<WritingSubmissionHub> hubContext) : IWritingEventHandler<WritingGradeReady>
{
    public async Task HandleAsync(WritingGradeReady @event, CancellationToken ct)
    {
        var payload = new WritingGradeReadyHubPayload(
            @event.SubmissionId,
            @event.GradeId,
            @event.EstimatedBand,
            @event.BandLabel,
            @event.OccurredAt);

        await hubContext.Clients
            .Group(WritingSubmissionHub.SubmissionGroup(@event.SubmissionId))
            .SendAsync(WritingSubmissionHub.GradeReadyEvent, payload, ct);
    }
}
