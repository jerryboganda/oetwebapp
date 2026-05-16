using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class MarketplaceEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Learner / Contributor endpoints ──────────────────
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var mp = v1.MapGroup("/marketplace");

        // Browse approved content
        mp.MapGet("/browse", async ([FromQuery] string? examTypeCode, [FromQuery] string? subtest, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.BrowseContentAsync(examTypeCode, subtest, search, page ?? 1, pageSize ?? 20, ct)));

        // Contributor profile
        mp.MapGet("/profile", async (HttpContext http, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetOrCreateContributorProfileAsync(http.UserId(), ct)));

        mp.MapPatch("/profile", async (HttpContext http, MarketplaceProfileUpdateRequest request, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateContributorProfileAsync(http.UserId(), request, ct)));

        // Submissions
        mp.MapPost("/submissions", async (HttpContext http, MarketplaceSubmissionRequest request, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateSubmissionAsync(http.UserId(), request, ct)));

        mp.MapGet("/submissions", async (HttpContext http, [FromQuery] int? page, [FromQuery] int? pageSize, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMySubmissionsAsync(http.UserId(), page ?? 1, pageSize ?? 20, ct)));

        mp.MapGet("/submissions/{submissionId}", async (string submissionId, HttpContext http, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSubmissionAsync(http.UserId(), submissionId, ct)));

        // ── Admin review endpoints ──────────────────────────
        var admin = app.MapGroup("/v1/admin/marketplace")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/pending", async ([FromQuery] int? page, [FromQuery] int? pageSize, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPendingSubmissionsAsync(page ?? 1, pageSize ?? 20, ct)))
            .WithAdminRead("AdminContentPublish");

        admin.MapPost("/submissions/{submissionId}/review", async (string submissionId, HttpContext http, MarketplaceReviewRequest request, MarketplaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReviewSubmissionAsync(http.AdminId(), submissionId, request, ct)))
            .WithAdminWrite("AdminContentPublish");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");
}
