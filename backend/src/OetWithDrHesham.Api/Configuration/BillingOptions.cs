namespace OetWithDrHesham.Api.Configuration;

public sealed class BillingOptions
{
    public string? CheckoutBaseUrl { get; set; }
    public bool AllowSandboxFallbacks { get; set; } = false;

    /// <summary>
    /// Maximum age (in seconds) tolerated for the timestamp embedded in a webhook
    /// signature header before the request is rejected as a replay. Default 300.
    /// </summary>
    public int WebhookMaxAgeSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum number of local processing attempts before a verified webhook is
    /// promoted to the dead-letter status surface for admin attention.
    /// </summary>
    public int WebhookMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Currency used when neither the target row nor a region-pricing override
    /// supplies one. Defaults to GBP (the platform's billing currency). Override
    /// via <c>Billing__DefaultCurrency</c>.
    /// </summary>
    public string DefaultCurrency { get; set; } = "GBP";

    /// <summary>
    /// Region used when the buyer's country cannot be detected. Defaults to ROW.
    /// </summary>
    public string DefaultRegion { get; set; } = "ROW";

    public StripeBillingOptions Stripe { get; set; } = new();
    public PayPalBillingOptions PayPal { get; set; } = new();
    public WalletBillingOptions Wallet { get; set; } = new();

    /// <summary>PayTabs (Gulf/Egypt) configuration. Inactive when ServerKey/ProfileId unset.</summary>
    public PayTabsOptions PayTabs { get; set; } = new();

    /// <summary>Paymob (Egypt) configuration. Inactive when ApiKey unset.</summary>
    public PaymobOptions Paymob { get; set; } = new();

    /// <summary>Checkout.com (MENA premium cards) configuration. Inactive when SecretKey unset.</summary>
    public CheckoutComOptions CheckoutCom { get; set; } = new();

    /// <summary>EasyKash (Egypt hosted Direct-Pay) configuration. Inactive when ApiKey/HmacSecret unset.</summary>
    public EasyKashOptions EasyKash { get; set; } = new();
}

public sealed class WalletBillingOptions
{
    /// <summary>
    /// Currency used for wallet top-ups. Defaults to AUD to match learner default plans.
    /// </summary>
    public string Currency { get; set; } = "AUD";

    /// <summary>
    /// Configurable wallet top-up tiers. Override via `Billing__Wallet__TopUpTiers__N__Amount` env vars
    /// or appsettings. Defaults provide the historic 4-tier set so behaviour is unchanged when unset.
    /// </summary>
    /// <summary>
    /// Unified credit-economy tiers priced at a sustainable mid-range ($2–$5/credit).
    /// Volume bonuses reward larger top-ups while keeping every tier within a
    /// narrow, learner-trustworthy price band.  Eliminates the old 16× gap between
    /// wallet ($0.63/credit) and add-on ($9–$10/credit) pricing.
    /// </summary>
    public List<WalletTopUpTierOption> TopUpTiers { get; set; } = new()
    {
        new WalletTopUpTierOption { Amount = 10, Credits = 3, Bonus = 0, Label = "Starter", IsPopular = false },
        new WalletTopUpTierOption { Amount = 25, Credits = 8, Bonus = 1, Label = "Standard", IsPopular = false },
        new WalletTopUpTierOption { Amount = 50, Credits = 16, Bonus = 4, Label = "Best value", IsPopular = true },
        new WalletTopUpTierOption { Amount = 100, Credits = 35, Bonus = 10, Label = "Power", IsPopular = false },
    };
}

public sealed class WalletTopUpTierOption
{
    public int Amount { get; set; }
    public int Credits { get; set; }
    public int Bonus { get; set; }
    public string? Label { get; set; }
    public bool IsPopular { get; set; }
}

public sealed class StripeBillingOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.stripe.com";
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

public sealed class PayPalBillingOptions
{
    public bool UseSandbox { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "https://api-m.paypal.com";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? WebhookId { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Env/default fallback for whether Expanded checkout may render embedded Advanced Card
    /// Fields. The admin runtime setting (<c>PayPalAdvancedCardsEnabled</c>) overrides this.
    /// </summary>
    public bool AdvancedCardsEnabled { get; set; } = true;
}
