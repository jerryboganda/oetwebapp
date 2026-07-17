using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

public static class WritingShowcaseEndpoints
{
    public static IEndpointRouteBuilder MapWritingShowcaseEndpoints(this IEndpointRouteBuilder app)
    {
        var browse = app.MapGroup("/v1/writing/showcase")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        browse.MapGet("/", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingShowcaseService service,
            CancellationToken ct)
            => Results.Ok(await service.ListShowcasePostsAsync(
                http.WritingV2UserId(),
                profession,
                letterType,
                page ?? 1,
                pageSize ?? 20,
                ct)))
            .WithName("ListWritingShowcasePosts");

        var submission = app.MapGroup("/v1/writing/submissions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        submission.MapPost("/{id:guid}/showcase", async (
            Guid id,
            HttpContext http,
            IWritingShowcaseService service,
            CancellationToken ct) =>
        {
            var post = await service.PublishToShowcaseAsync(http.WritingV2UserId(), id, ct);
            return post is null ? Results.NotFound() : Results.Created($"/v1/writing/showcase/{post.Id}", post);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("PublishWritingShowcase");

        return app;
    }
}
