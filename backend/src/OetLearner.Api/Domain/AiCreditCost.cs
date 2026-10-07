using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// AI Credit cost of one metered action (SAMI §9.1: config, never hard-coded).
/// The companion reads charges from here before starting a Power Action, and
/// shows the price before asking for confirmation. Row per action code;
/// admins edit via /v1/admin/ai/credit-costs without a deploy.
/// </summary>
public class AiCreditCost
{
    [Key]
    [MaxLength(48)]
    public string ActionCode { get; set; } = default!;

    /// <summary>AI Credits charged for one execution of the action. 0 = free (never charge).</summary>
    public int Credits { get; set; }

    /// <summary>False = the action is not yet metered (config pending, e.g. live voice / deep PDF
    /// whose conversion the spec leaves configurable until validated). Refuses to charge.</summary>
    public bool Enabled { get; set; }

    /// <summary>Learner-facing one-liner shown with the charge ("What the action does").</summary>
    [MaxLength(256)]
    public string Description { get; set; } = default!;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}
