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
        app.MapWritingPathwayV2Endpoints();
        app.MapWritingSubmissionEndpoints();
        app.MapWritingDraftV2Endpoints();
        app.MapWritingScenarioEndpoints();
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
        // Post-launch additions (spec §23.5 Buddy System, §33 calibration).
        app.MapWritingBuddyEndpoints();

        // Native WebSocket fallback for the Coach panel.
        app.MapWritingCoachWebSocketEndpoint();

        // Admin + tutor portals
        app.MapWritingAdminContentEndpoints();
        app.MapWritingTutorPortalEndpoints();
        // 50-letter calibration harness (spec §33) — admin-only.
        app.MapWritingCalibrationEndpoints();

        // OET exam-faithful closure (WS-B2..B5):
        //  • admin unified task builder + JSON import/export (spec §3-§6, §18)
        //  • learner attempt-event ingestion (spec §17.7)
        //  • tutor marking: annotations, content-checklist, double-marking, moderation (spec §12-§14)
        //  • learner gated feedback + rewrite comparison + result-visibility (spec §15)
        //  • admin analytics + marking quality control (spec §16)
        app.MapWritingTaskAdminEndpoints();
        app.MapWritingAttemptEventEndpoints();
        app.MapWritingMarkingEndpoints();
        app.MapWritingResultVisibilityEndpoints();
        app.MapWritingExamAnalyticsAdminEndpoints();

        return app;
    }
}
