using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ExpertMessagingEndpoints
{
    public static IEndpointRouteBuilder MapExpertMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var messaging = app.MapGroup("/v1/expert/messages")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");

        messaging.MapGet("/", async (HttpContext http, ExpertMessagingService service, CancellationToken ct)
            => Results.Ok(await service.GetThreadsAsync(http.ExpertId(), ct)));

        messaging.MapPost("/", async (HttpContext http, ExpertMessagingService service, CancellationToken ct,
            [FromBody] CreateMessageThreadRequest request)
            => Results.Ok(await service.CreateThreadAsync(http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        messaging.MapGet("/{threadId}", async (string threadId, HttpContext http, ExpertMessagingService service, CancellationToken ct)
            => Results.Ok(await service.GetThreadDetailAsync(threadId, http.ExpertId(), ct)));

        messaging.MapPost("/{threadId}/replies", async (string threadId, HttpContext http, ExpertMessagingService service, CancellationToken ct,
            [FromBody] CreateMessageReplyRequest request)
            => Results.Ok(await service.PostReplyAsync(threadId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}
