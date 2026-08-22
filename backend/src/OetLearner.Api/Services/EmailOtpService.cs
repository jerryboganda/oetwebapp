using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Otp;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed class EmailOtpService(
    LearnerDbContext db,
    IOptions<AuthTokenOptions> authTokenOptions,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    IOtpDeliveryOrchestrator? otpDelivery = null,
    IRuntimeSettingsProvider? runtimeSettings = null,
    IFirebaseSmsOtpClient? firebaseSms = null)
{
    public const string EmailVerificationPurpose = "verify_email";
    public const string PasswordResetPurpose = "reset_password";
    public const string DeviceTrustPurpose = "trust_device";
    internal const string FirebaseSmsSentinelCode = "external:firebase_sms";
    private const int RetryAfterSeconds = 60;
    // H2 (security): cap wrong-code guesses per challenge. 6-digit codes in a
    // 10-minute lifetime + only IP rate limiting left OTP/reset codes brute
    // forceable. Hard-cap attempts; after the cap the challenge is invalidated
    // and the user must request a new code.
    private const int MaxOtpAttempts = 5;

    private readonly TimeSpan _otpLifetime = authTokenOptions.Value.OtpLifetime;

    public async Task<OtpChallengeResponse> RequestEmailVerificationOtpAsync(
        string email,
        CancellationToken cancellationToken = default,
        bool forceNew = false)
    {
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(_otpLifetime);

        // M1 (security): do not disclose account existence. Mirror the
        // password-reset flow: when the account does not exist, return a
        // synthetic masked response instead of a 404.
        if (account is null)
        {
            return new OtpChallengeResponse(
                Guid.NewGuid().ToString(),
                EmailVerificationPurpose,
                "email",
                AuthEmailAddress.Mask(email),
                expiresAt,
                RetryAfterSeconds);
        }

        var pendingChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id && x.Purpose == EmailVerificationPurpose && x.VerifiedAt == null)
            .ToListAsync(cancellationToken);

        // Remounts / auto-send must not rotate a still-valid code. Students
        // were typing the first email into a newer challenge. Explicit Resend
        // (forceNew) still issues a fresh code.
        if (!forceNew)
        {
            var reusable = pendingChallenges
                .Where(x => x.ExpiresAt > now && x.AttemptCount < MaxOtpAttempts)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();

            if (reusable is not null)
            {
                return new OtpChallengeResponse(
                    reusable.Id.ToString(),
                    EmailVerificationPurpose,
                    reusable.DeliveryChannel ?? "email",
                    reusable.DestinationHint ?? AuthEmailAddress.Mask(account.Email),
                    reusable.ExpiresAt,
                    RetryAfterSeconds);
            }
        }

        var challengeId = Guid.NewGuid();

        if (pendingChallenges.Count > 0)
        {
            db.EmailOtpChallenges.RemoveRange(pendingChallenges);
        }

        var otpCode = GenerateSixDigitCode();
        var challenge = new EmailOtpChallenge
        {
            Id = challengeId,
            ApplicationUserAccountId = account.Id,
            Purpose = EmailVerificationPurpose,
            CodeHash = HashOtp(challengeId, otpCode, account.Id, EmailVerificationPurpose),
            AttemptCount = 0,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            Provider = EmailOtpProviders.BrevoEmail,
            DeliveryChannel = "email",
            DestinationHint = AuthEmailAddress.Mask(account.Email)
        };

        var subject = "Verify your email address";
        var textBody = BuildTextBody(account.Email, otpCode, expiresAt);
        await emailSender.SendAsync(new EmailMessage(
            account.Email,
            subject,
            textBody,
            HtmlBody: BuildHtmlBody(subject, account.Email, otpCode, expiresAt),
            TemplateKey: EmailTemplateKeys.EmailVerificationOtp,
            TemplateParameters: new Dictionary<string, object?>
            {
                ["email"] = account.Email,
                ["displayName"] = BuildDisplayName(account.Email),
                ["otpCode"] = otpCode,
                ["expiresAt"] = expiresAt.ToString("O")
            }), cancellationToken);

        if (pendingChallenges.Count > 0)
        {
            db.EmailOtpChallenges.RemoveRange(pendingChallenges);
        }

        db.EmailOtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        return new OtpChallengeResponse(
            challengeId.ToString(),
            EmailVerificationPurpose,
            "email",
            AuthEmailAddress.Mask(account.Email),
            expiresAt,
            RetryAfterSeconds);
    }

    public async Task<ApplicationUserAccount> VerifyEmailVerificationOtpAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ApiException.Validation("otp_code_required", "Verification code is required.");
        }

        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        // M1 (security): use a generic invalid-code error for a missing account
        // so this endpoint does not disclose whether the email is registered.
        if (account is null)
        {
            throw ApiException.Validation("invalid_otp_code", "The verification code is invalid.");
        }

        var challenge = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id
                && x.Purpose == EmailVerificationPurpose
                && x.VerifiedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            throw ApiException.Validation("invalid_otp_code", "The verification code is invalid.");
        }

        var now = timeProvider.GetUtcNow();
        if (challenge.ExpiresAt <= now)
        {
            throw ApiException.Validation("expired_otp_code", "The verification code has expired.");
        }

        // H2 (security): enforce the per-challenge attempt cap before any
        // further comparison. Over-the-cap presentations do not leak further
        // information about the stored code.
        if (challenge.AttemptCount >= MaxOtpAttempts)
        {
            throw ApiException.Validation("otp_attempts_exceeded", "Too many invalid attempts. Request a new code.");
        }

        // Email verification is always a local hash compare. SMS must never
        // mark EmailVerifiedAt.
        var codeHash = HashOtp(challenge.Id, code.Trim(), account.Id, EmailVerificationPurpose);
        // M6 (security): constant-time comparison of the hex-encoded hashes to
        // avoid any micro-timing leak around the stored code hash.
        if (!FixedTimeHexEquals(challenge.CodeHash, codeHash))
        {
            challenge.AttemptCount += 1;
            await db.SaveChangesAsync(cancellationToken);
            throw ApiException.Validation("invalid_otp_code", "The verification code is invalid.");
        }

        challenge.VerifiedAt = now;
        account.EmailVerifiedAt ??= now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return account;
    }

    public async Task<OtpChallengeResponse> RequestPasswordResetOtpAsync(
        string email,
        CancellationToken cancellationToken = default,
        string? recaptchaToken = null)
    {
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(_otpLifetime);

        if (account is null)
        {
            return new OtpChallengeResponse(
                Guid.NewGuid().ToString(),
                PasswordResetPurpose,
                "email",
                AuthEmailAddress.Mask(email),
                expiresAt,
                RetryAfterSeconds);
        }

        return await IssueChannelOtpAsync(
            account,
            PasswordResetPurpose,
            recaptchaToken,
            now,
            expiresAt,
            emailSubject: "Reset your password",
            emailText: (otpCode) => BuildPasswordResetTextBody(account.Email, otpCode, expiresAt),
            emailTemplate: EmailTemplateKeys.PasswordResetOtp,
            cancellationToken);
    }

    public async Task<ApplicationUserAccount> VerifyPasswordResetOtpAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ApiException.Validation("reset_token_required", "Password reset code is required.");
        }

        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (account is null)
        {
            throw ApiException.Validation("invalid_reset_token", "The password reset token is invalid.");
        }

        var challenge = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id
                && x.Purpose == PasswordResetPurpose
                && x.VerifiedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            throw ApiException.Validation("invalid_reset_token", "The password reset token is invalid.");
        }

        var now = timeProvider.GetUtcNow();
        if (challenge.ExpiresAt <= now)
        {
            throw ApiException.Validation("expired_reset_token", "The password reset token has expired.");
        }

        // H2 (security): cap attempts on password reset tokens identically.
        if (challenge.AttemptCount >= MaxOtpAttempts)
        {
            throw ApiException.Validation("invalid_reset_token", "The password reset token is invalid.");
        }

        if (!await MatchesChallengeCodeAsync(challenge, account, code.Trim(), cancellationToken))
        {
            challenge.AttemptCount += 1;
            await db.SaveChangesAsync(cancellationToken);
            throw ApiException.Validation("invalid_reset_token", "The password reset token is invalid.");
        }

        challenge.VerifiedAt = now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return account;
    }

    /// <summary>Security spec §3.2: challenge for approving sign-in from a new
    /// device. Unlike the two purposes above, the account is already known
    /// (resolved mid-sign-in via the device challenge token) — no separate
    /// email-based lookup or account-enumeration guard is needed here.</summary>
    public async Task<OtpChallengeResponse> RequestDeviceTrustOtpAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken = default,
        string? recaptchaToken = null)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(_otpLifetime);
        return await IssueChannelOtpAsync(
            account,
            DeviceTrustPurpose,
            recaptchaToken,
            now,
            expiresAt,
            emailSubject: "Approve this new device",
            emailText: (otpCode) => $"Hello {BuildDisplayName(account.Email)},\n\nA sign-in from a new device needs approval. Your code is {otpCode}.\nIt expires at {expiresAt:O}.\n\nIf this wasn't you, do not share this code and consider changing your password.",
            emailTemplate: EmailTemplateKeys.EmailVerificationOtp,
            cancellationToken);
    }

    public async Task VerifyDeviceTrustOtpAsync(
        ApplicationUserAccount account, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ApiException.Validation("otp_code_required", "Verification code is required.");
        }

        var challenge = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id
                && x.Purpose == DeviceTrustPurpose
                && x.VerifiedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            throw ApiException.Validation("invalid_otp_code", "The verification code is invalid.");
        }

        var now = timeProvider.GetUtcNow();
        if (challenge.ExpiresAt <= now)
        {
            throw ApiException.Validation("expired_otp_code", "The verification code has expired.");
        }

        if (challenge.AttemptCount >= MaxOtpAttempts)
        {
            throw ApiException.Validation("otp_attempts_exceeded", "Too many invalid attempts. Request a new code.");
        }

        if (!await MatchesChallengeCodeAsync(challenge, account, code.Trim(), cancellationToken))
        {
            challenge.AttemptCount += 1;
            await db.SaveChangesAsync(cancellationToken);
            throw ApiException.Validation("invalid_otp_code", "The verification code is invalid.");
        }

        challenge.VerifiedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<OtpChallengeResponse> IssueChannelOtpAsync(
        ApplicationUserAccount account,
        string purpose,
        string? recaptchaToken,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        string emailSubject,
        Func<string, string> emailText,
        string emailTemplate,
        CancellationToken cancellationToken)
    {
        var challengeId = Guid.NewGuid();
        var pendingChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id && x.Purpose == purpose && x.VerifiedAt == null)
            .ToListAsync(cancellationToken);

        if (pendingChallenges.Count > 0)
        {
            db.EmailOtpChallenges.RemoveRange(pendingChallenges);
        }

        var sms = otpDelivery is null
            ? new OtpSmsDeliveryResult(false, false, null, null)
            : await otpDelivery.TrySendFirebaseSmsAsync(account, purpose, recaptchaToken, cancellationToken);

        EmailOtpChallenge challenge;
        string deliveryChannel;
        string destinationHint;

        if (sms.Delivered && !string.IsNullOrWhiteSpace(sms.SessionInfo) && !string.IsNullOrWhiteSpace(sms.PhoneNumber))
        {
            // Firebase generated the only numeric code. Do not also email a
            // different local code for this challenge.
            challenge = new EmailOtpChallenge
            {
                Id = challengeId,
                ApplicationUserAccountId = account.Id,
                Purpose = purpose,
                CodeHash = CreateFirebaseSentinelHash(challengeId, account.Id, purpose),
                AttemptCount = 0,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                Provider = EmailOtpProviders.FirebaseSms,
                DeliveryChannel = "sms",
                DestinationHint = PhoneNumberNormalizer.Mask(sms.PhoneNumber),
                ExternalSessionInfoEncrypted = runtimeSettings?.Protect(sms.SessionInfo)
            };
            deliveryChannel = "sms";
            destinationHint = challenge.DestinationHint ?? PhoneNumberNormalizer.Mask(sms.PhoneNumber);
        }
        else
        {
            var otpCode = GenerateSixDigitCode();
            challenge = new EmailOtpChallenge
            {
                Id = challengeId,
                ApplicationUserAccountId = account.Id,
                Purpose = purpose,
                CodeHash = HashOtp(challengeId, otpCode, account.Id, purpose),
                AttemptCount = 0,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                Provider = EmailOtpProviders.BrevoEmail,
                DeliveryChannel = "email",
                DestinationHint = AuthEmailAddress.Mask(account.Email)
            };

            await emailSender.SendAsync(new EmailMessage(
                account.Email,
                emailSubject,
                emailText(otpCode),
                HtmlBody: BuildHtmlBody(emailSubject, account.Email, otpCode, expiresAt),
                TemplateKey: emailTemplate,
                TemplateParameters: new Dictionary<string, object?>
                {
                    ["email"] = account.Email,
                    ["displayName"] = BuildDisplayName(account.Email),
                    ["otpCode"] = otpCode,
                    ["expiresAt"] = expiresAt.ToString("O")
                }), cancellationToken);

            deliveryChannel = "email";
            destinationHint = AuthEmailAddress.Mask(account.Email);
        }

        db.EmailOtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        return new OtpChallengeResponse(
            challengeId.ToString(),
            purpose,
            deliveryChannel,
            destinationHint,
            expiresAt,
            RetryAfterSeconds);
    }

    private async Task<bool> MatchesChallengeCodeAsync(
        EmailOtpChallenge challenge,
        ApplicationUserAccount account,
        string code,
        CancellationToken cancellationToken)
    {
        if (IsFirebaseSmsChallenge(challenge))
        {
            return await VerifyFirebaseSmsAsync(challenge, account, code, cancellationToken);
        }

        var codeHash = HashOtp(challenge.Id, code, account.Id, challenge.Purpose);
        return FixedTimeHexEquals(challenge.CodeHash, codeHash);
    }

    private async Task<bool> VerifyFirebaseSmsAsync(
        EmailOtpChallenge challenge,
        ApplicationUserAccount account,
        string code,
        CancellationToken cancellationToken)
    {
        if (firebaseSms is null || runtimeSettings is null)
        {
            return false;
        }

        var sessionInfo = runtimeSettings.Unprotect(challenge.ExternalSessionInfoEncrypted);
        var webKey = (await runtimeSettings.GetAsync(cancellationToken)).FirebaseOtp.WebApiKey;
        if (string.IsNullOrWhiteSpace(sessionInfo) || string.IsNullOrWhiteSpace(webKey))
        {
            return false;
        }

        var result = await firebaseSms.VerifyCodeAsync(sessionInfo, code, webKey, cancellationToken);
        if (!result.Success)
        {
            return false;
        }

        var storedPhone = await db.LearnerRegistrationProfiles
            .AsNoTracking()
            .Where(x => x.ApplicationUserAccountId == account.Id)
            .Select(x => x.MobileNumber)
            .FirstOrDefaultAsync(cancellationToken);
        var expected = PhoneNumberNormalizer.TryNormalize(storedPhone);
        var actual = PhoneNumberNormalizer.TryNormalize(result.PhoneNumber);
        return expected is not null
            && actual is not null
            && string.Equals(expected, actual, StringComparison.Ordinal);
    }

    private static bool IsFirebaseSmsChallenge(EmailOtpChallenge challenge)
        => string.Equals(challenge.Provider, EmailOtpProviders.FirebaseSms, StringComparison.Ordinal)
           || (string.Equals(challenge.DeliveryChannel, "sms", StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(challenge.ExternalSessionInfoEncrypted));

    internal static string CreateFirebaseSentinelHash(Guid challengeId, string accountId, string purpose)
        => HashOtp(challengeId, FirebaseSmsSentinelCode, accountId, purpose);

    private static string GenerateSixDigitCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    internal static string HashOtp(Guid challengeId, string code, string accountId, string purpose)
    {
        var payload = $"{challengeId:N}:{accountId}:{purpose}:{code}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string BuildTextBody(string displayName, string otpCode, DateTimeOffset expiresAt)
        => $"Hello {displayName},\n\nYour OET Learner verification code is {otpCode}.\nIt expires at {expiresAt:O}.\n\nIf you did not request this, you can ignore this message.";

    private static string BuildPasswordResetTextBody(string displayName, string otpCode, DateTimeOffset expiresAt)
        => $"Hello {displayName},\n\nYour OET Learner password reset code is {otpCode}.\nIt expires at {expiresAt:O}.\n\nIf you did not request this, you can ignore this message.";

    // M8 (security): HTML-encode every runtime-interpolated value. The email
    // local-part (used as displayName) is attacker-controllable at registration
    // time, and HTML-injection in transactional email bodies is cheap to fix.
    private static string BuildHtmlBody(string title, string displayName, string otpCode, DateTimeOffset expiresAt)
    {
        var encoder = HtmlEncoder.Default;
        return $"<div style=\"font-family:Arial,sans-serif;line-height:1.6;color:#10233f\"><h2>{encoder.Encode(title)}</h2><p>Hello {encoder.Encode(displayName)},</p><p>Your OET Learner code is <strong>{encoder.Encode(otpCode)}</strong>.</p><p>It expires at {encoder.Encode(expiresAt.ToString("O"))}.</p><p>If you did not request this, you can ignore this message.</p></div>";
    }

    private static string BuildDisplayName(string email)
        => string.IsNullOrWhiteSpace(email) ? "there" : email.Split('@', 2)[0];

    private static bool FixedTimeHexEquals(string a, string b)
    {
        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }
        var aBytes = Encoding.ASCII.GetBytes(a);
        var bBytes = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
