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
    public Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
        => BeginAuthenticatorSetupAsync(principal, currentPassword: null, currentCode: null, recoveryCode: null, cancellationToken);

    /// <summary>
    /// Starts (or, hardened, replaces) authenticator enrolment. First-time enrolment is
    /// unchanged. When an authenticator is ALREADY enabled, replacing it requires the
    /// current password plus a current authenticator code (replay-guarded) or an unused
    /// recovery code; a successful re-enrolment is recorded as an
    /// <c>auth.authenticator_reenrolled</c> SecurityEvent (atomically with the secret
    /// rotation), which revokes every Owner Agent Console unlock ticket issued before it
    /// and blocks console unlock for 72 hours, and the account holder is emailed.
    /// </summary>
    public async Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(
        ClaimsPrincipal principal,
        string? currentPassword,
        string? currentCode,
        string? recoveryCode,
        CancellationToken cancellationToken = default)
    {
        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var isReenrolment = account.AuthenticatorEnabledAt is not null;
        string? reenrolmentFactor = null;
        if (isReenrolment)
        {
            reenrolmentFactor = await VerifyReenrolmentFactorsAsync(account, currentPassword, currentCode, recoveryCode, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var secretKey = AuthenticatorTotp.GenerateSecretKey();
        var recoveryCodes = AuthenticatorTotp.GenerateRecoveryCodes();
        if (db.Database.IsInMemory())
        {
            var existingRecoveryCodes = await db.MfaRecoveryCodes
                .Where(x => x.ApplicationUserAccountId == account.Id)
                .ToListAsync(cancellationToken);

            if (existingRecoveryCodes.Count > 0)
            {
                db.MfaRecoveryCodes.RemoveRange(existingRecoveryCodes);
            }
        }
        else
        {
            await db.MfaRecoveryCodes
                .Where(x => x.ApplicationUserAccountId == account.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        db.MfaRecoveryCodes.AddRange(recoveryCodes.Select(code => new MfaRecoveryCode
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = account.Id,
            CodeHash = AuthenticatorTotp.HashRecoveryCode(code),
            CreatedAt = now
        }));

        account.ProtectedAuthenticatorSecret = _authenticatorSecretProtector.Protect(secretKey);
        account.AuthenticatorEnabledAt = null;
        account.UpdatedAt = now;
        if (isReenrolment)
        {
            // Same SaveChanges as the rotation: the revocation watermark / unlock cooldown
            // must exist whenever the old authenticator stopped being the live one.
            db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
                account.Id,
                OwnerAgentSecurityEventKinds.AuthenticatorReenrolled,
                now,
                httpContextAccessor.HttpContext,
                details: new { factor = reenrolmentFactor }));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (isReenrolment)
        {
            await TrySendAuthenticatorSecurityEmailAsync(
                account,
                "Security alert: your authenticator app was replaced",
                "The authenticator app on your OET account was just replaced. If this was not you, "
                + "reset your password immediately and contact support.",
                "authenticator_reenrolled",
                cancellationToken);
        }

        var otpAuthUri = BuildOtpAuthUri(account.Email, secretKey);
        return new AuthenticatorSetupResponse(
            secretKey,
            otpAuthUri,
            BuildQrCodeDataUrl(secretKey, otpAuthUri),
            recoveryCodes);
    }

    public async Task<CurrentUserResponse> ConfirmAuthenticatorSetupAsync(
        ClaimsPrincipal principal,
        ConfirmAuthenticatorSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw ApiException.Validation("authenticator_code_required", "Authenticator code is required.");
        }

        var (account, authenticatedLearner) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        if (!AuthenticatorTotp.VerifyCode(secretKey, request.Code, timeProvider.GetUtcNow(), AllowedAuthenticatorDriftWindows))
        {
            throw ApiException.Validation("invalid_authenticator_code", "The authenticator code is invalid.");
        }

        if (account.AuthenticatorEnabledAt is not null)
        {
            // Hardened re-enrolment: confirm never re-arms an authenticator that is already
            // live (replacing one must go through BeginAuthenticatorSetupAsync with the
            // current factors). A repeated confirm with a valid current code is an
            // idempotent no-op, so a double-submitted setup form still succeeds.
            var unchangedSubject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
            return BuildCurrentUserResponse(unchangedSubject);
        }

        var now = timeProvider.GetUtcNow();
        account.AuthenticatorEnabledAt = now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        return BuildCurrentUserResponse(subject);
    }

    public async Task<AuthSessionResponse> CompleteMfaChallengeAsync(
        MfaChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw ApiException.Validation("authenticator_code_required", "Authenticator code is required.");
        }

        var (account, authenticatedLearner) = await ResolveTrackedMfaAccountAsync(request.Email, request.ChallengeToken, cancellationToken);
        // H2 (security): cap MFA attempts per account per challenge window.
        EnsureMfaAttemptsAvailable(account.Id);
        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        if (!AuthenticatorTotp.VerifyCode(secretKey, request.Code, timeProvider.GetUtcNow(), AllowedAuthenticatorDriftWindows))
        {
            RegisterMfaFailure(account.Id);
            await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthMfaFailed, cancellationToken: cancellationToken);
            throw ApiException.Validation("invalid_authenticator_code", "The authenticator code is invalid.");
        }

        ResetMfaAttempts(account.Id);
        return await CompleteMfaSignInAsync(account, authenticatedLearner, cancellationToken);
    }

    public async Task<AuthSessionResponse> CompleteRecoveryChallengeAsync(
        MfaChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            throw ApiException.Validation("mfa_recovery_code_required", "Recovery code is required.");
        }

        var (account, authenticatedLearner) = await ResolveTrackedMfaAccountAsync(request.Email, request.ChallengeToken, cancellationToken);
        // H2 (security): same attempt cap covers recovery-code attempts.
        EnsureMfaAttemptsAvailable(account.Id);
        var codeHash = AuthenticatorTotp.HashRecoveryCode(request.RecoveryCode);
        var recoveryCode = await db.MfaRecoveryCodes
            .SingleOrDefaultAsync(
                x => x.ApplicationUserAccountId == account.Id
                    && x.RedeemedAt == null
                    && x.CodeHash == codeHash,
                cancellationToken);

        if (recoveryCode is null)
        {
            RegisterMfaFailure(account.Id);
            await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthMfaFailed, cancellationToken: cancellationToken);
            throw ApiException.Validation("invalid_mfa_recovery_code", "The recovery code is invalid or already used.");
        }

        recoveryCode.RedeemedAt = timeProvider.GetUtcNow();
        ResetMfaAttempts(account.Id);
        return await CompleteMfaSignInAsync(account, authenticatedLearner, cancellationToken);
    }

    // H2 helpers (security): per-account sliding counter of invalid MFA
    // attempts. The sliding window matches the MFA challenge lifetime so
    // legitimate retries reset naturally.
    private void EnsureMfaAttemptsAvailable(string accountId)
    {
        if (memoryCache.TryGetValue(MfaAttemptCacheKey(accountId), out int attempts) && attempts >= MaxMfaAttempts)
        {
            throw ApiException.Validation("mfa_attempts_exceeded", "Too many invalid authentication attempts. Try again later.");
        }
    }

    private void RegisterMfaFailure(string accountId)
    {
        var key = MfaAttemptCacheKey(accountId);
        var attempts = memoryCache.TryGetValue(key, out int existing) ? existing + 1 : 1;
        memoryCache.Set(key, attempts, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _mfaChallengeLifetime
        });
    }

    private void ResetMfaAttempts(string accountId) => memoryCache.Remove(MfaAttemptCacheKey(accountId));

    private static string MfaAttemptCacheKey(string accountId) => $"auth:mfa-attempts:{accountId}";

    // ════════════════════════════════════════════════════════════════════════
    //  Authenticator step-up (Owner Agent Console) + hardened re-enrolment
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Password + current TOTP re-verification for an already signed-in principal
    /// (Owner Agent Console unlock). Rules:
    /// <list type="bullet">
    /// <item>the account must have an enabled authenticator (<c>AuthenticatorEnabledAt</c>);</item>
    /// <item>recovery codes are never accepted (and never consumed here);</item>
    /// <item>a code whose RFC 6238 time-step is not newer than the last accepted step-up
    /// step is rejected as a replay (durable, via <c>auth.step_up_succeeded</c> rows);</item>
    /// <item>failures are counted both in-process and from recent <c>auth.mfa_failed</c>
    /// SecurityEvents (so a slot switch or restart does not reset the budget); the
    /// account holder is emailed when the budget is exhausted.</item>
    /// </list>
    /// </summary>
    public Task<AuthenticatorStepUpResult> VerifyAuthenticatorStepUpAsync(
        ClaimsPrincipal principal,
        string? password,
        string? code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw ApiException.Validation("password_required", "Your current password is required.");
        }

        return VerifyAuthenticatorStepUpCoreAsync(principal, password, code, "unlock", cancellationToken);
    }

    private async Task<AuthenticatorStepUpResult> VerifyAuthenticatorStepUpCoreAsync(
        ClaimsPrincipal principal,
        string? password,
        string? code,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ApiException.Validation("authenticator_code_required", "Authenticator code is required.");
        }

        var claimedAccountId = principal.FindFirstValue(AuthTokenService.AuthAccountIdClaimType);
        if (string.IsNullOrWhiteSpace(claimedAccountId))
        {
            throw ApiException.Forbidden("step_up_account_required", "Sign in again to verify your identity.");
        }

        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        if (!string.Equals(account.Id, claimedAccountId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("step_up_account_mismatch", "Sign in again to verify your identity.");
        }

        if (account.AuthenticatorEnabledAt is null || string.IsNullOrWhiteSpace(account.ProtectedAuthenticatorSecret))
        {
            throw ApiException.Forbidden("mfa_not_configured", "Enable an authenticator app on this account first.");
        }

        var now = timeProvider.GetUtcNow();
        await EnsureStepUpAttemptsAvailableAsync(account, now, cancellationToken);

        var requirePassword = password is not null;
        var invalidCode = requirePassword ? "invalid_step_up_credentials" : "invalid_authenticator_code";
        var invalidMessage = requirePassword
            ? "The password or authenticator code is incorrect."
            : "The authenticator code is incorrect.";

        if (LooksLikeRecoveryCode(code))
        {
            // Recovery codes are a sign-in fallback only. Rejected on shape BEFORE the password
            // is checked, so this answer can never confirm a guessed password; not checked
            // against the stored codes (no oracle, nothing consumed) and not counted as a failure.
            throw ApiException.Validation(
                "recovery_code_not_accepted",
                "Recovery codes cannot be used here. Enter the current code from your authenticator app.");
        }

        if (requirePassword
            && passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password!) == PasswordVerificationResult.Failed)
        {
            await RegisterStepUpFailureAsync(account, $"{purpose}_password", now, cancellationToken);
            throw ApiException.Validation(invalidCode, invalidMessage);
        }

        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        var timeStep = await AcceptAuthenticatorCodeOnceAsync(
            account, secretKey, code, purpose, now, invalidCode, invalidMessage, cancellationToken);
        ResetMfaAttempts(account.Id);
        return new AuthenticatorStepUpResult(account.Id, timeStep, now);
    }

    /// <summary>
    /// Re-authentication required to REPLACE an enabled authenticator: current password
    /// plus either a current TOTP code (replay-guarded) or an unused recovery code.
    /// Returns which second factor was used. The recovery code is not marked redeemed
    /// here because the rotation that follows replaces every recovery code anyway.
    /// </summary>
    private async Task<string> VerifyReenrolmentFactorsAsync(
        ApplicationUserAccount account,
        string? currentPassword,
        string? currentCode,
        string? recoveryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentPassword)
            || (string.IsNullOrWhiteSpace(currentCode) && string.IsNullOrWhiteSpace(recoveryCode)))
        {
            throw ApiException.Forbidden(
                "authenticator_reauthentication_required",
                "An authenticator app is already enabled. Enter your current password and a current authenticator code "
                + "(or an unused recovery code) to replace it.");
        }

        const string invalidCode = "invalid_reauthentication";
        const string invalidMessage = "The password or verification code is incorrect.";
        var now = timeProvider.GetUtcNow();
        await EnsureStepUpAttemptsAvailableAsync(account, now, cancellationToken);

        if (passwordHasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            await RegisterStepUpFailureAsync(account, "reenrolment_password", now, cancellationToken);
            throw ApiException.Validation(invalidCode, invalidMessage);
        }

        if (!string.IsNullOrWhiteSpace(currentCode))
        {
            var secretKey = ReadAuthenticatorSecretOrThrow(account);
            await AcceptAuthenticatorCodeOnceAsync(
                account, secretKey, currentCode, "reenrolment", now, invalidCode, invalidMessage, cancellationToken);
            ResetMfaAttempts(account.Id);
            return "totp";
        }

        var codeHash = AuthenticatorTotp.HashRecoveryCode(recoveryCode!);
        var recoveryCodeValid = await db.MfaRecoveryCodes
            .AsNoTracking()
            .AnyAsync(
                x => x.ApplicationUserAccountId == account.Id
                     && x.RedeemedAt == null
                     && x.CodeHash == codeHash,
                cancellationToken);
        if (!recoveryCodeValid)
        {
            await RegisterStepUpFailureAsync(account, "reenrolment_recovery_code", now, cancellationToken);
            throw ApiException.Validation(invalidCode, invalidMessage);
        }

        ResetMfaAttempts(account.Id);
        return "recovery_code";
    }

    /// <summary>
    /// Verifies <paramref name="code"/> against the secret and records its time-step as the
    /// newest accepted one, all under <see cref="TotpStepUpGate"/>. Throws the supplied
    /// invalid-credential error, or <c>authenticator_code_replayed</c> for a replayed step.
    /// </summary>
    private async Task<long> AcceptAuthenticatorCodeOnceAsync(
        ApplicationUserAccount account,
        string secretKey,
        string code,
        string purpose,
        DateTimeOffset now,
        string invalidCode,
        string invalidMessage,
        CancellationToken cancellationToken)
    {
        await TotpStepUpGate.WaitAsync(cancellationToken);
        try
        {
            var timeStep = MatchAuthenticatorTimeStep(secretKey, code, now);
            if (timeStep is null)
            {
                await RegisterStepUpFailureAsync(account, $"{purpose}_totp", now, cancellationToken);
                throw ApiException.Validation(invalidCode, invalidMessage);
            }

            var recentSuccesses = await OwnerAgentSecurityEvents.RecentAsync(
                db,
                account.Id,
                [OwnerAgentSecurityEventKinds.StepUpSucceeded],
                now - StepUpReplayLookback,
                cancellationToken);
            var lastAcceptedStep = recentSuccesses
                .Select(row => OwnerAgentSecurityEvents.ReadLongDetail(row.DetailsJson, "timeStep"))
                .Where(step => step is not null)
                .Select(step => step!.Value)
                .DefaultIfEmpty(long.MinValue)
                .Max();
            if (timeStep.Value <= lastAcceptedStep)
            {
                await RegisterStepUpFailureAsync(account, $"{purpose}_totp_replay", now, cancellationToken);
                throw ApiException.Validation(
                    "authenticator_code_replayed",
                    "This authenticator code was already used. Wait for the next code and try again.");
            }

            db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
                account.Id,
                OwnerAgentSecurityEventKinds.StepUpSucceeded,
                now,
                httpContextAccessor.HttpContext,
                details: new { timeStep = timeStep.Value, purpose }));
            await db.SaveChangesAsync(cancellationToken);
            return timeStep.Value;
        }
        finally
        {
            TotpStepUpGate.Release();
        }
    }

    /// <summary>The RFC 6238 time-step (±1 drift window) that <paramref name="code"/> matches, if any.</summary>
    private static long? MatchAuthenticatorTimeStep(string secretKey, string code, DateTimeOffset now)
    {
        for (var offset = -AllowedAuthenticatorDriftWindows; offset <= AllowedAuthenticatorDriftWindows; offset++)
        {
            var candidateTime = now.AddSeconds(offset * TotpTimeStepSeconds);
            if (AuthenticatorTotp.VerifyCode(secretKey, code, candidateTime, allowedDriftWindows: 0))
            {
                return candidateTime.ToUnixTimeSeconds() / TotpTimeStepSeconds;
            }
        }

        return null;
    }

    private static bool LooksLikeRecoveryCode(string code)
    {
        var digits = VerificationCodeDigits.Normalize(code);
        if (digits.Length == 6 && digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var compact = AuthenticatorTotp.NormalizeRecoveryCode(code);
        return compact.Length >= 16 && compact.All(char.IsAsciiHexDigit);
    }

    private async Task EnsureStepUpAttemptsAvailableAsync(
        ApplicationUserAccount account,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (account.LockoutUntil is { } lockedUntil && lockedUntil > now)
        {
            throw ApiException.Validation("mfa_attempts_exceeded", "Too many invalid authentication attempts. Try again later.");
        }

        EnsureMfaAttemptsAvailable(account.Id);
        if (await CountRecentStepUpFailuresAsync(account.Id, now, cancellationToken) >= MaxMfaAttempts)
        {
            throw ApiException.Validation("mfa_attempts_exceeded", "Too many invalid authentication attempts. Try again in 15 minutes.");
        }
    }

    /// <summary>
    /// DB-backed failure count: <c>auth.mfa_failed</c> rows in the last
    /// <see cref="StepUpFailureWindow"/> after the most recent successful step-up.
    /// </summary>
    private async Task<int> CountRecentStepUpFailuresAsync(string accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await OwnerAgentSecurityEvents.RecentAsync(
            db,
            accountId,
            [SecurityEventKinds.AuthMfaFailed, OwnerAgentSecurityEventKinds.StepUpSucceeded],
            now - StepUpFailureWindow,
            cancellationToken);
        var lastSuccess = rows
            .Where(row => row.Kind == OwnerAgentSecurityEventKinds.StepUpSucceeded)
            .Select(row => (DateTimeOffset?)row.OccurredAt)
            .Max();
        return rows.Count(row => row.Kind == SecurityEventKinds.AuthMfaFailed
                                 && (lastSuccess is null || row.OccurredAt > lastSuccess));
    }

    private async Task RegisterStepUpFailureAsync(
        ApplicationUserAccount account,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        RegisterMfaFailure(account.Id);
        await securityEventLogger.TryLogAsync(
            account.Id,
            SecurityEventKinds.AuthMfaFailed,
            details: new { reason },
            cancellationToken: cancellationToken);

        int failures;
        try
        {
            failures = await CountRecentStepUpFailuresAsync(account.Id, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Could not count step-up failures for account {AccountId}", account.Id);
            return;
        }

        if (failures == MaxMfaAttempts)
        {
            await TrySendAuthenticatorSecurityEmailAsync(
                account,
                "Security alert: identity confirmation locked",
                "Several incorrect passwords or authenticator codes were entered to confirm your identity on your OET account. "
                + "Further attempts are blocked for 15 minutes. If this was not you, change your password immediately.",
                "step_up_attempts_exceeded",
                cancellationToken);
        }
    }

    /// <summary>Best-effort security notice to the account holder; never changes the outcome.</summary>
    private async Task TrySendAuthenticatorSecurityEmailAsync(
        ApplicationUserAccount account,
        string subject,
        string body,
        string reason,
        CancellationToken cancellationToken)
    {
        if (emailSender is null)
        {
            return;
        }

        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    account.Email,
                    subject,
                    body,
                    TemplateKey: EmailTemplateKeys.SecurityAlert,
                    TemplateParameters: new Dictionary<string, object?>
                    {
                        ["reason"] = reason,
                        ["country"] = "unknown",
                    }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to send {Reason} security email for account {AccountId}", reason, account.Id);
        }
    }

    private async Task<AuthSessionResponse> CompleteMfaSignInAsync(
        ApplicationUserAccount account,
        LearnerUser? authenticatedLearner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        account.LastLoginAt = now;
        account.UpdatedAt = now;

        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        // TOTP MFA already proved a second factor this attempt — a risk
        // step-up on top of it would be redundant (High-risk still blocks).
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken, riskStepUpSatisfied: true);
        await db.SaveChangesAsync(cancellationToken);
        await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthSignInSucceeded, cancellationToken: cancellationToken);
        return session;
    }

    private string ReadAuthenticatorSecretOrThrow(ApplicationUserAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ProtectedAuthenticatorSecret))
        {
            throw ApiException.Validation("authenticator_setup_required", "Begin authenticator setup before confirming MFA.");
        }

        try
        {
            return _authenticatorSecretProtector.Unprotect(account.ProtectedAuthenticatorSecret);
        }
        catch
        {
            throw ApiException.Validation("invalid_authenticator_secret", "The authenticator setup secret is invalid.");
        }
    }

    private string CreateMfaChallengeToken(string accountId)
    {
        var challenge = new MfaChallengeTicket(accountId, timeProvider.GetUtcNow().Add(_mfaChallengeLifetime));
        var serialized = JsonSerializer.Serialize(challenge);
        var protectedPayload = _mfaChallengeProtector.Protect(serialized);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private MfaChallengeTicket ReadMfaChallengeTokenOrThrow(string? challengeToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
        {
            throw ApiException.Validation("mfa_challenge_token_required", "MFA challenge token is required.");
        }

        try
        {
            var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(challengeToken));
            var serialized = _mfaChallengeProtector.Unprotect(protectedPayload);
            var challenge = JsonSerializer.Deserialize<MfaChallengeTicket>(serialized)
                            ?? throw new InvalidOperationException("Challenge token payload was empty.");
            if (challenge.ExpiresAt <= timeProvider.GetUtcNow())
            {
                throw ApiException.Validation("invalid_mfa_challenge", "The MFA challenge is invalid or expired.");
            }

            return challenge;
        }
        catch (ApiException)
        {
            throw;
        }
        catch
        {
            throw ApiException.Validation("invalid_mfa_challenge", "The MFA challenge is invalid or expired.");
        }
    }

    private string BuildOtpAuthUri(string email, string secretKey)
    {
        var escapedIssuer = Uri.EscapeDataString(_authenticatorIssuer);
        var escapedLabel = Uri.EscapeDataString($"{_authenticatorIssuer}:{email}");
        return $"otpauth://totp/{escapedLabel}?secret={secretKey}&issuer={escapedIssuer}&digits=6&period=30";
    }

    private static string BuildQrCodeDataUrl(string secretKey, string otpAuthUri)
    {
        var svg = $$"""
                    <svg xmlns="http://www.w3.org/2000/svg" width="420" height="180" viewBox="0 0 420 180">
                      <rect width="100%" height="100%" fill="white" />
                      <text x="16" y="28" font-size="16" font-family="monospace" fill="#111827">Authenticator setup</text>
                      <text x="16" y="56" font-size="14" font-family="monospace" fill="#111827">Secret: {{WebUtility.HtmlEncode(secretKey)}}</text>
                      <text x="16" y="84" font-size="10" font-family="monospace" fill="#374151">Paste the secret manually if your app cannot scan a QR code.</text>
                      <text x="16" y="112" font-size="10" font-family="monospace" fill="#374151">{{WebUtility.HtmlEncode(otpAuthUri)}}</text>
                    </svg>
                    """;

        return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
    }
}
