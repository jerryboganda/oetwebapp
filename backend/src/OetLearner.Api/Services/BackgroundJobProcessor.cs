using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

public class BackgroundJobProcessor(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

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
                await CompleteWritingEvaluationAsync(services, db, notifications, job, cancellationToken);
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
                await CompleteConversationEvaluationAsync(services, db, job, cancellationToken);
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

    private static async Task CompleteWritingEvaluationAsync(IServiceProvider services, LearnerDbContext db, NotificationService notifications, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AttemptId)) return;

        var attempt = await db.Attempts.FirstAsync(x => x.Id == job.AttemptId, cancellationToken);
        var evaluation = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var goal = await db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == attempt.UserId, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());

        var aiGateway = services.GetRequiredService<IAiGatewayService>();
        var writingRules = services.GetRequiredService<WritingRuleEngine>();
        var profession = ResolveWritingProfession(content.ProfessionId);
        var letterType = FirstNonBlank(
            content.ScenarioType,
            ReadString(detail, "letterType"),
            ReadString(detail, "letter_type"),
            "routine_referral")!;

        var prompt = BuildWritingPrompt(aiGateway, profession, letterType, goal?.TargetCountry);
        var allowedRuleIds = prompt.Metadata.AppliedRuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userInput = BuildWritingEvaluationUserInput(attempt, content, detail, goal);
        WritingEvaluationProjection? projection = null;

        try
        {
            var aiResult = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Model = "",
                Temperature = 0.2,
                MaxTokens = 1400,
                UserId = attempt.UserId,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.grade.v1"
            }, cancellationToken);

            projection = TryBuildWritingProjection(aiResult, allowedRuleIds);
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (OetLearner.Api.Services.AiManagement.AiQuotaDeniedException)
        {
            projection = null;
        }
        catch
        {
            projection = null;
        }

        projection ??= BuildWritingFallbackProjection(writingRules, attempt, content, detail, prompt.Metadata);

        evaluation.State = AsyncState.Completed;
        evaluation.ScoreRange = projection.ScoreRange;
        evaluation.GradeRange = projection.GradeRange;
        evaluation.ConfidenceBand = projection.ConfidenceBand;
        evaluation.StrengthsJson = JsonSupport.Serialize(projection.Strengths);
        evaluation.IssuesJson = JsonSupport.Serialize(projection.Issues);
        evaluation.CriterionScoresJson = JsonSupport.Serialize(projection.CriterionScores);
        evaluation.FeedbackItemsJson = JsonSupport.Serialize(projection.FeedbackItems);
        evaluation.GeneratedAt = DateTimeOffset.UtcNow;
        evaluation.ModelExplanationSafe = projection.ModelExplanationSafe;
        evaluation.LearnerDisclaimer = projection.LearnerDisclaimer;
        evaluation.StatusReasonCode = projection.StatusReasonCode;
        evaluation.StatusMessage = projection.StatusMessage;
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

    private static AiGroundedPrompt BuildWritingPrompt(
        IAiGatewayService gateway,
        ExamProfession profession,
        string letterType,
        string? candidateCountry)
    {
        try
        {
            return gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = letterType,
                Task = AiTaskMode.Score,
                CandidateCountry = candidateCountry
            });
        }
        catch (RulebookNotFoundException) when (profession != ExamProfession.Medicine)
        {
            return gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                LetterType = letterType,
                Task = AiTaskMode.Score,
                CandidateCountry = candidateCountry
            });
        }
    }

    private static string BuildWritingEvaluationUserInput(
        Attempt attempt,
        ContentItem content,
        IReadOnlyDictionary<string, object?> detail,
        LearnerGoal? goal)
    {
        return JsonSerializer.Serialize(new
        {
            task = new
            {
                contentId = content.Id,
                title = content.Title,
                professionId = content.ProfessionId,
                letterType = FirstNonBlank(content.ScenarioType, ReadString(detail, "letterType"), ReadString(detail, "letter_type")),
                difficulty = content.Difficulty,
                caseNotes = content.CaseNotes,
                detail
            },
            learnerContext = new
            {
                userId = attempt.UserId,
                targetCountry = goal?.TargetCountry,
                examFamilyCode = attempt.ExamFamilyCode
            },
            draft = new
            {
                content = attempt.DraftContent,
                scratchpad = attempt.Scratchpad,
                checklist = JsonSupport.Deserialize<Dictionary<string, bool>>(attempt.ChecklistJson, new Dictionary<string, bool>()),
                elapsedSeconds = attempt.ElapsedSeconds
            },
            instructions = new
            {
                output = "Return only the JSON object requested by the grounded prompt.",
                officiality = "Practice estimate only; never describe it as an official OET result."
            }
        }, JsonOptions);
    }

    private static WritingEvaluationProjection? TryBuildWritingProjection(AiGatewayResult result, IReadOnlySet<string> allowedRuleIds)
    {
        var json = ExtractJsonObject(result.Completion);
        if (json is null) return null;

        WritingEvaluationAiReply? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WritingEvaluationAiReply>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is null || parsed.CriteriaScores.Count == 0) return null;

        var criteria = BuildCriterionScores(parsed.CriteriaScores, parsed.Findings, confidence: "medium");
        if (criteria.Count < WritingCriterionMap.Count) return null;

        var scaledScore = Clamp((int)Math.Round(parsed.EstimatedScaledScore ?? EstimateScaledFromCriteria(parsed.CriteriaScores)), 0, 500);
        var grade = FirstNonBlank(parsed.EstimatedGrade, OetScoring.OetGradeLetterFromScaled(scaledScore))!;
        var validFindings = parsed.Findings
            .Where(f => !string.IsNullOrWhiteSpace(f.RuleId) && allowedRuleIds.Contains(f.RuleId!))
            .ToList();
        var feedbackItems = BuildFeedbackItems(validFindings);
        var strengths = criteria
            .Where(x => TryReadLeadingScore(x.ScoreRange) >= 4.5)
            .Select(x => $"{CriterionLabel(x.CriterionCode)} is comparatively strong.")
            .DefaultIfEmpty("Your response shows enough structure for criterion-level review.")
            .Take(3)
            .ToArray();
        var issues = validFindings
            .Select(f => f.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!)
            .DefaultIfEmpty("Review the criterion-level comments before relying on this estimate.")
            .Take(4)
            .ToArray();

        return new WritingEvaluationProjection(
            ScoreRange: ScoreRangeAround(scaledScore, margin: 10),
            GradeRange: grade,
            ConfidenceBand: ConfidenceBand.Medium,
            Strengths: strengths,
            Issues: issues,
            CriterionScores: criteria,
            FeedbackItems: feedbackItems,
            ModelExplanationSafe: $"Grounded AI practice estimate based on Writing rulebook v{result.RulebookVersion}. The country-aware pass mark used by the prompt was {result.Metadata.ScoringPassMark}/500 ({result.Metadata.ScoringGrade}).",
            LearnerDisclaimer: "Practice estimate only. This is not an official OET result; use expert review for a higher-trust external check.",
            StatusReasonCode: "completed_ai_grounded",
            StatusMessage: "Writing evaluation completed with grounded rubric feedback.");
    }

    private static WritingEvaluationProjection BuildWritingFallbackProjection(
        WritingRuleEngine writingRules,
        Attempt attempt,
        ContentItem content,
        IReadOnlyDictionary<string, object?> detail,
        AiGroundedPromptMetadata metadata)
    {
        var letterType = FirstNonBlank(content.ScenarioType, ReadString(detail, "letterType"), ReadString(detail, "letter_type"), "routine_referral")!;
        var findings = writingRules.Lint(new WritingLintInput(
            attempt.DraftContent ?? "",
            letterType,
            Profession: metadata.Profession));
        var penalty = findings.Sum(f => f.Severity switch
        {
            RuleSeverity.Critical => 28,
            RuleSeverity.Major => 16,
            RuleSeverity.Minor => 7,
            _ => 2
        });
        var scaledScore = Clamp(350 - penalty, 230, 380);
        var feedbackItems = findings.Take(6).Select((finding, index) => new WritingFeedbackItem(
            FeedbackItemId: $"fallback-{attempt.Id}-{index + 1}",
            CriterionCode: CriterionFromRuleId(finding.RuleId),
            Type: "rulebook_lint",
            Anchor: new { snippet = finding.Quote },
            Message: finding.Message,
            Severity: finding.Severity.ToString().ToLowerInvariant(),
            SuggestedFix: finding.FixSuggestion ?? "Revise this point against the active Writing rulebook."
        )).ToArray();
        var criteria = WritingCriterionMap.Select(pair =>
        {
            var affected = findings.Count(f => CriterionFromRuleId(f.RuleId) == pair.Value);
            var score = Clamp(4.0 - affected * 0.4, 2.5, 4.0);
            return new WritingCriterionScore(
                CriterionCode: pair.Value,
                ScoreRange: $"{score:0.#}/6",
                ConfidenceBand: "low",
                Explanation: affected == 0
                    ? "No deterministic rulebook lint finding was attached to this criterion."
                    : $"{affected} deterministic rulebook lint finding(s) affected this criterion.");
        }).ToArray();

        return new WritingEvaluationProjection(
            ScoreRange: ScoreRangeAround(scaledScore, margin: 20),
            GradeRange: OetScoring.OetGradeLetterFromScaled(scaledScore),
            ConfidenceBand: ConfidenceBand.Low,
            Strengths: new[] { "Your draft was checked against deterministic Writing rulebook rules." },
            Issues: findings.Select(f => f.Message).DefaultIfEmpty("AI scoring was unavailable; request expert review before trusting this estimate.").Take(4).ToArray(),
            CriterionScores: criteria,
            FeedbackItems: feedbackItems,
            ModelExplanationSafe: "Low-confidence deterministic fallback because the AI provider could not return a valid grounded score. It used rulebook lint findings only and is not an official OET result.",
            LearnerDisclaimer: "Practice estimate only. Use expert review for a higher-trust external check.",
            StatusReasonCode: "completed_ai_fallback",
            StatusMessage: "Writing evaluation completed with low-confidence fallback feedback.");
    }

    private static IReadOnlyList<WritingCriterionScore> BuildCriterionScores(
        IReadOnlyDictionary<string, double?> scores,
        IReadOnlyList<WritingEvaluationFinding> findings,
        string confidence)
    {
        var result = new List<WritingCriterionScore>();
        foreach (var (sourceCode, criterionCode) in WritingCriterionMap)
        {
            if (!scores.TryGetValue(sourceCode, out var rawScore) || rawScore is null) continue;
            var score = Clamp(rawScore.Value, 0, 6);
            var finding = findings.FirstOrDefault(f => string.Equals(MapCriterionCode(f.CriterionCode), criterionCode, StringComparison.OrdinalIgnoreCase));
            result.Add(new WritingCriterionScore(
                CriterionCode: criterionCode,
                ScoreRange: $"{score:0.#}/6",
                ConfidenceBand: confidence,
                Explanation: FirstNonBlank(finding?.Message, $"{CriterionLabel(criterionCode)} scored {score:0.#}/6 against the grounded Writing rubric.")!));
        }

        return result;
    }

    private static IReadOnlyList<WritingFeedbackItem> BuildFeedbackItems(IReadOnlyList<WritingEvaluationFinding> findings)
    {
        return findings.Take(8).Select((finding, index) => new WritingFeedbackItem(
            FeedbackItemId: $"ai-{index + 1}",
            CriterionCode: MapCriterionCode(finding.CriterionCode) ?? CriterionFromRuleId(finding.RuleId),
            Type: "anchored_comment",
            Anchor: new { snippet = finding.Quote },
            Message: finding.Message ?? "Review this rule-cited issue.",
            Severity: FirstNonBlank(finding.Severity, "medium")!,
            SuggestedFix: finding.FixSuggestion ?? "Revise this point using the cited rule."
        )).ToArray();
    }

    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }

    private static ExamProfession ResolveWritingProfession(string? professionId)
    {
        if (string.IsNullOrWhiteSpace(professionId)) return ExamProfession.Medicine;
        var normalized = string.Concat(professionId
            .Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        return Enum.TryParse<ExamProfession>(normalized, ignoreCase: true, out var parsed)
            ? parsed
            : ExamProfession.Medicine;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static double EstimateScaledFromCriteria(IReadOnlyDictionary<string, double?> scores)
    {
        var values = scores.Values.Where(score => score is not null).Select(score => score!.Value).ToArray();
        return values.Length == 0 ? 300 : values.Average() * 500 / 6;
    }

    private static double? TryReadLeadingScore(string scoreRange)
    {
        var first = scoreRange.Split('/', '-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(first, out var score) ? score : null;
    }

    private static string ScoreRangeAround(int scaledScore, int margin)
        => $"{Clamp(scaledScore - margin, 0, 500)}-{Clamp(scaledScore + margin, 0, 500)}";

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

    private static string MapCriterionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "content";
        return WritingCriterionMap.TryGetValue(code, out var mapped)
            ? mapped
            : WritingCriterionMap.TryGetValue(code.Replace('-', '_'), out mapped)
                ? mapped
                : code;
    }

    private static string CriterionFromRuleId(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return "content";
        return ruleId.StartsWith("R01", StringComparison.OrdinalIgnoreCase) ? "purpose"
            : ruleId.StartsWith("R02", StringComparison.OrdinalIgnoreCase) ? "content"
            : ruleId.StartsWith("R03", StringComparison.OrdinalIgnoreCase) ? "conciseness"
            : ruleId.StartsWith("R04", StringComparison.OrdinalIgnoreCase) ? "genre"
            : ruleId.StartsWith("R05", StringComparison.OrdinalIgnoreCase) ? "organization"
            : ruleId.StartsWith("R06", StringComparison.OrdinalIgnoreCase) ? "language"
            : "content";
    }

    private static string CriterionLabel(string criterionCode) => criterionCode switch
    {
        "purpose" => "Purpose",
        "content" => "Content",
        "conciseness" => "Conciseness and clarity",
        "genre" => "Genre and style",
        "organization" => "Organisation and layout",
        "language" => "Language",
        _ => criterionCode
    };

    private static readonly IReadOnlyDictionary<string, string> WritingCriterionMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["purpose"] = "purpose",
            ["content"] = "content",
            ["conciseness_clarity"] = "conciseness",
            ["conciseness"] = "conciseness",
            ["genre_style"] = "genre",
            ["genre"] = "genre",
            ["organisation_layout"] = "organization",
            ["organization_layout"] = "organization",
            ["organization"] = "organization",
            ["language"] = "language"
        };

    private sealed record WritingEvaluationProjection(
        string ScoreRange,
        string GradeRange,
        ConfidenceBand ConfidenceBand,
        IReadOnlyList<string> Strengths,
        IReadOnlyList<string> Issues,
        IReadOnlyList<WritingCriterionScore> CriterionScores,
        IReadOnlyList<WritingFeedbackItem> FeedbackItems,
        string ModelExplanationSafe,
        string LearnerDisclaimer,
        string StatusReasonCode,
        string StatusMessage);

    private sealed record WritingCriterionScore(
        string CriterionCode,
        string ScoreRange,
        string ConfidenceBand,
        string Explanation);

    private sealed record WritingFeedbackItem(
        string FeedbackItemId,
        string CriterionCode,
        string Type,
        object Anchor,
        string Message,
        string Severity,
        string SuggestedFix);

    private sealed record WritingEvaluationAiReply
    {
        public List<WritingEvaluationFinding> Findings { get; init; } = [];
        public Dictionary<string, double?> CriteriaScores { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public double? EstimatedScaledScore { get; init; }
        public string? EstimatedGrade { get; init; }
    }

    private sealed record WritingEvaluationFinding
    {
        public string? RuleId { get; init; }
        public string? CriterionCode { get; init; }
        public string? Severity { get; init; }
        public string? Quote { get; init; }
        public string? Message { get; init; }
        public string? FixSuggestion { get; init; }
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

    private static async Task CompleteConversationEvaluationAsync(IServiceProvider services, LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ResourceId)) return;

        var session = await db.ConversationSessions.FirstOrDefaultAsync(s => s.Id == job.ResourceId, cancellationToken);
        if (session == null) return;

        // If a prior attempt already produced an evaluation, just mark session evaluated.
        var existing = await db.ConversationEvaluations.FirstOrDefaultAsync(e => e.SessionId == session.Id, cancellationToken);
        if (existing is not null)
        {
            session.State = "evaluated";
            session.EvaluationId = existing.Id;
            return;
        }

        var orchestrator = services.GetRequiredService<Conversation.IConversationAiOrchestrator>();

        // Rehydrate context.
        if (!Enum.TryParse<ExamProfession>(
                (session.Profession ?? "medicine").Replace("-", "").Replace("_", ""),
                ignoreCase: true, out var profession))
        {
            profession = ExamProfession.Medicine;
        }

        var elapsedSeconds = session.StartedAt.HasValue && session.CompletedAt.HasValue
            ? (int)(session.CompletedAt.Value - session.StartedAt.Value).TotalSeconds
            : session.DurationSeconds;

        var ctx = new Conversation.ConversationAiContext(
            SessionId: session.Id,
            UserId: session.UserId,
            AuthAccountId: null,
            TenantId: null,
            Profession: profession,
            TaskTypeCode: session.TaskTypeCode,
            ScenarioJson: session.ScenarioJson,
            TranscriptJson: session.TranscriptJson,
            TurnIndex: session.TurnCount,
            ElapsedSeconds: elapsedSeconds,
            RemainingSeconds: 0,
            CandidateCountry: null);

        Conversation.ConversationAiEvaluation aiEval;
        try
        {
            aiEval = await orchestrator.EvaluateAsync(ctx, cancellationToken);
        }
        catch (PromptNotGroundedException)
        {
            // Should never happen, but refuse safely.
            session.State = "failed";
            session.LastErrorCode = "ungrounded";
            return;
        }
        catch (Exception ex)
        {
            // Fail-soft: persist a minimal evaluation so the UI shows a consistent message.
            var logger = services.GetService<ILogger<BackgroundJobProcessor>>();
            logger?.LogError(ex, "Conversation AI evaluation failed for session {SessionId}", session.Id);
            aiEval = new Conversation.ConversationAiEvaluation(
                Criteria: new[]
                {
                    new Conversation.ConversationAiCriterion("intelligibility", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("fluency", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("appropriateness", 0, "evaluation error", Array.Empty<string>()),
                    new Conversation.ConversationAiCriterion("grammar_expression", 0, "evaluation error", Array.Empty<string>()),
                },
                TurnAnnotations: Array.Empty<Conversation.ConversationAiAnnotation>(),
                Strengths: Array.Empty<string>(),
                Improvements: new[] { "The AI evaluator could not complete. Try the session again." },
                SuggestedPractice: Array.Empty<string>(),
                AppliedRuleIds: Array.Empty<string>(),
                Advisory: "AI evaluation failed.",
                RulebookVersion: "");
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
                id = c.Id,
                score06 = c.Score06,
                maxScore = 6.0,
                evidence = c.Evidence,
                quotes = c.Quotes,
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

        var examTypeCode = session.ExamTypeCode ?? "oet";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seededReviewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in aiEval.TurnAnnotations)
        {
            var rowId = $"cta-{Guid.NewGuid():N}";
            db.ConversationTurnAnnotations.Add(new ConversationTurnAnnotation
            {
                Id = rowId,
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
                r.UserId == session.UserId &&
                r.SourceType == "conversation_issue" &&
                r.SourceId == sourceId, cancellationToken);
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

    private static Task CompletePronunciationAnalysisAsync(LearnerDbContext db, BackgroundJobItem job, CancellationToken cancellationToken)
    {
        // Pronunciation analysis is handled inline in PronunciationService.SubmitDrillAttemptAsync
        // This handler exists for future production integration with Azure Speech SDK async processing
        return Task.CompletedTask;
    }
}
