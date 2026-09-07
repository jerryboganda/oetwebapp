using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Mock Speaking access info (W8 result-release contract). Exposes the
/// candidate's <see cref="Domain.LearnerGoal.TargetExamDate"/> countdown
/// alongside the AI-only policy flag: human tutor review is optional
/// escalation, never a result-release dependency, so requiresAiOnly is
/// always false. The field is kept so existing clients keep parsing it.
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

            // W8: human tutor review is optional escalation, never a result-
            // release dependency. Keep the field so existing clients parse it.
            bool? requiresAiOnly = MockSpeakingAccessPolicy.RequiresAiOnly;

            return Results.Ok(new { requiresAiOnly, daysUntilExam });
        });

        return app;
    }
}

public static class MockSpeakingAccessPolicy
{
    /// <summary>W8 result-release contract: never force AI-only.</summary>
    public static bool RequiresAiOnly => false;
}

file static class MockSpeakingGatewayHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
