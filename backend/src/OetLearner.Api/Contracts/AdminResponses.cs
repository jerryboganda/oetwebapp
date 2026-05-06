namespace OetLearner.Api.Contracts;

public sealed record AdminAlertItemResponse(
    string AlertType,
    string Severity,
    string Title,
    string Description,
    string ActionRoute,
    DateTimeOffset DetectedAt);

public sealed record AdminAlertSummaryResponse(
    IReadOnlyList<AdminAlertItemResponse> Alerts,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    DateTimeOffset GeneratedAt);

public sealed record AdminBulkContentResponse(
    IReadOnlyList<string> SuccessIds,
    IReadOnlyList<string> FailedIds,
    string Action,
    int TotalProcessed);
