using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OetLearner.Api.Services;
using OetLearner.Api.Services.StepUp;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Route-builder extensions that collapse the repeated auth + rate-limit
/// boilerplate applied to every admin endpoint handler.
///
/// Before:
/// <code>
/// admin.MapPost("/x", handler)
///      .RequireRateLimiting("PerUserWrite")
///      .RequireAuthorization("AdminContentWrite");
/// </code>
///
/// After:
/// <code>
/// admin.MapPost("/x", handler).WithAdminWrite("AdminContentWrite");
/// </code>
///
/// The admin <see cref="RouteGroupBuilder"/> still applies
/// <c>RequireAuthorization("AdminOnly")</c> and <c>RequireRateLimiting("PerUser")</c>
/// at group level; these helpers layer the per-endpoint permission (and, for
/// writes, the write-bucket rate limit) on top.
/// </summary>
internal static class AdminRouteBuilderExtensions
{
    /// <summary>
    /// Apply the standard admin write-endpoint policy: write-bucket rate limit
    /// plus the granular permission check.
    /// </summary>
    public static RouteHandlerBuilder WithAdminWrite(this RouteHandlerBuilder builder, string permission)
        => builder
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization(permission);

    /// <summary>
    /// Apply the standard admin read-endpoint policy: the granular permission
    /// check only (the group-level <c>PerUser</c> read rate limit already applies).
    /// </summary>
    public static RouteHandlerBuilder WithAdminRead(this RouteHandlerBuilder builder, string permission)
        => builder.RequireAuthorization(permission);

    /// <summary>
    /// Require a fresh step-up (re-)authentication proof scoped to
    /// <paramref name="scope"/> (PAY-16 / IAM-08) on top of the policy call.
    /// </summary>
    public static RouteHandlerBuilder WithStepUp(this RouteHandlerBuilder builder, string scope)
        => builder.AddEndpointFilter(new StepUpEndpointFilter(scope));

    /// <summary>Reads the <c>X-OET-Step-Up</c> header and verifies the presented
    /// token against the route's required scope.</summary>
    /// <remarks>
    /// An endpoint filter runs before the authorization middleware populates the
    /// principal, so <c>HttpContext.User</c> is not yet the authenticated user here.
    /// The filter therefore authenticates explicitly before reading the account claim;
    /// otherwise it would treat every caller as anonymous. It still never grants
    /// anything — the route's own policies remain the authority on access, and this
    /// only adds the step-up requirement on top.
    /// </remarks>
    internal sealed class StepUpEndpointFilter(string scope) : IEndpointFilter
    {
        public const string HeaderName = "X-OET-Step-Up";

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var httpContext = context.HttpContext;
            var authenticate = await httpContext.AuthenticateAsync();
            var principal = authenticate.Succeeded ? authenticate.Principal : httpContext.User;
            var authAccountId = principal?.FindFirstValue(AuthTokenService.AuthAccountIdClaimType);
            if (string.IsNullOrWhiteSpace(authAccountId))
            {
                return Results.Unauthorized();
            }

            var token = httpContext.Request.Headers[HeaderName].ToString();
            var stepUp = httpContext.RequestServices.GetRequiredService<IStepUpService>();
            var verification = await stepUp.VerifyAsync(authAccountId, token, scope, httpContext.RequestAborted);
            if (!verification.Ok)
            {
                return Results.Json(
                    new
                    {
                        code = "step_up_required",
                        message = "Confirm this action with your authenticator code.",
                        retryable = false,
                        requiredScope = scope,
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        }
    }
}
