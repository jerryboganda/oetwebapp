using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Mocks V2 Wave 5 — A remediation task seeded from a MockReport's weakness signals.
/// One MockReport produces up to 7 tasks (one per study-day in the next week).
/// AI personalisation goes through <c>IAiGatewayService</c> with feature code
/// <c>AiFeatureCodes.MockRemediationDraft</c>; if that pathway is unavailable
/// the deterministic rule-based plan from <c>RemediationPlanService</c> is used.
/// </summary>
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(MockReportId))]
public class RemediationTask
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string MockReportId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string WeaknessTag { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(1000)]
    public string Description { get; set; } = default!;

    [MaxLength(256)]
    public string? RouteHref { get; set; }

    /// <summary>1..7 — which day of the 7-day plan this task lives on.</summary>
    public int DayIndex { get; set; }

    /// <summary>"pending" | "completed" | "skipped".</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "pending";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class RemediationTaskStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Skipped = "skipped";
}
