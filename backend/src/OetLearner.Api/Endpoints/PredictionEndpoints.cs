using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class PredictionEndpoints
{
    public static IEndpointRouteBuilder MapPredictionEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var pred = v1.MapGroup("/predictions");

        pred.MapGet("/", async (HttpContext http, [FromQuery] string? examTypeCode, PredictionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPredictionsAsync(http.UserId(), examTypeCode, ct)));

        pred.MapGet("/{examTypeCode}/{subtestCode}", async (HttpContext http, string examTypeCode, string subtestCode, PredictionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPredictionAsync(http.UserId(), examTypeCode, subtestCode, ct)));

        pred.MapPost("/compute", async (HttpContext http, ComputePredictionRequest request, PredictionService svc, CancellationToken ct) =>
            Results.Ok(await svc.ComputePredictionAsync(http.UserId(), request.ExamTypeCode, request.SubtestCode, ct)));

        return app;
    }
}

public record ComputePredictionRequest(string ExamTypeCode, string SubtestCode);

file static class PredictionHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
