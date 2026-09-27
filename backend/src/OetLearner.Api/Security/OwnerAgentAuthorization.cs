using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Security;

/// <summary>Policy names for the Owner Agent Console (agent-console/CONTRACT.md §5).</summary>
public static class OwnerAgentPolicies
{
    /// <summary>Owner identity only (no unlock ticket): <c>/unlock</c>.</summary>
    public const string Owner = "OwnerAgentOwner";

    /// <summary>Owner identity + a valid <c>X-Owner-Agent-Unlock</c> ticket: every other route and the hub.</summary>
    public const string Unlocked = "OwnerAgent";

    /// <summary>
    /// Rate-limit policy for <c>/v1/owner-agent/hub</c>. The shared
    /// <c>HubConnect</c> bucket (30/min) counts every HTTP request under a hub
    /// route, and the console hub is long-polling only (the Next proxy cannot
    /// upgrade WebSockets and header auth rules out SSE/WebSockets). A streaming
    /// turn returns a poll per burst of events, which would exhaust 30/min within
    /// seconds, so the hub gets its own per-account bucket sized for long polling.
    /// </summary>
    public const string HubRateLimit = "OwnerAgentHub";

    public static void AddOwnerAgentPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Owner, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(ApplicationUserRoles.Admin)
            .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant())
            .AddRequirements(new OwnerAgentRequirement(requireUnlock: false)));
        options.AddPolicy(Unlocked, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(ApplicationUserRoles.Admin)
            .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant())
            .AddRequirements(new OwnerAgentRequirement(requireUnlock: true)));
    }
}

/// <summary>Header names used between the admin UI, the API and the hub.</summary>
public static class OwnerAgentHeaders
{
    public const string Unlock = "X-Owner-Agent-Unlock";
    public const string StepUp = "X-Owner-Agent-StepUp";

    /// <summary>Hard cap on presented ticket/token length (a real one is a few hundred chars).</summary>
    public const int MaxTokenLength = 4096;

    public static string? Read(HttpContext? httpContext, string headerName)
    {
        if (httpContext is null)
        {
            return null;
        }

        var value = httpContext.Request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(value) || value.Length > MaxTokenLength ? null : value.Trim();
    }
}

/// <summary>Stable machine codes for Owner Agent authorization failures (403 bodies).</summary>
public static class OwnerAgentFailureCodes
{
    public const string NotOwner = "owner_agent_not_owner";
    public const string UnlockRequired = "owner_agent_unlock_required";
    public const string UnlockInvalid = "owner_agent_unlock_invalid";
    public const string UnlockExpired = "owner_agent_unlock_expired";
    /// <summary>
    /// Revoked by an explicit lock or an authenticator re-enrolment (the console is "locked").
    /// Must stay in sync with <c>OWNER_AGENT_LOCK_ERROR_CODES</c> in lib/owner-agent/api.ts and
    /// the hub error list in lib/owner-agent/signalr.ts.
    /// </summary>
    public const string UnlockRevoked = "owner_agent_unlock_revoked";
    public const string UnlockSessionMismatch = "owner_agent_unlock_session_mismatch";
    public const string SessionRevoked = "owner_agent_session_revoked";

    /// <summary>Codes that mean "show the unlock screen" and get a JSON 403 body.</summary>
    public static readonly IReadOnlySet<string> UnlockCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        UnlockRequired, UnlockInvalid, UnlockExpired, UnlockRevoked, UnlockSessionMismatch, SessionRevoked,
    };

    public static string Describe(string code) => code switch
    {
        UnlockRequired => "Unlock the agent console with your password and authenticator code.",
        UnlockExpired => "The agent console unlock has expired. Unlock again to continue.",
        UnlockRevoked => "The agent console was locked. Unlock again to continue.",
        SessionRevoked => "Your session was signed out. Sign in and unlock again.",
        UnlockSessionMismatch => "The unlock belongs to a different session. Unlock again in this session.",
        _ => "The agent console unlock is invalid. Unlock again to continue.",
    };
}

