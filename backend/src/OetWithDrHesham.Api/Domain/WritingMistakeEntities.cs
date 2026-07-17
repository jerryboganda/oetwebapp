using System.ComponentModel.DataAnnotations;

namespace OetWithDrHesham.Api.Domain;

public class WritingCommonMistake
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = default!;

    [MaxLength(500)]
    public string Summary { get; set; } = default!;

    [MaxLength(1000)]
    public string ExampleWrong { get; set; } = default!;

    [MaxLength(1000)]
    public string ExampleRight { get; set; } = default!;

    [MaxLength(16)]
    public string? CanonRuleId { get; set; }

    [MaxLength(4)]
    public string? RelatedSubSkill { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingLearnerMistakeStat
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public Guid MistakeId { get; set; }

    public int OccurrenceCount { get; set; }

    public DateTimeOffset LastOccurredAt { get; set; }

    public DateTimeOffset FirstOccurredAt { get; set; }
}
