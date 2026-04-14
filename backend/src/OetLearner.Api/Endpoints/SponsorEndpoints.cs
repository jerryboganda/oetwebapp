using System.Security.Claims;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class SponsorEndpoints
{
    public static IEndpointRouteBuilder MapSponsorEndpoints(this IEndpointRouteBuilder app)
    {
        var sponsor = app.MapGroup("/v1/sponsor")
            .RequireAuthorization("SponsorOnly")
            .RequireRateLimiting("PerUser");

        sponsor.MapGet("/dashboard", async (HttpContext http, SponsorService service, CancellationToken ct)
            => Results.Ok(await service.GetDashboardAsync(http.SponsorId(), ct)));

        sponsor.MapGet("/learners", async (HttpContext http, SponsorService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetLearnersAsync(http.SponsorId(), page ?? 1, pageSize ?? 20, ct)));

        sponsor.MapPost("/learners/invite", async (HttpContext http,
            SponsorInviteRequest request, SponsorService service, CancellationToken ct)
            => Results.Ok(await service.InviteLearnerAsync(http.SponsorId(), request.Email, ct)));

        sponsor.MapDelete("/learners/{id:guid}", async (Guid id, HttpContext http, SponsorService service, CancellationToken ct)
            => Results.Ok(await service.RemoveSponsorshipAsync(http.SponsorId(), id, ct)));

        sponsor.MapGet("/billing", async (HttpContext http, SponsorService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingAsync(http.SponsorId(), ct)));

        return app;
    }

    private static string SponsorId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated sponsor id is required.");
}

public record SponsorInviteRequest(string Email);
