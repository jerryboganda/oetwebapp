using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingMock
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    public int Difficulty { get; set; } = 4;

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingMockSession
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid MockId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? ReadingPhaseEndedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? SubmissionId { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "started";

    public DateTimeOffset CreatedAt { get; set; }
}
