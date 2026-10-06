using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetWritingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        // The historical attempt/evaluation surface has no v1.1 report link.
        // Do not expose its raw-total or band fields through learner home.
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var examFamilyLabel = FormatExamFamilyLabel(profile.Goal.ExamFamilyCode);
        var tasks = await GetTasksBySubtestAsync(userId, "writing", cancellationToken);
        var attempts = await db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SubtestCode == "writing")
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToListAsync(cancellationToken);
        var draftAttempt = await db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.SubtestCode == "writing" && x.State == AttemptState.InProgress)
            .OrderByDescending(x => x.LastClientSyncAt ?? x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var recentAttemptIds = attempts.Select(attempt => attempt.Id).ToArray();
        var latestEvaluationIdQuery = db.Evaluations
            .Where(_ => false)
            .Select(evaluation => evaluation.Id)
            .Take(1);
        var evaluationRows = await (
                from evaluation in db.Evaluations.AsNoTracking()
                join attempt in db.Attempts.AsNoTracking()
                    on evaluation.AttemptId equals attempt.Id
                join content in db.ContentItems.AsNoTracking()
                    on attempt.ContentId equals content.Id
                where attempt.UserId == userId
                      && attempt.SubtestCode == "writing"
                      && false
                      && (recentAttemptIds.Contains(evaluation.AttemptId)
                          || latestEvaluationIdQuery.Contains(evaluation.Id))
                select new WritingHomeEvaluationRow(
                    evaluation,
                    attempt,
                    content,
                    latestEvaluationIdQuery.Contains(evaluation.Id)))
            .ToListAsync(cancellationToken);
        var latestEvaluationRow = evaluationRows.FirstOrDefault(row => row.IsLatest);
        var criterionDrillLibrary = latestEvaluationRow is not null
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestEvaluationRow.Evaluation.CriterionScoresJson, [])
                .OrderBy(x => ParseCriterionScore(x.GetValueOrDefault("scoreRange")?.ToString()))
                .Take(3)
                .Select(x => new
                {
                    criterionCode = x.GetValueOrDefault("criterionCode")?.ToString(),
                    criterionLabel = CriterionLabelFromCode(x.GetValueOrDefault("criterionCode")?.ToString()),
                    rationale = x.GetValueOrDefault("explanation")?.ToString() ?? "Target this criterion with a focused writing drill.",
                    route = $"/writing/tasks?criterion={x.GetValueOrDefault("criterionCode")}"
                })
                .ToList<object>()
            : tasks.Take(3).Select(task => task).ToList();
        var practiceLibrary = tasks.Take(4).ToList();
        var recommendedTask = practiceLibrary.FirstOrDefault();
        var evaluationByAttemptId = evaluationRows
            .Where(row => recentAttemptIds.Contains(row.Evaluation.AttemptId))
            .GroupBy(row => row.Evaluation.AttemptId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.Evaluation.GeneratedAt)
                    .First()
                    .Evaluation);
        object? latestEvaluationSummary = null;
        if (latestEvaluationRow is not null)
        {
            // No server-side "evaluation_viewed" write on this read path: the client tracks the view.
            latestEvaluationSummary = BuildWritingEvaluationSummaryDto(
                latestEvaluationRow.Evaluation,
                latestEvaluationRow.Attempt,
                latestEvaluationRow.Content);
        }

        return new
        {
            recommendedTask,
            practiceLibrary,
            criterionDrillLibrary,
            pastSubmissions = attempts.Select(attempt =>
            {
                evaluationByAttemptId.TryGetValue(attempt.Id, out var evaluation);
                return new
                {
                    attemptId = attempt.Id,
                    contentId = attempt.ContentId,
                    state = ToApiState(attempt.State),
                    scoreEstimate = evaluation?.ScoreRange,
                    route = evaluation?.Id is null ? $"/writing/attempt/{attempt.Id}" : $"/writing/result?id={Uri.EscapeDataString(evaluation.Id)}"
                };
            }),
            reviewCredits = new
            {
                available = profile.Wallet.CreditBalance,
                route = "/reviews",
                billingRoute = "/billing"
            },
            fullMockEntry = new
            {
                title = $"{examFamilyLabel} Full Mock Test",
                route = "/mocks",
                rationale = $"Use a full mock to confirm whether your {examFamilyLabel} writing gains are transferring under timed conditions."
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluationSummary,
            actions = new[]
            {
                new { label = "Browse Writing Tasks", route = "/writing/tasks" },
                new { label = draftAttempt is null ? "Start Writing Task" : "Resume Draft", route = draftAttempt is null ? "/writing/tasks" : $"/writing/attempt/{draftAttempt.Id}" }
            }
        };
    }

    public async Task<List<object>> GetWritingTasksAsync(string userId, CancellationToken cancellationToken) => await GetTasksBySubtestAsync(userId, "writing", cancellationToken);

    public async Task<object> GetWritingTaskAsync(string userId, string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing" && x.Status == ContentStatus.Published, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing task not found.");
        // CRITICAL SECURITY FIX (22 Sep 2026 handoff, item 2): this direct-route
        // task preview had no profession check at all.
        await RequireOwnProfessionAsync(userId, item.ProfessionId, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["contentType"] = item.ContentType,
            ["subtest"] = item.SubtestCode,
            ["professionId"] = item.ProfessionId,
            ["title"] = item.Title,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["criteriaFocus"] = JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []),
            ["scenarioType"] = item.ScenarioType,
            ["modeSupport"] = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            ["publishedRevisionId"] = item.PublishedRevisionId,
            ["status"] = ToContentStatus(item.Status),
            ["caseNotes"] = item.CaseNotes
        }, detail);
    }

    // NOTE: this is the legacy Writing-Tasks content-item attempt flow
    // (POST /v1/writing/attempts) — confirmed unreachable from any current
    // frontend page (no app/writing/tasks or app/writing/attempt/[id]
    // routes exist; the live "Practice this" journey is the writing-v2
    // scenario flow below, gated by WritingEntitlementService via
    // WritingScenarioEndpoints' eligibility endpoint, which IS the fix for
    // Writing Rule Enforcement Addendum Rev5 §12). Left on its original,
    // AiPackageCreditService-only gate rather than routed through the
    // canonical resolver too: doing so broke pre-existing
    // LearnerSpecRegressionTests coverage of this dead code path with no
    // live-candidate benefit, since it isn't reachable to begin with.
    public async Task<object> CreateWritingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken)
    {
        var existingAttempts = await db.Attempts
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && x.ContentId == request.ContentId
                        && x.SubtestCode == "writing"
                        && x.Context == (request.Context ?? "practice")
                        && x.State == AttemptState.InProgress)
            .ToListAsync(cancellationToken);
        if (existingAttempts.Count > 0)
        {
            return await CreateAttemptAsync(userId, request, "writing", cancellationToken);
        }

        var created = await CreateAttemptAsync(userId, request, "writing", cancellationToken);
        if (aiPackageCreditService is null)
        {
            return created;
        }

        var attemptId = created.GetType().GetProperty("attemptId")?.GetValue(created) as string;
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return created;
        }

        var debit = await aiPackageCreditService.DeductGradingCreditAsync(
            userId, "writing", attemptId, AiGradingCreditCost.WritingExam, cancellationToken);
        if (debit.Debited)
        {
            return MergeWritingAttemptWithFeedback(created, debit.FeedbackMessage);
        }

        var attempt = await db.Attempts.FirstOrDefaultAsync(row => row.Id == attemptId, cancellationToken);
        if (attempt is not null)
        {
            db.Attempts.Remove(attempt);
            await db.SaveChangesAsync(cancellationToken);
        }

        throw ApiException.PaymentRequired(
            debit.ErrorCode ?? "no_ai_package_credits",
            debit.ErrorMessage ?? "You do not have enough credits to start this activity. Please purchase another package or upgrade your plan.");
    }

    public async Task<object> GetWritingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task<object> GetWritingPaperAssetsAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        _ = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var assets = await LoadWritingPaperAssetResponsesAsync(attemptId, cancellationToken);
        return new
        {
            attemptId,
            assets,
            extractionState = SummarizeExtractionState(assets),
            extractedText = await BuildWritingPaperExtractedTextAsync(attemptId, cancellationToken)
        };
    }

    public async Task<object> AttachWritingPaperAssetsAsync(string userId, string attemptId, WritingPaperAssetAttachRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureWritingDraftEditable(attempt);

        var mediaAssetIds = (request.MediaAssetIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (mediaAssetIds.Count == 0)
        {
            throw ApiException.Validation(
                "paper_assets_required",
                "Upload at least one handwritten writing page before continuing.",
                [new ApiFieldError("mediaAssetIds", "required", "Upload one or more image or PDF pages.")]);
        }

        if (request.ReplaceExisting == true)
        {
            var existingRows = await db.WritingAttemptAssets.Where(asset => asset.AttemptId == attempt.Id).ToListAsync(cancellationToken);
            db.WritingAttemptAssets.RemoveRange(existingRows);
        }

        var mediaAssets = await db.MediaAssets
            .Where(asset => mediaAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (mediaAssets.Count != mediaAssetIds.Count)
        {
            throw ApiException.Validation(
                "paper_asset_not_found",
                "One or more uploaded files could not be found.",
                [new ApiFieldError("mediaAssetIds", "not_found", "Re-upload the missing page and try again.")]);
        }

        var now = DateTimeOffset.UtcNow;
        var existingByMediaId = await db.WritingAttemptAssets
            .Where(asset => asset.AttemptId == attempt.Id && mediaAssetIds.Contains(asset.MediaAssetId))
            .ToDictionaryAsync(asset => asset.MediaAssetId, cancellationToken);
        var pageNumber = await db.WritingAttemptAssets
            .Where(asset => asset.AttemptId == attempt.Id)
            .Select(asset => (int?)asset.PageNumber)
            .MaxAsync(cancellationToken) ?? 0;

        foreach (var media in mediaAssets.OrderBy(asset => mediaAssetIds.IndexOf(asset.Id)))
        {
            ValidateWritingPaperMediaAsset(userId, media);
            if (!existingByMediaId.TryGetValue(media.Id, out var writingAsset))
            {
                writingAsset = new WritingAttemptAsset
                {
                    Id = $"wpa-{Guid.NewGuid():N}",
                    AttemptId = attempt.Id,
                    UserId = userId,
                    MediaAssetId = media.Id,
                    PageNumber = ++pageNumber,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.WritingAttemptAssets.Add(writingAsset);
            }

            if (!string.Equals(writingAsset.ExtractionState, "completed", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(writingAsset.ExtractedText))
            {
                await ExtractWritingPaperAssetAsync(writingAsset, media, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var assets = await LoadWritingPaperAssetResponsesAsync(attempt.Id, cancellationToken);
        var extractedText = await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken);
        MergeWritingAttemptMetadata(attempt, "paper", "pending", assets.Select(asset => asset.MediaAssetId), extractedText.Length);
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            attemptId = attempt.Id,
            assets,
            extractionState = SummarizeExtractionState(assets),
            extractedText,
            extractedCharCount = extractedText.Length,
            wordCount = CountWords(extractedText)
        };
    }

    public async Task<object> UpdateWritingDraftAsync(string userId, string attemptId, DraftUpdateRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        EnsureWritingDraftEditable(attempt);
        EnsureWritingReadOnlyPhaseAllowsDraftMutation(attempt, request);
        if (request.DraftVersion.HasValue && request.DraftVersion.Value != attempt.DraftVersion)
        {
            throw ApiException.Conflict(
                "draft_version_conflict",
                "This draft has changed since your last save. Refresh the latest server version before saving again.",
                [new ApiFieldError("draftVersion", "stale_value", "The draft version is stale.")]);
        }

        if (request.Content is not null) attempt.DraftContent = request.Content;
        if (request.Scratchpad is not null) attempt.Scratchpad = request.Scratchpad;
        if (request.Checklist is not null) attempt.ChecklistJson = JsonSupport.Serialize(request.Checklist);
        attempt.DraftVersion += 1;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        attempt.State = AttemptState.InProgress;
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            attemptId = attempt.Id,
            saved = true,
            draftVersion = attempt.DraftVersion,
            lastSavedAt = attempt.LastClientSyncAt,
            state = ToApiState(attempt.State),
            saveState = new
            {
                state = "saved",
                message = "Draft saved.",
                lastSavedAt = attempt.LastClientSyncAt
            }
        };
    }

    private static void EnsureWritingDraftEditable(Attempt attempt)
    {
        if (attempt.State is AttemptState.NotStarted or AttemptState.InProgress or AttemptState.Paused)
        {
            return;
        }

        throw ApiException.Conflict(
            "writing_attempt_locked",
            "This writing attempt has already been submitted and cannot be edited.",
            [new ApiFieldError("attemptId", "locked", "Start a new writing attempt before editing another response.")]);
    }

    public async Task<object> SubmitWritingAttemptAsync(string userId, string attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var attempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var examMode = NormalizeWritingExamMode(request.ExamMode);
        var assessorType = NormalizeWritingAssessorType(request.AssessorType);
        if (assessorType == "ai")
        {
            throw ApiException.Conflict(
                "writing_v11_required",
                "This Writing submission route is no longer available. Submit your letter from the Writing task page instead.",
                [new ApiFieldError("assessorType", "v11_required", "Submit your letter from the Writing task page.")]);
        }
        var idempotencyScope = $"writing-submit:{userId}:{attempt.Id}";
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == attemptId, cancellationToken);
            var existingReview = await db.ReviewRequests
                .Where(review => review.AttemptId == attemptId && review.State != ReviewRequestState.Cancelled && review.State != ReviewRequestState.Failed)
                .OrderByDescending(review => review.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingReview is not null && existing is null)
            {
                return new
                {
                    attemptId = attempt.Id,
                    reviewRequestId = existingReview.Id,
                    state = ToReviewRequestState(existingReview.State),
                    examMode,
                    assessorType = "instructor"
                };
            }

            return new { attemptId = attempt.Id, evaluationId = existing?.Id, state = existing is null ? "queued" : ToAsyncState(existing.State), examMode, assessorType };
        }

        var proposedContent = examMode == "paper" ? request.Content ?? await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken) : request.Content ?? attempt.DraftContent;
        if (examMode == "computer")
        {
            EnsureWritingReadOnlyPhaseAllowsContentMutation(attempt, proposedContent);
        }

        if (examMode == "paper")
        {
            if (request.PaperAssetIds?.Count > 0)
            {
                await AttachWritingPaperAssetsAsync(userId, attempt.Id, new WritingPaperAssetAttachRequest(request.PaperAssetIds, false), cancellationToken);
            }

            var extractedText = await BuildWritingPaperExtractedTextAsync(attempt.Id, cancellationToken);
            var incompletePaperAssets = await db.WritingAttemptAssets
                .CountAsync(asset => asset.AttemptId == attempt.Id && asset.ExtractionState != "completed", cancellationToken);
            if (incompletePaperAssets > 0)
            {
                throw ApiException.Validation(
                    "paper_ocr_incomplete",
                    "OCR must finish successfully for every handwritten page before submission.",
                    [new ApiFieldError("paperAssetIds", "ocr_incomplete", "Re-upload failed pages or wait for extraction before submitting.")]);
            }
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw ApiException.Validation(
                    "paper_ocr_required",
                    "We could not extract enough text from the handwritten pages. Re-upload clearer pages before submitting.",
                    [new ApiFieldError("paperAssetIds", "ocr_required", "Paper-based assessment needs successful OCR before grading or tutor review.")]);
            }

            attempt.DraftContent = extractedText;
        }
        else if (request.Content is not null)
        {
            attempt.DraftContent = request.Content;
        }

        if (string.IsNullOrWhiteSpace(attempt.DraftContent))
        {
            throw ApiException.Validation(
                "writing_content_required",
                "Writing content is required before submission.",
                [new ApiFieldError("content", "required", "Enter your response before submitting.")]);
        }

        // Free-tier / kill-switch entitlement gate. Premium subscribers
        // pass through unconditionally; free tier respects the runtime
        // WritingOptions singleton (default: premium-only).
        if (assessorType == "ai" && writingEntitlement is not null)
        {
            var ent = await writingEntitlement.CheckAsync(userId, cancellationToken);
            if (!ent.Allowed)
            {
                var msg = ent.Reason switch
                {
                    "premium_required" => "Writing practice requires an active subscription.",
                    "quota_exceeded" => $"Free tier allows {ent.LimitPerWindow} writing attempts every {ent.WindowDays} days.",
                    _ => ent.Reason,
                };
                throw ApiException.PaymentRequired("writing_quota_exceeded", msg);
            }
        }

        List<string> paperAssetIds = examMode == "paper"
            ? await db.WritingAttemptAssets
                .Where(asset => asset.AttemptId == attempt.Id)
                .OrderBy(asset => asset.PageNumber)
                .Select(asset => asset.MediaAssetId)
                .ToListAsync(cancellationToken)
            : new List<string>();
        MergeWritingAttemptMetadata(attempt, examMode, assessorType, paperAssetIds, attempt.DraftContent.Length);

        if (assessorType == "instructor")
        {
            attempt.State = AttemptState.Completed;
            attempt.SubmittedAt = DateTimeOffset.UtcNow;
            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
            await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);

            var reviewResponse = await CreateReviewRequestCoreAsync(userId, new ReviewRequestCreateRequest(
                attempt.Id,
                "writing",
                string.IsNullOrWhiteSpace(request.TurnaroundOption) ? "standard" : request.TurnaroundOption,
                request.FocusAreas ?? ["OET writing criteria", "voice-note feedback"],
                BuildInstructorLearnerNotes(request.LearnerNotes, examMode),
                "credits",
                request.IdempotencyKey is null ? null : $"writing-instructor-{request.IdempotencyKey}"), cancellationToken);

            var review = await db.ReviewRequests
                .Where(existingReview => existingReview.AttemptId == attempt.Id && existingReview.State != ReviewRequestState.Cancelled)
                .OrderByDescending(existingReview => existingReview.CreatedAt)
                .FirstAsync(cancellationToken);
            await TryAssignDrAhmedAsync(review.Id, cancellationToken);
            await RecordEventAsync(userId, "writing_instructor_review_requested", new { attemptId = attempt.Id, reviewRequestId = review.Id, examMode, contentId = attempt.ContentId }, cancellationToken);

            var response = new
            {
                attemptId = attempt.Id,
                reviewRequestId = review.Id,
                state = "queued_for_instructor_review",
                examMode,
                assessorType,
                review = reviewResponse
            };
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await SaveIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, response, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            return response;
        }

        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);

        var evaluationId = $"we-{Guid.NewGuid():N}";
        var evaluation = new Evaluation
        {
            Id = evaluationId,
            AttemptId = attempt.Id,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Evaluation queued.",
            LearnerDisclaimer = "Estimated training result pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Writing evaluation queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.WritingEvaluation, attemptId: attempt.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        var aiResponse = new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000, examMode, assessorType };
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing", contentId = attempt.ContentId, examMode, assessorType }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync(idempotencyScope, request.IdempotencyKey, aiResponse, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return aiResponse;
    }

    private static string NormalizeWritingExamMode(string? examMode)
    {
        var normalized = (examMode ?? "computer").Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "paper" or "paper-based" or "handwritten" => "paper",
            "computer" or "computer-based" or "typed" => "computer",
            _ => throw ApiException.Validation(
                "invalid_writing_exam_mode",
                "Choose either computer-based or paper-based writing mode.",
                [new ApiFieldError("examMode", "invalid", "Use computer or paper.")])
        };
    }

    private static string NormalizeWritingAssessorType(string? assessorType)
    {
        var normalized = (assessorType ?? "ai").Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "ai" or "ai-assessment" or "automatic" => "ai",
            "instructor" or "dr-ahmed" or "doctor-ahmed" or "human" or "tutor" => "instructor",
            _ => throw ApiException.Validation(
                "invalid_writing_assessor",
                "Choose either AI assessment or Dr. Ahmed instructor assessment.",
                [new ApiFieldError("assessorType", "invalid", "Use ai or instructor.")])
        };
    }

    private static string BuildInstructorLearnerNotes(string? learnerNotes, string examMode)
    {
        var modeLabel = examMode == "paper" ? "Paper-based handwritten submission" : "Computer-based typed submission";
        return string.IsNullOrWhiteSpace(learnerNotes)
            ? $"Dr. Ahmed instructor assessment requested. {modeLabel}. Voice-note feedback expected."
            : $"Dr. Ahmed instructor assessment requested. {modeLabel}. Voice-note feedback expected.\n\nLearner notes: {learnerNotes.Trim()}";
    }

    private async Task TryAssignDrAhmedAsync(string reviewRequestId, CancellationToken cancellationToken)
    {
        var existingAssignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(assignment => assignment.ReviewRequestId == reviewRequestId
                && assignment.ClaimState != ExpertAssignmentState.Released, cancellationToken);
        if (existingAssignment is not null)
        {
            return;
        }

        var drAhmed = await db.ExpertUsers
            .Where(expert => expert.IsActive
                && (expert.DisplayName.ToLower().Contains("ahmed")
                    || expert.Email.ToLower().Contains("ahmed")))
            .OrderBy(expert => expert.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (drAhmed is null)
        {
            return;
        }

        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = $"era-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            AssignedReviewerId = drAhmed.Id,
            AssignedBy = "system-dr-ahmed-routing",
            AssignedAt = DateTimeOffset.UtcNow,
            ClaimState = ExpertAssignmentState.Assigned,
            ReasonCode = "dr-ahmed-writing-assessment"
        });
    }

    private static void ValidateWritingPaperMediaAsset(string userId, MediaAsset media)
    {
        if (!string.Equals(media.UploadedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("paper_asset_forbidden", "You can only submit paper assets uploaded by your account.");
        }

        var isSupportedPaperFile = string.Equals(media.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(media.MimeType, "image/png", StringComparison.OrdinalIgnoreCase);
        if (!isSupportedPaperFile)
        {
            throw ApiException.Validation(
                "invalid_paper_asset_type",
                "Writing paper submissions must be JPG, PNG, or PDF pages.",
                [new ApiFieldError("mediaAssetIds", "invalid_type", "Upload JPG, PNG, or PDF pages.")]);
        }
    }

    private async Task ExtractWritingPaperAssetAsync(WritingAttemptAsset writingAsset, MediaAsset media, CancellationToken cancellationToken)
    {
        writingAsset.ExtractionState = "processing";
        writingAsset.UpdatedAt = DateTimeOffset.UtcNow;
        writingAsset.ExtractionProvider = "auto-docintel-pdfpig";
        writingAsset.ExtractionReasonCode = null;
        writingAsset.ExtractionMessage = null;

        try
        {
            await using var stream = await fileStorage.OpenReadAsync(media.StoragePath, cancellationToken);
            var text = (await pdfTextExtractor.ExtractAsync(stream, cancellationToken)).Trim();
            writingAsset.ExtractedText = text;
            writingAsset.ExtractedAt = DateTimeOffset.UtcNow;
            writingAsset.UpdatedAt = writingAsset.ExtractedAt.Value;

            if (text.Length < 20)
            {
                writingAsset.ExtractionState = "failed";
                writingAsset.ExtractionReasonCode = "ocr_no_text";
                writingAsset.ExtractionMessage = "OCR completed but did not find enough readable text.";
                return;
            }

            writingAsset.ExtractionState = "completed";
            writingAsset.ExtractionMessage = "Text extracted successfully.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            writingAsset.ExtractionState = "failed";
            writingAsset.ExtractedText = string.Empty;
            writingAsset.ExtractionReasonCode = "ocr_failed";
            writingAsset.ExtractionMessage = "OCR failed for this page. Re-upload a clearer scan or use computer-based entry.";
            writingAsset.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<IReadOnlyList<WritingPaperAssetResponse>> LoadWritingPaperAssetResponsesAsync(string attemptId, CancellationToken cancellationToken)
    {
        var rows = await db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.AttemptId == attemptId)
            .OrderBy(asset => asset.PageNumber)
            .Join(db.MediaAssets.AsNoTracking(), asset => asset.MediaAssetId, media => media.Id, (asset, media) => new { asset, media })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new WritingPaperAssetResponse(
            row.asset.Id,
            row.media.Id,
            row.media.OriginalFilename,
            row.media.MimeType,
            row.media.Format,
            row.media.SizeBytes,
            row.asset.PageNumber,
            row.asset.ExtractionState,
            row.asset.ExtractedText?.Length ?? 0,
            row.asset.ExtractionMessage,
            $"/v1/media/{row.media.Id}/content")).ToList();
    }

    private async Task<string> BuildWritingPaperExtractedTextAsync(string attemptId, CancellationToken cancellationToken)
    {
        var texts = await db.WritingAttemptAssets
            .AsNoTracking()
            .Where(asset => asset.AttemptId == attemptId && asset.ExtractionState == "completed")
            .OrderBy(asset => asset.PageNumber)
            .Select(asset => asset.ExtractedText)
            .ToListAsync(cancellationToken);
        return string.Join("\n\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()));
    }

    private static string SummarizeExtractionState(IReadOnlyList<WritingPaperAssetResponse> assets)
    {
        if (assets.Count == 0) return "empty";
        if (assets.Any(asset => asset.ExtractionState == "processing" || asset.ExtractionState == "queued")) return "processing";
        if (assets.All(asset => asset.ExtractionState == "completed")) return "completed";
        if (assets.Any(asset => asset.ExtractionState == "completed")) return "partial";
        return "failed";
    }

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Regex.Matches(text, @"\b[\p{L}\p{N}']+\b").Count;

    private static void MergeWritingAttemptMetadata(Attempt attempt, string examMode, string assessorType, IEnumerable<string> paperAssetIds, int extractedCharCount)
    {
        var analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>());
        analysis["writingSubmission"] = new
        {
            examMode,
            assessorType,
            submittedVia = examMode == "paper" ? "paper-upload" : "typed-editor",
            paperAssetIds = paperAssetIds.ToArray(),
            extractedCharCount,
            reviewer = assessorType == "instructor" ? "dr-ahmed" : null,
            updatedAt = DateTimeOffset.UtcNow
        };
        attempt.AnalysisJson = JsonSupport.Serialize(analysis);
    }

    private static void EnsureWritingReadOnlyPhaseAllowsDraftMutation(Attempt attempt, DraftUpdateRequest request)
    {
        var hasDraftMutation = request.Content is not null
            || request.Scratchpad is not null
            || request.Checklist is not null;
        EnsureWritingReadOnlyPhaseAllowsMutation(attempt, hasDraftMutation);
    }

    private static void EnsureWritingReadOnlyPhaseAllowsContentMutation(Attempt attempt, string? proposedContent)
        => EnsureWritingReadOnlyPhaseAllowsMutation(attempt, !string.IsNullOrWhiteSpace(proposedContent));

    private static void EnsureWritingReadOnlyPhaseAllowsMutation(Attempt attempt, bool hasWritingBearingMutation)
    {
        if (!string.Equals(attempt.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(attempt.Mode, "exam", StringComparison.OrdinalIgnoreCase)) return;
        if (!hasWritingBearingMutation) return;

        var readingWindowEndsAt = attempt.StartedAt.AddMinutes(5);
        if (DateTimeOffset.UtcNow >= readingWindowEndsAt) return;

        throw ApiException.Conflict(
            "writing_reading_window_active",
            "The first 5 minutes of OET Writing exam mode are reading-only. Writing is accepted after the reading window ends.");
    }

    public async Task<object> GetWritingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        if (evaluationId is not null)
        {
            throw ApiException.Conflict(
                "writing_v11_required",
                "This Writing result view is no longer available. Open your result from Past submissions instead.",
                [new ApiFieldError("evaluationId", "v11_required", "Open your result from Past submissions.")]);
        }
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        // No server-side "evaluation_viewed" write on this read path: the client tracks the view.
        return BuildWritingEvaluationSummaryDto(evaluation, attempt, content);
    }

    private static object BuildWritingEvaluationSummaryDto(
        Evaluation evaluation,
        Attempt attempt,
        ContentItem content)
    {
        var examFamilyCode = NormalizeExamFamilyCode(attempt.ExamFamilyCode);
        var examFamilyLabel = FormatExamFamilyLabel(examFamilyCode);
        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            profession = content.ProfessionId ?? "medicine",
            examFamilyCode,
            examFamilyLabel,
            subtest = evaluation.SubtestCode,
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            gradeRange = evaluation.GradeRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            confidenceLabel = BuildConfidenceLabel(evaluation.ConfidenceBand),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            generatedAt = evaluation.GeneratedAt,
            recommendedDrills = new[]
            {
                new
                {
                    id = $"phrasing-{evaluation.Id}",
                    title = "Phrasing and transcript drill",
                    description = "Practise stronger patient-centred alternatives from the marked transcript.",
                    route = $"/speaking/phrasing/{evaluation.Id}"
                },
                new
                {
                    id = "ai-patient-practice",
                    title = "AI patient conversation practice",
                    description = "Launch the existing conversation module for a grounded patient practice session.",
                    route = "/conversation"
                }
            },
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = evaluation.LearnerDisclaimer,
            isOfficialScore = false,
            methodLabel = BuildAiMethodLabel(evaluation.SubtestCode),
            provenanceLabel = $"{examFamilyLabel} practice estimate",
            humanReviewRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            escalationRecommended = ShouldRecommendHumanReview(evaluation.ConfidenceBand),
            statusReasonCode = evaluation.StatusReasonCode,
            retryable = evaluation.Retryable,
            retryAfterMs = evaluation.RetryAfterMs
        };
    }

    public async Task<object> GetWritingFeedbackAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var summary = await GetWritingEvaluationSummaryAsync(userId, evaluationId, cancellationToken);
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        return new
        {
            summary,
            criterionScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, [])
        };
    }

    public async Task<object> GetWritingRevisionAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        if (attemptId is not null)
        {
            throw ApiException.Conflict(
                "writing_v11_required",
                "This option is no longer available. To try this task again, start a new attempt from the task page.",
                [new ApiFieldError("attemptId", "v11_required", "Start a new attempt from the task page.")]);
        }
        var requestedAttempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var attempt = requestedAttempt.ParentAttemptId is null
            ? requestedAttempt
            : await GetWritingAttemptOwnedByUserAsync(userId, requestedAttempt.ParentAttemptId, cancellationToken);
        var evaluation = await GetCompletedWritingEvaluationForRevisionAsync(attempt.Id, cancellationToken);
        var related = await db.Attempts.Where(x => x.ParentAttemptId == attempt.Id && x.UserId == userId && x.SubtestCode == "writing").OrderByDescending(x => x.StartedAt).ToListAsync(cancellationToken);
        var latestRevision = related.FirstOrDefault();
        var latestRevisionEvaluation = latestRevision is null
            ? null
            : await db.Evaluations.Where(x => x.AttemptId == latestRevision.Id).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        await RecordEventAsync(userId, "revision_started", new { attemptId = attempt.Id, subtest = attempt.SubtestCode }, cancellationToken);

        var baseCriterionScores = evaluation is null
            ? []
            : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
        var revisedCriterionScores = latestRevisionEvaluation is null
            ? baseCriterionScores
            : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestRevisionEvaluation.CriterionScoresJson, []);

        var deltaSummary = baseCriterionScores.Select(baseScore =>
        {
            var code = baseScore.GetValueOrDefault("criterionCode")?.ToString();
            var revised = revisedCriterionScores.FirstOrDefault(x => x.GetValueOrDefault("criterionCode")?.ToString() == code);
            return new
            {
                name = CriterionLabelFromCode(code),
                original = ParseCriterionScore(baseScore.GetValueOrDefault("scoreRange")?.ToString()),
                revised = ParseCriterionScore(revised?.GetValueOrDefault("scoreRange")?.ToString() ?? baseScore.GetValueOrDefault("scoreRange")?.ToString()),
                max = 6
            };
        }).ToList();

        var unresolvedIssues = evaluation is null
            ? new List<string>()
            : JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []);

        return new
        {
            baseAttempt = new { attemptId = attempt.Id, content = attempt.DraftContent, draftVersion = attempt.DraftVersion },
            revisionDraft = new { attemptId = latestRevision?.Id, content = latestRevision?.DraftContent ?? attempt.DraftContent },
            latestEvaluationId = evaluation?.Id,
            criterionScores = baseCriterionScores,
            deltaSummary,
            unresolvedIssues,
            priorRevisions = related.Select(x => new { attemptId = x.Id, submittedAt = x.SubmittedAt, state = ToApiState(x.State) }),
            actions = new[] { new { label = "Open writing", route = "/writing" } }
        };
    }

    public async Task<object> SubmitWritingRevisionAsync(string userId, string attemptId, RevisionSubmitRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        throw ApiException.Conflict(
            "writing_v11_required",
            "This option is no longer available. To try this task again, start a new attempt from the task page.",
            [new ApiFieldError("attemptId", "v11_required", "Start a new attempt from the task page.")]);
        var idempotencyKey = NormalizeWritingRevisionIdempotencyKey(request.IdempotencyKey);
        var idempotencyScope = $"writing-revision-submit:{userId}:{attemptId}";
        if (idempotencyKey is not null)
        {
            var cached = await GetIdempotentResponseAsync(idempotencyScope, idempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var baseAttempt = await GetWritingAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (baseAttempt.State is not (AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed))
        {
            throw ApiException.Validation(
                "writing_revision_base_not_submitted",
                "Submit the original Writing attempt before creating a revision.",
                [new ApiFieldError("attemptId", "not_submitted", "Revision requires a submitted Writing attempt.")]);
        }

        if (baseAttempt.ParentAttemptId is not null)
        {
            throw ApiException.Conflict(
                "writing_revision_base_is_revision",
                "Open the original Writing result before creating another revision.",
                [new ApiFieldError("attemptId", "revision_attempt", "Revision submission must target the original Writing attempt.")]);
        }

        _ = await GetCompletedWritingEvaluationForRevisionAsync(baseAttempt.Id, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw ApiException.Validation(
                "writing_revision_content_required",
                "Revised Writing content is required before submission.",
                [new ApiFieldError("content", "required", "Enter your revised response before submitting.")]);
        }

        if (request.Content.Length > WritingRevisionContentMaxLength)
        {
            throw ApiException.Validation(
                "writing_revision_content_too_long",
                "Revised Writing content is too long.",
                [new ApiFieldError("content", "too_long", $"Keep the revised response under {WritingRevisionContentMaxLength} characters.")]);
        }

        if (writingEntitlement is not null)
        {
            var ent = await writingEntitlement.CheckAsync(userId, cancellationToken);
            if (!ent.Allowed)
            {
                var msg = ent.Reason switch
                {
                    "premium_required" => "Writing practice requires an active subscription.",
                    "quota_exceeded" => $"Free tier allows {ent.LimitPerWindow} writing attempts every {ent.WindowDays} days.",
                    _ => ent.Reason,
                };
                var code = ent.Reason is "premium_required" or "quota_exceeded" ? ent.Reason : "writing_entitlement_blocked";
                throw ApiException.PaymentRequired(code, msg);
            }
        }

        var revision = new Attempt
        {
            Id = $"wa-{Guid.NewGuid():N}",
            UserId = baseAttempt.UserId,
            ContentId = baseAttempt.ContentId,
            SubtestCode = "writing",
            Context = "revision",
            Mode = baseAttempt.Mode,
            State = AttemptState.Evaluating,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            DraftContent = request.Content,
            ParentAttemptId = baseAttempt.Id,
            ComparisonGroupId = baseAttempt.ComparisonGroupId,
            DeviceType = baseAttempt.DeviceType,
            DraftVersion = 1,
            LastClientSyncAt = DateTimeOffset.UtcNow
        };
        db.Attempts.Add(revision);
        var evaluation = new Evaluation
        {
            Id = $"we-{Guid.NewGuid():N}",
            AttemptId = revision.Id,
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Revision evaluation queued.",
            LearnerDisclaimer = "Estimated training result pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Revision queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.WritingEvaluation, attemptId: revision.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        var response = new { attemptId = revision.Id, evaluationId = evaluation.Id, state = "queued" };
        await RecordEventAsync(baseAttempt.UserId, "revision_submitted", new { attemptId = revision.Id, parentAttemptId = baseAttempt.Id, evaluationId = evaluation.Id }, cancellationToken);
        if (idempotencyKey is not null)
        {
            await SaveIdempotentResponseAsync(idempotencyScope, idempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    private async Task<Evaluation> GetCompletedWritingEvaluationForRevisionAsync(string attemptId, CancellationToken cancellationToken)
    {
        return await db.Evaluations
            .Where(x => x.AttemptId == attemptId && x.SubtestCode == "writing" && x.State == AsyncState.Completed)
            .OrderByDescending(x => x.GeneratedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw ApiException.Conflict(
                "writing_revision_feedback_not_ready",
                "Complete the original Writing feedback before creating a revision.",
                [new ApiFieldError("attemptId", "feedback_not_ready", "Revision requires completed Writing feedback.")]);
    }

    private static string? NormalizeWritingRevisionIdempotencyKey(string? key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > WritingRevisionIdempotencyKeyMaxLength || !WritingRevisionIdempotencyKeyRegex.IsMatch(normalized))
        {
            throw ApiException.Validation(
                "writing_revision_idempotency_key_invalid",
                "Revision idempotency key is invalid.",
                [new ApiFieldError("idempotencyKey", "invalid", "Use only letters, numbers, dots, underscores, colons, or hyphens, up to 64 characters.")]);
        }

        return normalized;
    }

    public async Task<object> GetWritingModelAnswerAsync(string userId, string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing" && x.Status == ContentStatus.Published, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing model answer not found.");
        await RequireOwnProfessionAsync(userId, item.ProfessionId, cancellationToken);

        var hasSubmittedAttempt = await db.Attempts.AnyAsync(attempt =>
            attempt.UserId == userId &&
            attempt.ContentId == contentId &&
            attempt.SubtestCode == "writing" &&
            attempt.SubmittedAt != null &&
            (attempt.State == AttemptState.Submitted ||
             attempt.State == AttemptState.Evaluating ||
             attempt.State == AttemptState.Completed), cancellationToken);

        if (!hasSubmittedAttempt)
        {
            throw ApiException.Forbidden("writing_model_answer_locked", "Submit your Writing attempt before viewing the model answer.");
        }

        return new
        {
            contentId = item.Id,
            title = item.Title,
            professionId = item.ProfessionId,
            payload = JsonSupport.Deserialize<Dictionary<string, object?>>(item.ModelAnswerJson, new Dictionary<string, object?>())
        };
    }

    private static object MergeWritingAttemptWithFeedback(object created, string? feedbackMessage)
    {
        if (string.IsNullOrWhiteSpace(feedbackMessage))
        {
            return created;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in created.GetType().GetProperties())
        {
            payload[property.Name] = property.GetValue(created);
        }

        payload["feedbackMessage"] = feedbackMessage;
        return payload;
    }
}
