using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingMyWorkEndpoints
{
    /// <summary>GET /v1/writing/my-work?limit&amp;before — Post Submissions (drafts + submissions).</summary>
    public static IEndpointRouteBuilder MapWritingMyWorkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/writing/my-work", async (
                int? limit,
                DateTimeOffset? before,
                HttpContext http,
                WritingMyWorkService service,
                CancellationToken ct)
                => Results.Ok(await service.ListAsync(http.WritingV2UserId(), limit, before, ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser")
            .WithName("GetWritingMyWork");

        return app;
    }
}
