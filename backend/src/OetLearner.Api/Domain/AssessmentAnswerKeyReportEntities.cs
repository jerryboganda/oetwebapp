using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

public static class AnswerKeyReportAssessments
{
    public const string Reading = "reading";
    public const string Listening = "listening";
}

public static class AnswerKeyReportReasonCodes
{
    public const string WrongOfficialAnswer = "wrong_official_answer";
    public const string MissingAcceptedVariant = "missing_accepted_variant";
    public const string Other = "other";
}

public static class AnswerKeyReportStatuses
{
    public const string Open = "open";
    public const string Investigating = "investigating";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";
}

/// <summary>
/// A candidate report that an official Reading or Listening answer key looks
/// wrong. Separate from personal Flag bookmarks and from Escalations.
/// </summary>
[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(Assessment), nameof(Status))]
[Index(nameof(ReporterUserId), nameof(Assessment), nameof(QuestionId), nameof(AttemptId))]
public sealed class AssessmentAnswerKeyReport
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string Assessment { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string PaperId { get; set; } = default!;

    [MaxLength(64)]
    public string QuestionId { get; set; } = default!;

    public int QuestionNumber { get; set; }

    [MaxLength(16)]
    public string PartCode { get; set; } = string.Empty;

    [MaxLength(512)]
    public string QuestionStemSnapshot { get; set; } = string.Empty;

    [MaxLength(200)]
    public string PaperTitleSnapshot { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ReporterUserId { get; set; } = default!;

    [MaxLength(512)]
    public string LearnerAnswerSnapshot { get; set; } = string.Empty;

    [MaxLength(512)]
    public string OfficialAnswerSnapshot { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ReasonCode { get; set; } = default!;

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = AnswerKeyReportStatuses.Open;

    [MaxLength(2000)]
    public string? ResolutionNote { get; set; }

    [MaxLength(64)]
    public string? ResolvedByAdminId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
