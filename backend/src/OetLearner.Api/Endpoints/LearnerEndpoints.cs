using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

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
        v1.MapGet("/submissions", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetSubmissionsAsync(http.UserId(), ct)));
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
        speaking.MapPost("/device-checks", (DeviceCheckRequest request, LearnerService service) => Results.Ok(service.SaveDeviceCheck(request)));

        var reading = v1.MapGroup("/reading");
        reading.MapGet("/home", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingHomeAsync(ct)));
        reading.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingTaskAsync(contentId, ct)));
        reading.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateReadingAttemptAsync(http.UserId(), request, ct)));
        reading.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingAttemptAsync(http.UserId(), attemptId, ct)));
        reading.MapPatch("/attempts/{attemptId}/answers", async (HttpContext http, string attemptId, AnswersUpdateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.UpdateReadingAnswersAsync(http.UserId(), attemptId, request, ct)));
        reading.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        reading.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitReadingAttemptAsync(http.UserId(), attemptId, ct)));
        reading.MapGet("/evaluations/{evaluationId}", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetReadingEvaluationAsync(http.UserId(), evaluationId, ct)));

        var listening = v1.MapGroup("/listening");
        listening.MapGet("/home", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningHomeAsync(ct)));
        listening.MapGet("/tasks/{contentId}", async (string contentId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningTaskAsync(contentId, ct)));
        listening.MapPost("/attempts", async (HttpContext http, CreateAttemptRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateListeningAttemptAsync(http.UserId(), request, ct)));
        listening.MapGet("/attempts/{attemptId}", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningAttemptAsync(http.UserId(), attemptId, ct)));
        listening.MapPatch("/attempts/{attemptId}/answers", async (HttpContext http, string attemptId, AnswersUpdateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.UpdateListeningAnswersAsync(http.UserId(), attemptId, request, ct)));
        listening.MapPatch("/attempts/{attemptId}/heartbeat", async (HttpContext http, string attemptId, HeartbeatRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.HeartbeatAttemptAsync(http.UserId(), attemptId, request, ct)));
        listening.MapPost("/attempts/{attemptId}/submit", async (HttpContext http, string attemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitListeningAttemptAsync(http.UserId(), attemptId, ct)));
        listening.MapGet("/evaluations/{evaluationId}", async (HttpContext http, string evaluationId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningEvaluationAsync(http.UserId(), evaluationId, ct)));
        listening.MapGet("/drills/{drillId}", async (string drillId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetListeningDrillAsync(drillId, ct)));

        v1.MapGet("/mocks", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetMocksAsync(http.UserId(), ct)));
        v1.MapGet("/mocks/options", async (LearnerService service, CancellationToken ct) => Results.Ok(await service.GetMockOptionsAsync(ct)));
        v1.MapPost("/mock-attempts", async (HttpContext http, MockAttemptCreateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateMockAttemptAsync(http.UserId(), request, ct)));
        v1.MapGet("/mock-attempts/{mockAttemptId}", async (HttpContext http, string mockAttemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetMockAttemptAsync(http.UserId(), mockAttemptId, ct)));
        v1.MapPost("/mock-attempts/{mockAttemptId}/submit", async (HttpContext http, string mockAttemptId, LearnerService service, CancellationToken ct) => Results.Ok(await service.SubmitMockAttemptAsync(http.UserId(), mockAttemptId, ct)));
        v1.MapGet("/mock-reports/{reportId}", async (HttpContext http, string reportId, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetMockReportAsync(http.UserId(), reportId, ct)));

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
        billing.MapGet("/invoices", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetInvoicesAsync(http.UserId(), ct)));
        billing.MapGet("/invoices/{invoiceId}/download", async (HttpContext http, string invoiceId, LearnerService service, CancellationToken ct) =>
        {
            var file = await service.GetInvoiceDownloadAsync(http.UserId(), invoiceId, ct);
            return Results.File(file.Stream, file.ContentType, fileDownloadName: file.FileName);
        });
        billing.MapGet("/review-options", (LearnerService service) => Results.Ok(service.GetReviewOptions()));
        billing.MapGet("/extras", async (LearnerService service) => Results.Ok(await service.GetBillingExtrasAsync()));
        billing.MapPost("/checkout-sessions", async (HttpContext http, CheckoutSessionCreateRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateCheckoutSessionAsync(http.UserId(), request, ct)));

        // Engagement endpoints
        v1.MapGet("/learner/engagement", async (HttpContext http, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetEngagementAsync(http.UserId(), ct)));

        // Wallet endpoints
        billing.MapGet("/wallet/transactions", async (HttpContext http, [FromQuery] int? limit, LearnerService service, CancellationToken ct) => Results.Ok(await service.GetWalletTransactionsAsync(http.UserId(), limit ?? 20, ct)));
        billing.MapPost("/wallet/top-up", async (HttpContext http, WalletTopUpRequest request, LearnerService service, CancellationToken ct) => Results.Ok(await service.CreateWalletTopUpAsync(http.UserId(), request, ct)));

        // Payment webhook endpoints (no auth required)
        var webhooks = app.MapGroup("/v1/payment/webhooks");
        webhooks.MapPost("/stripe", async (HttpContext http, LearnerService service, CancellationToken ct) =>
        {
            var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
            var headers = http.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            return Results.Ok(await service.HandleStripeWebhookAsync(payload, headers, ct));
        });
        webhooks.MapPost("/paypal", async (HttpContext http, LearnerService service, CancellationToken ct) =>
        {
            var payload = await new StreamReader(http.Request.Body).ReadToEndAsync(ct);
            var headers = http.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            return Results.Ok(await service.HandlePayPalWebhookAsync(payload, headers, ct));
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
}

public record PeerReviewSubmitRequest(string AttemptId, string SubtestCode);
public record PeerReviewFeedbackRequest(int OverallRating, string Comments, string? Strengths, string? Improvements);
public record LearnerEscalationSubmitRequest(string SubmissionId, string Reason, string Details);
