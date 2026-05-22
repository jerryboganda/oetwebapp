using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Phase 10 — daily rolled-up business metric. Avoids expensive on-demand
/// aggregation. Filled by BillingMetricsRollupWorker overnight.
/// </summary>
[Index(nameof(MetricDate), nameof(MetricCode), nameof(Region), IsUnique = true)]
[Index(nameof(MetricCode), nameof(MetricDate))]
public class BillingMetricDaily
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    public DateOnly MetricDate { get; set; }

    /// <summary>mrr, arr, new_subscriptions, churn_rate, refund_rate, arpu, gross_revenue, net_revenue, tax_collected, credits_sold, ...</summary>
    [MaxLength(64)]
    public string MetricCode { get; set; } = default!;

    [MaxLength(16)]
    public string Region { get; set; } = "GLOBAL";

    [MaxLength(8)]
    public string Currency { get; set; } = "AUD";

    public decimal Value { get; set; }

    /// <summary>Optional supporting JSON (e.g. cohort sizes, breakdown by plan).</summary>
    [MaxLength(2048)]
    public string? DetailsJson { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
