using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/v1/auth");

        auth.MapPost("/login", async (AuthLoginRequest request, AuthService service, CancellationToken ct)
            => Results.Ok(await service.LoginAsync(request, ct)))
            .AllowAnonymous()
            .RequireRateLimiting("PerUserWrite");

        auth.MapGet("/me", async (HttpContext http, AuthService service, CancellationToken ct)
            => Results.Ok(await service.GetCurrentUserAsync(http.User, ct)))
            .RequireAuthorization();

        return app;
    }
}
