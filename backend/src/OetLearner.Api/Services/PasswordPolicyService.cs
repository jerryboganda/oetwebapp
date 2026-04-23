using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

/// <summary>
/// Server-side password policy enforcement.
///
/// <para>
/// This service is deliberately the <b>only</b> place new user-supplied passwords are
/// accepted. Both <c>RegisterLearnerAsync</c> and <c>ResetPasswordAsync</c> must call
/// <see cref="EnsurePasswordAcceptableAsync"/> before hashing. Do NOT duplicate the
/// rules inline at call sites — it drifts.
/// </para>
///
/// <para>Two layers of defence:</para>
/// <list type="number">
///   <item>
///     <b>Complexity rules</b> (deterministic, no network). Enforced always.
///   </item>
///   <item>
///     <b>HaveIBeenPwned k-anonymity breach check</b> (network, optional). The full
///     password never leaves the server — only the first 5 hex chars of the SHA-1
///     hash are sent, and HIBP returns every hash suffix that starts with that
///     prefix. We count our full-hash occurrences in the returned list. See
///     https://haveibeenpwned.com/API/v3#PwnedPasswords. This check is opt-in via
///     <c>PasswordPolicyOptions.BreachCheckEnabled</c> because in air-gapped or
///     locked-down deployments HIBP is unreachable and we must not lock out users.
///   </item>
/// </list>
/// </summary>
public sealed class PasswordPolicyService(
    IHttpClientFactory httpClientFactory,
    IOptions<PasswordPolicyOptions> options,
    ILogger<PasswordPolicyService> logger)
{
    public const string HibpHttpClientName = "HaveIBeenPwned";

    private readonly PasswordPolicyOptions _options = options.Value;

    /// <summary>
    /// Validate a candidate password. Throws <see cref="ApiException"/> with a
    /// machine-readable error code when rejected so the client can localise the
    /// message. Returns silently when accepted.
    /// </summary>
    /// <param name="password">The plaintext candidate. Required.</param>
    /// <param name="email">
    /// The account email, if known. Used to reject passwords equal to the email
    /// or that contain the local-part. Pass <c>null</c> during flows where the
    /// email isn't yet available (this weakens the check but doesn't block).
    /// </param>
    public async Task EnsurePasswordAcceptableAsync(
        string? password,
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw ApiException.Validation("password_required", "Password is required.");
        }

        ValidateComplexity(password, email);

        if (_options.BreachCheckEnabled)
        {
            var pwned = await IsPasswordBreachedAsync(password, cancellationToken);
            if (pwned)
            {
                throw ApiException.Validation(
                    "password_breached",
                    "This password has appeared in a known public data breach. Please choose a different password.");
            }
        }
    }

    private void ValidateComplexity(string password, string? email)
    {
        if (password.Length < _options.MinimumLength)
        {
            throw ApiException.Validation(
                "password_too_short",
                $"Password must be at least {_options.MinimumLength} characters long.");
        }

        // Keep the constraints meaningful but not so strict that users migrate to
        // pattern-based passwords ("Password1!"). Length matters more than class
        // count, per NIST 800-63B — we already enforce 10+ above.
        if (_options.RequireMixedCase)
        {
            var hasUpper = false;
            var hasLower = false;
            foreach (var ch in password)
            {
                if (char.IsUpper(ch)) hasUpper = true;
                else if (char.IsLower(ch)) hasLower = true;
                if (hasUpper && hasLower) break;
            }
            if (!hasUpper || !hasLower)
            {
                throw ApiException.Validation(
                    "password_missing_case",
                    "Password must include both uppercase and lowercase letters.");
            }
        }

        if (_options.RequireDigit && !password.Any(char.IsDigit))
        {
            throw ApiException.Validation(
                "password_missing_digit",
                "Password must include at least one digit.");
        }

        if (_options.RequireSymbol && password.All(char.IsLetterOrDigit))
        {
            throw ApiException.Validation(
                "password_missing_symbol",
                "Password must include at least one symbol (e.g. !@#$%).");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var normalizedPassword = password.ToLowerInvariant();
            var localPart = normalizedEmail.Split('@', 2)[0];

            if (string.Equals(normalizedPassword, normalizedEmail, StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "password_equals_email",
                    "Password cannot be the same as your email address.");
            }

            if (localPart.Length >= 4 && normalizedPassword.Contains(localPart, StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "password_contains_email",
                    "Password cannot contain the local part of your email address.");
            }
        }
    }

    /// <summary>
    /// Call HIBP Pwned Passwords v3 using k-anonymity: send the first 5 hex
    /// characters of SHA-1(password), receive a list of suffixes, check whether
    /// ours appears. Returns <c>false</c> on any network/parse error — we do
    /// <b>not</b> want an HIBP outage to become a sign-up outage.
    /// </summary>
    private async Task<bool> IsPasswordBreachedAsync(string password, CancellationToken cancellationToken)
    {
        string hashHex;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(password);
            // HIBP requires SHA-1. This is NOT used to store passwords — don't let
            // "SHA-1 is broken" reviews flag this line; the weakness is irrelevant
            // to the range-query pattern.
#pragma warning disable CA5350
            var hash = SHA1.HashData(bytes);
#pragma warning restore CA5350
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("X2"));
            }
            hashHex = sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to hash candidate password for HIBP check; skipping.");
            return false;
        }

        var prefix = hashHex[..5];
        var suffix = hashHex[5..];

        try
        {
            using var client = httpClientFactory.CreateClient(HibpHttpClientName);
            using var response = await client.GetAsync(
                $"range/{prefix}",
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "HIBP range request returned {Status}; fail-open (allowing password).",
                    (int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Response format per line: SUFFIX:COUNT
            foreach (var rawLine in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var candidate = line.AsSpan(0, colon);
                if (candidate.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "HIBP range request failed; fail-open (allowing password).");
            return false;
        }
    }
}
