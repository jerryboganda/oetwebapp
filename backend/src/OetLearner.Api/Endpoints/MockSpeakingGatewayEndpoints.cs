using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Full Mock Speaking 7-day AI/tutor gate (2026-07-22 owner rule). Exposes
/// the policy the Mock Speaking Gateway page reads before offering the
/// candidate an AI-only or AI-vs-tutor choice: under 7 days to the
/// candidate's <see cref="Domain.LearnerGoal.TargetExamDate"/>, only the AI
/// exam is allowed (a live-tutor booking can't reliably be arranged in
/// time); 7+ days out, either is offered.
/// </summary>
public static class MockSpeakingGatewayEndpoints
{
    public static IEndpointRouteBuilder MapMockSpeakingGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1/mocks").RequireAuthorization("LearnerOnly");

        v1.MapGet("/speaking-access", async (
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var userId = http.UserId();
            var targetExamDate = await db.Goals.AsNoTracking()
                .Where(g => g.UserId == userId)
                .Select(g => (DateOnly?)g.TargetExamDate)
                .SingleOrDefaultAsync(ct);

            int? daysUntilExam = targetExamDate is null
                ? null
                : targetExamDate.Value.DayNumber - DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).DayNumber;

            var requiresAiOnly = daysUntilExam is not null && daysUntilExam.Value < 7;

            return Results.Ok(new { requiresAiOnly, daysUntilExam });
        });

        return app;
    }
}

file static class MockSpeakingGatewayHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
