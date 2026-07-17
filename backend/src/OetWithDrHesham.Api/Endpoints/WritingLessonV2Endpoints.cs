using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// V2 lesson endpoints. Mounted at /v1/writing/v2/lessons to avoid collision
/// with the legacy slug-based /v1/writing/lessons routes in
/// WritingPathwayEndpoints (minimal API throws on duplicate path
/// registrations). Wave C will update lib/writing/api.ts to point at the V2
/// path or we'll add a proxy route once legacy is retired.
/// </summary>
public static class WritingLessonV2Endpoints
{
    public static IEndpointRouteBuilder MapWritingLessonV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/v2/lessons")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? subSkill,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.ListLessonsV2Async(http.WritingV2UserId(), subSkill, ct)))
            .WithName("ListWritingLessonsV2");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var lesson = await service.GetLessonV2Async(http.WritingV2UserId(), id, ct);
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        })
        .WithName("GetWritingLessonV2");

        group.MapPost("/{id:guid}/complete", async (
            Guid id,
            WritingLessonCompleteRequestV2 request,
            HttpContext http,
            IWritingLessonServiceV2 service,
            CancellationToken ct) =>
        {
            var completion = await service.CompleteLessonV2Async(http.WritingV2UserId(), id, request, ct);
            return completion is null ? Results.NotFound() : Results.Ok(completion);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("CompleteWritingLessonV2");

        return app;
    }
}
