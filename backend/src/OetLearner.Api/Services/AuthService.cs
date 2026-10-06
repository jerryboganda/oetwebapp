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

public sealed partial class AuthService(
    LearnerDbContext db,
    IPasswordHasher<ApplicationUserAccount> passwordHasher,
    PasswordPolicyService passwordPolicy,
    AuthTokenService tokenService,
    EmailOtpService emailOtpService,
    ExternalAuthTicketService externalAuthTicketService,
    IOptions<ExternalAuthOptions> externalAuthOptions,
    IOptions<AuthTokenOptions> authTokenOptions,
    IOptions<AuthOptions> authOptions,
    IWebHostEnvironment environment,
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache memoryCache,
    ISecurityEventLogger securityEventLogger,
    ISessionRevocationService sessionRevocationService,
    ISignInRiskService signInRiskService,
    ITrustedDeviceService trustedDeviceService,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    TimeProvider timeProvider,
    // Optional (DI always supplies them; the unit-test harness constructs
    // AuthService without the full notification/email stack). Used only for
    // the best-effort §3.3 blocked-sign-in side channel.
    NotificationService? notifications = null,
    IEmailSender? emailSender = null,
    ILogger<AuthService>? logger = null)
{
    private const int AllowedAuthenticatorDriftWindows = 1;
    // H2 (security): cap MFA/recovery attempts per account inside the
    // challenge lifetime. Stored in the in-process memory cache; on multi-node
    // deployments this partitions per node which is still a strict tightening
    // over the previous unbounded behaviour.
    private const int MaxMfaAttempts = 5;
    // Authenticator step-up (Owner Agent Console unlock/step-up, hardened re-enrolment):
    // DB-backed failure window, replay-guard look-back, and the RFC 6238 step size the
    // AuthenticatorTotp verifier uses.
    private static readonly TimeSpan StepUpFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StepUpReplayLookback = TimeSpan.FromMinutes(10);
    private const int TotpTimeStepSeconds = 30;
    // Serializes "read last accepted time-step → verify → record" so two concurrent
    // requests carrying the same code cannot both pass the replay guard. Step-up is
    // owner-only and rare, so a single process-wide gate is cheap; the API runs one
    // active blue/green slot at a time.
    private static readonly SemaphoreSlim TotpStepUpGate = new(1, 1);
    private readonly bool _allowLocalDemoWithoutMfa = environment.IsDevelopment() && authOptions.Value.UseDevelopmentAuth;

    private readonly string _authenticatorIssuer = string.IsNullOrWhiteSpace(authTokenOptions.Value.AuthenticatorIssuer)
        ? throw new InvalidOperationException("AuthTokens:AuthenticatorIssuer must be configured.")
        : authTokenOptions.Value.AuthenticatorIssuer;
    private readonly TimeSpan _mfaChallengeLifetime = authTokenOptions.Value.OtpLifetime;
    private readonly IDataProtector _authenticatorSecretProtector = dataProtectionProvider.CreateProtector("AuthService.AuthenticatorSecret");
    private readonly IDataProtector _mfaChallengeProtector = dataProtectionProvider.CreateProtector("AuthService.MfaChallenge");
    private readonly IDataProtector _deviceChallengeProtector = dataProtectionProvider.CreateProtector("AuthService.DeviceChallenge");

    // M2 (security): a PBKDF2-hashed sentinel password used to normalise
    // sign-in response timing when the email does not map to any account.
    // The hasher runs the full KDF on Verify, so invoking it against a
    // pre-baked hash takes the same time as a real miss. We do this instead
    // of building a throwaway account so the hash format tracks the
    // configured identity profile.
    // IAM-01: built with the SAME PBKDF2-HMAC-SHA512/>=220k profile the real
    // hasher uses (PasswordHasherPolicy), so the sentinel cost cannot be used
    // as a distinguisher.
    private static readonly string _dummyPasswordHash =
        OetLearner.Api.Security.PasswordHasherPolicy.CreateHasher<ApplicationUserAccount>()
            .HashPassword(new ApplicationUserAccount(), "enumeration-guard-dummy-password");

    public async Task<AuthSessionResponse> RegisterLearnerAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            throw ApiException.Validation("invalid_registration_role", "Only learner self-registration is supported.");
        }

        // Password complexity + HIBP breach check is centralised in PasswordPolicyService.
        // Do not re-check here; we still need request.Password to be non-null for the below
        // reference, but validation (including the null/whitespace case) lives in the policy.
        if (request.Password is null)
        {
            throw ApiException.Validation("password_required", "Password is required.");
        }

        var externalRegistration = ResolveExternalRegistrationTicket(request.ExternalRegistrationToken);
        var email = externalRegistration is not null
            ? AuthEmailAddress.TrimAndValidateOrThrow(externalRegistration.Email)
            : AuthEmailAddress.TrimAndValidateOrThrow(request.Email);
        if (externalRegistration is not null
            && !string.IsNullOrWhiteSpace(request.Email)
            && !string.Equals(
                AuthEmailAddress.NormalizeOrThrow(request.Email),
                AuthEmailAddress.NormalizeOrThrow(externalRegistration.Email),
                StringComparison.Ordinal))
        {
            throw ApiException.Validation(
                "external_registration_email_mismatch",
                "The social sign-in email must match the registration email.");
        }

        // Registration purpose: the standard OET enrollment signup requires
        // the full healthcare-enrollment block; the free General-English
        // placement test ("placement") defers it — the learner supplies it
        // later via goals/onboarding the first time they enroll for OET.
        var isPlacementSignup = string.Equals(request.RegistrationPurpose, "placement", StringComparison.OrdinalIgnoreCase);
        if (request.RegistrationPurpose is not null && !isPlacementSignup)
        {
            throw ApiException.Validation(
                "invalid_registration_purpose",
                "Registration purpose must be omitted or 'placement'.");
        }

        var firstName = !string.IsNullOrWhiteSpace(request.FirstName)
            ? request.FirstName.Trim()
            : externalRegistration?.FirstName?.Trim();
        var lastName = !string.IsNullOrWhiteSpace(request.LastName)
            ? request.LastName.Trim()
            : externalRegistration?.LastName?.Trim();
        var mobileNumber = RequireTrimmed(request.MobileNumber, "mobile_number_required", "Mobile number is required.");

        // Enrollment block: required for the standard signup, deferred (null)
        // for placement signups.
        string? examTypeId;
        string? professionId;
        string? countryTarget;
        DateOnly? targetExamDate;
        if (isPlacementSignup)
        {
            examTypeId = null;
            professionId = null;
            countryTarget = null;
            targetExamDate = null;
        }
        else
        {
            examTypeId = RequireTrimmed(request.ExamTypeId, "exam_type_required", "Exam type is required.");
            professionId = RequireTrimmed(request.ProfessionId, "profession_required", "Profession is required.");
            countryTarget = TargetCountryOptions.Canonicalize(request.CountryTarget);

            if (request.TargetExamDate is null)
            {
                throw ApiException.Validation("target_exam_date_required", "Your target OET exam date is required.");
            }
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (request.TargetExamDate.Value < today)
            {
                throw ApiException.Validation("target_exam_date_in_past", "Your target OET exam date must be today or later.");
            }
            targetExamDate = request.TargetExamDate.Value;
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw ApiException.Validation("first_name_required", "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw ApiException.Validation("last_name_required", "Last name is required.");
        }

        if (request.AgreeToTerms is not true)
        {
            throw ApiException.Validation("terms_required", "Accept the terms to continue.");
        }

        if (request.AgreeToPrivacy is not true)
        {
            throw ApiException.Validation("privacy_required", "Accept the privacy policy to continue.");
        }

        // Catalog validation only applies to the standard (enrollment)
        // signup — placement signups defer the enrollment block entirely.
        (SignupExamTypeCatalog ExamType, SignupProfessionCatalog Profession)? signupSelection = isPlacementSignup
            ? null
            : await ValidateSignupSelectionAsync(
                examTypeId!,
                professionId!,
                countryTarget!,
                cancellationToken);
        // Enforce password policy now that we have the resolved email. Placed here (not
        // at the top of the method) so the policy can reject passwords that equal or
        // contain the local-part of the user's email address.
        await passwordPolicy.EnsurePasswordAcceptableAsync(request.Password, email, cancellationToken);

        var normalizedEmail = email.ToUpperInvariant();
        var existingAccount = await db.ApplicationUserAccounts
            .AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (existingAccount)
        {
            throw ApiException.Conflict("email_already_registered", "An account with that email already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var account = new ApplicationUserAccount
        {
            Id = $"auth_{Guid.NewGuid():N}",
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = ApplicationUserRoles.Learner,
            EmailVerifiedAt = externalRegistration is not null ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, request.Password);

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{firstName} {lastName}".Trim()
            : request.DisplayName.Trim();

        var learner = new LearnerUser
        {
            Id = $"learner_{Guid.NewGuid():N}",
            AuthAccountId = account.Id,
            Role = ApplicationUserRoles.Learner,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? BuildDefaultDisplayName(account.Email) : displayName,
            Email = account.Email,
            ActiveProfessionId = signupSelection?.Profession.Id,
            CreatedAt = now,
            LastActiveAt = now
        };

        var registrationProfile = new LearnerRegistrationProfile
        {
            Id = $"signup_{Guid.NewGuid():N}",
            ApplicationUserAccountId = account.Id,
            LearnerUserId = learner.Id,
            FirstName = firstName,
            LastName = lastName,
            ExamTypeId = signupSelection?.ExamType.Id,
            ProfessionId = signupSelection?.Profession.Id,
            SessionId = string.Empty,
            CountryTarget = countryTarget,
            TargetExamDate = targetExamDate,
            RegistrationPurpose = isPlacementSignup ? "placement" : null,
            MobileNumber = mobileNumber,
            AgreeToTerms = request.AgreeToTerms ?? false,
            AgreeToPrivacy = request.AgreeToPrivacy ?? false,
            MarketingOptIn = request.MarketingOptIn ?? false,
            UtmSource = request.UtmSource,
            UtmMedium = request.UtmMedium,
            UtmCampaign = request.UtmCampaign,
            UtmTerm = request.UtmTerm,
            UtmContent = request.UtmContent,
            ReferrerUrl = request.ReferrerUrl,
            LandingPath = request.LandingPath,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ApplicationUserAccounts.Add(account);
        db.Users.Add(learner);
        db.LearnerRegistrationProfiles.Add(registrationProfile);

        if (externalRegistration is not null)
        {
            db.ExternalIdentityLinks.Add(new ExternalIdentityLink
            {
                Id = Guid.NewGuid(),
                ApplicationUserAccountId = account.Id,
                Provider = externalRegistration.Provider,
                ProviderSubject = externalRegistration.ProviderSubject,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                CreatedAt = now,
                UpdatedAt = now,
                LastSignedInAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await CreateSessionAsync(account, learner.Id, learner.DisplayName, cancellationToken);
    }

    public async Task<SignupCatalogResponse> GetSignupCatalogAsync(CancellationToken cancellationToken = default)
    {
        var examTypes = await db.SignupExamTypeCatalog
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .Select(item => new SignupExamTypeResponse(
                item.Id,
                item.Label,
                item.Code,
                item.Description))
            .ToListAsync(cancellationToken);

        var professions = await db.SignupProfessionCatalog
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        return new SignupCatalogResponse(
            examTypes,
            professions.Select(item => new SignupProfessionResponse(
                item.Id,
                item.Label,
                DeserializeStringList(item.CountryTargetsJson).Count > 0
                    ? DeserializeStringList(item.CountryTargetsJson)
                    : TargetCountryOptions.All,
                DeserializeStringList(item.ExamTypeIdsJson),
                item.Description)).ToList(),
            ExternalAuthProviders.All
                .Where(provider => externalAuthOptions.Value.GetProvider(provider).Enabled)
                .ToArray(),
            TargetCountryOptions.All);
    }

    public async Task<AuthSessionResponse> SignInAsync(PasswordSignInRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw ApiException.Validation("invalid_credentials", "Email and password are required.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (account is null)
        {
            // M2 (security): perform an equivalent-cost password verification
            // so that the response time does not leak whether the email is
            // registered. The sentinel hash is a real PBKDF2 output so the
            // Verify path follows the same code/iteration count as a real
            // miss.
            _ = passwordHasher.VerifyHashedPassword(new ApplicationUserAccount(), _dummyPasswordHash, request.Password);
            throw ApiException.Validation("invalid_credentials", "Invalid email or password.");
        }

        var nowForLockout = timeProvider.GetUtcNow();

        // H1 (security): per-account soft lockout. The AuthBruteforce IP limiter
        // blocks volumetric attacks; this blocks credential-stuffing from a
        // rotating-IP attacker targeting one account. Return the same generic
        // error to avoid confirming the email exists to an outsider.
        if (account.LockoutUntil is { } locked && locked > nowForLockout)
        {
            _ = passwordHasher.VerifyHashedPassword(account, _dummyPasswordHash, request.Password);
            throw ApiException.Validation("invalid_credentials", "Invalid email or password.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            account.FailedSignInCount += 1;
            // Threshold: allow 5 free failures, then exponentially back off up
            // to a 60-minute cap. The counter is NOT reset by the lockout
            // window — it only resets on a successful sign-in — so repeated
            // failures grow the penalty.
            if (account.FailedSignInCount >= 5)
            {
                var over = account.FailedSignInCount - 5;
                var minutes = Math.Min(60, Math.Pow(2, Math.Min(over, 16)));
                account.LockoutUntil = nowForLockout.AddMinutes(minutes);
            }
            account.UpdatedAt = nowForLockout;
            await db.SaveChangesAsync(cancellationToken);
            await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthSignInFailed, cancellationToken: cancellationToken);
            throw ApiException.Validation("invalid_credentials", "Invalid email or password.");
        }

        // IAM-01 (OWASP): migrate any older/weaker password hash (v3 SHA-1/SHA-256
        // formats or lower iteration counts) to the configured PBKDF2-HMAC-SHA512
        // >=220k profile on successful sign-in, while the plaintext password is
        // in hand. This runs BEFORE the MFA/device-challenge throws so an
        // MFA-gated account still migrates on the password-verified attempt.
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
            // Persist immediately: sign-ins that continue into an MFA challenge
            // throw out of this request, and the rehash must not depend on the
            // later session-creation SaveChanges surviving.
            await db.SaveChangesAsync(cancellationToken);
        }

        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        var securityExempt = await ApplySecurityExemptionAsync(account, cancellationToken, authenticatedLearner?.Email);

        if (!securityExempt
            && (string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal)
                || string.Equals(account.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
            && account.EmailVerifiedAt is null)
        {
            throw ApiException.Forbidden("email_verification_required", "Email verification is required before privileged access is allowed.");
        }

        if (!securityExempt && account.AuthenticatorEnabledAt is not null)
        {
            throw new MfaChallengeRequiredException(account.Email, CreateMfaChallengeToken(account.Id));
        }

        var now = timeProvider.GetUtcNow();
        // H1: successful password verification clears lockout state.
        account.FailedSignInCount = 0;
        account.LockoutUntil = null;
        account.LastLoginAt = now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await securityEventLogger.TryLogAsync(account.Id, SecurityEventKinds.AuthSignInSucceeded, cancellationToken: cancellationToken);

        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        return await CreateSessionFromSubjectAsync(account, subject, cancellationToken, authenticatedLearner?.Email);
    }

    public async Task<AuthSessionResponse> CompleteDirectSignInAsync(
        string accountId,
        bool markEmailVerified,
        CancellationToken cancellationToken = default)
    {
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw ApiException.Forbidden("account_not_found", "This account is not available.");

        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        await ApplySecurityExemptionAsync(account, cancellationToken, authenticatedLearner?.Email);

        var now = timeProvider.GetUtcNow();
        if (markEmailVerified && account.EmailVerifiedAt is null)
        {
            account.EmailVerifiedAt = now;
        }

        account.LastLoginAt = now;
        account.UpdatedAt = now;

        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken, knownProfileEmail: authenticatedLearner?.Email);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<AuthSessionResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        // C1: prefer the HttpOnly cookie over the request body. New clients stop
        // sending the body entirely; legacy clients keep working until they're
        // updated. Do NOT throw when the body is missing as long as the cookie is
        // present — that's the migration target.
        var presented = ReadRefreshCookie();
        if (string.IsNullOrWhiteSpace(presented))
        {
            presented = request.RefreshToken;
        }
        if (string.IsNullOrWhiteSpace(presented))
        {
            throw ApiException.Validation("refresh_token_required", "Refresh token is required.");
        }

        var now = timeProvider.GetUtcNow();
        var tokenHash = tokenService.HashRefreshToken(presented);
        var refreshToken = await db.RefreshTokenRecords
            .Include(x => x.ApplicationUserAccount)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is null)
        {
            throw ApiException.Forbidden("invalid_refresh_token", "Refresh token is invalid or expired.");
        }

        // H3 (security): refresh-token reuse detection per OAuth 2 BCP §4.13.2.
        // If the presented token was already rotated (RevokedAt != null) or has
        // already expired, treat it as a potential compromise: revoke every
        // still-live sibling in its family so an attacker who stole the token
        // loses all downstream access. The client's real active session is
        // collateral damage, which is the desired outcome — it forces a fresh
        // sign-in and invalidates whichever copy the attacker has.
        if (refreshToken.RevokedAt is not null || refreshToken.ExpiresAt <= now)
        {
            if (refreshToken.RevokedAt is not null)
            {
                var familyId = refreshToken.FamilyId;
                var livingSiblings = await db.RefreshTokenRecords
                    .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var sibling in livingSiblings)
                {
                    sibling.RevokedAt = now;
                }
                if (livingSiblings.Count > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                await securityEventLogger.TryLogAsync(
                    refreshToken.ApplicationUserAccountId,
                    SecurityEventKinds.AuthRefreshReuseDetected,
                    sessionFamilyId: familyId,
                    cancellationToken: cancellationToken);
            }
            throw ApiException.Forbidden("invalid_refresh_token", "Refresh token is invalid or expired.");
        }

        // Security spec §3.2: a copied refresh cookie presented from a
        // different device than the one this session was issued to is a
        // compromise signal — burn the whole family, same as reuse
        // detection above. Only checked when BOTH sides have a device id
        // (old clients sending no X-OET-Device-Id header, or sessions
        // created before this column existed, skip silently).
        var presentedDeviceId = await ResolveDeviceIdForRequestAsync(
            refreshToken.ApplicationUserAccountId,
            httpContextAccessor.HttpContext?.Request.Headers["X-OET-Device-Id"].ToString(),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(presentedDeviceId)
            && !string.IsNullOrWhiteSpace(refreshToken.DeviceId)
            && !string.Equals(presentedDeviceId, refreshToken.DeviceId, StringComparison.Ordinal))
        {
            var mismatchedFamilyId = refreshToken.FamilyId;
            var familyTokens = await db.RefreshTokenRecords
                .Where(t => t.FamilyId == mismatchedFamilyId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in familyTokens)
            {
                token.RevokedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken);
            await securityEventLogger.TryLogAsync(
                refreshToken.ApplicationUserAccountId,
                SecurityEventKinds.AuthRefreshDeviceMismatch,
                sessionFamilyId: mismatchedFamilyId,
                deviceId: presentedDeviceId,
                cancellationToken: cancellationToken);
            throw ApiException.Forbidden("invalid_refresh_token", "Refresh token is invalid or expired.");
        }

        refreshToken.LastUsedAt = now;
        refreshToken.RevokedAt = now;

        var account = refreshToken.ApplicationUserAccount;
        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken, refreshToken.FamilyId);

        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<OtpChallengeResponse> SendEmailVerificationOtpAsync(SendEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Purpose, EmailOtpService.EmailVerificationPurpose, StringComparison.Ordinal))
        {
            throw ApiException.Validation("unsupported_otp_purpose", "Only email verification OTP requests are currently supported.");
        }

        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(request.Email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (account is not null)
        {
            await ApplySecurityExemptionAsync(account, cancellationToken);
            if (account.EmailVerifiedAt is not null)
            {
                await db.SaveChangesAsync(cancellationToken);
                var now = timeProvider.GetUtcNow();
                return new OtpChallengeResponse(
                    Guid.NewGuid().ToString(),
                    EmailOtpService.EmailVerificationPurpose,
                    "email",
                    AuthEmailAddress.Mask(account.Email),
                    now.Add(authTokenOptions.Value.OtpLifetime),
                    60);
            }
        }

        return await emailOtpService.RequestEmailVerificationOtpAsync(
            request.Email, cancellationToken, request.ForceNew);
    }

    public async Task<CurrentUserResponse> VerifyEmailOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Purpose, EmailOtpService.EmailVerificationPurpose, StringComparison.Ordinal))
        {
            throw ApiException.Validation("unsupported_otp_purpose", "Only email verification OTP requests are currently supported.");
        }

        var account = await emailOtpService.VerifyEmailVerificationOtpAsync(request.Email, request.Code, cancellationToken);
        var subject = await ResolveSubjectAsync(account, cancellationToken);
        return BuildCurrentUserResponse(subject);
    }

    public async Task<OtpChallengeResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var response = await emailOtpService.RequestPasswordResetOtpAsync(
            request.Email, cancellationToken, request.RecaptchaToken);
        await ApplyOtpRateLimitItemsAsync(request.Email, response.DeliveryChannel, cancellationToken);
        return response;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        // Run password-policy validation BEFORE consuming the OTP. Verifying the OTP
        // marks the challenge as VerifiedAt = now (single-use); if we then rejected
        // the password as too short / breached / similar-to-email, the user would
        // be left with a burned OTP and "invalid_reset_token" on retry. Validating
        // first means a bad password is a recoverable error and the OTP is only
        // consumed when the password is acceptable. The policy's email-similarity
        // check uses the request email (it's just UX, not a security boundary —
        // the OTP is what proves email ownership, and that still runs next).
        if (request.NewPassword is null)
        {
            throw ApiException.Validation("new_password_required", "A new password is required.");
        }

        await passwordPolicy.EnsurePasswordAcceptableAsync(request.NewPassword, request.Email, cancellationToken);

        var account = await emailOtpService.VerifyPasswordResetOtpAsync(request.Email, request.ResetToken, cancellationToken);

        var now = timeProvider.GetUtcNow();
        account.PasswordHash = passwordHasher.HashPassword(account, request.NewPassword);
        account.UpdatedAt = now;

        var activeRefreshTokens = await db.RefreshTokenRecords
            .Where(x => x.ApplicationUserAccountId == account.Id && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Refresh cookie helpers — C1 (HttpOnly refresh cookie migration)
    //
    //  Web clients receive refresh tokens only in this cookie. Native/mobile
    //  shells can still request a body refresh token by sending the explicit
    //  client-platform header because they store it outside web storage.
    //
    //  Cookie invariants:
    //   * HttpOnly  — hard block on JS access, which is the entire point
    //   * Secure    — refuse to send on http (except localhost dev)
    //   * SameSite  — None in prod because the SPA at app.oetwithdrhesham.co.uk
    //                  calls the API at api.oetwithdrhesham.co.uk (cross-origin
    //                  even though same-site registrable domain). SameSite=None
    //                  + Secure is the documented pattern for SPA/API splits.
    //                  CSRF is mitigated by (a) the double-submit cookie check
    //                  on the /api/backend/* Next.js proxy, and (b) the
    //                  AuthBruteforce rate limiter on /v1/auth/refresh itself.
    //                  Lax is used when Secure can't be set (http localhost dev)
    //                  since browsers reject SameSite=None without Secure.
    //   * Path=/       - allows same-site proxied web refresh/sign-out calls
    //                  to carry the cookie across deployment topologies.
    // ═══════════════════════════════════════════════════════════════════════
    private const string RefreshCookieName = "oet_rt";
    private const string CsrfCookieName = "oet_csrf";
    // HttpOnly continuity for the browser/device identity. The client-generated
    // header remains the primary identity signal, but this cookie survives the
    // privacy-oriented storage resets that otherwise mint a new id on every
    // launch and eventually trigger the device-change cooldown.
    private const string DeviceBindingCookieName = "oet_device_binding";
    private const string ClientPlatformHeader = "X-OET-Client-Platform";
    private const string RefreshCookiePath = "/";
    private static readonly TimeSpan DeviceBindingCookieLifetime = TimeSpan.FromDays(365);

    private async Task<        AuthenticatedSessionSubject> ResolveSubjectAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken,
        LearnerUser? authenticatedLearner = null)
    {
        await ApplySecurityExemptionAsync(account, cancellationToken, authenticatedLearner?.Email);

        if (string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            var learner = authenticatedLearner
                ?? await db.Users
                    .AsNoTracking()
                    .SingleAsync(x => x.AuthAccountId == account.Id, cancellationToken);

            string? professionLabel = null;
            if (!string.IsNullOrWhiteSpace(learner.ActiveProfessionId))
            {
                professionLabel = await db.Professions
                    .AsNoTracking()
                    .Where(p => p.Id == learner.ActiveProfessionId)
                    .Select(p => p.Label)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return await BuildSubjectAsync(
                account,
                learner.Id,
                learner.DisplayName,
                cancellationToken,
                activeProfessionId: learner.ActiveProfessionId,
                activeProfessionLabel: professionLabel,
                avatarUrl: learner.AvatarUrl);
        }

        if (string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal))
        {
            var expert = await db.ExpertUsers
                .AsNoTracking()
                .SingleAsync(x => x.AuthAccountId == account.Id, cancellationToken);
            return await BuildSubjectAsync(account, expert.Id, expert.DisplayName, cancellationToken);
        }

        // Admin: load granular permissions
        string[]? adminPerms = null;
        if (string.Equals(account.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
        {
            adminPerms = await db.AdminPermissionGrants
                .AsNoTracking()
                .Where(g => g.AdminUserId == account.Id)
                .Select(g => g.Permission)
                .ToArrayAsync(cancellationToken);
        }

        return await BuildSubjectAsync(account, account.Id, BuildDefaultDisplayName(account.Email), cancellationToken, adminPerms);
    }

    private async Task<ApplicationUserAccount> ResolveAccountFromPrincipalAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var candidateIds = principal.Claims
            .Where(claim =>
                claim.Type == AuthTokenService.AuthAccountIdClaimType
                || claim.Type == ClaimTypes.NameIdentifier
                || claim.Type == "nameid"
                || claim.Type == JwtRegisteredClaimNames.Sub)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var candidateId in candidateIds)
        {
            var directMatch = await db.ApplicationUserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == candidateId, cancellationToken);

            if (directMatch is not null)
            {
                return directMatch;
            }
        }

        var email = FindFirstValue(principal, ClaimTypes.Email, JwtRegisteredClaimNames.Email, "email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = NormalizeEmail(email);
            var emailMatch = await db.ApplicationUserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

            if (emailMatch is not null)
            {
                return emailMatch;
            }
        }

        var learnerAuthAccountId = await db.Users
            .AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id) && x.AuthAccountId != null)
            .Select(x => x.AuthAccountId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(learnerAuthAccountId))
        {
            return await db.ApplicationUserAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == learnerAuthAccountId, cancellationToken);
        }

        var expertAuthAccountId = await db.ExpertUsers
            .AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id) && x.AuthAccountId != null)
            .Select(x => x.AuthAccountId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(expertAuthAccountId))
        {
            return await db.ApplicationUserAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == expertAuthAccountId, cancellationToken);
        }

        var routeUserId = candidateIds.FirstOrDefault()
            ?? throw new InvalidOperationException("Authenticated user id claim is required.");

        return await db.ApplicationUserAccounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == routeUserId, cancellationToken);
    }

    private async Task<(ApplicationUserAccount Account, LearnerUser? AuthenticatedLearner)> ResolveTrackedAccountFromPrincipalAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountFromPrincipalAsync(principal, cancellationToken);
        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        var trackedAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == account.Id, cancellationToken);
        return (trackedAccount, authenticatedLearner);
    }

    private async Task<(ApplicationUserAccount Account, LearnerUser? AuthenticatedLearner)> ResolveTrackedMfaAccountAsync(
        string email,
        string? challengeToken,
        CancellationToken cancellationToken)
    {
        var challenge = ReadMfaChallengeTokenOrThrow(challengeToken);
        var normalizedEmail = AuthEmailAddress.NormalizeOrThrow(email);
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(
                x => x.Id == challenge.AccountId && x.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (account is null)
        {
            throw ApiException.Validation("invalid_mfa_challenge", "The MFA challenge is invalid or expired.");
        }

        if (account.AuthenticatorEnabledAt is null)
        {
            throw ApiException.Forbidden("mfa_not_configured", "Authenticator-based MFA is not configured for this account.");
        }

        var authenticatedLearner = await EnsureAccountCanAuthenticateAsync(account, cancellationToken);

        return (account, authenticatedLearner);
    }

    private ExternalRegistrationTicket? ResolveExternalRegistrationTicket(string? externalRegistrationToken)
    {
        if (string.IsNullOrWhiteSpace(externalRegistrationToken))
        {
            return null;
        }

        return externalAuthTicketService.ReadRegistrationToken(externalRegistrationToken);
    }

    private async Task<(SignupExamTypeCatalog ExamType, SignupProfessionCatalog Profession)> ValidateSignupSelectionAsync(
        string examTypeId,
        string professionId,
        string countryTarget,
        CancellationToken cancellationToken)
    {
        var examType = await db.SignupExamTypeCatalog
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == examTypeId && item.IsActive, cancellationToken)
            ?? throw ApiException.Validation("exam_type_invalid", "Select a valid exam type.");

        var profession = await db.SignupProfessionCatalog
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == professionId && item.IsActive, cancellationToken)
            ?? throw ApiException.Validation("profession_invalid", "Select a valid profession.");

        var professionExamTypes = DeserializeStringList(profession.ExamTypeIdsJson);
        if (!professionExamTypes.Contains(examType.Id, StringComparer.Ordinal))
        {
            throw ApiException.Validation("profession_exam_mismatch", "The selected profession is not available for that exam.");
        }

        if (!TargetCountryOptions.Contains(countryTarget))
        {
            throw ApiException.Validation("country_target_invalid", "Select a valid target country.");
        }

        var professionCountryTargets = DeserializeStringList(profession.CountryTargetsJson);
        if (professionCountryTargets.Count > 0
            && !professionCountryTargets.Contains(countryTarget, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("profession_country_mismatch", "The selected target country is not available for that profession.");
        }

        return (examType, profession);
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
        => JsonSupport.Deserialize(json, Array.Empty<string>());

    private static string RequireTrimmed(string? value, string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiException.Validation(errorCode, message);
        }

        return value.Trim();
    }

    private async Task<AuthenticatedSessionSubject> BuildSubjectAsync(
        ApplicationUserAccount account,
        string userId,
        string displayName,
        CancellationToken cancellationToken,
        string[]? adminPermissions = null,
        string? activeProfessionId = null,
        string? activeProfessionLabel = null,
        string? avatarUrl = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requiresMfa = false;

        if (_allowLocalDemoWithoutMfa)
        {
            requiresMfa = false;
        }

        // Resolve subscription tier so that the vocabulary premium gate works
        // correctly without a per-request DB round-trip. The claim is stamped
        // into the JWT at session creation time; it reflects the state at login.
        string? subscriptionTier = null;
        if (account.Role == "learner")
        {
            var hasActiveSub = await HasActiveLearnerSubscriptionAsync(userId, cancellationToken);
            if (hasActiveSub) subscriptionTier = "paid";
        }

        return new AuthenticatedSessionSubject(
            userId,
            account.Id,
            account.Email,
            account.Role,
            displayName,
            account.EmailVerifiedAt is not null,
            account.AuthenticatorEnabledAt is not null,
            account.EmailVerifiedAt is null,
            requiresMfa,
            account.EmailVerifiedAt,
            account.AuthenticatorEnabledAt,
            AdminPermissions: adminPermissions,
            ActiveProfessionId: activeProfessionId,
            ActiveProfessionLabel: activeProfessionLabel,
            SubscriptionTier: subscriptionTier,
            AvatarUrl: avatarUrl);
    }

    private async Task<bool> HasActiveLearnerSubscriptionAsync(string userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var activeStatuses = new[] { Domain.SubscriptionStatus.Active, Domain.SubscriptionStatus.Trial };

        if (!db.Database.IsSqlite())
        {
            return await db.Subscriptions
                .AsNoTracking()
                .AnyAsync(s => s.UserId == userId
                    && activeStatuses.Contains(s.Status)
                    && (s.ExpiresAt == null || s.ExpiresAt > now),
                    cancellationToken);
        }

        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId && activeStatuses.Contains(s.Status))
            .Select(s => new { s.Status, s.ExpiresAt })
            .ToListAsync(cancellationToken);

        return subscriptions.Any(s => s.ExpiresAt is null || s.ExpiresAt > now);
    }

    private static CurrentUserResponse BuildCurrentUserResponse(AuthenticatedSessionSubject subject)
        => new(
            subject.UserId,
            subject.Email,
            subject.Role,
            subject.DisplayName,
            subject.IsEmailVerified,
            subject.IsAuthenticatorEnabled,
            subject.RequiresEmailVerification,
            subject.RequiresMfa,
            subject.EmailVerifiedAt,
            subject.AuthenticatorEnabledAt,
            subject.AdminPermissions,
            subject.ActiveProfessionId,
            subject.ActiveProfessionLabel,
            subject.AvatarUrl,
            // In-memory check against the hard-coded list; true or null (null = omitted from the JSON).
            WritingUnrestricted: OetLearner.Api.Services.Writing.WritingUnrestrictedAccounts.IsUnrestricted(subject.Email) ? true : (bool?)null);

    private async Task<LearnerUser?> EnsureAccountCanAuthenticateAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (account.DeletedAt is not null)
        {
            throw ApiException.Forbidden("account_deleted", "This account has been deleted.");
        }

        if (string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            var learner = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.AuthAccountId == account.Id, cancellationToken);

            if (learner is null || !string.Equals(learner.AccountStatus, "active", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Forbidden("account_suspended", "This account is suspended.");
            }

            // Admin-set master access expiry (see LearnerUser.AccessExpiresAt). When
            // elapsed, refuse login AND token refresh (this gate runs on both) with a
            // 403 + snake_case code the sign-in form maps to the "renew" popup. A 403
            // is required: the client remaps a 400 auth error to invalid_credentials.
            if (learner.AccessExpiresAt is { } accessExpiry && accessExpiry <= timeProvider.GetUtcNow())
            {
                throw ApiException.Forbidden(
                    "subscription_expired",
                    "Your Subscription has expired. Please Renew Your subscription");
            }

            return learner;
        }

        if (string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal))
        {
            var expert = await db.ExpertUsers
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.AuthAccountId == account.Id, cancellationToken);

            if (expert is null || !expert.IsActive)
            {
                throw ApiException.Forbidden("account_suspended", "This account is suspended.");
            }
        }

        if (string.Equals(account.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
        {
            // Security spec §4.4: admin accounts must be revocable too. AdminUser.Id
            // shares the ApplicationUserAccount primary key (see AdminEndpoints.cs
            // FindAsync([userId]) call sites). A missing row is treated as active —
            // AdminUser rows are only created for permission-bearing admins, and the
            // "system_admin" bootstrap account may predate this table.
            var admin = await db.AdminUsers
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == account.Id, cancellationToken);

            if (admin is not null && !admin.IsActive)
            {
                throw ApiException.Forbidden("account_suspended", "This account is suspended.");
            }
        }

        return null;
    }

    private static string? FindFirstValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static string BuildDefaultDisplayName(string email)
        => email.Split('@', 2)[0];

    private sealed record MfaChallengeTicket(string AccountId, DateTimeOffset ExpiresAt);

    private sealed record DeviceChallengeTicket(string AccountId, string DeviceId, DateTimeOffset ExpiresAt, string Mode = "otp_required", Guid? SelectedTrustedDeviceId = null);
}

/// <summary>Result of a successful authenticator step-up (the accepted RFC 6238 time-step is now burned).</summary>
public sealed record AuthenticatorStepUpResult(string AccountId, long TimeStep, DateTimeOffset VerifiedAt);