/// <summary>Claim helpers for the owner gate.</summary>
public static class OwnerAgentIdentity
{
    public static string? GetAuthAccountId(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(AuthTokenService.AuthAccountIdClaimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static Guid? GetSessionFamilyId(ClaimsPrincipal? principal)
        => Guid.TryParse(principal?.FindFirst(AuthTokenService.SessionFamilyClaimType)?.Value, out var familyId)
            ? familyId
            : null;

    /// <summary>
    /// Role <c>admin</c> + verified email + <c>system_admin</c> permission +
    /// <c>auth_account_id</c> on the env-only allow-list. All four, always.
    /// </summary>
    public static bool IsOwner(ClaimsPrincipal? principal, OwnerAgentOptions options)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!principal.IsInRole(ApplicationUserRoles.Admin))
        {
            return false;
        }

        var emailVerified = principal.FindFirst(AuthTokenService.IsEmailVerifiedClaimType)?.Value;
        if (!bool.TryParse(emailVerified, out var verified) || !verified)
        {
            return false;
        }

        if (!AdminPermissionEvaluator.HasAny(
                principal.FindFirst(AuthTokenService.AdminPermissionsClaimType)?.Value,
                AdminPermissions.SystemAdmin))
        {
            return false;
        }

        return options.IsOwnerAccount(GetAuthAccountId(principal));
    }
}

public sealed class OwnerAgentRequirement(bool requireUnlock) : IAuthorizationRequirement
{
    public bool RequireUnlock { get; } = requireUnlock;
}

/// <summary>
/// Evaluates <see cref="OwnerAgentRequirement"/>. The unlock ticket is read from the
/// <c>X-Owner-Agent-Unlock</c> header of the request being authorized — for the hub
/// that is the negotiate request and every long-polling request, because the SignalR
/// client re-sends its <c>headers</c> option on each of them. There is deliberately
/// no query-string fallback.
/// </summary>
public sealed class OwnerAgentAuthorizationHandler(
    IOptions<OwnerAgentOptions> options,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<OwnerAgentRequirement>
{
    // IAuthorizationHandler instances are resolved for every authorization evaluation in the
    // app, so this handler keeps its constructor cheap and resolves the (DB-backed) unlock
    // service only when an OwnerAgent requirement is actually being evaluated.
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerAgentRequirement requirement)
    {
        // Unauthenticated callers are rejected (401) by RequireAuthenticatedUser on the
        // same policy; failing here would turn the challenge into a 403.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!OwnerAgentIdentity.IsOwner(context.User, options.Value))
        {
            context.Fail(new AuthorizationFailureReason(this, OwnerAgentFailureCodes.NotOwner));
            return;
        }

        if (!requirement.RequireUnlock)
        {
            context.Succeed(requirement);
            return;
        }

        var httpContext = context.Resource switch
        {
            HttpContext direct => direct,
            HubInvocationContext invocation => invocation.Context.GetHttpContext(),
            _ => null,
        } ?? httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            context.Fail(new AuthorizationFailureReason(this, OwnerAgentFailureCodes.UnlockRequired));
            return;
        }

        if (httpContext.Items.TryGetValue(OwnerAgentUnlockValidation.HttpContextItemKey, out var cached)
            && cached is OwnerAgentUnlockValidation { IsValid: true })
        {
            context.Succeed(requirement);
            return;
        }

        var ticket = OwnerAgentHeaders.Read(httpContext, OwnerAgentHeaders.Unlock);
        if (ticket is null)
        {
            context.Fail(new AuthorizationFailureReason(this, OwnerAgentFailureCodes.UnlockRequired));
            return;
        }

        var services = (context.Resource as HubInvocationContext)?.ServiceProvider ?? httpContext.RequestServices;
        var unlockService = services.GetRequiredService<IOwnerAgentUnlockService>();
        var validation = await unlockService.ValidateAsync(context.User, ticket, httpContext.RequestAborted);
        if (!validation.IsValid)
        {
            context.Fail(new AuthorizationFailureReason(this, validation.FailureCode ?? OwnerAgentFailureCodes.UnlockInvalid));
            return;
        }

        httpContext.Items[OwnerAgentUnlockValidation.HttpContextItemKey] = validation;
        context.Succeed(requirement);
    }
}

/// <summary>
/// Decorates the app's authorization result handler so an owner whose unlock is
/// missing/expired/revoked gets <c>{ code, message }</c> (what lib/api.ts parses)
/// instead of an empty 403, letting the console show the unlock screen. Every other
/// outcome — including non-owners, who get the framework's empty 403 — is delegated.
/// </summary>
public sealed class OwnerAgentAuthorizationResultHandler(IAuthorizationMiddlewareResultHandler inner)
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons
                .FirstOrDefault(r => r.Handler is OwnerAgentAuthorizationHandler
                                     && OwnerAgentFailureCodes.UnlockCodes.Contains(r.Message)) is { } reason)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new
            {
                code = reason.Message,
                message = OwnerAgentFailureCodes.Describe(reason.Message),
                retryable = false,
            });
            return;
        }

        await inner.HandleAsync(next, context, policy, authorizeResult);
    }
}
