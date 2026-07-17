namespace OetWithDrHesham.Api.Configuration;

/// <summary>
/// Tunables for the Phase-4 expert auto-assignment loop. Defaults match the
/// product decisions in the plan: 30s assignment cadence, 60s SLA cadence,
/// 48h standard turnaround / 12h express, max 8 active assignments per
/// expert, 24h lookback for the load tally.
/// </summary>
public sealed class ExpertAutoAssignmentOptions
{
    public const string SectionName = "Expert:AutoAssignment";

    /// <summary>Set to false to disable the auto-assigner entirely. Useful
    /// for the Testing host environment so unit tests stay deterministic.</summary>
    public bool Enabled { get; set; } = true;

    public int PollingIntervalSeconds { get; set; } = 30;

    public int SlaEscalationIntervalSeconds { get; set; } = 60;

    public int SlaHoursStandard { get; set; } = 48;

    public int SlaHoursExpress { get; set; } = 12;

    public int MaxActiveAssignmentsPerExpert { get; set; } = 8;

    public int LookbackHoursForLoad { get; set; } = 24;

    /// <summary>Hard cap on the number of pending requests processed per poll
    /// cycle. Prevents a runaway tick from monopolising the background loop.</summary>
    public int BatchSize { get; set; } = 50;
}
