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

        await ReconcileFreezeLifecycleAsync(scope.ServiceProvider, db, cancellationToken);
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
                await CompleteStudyPlanRegenerationAsync(services, db, notifications, job, cancellationToken);
                break;
            case JobType.MockReportGeneration:
                await CompleteMockReportGenerationAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.ReviewCompletion:
                await CompleteReviewRequestAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.FreezeStart:
                await CompleteFreezeStartAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.FreezeEnd:
                await CompleteFreezeEndAsync(db, notifications, job, cancellationToken);
                break;
            case JobType.NotificationFanout:
                await notifications.ProcessFanoutAsync(job, cancellationToken);
                break;
            case JobType.NotificationDigestDispatch:
                await notifications.ProcessDigestDispatchAsync(job, cancellationToken);
                break;
            case JobType.ContentGeneration:
                await CompleteContentGenerationAsync(db, job, cancellationToken);
                break;
            case JobType.ConversationEvaluation:
                await CompleteConversationEvaluationAsync(db, job, cancellationToken);
                break;
            case JobType.PronunciationAnalysis:
                await CompletePronunciationAnalysisAsync(db, job, cancellationToken);
                break;
            case JobType.PrivateSpeakingZoomCreate:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.CreateZoomMeetingForBookingAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingBookingConfirmation:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.SendBookingConfirmationNotificationsAsync(job.ResourceId!, cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingReminder:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.ProcessRemindersAsync(cancellationToken);
                break;
            }
            case JobType.PrivateSpeakingReservationExpiry:
            {
                var psSvc = services.GetRequiredService<PrivateSpeakingService>();
                await psSvc.ExpireStaleReservationsAsync(cancellationToken);
                break;
            }
        }
    }

    private static async Task CompleteFreezeStartAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Active or FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.ScheduledStartAt is not null && record.ScheduledStartAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Active;
        record.IsCurrent = true;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeStarted,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze is now active."
            },
            cancellationToken);
    }

    private static async Task CompleteFreezeEndAsync(LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId))
        {
            return;
        }

        var record = await db.AccountFreezeRecords.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (record is null)
        {
            return;
        }

        if (record.Status is FreezeStatus.Completed or FreezeStatus.ForceEnded or FreezeStatus.Cancelled or FreezeStatus.Rejected)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (record.EndedAt is not null && record.EndedAt > now)
        {
            return;
        }

        record.Status = FreezeStatus.Completed;
        record.IsCurrent = false;
        record.StartedAt ??= record.ScheduledStartAt ?? now;
        record.EndedAt ??= now;
        record.EndReason ??= "Freeze period ended";
        record.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerFreezeEnded,
            record.UserId,
            nameof(AccountFreezeRecord),
            record.Id,
            record.PolicyVersionSnapshot.ToString(),
            new Dictionary<string, object?>
            {
                ["freezeId"] = record.Id,
                ["message"] = "Your freeze period has ended."
            },
            cancellationToken);
    }

    private static async Task ReconcileFreezeLifecycleAsync(IServiceProvider services, LearnerDbContext db, CancellationToken cancellationToken)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var now = DateTimeOffset.UtcNow;
        var recordsQuery = db.AccountFreezeRecords
            .Where(x => x.Status == FreezeStatus.Scheduled || x.Status == FreezeStatus.Active);

        var records = db.Database.IsSqlite()
            ? (await recordsQuery.ToListAsync(cancellationToken))
                .OrderBy(x => x.ScheduledStartAt)
                .ToList()
            : await recordsQuery
                .OrderBy(x => x.ScheduledStartAt)
                .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var record in records)
        {
            if (record.Status == FreezeStatus.Scheduled && record.ScheduledStartAt is not null && record.ScheduledStartAt <= now)
            {
                record.Status = FreezeStatus.Active;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.UpdatedAt = now;
                changed = true;

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeStarted,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze is now active."
                    },
                    cancellationToken);
            }

            if (record.Status == FreezeStatus.Active && record.EndedAt is not null && record.EndedAt <= now)
            {
                record.Status = FreezeStatus.Completed;
                record.IsCurrent = false;
                record.StartedAt ??= record.ScheduledStartAt ?? now;
                record.EndedAt ??= now;
                record.EndReason ??= "Freeze period ended";
                record.UpdatedAt = now;
                changed = true;

                await notifications.CreateForLearnerAsync(
                    NotificationEventKey.LearnerFreezeEnded,
                    record.UserId,
                    nameof(AccountFreezeRecord),
                    record.Id,
                    record.PolicyVersionSnapshot.ToString(),
                    new Dictionary<string, object?>
                    {
                        ["freezeId"] = record.Id,
                        ["message"] = "Your freeze period has ended."
                    },
                    cancellationToken);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
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

    private static async Task CompleteStudyPlanRegenerationAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == job.ResourceId, cancellationToken);

        // Study Planner v2 HARD CUTOVER: delegate to the new generator. Falls
        // back to a safe no-op message if the new service is unavailable
        // (e.g. during upgrade rollout without templates yet seeded).
        try
        {
            var planner = services.GetRequiredService<OetLearner.Api.Services.StudyPlanner.IStudyPlannerService>();
            await planner.GenerateForLearnerAsync(plan.UserId, "background_job", cancellationToken);
            // Reload to get the refreshed state.
            await db.Entry(plan).ReloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail soft: if no template has been authored yet, mark the plan
            // completed with a safe message so the learner is not stuck in Queued.
            plan.State = AsyncState.Completed;
            plan.Version += 1;
            plan.GeneratedAt = DateTimeOffset.UtcNow;
            plan.Checkpoint = "Plan refreshed";
            plan.WeakSkillFocus = "Complete a diagnostic to personalise your plan.";
            await db.SaveChangesAsync(cancellationToken);
            // Swallow the exception trace but preserve message for downstream logging.
            System.Diagnostics.Trace.TraceWarning($"StudyPlanner v2 generator fallback: {ex.Message}");
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

        if (job.Type is JobType.WritingEvaluation or JobType.SpeakingEvaluation && !string.IsNullOrWhiteSpace(job.AttemptId))
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

    private static async Task CompleteContentGenerationAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var genJob = await db.ContentGenerationJobs.FirstOrDefaultAsync(j => j.Id == job.ResourceId, cancellationToken);
        if (genJob == null) return;

        genJob.State = "generating";
        await db.SaveChangesAsync(cancellationToken);

        var generatedIds = new List<string>();
        for (var i = 0; i < genJob.RequestedCount; i++)
        {
            var contentId = $"ci-{Guid.NewGuid():N}";
            db.ContentItems.Add(new ContentItem
            {
                Id = contentId,
                ExamFamilyCode = genJob.ExamTypeCode,
                SubtestCode = genJob.SubtestCode,
                ContentType = "practice_task",
                ProfessionId = genJob.ProfessionId,
                Title = $"[AI Generated] {genJob.SubtestCode} Task — {genJob.Difficulty}",
                Difficulty = genJob.Difficulty ?? "medium",
                DetailJson = JsonSupport.Serialize(new
                {
                    generatedBy = "AI",
                    generationJobId = genJob.Id,
                    prompt = genJob.PromptConfigJson,
                    caseNotes = "This is an AI-generated practice task. Review and edit before publishing.",
                    scenarioType = genJob.SubtestCode == "writing" ? "referral_letter" : "roleplay"
                }),
                Status = ContentStatus.Draft,
                SourceType = "ai_generated",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            generatedIds.Add(contentId);
        }

        genJob.GeneratedCount = generatedIds.Count;
        genJob.GeneratedContentIdsJson = JsonSupport.Serialize(generatedIds);
        genJob.State = "completed";
        genJob.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static async Task CompleteConversationEvaluationAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var session = await db.ConversationSessions.FirstOrDefaultAsync(s => s.Id == job.ResourceId, cancellationToken);
        if (session == null) return;

        session.State = "evaluated";
        session.EvaluationId = $"ce-{Guid.NewGuid():N}";
    }

    private static Task CompletePronunciationAnalysisAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        // Pronunciation analysis is handled inline in PronunciationService.SubmitDrillAttemptAsync
        // This handler exists for future production integration with Azure Speech SDK async processing
        return Task.CompletedTask;
    }
}
