using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Singleton per-exam-family configuration for the Progress v2 dashboard.
/// Replaces a host of constants that used to live in the old
/// <c>LearnerService.GetProgressAsync</c>. Every knob is admin-tunable via
/// <c>/admin/progress-policy</c>.
///
/// <para>
/// Per <c>AGENTS.md</c>: scoring math NEVER lives here. This only controls
/// display + gating. All pass/fail + raw→scaled conversions route through
/// <see cref="OetLearner.Api.Services.OetScoring"/>.
/// </para>
/// </summary>
[Index(nameof(ExamFamilyCode), IsUnique = true)]
public class ProgressPolicy
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    /// <summary>Default time range the page opens on.</summary>
    [MaxLength(8)]
    public string DefaultTimeRange { get; set; } = "90d";

    /// <summary>Rolling-average window for trend smoothing (number of attempts). 0 disables smoothing.</summary>
    public int SmoothingWindow { get; set; } = 3;

    /// <summary>Hide the comparative cohort panel entirely when the cohort has fewer than this many peers.</summary>
    public int MinCohortSize { get; set; } = 30;

    /// <summary>Show mocks as a dashed series distinct from practice.</summary>
    public bool MockDistinctStyle { get; set; } = true;

    /// <summary>Show the Score Guarantee strip (admin kill-switch).</summary>
    public bool ShowScoreGuaranteeStrip { get; set; } = true;

    /// <summary>Show the 95% confidence-interval band on criterion charts.</summary>
    public bool ShowCriterionConfidenceBand { get; set; } = true;

    /// <summary>Minimum evaluations before the trend chart replaces the empty-state with real lines.</summary>
    public int MinEvaluationsForTrend { get; set; } = 2;

    /// <summary>Legal gate on the PDF export endpoint.</summary>
    public bool ExportPdfEnabled { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
