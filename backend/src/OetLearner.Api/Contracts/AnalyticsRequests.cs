namespace OetLearner.Api.Contracts;

public record AnalyticsTrackRequest(
    string EventName,
    Dictionary<string, object?>? Properties);