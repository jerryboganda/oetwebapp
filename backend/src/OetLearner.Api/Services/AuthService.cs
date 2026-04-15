using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class AuthService(
    LearnerDbContext db,
    IPasswordHasher<ApplicationUserAccount> passwordHasher,
    AuthTokenService tokenService,
    EmailOtpService emailOtpService,
    ExternalAuthTicketService externalAuthTicketService,
    IOptions<ExternalAuthOptions> externalAuthOptions,
    IOptions<AuthTokenOptions> authTokenOptions,
    IOptions<AuthOptions> authOptions,
    IWebHostEnvironment environment,
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    private const int AllowedAuthenticatorDriftWindows = 1;
    private readonly bool _allowLocalDemoWithoutMfa = environment.IsDevelopment() && authOptions.Value.UseDevelopmentAuth;

    private readonly string _authenticatorIssuer = string.IsNullOrWhiteSpace(authTokenOptions.Value.AuthenticatorIssuer)
        ? throw new InvalidOperationException("AuthTokens:AuthenticatorIssuer must be configured.")
        : authTokenOptions.Value.AuthenticatorIssuer;
    private readonly TimeSpan _mfaChallengeLifetime = authTokenOptions.Value.OtpLifetime;
    private readonly IDataProtector _authenticatorSecretProtector = dataProtectionProvider.CreateProtector("AuthService.AuthenticatorSecret");
    private readonly IDataProtector _mfaChallengeProtector = dataProtectionProvider.CreateProtector("AuthService.MfaChallenge");

    public async Task<AuthSessionResponse> RegisterLearnerAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            throw ApiException.Validation("invalid_registration_role", "Only learner self-registration is supported.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
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

        var firstName = !string.IsNullOrWhiteSpace(request.FirstName)
            ? request.FirstName.Trim()
            : externalRegistration?.FirstName?.Trim();
        var lastName = !string.IsNullOrWhiteSpace(request.LastName)
            ? request.LastName.Trim()
            : externalRegistration?.LastName?.Trim();
        var mobileNumber = RequireTrimmed(request.MobileNumber, "mobile_number_required", "Mobile number is required.");
        var examTypeId = RequireTrimmed(request.ExamTypeId, "exam_type_required", "Exam type is required.");
        var professionId = RequireTrimmed(request.ProfessionId, "profession_required", "Profession is required.");
        var sessionId = RequireTrimmed(request.SessionId, "session_required", "Session is required.");
        var countryTarget = RequireTrimmed(request.CountryTarget, "country_target_required", "Target country is required.");

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

        var signupSelection = await ValidateSignupSelectionAsync(
            examTypeId,
            professionId,
            sessionId,
            countryTarget,
            cancellationToken);
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
            ActiveProfessionId = signupSelection.Profession.Id,
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
            ExamTypeId = signupSelection.ExamType.Id,
            ProfessionId = signupSelection.Profession.Id,
            SessionId = signupSelection.Session.Id,
            CountryTarget = countryTarget,
            MobileNumber = mobileNumber,
            AgreeToTerms = request.AgreeToTerms ?? false,
            AgreeToPrivacy = request.AgreeToPrivacy ?? false,
            MarketingOptIn = request.MarketingOptIn ?? false,
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

        var sessions = await db.SignupSessionCatalog
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        var billingPlans = await db.BillingPlans
            .AsNoTracking()
            .Where(plan => plan.Status == BillingPlanStatus.Active && plan.IsVisible)
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Price)
            .ThenBy(plan => plan.Name)
            .Select(plan => new SignupBillingPlanResponse(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.Description,
                plan.Price,
                plan.Currency,
                plan.Interval,
                plan.IncludedCredits,
                plan.DisplayOrder,
                plan.IsVisible,
                plan.IsRenewable,
                plan.TrialDays,
                DeserializeStringList(plan.IncludedSubtestsJson),
                JsonSupport.Deserialize<Dictionary<string, object?>>(plan.EntitlementsJson, new Dictionary<string, object?>())))
            .ToListAsync(cancellationToken);

        return new SignupCatalogResponse(
            examTypes,
            professions.Select(item => new SignupProfessionResponse(
                item.Id,
                item.Label,
                DeserializeStringList(item.CountryTargetsJson),
                DeserializeStringList(item.ExamTypeIdsJson),
                item.Description)).ToList(),
            sessions.Select(item => new SignupSessionResponse(
                item.Id,
                item.Name,
                item.ExamTypeId,
                DeserializeStringList(item.ProfessionIdsJson),
                item.PriceLabel,
                item.StartDate,
                item.EndDate,
                item.DeliveryMode,
                item.Capacity,
                item.SeatsRemaining)).ToList(),
            billingPlans,
            ExternalAuthProviders.All
                .Where(provider => externalAuthOptions.Value.GetProvider(provider).Enabled)
                .ToArray());
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
            throw ApiException.Validation("invalid_credentials", "Invalid email or password.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw ApiException.Validation("invalid_credentials", "Invalid email or password.");
        }

        await EnsureAccountCanAuthenticateAsync(account, cancellationToken);

        if ((string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal)
                || string.Equals(account.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
            && account.EmailVerifiedAt is null)
        {
            throw ApiException.Forbidden("email_verification_required", "Email verification is required before privileged access is allowed.");
        }

        if (account.AuthenticatorEnabledAt is not null)
        {
            throw new MfaChallengeRequiredException(account.Email, CreateMfaChallengeToken(account.Id));
        }

        var now = timeProvider.GetUtcNow();
        account.LastLoginAt = now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var subject = await ResolveSubjectAsync(account, cancellationToken);
        return await CreateSessionFromSubjectAsync(account, subject, cancellationToken);
    }

    public async Task<AuthSessionResponse> CompleteDirectSignInAsync(
        string accountId,
        bool markEmailVerified,
        CancellationToken cancellationToken = default)
    {
        var account = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw ApiException.Forbidden("account_not_found", "This account is not available.");

        await EnsureAccountCanAuthenticateAsync(account, cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (markEmailVerified && account.EmailVerifiedAt is null)
        {
            account.EmailVerifiedAt = now;
        }

        account.LastLoginAt = now;
        account.UpdatedAt = now;

        var subject = await ResolveSubjectAsync(account, cancellationToken);
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<AuthSessionResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw ApiException.Validation("refresh_token_required", "Refresh token is required.");
        }

        var now = timeProvider.GetUtcNow();
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var refreshToken = await db.RefreshTokenRecords
            .Include(x => x.ApplicationUserAccount)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is null || refreshToken.RevokedAt is not null || refreshToken.ExpiresAt <= now)
        {
            throw ApiException.Forbidden("invalid_refresh_token", "Refresh token is invalid or expired.");
        }

        refreshToken.LastUsedAt = now;
        refreshToken.RevokedAt = now;

        var account = refreshToken.ApplicationUserAccount;
        await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        var subject = await ResolveSubjectAsync(account, cancellationToken);
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<OtpChallengeResponse> SendEmailVerificationOtpAsync(SendEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Purpose, EmailOtpService.EmailVerificationPurpose, StringComparison.Ordinal))
        {
            throw ApiException.Validation("unsupported_otp_purpose", "Only email verification OTP requests are currently supported.");
        }

        return await emailOtpService.RequestEmailVerificationOtpAsync(request.Email, cancellationToken);
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
        return await emailOtpService.RequestPasswordResetOtpAsync(request.Email, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw ApiException.Validation("new_password_required", "A new password is required.");
        }

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

    public async Task<AuthenticatorSetupResponse> BeginAuthenticatorSetupAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
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
        await db.SaveChangesAsync(cancellationToken);

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

        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        if (!AuthenticatorTotp.VerifyCode(secretKey, request.Code, timeProvider.GetUtcNow(), AllowedAuthenticatorDriftWindows))
        {
            throw ApiException.Validation("invalid_authenticator_code", "The authenticator code is invalid.");
        }

        var now = timeProvider.GetUtcNow();
        account.AuthenticatorEnabledAt = now;
        account.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var subject = await ResolveSubjectAsync(account, cancellationToken);
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

        var account = await ResolveTrackedMfaAccountAsync(request.Email, request.ChallengeToken, cancellationToken);
        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        if (!AuthenticatorTotp.VerifyCode(secretKey, request.Code, timeProvider.GetUtcNow(), AllowedAuthenticatorDriftWindows))
        {
            throw ApiException.Validation("invalid_authenticator_code", "The authenticator code is invalid.");
        }

        return await CompleteMfaSignInAsync(account, cancellationToken);
    }

    public async Task<AuthSessionResponse> CompleteRecoveryChallengeAsync(
        MfaChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            throw ApiException.Validation("mfa_recovery_code_required", "Recovery code is required.");
        }

        var account = await ResolveTrackedMfaAccountAsync(request.Email, request.ChallengeToken, cancellationToken);
        var codeHash = AuthenticatorTotp.HashRecoveryCode(request.RecoveryCode);
        var recoveryCode = await db.MfaRecoveryCodes
            .SingleOrDefaultAsync(
                x => x.ApplicationUserAccountId == account.Id
                    && x.RedeemedAt == null
                    && x.CodeHash == codeHash,
                cancellationToken);

        if (recoveryCode is null)
        {
            throw ApiException.Validation("invalid_mfa_recovery_code", "The recovery code is invalid or already used.");
        }

        recoveryCode.RedeemedAt = timeProvider.GetUtcNow();
        return await CompleteMfaSignInAsync(account, cancellationToken);
    }

    public async Task SignOutAsync(SignOutRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw ApiException.Validation("refresh_token_required", "Refresh token is required.");
        }

        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var refreshToken = await db.RefreshTokenRecords
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is null || refreshToken.RevokedAt is not null)
        {
            return;
        }

        refreshToken.RevokedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var subject = await ResolveSubjectAsync(account, cancellationToken);

        return BuildCurrentUserResponse(subject);
    }

    public async Task DeleteAccountAsync(ClaimsPrincipal principal, DeleteAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw ApiException.Validation("password_required", "Password is required to confirm account deletion.");
        }

        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);

        var verificationResult = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            throw ApiException.Unauthorized("invalid_password", "The password provided is incorrect.");
        }

        var learner = await db.Users.SingleOrDefaultAsync(x => x.AuthAccountId == account.Id, cancellationToken);
        if (learner is null)
        {
            throw ApiException.NotFound("learner_not_found", "No learner profile found for this account.");
        }

        var now = timeProvider.GetUtcNow();

        try
        {
            account.DeletedAt = now;
            account.UpdatedAt = now;
            learner.AccountStatus = "deleted";

            var activeRefreshTokens = await db.RefreshTokenRecords
                .Where(t => t.ApplicationUserAccountId == account.Id && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in activeRefreshTokens)
            {
                token.RevokedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("account_update_conflict", "The account was modified concurrently. Please retry.");
        }
    }

    public async Task<ActiveSessionListResponse> GetActiveSessionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var currentSessionId = Guid.TryParse(
            principal.FindFirstValue(AuthTokenService.SessionIdClaimType), out var sid)
            ? sid
            : (Guid?)null;

        var tokens = await db.RefreshTokenRecords
            .AsNoTracking()
            .Where(t => t.ApplicationUserAccountId == account.Id && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
            .ToListAsync(cancellationToken);

        var sessions = tokens.Select(t => new ActiveSessionResponse(
            t.Id,
            t.DeviceInfo,
            t.IpAddress,
            t.LastUsedAt,
            t.CreatedAt,
            t.Id == currentSessionId
        )).ToList();

        return new ActiveSessionListResponse(sessions);
    }

    public async Task RevokeSessionAsync(ClaimsPrincipal principal, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);

        var currentSessionId = Guid.TryParse(
            principal.FindFirstValue(AuthTokenService.SessionIdClaimType), out var sid)
            ? sid
            : (Guid?)null;

        if (sessionId == currentSessionId)
        {
            throw ApiException.Validation("cannot_revoke_current_session", "Cannot revoke the current session.");
        }

        var token = await db.RefreshTokenRecords
            .SingleOrDefaultAsync(t => t.Id == sessionId && t.ApplicationUserAccountId == account.Id && t.RevokedAt == null, cancellationToken);

        if (token is null)
        {
            throw ApiException.NotFound("session_not_found", "Session not found or already revoked.");
        }

        token.RevokedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeAllOtherSessionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var account = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var currentSessionId = Guid.TryParse(
            principal.FindFirstValue(AuthTokenService.SessionIdClaimType), out var sid)
            ? sid
            : (Guid?)null;

        var tokens = await db.RefreshTokenRecords
            .Where(t => t.ApplicationUserAccountId == account.Id && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var token in tokens)
        {
            if (token.Id == currentSessionId) continue;
            token.RevokedAt = now;
            count++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<AuthSessionResponse> CreateSessionAsync(
        ApplicationUserAccount account,
        string userId,
        string displayName,
        CancellationToken cancellationToken)
    {
        var subject = await BuildSubjectAsync(account, userId, displayName, cancellationToken);
        return await CreateSessionFromSubjectAsync(account, subject, cancellationToken);
    }

    private async Task<AuthSessionResponse> CreateSessionFromSubjectAsync(
        ApplicationUserAccount account,
        AuthenticatedSessionSubject subject,
        CancellationToken cancellationToken)
    {
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task<AuthSessionResponse> CreateSessionCoreAsync(
        ApplicationUserAccount account,
        AuthenticatedSessionSubject subject,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var issuedSession = tokenService.IssueSession(subject, sessionId);

        string? deviceInfo = null;
        string? ipAddress = null;
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            deviceInfo = httpContext.Request.Headers.UserAgent.ToString();
            if (deviceInfo?.Length > 512) deviceInfo = deviceInfo[..512];
            ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            if (ipAddress?.Length > 64) ipAddress = ipAddress[..64];
        }

        db.RefreshTokenRecords.Add(new RefreshTokenRecord
        {
            Id = sessionId,
            ApplicationUserAccountId = account.Id,
            TokenHash = issuedSession.RefreshTokenHash,
            ExpiresAt = issuedSession.RefreshTokenExpiresAt,
            CreatedAt = timeProvider.GetUtcNow(),
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        });

        await Task.CompletedTask;
        return new AuthSessionResponse(
            issuedSession.AccessToken,
            issuedSession.RefreshToken,
            issuedSession.AccessTokenExpiresAt,
            issuedSession.RefreshTokenExpiresAt,
            BuildCurrentUserResponse(subject));
    }

    private async Task<AuthenticatedSessionSubject> ResolveSubjectAsync(ApplicationUserAccount account, CancellationToken cancellationToken)
    {
        if (string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            var learner = await db.Users
                .AsNoTracking()
                .SingleAsync(x => x.AuthAccountId == account.Id, cancellationToken);
            return await BuildSubjectAsync(account, learner.Id, learner.DisplayName, cancellationToken);
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

    private async Task<ApplicationUserAccount> ResolveTrackedAccountFromPrincipalAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountFromPrincipalAsync(principal, cancellationToken);
        await EnsureAccountCanAuthenticateAsync(account, cancellationToken);
        return await db.ApplicationUserAccounts.SingleAsync(x => x.Id == account.Id, cancellationToken);
    }

    private async Task<ApplicationUserAccount> ResolveTrackedMfaAccountAsync(
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

        await EnsureAccountCanAuthenticateAsync(account, cancellationToken);

        return account;
    }

    private async Task<AuthSessionResponse> CompleteMfaSignInAsync(
        ApplicationUserAccount account,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        account.LastLoginAt = now;
        account.UpdatedAt = now;

        var subject = await ResolveSubjectAsync(account, cancellationToken);
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
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

    private ExternalRegistrationTicket? ResolveExternalRegistrationTicket(string? externalRegistrationToken)
    {
        if (string.IsNullOrWhiteSpace(externalRegistrationToken))
        {
            return null;
        }

        return externalAuthTicketService.ReadRegistrationToken(externalRegistrationToken);
    }

    private async Task<(SignupExamTypeCatalog ExamType, SignupProfessionCatalog Profession, SignupSessionCatalog Session)> ValidateSignupSelectionAsync(
        string examTypeId,
        string professionId,
        string sessionId,
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

        var session = await db.SignupSessionCatalog
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sessionId && item.IsActive, cancellationToken)
            ?? throw ApiException.Validation("session_invalid", "Select a valid session.");

        var professionExamTypes = DeserializeStringList(profession.ExamTypeIdsJson);
        if (!professionExamTypes.Contains(examType.Id, StringComparer.Ordinal))
        {
            throw ApiException.Validation("profession_exam_mismatch", "The selected profession is not available for that exam.");
        }

        var allowedCountries = DeserializeStringList(profession.CountryTargetsJson);
        if (allowedCountries.Count > 0 && !allowedCountries.Contains(countryTarget, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("country_target_invalid", "Select a valid target country.");
        }

        var sessionProfessionIds = DeserializeStringList(session.ProfessionIdsJson);
        if (!string.Equals(session.ExamTypeId, examType.Id, StringComparison.Ordinal)
            || !sessionProfessionIds.Contains(profession.Id, StringComparer.Ordinal))
        {
            throw ApiException.Validation("session_selection_invalid", "The selected session is not available for this exam and profession.");
        }

        return (examType, profession, session);
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

    private Task<AuthenticatedSessionSubject> BuildSubjectAsync(
        ApplicationUserAccount account,
        string userId,
        string displayName,
        CancellationToken cancellationToken,
        string[]? adminPermissions = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requiresMfa = false;

        if (_allowLocalDemoWithoutMfa)
        {
            requiresMfa = false;
        }

        return Task.FromResult(new AuthenticatedSessionSubject(
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
            AdminPermissions: adminPermissions));
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
            subject.AdminPermissions);

    private static bool TryBuildCurrentUserFromClaims(
        ClaimsPrincipal principal,
        string? role,
        out CurrentUserResponse currentUser)
    {
        currentUser = default!;

        var userId = FindFirstValue(principal, ClaimTypes.NameIdentifier, "nameid", JwtRegisteredClaimNames.Sub);
        var email = FindFirstValue(principal, ClaimTypes.Email, JwtRegisteredClaimNames.Email, "email");
        var displayName = FindFirstValue(principal, ClaimTypes.Name, JwtRegisteredClaimNames.UniqueName, "unique_name");

        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(role)
            || !TryParseBooleanClaim(principal, AuthTokenService.IsEmailVerifiedClaimType, out var isEmailVerified)
            || !TryParseBooleanClaim(principal, AuthTokenService.IsAuthenticatorEnabledClaimType, out var isAuthenticatorEnabled)
            || !TryParseBooleanClaim(principal, AuthTokenService.RequiresEmailVerificationClaimType, out var requiresEmailVerification)
            || !TryParseBooleanClaim(principal, AuthTokenService.RequiresMfaClaimType, out var requiresMfa)
            || !TryParseDateTimeOffsetClaim(principal, AuthTokenService.EmailVerifiedAtClaimType, out var emailVerifiedAt)
            || !TryParseDateTimeOffsetClaim(principal, AuthTokenService.AuthenticatorEnabledAtClaimType, out var authenticatorEnabledAt))
        {
            return false;
        }

        var adminPermsClaim = FindFirstValue(principal, AuthTokenService.AdminPermissionsClaimType);
        var adminPermissions = string.IsNullOrEmpty(adminPermsClaim)
            ? null
            : adminPermsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        currentUser = new CurrentUserResponse(
            userId,
            email,
            role,
            displayName,
            isEmailVerified,
            isAuthenticatorEnabled,
            requiresEmailVerification,
            requiresMfa,
            emailVerifiedAt,
            authenticatorEnabledAt,
            adminPermissions);

        return true;
    }

    private async Task EnsureAccountCanAuthenticateAsync(ApplicationUserAccount account, CancellationToken cancellationToken)
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

            return;
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
    }

    private static bool TryParseBooleanClaim(ClaimsPrincipal principal, string claimType, out bool value)
    {
        value = default;
        var rawValue = principal.FindFirstValue(claimType);
        return !string.IsNullOrWhiteSpace(rawValue) && bool.TryParse(rawValue, out value);
    }

    private static bool TryParseDateTimeOffsetClaim(
        ClaimsPrincipal principal,
        string claimType,
        out DateTimeOffset? value)
    {
        value = null;

        var rawValue = principal.FindFirstValue(claimType);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(rawValue, out var parsedValue))
        {
            return false;
        }

        value = parsedValue;
        return true;
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
}
