namespace OetLearner.Api.Services;

/// <summary>
/// Mail lanes keep transactional/auth delivery independent of marketing
/// subscription state. A marketing unsubscribe must never block OTP,
/// email verification, or password-reset mail.
/// </summary>
public enum EmailLane
{
    Auth,
    Product,
    Marketing,
    Support
}

public static class EmailLanes
{
    public const string MarketingSuppressionEventKey = "__marketing__";
    public const string NonAuthSuppressionEventKey = "__non_auth__";

    public const string DefaultAuthFromAddress = "auth@oetwithdrhesham.co.uk";
    public const string DefaultAuthFromName = "Auth | OET with Dr Hesham";
    public const string DefaultMarketingFromAddress = "updates@oetwithdrhesham.co.uk";
    public const string DefaultMarketingFromName = "Updates | OET with Dr Hesham";
    public const string DefaultProductFromAddress = "no-reply@oetwithdrhesham.co.uk";
    public const string DefaultProductFromName = "No-reply | OET with Dr Hesham";
    public const string DefaultSupportFromAddress = "support@oetwithdrhesham.co.uk";
    public const string DefaultSupportFromName = "OET with Dr. Ahmed Hesham";

    private static readonly HashSet<string> AuthTemplateKeys = new(StringComparer.Ordinal)
    {
        EmailTemplateKeys.EmailVerificationOtp,
        EmailTemplateKeys.PasswordResetOtp,
        EmailTemplateKeys.PasswordChanged,
        EmailTemplateKeys.MfaEnabled,
        EmailTemplateKeys.AdminInvite,
        EmailTemplateKeys.SecurityAlert
    };

    private static readonly HashSet<string> MarketingEventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LearnerCreditsLow",
        "LearnerCreditsExpiring",
        "LearnerTrialConversionNudge",
        "LearnerAbandonedCheckout",
        "LearnerWinBack",
        "LearnerCourseLaunch",
        "LearnerInactiveNudge",
        "LearnerExamUrgencyReminder",
        "credits_low",
        "credits_expiring"
    };

    public static EmailLane Resolve(EmailMessage message)
        => message.Lane
           ?? Resolve(message.TemplateKey, message.Category, message.EventKey);

    public static EmailLane Resolve(string? templateKey, string? category = null, string? eventKey = null)
    {
        if (!string.IsNullOrWhiteSpace(templateKey) && AuthTemplateKeys.Contains(templateKey))
        {
            return EmailLane.Auth;
        }

        if (IsMarketing(category, eventKey))
        {
            return EmailLane.Marketing;
        }

        if (string.Equals(category, "security", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "operations", StringComparison.OrdinalIgnoreCase))
        {
            return EmailLane.Support;
        }

        return EmailLane.Product;
    }

    public static bool IsMarketing(string? category, string? eventKey = null)
    {
        if (string.Equals(category, "marketing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "engagement", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(eventKey) && MarketingEventKeys.Contains(eventKey.Trim());
    }

    public static bool IsAuthEvent(string? eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
        {
            return false;
        }

        return eventKey.Contains("EmailVerification", StringComparison.OrdinalIgnoreCase)
               || eventKey.Contains("PasswordReset", StringComparison.OrdinalIgnoreCase)
               || eventKey.Contains("SecurityAlert", StringComparison.OrdinalIgnoreCase)
               || eventKey.Contains("MfaEnabled", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAuthReason(string? reasonCode)
        => string.Equals(reasonCode, "brevo_unsubscribed", StringComparison.OrdinalIgnoreCase);

    public static (string Email, string Name) ResolveSender(EmailSettingsSnapshot settings, EmailLane lane)
        => lane switch
        {
            EmailLane.Auth => (
                First(settings.AuthFromAddress, settings.SmtpFromAddress, DefaultAuthFromAddress),
                First(settings.AuthFromName, settings.SmtpFromName, DefaultAuthFromName)),
            EmailLane.Marketing => (
                First(settings.MarketingFromAddress, settings.SmtpFromAddress, DefaultMarketingFromAddress),
                First(settings.MarketingFromName, settings.SmtpFromName, DefaultMarketingFromName)),
            EmailLane.Support => (
                First(settings.SupportFromAddress, settings.SmtpFromAddress, DefaultSupportFromAddress),
                First(settings.SupportFromName, settings.SmtpFromName, DefaultSupportFromName)),
            _ => (
                First(settings.ProductFromAddress, settings.SmtpFromAddress, DefaultProductFromAddress),
                First(settings.ProductFromName, settings.SmtpFromName, DefaultProductFromName))
        };

    public static IReadOnlyList<string> TagsFor(EmailLane lane, EmailMessage message)
    {
        var tags = new List<string> { $"lane:{lane.ToString().ToLowerInvariant()}" };
        if (!string.IsNullOrWhiteSpace(message.TemplateKey))
        {
            tags.Add($"template:{message.TemplateKey}");
        }

        if (!string.IsNullOrWhiteSpace(message.EventKey))
        {
            tags.Add($"event:{message.EventKey}");
        }

        return tags;
    }

    private static string First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}

/// <summary>
/// Narrow sender snapshot so SMTP and Brevo share one resolution path
/// without taking a dependency on the full runtime-settings record.
/// </summary>
public readonly record struct EmailSettingsSnapshot(
    string? SmtpFromAddress,
    string? SmtpFromName,
    string? AuthFromAddress,
    string? AuthFromName,
    string? MarketingFromAddress,
    string? MarketingFromName,
    string? ProductFromAddress,
    string? ProductFromName,
    string? SupportFromAddress,
    string? SupportFromName);

public static class EmailSettingsSnapshotExtensions
{
    public static EmailSettingsSnapshot ToSnapshot(this Settings.EmailSettings settings)
        => new(
            settings.SmtpFromAddress,
            settings.SmtpFromName,
            settings.AuthFromAddress,
            settings.AuthFromName,
            settings.MarketingFromAddress,
            settings.MarketingFromName,
            settings.ProductFromAddress,
            settings.ProductFromName,
            settings.SupportFromAddress,
            settings.SupportFromName);
}
