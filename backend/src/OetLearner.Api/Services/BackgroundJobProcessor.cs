using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class BackgroundJobProcessor(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job processing failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var queuedJobQuery = db.BackgroundJobs
            .Where(x => x.State == AsyncState.Queued);

        var queuedJobs = db.Database.IsSqlite()
            ? await queuedJobQuery
                .Take(200)
                .ToListAsync(cancellationToken)
            : await queuedJobQuery
                .OrderBy(x => x.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

        var jobs = queuedJobs
            .Where(x => x.AvailableAt <= now)
            .OrderBy(x => x.AvailableAt)
            .ThenBy(x => x.CreatedAt)
            .Take(50)
            .ToList();

        foreach (var job in jobs)
        {
            job.State = AsyncState.Processing;
            job.StatusReasonCode = "processing";
            job.StatusMessage = "Job is processing.";
            job.LastTransitionAt = now;
        }

        if (jobs.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var job in jobs)
        {
            try
            {
                await ExecuteJobAsync(scope.ServiceProvider, db, job, cancellationToken);
                job.State = AsyncState.Completed;
                job.StatusReasonCode = "completed";
                job.StatusMessage = "Job completed successfully.";
                job.LastTransitionAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} of type {JobType} failed (attempt {Attempt})", job.Id, job.Type, job.RetryCount + 1);
                job.RetryCount += 1;
                job.LastTransitionAt = DateTimeOffset.UtcNow;

                const int maxRetries = 3;
                if (job.RetryCount < maxRetries)
                {
                    // Re-queue with exponential backoff
                    var delayMs = (int)Math.Pow(2, job.RetryCount) * 5000;
                    job.State = AsyncState.Queued;
                    job.AvailableAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
                    job.StatusReasonCode = "retrying";
                    job.StatusMessage = $"Retry {job.RetryCount}/{maxRetries} after failure: {ex.Message}";
                    job.RetryAfterMs = delayMs;
                }
                else
                {
                    job.State = AsyncState.Failed;
                    job.StatusReasonCode = "processing_failed";
                    job.StatusMessage = $"Failed after {maxRetries} attempts: {ex.Message}";
                    job.RetryAfterMs = 0;
                    await EmitFailureNotificationsAsync(scope.ServiceProvider, db, job, ex, cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task ExecuteJobAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        switch (job.Type)
        {
            case JobType.WritingEvaluation:
                await CompleteWritingEvaluationAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.SpeakingTranscription:
                await CompleteSpeakingTranscriptionAsync(db, job, cancellationToken);
                break;
            case JobType.SpeakingEvaluation:
                await CompleteSpeakingEvaluationAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.StudyPlanRegeneration:
                await CompleteStudyPlanRegenerationAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.MockReportGeneration:
                await CompleteMockReportGenerationAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.ReviewCompletion:
                await CompleteReviewRequestAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.NotificationFanout:
                await notifications.ProcessFanoutAsync(job, cancellationToken);
                break;
            case JobType.NotificationDigestDispatch:
                await notifications.ProcessDigestDispatchAsync(job, cancellationToken);
                break;
        }
    }

    private static async Task CompleteWritingEvaluationAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        var wordCount = attempt.DraftContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var concisenessBand = wordCount switch
        {
            < 140 => "3-4/6",
            <= 220 => "4-5/6",
            _ => "3/6"
        };

        evaluation.State = AsyncState.Completed;
        evaluation.ScoreRange = wordCount > 200 ? "340-370" : "320-350";
        evaluation.GradeRange = wordCount > 200 ? "B-B+" : "C+-B";
        evaluation.ConfidenceBand = wordCount > 160 ? ConfidenceBand.Medium : ConfidenceBand.Low;
        evaluation.StrengthsJson = JsonSupport.Serialize(new[]
        {
            "Your structure stays focused on the receiving clinician.",
            "Core postoperative actions are covered clearly."
        });
        evaluation.IssuesJson = JsonSupport.Serialize(new[]
        {
            "Trim lower-priority procedural detail more aggressively.",
            "Proofread for small wording repetitions before submission."
        });
        evaluation.CriterionScoresJson = JsonSupport.Serialize(new[]
        {
            new { criterionCode = "purpose", scoreRange = "4-5/6", confidenceBand = "medium", explanation = "Purpose is clear in the opening lines." },
            new { criterionCode = "content", scoreRange = "4-5/6", confidenceBand = "high", explanation = "Key discharge details are present." },
            new { criterionCode = "conciseness", scoreRange = concisenessBand, confidenceBand = "medium", explanation = "Conciseness improves when only ongoing-care information is retained." },
            new { criterionCode = "genre", scoreRange = "4/6", confidenceBand = "medium", explanation = "Tone remains professional overall." },
            new { criterionCode = "organization", scoreRange = "4/6", confidenceBand = "medium", explanation = "The sequence of information is logical." },
            new { criterionCode = "language", scoreRange = "4/6", confidenceBand = "medium", explanation = "Grammar and wording are generally secure." }
        });
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(new[]
        {
            new { feedbackItemId = $"{evaluation.Id}-1", criterionCode = "conciseness", type = "anchored_comment", anchor = new { snippet = attempt.DraftContent.Length > 80 ? attempt.DraftContent[..80] : attempt.DraftContent }, message = "Prioritise information that changes the reader's follow-up actions.", severity = "medium", suggestedFix = "Remove low-impact procedural details." }
        });
        evaluation.GeneratedAt = DateTimeOffset.UtcNow;
        evaluation.ModelExplanationSafe = "This training estimate is based on criterion-level writing signals and is not an official OET result.";
        evaluation.LearnerDisclaimer = "Use expert review when you need a higher-trust external check.";
        evaluation.StatusReasonCode = "completed";
        evaluation.StatusMessage = "Writing evaluation completed.";
        evaluation.RetryAfterMs = null;
        evaluation.LastTransitionAt = DateTimeOffset.UtcNow;

        attempt.State = AttemptState.Completed;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(db, attempt.UserId, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        var evaluationVersion = (evaluation.GeneratedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString();
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerEvaluationCompleted,
            attempt.UserId,
            "attempt",
            attempt.Id,
            evaluationVersion,
            new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["subtest"] = "writing"
            },
            cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            attempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after the latest writing evaluation."
            },
            cancellationToken);
    }

    private static async Task CompleteSpeakingTranscriptionAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;
        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        attempt.TranscriptJson = JsonSupport.Serialize(new object[]
        {
            new { id = "t1", speaker = "candidate", text = "Good morning, I'm handing over the care of Mr James Wheeler.", startTime = 0, endTime = 6, markers = (object[]?)null },
            new { id = "t2", speaker = "candidate", text = "Um, he mobilised with physiotherapy this afternoon and tolerated fifteen minutes out of bed.", startTime = 7, endTime = 15, markers = new[] { new { id = "tm1", type = "fluency", startTime = 7, endTime = 8, text = "Um", suggestion = "Reduce filler words for a more confident clinical opening." } } }
        });
    }

    private static async Task CompleteSpeakingEvaluationAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        evaluation.State = AsyncState.Completed;
        evaluation.ScoreRange = "330-360";
        evaluation.ConfidenceBand = ConfidenceBand.Medium;
        evaluation.StrengthsJson = JsonSupport.Serialize(new[]
        {
            "Your handover remains logically structured.",
            "Clinical terminology is generally appropriate for the task."
        });
        evaluation.IssuesJson = JsonSupport.Serialize(new[]
        {
            "Remove filler words at the start of key sections.",
            "Increase confidence when stating management plans."
        });
        evaluation.CriterionScoresJson = JsonSupport.Serialize(new[]
        {
            new { criterionCode = "intelligibility", scoreRange = "4-5/6", confidenceBand = "high", explanation = "Speech is understandable and clear overall." },
            new { criterionCode = "fluency", scoreRange = "3-4/6", confidenceBand = "medium", explanation = "Hesitation markers still appear in transitions." },
            new { criterionCode = "appropriateness", scoreRange = "4/6", confidenceBand = "medium", explanation = "Professional tone is mostly maintained." },
            new { criterionCode = "grammar_expression", scoreRange = "4/6", confidenceBand = "medium", explanation = "Expression is accurate with room for richer phrasing." }
        });
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(new[]
        {
            new { feedbackItemId = $"{evaluation.Id}-1", criterionCode = "fluency", type = "transcript_marker", anchor = new { lineId = "t2", startTime = 7, endTime = 8 }, message = "Start directly rather than using a filler before the update.", severity = "medium", suggestedFix = "Lead with the key patient status update." }
        });
        evaluation.GeneratedAt = DateTimeOffset.UtcNow;
        evaluation.ModelExplanationSafe = "This learner-safe estimate uses transcript markers and speaking quality signals rather than any official OET scoring system.";
        evaluation.LearnerDisclaimer = "Training estimate only. Confidence can change with audio quality and task complexity.";
        evaluation.StatusReasonCode = "completed";
        evaluation.StatusMessage = "Speaking evaluation completed.";
        evaluation.RetryAfterMs = null;
        evaluation.LastTransitionAt = DateTimeOffset.UtcNow;

        attempt.State = AttemptState.Completed;
        attempt.CompletedAt = DateTimeOffset.UtcNow;
        attempt.AnalysisJson = JsonSupport.Serialize(new
        {
            phrasing = new[]
            {
                new { id = "phr-1", originalPhrase = "Um, he mobilised", issueExplanation = "Filler words weaken fluency.", strongerAlternative = "He mobilised", drillPrompt = "Repeat the update starting directly with the patient action." }
            },
            waveformPeaks = new[] { 5, 11, 8, 15, 7, 10 }
        });

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(db, attempt.UserId, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        var evaluationVersion = (evaluation.GeneratedAt ?? DateTimeOffset.UtcNow).UtcDateTime.Ticks.ToString();
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerEvaluationCompleted,
            attempt.UserId,
            "attempt",
            attempt.Id,
            evaluationVersion,
            new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["subtest"] = "speaking"
            },
            cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            attempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after the latest speaking evaluation."
            },
            cancellationToken);
    }

    private static async Task CompleteStudyPlanRegenerationAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);
        plan.State = AsyncState.Completed;
        plan.Version += 1;
        plan.GeneratedAt = DateTimeOffset.UtcNow;
        plan.Checkpoint = "Regenerated after your latest evaluated attempt.";
        plan.WeakSkillFocus = "Writing conciseness and speaking fluency remain top priority.";

        var items = await db.StudyPlanItems.Where(x => x.StudyPlanId == plan.Id).ToListAsync(cancellationToken);
        foreach (var item in items.Where(x => x.Status == StudyPlanItemStatus.NotStarted))
        {
            item.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        }

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerStudyPlanRegenerated,
            plan.UserId,
            "study_plan",
            plan.Id,
            plan.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = plan.Checkpoint
            },
            cancellationToken);
    }

    private static async Task CompleteMockReportGenerationAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var mockAttempt = await db.MockAttempts.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);
        var reportId = $"mock-report-{Guid.NewGuid():N}";

        db.MockReports.Add(new MockReport
        {
            Id = reportId,
            MockAttemptId = mockAttempt.Id,
            State = AsyncState.Completed,
            GeneratedAt = DateTimeOffset.UtcNow,
            PayloadJson = JsonSupport.Serialize(new
            {
                id = reportId,
                title = "Generated OET Mock Report",
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                overallScore = "345",
                summary = "The latest mock shows steady improvement, with Writing still trailing the receptive sub-tests.",
                subTests = new[]
                {
                    new { id = "g-r", name = "Reading", score = "375", rawScore = "39/42" },
                    new { id = "g-l", name = "Listening", score = "355", rawScore = "36/42" },
                    new { id = "g-w", name = "Writing", score = "330", rawScore = "26/36" },
                    new { id = "g-s", name = "Speaking", score = "340", rawScore = "N/A" }
                },
                weakestCriterion = new { subtest = "Writing", criterion = "Conciseness & Clarity", description = "Continue pruning lower-priority detail for the reader." },
                priorComparison = new { exists = true, priorMockName = "Full OET Mock Test #1", overallTrend = "up", details = "Overall score increased by 5 points since the previous mock." }
            })
        });

        mockAttempt.ReportId = reportId;
        mockAttempt.State = AttemptState.Completed;
        mockAttempt.CompletedAt = DateTimeOffset.UtcNow;
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = mockAttempt.UserId,
            EventName = "mock_completed",
            PayloadJson = JsonSupport.Serialize(new { mockAttemptId = mockAttempt.Id, reportId }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerMockReportReady,
            mockAttempt.UserId,
            "mock_attempt",
            mockAttempt.Id,
            reportId,
            new Dictionary<string, object?>
            {
                ["mockAttemptId"] = mockAttempt.Id
            },
            cancellationToken);
    }

    private static async Task CompleteReviewRequestAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var request = await db.ReviewRequests.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (request.State != ReviewRequestState.Completed || request.CompletedAt is null)
        {
            job.StatusReasonCode = "review_completion_not_ready";
            job.StatusMessage = "Review completion fan-out skipped because the review has not been completed by an expert yet.";
            return;
        }

        var attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == request.AttemptId, cancellationToken);
        if (attempt is not null)
        {
            db.AnalyticsEvents.Add(new AnalyticsEventRecord
            {
                Id = $"evt-{Guid.NewGuid():N}",
                UserId = attempt.UserId,
                EventName = "review_completed",
                PayloadJson = JsonSupport.Serialize(new { reviewRequestId = request.Id, attemptId = request.AttemptId, subtest = request.SubtestCode }),
                OccurredAt = DateTimeOffset.UtcNow
            });

            // Trigger study plan regeneration after expert review completes
            var readiness = await RefreshReadinessAsync(db, attempt.UserId, cancellationToken);
            await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerReadinessUpdated,
                attempt.UserId,
                "readiness_snapshot",
                readiness.Id,
                readiness.Version.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = "Your readiness snapshot was updated after expert review feedback was applied."
                },
                cancellationToken);
        }
    }

    private static async Task<ReadinessSnapshot> RefreshReadinessAsync(LearnerDbContext db, string userId, CancellationToken cancellationToken)
    {
        var snapshot = await db.ReadinessSnapshots.FirstAsync(x => x.UserId == userId, cancellationToken);
        var payload = JsonSupport.Deserialize(snapshot.PayloadJson, new Dictionary<string, object?>());
        payload["computedAt"] = DateTimeOffset.UtcNow;
        snapshot.PayloadJson = JsonSupport.Serialize(payload);
        snapshot.ComputedAt = DateTimeOffset.UtcNow;
        snapshot.Version += 1;
        return snapshot;
    }

    private static async Task EmitFailureNotificationsAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, Exception ex, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var failureVersion = $"{job.Id}:{job.RetryCount}";

        if (job.Type is JobType.WritingEvaluation or JobType.SpeakingEvaluation)
        {
            if (!string.IsNullOrWhiteSpace(job.AttemptId))
            {
                var attempt = await db.Attempts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(existingAttempt => existingAttempt.Id == job.AttemptId, cancellationToken);
                if (attempt is not null)
                {
                    await notifications.CreateForLearnerAsync(
                        NotificationEventKey.LearnerEvaluationFailed,
                        attempt.UserId,
                        "attempt",
                        attempt.Id,
                        failureVersion,
                        new Dictionary<string, object?>
                        {
                            ["attemptId"] = attempt.Id,
                            ["subtest"] = attempt.SubtestCode,
                            ["message"] = $"We could not finish your {attempt.SubtestCode} evaluation automatically. Please try again shortly."
                        },
                        cancellationToken);
                }
            }
        }

        var adminAlertKey = job.Type is JobType.NotificationFanout or JobType.NotificationDigestDispatch
            ? NotificationEventKey.AdminNotificationDeliveryFailureAlert
            : NotificationEventKey.AdminStuckJobAlert;

        await notifications.CreateForAdminsAsync(
            adminAlertKey,
            "background_job",
            job.Id,
            failureVersion,
            new Dictionary<string, object?>
            {
                ["message"] = $"Background job {job.Id} ({job.Type}) failed after {job.RetryCount} attempts: {ex.Message}"
            },
            cancellationToken);
    }
}
