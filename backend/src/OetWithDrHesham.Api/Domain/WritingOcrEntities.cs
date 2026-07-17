using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

public class WritingOcrJob
{
    public Guid Id { get; set; }

    public Guid? SubmissionId { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    [MaxLength(16)]
    public string Provider { get; set; } = "tesseract";

    public double? ConfidenceScore { get; set; }

    public string? ExtractedText { get; set; }

    public string ImageUrlsJson { get; set; } = "[]";

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
