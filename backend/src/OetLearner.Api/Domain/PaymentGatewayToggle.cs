using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// Admin ON/OFF catalog for candidate-facing payment gateways.
/// Credentials stay in runtime settings; this row only controls visibility,
/// order, and labels. Disabling a gateway must not delete its configuration.
/// </summary>
[Index(nameof(Name), IsUnique = true)]
[Index(nameof(Region), nameof(IsEnabled), nameof(DisplayOrder))]
public class PaymentGatewayToggle
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string Name { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(128)]
    public string CandidateLabel { get; set; } = default!;

    /// <summary>global | egypt | all</summary>
    [MaxLength(16)]
    public string Region { get; set; } = PaymentGatewayRegions.Global;

    /// <summary>embedded | iframe | redirect</summary>
    [MaxLength(16)]
    public string Mode { get; set; } = PaymentGatewayModes.Redirect;

    [MaxLength(64)]
    public string IconName { get; set; } = "credit-card";

    public bool IsEnabled { get; set; }

    public bool IsPrimary { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }
}

public static class PaymentGatewayRegions
{
    public const string Global = "global";
    public const string Egypt = "egypt";
    public const string All = "all";
}

public static class PaymentGatewayModes
{
    public const string Embedded = "embedded";
    public const string Iframe = "iframe";
    public const string Redirect = "redirect";
}

public static class PaymentGatewayNames
{
    public const string Whop = "whop";
    public const string Fawaterak = "fawaterak";
    public const string Paymob = "paymob";
    public const string PayTabs = "paytabs";
    public const string EasyKash = "easykash";
    public const string CheckoutCom = "checkoutcom";
    public const string Stripe = "stripe";
    public const string PayPal = "paypal";
}
