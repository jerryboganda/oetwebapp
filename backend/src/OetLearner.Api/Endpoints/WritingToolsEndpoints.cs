using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingToolsEndpoints
{
    public static IEndpointRouteBuilder MapWritingToolsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/tools")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/rewrite", async (
            WritingRewriteRequest request,
            HttpContext http,
            IWritingRewriteService service,
            CancellationToken ct)
            => Results.Ok(await service.RewriteAsync(http.WritingV2UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RequestWritingRewrite");

        group.MapPost("/paraphrase", async (
            WritingParaphraseRequest request,
            HttpContext http,
            IWritingParaphraseService service,
            CancellationToken ct)
            => Results.Ok(await service.ParaphraseAsync(http.WritingV2UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RequestWritingParaphrase");

        group.MapPost("/ask", async (
            WritingAskRequest request,
            HttpContext http,
            IWritingAskService service,
            CancellationToken ct)
            => Results.Ok(await service.AskAsync(http.WritingV2UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RequestWritingAsk");

        group.MapPost("/outline", async (
            WritingOutlineRequest request,
            HttpContext http,
            IWritingOutlineService service,
            CancellationToken ct)
            => Results.Ok(await service.GenerateOutlineAsync(http.WritingV2UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("RequestWritingOutline");

        return app;
    }
}
