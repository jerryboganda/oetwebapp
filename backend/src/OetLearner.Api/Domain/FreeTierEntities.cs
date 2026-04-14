using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class FreeTierConfig
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public bool Enabled { get; set; } = true;

    public int MaxWritingAttempts { get; set; } = 3;
    public int MaxSpeakingAttempts { get; set; } = 3;
    public int MaxReadingAttempts { get; set; } = 5;
    public int MaxListeningAttempts { get; set; } = 5;

    public int TrialDurationDays { get; set; } = 7;

    public bool ShowUpgradePrompts { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
