using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Otp;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed partial class AuthService
{
    /// <summary>Security spec §3.2: re-send the device-approval email code
    /// for a pending device challenge (mirrors the MFA challenge transport —
    /// see DeviceVerificationRequiredException / ReadDeviceChallengeTokenOrThrow).</summary>
    public async Task<OtpChallengeResponse> SendDeviceVerificationOtpAsync(
        string? challengeToken, CancellationToken cancellationToken = default, string? recaptchaToken = null)
    {
        var challenge = ReadDeviceChallengeTokenOrThrow(challengeToken);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.Id == challenge.AccountId, cancellationToken)
            ?? throw ApiException.Forbidden("account_not_found", "This account is not available.");

        await EnsureDeviceVerificationIsRequiredAsync(account, cancellationToken);

        // Replacement mode requires an explicit selection-bound token. The free-slot flow preserves existing behavior.
        if (string.Equals(challenge.Mode, "replacement_required", StringComparison.Ordinal) && challenge.SelectedTrustedDeviceId is null)
        {
            throw ApiException.Validation("replacement_selection_required", "Select which device to replace before sending a verification code.");
        }

        if (string.Equals(challenge.Mode, "replacement_required", StringComparison.Ordinal) && challenge.SelectedTrustedDeviceId.HasValue)
        {
            var ownsSelected = await db.TrustedDevices.AsNoTracking().AnyAsync(
                d => d.Id == challenge.SelectedTrustedDeviceId.Value
                    && d.ApplicationUserAccountId == account.Id
                    && d.RevokedAt == null,
                cancellationToken);
            if (!ownsSelected)
            {
                throw ApiException.Validation("invalid_replacement_device", "The selected device to replace is not valid or is no longer active.");
            }
        }

        var response = await emailOtpService.RequestDeviceTrustOtpAsync(account, cancellationToken, recaptchaToken);
        await ApplyOtpRateLimitItemsAsync(account.Email, response.DeliveryChannel, cancellationToken, account.Id);
        return response;
    }

    /// <summary>Binds an explicit replacement target to a pending device challenge. The caller has already
    /// authenticated with a correct password and received a <c>replacement_required</c> challenge; this step
    /// records which of the two approved slots the learner chose to free. The returned token is
    /// selection-bound and must be used for the subsequent OTP send/verify calls.</summary>
    public async Task<string> SelectReplacementDeviceAsync(string? challengeToken, Guid selectedTrustedDeviceId, CancellationToken cancellationToken = default)
    {
        var challenge = ReadDeviceChallengeTokenOrThrow(challengeToken);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.Id == challenge.AccountId, cancellationToken)
            ?? throw ApiException.Forbidden("account_not_found", "This account is not available.");

        if (!string.Equals(challenge.Mode, "replacement_required", StringComparison.Ordinal))
        {
            throw ApiException.Validation("replacement_selection_not_required", "This device challenge does not require a replacement selection.");
        }

        var selected = await db.TrustedDevices.AsNoTracking().FirstOrDefaultAsync(
            d => d.Id == selectedTrustedDeviceId && d.ApplicationUserAccountId == account.Id && d.RevokedAt == null,
            cancellationToken);
        if (selected is null)
        {
            throw ApiException.Validation("invalid_replacement_device", "The selected device to replace is not valid or is no longer active.");
        }

        // Issue a new protected ticket that binds the selection, preserving the original candidate device and expiry window.
        var boundToken = CreateDeviceChallengeToken(challenge.AccountId, challenge.DeviceId, "replacement_required", selected.Id);
        await securityEventLogger.TryLogAsync(
            account.Id,
            SecurityEventKinds.DeviceTrustRequested,
            deviceId: challenge.DeviceId,
            details: new { action = "replacement_selected", selectedDeviceId = selected.Id, selectedMaskedId = MaskDeviceId(selected.DeviceId) },
            cancellationToken: cancellationToken);
        return boundToken;
    }

    private static string MaskDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown device";
        var normalized = new string(value.Trim().Select(character => char.IsControl(character) ? '?' : character).ToArray());
        return normalized.Length <= 8 ? normalized : $"{normalized[..4]}…{normalized[^4..]}";
    }

    private async Task ApplyOtpRateLimitItemsAsync(
        string? email,
        string? deliveryChannel,
        CancellationToken cancellationToken,
        string? accountId = null)
    {
        var http = httpContextAccessor.HttpContext;
        if (http is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            http.Items["otp_email"] = email.Trim().ToLowerInvariant();
        }

        if (!string.Equals(deliveryChannel, "sms", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            phone = await db.LearnerRegistrationProfiles
                .AsNoTracking()
                .Where(x => x.ApplicationUserAccountId == accountId)
                .Select(x => x.MobileNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
            phone = await (
                from profile in db.LearnerRegistrationProfiles.AsNoTracking()
                join acc in db.ApplicationUserAccounts.AsNoTracking()
                    on profile.ApplicationUserAccountId equals acc.Id
                where acc.NormalizedEmail == normalizedEmail
                select profile.MobileNumber
            ).FirstOrDefaultAsync(cancellationToken);
        }

        var normalizedPhone = PhoneNumberNormalizer.TryNormalize(phone);
        if (normalizedPhone is not null)
        {
            http.Items["otp_phone"] = normalizedPhone;
        }
    }

    /// <summary>Verifies the device-approval code, trusts the device
    /// (auto-revoking only the selected device when in replacement mode), and completes the
    /// sign-in that was paused for this challenge.</summary>
    public async Task<AuthSessionResponse> CompleteDeviceVerificationAsync(
        string? challengeToken, string? code, CancellationToken cancellationToken = default)
    {
        var challenge = ReadDeviceChallengeTokenOrThrow(challengeToken);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.Id == challenge.AccountId, cancellationToken)
            ?? throw ApiException.Forbidden("account_not_found", "This account is not available.");

        await EnsureDeviceVerificationIsRequiredAsync(account, cancellationToken);

        if (string.Equals(challenge.Mode, "replacement_required", StringComparison.Ordinal) && challenge.SelectedTrustedDeviceId is null)
        {
            throw ApiException.Validation("replacement_selection_required", "Select which device to replace before verifying the code.");
        }

        if (string.Equals(challenge.Mode, "replacement_required", StringComparison.Ordinal) && challenge.SelectedTrustedDeviceId.HasValue)
        {
            var ownsSelected = await db.TrustedDevices.AsNoTracking().AnyAsync(
                d => d.Id == challenge.SelectedTrustedDeviceId.Value
                    && d.ApplicationUserAccountId == account.Id
                    && d.RevokedAt == null,
                cancellationToken);
            if (!ownsSelected)
            {
                throw ApiException.Validation("invalid_replacement_device", "The selected device to replace is not valid or is no longer active.");
            }
        }

        await emailOtpService.VerifyDeviceTrustOtpAsync(account, code ?? string.Empty, cancellationToken);

        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);

        string? deviceInfo = null;
        string? platform = null;
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            deviceInfo = httpContext.Request.Headers.UserAgent.ToString();
            if (deviceInfo.Length > 512) deviceInfo = deviceInfo[..512];
            platform = httpContext.Request.Headers["X-OET-Client-Platform"].ToString();
            if (string.IsNullOrWhiteSpace(platform)) platform = null;
        }

        // Trust BEFORE creating the new session, so CreateSessionCoreAsync's own device check below
        // sees this device as already Trusted rather than looping back into OtpRequired.
        // Replacement mode revokes only the selected identity; free-slot mode relies on the existing capacity logic.
        if (challenge.SelectedTrustedDeviceId.HasValue)
        {
            await trustedDeviceService.TrustDeviceWithReplacementAsync(
                account.Id, challenge.DeviceId, challenge.SelectedTrustedDeviceId.Value, deviceInfo, platform, "otp_verified", cancellationToken);
        }
        else
        {
            await trustedDeviceService.TrustDeviceAsync(
                account.Id, challenge.DeviceId, deviceInfo, platform, "otp_verified", cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        account.LastLoginAt = now;
        account.UpdatedAt = now;

        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        // The device email-OTP just verified IS the §3.3 step-up — evaluating
        // step-up again here would loop the challenge forever.
        var session = await CreateSessionCoreAsync(
            account,
            subject,
            cancellationToken,
            riskStepUpSatisfied: true,
            deviceIdOverride: challenge.DeviceId);
        await db.SaveChangesAsync(cancellationToken);
        await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthSignInSucceeded, cancellationToken: cancellationToken);
        return session;
    }

    /// <summary>Spec §3.3 country allow-list check. Unknown countries (no
    /// CF-IPCountry — local dev, direct-to-origin) and an empty list both
    /// pass: the control is a policy fence, not an authenticity proof.</summary>
    private static bool IsOutsideCountryAllowList(string allowListCsv, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(allowListCsv) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        foreach (var entry in allowListCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(entry, countryCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>RuntimeSettings.Security.DeviceVerificationExemptEmails safety
    /// valve: owner/staff accounts fully exempt from device-verification OTP,
    /// risk step-up, and the learner email-verification OTP gate. The persisted
    /// admin list is the single source of truth so removing an address in the
    /// admin UI revokes the exemption.</summary>
    internal static bool IsDeviceVerificationExempt(string? email, string? exemptEmailsCsv)
        => AreAnyDeviceVerificationExempt([email], exemptEmailsCsv);

    internal static bool AreAnyDeviceVerificationExempt(
        IEnumerable<string?> emails,
        string? exemptEmailsCsv)
    {
        var normalizedEmails = emails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedEmails.Count == 0)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(exemptEmailsCsv)
            && exemptEmailsCsv
                .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(candidate => candidate.ToUpperInvariant())
                .Any(normalizedEmails.Contains);
    }

    private async Task<bool> IsDeviceVerificationExemptAsync(
        ApplicationUserAccount account,
        string? exemptEmailsCsv,
        CancellationToken cancellationToken,
        string? knownProfileEmail = null)
    {
        // Sign-in resolves the learner profile once up front and threads its
        // email through every exemption check, so this lookup must not repeat
        // per call site (LearnerSignIn_LoadsAuthenticationProfileOnce pins it).
        string? profileEmail = knownProfileEmail;
        if (profileEmail is null && string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            profileEmail = await db.Users
                .AsNoTracking()
                .Where(user => user.AuthAccountId == account.Id)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal))
        {
            profileEmail = await db.ExpertUsers
                .AsNoTracking()
                .Where(user => user.AuthAccountId == account.Id)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return AreAnyDeviceVerificationExempt(
            [account.Email, account.NormalizedEmail, profileEmail],
            exemptEmailsCsv);
    }

    /// <summary>Listed exemption emails skip every login OTP (email
    /// verification, device trust, risk step-up, and MFA). Persist
    /// verification so the JWT claim and learner gate match the list.</summary>
    private async Task<bool> ApplySecurityExemptionAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken,
        string? knownProfileEmail = null)
    {
        var csv = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Security.DeviceVerificationExemptEmails;
        if (!await IsDeviceVerificationExemptAsync(account, csv, cancellationToken, knownProfileEmail))
        {
            return false;
        }

        if (account.EmailVerifiedAt is null)
        {
            var now = timeProvider.GetUtcNow();
            account.EmailVerifiedAt = now;
            account.UpdatedAt = now;
        }

        return true;
    }

    private async Task EnsureDeviceVerificationIsRequiredAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken)
    {
        var security = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Security;
        if (await IsDeviceVerificationExemptAsync(account, security.DeviceVerificationExemptEmails, cancellationToken))
        {
            throw ApiException.Forbidden(
                "device_verification_not_required",
                "This account is exempt from device verification. Return to sign in and continue.");
        }
    }

    /// <summary>Spec §3.3 enforce-mode side channel: a blocked high-risk
    /// sign-in raises an admin notification and a security-alert email to the
    /// account owner. Both are best-effort — a notification/email outage must
    /// never change the block outcome (the SecurityEvent row is the durable
    /// record either way).</summary>
    private async Task NotifyRiskSignInBlockedAsync(
        ApplicationUserAccount account,
        IReadOnlyList<string> reasons,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var reasonText = string.Join(", ", reasons);
        if (notifications is not null)
        {
            try
            {
                await notifications.CreateForAdminsAsync(
                    NotificationEventKey.AdminSecurityRiskAlert,
                    "auth_account",
                    account.Id,
                    timeProvider.GetUtcNow().UtcDateTime.Ticks.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["message"] = $"A high-risk sign-in for {account.Email} was blocked ({reasonText}; country: {countryCode ?? "unknown"}).",
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to raise admin risk alert for blocked sign-in on account {AccountId}", account.Id);
            }
        }

        if (emailSender is not null)
        {
            try
            {
                await emailSender.SendAsync(
                    new EmailMessage(
                        account.Email,
                        "Security alert: a sign-in to your account was blocked",
                        $"A sign-in attempt to your OET account from {(countryCode is null ? "an unrecognised location" : $"country {countryCode}")} "
                        + "was blocked because it looked unusual. If this was you, contact support; "
                        + "if it was not, we recommend changing your password.",
                        TemplateKey: EmailTemplateKeys.SecurityAlert,
                        TemplateParameters: new Dictionary<string, object?>
                        {
                            ["reason"] = reasonText,
                            ["country"] = countryCode ?? "unknown",
                        }),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to send security-alert email for blocked sign-in on account {AccountId}", account.Id);
            }
        }
    }

    // Security spec §3.2: same DataProtection-token transport as the MFA
    // challenge above, carrying the account id AND the specific device id
    // being challenged (so verification knows exactly which device to trust
    // without re-deriving it from a header that could differ by the time the
    // OTP is submitted). Extended for the two-device approved limit: the
    // protected ticket also carries the selection mode (free_slot vs
    // replacement_required vs cooldown) and, after the explicit selection step,
    // the chosen TrustedDevice id to replace.
    private string CreateDeviceChallengeToken(string accountId, string deviceId, string mode = "otp_required", Guid? selectedTrustedDeviceId = null)
    {
        var challenge = new DeviceChallengeTicket(accountId, deviceId, timeProvider.GetUtcNow().Add(_mfaChallengeLifetime), mode, selectedTrustedDeviceId);
        var serialized = JsonSerializer.Serialize(challenge);
        var protectedPayload = _deviceChallengeProtector.Protect(serialized);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private string CreateDeviceChallengeTokenForResolution(string accountId, string deviceId, DeviceResolutionResult resolution)
    {
        var mode = resolution.Resolution switch
        {
            DeviceResolution.ReplacementRequired => "replacement_required",
            DeviceResolution.CooldownBlocked => "cooldown",
            DeviceResolution.OtpRequired => "otp_required",
            _ => "otp_required",
        };
        // Cooldown for learners still flows through device OTP recovery, so keep mode as otp_required-like but preserve cooldown flag
        // For free-slot vs replacement we already distinguished.
        return CreateDeviceChallengeToken(accountId, deviceId, mode);
    }

    private DeviceChallengeTicket ReadDeviceChallengeTokenOrThrow(string? challengeToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
        {
            throw ApiException.Validation("device_challenge_token_required", "Device challenge token is required.");
        }

        try
        {
            var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(challengeToken));
            var serialized = _deviceChallengeProtector.Unprotect(protectedPayload);
            var challenge = JsonSerializer.Deserialize<DeviceChallengeTicket>(serialized)
                            ?? throw new InvalidOperationException("Challenge token payload was empty.");
            if (challenge.ExpiresAt <= timeProvider.GetUtcNow())
            {
                throw ApiException.Validation("invalid_device_challenge", "The device challenge is invalid or expired.");
            }

            // Back-compat: older tokens missing Mode/SelectedTrustedDeviceId deserialize with defaults via optional params.
            return challenge;
        }
        catch (ApiException)
        {
            throw;
        }
        catch
        {
            throw ApiException.Validation("invalid_device_challenge", "The device challenge is invalid or expired.");
        }
    }

    private static string FormatCooldownCountdown(int secondsRemaining)
    {
        if (secondsRemaining <= 0) return "0s";
        var ts = TimeSpan.FromSeconds(secondsRemaining);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }
        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }
        return $"{ts.Seconds}s";
    }
}
