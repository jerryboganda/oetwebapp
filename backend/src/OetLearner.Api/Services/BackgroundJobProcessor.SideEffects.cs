using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public partial class BackgroundJobProcessor
{
    /// <summary>
    /// Option C background exemplar worker: generates one task's
    /// pre-generated Model Answer with no HTTP timeout pressure. The work
    /// item itself is idempotent (Ready + fresh answers are skipped with zero
    /// provider calls), so redelivery and stuck-job recovery after a restart
    /// can never duplicate paid AI work. Transient infrastructure failures
    /// throw so the standard bounded-retry machinery applies; content/quality
    /// holds complete the job for human follow-up instead of burning retries.
    /// </summary>
    private static async Task CompleteWritingModelAnswerGenerationAsync(
        IServiceProvider services, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(job.ResourceId, out var scenarioId))
        {
            throw new InvalidOperationException($"Model-answer job {job.Id} carries an invalid scenario id.");
        }

        string adminUserId = "system:background-worker";
        try
        {
            using var document = JsonDocument.Parse(job.PayloadJson ?? "{}");
            if (document.RootElement.TryGetProperty("requestedBy", out var requestedBy)
                && requestedBy.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(requestedBy.GetString()))
            {
                adminUserId = requestedBy.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Malformed payload: fall back to the worker identity rather than failing the job.
        }

        var modelAnswers = services.GetRequiredService<OetLearner.Api.Services.Writing.IWritingTaskModelAnswerService>();
        var outcome = await modelAnswers.GenerateIfNeededAsync(scenarioId, adminUserId, cancellationToken);
        job.StatusMessage = $"Model-answer worker outcome for {scenarioId}: {outcome.Outcome}."
            + (outcome.HoldReason is null ? string.Empty : $" Hold: {outcome.HoldReason}.");

        if (OetLearner.Api.Services.Writing.WritingTaskModelAnswerService.IsTransientHold(outcome.HoldReason)
            && !string.Equals(outcome.Outcome, "generated", StringComparison.Ordinal)
            && !string.Equals(outcome.Outcome, "ready-skipped", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Transient model-answer failure for scenario {scenarioId}: {outcome.HoldReason}.");
        }
    }

    private static async Task CompleteWritingEvaluationSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);

        // Only emit success-side analytics + notifications when the
        // pipeline actually completed grading. On Failed evaluations the
        // pipeline preserves rule-engine findings and surfaces the failure
        // through evaluation.StatusReasonCode; the evaluation_failed-style
        // notification path lives elsewhere (job retry / SLA worker).
        if (evaluation.State != AsyncState.Completed)
        {
            return;
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
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

    private static async Task CompleteSpeakingEvaluationSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await SpeakingEvaluationPipeline.FindEvaluationForJobAsync(db, job, cancellationToken);
        if (evaluation is null) return;

        if (evaluation.State != AsyncState.Completed)
        {
            var failureVersion = evaluation.LastTransitionAt.UtcDateTime.Ticks.ToString();
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerEvaluationFailed,
                attempt.UserId,
                "attempt",
                attempt.Id,
                failureVersion,
                new Dictionary<string, object?>
                {
                    ["attemptId"] = attempt.Id,
                    ["subtest"] = "speaking",
                    ["message"] = evaluation.StatusMessage ?? "We could not finish your speaking evaluation automatically. Please try again shortly."
                },
                cancellationToken);
            return;
        }

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = attempt.UserId,
            EventName = "evaluation_completed",
            PayloadJson = JsonSupport.Serialize(new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking" }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
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

    private static async Task DiscardPendingJobSideEffectsAsync(LearnerDbContext db, string currentJobId, CancellationToken cancellationToken)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is BackgroundJobItem backgroundJob)
            {
                if (backgroundJob.Id == currentJobId)
                {
                    await entry.ReloadAsync(cancellationToken);
                }

                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                try
                {
                    await entry.ReloadAsync(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    private static async Task CompleteStudyPlanRegenerationAsync(
        IServiceProvider services,
        LearnerDbContext db,
        NotificationService notifications,
        BackgroundJobItem job,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var priorPlan = await db.StudyPlans.FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (priorPlan is null) return;

        var generator = services.GetRequiredService<OetLearner.Api.Services.Planner.IStudyPlanGenerator>();
        var trigger = ResolveTrigger(job);
        var result = await generator.GenerateAsync(priorPlan.UserId, trigger, cancellationToken);
        job.ResourceId = result.PlanId;

        if (result.SkippedBecauseUnchanged)
        {
            // Mark prior plan back to Completed; nothing materially changed.
            priorPlan.State = AsyncState.Completed;
            return;
        }

        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerStudyPlanRegenerated,
            priorPlan.UserId,
            "study_plan",
            result.PlanId,
            result.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = $"Your study plan has been refreshed (v{result.Version}, {result.ItemsCreated} new tasks).",
                ["templateId"] = result.TemplateId,
                ["trigger"] = trigger.ToString()
            },
            cancellationToken);

        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = priorPlan.UserId,
            EventName = "study_plan_generated",
            PayloadJson = JsonSupport.Serialize(new
            {
                planId = result.PlanId,
                version = result.Version,
                itemsCreated = result.ItemsCreated,
                itemsPreserved = result.ItemsPreservedFromPrior,
                templateId = result.TemplateId,
                trigger = trigger.ToString()
            }),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static OetLearner.Api.Services.Planner.StudyPlanGenerationTrigger ResolveTrigger(BackgroundJobItem job)
    {
        if (string.IsNullOrWhiteSpace(job.PayloadJson))
        {
            return OetLearner.Api.Services.Planner.StudyPlanGenerationTrigger.Manual;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(job.PayloadJson);
            if (doc.RootElement.TryGetProperty("trigger", out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (Enum.TryParse<OetLearner.Api.Services.Planner.StudyPlanGenerationTrigger>(prop.GetString(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // fall through
        }

        return OetLearner.Api.Services.Planner.StudyPlanGenerationTrigger.Manual;
    }

    private static async Task CompleteReviewRequestAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
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

            // Trigger study plan regeneration after tutor review completes
            var readiness = await RefreshReadinessAsync(services, db, attempt.UserId, cancellationToken);
            await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerReadinessUpdated,
                attempt.UserId,
                "readiness_snapshot",
                readiness.Id,
                readiness.Version.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = "Your readiness snapshot was updated after tutor review feedback was applied."
                },
                cancellationToken);
        }
    }

    private static async Task<ReadinessSnapshot> RefreshReadinessAsync(IServiceProvider services, LearnerDbContext db, string userId, CancellationToken cancellationToken)
    {
        var computation = services.GetRequiredService<OetLearner.Api.Services.Readiness.ReadinessComputationService>();
        return await computation.ComputeAsync(userId, cancellationToken);
    }

    private static async Task CompleteMockReportSideEffectsAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;
        var mockAttempt = await db.MockAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.ResourceId, cancellationToken);
        if (mockAttempt is null) return;

        var readiness = await RefreshReadinessAsync(services, db, mockAttempt.UserId, cancellationToken);
        await notifications.CreateForLearnerAsync(
            NotificationEventKey.LearnerReadinessUpdated,
            mockAttempt.UserId,
            "readiness_snapshot",
            readiness.Id,
            readiness.Version.ToString(),
            new Dictionary<string, object?>
            {
                ["message"] = "Your readiness snapshot was recalculated after your mock report finished generating."
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

    private static async Task CompleteConversationEvaluationAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var session = await db.ConversationSessions.FirstOrDefaultAsync(s => s.Id == job.ResourceId, cancellationToken);
        if (session == null) return;

        var existing = await db.ConversationEvaluations.FirstOrDefaultAsync(e => e.SessionId == session.Id, cancellationToken);
        if (existing is not null)
        {
            session.State = "evaluated";
            session.EvaluationId = existing.Id;
            return;
        }

        var orchestrator = services.GetRequiredService<Conversation.IConversationAiOrchestrator>();

        if (!Enum.TryParse<OetLearner.Api.Services.Rulebook.ExamProfession>(
                (session.Profession ?? "medicine").Replace("-", "").Replace("_", ""),
                ignoreCase: true, out var profession))
            profession = OetLearner.Api.Services.Rulebook.ExamProfession.Medicine;

        var elapsedSeconds = session.StartedAt.HasValue && session.CompletedAt.HasValue
            ? (int)(session.CompletedAt.Value - session.StartedAt.Value).TotalSeconds
            : session.DurationSeconds;

        var ctx = new Conversation.ConversationAiContext(
            session.Id, session.UserId, null, null, profession,
            session.TaskTypeCode, session.ScenarioJson, session.TranscriptJson,
            session.TurnCount, elapsedSeconds, 0, null);

        Conversation.ConversationAiEvaluation aiEval;
        try
        {
            aiEval = await orchestrator.EvaluateAsync(ctx, cancellationToken);
        }
        catch (OetLearner.Api.Services.Rulebook.PromptNotGroundedException)
        {
            session.State = "failed";
            session.LastErrorCode = "ungrounded";
            return;
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<BackgroundJobProcessor>>();
            logger?.LogError(ex, "Conversation AI evaluation failed for {SessionId}", session.Id);
            aiEval = new Conversation.ConversationAiEvaluation(
                new[]
                {
                    new Conversation.ConversationAiCriterion("intelligibility", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("fluency", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("appropriateness", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("grammar_expression", 0, "evaluation error", Array.Empty<string>()),
                },
                Array.Empty<Conversation.ConversationAiAnnotation>(),
                Array.Empty<string>(),
                new[] { "The AI evaluator could not complete. Try the session again." },
                Array.Empty<string>(), Array.Empty<string>(),
                "AI evaluation failed.", "");
        }

        var intelligibility = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("intelligibility", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var fluency = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("fluency", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var appropriateness = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("appropriateness", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;
        var grammarExpression = aiEval.Criteria.FirstOrDefault(c => c.Id.Equals("grammar_expression", StringComparison.OrdinalIgnoreCase))?.Score06 ?? 0;

        var mean = (intelligibility + fluency + appropriateness + grammarExpression) / 4.0;
        var scaled = OetScoring.ConversationProjectedScaled(mean);
        var band = OetScoring.GradeSpeaking(scaled);

        var evaluationId = $"ce-{Guid.NewGuid():N}";
        var evaluation = new ConversationEvaluation
        {
            Id = evaluationId,
            SessionId = session.Id,
            UserId = session.UserId,
            OverallScaled = band.ScaledScore,
            OverallGrade = band.Grade,
            Passed = band.Passed,
            CountryVariant = null,
            CriteriaJson = JsonSupport.Serialize(aiEval.Criteria.Select(c => new
            {
                id = c.Id, score06 = c.Score06, maxScore = 6.0, evidence = c.Evidence, quotes = c.Quotes,
            })),
            StrengthsJson = JsonSupport.Serialize(aiEval.Strengths),
            ImprovementsJson = JsonSupport.Serialize(aiEval.Improvements),
            SuggestedPracticeJson = JsonSupport.Serialize(aiEval.SuggestedPractice),
            AppliedRuleIdsJson = JsonSupport.Serialize(aiEval.AppliedRuleIds),
            RulebookVersion = aiEval.RulebookVersion,
            Advisory = aiEval.Advisory,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ConversationEvaluations.Add(evaluation);

        // Advisory Jev cross-check of the evaluation above (flag-gated, time-boxed, fail-soft).
        // Persists an AuditEvent only; the scores, band and credits computed above never change.
        await TryRecordJevConversationCrosscheckAsync(services, db, session, evaluationId, aiEval, cancellationToken);

        var examTypeCode = OetLearner.Api.Services.Common.ExamCodes.NormalizeOrNull(session.ExamTypeCode) ?? OetLearner.Api.Services.Common.ExamCodes.DefaultCode;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seededReviewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in aiEval.TurnAnnotations)
        {
            db.ConversationTurnAnnotations.Add(new ConversationTurnAnnotation
            {
                Id = $"cta-{Guid.NewGuid():N}",
                SessionId = session.Id,
                EvaluationId = evaluationId,
                TurnNumber = a.TurnNumber,
                Type = a.Type,
                Category = a.Category,
                RuleId = a.RuleId,
                Evidence = a.Evidence,
                Suggestion = a.Suggestion,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            if (!string.Equals(a.Type, "error", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(a.Type, "improvement", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(a.RuleId)) continue;

            var sourceId = $"{session.Id}:{a.TurnNumber}:{a.RuleId}";
            if (!seededReviewKeys.Add(sourceId)) continue;

            var existingReview = await db.ReviewItems.AnyAsync(r =>
                r.UserId == session.UserId && r.SourceType == "conversation_issue" && r.SourceId == sourceId,
                cancellationToken);
            if (existingReview) continue;

            db.ReviewItems.Add(new ReviewItem
            {
                Id = $"rv-{Guid.NewGuid():N}",
                UserId = session.UserId,
                ExamTypeCode = examTypeCode,
                SubtestCode = "speaking",
                SourceType = "conversation_issue",
                SourceId = sourceId,
                CriterionCode = a.Category,
                QuestionJson = JsonSupport.Serialize(new
                {
                    prompt = $"Conversation turn {a.TurnNumber}: {a.Evidence}",
                    ruleId = a.RuleId,
                    sessionId = session.Id,
                }),
                AnswerJson = JsonSupport.Serialize(new
                {
                    suggestion = a.Suggestion ?? "Revisit the rule and re-attempt this scenario.",
                    ruleId = a.RuleId,
                }),
                EaseFactor = 2.5,
                IntervalDays = 1,
                ReviewCount = 0,
                ConsecutiveCorrect = 0,
                DueDate = today.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "active",
            });
        }

        session.State = "evaluated";
        session.EvaluationId = evaluationId;
    }

    private static async Task TryRecordJevConversationCrosscheckAsync(
        IServiceProvider services,
        LearnerDbContext db,
        ConversationSession session,
        string evaluationId,
        Conversation.ConversationAiEvaluation aiEval,
        CancellationToken cancellationToken)
    {
        var tsOptions = services.GetService<Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.TypeSafeOptions>>()?.Value;
        if (!Ai.TypeSafe.JevConversationCrosscheck.Enabled(tsOptions)) return;
        var judgments = services.GetService<Ai.TypeSafe.ITypeSafeJudgmentService>();
        if (judgments is null) return;

        try
        {
            var turns = await db.ConversationTurns.AsNoTracking()
                .Where(t => t.SessionId == session.Id)
                .OrderBy(t => t.TurnNumber)
                .Select(t => new { t.TurnNumber, t.Role, t.Content, t.ConfidenceScore, t.ProviderName })
                .ToListAsync(cancellationToken);
            // Mock ASR context: nothing real to cross-check.
            if (turns.Any(t => string.Equals(t.ProviderName, "mock", StringComparison.OrdinalIgnoreCase))) return;

            // Fallback stubs (parse/evaluation error, defaulted criterion) are not grader judgments.
            var stubEvidence = new[] { "parse error", "evaluation error", "no evidence" };
            var criteria = aiEval.Criteria
                .Where(c => !stubEvidence.Contains(c.Evidence, StringComparer.OrdinalIgnoreCase))
                .Select(c => new Ai.TypeSafe.ConversationCriterionInput(c.Id, c.Score06))
                .ToList();

            var advisory = await Ai.TypeSafe.JevConversationCrosscheck.CrosscheckAsync(
                judgments, tsOptions!,
                turns.Select(t => new Ai.TypeSafe.ConversationCrosscheckTurn(t.TurnNumber, t.Role, t.Content, t.ConfidenceScore)).ToList(),
                criteria, session.UserId, session.Id, cancellationToken,
                logger: services.GetService<ILogger<BackgroundJobProcessor>>());
            var payload = Ai.TypeSafe.JevConversationCrosscheck.AdvisoryPayload(advisory, evaluationId);
            if (payload is null) return;

            db.AuditEvents.Add(Ai.TypeSafe.JevSpeakingAdvisor.ReviewEvent(
                Ai.TypeSafe.JevConversationCrosscheck.AdvisoryAction, "ConversationSession", session.Id,
                DateTimeOffset.UtcNow, payload));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            services.GetService<ILogger<BackgroundJobProcessor>>()
                ?.LogWarning(ex, "Jev conversation cross-check failed for {SessionId}; evaluation unaffected.", session.Id);
        }
    }

    private static Task CompletePronunciationAnalysisAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        // Pronunciation analysis is handled inline in PronunciationService.SubmitDrillAttemptAsync
        // This handler exists for future production integration with Azure Speech SDK async processing
        return Task.CompletedTask;
    }
}
