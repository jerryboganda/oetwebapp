using Microsoft.AspNetCore.Mvc;

namespace OetLearner.Api.Contracts;

public sealed class ExpertQueueQueryRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    [FromQuery(Name = "type")]
    public string? Type { get; init; }

    [FromQuery(Name = "profession")]
    public string? Profession { get; init; }

    [FromQuery(Name = "priority")]
    public string? Priority { get; init; }

    [FromQuery(Name = "status")]
    public string? Status { get; init; }

    [FromQuery(Name = "confidence")]
    public string? Confidence { get; init; }

    [FromQuery(Name = "assignment")]
    public string? Assignment { get; init; }

    [FromQuery(Name = "overdue")]
    public bool? Overdue { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

public sealed class ExpertLearnersQueryRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; init; }

    [FromQuery(Name = "profession")]
    public string? Profession { get; init; }

    [FromQuery(Name = "subTest")]
    public string? SubTest { get; init; }

    [FromQuery(Name = "relevance")]
    public string? Relevance { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}

public record ExpertDraftSaveRequest(
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment,
    List<ExpertAnchoredCommentDto>? AnchoredComments,
    List<ExpertTimestampCommentDto>? TimestampComments,
    string? Scratchpad,
    List<ExpertChecklistItemDto>? ChecklistItems,
    int? Version);

public record ExpertReviewSubmitRequest(
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment,
    int? Version);

public record ExpertReworkRequest(string Reason);

public record ExpertCalibrationSubmitRequest(
    Dictionary<string, int> Scores,
    string? Notes);

public record ExpertAvailabilityUpdateRequest(
    string Timezone,
    Dictionary<string, ExpertScheduleDayDto> Days);

public record ExpertScheduleDayDto(
    bool Active,
    string Start,
    string End);

public record CreateScheduleExceptionRequest(
    string Date,
    bool IsBlocked,
    string? StartTime,
    string? EndTime,
    string? Reason);

public record ExpertAnchoredCommentDto(
    string? Id,
    string? Criterion,
    string Text,
    int StartOffset,
    int EndOffset,
    string? CreatedAt);

public record ExpertTimestampCommentDto(
    string? Id,
    string? Criterion,
    string Text,
    double TimestampStart,
    double? TimestampEnd,
    string? CreatedAt);

public record ExpertChecklistItemDto(
    string Id,
    string Label,
    bool Checked);

public record ExpertReviewAmendRequest(
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment);

public record ExpertBulkClaimRequest(
    List<string> ReviewRequestIds);

public record ExpertBulkReleaseRequest(
    List<string> ReviewRequestIds);

public record CreateMessageThreadRequest(
    string Title,
    string Body,
    string? LinkedReviewRequestId,
    string? LinkedCalibrationCaseId,
    string? LinkedLearnerId);

public record CreateMessageReplyRequest(string Body);
