using OetWithDrHesham.Api.Contracts.Classes;

namespace OetWithDrHesham.Api.Services.Classes;

public interface IClassFeedbackService
{
    Task<ClassFeedbackDto> SubmitAsync(string sessionId, string userId, ClassFeedbackSubmitRequest request, string? idempotencyKey, CancellationToken ct);

    Task<ClassFeedbackAggregateDto> GetForSessionAsync(string sessionId, int recentLimit, CancellationToken ct);

    Task<ClassFeedbackAggregateDto> GetForTutorAsync(string tutorUserId, int recentLimit, CancellationToken ct);
}
