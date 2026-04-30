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

    // Wave 3 of docs/SPEAKING-MODULE-PLAN.md (Q1 of decisions §6 —
    // locked). A speaking mock set bundles two role-plays and is
    // capped on top of the per-task `MaxSpeakingAttempts`.
    // Default = 1 distinct session per rolling 7 days for free tier.
    public int MaxSpeakingMockSets { get; set; } = 1;

    public int TrialDurationDays { get; set; } = 7;

    public bool ShowUpgradePrompts { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
