using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

public static class LearnerEndpoints
{
    public static IEndpointRouteBuilder MapLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");

        v1.MapGet("/me", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetMeAsync(http.UserId(), ct)));
        v1.MapGet("/me/bootstrap", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetBootstrapAsync(http.UserId(), ct)));
        v1.MapGet("/freeze", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetFreezeStatusAsync(http.UserId(), ct)));
        v1.MapPost("/freeze/request", async (HttpContext http, FreezeRequestRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.RequestFreezeAsync(http.UserId(), request, ct)));
        v1.MapPost("/freeze/{freezeId}/confirm", async (HttpContext http, string freezeId, LearnerService service, CancellationToken ct) => Results.Ok(await service.ConfirmFreezeAsync(http.UserId(), freezeId, ct)));
        v1.MapPost("/freeze/{freezeId}/cancel", async (HttpContext http, string freezeId, LearnerService service, CancellationToken ct) => Results.Ok(await service.CancelFreezeAsync(http.UserId(), freezeId, ct)));

        v1.MapGet("/reference/professions", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetProfessionsAsync(ct)));
        v1.MapGet("/reference/subtests", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSubtestsAsync(ct)));
        v1.MapGet("/reference/criteria", async ([FromQuery] string? subtest, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetCriteriaAsync(subtest, ct)));
        v1.MapGet("/reference/filters/{surface}", (string surface, LearnerService service) => Results.Ok(service.GetFilters(surface)));

        var onboarding = v1.MapGroup("/learner/onboarding");
        onboarding.MapGet("/state", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetOnboardingStateAsync(http.UserId(), ct)));
        onboarding.MapPost("/start", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.StartOnboardingAsync(http.UserId(), ct)));
        onboarding.MapPost("/complete", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.CompleteOnboardingAsync(http.UserId(), ct)));

        var goals = v1.MapGroup("/learner/goals");
        goals.MapGet("/", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetGoalsAsync(http.UserId(), ct)));
        goals.MapPatch("/", async (HttpContext http, PatchGoalsRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchGoalsAsync(http.UserId(), request, ct)));
        goals.MapPost("/submit", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitGoalsAsync(http.UserId(), ct)));

        v1.MapGet("/settings", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSettingsAsync(http.UserId(), ct)));
        v1.MapGet("/settings/{section}", async (HttpContext http, string section, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSettingsSectionAsync(http.UserId(), section, ct)));
        v1.MapPatch("/settings/profile", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "profile", request, ct)));
        v1.MapPatch("/settings/goals", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "goals", request, ct)));
        v1.MapPatch("/settings/notifications", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "notifications", request, ct)));
        v1.MapPatch("/settings/privacy", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "privacy", request, ct)));
        v1.MapPatch("/settings/accessibility", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "accessibility", request, ct)));
        v1.MapPatch("/settings/audio", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "audio", request, ct)));
        v1.MapPatch("/settings/study", async (HttpContext http, PatchSectionRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.PatchSettingsSectionAsync(http.UserId(), "study", request, ct)));

        var diagnostic = v1.MapGroup("/diagnostic");
        diagnostic.MapGet("/overview", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetDiagnosticOverviewAsync(http.UserId(), ct)));
        diagnostic.MapPost("/attempts", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateOrResumeDiagnosticAsync(http.UserId(), ct)));
        diagnostic.MapGet("/attempts/{diagnosticId}", async (HttpContext http, string diagnosticId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetDiagnosticAttemptAsync(http.UserId(), diagnosticId, ct)));
        diagnostic.MapGet("/attempts/{diagnosticId}/hub", async (HttpContext http, string diagnosticId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetDiagnosticHubAsync(http.UserId(), diagnosticId, ct)));
        diagnostic.MapGet("/attempts/{diagnosticId}/results", async (HttpContext http, string diagnosticId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetDiagnosticResultsAsync(http.UserId(), diagnosticId, ct)));

        v1.MapGet("/learner/dashboard", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetDashboardAsync(http.UserId(), ct)));
        v1.MapGet("/study-plan", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetStudyPlanAsync(http.UserId(), ct)));
        v1.MapPost("/study-plan/regenerate", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.RegenerateStudyPlanAsync(http.UserId(), ct)));
        v1.MapPost("/study-plan/items/{itemId}/complete", async (HttpContext http, string itemId, LearnerService service, CancellationToken ct) => Results.Ok(await service.CompleteStudyPlanItemAsync(http.UserId(), itemId, ct)));
        v1.MapPost("/study-plan/items/{itemId}/skip", async (HttpContext http, string itemId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SkipStudyPlanItemAsync(http.UserId(), itemId, ct)));
        v1.MapPost("/study-plan/items/{itemId}/reset", async (HttpContext http, string itemId, LearnerService service, CancellationToken ct) => Results.Ok(await service.ResetStudyPlanItemAsync(http.UserId(), itemId, ct)));
        v1.MapPost("/study-plan/items/{itemId}/reschedule", async (HttpContext http, string itemId, StudyPlanRescheduleRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.RescheduleStudyPlanItemAsync(http.UserId(), itemId, request, ct)));
        v1.MapPost("/study-plan/items/{itemId}/swap", async (HttpContext http, string itemId, StudyPlanSwapRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.SwapStudyPlanItemAsync(http.UserId(), itemId, request, ct)));
        v1.MapGet("/readiness", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadinessAsync(http.UserId(), ct)));
        v1.MapGet("/progress", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetProgressAsync(http.UserId(), ct)));
        v1.MapGet("/submissions", async (HttpContext http, [FromQuery] string? cursor, [FromQuery] int? limit, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSubmissionsAsync(http.UserId(), cursor, limit, ct)));
        v1.MapGet("/submissions/compare", async (HttpContext http, [FromQuery] string? leftId, [FromQuery] string? rightId, LearnerService service, CancellationToken ct) => Results.Ok(await service.CompareSubmissionsAsync(http.UserId(), leftId, rightId, ct)));

        var writing = v1.MapGroup("/writing");
        writing.MapGet("/home", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingHomeAsync(http.UserId(), ct)));
        writing.MapGet("/tasks", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingTasksAsync(ct)));
        writing.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingTaskAsync(contentId, ct)));
        writing.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateWritingAttemptAsync(http.UserId(), request, ct)));
        writing.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingAttemptAsync(http.UserId(), attemptId, ct)));
        writing.MapPatch("/attempts/{attemptId}/draft", async (HttpContext http, string attemptId, DraftUpdateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.UpdateWritingDraftAsync(http.UserId(), attemptId, request, ct)));
        writing.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        writing.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, SubmitAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitWritingAttemptAsync(http.UserId(), attemptId, request, ct)));
        writing.MapGet("/evaluations/{evaluationId}/summary", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingEvaluationSummaryAsync(http.UserId(), evaluationId, ct)));
        writing.MapGet("/evaluations/{evaluationId}/feedback", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingFeedbackAsync(http.UserId(), evaluationId, ct)));
        writing.MapGet("/revisions/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingRevisionAsync(http.UserId(), attemptId, ct)));
        writing.MapPost("/revisions/{attemptId}/submit", async (HttpContext http, string attemptId, RevisionSubmitRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitWritingRevisionAsync(http.UserId(), attemptId, request, ct)));
        writing.MapGet("/content/{contentId}/model-answer", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWritingModelAnswerAsync(contentId, ct)));

        var speaking = v1.MapGroup("/speaking");
        speaking.MapGet("/home", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingHomeAsync(http.UserId(), ct)));
        speaking.MapGet("/tasks", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingTasksAsync(ct)));
        speaking.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingTaskAsync(contentId, ct)));
        speaking.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateSpeakingAttemptAsync(http.UserId(), request, ct)));
        speaking.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingAttemptAsync(http.UserId(), attemptId, ct)));
        speaking.MapPost("/attempts/{attemptId}/audio/upload-session", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateSpeakingUploadSessionAsync(http.UserId(), attemptId, ct)));
        speaking.MapPut("/upload-sessions/{uploadSessionId}/content", async (HttpContext http, string uploadSessionId, LearnerService service, CancellationToken ct) => Results.Ok(await service.UploadSpeakingAudioAsync(http.UserId(), uploadSessionId, http.Request.Body, http.Request.ContentType, ct)));
        speaking.MapPost("/attempts/{attemptId}/audio/complete", async (HttpContext http, string attemptId, UploadCompleteRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CompleteSpeakingUploadAsync(http.UserId(), attemptId, request, ct)));
        speaking.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        speaking.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitSpeakingAttemptAsync(http.UserId(), attemptId, ct)));
        speaking.MapGet("/attempts/{attemptId}/processing", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingProcessingAsync(http.UserId(), attemptId, ct)));
        speaking.MapGet("/evaluations/{evaluationId}/summary", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingEvaluationSummaryAsync(http.UserId(), evaluationId, ct)));
        speaking.MapGet("/evaluations/{evaluationId}/review", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSpeakingReviewAsync(http.UserId(), evaluationId, ct)));
        speaking.MapGet("/evaluations/{evaluationId}/audio", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) =>
        {
            var file = await service.GetSpeakingEvaluationAudioAsync(http.UserId(), evaluationId, ct);
            return Results.File(file.Stream, file.ContentType, enableRangeProcessing: true);
        });
        speaking.MapPost("/device-checks", async (HttpContext http, DeviceCheckRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.SaveDeviceCheckAsync(http.UserId(), request, ct)));

        // Wave 3 of docs/SPEAKING-MODULE-PLAN.md - speaking mock sets
        // (two role-plays attempted as one mock). The free-tier rolling
        // 7-day cap is enforced server-side by StartSpeakingMockSetAsync.
        speaking.MapGet("/mock-sets", async (HttpContext http, LearnerService service, CancellationToken ct) =>
            Results.Ok(await service.ListSpeakingMockSetsAsync(http.UserId(), ct)));
        speaking.MapPost("/mock-sets/{mockSetId}/start", async (HttpContext http, string mockSetId, StartSpeakingMockSetRequest? body, LearnerService service, CancellationToken ct) =>
            Results.Ok(await service.StartSpeakingMockSetAsync(http.UserId(), mockSetId, body?.Mode ?? "exam", ct)));
        speaking.MapGet("/mock-sessions/{sessionId}", async (HttpContext http, string sessionId, LearnerService service, CancellationToken ct) =>
            Results.Ok(await service.GetSpeakingMockSessionAsync(http.UserId(), sessionId, ct)));

        // Wave 5 of docs/SPEAKING-MODULE-PLAN.md - deep-link from a
        // speaking task into the AI-patient Conversation module so the
        // learner can practise the same scenario unlimited times. Reuses
        // ConversationService end-to-end — no new AI provider, no new
        // grounding code. Free-tier caps come from
        // IConversationEntitlementService inside CreateSessionAsync.
        speaking.MapPost("/tasks/{contentId}/self-practice", async (
            HttpContext http, string contentId,
            LearnerService service, ConversationService conversation, CancellationToken ct) =>
            Results.Ok(await service.StartSpeakingSelfPracticeAsync(http.UserId(), contentId, conversation, ct)));

        // Wave 6 of docs/SPEAKING-MODULE-PLAN.md - speaking drills bank.
        // Filterable by drill kind / profession / criterion focus.
        speaking.MapGet("/drills", async (
            HttpContext http,
            string? kind, string? profession, string? criterion,
            LearnerService service, CancellationToken ct) =>
            Results.Ok(await service.ListSpeakingDrillsAsync(http.UserId(), kind, profession, criterion, ct)));

        // Wave 7 of docs/SPEAKING-MODULE-PLAN.md - learner-facing
        // compliance copy (consent text + score disclaimer + retention
        // window). Driven by SpeakingComplianceOptions so operators can
        // tune wording and retention without code changes.
        speaking.MapGet("/compliance", (
            Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.SpeakingComplianceOptions> opts) =>
        {
            var o = opts.Value;
            return Results.Ok(new
            {
                consentText = o.ConsentText,
                scoreDisclaimer = o.ScoreDisclaimer,
                audioRetentionDays = o.AudioRetentionDays,
            });
        });

        var reading = v1.MapGroup("/reading");
        reading.MapGet("/home", async (
            HttpContext http,
            LearnerDbContext db,
            IReadingPolicyService policy,
            IContentEntitlementService contentEntitlements,
            CancellationToken ct) =>
            Results.Ok(await GetStructuredReadingHomeAsync(http.UserId(), db, policy, contentEntitlements, ct)));
        reading.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingTaskAsync(contentId, ct)));
        reading.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateReadingAttemptAsync(http.UserId(), request, ct)));
        reading.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingAttemptAsync(http.UserId(), attemptId, ct)));
        reading.MapPatch("/attempts/{attemptId}/answers", async (HttpContext http, string attemptId, AnswersUpdateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.UpdateReadingAnswersAsync(http.UserId(), attemptId, request, ct)));
        reading.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        reading.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitReadingAttemptAsync(http.UserId(), attemptId, ct)));
        reading.MapGet("/evaluations/{evaluationId}", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingEvaluationAsync(http.UserId(), evaluationId, ct)));

        var listening = v1.MapGroup("/listening");
        listening.MapGet("/home", async (HttpContext http, ListeningLearnerService service, CancellationToken ct) => Results.Ok(await service.GetHomeAsync(http.UserId(), ct)));
        listening.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningTaskAsync(contentId, ct)));
        listening.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateListeningAttemptAsync(http.UserId(), request, ct)));
        listening.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningAttemptAsync(http.UserId(), attemptId, ct)));
        listening.MapPatch("/attempts/{attemptId}/answers", async (HttpContext http, string attemptId, AnswersUpdateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.UpdateListeningAnswersAsync(http.UserId(), attemptId, request, ct)));
        listening.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        listening.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitListeningAttemptAsync(http.UserId(), attemptId, ct)));
        listening.MapGet("/evaluations/{evaluationId}", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningEvaluationAsync(http.UserId(), evaluationId, ct)));
        listening.MapGet("/drills/{drillId}", async (string drillId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningDrillAsync(drillId, ct)));

        v1.MapGet("/mocks", async (HttpContext http, MockService service, CancellationToken ct) => Results.Ok(await service.GetMocksAsync(http.UserId(), ct)));
        v1.MapGet("/mocks/options", async (HttpContext http, MockService service, CancellationToken ct) => Results.Ok(await service.GetMockOptionsAsync(http.UserId(), ct)));
        v1.MapPost("/mock-attempts", async (HttpContext http, MockAttemptCreateRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.CreateMockAttemptAsync(http.UserId(), request, ct)));
        v1.MapGet("/mock-attempts/{mockAttemptId}", async (HttpContext http, string mockAttemptId, MockService service, CancellationToken ct) => Results.Ok(await service.GetMockAttemptAsync(http.UserId(), mockAttemptId, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/sections/{sectionId}/start", async (HttpContext http, string mockAttemptId, string sectionId, MockSectionStartRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.StartMockSectionAsync(http.UserId(), mockAttemptId, sectionId, request, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/sections/{sectionId}/complete", async (HttpContext http, string mockAttemptId, string sectionId, MockSectionCompleteRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.CompleteMockSectionAsync(http.UserId(), mockAttemptId, sectionId, request, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/submit", async (HttpContext http, string mockAttemptId, MockService service, CancellationToken ct) => Results.Ok(await service.SubmitMockAttemptAsync(http.UserId(), mockAttemptId, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/cancel", async (HttpContext http, string mockAttemptId, MockService service, CancellationToken ct) => Results.Ok(await service.CancelMockAttemptAsync(http.UserId(), mockAttemptId, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/proctoring-events", async (HttpContext http, string mockAttemptId, MockProctoringEventBatchRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.RecordProctoringEventsAsync(http.UserId(), mockAttemptId, request, ct)));
        v1.MapGet("/mock-reports/{reportId}", async (HttpContext http, string reportId, MockService service, CancellationToken ct) => Results.Ok(await service.GetMockReportAsync(http.UserId(), reportId, ct)));

        // Mocks V2 Wave 4 — booking
        v1.MapGet("/mock-bookings", async (HttpContext http, MockBookingService bookings, CancellationToken ct) => Results.Ok(await bookings.ListForUserAsync(http.UserId(), ct)));
        v1.MapPost("/mock-bookings", async (HttpContext http, MockBookingCreateRequest request, MockBookingService bookings, CancellationToken ct) => Results.Ok(await bookings.CreateAsync(http.UserId(), request, ct)));
        v1.MapPatch("/mock-bookings/{bookingId}/reschedule", async (HttpContext http, string bookingId, MockBookingRescheduleRequest request, MockBookingService bookings, CancellationToken ct) => Results.Ok(await bookings.RescheduleAsync(http.UserId(), bookingId, request, ct)));
        v1.MapPatch("/mock-bookings/{bookingId}", async (HttpContext http, string bookingId, MockBookingUpdateRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.UpdateMockBookingAsync(http.UserId(), bookingId, request, ct)));
        v1.MapPost("/mock-bookings/{bookingId}/cancel", async (HttpContext http, string bookingId, MockBookingService bookings, CancellationToken ct) => Results.Ok(await bookings.CancelAsync(http.UserId(), bookingId, ct)));
        // Mocks V2 Wave 6 — live-room state transitions (audio-only Speaking room).
        v1.MapPost("/mock-bookings/{bookingId}/live-room/transition", async (HttpContext http, string bookingId, LiveRoomTransitionRequest request, MockBookingService bookings, CancellationToken ct) => Results.Ok(await bookings.TransitionLiveRoomAsync(http.UserId(), isAdmin: false, bookingId, request.TargetState, ct)));

        // Mocks V2 Wave 5 — 7-day remediation plan from MockReport
        v1.MapGet("/mocks/remediation-plan", async (HttpContext http, RemediationPlanService plan, CancellationToken ct) => Results.Ok(await plan.GetActivePlanAsync(http.UserId(), ct)));
        v1.MapPost("/mocks/reports/{reportId}/remediation-plan/generate", async (HttpContext http, string reportId, RemediationPlanService plan, CancellationToken ct) => Results.Ok(await plan.GenerateFromReportAsync(http.UserId(), reportId, ct)));
        v1.MapPatch("/remediation-tasks/{taskId}/complete", async (HttpContext http, string taskId, RemediationPlanService plan, CancellationToken ct) => Results.Ok(await plan.CompleteTaskAsync(http.UserId(), taskId, ct)));

        // Mocks V2 Wave 7 — Diagnostic-mock entitlement check.
        v1.MapGet("/mocks/diagnostic/entitlement", async (HttpContext http, MockDiagnosticEntitlementService gate, CancellationToken ct) =>
        {
            var decision = await gate.CanStartDiagnosticAsync(http.UserId(), ct);
            return Results.Ok(new
            {
                allowed = decision.Allowed,
                entitlement = decision.Entitlement,
                reason = decision.Reason,
                message = decision.Message,
            });
        });
        v1.MapPost("/mocks/leak-report", async (HttpContext http, MockLeakReportRequest request, MockService service, CancellationToken ct) => Results.Ok(await service.ReportMockLeakAsync(http.UserId(), request, ct)));
        v1.MapGet("/mocks/diagnostic/study-path", async (HttpContext http, MockService service, CancellationToken ct) => Results.Ok(await service.GetDiagnosticStudyPathAsync(http.UserId(), ct)));

        var reviews = v1.MapGroup("/reviews");
        reviews.MapGet("/", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReviewsAsync(http.UserId(), ct)));
        reviews.MapGet("/eligibility", async (HttpContext http, [FromQuery] string? attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReviewEligibilityAsync(http.UserId(), attemptId, ct)));
        reviews.MapPost("/requests", async (HttpContext http, ReviewRequestCreateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateReviewRequestAsync(http.UserId(), request, ct)));
        reviews.MapGet("/requests/{reviewRequestId}", async (HttpContext http, string reviewRequestId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReviewRequestAsync(http.UserId(), reviewRequestId, ct)));

        var billing = v1.MapGroup("/billing");
        billing.MapGet("/summary", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetBillingSummaryAsync(http.UserId(), ct)));
        billing.MapGet("/plans", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetBillingPlansAsync(http.UserId(), ct)));
        billing.MapGet("/change-preview", async (HttpContext http, [FromQuery] string targetPlanId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetBillingChangePreviewAsync(http.UserId(), targetPlanId, ct)));
        billing.MapGet("/quote", async (HttpContext http,
            [FromQuery] string productType,
            [FromQuery] int quantity,
            [FromQuery] string? priceId,
            [FromQuery] string? couponCode,
            [FromQuery] string? addOnCodes,
            LearnerService service,
            CancellationToken ct)
            => Results.Ok(await service.GetBillingQuoteAsync(http.UserId(), new BillingQuoteRequest(
                productType,
                quantity,
                priceId,
                couponCode,
                string.IsNullOrWhiteSpace(addOnCodes)
                    ? null
                    : addOnCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()), ct)));
        billing.MapGet("/invoices", async (HttpContext http, [FromQuery] string? cursor, [FromQuery] int? limit, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetInvoicesAsync(http.UserId(), cursor, limit, ct)));
        billing.MapGet("/invoices/{invoiceId}/download", async (HttpContext http, string invoiceId, LearnerService service, CancellationToken ct) =>
        {
            var file = await service.GetInvoiceDownloadAsync(http.UserId(), invoiceId, ct);
            return Results.File(file.Stream, file.ContentType, fileDownloadName: file.FileName);
        });
        billing.MapGet("/review-options", (LearnerService service) => Results.Ok(service.GetReviewOptions()));
        billing.MapGet("/extras", async (LearnerService service) => Results.Ok(await service.GetBillingExtrasAsync()));
        billing.MapPost("/checkout-sessions", async (HttpContext http, CheckoutSessionCreateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateCheckoutSessionAsync(http.UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Engagement endpoints
        v1.MapGet("/learner/engagement", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetEngagementAsync(http.UserId(), ct)));

        // Wallet endpoints
        billing.MapGet("/wallet/transactions", async (HttpContext http, [FromQuery] int? limit, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWalletTransactionsAsync(http.UserId(), limit ?? 20, ct)));
        billing.MapPost("/wallet/top-up", async (HttpContext http, WalletTopUpRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateWalletTopUpAsync(http.UserId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");
        billing.MapGet("/wallet/top-up-tiers", (LearnerService service) => Results.Ok(service.GetWalletTopUpTiers()));

        // Payment webhook endpoints (no auth required)
        var webhooks = app.MapGroup("/v1/payment/webhooks");
        webhooks.MapPost("/stripe", async (HttpContext http, LearnerService service, CancellationToken ct) =>
        {
            var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
            var headers = http.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var outcome = await service.HandleStripeWebhookAsync(payload, headers, ct);
            return LearnerService.IsRejectedWebhookOutcome(outcome)
                ? Results.StatusCode(StatusCodes.Status400BadRequest)
                : Results.Ok(outcome);
        });
        webhooks.MapPost("/paypal", async (HttpContext http, LearnerService service, CancellationToken ct) =>
        {
            var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
            var headers = http.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var outcome = await service.HandlePayPalWebhookAsync(payload, headers, ct);
            return LearnerService.IsRejectedWebhookOutcome(outcome)
                ? Results.StatusCode(StatusCodes.Status400BadRequest)
                : Results.Ok(outcome);
        });

        // Exam family reference
        v1.MapGet("/reference/exam-families", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetExamFamiliesAsync(ct)));

        // Target-date risk assessment
        v1.MapGet("/learner/readiness/risk", async (HttpContext http, EngagementService engagement, CancellationToken ct) => Results.Ok(await engagement.CalculateTargetDateRiskAsync(http.UserId(), ct)));

        // Streak freeze
        v1.MapPost("/learner/engagement/streak-freeze", async (HttpContext http, EngagementService engagement, CancellationToken ct) =>
        {
            var result = await engagement.UseStreakFreezeAsync(http.UserId(), ct);
            return Results.Ok(new { applied = result, message = result ? "Streak freeze applied." : "No streak freeze needed or available." });
        });

        // ── Score Guarantee ─────────────────────────────

        v1.MapGet("/learner/score-guarantee", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetScoreGuaranteeAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/score-guarantee/activate", async (HttpContext http, ScoreGuaranteeActivateRequest request, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.ActivateScoreGuaranteeAsync(http.UserId(), request, ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUserWrite");

        v1.MapPost("/learner/score-guarantee/claim", async (HttpContext http, ScoreGuaranteeClaimRequest request, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.SubmitScoreGuaranteeClaimAsync(http.UserId(), request, ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUserWrite");

        // ── Score Cross-Reference Calculator ────────────

        v1.MapGet("/reference/score-equivalences", async (LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetScoreEquivalencesAsync(ct)));

        // ── Study Commitment ────────────────────────────

        v1.MapGet("/learner/study-commitment", async (HttpContext http, GamificationService gamification, CancellationToken ct)
            => Results.Ok(await gamification.GetStudyCommitmentAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/study-commitment", async (HttpContext http, StudyCommitmentRequest request, GamificationService gamification, CancellationToken ct)
            => Results.Ok(await gamification.SetStudyCommitmentAsync(http.UserId(), request, ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUserWrite");

        // ── Certificates ────────────────────────────────

        v1.MapGet("/learner/certificates", async (HttpContext http, GamificationService gamification, CancellationToken ct)
            => Results.Ok(await gamification.GetCertificatesAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── Referral Program ────────────────────────────

        v1.MapGet("/learner/referral", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetReferralInfoAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/referral/generate", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GenerateReferralCodeAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUserWrite");

        // ── L3: Profession-Specific Learning Paths ──────

        v1.MapGet("/learner/learning-path", async (HttpContext http, [FromQuery] string? professionId, [FromQuery] string? examTypeCode, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetLearningPathAsync(http.UserId(), professionId, examTypeCode ?? "oet", ct)))
            .RequireAuthorization("LearnerOnly");

        // ── L5: Adaptive Weak-Area Remediation ──────────

        v1.MapGet("/learner/remediation", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetRemediationProfileAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/remediation/start", async (HttpContext http, RemediationStartRequest request, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.StartRemediationSessionAsync(http.UserId(), request.SubtestCode, request.CriterionCode, ct)))
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUserWrite");

        // ── E1: Smart Next Best Action ──────────────────

        v1.MapGet("/learner/next-actions", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetNextBestActionsAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E2: Diagnostic Post-Personalization ─────────

        v1.MapGet("/learner/diagnostic-personalization", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetDiagnosticPersonalizationAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E4: Speaking Fluency Timeline ───────────────

        v1.MapGet("/learner/speaking/{attemptId}/fluency-timeline", async (string attemptId, HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetFluencyTimelineAsync(http.UserId(), attemptId, ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E5: Comparative Analytics ───────────────────

        v1.MapGet("/learner/comparative-analytics", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetComparativeAnalyticsAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E6: Exam Simulation Mode ────────────────────

        v1.MapGet("/learner/exam-simulation-config", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetExamSimulationConfigAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E9: Study Plan Drift Detection ──────────────

        v1.MapGet("/learner/study-plan/drift", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.DetectStudyPlanDriftAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── E10: Billing Upgrade Path ───────────────────

        v1.MapGet("/learner/billing/upgrade-path", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetBillingUpgradePathAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        // ── L9: Interleaved Practice Session ────────────

        v1.MapGet("/learner/interleaved-practice", async (HttpContext http, [FromQuery] int? durationMinutes, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetInterleavedPracticeSessionAsync(http.UserId(), durationMinutes ?? 20, ct)))
            .RequireAuthorization("LearnerOnly");

        // ── L12: Peer Review Exchange ───────────────────

        v1.MapGet("/learner/peer-reviews", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetPeerReviewPoolAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/peer-reviews/submit", async (HttpContext http, PeerReviewSubmitRequest req, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.SubmitForPeerReviewAsync(http.UserId(), req.AttemptId, req.SubtestCode, ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/peer-reviews/{peerReviewId}/claim", async (string peerReviewId, HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.ClaimPeerReviewAsync(http.UserId(), peerReviewId, ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapPost("/learner/peer-reviews/{peerReviewId}/feedback", async (string peerReviewId, HttpContext http, PeerReviewFeedbackRequest req, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.SubmitPeerFeedbackAsync(http.UserId(), peerReviewId, req.OverallRating, req.Comments, req.Strengths, req.Improvements, ct)))
            .RequireAuthorization("LearnerOnly");

        // ── Learner Escalation / Dispute ────────────────

        v1.MapPost("/learner/escalations", async (HttpContext http, LearnerEscalationSubmitRequest req, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.SubmitEscalationAsync(http.UserId(), req.SubmissionId, req.Reason, req.Details, ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapGet("/learner/escalations", async (HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetMyEscalationsAsync(http.UserId(), ct)))
            .RequireAuthorization("LearnerOnly");

        v1.MapGet("/learner/escalations/{escalationId}", async (string escalationId, HttpContext http, LearnerService service, CancellationToken ct)
            => Results.Ok(await service.GetEscalationDetailsAsync(http.UserId(), escalationId, ct)))
            .RequireAuthorization("LearnerOnly");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");

    private static async Task<object> GetStructuredReadingHomeAsync(
        string userId,
        LearnerDbContext db,
        IReadingPolicyService policyService,
        IContentEntitlementService contentEntitlements,
        CancellationToken ct)
    {
        var policy = await policyService.ResolveForUserAsync(userId, ct);
        var publishedReadingPapers = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published
                && p.SubtestCode.ToLower() == "reading")
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Title)
            .ToListAsync(ct);

        var readyPapers = new List<ContentPaper>();
        var structureService = new ReadingStructureService(db);
        foreach (var paper in publishedReadingPapers)
        {
            var validation = await structureService.ValidatePaperAsync(paper.Id, ct);
            if (validation.IsPublishReady) readyPapers.Add(paper);
        }

        var accessByPaper = new Dictionary<string, ContentEntitlementResult>(StringComparer.Ordinal);
        foreach (var paper in readyPapers)
        {
            accessByPaper[paper.Id] = await contentEntitlements.AllowAccessAsync(userId, paper, ct);
        }

        var accessibleReadyPapers = readyPapers
            .Where(p => accessByPaper.TryGetValue(p.Id, out var access) && access.Allowed)
            .ToList();
        var paperIds = readyPapers.Select(p => p.Id).ToList();
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => paperIds.Contains(p.PaperId))
            .Include(p => p.Questions)
            .ToListAsync(ct);
        var partsByPaper = parts.GroupBy(p => p.PaperId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var attempts = await db.ReadingAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .Where(a => a.UserId == userId && paperIds.Contains(a.PaperId))
            .OrderByDescending(a => a.LastActivityAt)
            .ToListAsync(ct);
        var attemptsByPaper = attempts.GroupBy(a => a.PaperId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var paperTitles = readyPapers.ToDictionary(p => p.Id, p => p.Title);
        var safeDrills = BuildReadingSafeDrills(accessibleReadyPapers, attempts, partsByPaper, paperTitles, policy);
        var activeAttempts = attempts
            .Where(a => a.Status == ReadingAttemptStatus.InProgress)
            .Take(3)
            .Select(a =>
            {
                var snapshot = ResolveReadingPolicySnapshot(a.PolicySnapshotJson, policy);
                var scopeQuestionIds = ParseReadingScopeQuestionIds(a.ScopeJson);
                var totalQuestions = partsByPaper.TryGetValue(a.PaperId, out var paperParts)
                    ? scopeQuestionIds?.Count ?? paperParts.Sum(p => p.Questions.Count)
                    : 0;
                var (partADeadlineAt, partBCDeadlineAt) = ResolveReadingAttemptDeadlines(a, snapshot);
                return new
                {
                    attemptId = a.Id,
                    paperId = a.PaperId,
                    paperTitle = paperTitles.GetValueOrDefault(a.PaperId, "Reading paper"),
                    status = a.Status.ToString(),
                    mode = a.Mode.ToString(),
                    a.StartedAt,
                    a.DeadlineAt,
                    partADeadlineAt,
                    partBCDeadlineAt,
                    answeredCount = a.Answers.Count,
                    totalQuestions,
                    canResume = a.DeadlineAt is null || a.DeadlineAt >= DateTimeOffset.UtcNow || snapshot.AllowResumeAfterExpiry,
                    route = $"/reading/paper/{a.PaperId}?attemptId={a.Id}",
                };
            })
            .ToList();

        var recentResults = attempts
            .Where(a => a.Status == ReadingAttemptStatus.Submitted)
            .Where(IsCanonicalReadingScoreAttempt)
            .OrderByDescending(a => a.SubmittedAt)
            .Take(5)
            .Select(a => new
            {
                attemptId = a.Id,
                paperId = a.PaperId,
                paperTitle = paperTitles.GetValueOrDefault(a.PaperId, "Reading paper"),
                rawScore = a.RawScore ?? 0,
                maxRawScore = a.MaxRawScore,
                scaledScore = a.ScaledScore ?? OetScoring.OetRawToScaled(a.RawScore ?? 0),
                gradeLetter = OetScoring.OetGradeLetterFromScaled(
                    a.ScaledScore ?? OetScoring.OetRawToScaled(a.RawScore ?? 0)),
                a.SubmittedAt,
                route = $"/reading/paper/{a.PaperId}/results?attemptId={a.Id}",
            })
            .ToList();

        var papers = new List<object>();
        foreach (var p in readyPapers)
        {
            partsByPaper.TryGetValue(p.Id, out var paperParts);
            paperParts ??= new List<ReadingPart>();
            var lastAttempt = attemptsByPaper.TryGetValue(p.Id, out var paperAttempts)
                ? paperAttempts
                    .Where(a => a.Status == ReadingAttemptStatus.Submitted)
                    .Where(IsCanonicalReadingScoreAttempt)
                    .OrderByDescending(a => a.SubmittedAt ?? a.LastActivityAt)
                    .FirstOrDefault()
                : null;
            var access = accessByPaper.TryGetValue(p.Id, out var resolvedAccess)
                ? resolvedAccess
                : new ContentEntitlementResult(false, "plan_does_not_grant", null, $"subtest:{p.SubtestCode}");
            papers.Add(new
            {
                id = p.Id,
                p.Title,
                p.Slug,
                p.Difficulty,
                p.EstimatedDurationMinutes,
                p.PublishedAt,
                route = $"/reading/paper/{p.Id}",
                partACount = CountPartQuestions(paperParts, ReadingPartCode.A),
                partBCount = CountPartQuestions(paperParts, ReadingPartCode.B),
                partCCount = CountPartQuestions(paperParts, ReadingPartCode.C),
                totalPoints = paperParts.Sum(part => part.Questions.Sum(q => q.Points)),
                partATimerMinutes = policy.PartATimerMinutes,
                partBCTimerMinutes = policy.PartBCTimerMinutes,
                entitlement = new
                {
                    allowed = access.Allowed,
                    reason = access.Reason,
                    currentTier = access.CurrentTier,
                    requiredScope = access.RequiredScope,
                },
                lastAttempt = lastAttempt is null ? null : new
                {
                    attemptId = lastAttempt.Id,
                    status = lastAttempt.Status.ToString(),
                    lastAttempt.StartedAt,
                    lastAttempt.SubmittedAt,
                    rawScore = lastAttempt.RawScore,
                    scaledScore = lastAttempt.ScaledScore,
                    route = lastAttempt.Status == ReadingAttemptStatus.Submitted
                        ? $"/reading/paper/{p.Id}/results?attemptId={lastAttempt.Id}"
                        : $"/reading/paper/{p.Id}?attemptId={lastAttempt.Id}",
                },
            });
        }

        return new
        {
            intro = "Build Reading accuracy with full structured OET papers before validating it in mocks.",
            papers,
            activeAttempts,
            recentResults,
            policy = new
            {
                policy.PartATimerMinutes,
                policy.PartBCTimerMinutes,
                policy.AllowPausingAttempt,
                policy.AllowResumeAfterExpiry,
                policy.ShowCorrectAnswerOnReview,
                policy.ShowExplanationsAfterSubmit,
            },
            safeDrills,
        };
    }

    private static IReadOnlyList<object> BuildReadingSafeDrills(
        IReadOnlyList<ContentPaper> readyPapers,
        IReadOnlyList<ReadingAttempt> attempts,
        IReadOnlyDictionary<string, List<ReadingPart>> partsByPaper,
        IReadOnlyDictionary<string, string> paperTitles,
        ReadingResolvedPolicy policy)
    {
        var actions = new List<object>();
        var latestSubmitted = attempts
            .Where(a => a.Status == ReadingAttemptStatus.Submitted)
            .Where(IsCanonicalReadingScoreAttempt)
            .OrderByDescending(a => a.SubmittedAt)
            .FirstOrDefault();

        if (latestSubmitted is not null && partsByPaper.TryGetValue(latestSubmitted.PaperId, out var latestParts))
        {
            var answersByQuestion = latestSubmitted.Answers
                .GroupBy(a => a.ReadingQuestionId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AnsweredAt).First());
            var partLosses = latestParts
                .Select(part =>
                {
                    var max = part.Questions.Sum(q => q.Points);
                    var earned = part.Questions.Sum(q =>
                        answersByQuestion.TryGetValue(q.Id, out var answer) ? answer.PointsEarned : 0);
                    var unanswered = part.Questions.Count(q => !answersByQuestion.ContainsKey(q.Id));
                    return new
                    {
                        part.PartCode,
                        Max = max,
                        Earned = earned,
                        Lost = Math.Max(0, max - earned),
                        Unanswered = unanswered,
                    };
                })
                .Where(p => p.Lost > 0)
                .OrderByDescending(p => p.Lost)
                .ThenByDescending(p => p.Unanswered)
                .ToList();

            var weakestPart = partLosses.FirstOrDefault();
            if (weakestPart is not null)
            {
                var partMinutes = weakestPart.PartCode == ReadingPartCode.A
                    ? policy.PartATimerMinutes
                    : Math.Max(15, policy.PartBCTimerMinutes / 2);
                actions.Add(new
                {
                    id = $"review-{latestSubmitted.Id}-part-{weakestPart.PartCode}",
                    title = $"Repair Part {weakestPart.PartCode} score loss",
                    description = $"Review the attempt section where the most Reading marks were lost before starting another full paper.",
                    focusLabel = $"Part {weakestPart.PartCode}",
                    estimatedMinutes = partMinutes,
                    launchRoute = $"/reading/paper/{latestSubmitted.PaperId}/results?attemptId={latestSubmitted.Id}#part-breakdown",
                    highlights = new[]
                    {
                        $"{weakestPart.Earned}/{weakestPart.Max} marks in Part {weakestPart.PartCode}",
                        weakestPart.Unanswered > 0
                            ? $"{weakestPart.Unanswered} unanswered item(s) to review"
                            : $"{weakestPart.Lost} mark(s) lost to incorrect answers",
                    },
                });
            }

            var questionLookup = latestParts
                .SelectMany(part => part.Questions.Select(q => new
                {
                    q.Id,
                    Label = string.IsNullOrWhiteSpace(q.SkillTag) ? q.QuestionType.ToString() : q.SkillTag,
                }))
                .ToDictionary(q => q.Id, q => q.Label);
            var weakestSkillCandidate = latestParts
                .SelectMany(part => part.Questions)
                .Where(q => !answersByQuestion.TryGetValue(q.Id, out var answer) || answer.IsCorrect != true)
                .Select(q => questionLookup.TryGetValue(q.Id, out var label) ? label : null)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .GroupBy(label => label!)
                .Select(g => new { Label = g.Key, Misses = g.Count() })
                .OrderByDescending(g => g.Misses)
                .ThenBy(g => g.Label)
                .FirstOrDefault();

            if (weakestSkillCandidate is not null)
            {
                actions.Add(new
                {
                    id = $"review-{latestSubmitted.Id}-skill-{SanitiseActionId(weakestSkillCandidate.Label)}",
                    title = $"Target {weakestSkillCandidate.Label}",
                    description = "Use the skill breakdown to revisit the exact item patterns that cost marks.",
                    focusLabel = weakestSkillCandidate.Label,
                    estimatedMinutes = 12,
                    launchRoute = $"/reading/paper/{latestSubmitted.PaperId}/results?attemptId={latestSubmitted.Id}#skill-breakdown",
                    highlights = new[]
                    {
                        $"{weakestSkillCandidate.Misses} missed or unanswered item(s) in this cluster",
                        "Review evidence before repeating a timed paper",
                    },
                });
            }
        }

        var attemptedPaperIds = attempts.Select(a => a.PaperId).ToHashSet();
        var submittedPaperIds = attempts
            .Where(a => a.Status == ReadingAttemptStatus.Submitted)
            .Where(IsCanonicalReadingScoreAttempt)
            .Select(a => a.PaperId)
            .ToHashSet();
        var nextPaper = readyPapers.FirstOrDefault(p => !attemptedPaperIds.Contains(p.Id))
            ?? readyPapers.FirstOrDefault(p => submittedPaperIds.Contains(p.Id))
            ?? readyPapers.FirstOrDefault();
        if (nextPaper is not null && actions.Count < 3)
        {
            actions.Add(new
            {
                id = $"paper-{nextPaper.Id}-full-timed",
                title = "Run a full timed Reading paper",
                description = "Use strict Part A and B/C timing to check whether the latest review work transfers under exam pressure.",
                focusLabel = "Full paper",
                estimatedMinutes = policy.PartATimerMinutes + policy.PartBCTimerMinutes,
                launchRoute = $"/reading/paper/{nextPaper.Id}",
                highlights = new[]
                {
                    paperTitles.GetValueOrDefault(nextPaper.Id, nextPaper.Title),
                    $"{policy.PartATimerMinutes}+{policy.PartBCTimerMinutes} minute timing rhythm",
                },
            });
        }

        return actions.Take(3).ToList();
    }

    private static string SanitiseActionId(string value)
        => new(value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

    private static int CountPartQuestions(IReadOnlyCollection<ReadingPart> parts, ReadingPartCode code)
        => parts.FirstOrDefault(part => part.PartCode == code)?.Questions.Count ?? 0;

    private static bool IsCanonicalReadingScoreAttempt(ReadingAttempt attempt)
        => attempt.Mode is ReadingAttemptMode.Exam or ReadingAttemptMode.Learning
            && attempt.MaxRawScore == OetScoring.ListeningReadingRawMax;

    private static (DateTimeOffset PartADeadlineAt, DateTimeOffset PartBCDeadlineAt) ResolveReadingAttemptDeadlines(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy)
    {
        if (attempt.Mode == ReadingAttemptMode.Exam)
        {
            return (
                attempt.StartedAt.AddMinutes(policy.PartATimerMinutes),
                attempt.StartedAt.AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes));
        }

        var answerDeadline = ResolveReadingPracticeAnswerDeadline(attempt, policy);
        return (answerDeadline, answerDeadline);
    }

    private static DateTimeOffset ResolveReadingPracticeAnswerDeadline(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy)
    {
        if (attempt.DeadlineAt is DateTimeOffset deadline)
        {
            return deadline.AddSeconds(-Math.Max(0, policy.GracePeriodSeconds));
        }

        var minutes = TryReadReadingScopeMinutes(attempt.ScopeJson)
            ?? (attempt.Mode == ReadingAttemptMode.Learning
                ? Math.Max(60, policy.PartATimerMinutes + policy.PartBCTimerMinutes) * 4
                : policy.PartATimerMinutes + policy.PartBCTimerMinutes);
        return attempt.StartedAt.AddMinutes(minutes);
    }

    private static int? TryReadReadingScopeMinutes(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(scopeJson);
            if (doc.RootElement.TryGetProperty("minutes", out var minutesElement)
                && minutesElement.ValueKind == System.Text.Json.JsonValueKind.Number
                && minutesElement.TryGetInt32(out var minutes)
                && minutes is > 0 and <= 240)
            {
                return minutes;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return null;
    }

    private static HashSet<string>? ParseReadingScopeQuestionIds(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(scopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var questionIds)
                || questionIds.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            var ids = questionIds.EnumerateArray()
                .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);
            return ids.Count > 0 ? ids : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static ReadingResolvedPolicy ResolveReadingPolicySnapshot(
        string json,
        ReadingResolvedPolicy fallback)
    {
        try
        {
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<ReadingResolvedPolicy>(json);
            if (snapshot is not null
                && !string.IsNullOrWhiteSpace(snapshot.PartATimerStrictness)
                && !string.IsNullOrWhiteSpace(snapshot.ShortAnswerNormalisation))
            {
                return snapshot;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Use the current policy summary for legacy attempts with no usable snapshot.
        }

        return fallback;
    }
}

public record PeerReviewSubmitRequest(string AttemptId, string SubtestCode);
public record PeerReviewFeedbackRequest(int OverallRating, string Comments, string? Strengths, string? Improvements);
public record LearnerEscalationSubmitRequest(string SubmissionId, string Reason, string Details);
