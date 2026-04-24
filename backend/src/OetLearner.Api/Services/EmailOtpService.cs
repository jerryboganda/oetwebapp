using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class EmailOtpService(
    LearnerDbContext db,
    IOptions<AuthTokenOptions> authTokenOptions,
    IEmailSender emailSender,
    TimeProvider timeProvider)
{
    public const string EmailVerificationPurpose = "verify_email";
    public const string PasswordResetPurpose = "reset_password";
    private const int RetryAfterSeconds = 60;
    // H2 (security): cap wrong-code guesses per challenge. 6-digit codes in a
    // 10-minute lifetime + only IP rate limiting left OTP/reset codes brute
    // forceable. Hard-cap attempts; after the cap the challenge is invalidated
    // and the user must request a new code.
    private const int MaxOtpAttempts = 5;

    private readonly TimeSpan _otpLifetime = authTokenOptions.Value.OtpLifetime;

    public async Task<OtpChallengeResponse> RequestEmailVerificationOtpAsync(string email, CancellationToken cancellationToken = default)
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

        var challengeId = Guid.NewGuid();
        var pendingChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id && x.Purpose == EmailVerificationPurpose && x.VerifiedAt == null)
            .ToListAsync(cancellationToken);

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
            ExpiresAt = expiresAt
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

    public async Task<OtpChallengeResponse> RequestPasswordResetOtpAsync(string email, CancellationToken cancellationToken = default)
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

        var challengeId = Guid.NewGuid();
        var pendingChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id && x.Purpose == PasswordResetPurpose && x.VerifiedAt == null)
            .ToListAsync(cancellationToken);

        if (pendingChallenges.Count > 0)
        {
            db.EmailOtpChallenges.RemoveRange(pendingChallenges);
        }

        var otpCode = GenerateSixDigitCode();
        var challenge = new EmailOtpChallenge
        {
            Id = challengeId,
            ApplicationUserAccountId = account.Id,
            Purpose = PasswordResetPurpose,
            CodeHash = HashOtp(challengeId, otpCode, account.Id, PasswordResetPurpose),
            AttemptCount = 0,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        await emailSender.SendAsync(
            new EmailMessage(
                account.Email,
                "Reset your password",
                BuildPasswordResetTextBody(account.Email, otpCode, expiresAt),
                HtmlBody: BuildHtmlBody("Reset your password", account.Email, otpCode, expiresAt),
                TemplateKey: EmailTemplateKeys.PasswordResetOtp,
                TemplateParameters: new Dictionary<string, object?>
                {
                    ["email"] = account.Email,
                    ["displayName"] = BuildDisplayName(account.Email),
                    ["otpCode"] = otpCode,
                    ["expiresAt"] = expiresAt.ToString("O")
                }),
            cancellationToken);

        db.EmailOtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        return new OtpChallengeResponse(
            challengeId.ToString(),
            PasswordResetPurpose,
            "email",
            AuthEmailAddress.Mask(account.Email),
            expiresAt,
            RetryAfterSeconds);
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

        var codeHash = HashOtp(challenge.Id, code.Trim(), account.Id, PasswordResetPurpose);
        // M6 (security): constant-time comparison.
        if (!FixedTimeHexEquals(challenge.CodeHash, codeHash))
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

    private static string GenerateSixDigitCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashOtp(Guid challengeId, string code, string accountId, string purpose)
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

    // M6 (security): constant-time comparison for hex-encoded digests. Both
    // operands come from HashOtp which returns upper-case hex of identical
    // length, so length differences can only indicate tampering.
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
