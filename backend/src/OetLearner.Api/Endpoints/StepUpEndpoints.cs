using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.StepUp;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Step-up (re-)authentication endpoint (PAY-16 / IAM-08). Exchanges a fresh
/// authenticator code for a short-lived, single-scope token that the
/// money-moving admin calls must present on the <c>X-OET-Step-Up</c> header.
/// </summary>
public static class StepUpEndpoints
{
    public static IEndpointRouteBuilder MapStepUpEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth");

        auth.MapPost("/step-up", async (
                StepUpRequest request,
                ClaimsPrincipal user,
                IStepUpService stepUp,
                ISecurityEventLogger securityEvents,
                CancellationToken ct) =>
            {
                var authAccountId = user.FindFirstValue(AuthTokenService.AuthAccountIdClaimType);
                if (string.IsNullOrWhiteSpace(authAccountId))
                {
                    return Results.Unauthorized();
                }

                var scope = request.Scope?.Trim() ?? string.Empty;
                try
                {
                    var response = await stepUp.IssueAsync(authAccountId, scope, request.Code ?? string.Empty, ct);
                    await securityEvents.TryLogAsync(
                        authAccountId,
                        SecurityEventKinds.AuthSignInSucceeded,
                        details: new { factor = "totp", action = "step_up", scope },
                        cancellationToken: ct);
                    return Results.Ok(response);
                }
                catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status403Forbidden)
                {
                    await securityEvents.TryLogAsync(
                        authAccountId,
                        SecurityEventKinds.AuthMfaFailed,
                        details: new { factor = "totp", action = "step_up", scope, reason = exception.ErrorCode },
                        cancellationToken: ct);
                    throw;
                }
            })
            .RequireAuthorization()
            .RequireRateLimiting("AuthBruteforce");

        return app;
    }
}
