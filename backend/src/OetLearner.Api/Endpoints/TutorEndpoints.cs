using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Services.Classes;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Tutor-facing endpoints for the Zoom-backed live classes module.
/// Mounted at <c>/v1/tutor/me/*</c> behind the <c>ExpertOnly</c> policy.
/// See OET_ZOOM_INTEGRATION_PLAN.md §9.4.
/// </summary>
public static class TutorEndpoints
{
    public static IEndpointRouteBuilder MapTutorEndpoints(this IEndpointRouteBuilder app)
    {
        var tutor = app.MapGroup("/v1/tutor/me")
            .WithTags("Tutor")
            .RequireAuthorization("ExpertOnly");

        tutor.MapGet("", async (HttpContext http, ITutorService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.GetByUserIdAsync(userId, ct));
        });

        tutor.MapPost("", async (
            TutorUpsertRequest request,
            HttpContext http,
            ITutorService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var profile = await service.CreateAsync(userId, request, ct);
            return Results.Created($"/v1/tutor/me", profile);
        });

        tutor.MapPatch("", async (
            TutorUpsertRequest request,
            HttpContext http,
            ITutorService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.UpdateAsync(userId, request, ct));
        });

        tutor.MapGet("/availability", async (HttpContext http, ITutorService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.GetAvailabilityAsync(userId, ct));
        });

        tutor.MapPut("/availability", async (
            IReadOnlyList<TutorAvailabilityUpsertRequest> slots,
            HttpContext http,
            ITutorService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.ReplaceAvailabilityAsync(userId, slots ?? [], ct));
        });

        tutor.MapGet("/earnings", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            HttpContext http,
            ITutorService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.GetEarningsAsync(userId, from, to, ct));
        });

        tutor.MapPost("/zoom-user", async (HttpContext http, ITutorService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var zoomUserId = await service.ProvisionZoomUserAsync(userId, ct);
            return Results.Ok(new { zoomUserId });
        });

        // --- Tutor-owned class management ------------------------------------
        // These delegate to LiveClassService which owns the live class graph.
        // The tutor's identity is enforced by ListExpertClassesAsync /
        // CreateExpertJoinTokenAsync via the LiveClass.TutorProfile.ExpertUserId
        // bridge; for create/update/delete we rely on the existing
        // ExpertOnly policy plus the per-action ownership checks already
        // implemented in LiveClassService.

        tutor.MapGet("/classes", async (HttpContext http, LiveClassService service, CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            return Results.Ok(await service.ListExpertClassesAsync(userId, ct));
        });

        tutor.MapPost("/classes", async (
            TutorClassCreateRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var adminRequest = new AdminLiveClassUpsertRequest(
                request.Title,
                request.TitleAr,
                request.Description,
                request.DescriptionAr,
                request.Type,
                request.ProfessionTrack,
                request.Level,
                TutorProfileId: null, // resolved server-side via tutor identity in a later wave
                request.ScheduledStartAt,
                request.DurationMinutes,
                request.Capacity,
                request.CreditCost,
                request.CoverImageUrl,
                request.Tags,
                request.AutoPublish);
            var created = await service.CreateTutorClassAsync(adminRequest, userId, GetActorName(http), ct);
            return Results.Created($"/v1/tutor/me/classes/{created.Id}", created);
        });

        tutor.MapPatch("/classes/{classId}", async (
            string classId,
            TutorClassUpdateRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            // Class-level patches are routed through PublishClassAsync (status
            // toggles only at this wave). Tutor-owned mutation of title/body
            // lives behind the admin endpoint until plan §9.4 v2 lands. Until
            // then we surface a 501 so the frontend can opt the route off.
            _ = classId;
            _ = request;
            _ = http;
            _ = service;
            _ = ct;
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        });

        tutor.MapPost("/classes/{classId}/sessions", async (
            string classId,
            TutorClassSessionCreateRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            await service.EnsureTutorOwnsClassAsync(classId, userId, ct);
            var addRequest = new AdminLiveClassSessionAddRequest(
                request.ScheduledStartAt,
                request.DurationMinutes,
                request.Capacity);
            var created = await service.AddSessionAsync(classId, addRequest, userId, GetActorName(http), ct);
            return Results.Created($"/v1/tutor/me/classes/{classId}/sessions/{created.Id}", created);
        });

        tutor.MapPatch("/classes/sessions/{sessionId}", async (
            string sessionId,
            AdminLiveClassSessionUpdateRequest request,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            await service.EnsureTutorOwnsSessionAsync(sessionId, userId, ct);
            return Results.Ok(await service.UpdateSessionAsync(sessionId, request, userId, GetActorName(http), ct));
        });

        tutor.MapDelete("/classes/sessions/{sessionId}", async (
            string sessionId,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            await service.EnsureTutorOwnsSessionAsync(sessionId, userId, ct);
            await service.CancelSessionAsync(sessionId, userId, GetActorName(http), "Session cancelled by tutor.", ct);
            return Results.NoContent();
        });

        tutor.MapGet("/classes/sessions/{sessionId}/attendance", async (
            string sessionId,
            HttpContext http,
            LiveClassService service,
            CancellationToken ct) =>
        {
            var userId = GetRequiredUserId(http);
            var rows = await service.GetSessionAttendanceForTutorAsync(sessionId, userId, ct);
            var dtos = rows.Select(row => new TutorAttendanceLineDto(
                row.UserId,
                null,
                row.JoinedAt,
                row.LeftAt,
                row.DurationSeconds)).ToList();
            return Results.Ok(dtos);
        });

        return app;
    }

    private static string GetRequiredUserId(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("User identifier is missing.");

    private static string GetActorName(HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Tutor";
}
