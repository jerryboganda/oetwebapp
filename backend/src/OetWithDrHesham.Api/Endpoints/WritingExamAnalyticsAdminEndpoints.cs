using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Exam-faithful admin analytics endpoints (spec §16): cohort overview +
/// marking quality control. Distinct sub-paths from the legacy
/// <see cref="WritingAnalyticsAdminEndpoints"/> cohort-weakness routes under the
/// same <c>/v1/admin/writing/analytics</c> prefix. Gated by AdminOnly plus the
/// analytics-view permission.
/// </summary>
public static class WritingExamAnalyticsAdminEndpoints
{
    public static IEndpointRouteBuilder MapWritingExamAnalyticsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/v1/admin/writing/analytics")
            .WithTags("Writing Analytics (Admin)")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/overview", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromServices] IWritingAdminAnalyticsService analytics,
            CancellationToken ct) =>
        {
            var result = await analytics.GetOverviewAsync(profession, letterType, fromDate, toDate, ct);
            return Results.Ok(result);
        }).WithAdminRead("AdminQualityAnalytics");

        group.MapGet("/quality", async (
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromServices] IWritingAdminAnalyticsService analytics,
            CancellationToken ct) =>
        {
            var result = await analytics.GetMarkingQualityAsync(profession, letterType, fromDate, toDate, ct);
            return Results.Ok(result);
        }).WithAdminRead("AdminQualityAnalytics");

        return group;
    }
}
