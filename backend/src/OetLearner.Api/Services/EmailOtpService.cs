using System.Security.Cryptography;
using System.Text;
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

    private readonly TimeSpan _otpLifetime = authTokenOptions.Value.OtpLifetime;

    public async Task<OtpChallengeResponse> RequestEmailVerificationOtpAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (account is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "No account was found for the provided email address.");
        }

        var now = timeProvider.GetUtcNow();
        var challengeId = Guid.NewGuid();
        var pendingChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id && x.Purpose == EmailVerificationPurpose && x.VerifiedAt == null)
            .ToListAsync(cancellationToken);

        if (pendingChallenges.Count > 0)
        {
            db.EmailOtpChallenges.RemoveRange(pendingChallenges);
        }

        var otpCode = GenerateSixDigitCode();
        var expiresAt = now.Add(_otpLifetime);
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

        if (account is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "No account was found for the provided email address.");
        }

        var challenge = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id
                && x.Purpose == EmailVerificationPurpose
                && x.VerifiedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            throw ApiException.Validation("otp_challenge_not_found", "No active verification challenge exists for this email.");
        }

        var now = timeProvider.GetUtcNow();
        if (challenge.ExpiresAt <= now)
        {
            throw ApiException.Validation("expired_otp_code", "The verification code has expired.");
        }

        var codeHash = HashOtp(challenge.Id, code.Trim(), account.Id, EmailVerificationPurpose);
        if (!string.Equals(challenge.CodeHash, codeHash, StringComparison.Ordinal))
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

        var codeHash = HashOtp(challenge.Id, code.Trim(), account.Id, PasswordResetPurpose);
        if (!string.Equals(challenge.CodeHash, codeHash, StringComparison.Ordinal))
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

    private static string BuildHtmlBody(string title, string displayName, string otpCode, DateTimeOffset expiresAt)
        => $"<div style=\"font-family:Arial,sans-serif;line-height:1.6;color:#10233f\"><h2>{title}</h2><p>Hello {displayName},</p><p>Your OET Learner code is <strong>{otpCode}</strong>.</p><p>It expires at {expiresAt:O}.</p><p>If you did not request this, you can ignore this message.</p></div>";

    private static string BuildDisplayName(string email)
        => string.IsNullOrWhiteSpace(email) ? "there" : email.Split('@', 2)[0];
}
