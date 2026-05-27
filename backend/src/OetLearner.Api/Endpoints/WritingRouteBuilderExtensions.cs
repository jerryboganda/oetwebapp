namespace OetLearner.Api.Endpoints;

/// <summary>
/// Aggregates the Writing Module V2 endpoint groups so <c>Program.cs</c>
/// can wire them with a single call: <c>app.MapWritingV2Endpoints()</c>.
///
/// Legacy V1 endpoints (<c>WritingPathwayEndpoints</c>,
/// <c>WritingCoachEndpoints</c>, <c>WritingAnalyticsAdminEndpoints</c>,
/// <c>WritingPdfEndpoints</c>) are still mapped separately in
/// <c>Program.cs</c> for backwards compatibility per the plan.
/// </summary>
public static class WritingRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWritingV2Endpoints(this IEndpointRouteBuilder app)
    {
        // Learner-facing surface
        app.MapWritingOnboardingEndpoints();
        app.MapWritingDiagnosticEndpoints();
        app.MapWritingPathwayV2Endpoints();
        app.MapWritingSubmissionEndpoints();
        app.MapWritingDraftV2Endpoints();
        app.MapWritingScenarioEndpoints();
        app.MapWritingExemplarEndpoints();
        app.MapWritingDrillV2Endpoints();
        app.MapWritingLessonV2Endpoints();
        app.MapWritingMockEndpoints();
        app.MapWritingCoachV2Endpoints();
        app.MapWritingStatsEndpoints();
        app.MapWritingCanonLibraryEndpoints();
        app.MapWritingMistakeEndpoints();
        app.MapWritingTutorReviewLearnerEndpoints();
        app.MapWritingOcrEndpoints();
        app.MapWritingShowcaseEndpoints();
        app.MapWritingToolsEndpoints();

        // Native WebSocket fallback for the Coach panel.
        app.MapWritingCoachWebSocketEndpoint();

        // Admin + tutor portals
        app.MapWritingAdminContentEndpoints();
        app.MapWritingTutorPortalEndpoints();

        return app;
    }
}
