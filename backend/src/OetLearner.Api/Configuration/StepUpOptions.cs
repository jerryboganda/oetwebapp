namespace OetLearner.Api.Configuration;

/// <summary>
/// Configures step-up (re-)authentication for the money-moving admin actions
/// (security standard PAY-16 / IAM-08). Bound from the <c>StepUp</c>
/// configuration section.
/// </summary>
public sealed class StepUpOptions
{
    public const string SectionName = "StepUp";

    /// <summary>Lifetime of an issued step-up token in seconds. The admin must
    /// present a fresh proof-of-recent-TOTP on each high-risk call, so this is
    /// deliberately short.</summary>
    public int TokenLifetimeSeconds { get; set; } = 300;

    /// <summary>Require step-up on manual-payment approvals, proof waivers, and
    /// subscription fulfilment. Default on.</summary>
    public bool RequireForManualPaymentApproval { get; set; } = true;

    /// <summary>Require step-up on refund issuance. Default on.</summary>
    public bool RequireForRefunds { get; set; } = true;
}
