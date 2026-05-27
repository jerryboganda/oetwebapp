using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingShowcasePost
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    [MaxLength(64)]
    public string AuthorUserId { get; set; } = default!;

    public string AnonymizedLetterContent { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    [MaxLength(8)]
    public string LetterType { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    [MaxLength(64)]
    public string? ApprovedById { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
