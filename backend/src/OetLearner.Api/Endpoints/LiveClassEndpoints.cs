using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Endpoints;

public static class LiveClassEndpoints
{
    public static IEndpointRouteBuilder MapLiveClassEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/classes")
            .WithTags("Live Classes")
            .RequireAuthorization("LearnerOnly");

        learner.MapGet("", async (
            string? professionTrack,
            string? type,
            string? tutorProfileId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var query = new LiveClassListQuery(professionTrack, type, tutorProfileId, from, to, page ?? 1, pageSize ?? 20);
            return Results.Ok(await service.ListCatalogAsync(query, userId, ct));
        });

        learner.MapGet("/{idOrSlug}", async (string idOrSlug, HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.GetClassAsync(idOrSlug, userId, ct));
        });

        learner.MapPost("/sessions/{sessionId}/enroll", async (
            string sessionId,
            LiveClassEnrollRequest? request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var idempotencyKey = request?.IdempotencyKey ?? http.Request.Headers["Idempotency-Key"].FirstOrDefault();
            return Results.Ok(await service.EnrollAsync(sessionId, userId, idempotencyKey, ct));
        }).RequireRateLimiting("PerUser");

        learner.MapPost("/sessions/{sessionId}/cancel-enrollment", async (
            string sessionId,
            LiveClassCancelEnrollmentRequest? request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.CancelEnrollmentAsync(sessionId, userId, request?.Reason, ct));
        }).RequireRateLimiting("PerUser");

        learner.MapPost("/sessions/{sessionId}/join-token", async (string sessionId, HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.CreateLearnerJoinTokenAsync(sessionId, userId, ct));
        });

        learner.MapGet("/me/upcoming", async (HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.ListLearnerEnrollmentsAsync(userId, upcoming: true, ct));
        });

        learner.MapGet("/me/past", async (HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.ListLearnerEnrollmentsAsync(userId, upcoming: false, ct));
        });

        learner.MapGet("/sessions/{sessionId}/recording", async (string sessionId, HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.GetRecordingForLearnerAsync(sessionId, userId, ct));
        });

        var expert = app.MapGroup("/v1/expert/live-classes")
            .WithTags("Expert Live Classes")
            .RequireAuthorization("ExpertOnly");

        expert.MapPost("/sessions/{sessionId}/join-token", async (string sessionId, HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var expertUserId = GetRequiredUserId(http);
            return Results.Ok(await service.CreateExpertJoinTokenAsync(sessionId, expertUserId, ct));
        });

        var admin = app.MapGroup("/v1/admin/live-classes")
            .WithTags("Admin Live Classes")
            .RequireAuthorization("AdminOnly");

        admin.MapGet("", async (
            string? professionTrack,
            string? type,
            string? tutorProfileId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var query = new LiveClassListQuery(professionTrack, type, tutorProfileId, from, to, page ?? 1, pageSize ?? 50);
            return Results.Ok(await service.ListAdminClassesAsync(query, ct));
        }).WithAdminRead("AdminReviewOps");

        admin.MapGet("/analytics", async (LiveClassService service, CancellationToken ct) =>
            Results.Ok(await service.GetAnalyticsAsync(ct)))
            .WithAdminRead("AdminReviewOps");

        admin.MapGet("/{idOrSlug}", async (string idOrSlug, LiveClassService service, CancellationToken ct) =>
            Results.Ok(await service.GetAdminClassDetailAsync(idOrSlug, ct)))
            .WithAdminRead("AdminReviewOps");

        admin.MapPost("", async (AdminLiveClassUpsertRequest request, HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var created = await service.CreateAdminClassAsync(request, GetRequiredUserId(http), GetActorName(http), ct);
            return Results.Created($"/v1/admin/live-classes/{created.Id}", created);
        }).WithAdminWrite("AdminReviewOps");

        admin.MapPost("/{liveClassId}/publish", async (string liveClassId, HttpContext http, LiveClassService service, CancellationToken ct) =>
            Results.Ok(await service.PublishClassAsync(liveClassId, GetRequiredUserId(http), GetActorName(http), ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapPatch("/sessions/{sessionId}", async (
            string sessionId,
            AdminLiveClassSessionUpdateRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateSessionAsync(sessionId, request, GetRequiredUserId(http), GetActorName(http), ct)))
            .WithAdminWrite("AdminReviewOps");

        admin.MapPost("/{liveClassId}/sessions", async (
            string liveClassId,
            AdminLiveClassSessionAddRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var created = await service.AddSessionAsync(liveClassId, request, GetRequiredUserId(http), GetActorName(http), ct);
            return Results.Created($"/v1/admin/live-classes/{liveClassId}/sessions/{created.Id}", created);
        }).WithAdminWrite("AdminReviewOps");

        admin.MapPost("/sessions/{sessionId}/retry-zoom", async (
            string sessionId,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            await service.RetryZoomProvisioningAsync(sessionId, GetRequiredUserId(http), GetActorName(http), ct);
            return Results.NoContent();
        }).WithAdminWrite("AdminReviewOps");

        admin.MapPost("/sessions/{sessionId}/cancel", async (
            string sessionId,
            AdminLiveClassStatusRequest? request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            await service.CancelSessionAsync(sessionId, GetRequiredUserId(http), GetActorName(http), request?.Reason, ct);
            return Results.NoContent();
        }).WithAdminWrite("AdminReviewOps");

        app.MapPost("/v1/webhooks/zoom", async (HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            var rawBody = await reader.ReadToEndAsync(ct);
            var response = await service.HandleZoomWebhookAsync(rawBody, http.Request.Headers, ct);
            return Results.Ok(response ?? new { ok = true });
        }).WithTags("Zoom Webhooks").AllowAnonymous();

        return app;
    }

    private static string GetRequiredUserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("User identifier is missing.");

    private static string GetActorName(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
}