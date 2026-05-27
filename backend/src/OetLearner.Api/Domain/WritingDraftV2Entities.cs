using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class WritingDraftV2
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid ScenarioId { get; set; }

    [MaxLength(16)]
    public string Mode { get; set; } = "practice";

    public string Content { get; set; } = string.Empty;

    public int WordCount { get; set; }

    public int TimeSpentSeconds { get; set; }

    public DateTimeOffset LastSavedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
