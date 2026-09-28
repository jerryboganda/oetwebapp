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
    public async Task SignOutAsync(SignOutRequest request, CancellationToken cancellationToken = default)
    {
        // C1: prefer cookie, fall back to body.
        var presented = ReadRefreshCookie();
        if (string.IsNullOrWhiteSpace(presented))
        {
            presented = request.RefreshToken;
        }
        // Always clear the cookie, even if no token was supplied — a sign-out that
        // leaves a cookie behind is a correctness bug.
        ClearRefreshCookie();

        if (string.IsNullOrWhiteSpace(presented))
        {
            await TryRevokeCurrentBearerSessionAsync(cancellationToken);
            return;
        }

        var tokenHash = tokenService.HashRefreshToken(presented);
        var refreshToken = await db.RefreshTokenRecords
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is null)
        {
            return;
        }

        await RevokeRefreshTokenFamilyAsync(refreshToken.FamilyId, cancellationToken);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var (account, authenticatedLearner) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var subject = await ResolveSubjectAsync(account, cancellationToken, authenticatedLearner);

        return BuildCurrentUserResponse(subject);
    }

    public async Task DeleteAccountAsync(ClaimsPrincipal principal, DeleteAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw ApiException.Validation("password_required", "Password is required to confirm account deletion.");
        }

        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);

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
            ClearRefreshCookie();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("account_update_conflict", "The account was modified concurrently. Please retry.");
        }
    }

    public async Task<ActiveSessionListResponse> GetActiveSessionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
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
            t.Id == currentSessionId,
            t.CountryCode,
            t.Platform,
            t.DeviceId
        )).ToList();

        return new ActiveSessionListResponse(sessions);
    }

    /// <summary>The account's currently-trusted device (spec §3.2), for the
    /// learner's own sessions screen. Null when none has been bootstrapped
    /// yet. IsCurrentDevice uses the request identity, including the server
    /// continuity cookie when browser storage has been reset.</summary>
    public async Task<TrustedDeviceSelfResponse?> GetTrustedDeviceAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var devices = await trustedDeviceService.GetActiveDevicesAsync(account.Id, cancellationToken);
        var presentedDeviceId = await ResolveDeviceIdForRequestAsync(
            account.Id,
            httpContextAccessor.HttpContext?.Request.Headers["X-OET-Device-Id"].ToString(),
            cancellationToken);
        var device = devices.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(presentedDeviceId)
            && string.Equals(candidate.DeviceId, presentedDeviceId, StringComparison.Ordinal))
            ?? devices.FirstOrDefault();
        if (device is null)
        {
            return null;
        }

        var maxDevices = await trustedDeviceService.GetEffectiveMaxDevicesAsync(account.Id, cancellationToken);
        return new TrustedDeviceSelfResponse(
            device.DeviceName,
            device.Platform,
            device.TrustedAt,
            device.LastSeenAt,
            !string.IsNullOrWhiteSpace(presentedDeviceId)
                && string.Equals(device.DeviceId, presentedDeviceId.Trim(), StringComparison.Ordinal),
            devices.Count,
            maxDevices);
    }

    public async Task RevokeSessionAsync(ClaimsPrincipal principal, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);

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

        // Revoke the whole refresh-token family, not just the currently listed
        // row. Rotation leaves historical rows in the family; flipping one row
        // would let a still-live rotated token continue the session.
        await sessionRevocationService.RevokeFamilyAsync(
            account.Id, token.FamilyId, "user_session_revoke", cancellationToken);
    }

    public async Task<int> RevokeAllOtherSessionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var (account, _) = await ResolveTrackedAccountFromPrincipalAsync(principal, cancellationToken);
        var currentSessionId = Guid.TryParse(
            principal.FindFirstValue(AuthTokenService.SessionIdClaimType), out var sid)
            ? sid
            : (Guid?)null;

        Guid? currentFamilyId = null;
        if (currentSessionId is not null)
        {
            currentFamilyId = await db.RefreshTokenRecords
                .AsNoTracking()
                .Where(t => t.ApplicationUserAccountId == account.Id && t.Id == currentSessionId.Value)
                .Select(t => (Guid?)t.FamilyId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // The revocation service handles family-wide invalidation, playback
        // termination, push notification, and the corresponding audit rows.
        var revokedFamilyCount = await sessionRevocationService.RevokeAllFamiliesAsync(
            account.Id, currentFamilyId, "user_revoke_all", cancellationToken);
        if (revokedFamilyCount > 0)
        {
            await securityEventLogger.TryLogAsync(
                account.Id,
                SecurityEventKinds.SessionRevokedAll,
                details: new { revokedFamilyCount },
                cancellationToken: cancellationToken);
        }

        return revokedFamilyCount;
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
        CancellationToken cancellationToken,
        string? knownProfileEmail = null)
    {
        var session = await CreateSessionCoreAsync(account, subject, cancellationToken, knownProfileEmail: knownProfileEmail);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    private bool IsLocalhostDevelopmentRequest(HttpContext httpContext)
        => environment.IsDevelopment()
            && (httpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || httpContext.Request.Host.Host.Equals("127.0.0.1", StringComparison.Ordinal));

    private bool ShouldExposeRefreshTokenInResponse()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return true;
        }

        var platform = httpContext.Request.Headers[ClientPlatformHeader].ToString();
        return platform.StartsWith("capacitor-", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("capacitor", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("desktop", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("native", StringComparison.OrdinalIgnoreCase);
    }

    private void SetRefreshCookie(string refreshToken, DateTimeOffset expires)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        var isLocalDev = IsLocalhostDevelopmentRequest(httpContext);
        httpContext.Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalDev,
            // SameSite=None requires Secure; in http localhost dev we fall back
            // to Lax which still allows same-site subresource requests.
            SameSite = isLocalDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path = RefreshCookiePath,
            Expires = expires,
            IsEssential = true,
        });
        httpContext.Response.Cookies.Append(CsrfCookieName, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), new CookieOptions
        {
            HttpOnly = false,
            Secure = !isLocalDev,
            SameSite = isLocalDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path = RefreshCookiePath,
            Expires = expires,
            IsEssential = true,
        });
    }

    private void ClearRefreshCookie()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return;
        var isLocalDev = IsLocalhostDevelopmentRequest(httpContext);
        httpContext.Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalDev,
            SameSite = isLocalDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path = RefreshCookiePath,
        });
        httpContext.Response.Cookies.Delete(CsrfCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = !isLocalDev,
            SameSite = isLocalDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path = RefreshCookiePath,
        });
    }

    private async Task<string?> ResolveDeviceIdForRequestAsync(
        string authAccountId,
        string? presentedDeviceId,
        CancellationToken cancellationToken)
    {
        var normalizedPresented = NormalizeDeviceId(presentedDeviceId);
        var httpContext = httpContextAccessor.HttpContext;
        var continuityDeviceId = httpContext?.Request.Cookies[DeviceBindingCookieName];
        continuityDeviceId = NormalizeDeviceId(continuityDeviceId);

        if (normalizedPresented is not null
            && await db.TrustedDevices.AsNoTracking().AnyAsync(
                device => device.ApplicationUserAccountId == authAccountId
                    && device.RevokedAt == null
                    && device.DeviceId == normalizedPresented,
                cancellationToken))
        {
            // Prefer an explicitly presented identity when it is already
            // approved. This matters for bounded multi-device overrides: a
            // stale continuity cookie must not make a valid newer device look
            // like a refresh-token mismatch.
            return normalizedPresented;
        }

        if (continuityDeviceId is not null
            && await db.TrustedDevices.AsNoTracking().AnyAsync(
                device => device.ApplicationUserAccountId == authAccountId
                    && device.RevokedAt == null
                    && device.DeviceId == continuityDeviceId,
                cancellationToken))
        {
            // The continuity cookie is account-scoped by this database lookup.
            // If a browser storage reset generated a new header, keep the
            // already-approved identity instead of counting a false replacement.
            return continuityDeviceId;
        }

        return normalizedPresented;
    }

    private void SetDeviceBindingCookie(string deviceId)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null
            || string.IsNullOrWhiteSpace(deviceId)
            || deviceId.Any(static character => character is ';' or ',' or '\r' or '\n'))
        {
            return;
        }

        var isLocalDev = IsLocalhostDevelopmentRequest(httpContext);
        httpContext.Response.Cookies.Append(DeviceBindingCookieName, deviceId, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalDev,
            SameSite = isLocalDev ? SameSiteMode.Lax : SameSiteMode.None,
            Path = RefreshCookiePath,
            Expires = timeProvider.GetUtcNow().Add(DeviceBindingCookieLifetime),
            IsEssential = true,
        });
    }

    private static string? NormalizeDeviceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length is 0 or > 128 || normalized.Any(char.IsControl)) return null;
        return normalized;
    }

    private async Task<bool> TryRevokeCurrentBearerSessionAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(AuthTokenService.SessionIdClaimType), out var sessionId))
        {
            return false;
        }

        var refreshToken = await db.RefreshTokenRecords
            .SingleOrDefaultAsync(t => t.Id == sessionId, cancellationToken);
        if (refreshToken is null)
        {
            return false;
        }

        await RevokeRefreshTokenFamilyAsync(refreshToken.FamilyId, cancellationToken);
        return true;
    }

    private async Task RevokeRefreshTokenFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var activeFamilyTokens = await db.RefreshTokenRecords
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var familyToken in activeFamilyTokens)
        {
            familyToken.RevokedAt = now;
        }

        if (activeFamilyTokens.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            // Both callers of this method are sign-out paths (explicit token
            // sign-out and bearer-only sign-out) — see call sites.
            await securityEventLogger.TryLogAsync(
                activeFamilyTokens[0].ApplicationUserAccountId,
                SecurityEventKinds.AuthSignOut,
                sessionFamilyId: familyId,
                cancellationToken: cancellationToken);
            await LogAccountSignOutAuditAsync(
                activeFamilyTokens[0].ApplicationUserAccountId,
                familyId);
        }
    }

    private async Task LogAccountSignOutAuditAsync(string authAccountId, Guid familyId)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = authAccountId,
            ActorAuthAccountId = authAccountId,
            ActorName = "Account Holder",
            Action = "Signed Out",
            ResourceType = "AuthAccount",
            ResourceId = authAccountId,
            Details = $"Refresh-token session family {familyId} was signed out by the account holder.",
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private string? ReadRefreshCookie()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return null;
        return httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var value)
            ? value
            : null;
    }

    private async Task<AuthSessionResponse> CreateSessionCoreAsync(
        ApplicationUserAccount account,
        AuthenticatedSessionSubject subject,
        CancellationToken cancellationToken,
        Guid? familyId = null,
        bool riskStepUpSatisfied = false,
        string? deviceIdOverride = null,
        string? knownProfileEmail = null)
    {
        var sessionId = Guid.NewGuid();
        // Fresh sign-in (familyId is null on entry) starts its own family;
        // rotation (RefreshAsync) passes the presented token's FamilyId so
        // the chain survives across refreshes. Computed BEFORE IssueSession
        // so the "sfam" claim is stamped into the very first access token,
        // not just the refresh-token row.
        var resolvedFamilyId = familyId ?? sessionId;

        string? deviceInfo = null;
        string? ipAddress = null;
        string? countryCode = null;
        string? deviceId = null;
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            deviceInfo = httpContext.Request.Headers.UserAgent.ToString();
            if (deviceInfo?.Length > 512) deviceInfo = deviceInfo[..512];
            ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            if (ipAddress?.Length > 64) ipAddress = ipAddress[..64];
            countryCode = httpContext.Request.Headers["CF-IPCountry"].ToString();
            if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length > 8) countryCode = null;
            deviceId = httpContext.Request.Headers["X-OET-Device-Id"].ToString();
        }
        deviceId = deviceIdOverride is not null
            ? NormalizeDeviceId(deviceIdOverride)
            : await ResolveDeviceIdForRequestAsync(account.Id, deviceId, cancellationToken);
        var platform = httpContext?.Request.Headers["X-OET-Client-Platform"].ToString();
        if (string.IsNullOrWhiteSpace(platform) || platform.Length > 32) platform = null;
        var appVersion = httpContext?.Request.Headers["X-App-Version"].ToString();
        if (string.IsNullOrWhiteSpace(appVersion) || appVersion.Length > 64) appVersion = null;

        var deviceVerificationExempt = false;

        // Security spec §3.3: risk-score a genuinely fresh sign-in BEFORE
        // touching any existing session — a blocked high-risk attempt must
        // never cost the account its already-legitimate session(s). Never
        // evaluated on a refresh rotation (the user isn't present for that).
        // riskStepUpSatisfied marks sign-ins that already carried a second
        // factor this attempt (TOTP MFA, device email-OTP) — those skip the
        // step-up challenge (it would loop) but High-risk still blocks.
        if (familyId is null)
        {
            var security = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Security;

            // Spec §3.2/§3.3 safety valve (RuntimeSettings.Security.
            // DeviceVerificationExemptEmails): owner/staff accounts explicitly
            // exempted from BOTH the risk-based step-up below and the
            // trusted-device gate further down — never challenged, on any
            // device, from any country.
            deviceVerificationExempt = await IsDeviceVerificationExemptAsync(
                account, security.DeviceVerificationExemptEmails, cancellationToken, knownProfileEmail);

            // A device that already completed the persistent §3.2 trusted-device
            // check is a stronger identity signal than the heuristic §3.3 risk
            // scoring below — re-challenging it on every IP-derived country
            // wobble (CF-IPCountry flips between edges/VPN/mobile handoff for the
            // same physical device) is the "asks for OTP again and again on the
            // SAME device" regression this guards against. Only exempts the
            // MEDIUM-risk/step-up paths below; High-risk (impossible travel)
            // still blocks outright regardless of device trust, since a
            // replayed/stolen session can present the same device id from a
            // different place.
            var trustedDevices = deviceId is not null
                ? await trustedDeviceService.GetActiveDevicesAsync(account.Id, cancellationToken)
                : Array.Empty<TrustedDevice>();
            var deviceAlreadyTrusted = trustedDevices.Any(trustedDevice =>
                string.Equals(trustedDevice.DeviceId, deviceId, StringComparison.Ordinal));

            // §3.3 country allow-list — an independent control that works even
            // with the risk engine off. Sign-ins with no CF-IPCountry (local
            // dev, direct-to-origin) always pass: the control is a policy
            // fence, not an authenticity proof.
            if (!deviceVerificationExempt
                && !string.Equals(security.CountryAllowListMode, SecurityCountryAllowListModes.Off, StringComparison.Ordinal)
                && IsOutsideCountryAllowList(security.CountryAllowList, countryCode))
            {
                if (string.Equals(security.CountryAllowListMode, SecurityCountryAllowListModes.Block, StringComparison.Ordinal))
                {
                    var reasons = new[] { "country_not_allowed" };
                    await securityEventLogger.TryLogAsync(
                        account.Id, SecurityEventKinds.RiskSignInBlocked,
                        details: new { reasons, countryCode },
                        cancellationToken: cancellationToken);
                    await NotifyRiskSignInBlockedAsync(account, reasons, countryCode, cancellationToken);
                    throw ApiException.Forbidden(
                        "sign_in_blocked_risk",
                        "Sign-in from this location is not permitted for your account. Contact support if this was you.");
                }

                // step_up
                if (!riskStepUpSatisfied && !deviceAlreadyTrusted)
                {
                    await securityEventLogger.TryLogAsync(
                        account.Id, SecurityEventKinds.RiskStepUpRequired,
                        details: new { reasons = new[] { "country_not_allowed" }, countryCode, stepUpAvailable = deviceId is not null },
                        cancellationToken: cancellationToken);
                    if (deviceId is not null)
                    {
                        throw new DeviceVerificationRequiredException(
                            account.Email, CreateDeviceChallengeToken(account.Id, deviceId));
                    }
                    // No device id (old cached shell) → fail open; the event
                    // above still records that a step-up was warranted.
                }
            }

            if (!deviceVerificationExempt && !string.Equals(security.RiskMode, SecurityRiskModes.Off, StringComparison.Ordinal))
            {
                var risk = await signInRiskService.EvaluateAsync(account.Id, countryCode, ipAddress, cancellationToken);
                if (risk.Level != SignInRiskLevel.None)
                {
                    var kind = risk.Reasons.Contains("impossible_travel")
                        ? SecurityEventKinds.RiskImpossibleTravel
                        : SecurityEventKinds.RiskCountryChanged;
                    await securityEventLogger.TryLogAsync(
                        account.Id, kind, details: new { reasons = risk.Reasons, countryCode },
                        cancellationToken: cancellationToken);

                    if (string.Equals(security.RiskMode, SecurityRiskModes.Enforce, StringComparison.Ordinal))
                    {
                        if (risk.Level == SignInRiskLevel.High)
                        {
                            await securityEventLogger.TryLogAsync(
                                account.Id, SecurityEventKinds.RiskSignInBlocked,
                                details: new { reasons = risk.Reasons, countryCode },
                                cancellationToken: cancellationToken);
                            await NotifyRiskSignInBlockedAsync(account, risk.Reasons, countryCode, cancellationToken);
                            throw ApiException.Forbidden(
                                "sign_in_blocked_risk",
                                "This sign-in was blocked for unusual account activity. Contact support if this was you.");
                        }

                        // Medium → email-OTP step-up (spec §3.3), reusing the
                        // device-challenge transport the sign-in UI already
                        // handles. Skipped when this attempt already proved a
                        // second factor, when no device id is present, or when
                        // this device already holds the account's persistent
                        // §3.2 trust (see deviceAlreadyTrusted above).
                        if (risk.Level == SignInRiskLevel.Medium && !riskStepUpSatisfied && !deviceAlreadyTrusted)
                        {
                            await securityEventLogger.TryLogAsync(
                                account.Id, SecurityEventKinds.RiskStepUpRequired,
                                details: new { reasons = risk.Reasons, countryCode, stepUpAvailable = deviceId is not null },
                                cancellationToken: cancellationToken);
                            if (deviceId is not null)
                            {
                                throw new DeviceVerificationRequiredException(
                                    account.Email, CreateDeviceChallengeToken(account.Id, deviceId));
                            }
                        }
                    }
                }
            }
        }

        // Security spec §3.2: a genuinely fresh sign-in from a device other
        // than the one currently trusted needs an email-OTP challenge before
        // it can proceed — evaluated BEFORE IssueSession/revoke-others so an
        // OtpRequired/CooldownBlocked outcome never touches the account's
        // existing legitimate session. When enforcement is active a missing
        // device id is rejected; it is not a bypass for old clients.
        if (familyId is null)
        {
            var security = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Security;
            if (security.TrustedDeviceRequired && !deviceVerificationExempt)
            {
                var resolution = await trustedDeviceService.ResolveForSignInAsync(
                    account.Id, deviceId, security.DeviceChangeWindowDays, security.DeviceChangeMaxPerWindow, cancellationToken);

                switch (resolution.Resolution)
                {
                    case DeviceResolution.CooldownBlocked:
                        // A correct password plus the existing device email OTP
                        // is a stronger proof of account ownership than the
                        // client-generated identity churn that triggered this
                        // rolling counter. Learners must never be left at a
                        // support-only dead end: make the cooldown a step-up
                        // signal and let the existing verified-device flow
                        // approve the replacement. Privileged accounts retain
                        // the hard block and the admin audit signal, but now with
                        // exact cooldown evidence (cooldownUntil, secondsRemaining, window/limit, countdown).
                        if (string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
                        {
                            var learnerCooldownToken = CreateDeviceChallengeTokenForResolution(account.Id, deviceId!, resolution);
                            throw new DeviceVerificationRequiredException(
                                account.Email,
                                learnerCooldownToken,
                                mode: "cooldown",
                                registeredDevices: resolution.RegisteredDevices,
                                activeDeviceCount: resolution.ActiveDeviceCount,
                                maxDevices: resolution.MaxDevices,
                                cooldownUntil: resolution.CooldownUntil,
                                secondsRemaining: resolution.SecondsRemaining,
                                changeWindowDays: resolution.ChangeWindowDays,
                                changeMaxPerWindow: resolution.ChangeMaxPerWindow);
                        }
                        throw new DeviceChangeCooldownException(
                            $"Too many device changes recently. Try again in {FormatCooldownCountdown(resolution.SecondsRemaining ?? 0)} or contact support.",
                            resolution.CooldownUntil ?? timeProvider.GetUtcNow().AddDays(resolution.ChangeWindowDays ?? 7),
                            resolution.SecondsRemaining ?? 0,
                            resolution.ChangeWindowDays ?? 7,
                            resolution.ChangeMaxPerWindow ?? 3,
                            resolution.ActiveDeviceCount,
                            resolution.MaxDevices);
                    case DeviceResolution.OtpRequired:
                        throw new DeviceVerificationRequiredException(
                            account.Email,
                            CreateDeviceChallengeTokenForResolution(account.Id, deviceId!, resolution),
                            mode: "otp_required",
                            registeredDevices: resolution.RegisteredDevices,
                            activeDeviceCount: resolution.ActiveDeviceCount,
                            maxDevices: resolution.MaxDevices,
                            cooldownUntil: resolution.CooldownUntil,
                            secondsRemaining: resolution.SecondsRemaining,
                            changeWindowDays: resolution.ChangeWindowDays,
                            changeMaxPerWindow: resolution.ChangeMaxPerWindow);
                    case DeviceResolution.ReplacementRequired:
                        throw new DeviceVerificationRequiredException(
                            account.Email,
                            CreateDeviceChallengeTokenForResolution(account.Id, deviceId!, resolution),
                            mode: "replacement_required",
                            registeredDevices: resolution.RegisteredDevices,
                            activeDeviceCount: resolution.ActiveDeviceCount,
                            maxDevices: resolution.MaxDevices,
                            cooldownUntil: resolution.CooldownUntil,
                            secondsRemaining: resolution.SecondsRemaining,
                            changeWindowDays: resolution.ChangeWindowDays,
                            changeMaxPerWindow: resolution.ChangeMaxPerWindow);
                    case DeviceResolution.Bootstrap:
                        await trustedDeviceService.TrustDeviceAsync(
                            account.Id, deviceId!, deviceInfo, platform, "bootstrap", cancellationToken);
                        break;
                    case DeviceResolution.Trusted:
                        break;
                    case DeviceResolution.NoDeviceId:
                        throw ApiException.Forbidden(
                            "device_id_required",
                            "This app must identify the device before signing in. Update the app and try again.");
                    case DeviceResolution.InvalidDeviceId:
                        throw ApiException.Forbidden(
                            "device_id_invalid",
                            "This app sent an invalid device identity. Update the app and try again.");
                    default:
                        break;
                }
            }
        }

        if (deviceId is not null)
        {
            SetDeviceBindingCookie(deviceId);
        }

        var issuedSession = tokenService.IssueSession(subject, sessionId, resolvedFamilyId);

        // Security spec §3.1: signing in on any platform revokes every OTHER
        // active session immediately. Only on a genuinely fresh sign-in
        // (familyId is null) — a refresh rotation is the SAME session
        // continuing, not a new one, and must never revoke itself.
        if (familyId is null)
        {
            var securitySettings = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Security;
            // The anti-sharing device policy is a hard invariant: an
            // emergency toggle cannot turn a per-learner device override into
            // simultaneous account access. The legacy switch still controls
            // accounts that have device enforcement disabled.
            if (securitySettings.SingleActiveSessionEnabled || securitySettings.TrustedDeviceRequired)
            {
                await sessionRevocationService.RevokeAllFamiliesAsync(
                    account.Id, exceptFamilyId: resolvedFamilyId, reason: "new_sign_in", cancellationToken);
            }
        }

        db.RefreshTokenRecords.Add(new RefreshTokenRecord
        {
            Id = sessionId,
            ApplicationUserAccountId = account.Id,
            TokenHash = issuedSession.RefreshTokenHash,
            // H3 (security): a new sign-in starts its own refresh-token family.
            // Rotation paths pass the presented token's FamilyId so the chain is
            // preserved across refreshes; reuse of a revoked token then burns
            // the entire family. See RefreshAsync.
            FamilyId = resolvedFamilyId,
            ExpiresAt = issuedSession.RefreshTokenExpiresAt,
            CreatedAt = timeProvider.GetUtcNow(),
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            CountryCode = countryCode,
            DeviceId = deviceId,
            Platform = platform,
            AppVersion = appVersion
        });

        // Fresh sign-in (familyId is null on entry) vs. rotation (familyId ==
        // resolvedFamilyId, an existing family continuing) — only log the
        // former as a distinct "new session" event; rotation churn on the
        // same family is not itself security-interesting.
        if (familyId is null)
        {
            await securityEventLogger.TryLogAsync(
                account.Id,
                SecurityEventKinds.SessionCreated,
                sessionFamilyId: resolvedFamilyId,
                cancellationToken: cancellationToken);
        }

        await Task.CompletedTask;

        // Web transport is the HttpOnly cookie. Native/desktop shells receive
        // the body token only when they identify themselves explicitly.
        SetRefreshCookie(issuedSession.RefreshToken, issuedSession.RefreshTokenExpiresAt);

        return new AuthSessionResponse(
            issuedSession.AccessToken,
            ShouldExposeRefreshTokenInResponse() ? issuedSession.RefreshToken : null,
            issuedSession.AccessTokenExpiresAt,
            issuedSession.RefreshTokenExpiresAt,
            BuildCurrentUserResponse(subject));
    }
}
