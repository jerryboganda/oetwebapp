namespace OetLearner.Api.Configuration;

/// <summary>
/// Binds to the <c>PasswordPolicy</c> section in configuration. Every field has
/// a sensible production default so the service works out-of-the-box; override
/// via appsettings or environment (<c>PasswordPolicy__BreachCheckEnabled=false</c>)
/// for air-gapped deployments or CI.
/// </summary>
public sealed class PasswordPolicyOptions
{
    /// <summary>Minimum allowed password length. NIST 800-63B recommends 8+; we default to 10.</summary>
    public int MinimumLength { get; set; } = 10;

    /// <summary>Require both uppercase and lowercase letters.</summary>
    public bool RequireMixedCase { get; set; } = true;

    /// <summary>Require at least one digit.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Require at least one non-alphanumeric character.</summary>
    public bool RequireSymbol { get; set; } = true;

    /// <summary>
    /// Enable HaveIBeenPwned range-API check via k-anonymity. The full password
    /// never leaves the server. Disable for air-gapped or rate-limited environments.
    /// </summary>
    public bool BreachCheckEnabled { get; set; } = true;

    /// <summary>
    /// HIBP Pwned Passwords range API base. Override only for self-hosted mirror.
    /// Must end with a trailing slash.
    /// </summary>
    public string BreachApiBaseUrl { get; set; } = "https://api.pwnedpasswords.com/";

    /// <summary>Request timeout for the HIBP breach check.</summary>
    public TimeSpan BreachApiTimeout { get; set; } = TimeSpan.FromSeconds(3);
}
