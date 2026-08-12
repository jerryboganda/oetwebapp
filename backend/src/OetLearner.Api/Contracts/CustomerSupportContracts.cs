namespace OetLearner.Api.Contracts;

public sealed record CustomerSupportCaseCreateRequest(
    string? ExternalTicketId,
    string? CandidateUserId,
    string? Subject,
    DateTimeOffset ExpiresAt);

public sealed record CustomerSupportCaseCloseRequest(string? Reason);

public sealed record CustomerSupportCaseSummary(
    string Id,
    string ExternalTicketId,
    string CandidateUserId,
    string Subject,
    string Status,
    string OpenedByAdminId,
    string OpenedByAdminName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CustomerSupportCandidateProjection(
    string CaseId,
    string ExternalTicketId,
    string CandidateUserId,
    string DisplayName,
    string Email,
    string AccountStatus,
    string? ActiveProfessionId,
    DateTimeOffset ExpiresAt);
