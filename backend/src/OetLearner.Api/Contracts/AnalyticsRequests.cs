namespace OetLearner.Api.Contracts;

public record AnalyticsTrackRequest(
    string EventName,
    Dictionary<string, object?>? Properties);

/// <summary>Several events in one request (<c>POST /v1/analytics/events/batch</c>).</summary>
public record AnalyticsTrackBatchRequest(List<AnalyticsTrackRequest?>? Events);