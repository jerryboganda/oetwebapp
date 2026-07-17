using System.Security.Claims;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Writing V2 onboarding + profile surface. Some routes collide with the
/// legacy <c>WritingPathwayEndpoints</c> registrations (GET
/// /v1/writing/profile is already taken by V1). Per the plan we keep legacy
/// in place, so V2 surfaces those collisions at the <c>v2/</c> sub-prefix:
///   GET  /v1/writing/v2/profile         (V2)
///   POST /v1/writing/profile            (no V1 collision — V1 uses /onboarding)
///   GET  /v1/writing/profile/budget     (V2-only)
///   POST /v1/writing/onboarding/complete (V2-only)
/// </summary>
public static class WritingOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapWritingOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/v2/profile", async (HttpContext http, IWritingOnboardingService service, CancellationToken ct)
            => Results.Ok(await service.GetProfileAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingV2Profile");

        group.MapPost("/profile", async (
            WritingProfileUpdateRequest request,
            HttpContext http,
            IWritingOnboardingService service,
            CancellationToken ct)
            => Results.Ok(await service.SaveProfileAsync(http.WritingV2UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("SaveWritingV2Profile");

        group.MapGet("/profile/budget", async (HttpContext http, IWritingOnboardingService service, CancellationToken ct)
            => Results.Ok(await service.GetBudgetResponseAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingV2ProfileBudget");

        group.MapPost("/onboarding/complete", async (HttpContext http, IWritingOnboardingService service, CancellationToken ct)
            => Results.Ok(await service.CompleteOnboardingAsync(http.WritingV2UserId(), ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("CompleteWritingV2Onboarding");

        return app;
    }
}

internal static class WritingV2HttpContextExtensions
{
    internal static string WritingV2UserId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
