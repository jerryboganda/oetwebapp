using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingTutorReview
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "claimed";

    public string FreeTextFeedback { get; set; } = string.Empty;

    public string PerCriterionCommentsJson { get; set; } = "{}";

    public string? ScoreOverrideJson { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingTutorReviewAssignment
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset DueAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "claimed";

    public DateTimeOffset? ReleasedAt { get; set; }
}

public class WritingTutorCalibration
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string TutorId { get; set; } = default!;

    public decimal AgreementCoefficient { get; set; }

    public int SamplesReviewed { get; set; }

    public DateTimeOffset LastCalibratedAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}
