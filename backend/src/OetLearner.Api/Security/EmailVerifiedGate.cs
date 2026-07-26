using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Security;

/// <summary>
/// Course Platform Security Requirements §4.2 hard gate: learner API access
/// requires a verified email once <c>Security.RequireVerifiedEmailForLearners</c>
/// is flipped on (Runtime Settings — no deploy needed). Attached to the
/// LearnerOnly policy; other roles already carry a static
/// RequireClaim(email_verified) on their own policies. While the toggle is
/// OFF (the default) this requirement always succeeds, so the banner ships
/// enforcing nothing until the owner decides otherwise.
/// </summary>
public sealed class EmailVerifiedRequirement : IAuthorizationRequirement
{
    /// <summary>Failure-reason marker the result handler below matches on,
    /// and the error code the frontend maps to the verify screen.</summary>
    public const string FailureReason = "email_verification_required";
}

public sealed class EmailVerifiedRequirementHandler(IRuntimeSettingsProvider runtimeSettings)
    : AuthorizationHandler<EmailVerifiedRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EmailVerifiedRequirement requirement)
    {
        // Unauthenticated requests are rejected by RequireAuthenticatedUser
        // on the same policy; don't double-fail them here (a Fail would turn
        // the 401 challenge into a 403).
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!context.User.IsInRole(ApplicationUserRoles.Learner))
        {
            context.Succeed(requirement);
            return;
        }

        var security = (await runtimeSettings.GetAsync(CancellationToken.None)).Security;
        if (!security.RequireVerifiedEmailForLearners)
        {
            context.Succeed(requirement);
            return;
        }

        var raw = context.User.FindFirst(AuthTokenService.IsEmailVerifiedClaimType)?.Value;
        if (bool.TryParse(raw, out var verified) && verified)
        {
            context.Succeed(requirement);
            return;
        }

        // Tokens minted before the claim existed also land here — completing
        // verification issues a fresh session with the claim set, so the gate
        // self-heals the moment the user verifies.
        context.Fail(new AuthorizationFailureReason(this, EmailVerifiedRequirement.FailureReason));
    }
}

/// <summary>
/// Turns the requirement's failure into the app's standard error shape
/// (<c>{ code, message }</c> — what lib/api.ts parses) instead of the default
/// empty 403, so the frontend can route the learner to the verify screen.
/// Every other authorization outcome is delegated to the framework default.
/// </summary>
public sealed class EmailVerifiedAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler DefaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(
                r => string.Equals(r.Message, EmailVerifiedRequirement.FailureReason, StringComparison.Ordinal)) == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = EmailVerifiedRequirement.FailureReason,
                message = "Verify your email address to continue using your account.",
            });
            return;
        }

        await DefaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
