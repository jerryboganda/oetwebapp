namespace OetLearner.Api.Contracts;

public record ExpertDraftSaveRequest(
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment,
    List<ExpertAnchoredCommentDto>? AnchoredComments,
    List<ExpertTimestampCommentDto>? TimestampComments,
    int? Version);

public record ExpertReviewSubmitRequest(
    Dictionary<string, int> Scores,
    Dictionary<string, string> CriterionComments,
    string FinalComment);

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
