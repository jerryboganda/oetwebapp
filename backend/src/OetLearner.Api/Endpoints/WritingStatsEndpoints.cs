using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingStatsEndpoints
{
    public static IEndpointRouteBuilder MapWritingStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/stats")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/dashboard", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetDashboardAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsDashboard");

        group.MapGet("/bands", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetBandsAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsBands");

        group.MapGet("/criteria", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetCriteriaAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsCriteria");

        group.MapGet("/letter-types", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetLetterTypesAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsLetterTypes");

        group.MapGet("/canon", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetCanonStatsAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsCanon");

        group.MapGet("/time", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetTimeStatsAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsTime");

        group.MapGet("/skills", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetSkillsAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsSkills");

        group.MapGet("/readiness", async (
            HttpContext http,
            IWritingReadinessService service,
            CancellationToken ct)
            => Results.Ok(await service.GetReadinessAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingReadiness");

        group.MapGet("/calendar", async (
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.GetCalendarAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingStatsCalendar");

        group.MapGet("/export", async (
            string? format,
            HttpContext http,
            IWritingAnalyticsServiceV2 service,
            CancellationToken ct)
            => Results.Ok(await service.ExportStatsAsync(http.WritingV2UserId(), format ?? "pdf", ct)))
            .WithName("ExportWritingStats");

        return app;
    }
}
