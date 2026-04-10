using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class PronunciationEndpoints
{
    public static IEndpointRouteBuilder MapPronunciationEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var pron = v1.MapGroup("/pronunciation");

        pron.MapGet("/profile", async (HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetProfileAsync(http.UserId(), ct)));

        pron.MapGet("/drills", async ([FromQuery] string? examTypeCode, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDrillsAsync(examTypeCode, ct)));

        pron.MapGet("/drills/{drillId}", async (string drillId, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDrillAsync(drillId, ct)));

        pron.MapPost("/drills/{drillId}/attempt", async (string drillId, HttpContext http, PronunciationAttemptRequest request, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitDrillAttemptAsync(http.UserId(), drillId, request.AudioUrl, ct)));

        pron.MapGet("/assessment/{assessmentId}", async (string assessmentId, HttpContext http, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAssessmentAsync(http.UserId(), assessmentId, ct)));

        pron.MapGet("/speaking-linked", async (HttpContext http, [FromQuery] int? limit, PronunciationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSpeakingLinkedAssessmentsAsync(http.UserId(), limit ?? 20, ct)));

        return app;
    }
}

public record PronunciationAttemptRequest(string? AudioUrl);

file static class PronunciationHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
