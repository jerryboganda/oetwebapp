using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth");

        auth.MapPost("/register", async (RegisterRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.RegisterLearnerAsync(request, ct)))
            .AllowAnonymous();

        auth.MapGet("/catalog/signup", async (AuthService service, CancellationToken ct)
                => Results.Ok(await service.GetSignupCatalogAsync(ct)))
            .AllowAnonymous();

        auth.MapPost("/sign-in", async (PasswordSignInRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.SignInAsync(request, ct)))
            .AllowAnonymous();

        auth.MapGet("/external/{provider}/start", (string provider, string? next, string? platform, ExternalAuthService service)
                => Results.Redirect(service.BuildAuthorizationRedirect(provider, next, platform).ToString()))
            .AllowAnonymous();

        auth.MapGet("/external/{provider}/callback", async (
                string provider,
                string? code,
                string? state,
                string? error,
                ExternalAuthService service,
                CancellationToken ct) =>
                Results.Redirect((await service.CompleteCallbackAsync(provider, code, state, error, ct)).ToString()))
            .AllowAnonymous();

        auth.MapPost("/external/{provider}/exchange", async (
                string provider,
                ExternalAuthExchangeRequest request,
                ExternalAuthService service,
                CancellationToken ct) =>
                Results.Ok(await service.ExchangeAsync(provider, request, ct)))
            .AllowAnonymous();

        auth.MapPost("/email/send-verification-otp", async (SendEmailOtpRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.SendEmailVerificationOtpAsync(request, ct)))
            .AllowAnonymous();

        auth.MapPost("/email/verify-otp", async (VerifyEmailOtpRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.VerifyEmailOtpAsync(request, ct)))
            .AllowAnonymous();

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.ForgotPasswordAsync(request, ct)))
            .AllowAnonymous();

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.ResetPasswordAsync(request, ct);
                return Results.NoContent();
            })
            .AllowAnonymous();

        auth.MapPost("/mfa/authenticator/begin", async (ClaimsPrincipal user, BeginAuthenticatorSetupRequest _, AuthService service, CancellationToken ct)
                => Results.Ok(await service.BeginAuthenticatorSetupAsync(user, ct)))
            .RequireAuthorization();

        auth.MapPost("/mfa/authenticator/confirm", async (ClaimsPrincipal user, ConfirmAuthenticatorSetupRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.ConfirmAuthenticatorSetupAsync(user, request, ct)))
            .RequireAuthorization();

        auth.MapPost("/mfa/challenge", async (MfaChallengeRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.CompleteMfaChallengeAsync(request, ct)))
            .AllowAnonymous();

        auth.MapPost("/mfa/recovery", async (MfaChallengeRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.CompleteRecoveryChallengeAsync(request, ct)))
            .AllowAnonymous();

        auth.MapGet("/me", async (ClaimsPrincipal user, AuthService service, CancellationToken ct)
                => Results.Ok(await service.GetCurrentUserAsync(user, ct)))
            .RequireAuthorization();

        auth.MapPost("/refresh", async (RefreshTokenRequest request, AuthService service, CancellationToken ct)
                => Results.Ok(await service.RefreshAsync(request, ct)))
            .AllowAnonymous();

        auth.MapPost("/sign-out", async (SignOutRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.SignOutAsync(request, ct);
                return Results.NoContent();
            })
            .AllowAnonymous();

        auth.MapPost("/account/delete", async (ClaimsPrincipal user, DeleteAccountRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.DeleteAccountAsync(user, request, ct);
                return Results.NoContent();
            })
            .RequireAuthorization("LearnerOnly")
            .WithName("DeleteAccount")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        auth.MapGet("/sessions", async (ClaimsPrincipal user, AuthService service, CancellationToken ct)
                => Results.Ok(await service.GetActiveSessionsAsync(user, ct)))
            .RequireAuthorization();

        auth.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user, AuthService service, CancellationToken ct) =>
            {
                await service.RevokeSessionAsync(user, sessionId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization();

        auth.MapDelete("/sessions", async (ClaimsPrincipal user, AuthService service, CancellationToken ct) =>
            {
                var count = await service.RevokeAllOtherSessionsAsync(user, ct);
                return Results.Ok(new { revokedCount = count });
            })
            .RequireAuthorization();

        return app;
    }
}
