using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var conv = v1.MapGroup("/conversations");

        conv.MapPost("/", async (HttpContext http, ConversationCreateSessionRequest request, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateSessionAsync(http.UserId(), request, ct)));

        conv.MapGet("/{sessionId}", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSessionAsync(http.UserId(), sessionId, ct)));

        conv.MapPost("/{sessionId}/complete", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.CompleteSessionAsync(http.UserId(), sessionId, ct)));

        conv.MapGet("/{sessionId}/evaluation", async (string sessionId, HttpContext http, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetEvaluationAsync(http.UserId(), sessionId, ct)));

        conv.MapGet("/history", async (HttpContext http, [FromQuery] int page, [FromQuery] int pageSize, ConversationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetHistoryAsync(http.UserId(), page <= 0 ? 1 : page, pageSize <= 0 ? 10 : pageSize, ct)));

        return app;
    }
}

file static class ConversationHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
