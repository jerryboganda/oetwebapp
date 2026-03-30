using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class AnalyticsIngestionService(LearnerDbContext db, TimeProvider timeProvider)
{
    private const int MaxEventNameLength = 64;

    public async Task RecordAsync(string userId, AnalyticsTrackRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EventName))
        {
            throw ApiException.Validation(
                "invalid_analytics_event",
                "An analytics event name is required.",
                [new ApiFieldError("eventName", "required", "Provide a tracked event name.")]);
        }

        var normalizedEventName = request.EventName.Trim();
        if (normalizedEventName.Length > MaxEventNameLength)
        {
            throw ApiException.Validation(
                "invalid_analytics_event",
                $"Analytics event names must be {MaxEventNameLength} characters or fewer.",
                [new ApiFieldError("eventName", "max_length", $"Must be {MaxEventNameLength} characters or fewer.")]);
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"AN-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = normalizedEventName,
            PayloadJson = JsonSupport.Serialize(request.Properties ?? new Dictionary<string, object?>()),
            OccurredAt = timeProvider.GetUtcNow()
        });

        await db.SaveChangesAsync(ct);
    }
}