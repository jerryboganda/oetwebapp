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

        // ─── Seat Pack endpoints ───────────────────────────────────────────
        sponsor.MapGet("/seat-packs", async (HttpContext http, ISponsorSeatPackService service, CancellationToken ct)
            => Results.Ok(await service.ListPacksAsync(http.SponsorId(), ct)));

        sponsor.MapPost("/seat-packs", async (HttpContext http,
            PurchaseSeatPackRequest request, ISponsorSeatPackService service, CancellationToken ct)
            => Results.Ok(await service.PurchasePackAsync(http.SponsorId(), request, ct)));

        sponsor.MapPost("/seat-packs/{packId:guid}/assign", async (Guid packId, HttpContext http,
            AssignSeatRequest request, ISponsorSeatPackService service, CancellationToken ct)
            => Results.Ok(await service.AssignSeatAsync(http.SponsorId(), packId, request, ct)));

        sponsor.MapDelete("/seat-packs/assignments/{assignmentId:guid}", async (Guid assignmentId, HttpContext http,
            ISponsorSeatPackService service, CancellationToken ct) =>
        {
            await service.RevokeSeatAsync(http.SponsorId(), assignmentId, ct);
            return Results.NoContent();
        });

        sponsor.MapGet("/billing/ledger", async (HttpContext http, ISponsorSeatPackService service, CancellationToken ct,
            int? page, int? pageSize)
            => Results.Ok(await service.GetLedgerAsync(http.SponsorId(), page ?? 1, pageSize ?? 20, ct)));

        return app;
    }

    private static string SponsorId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated sponsor id is required.");
}

public record SponsorInviteRequest(string Email);
