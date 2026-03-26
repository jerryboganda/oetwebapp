using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ExpertService(LearnerDbContext db, ILogger<ExpertService> logger, MediaStorageService mediaStorage, PlatformLinkService platformLinks)
{
    // ──────────── Expert Identity ────────────

    public async Task<object> GetMeAsync(string userId, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.FindAsync([userId], ct);
        if (expert is null)
        {
            expert = new ExpertUser
            {
                Id = userId,
                DisplayName = userId,
                Email = platformLinks.BuildFallbackEmail(userId),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ExpertUsers.Add(expert);
            await db.SaveChangesAsync(ct);
        }

        return new
        {
            userId = expert.Id,
            role = expert.Role,
            displayName = expert.DisplayName,
            email = expert.Email,
            timezone = expert.Timezone,
            isActive = expert.IsActive,
            specialties = JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>()),
            createdAt = expert.CreatedAt
        };
    }

    // ──────────── Review Queue ────────────

    public async Task<IEnumerable<object>> GetQueueAsync(string reviewerId, CancellationToken ct)
    {
        var assignments = await db.ExpertReviewAssignments
            .Where(a => a.ClaimState != ExpertAssignmentState.Released)
            .ToListAsync(ct);

        var reviewRequestIds = assignments.Select(a => a.ReviewRequestId).Distinct().ToList();

        var reviewRequests = await db.ReviewRequests
            .Where(rr => reviewRequestIds.Contains(rr.Id) ||
                         rr.State == ReviewRequestState.Queued ||
                         rr.State == ReviewRequestState.InReview)
            .ToListAsync(ct);

        var attemptIds = reviewRequests.Select(rr => rr.AttemptId).Distinct().ToList();
        var attempts = await db.Attempts
            .Where(a => attemptIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var userIds = attempts.Values.Select(a => a.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        var result = new List<object>();
        foreach (var rr in reviewRequests.OrderByDescending(r => r.CreatedAt))
        {
            var assignment = assignments.FirstOrDefault(a => a.ReviewRequestId == rr.Id);
            attempts.TryGetValue(rr.AttemptId, out var attempt);
            var learnerId = attempt?.UserId;
            LearnerUser? learner = null;
            if (learnerId is not null) users.TryGetValue(learnerId, out learner);

            result.Add(new
            {
                id = rr.Id,
                learnerId = learnerId ?? "unknown",
                learnerName = learner?.DisplayName ?? "Unknown",
                profession = learner?.ActiveProfessionId ?? "nursing",
                subTest = rr.SubtestCode ?? attempt?.SubtestCode ?? "writing",
                type = rr.SubtestCode ?? attempt?.SubtestCode ?? "writing",
                aiConfidence = "medium",
                priority = MapPriority(rr.TurnaroundOption),
                slaDue = rr.CreatedAt.AddHours(rr.TurnaroundOption == "express" ? 24 : 48).ToString("o"),
                assignedReviewerId = assignment?.AssignedReviewerId,
                assignedReviewerName = assignment?.AssignedReviewerId != null ? "Expert Reviewer" : (string?)null,
                status = MapReviewRequestToQueueStatus(rr.State, assignment?.ClaimState),
                contentId = attempt?.ContentId,
                attemptId = rr.AttemptId,
                createdAt = rr.CreatedAt.ToString("o")
            });
        }

        return result;
    }

    public async Task<object> ClaimReviewAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        var existing = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && a.ClaimState != ExpertAssignmentState.Released, ct);

        if (existing is not null && existing.AssignedReviewerId != reviewerId)
            throw new InvalidOperationException("Review is already claimed by another reviewer.");

        if (existing is not null)
            return new { claimed = true, reviewRequestId };

        var assignment = new ExpertReviewAssignment
        {
            Id = $"era-{Guid.NewGuid():N}",
            ReviewRequestId = reviewRequestId,
            AssignedReviewerId = reviewerId,
            AssignedAt = DateTimeOffset.UtcNow,
            ClaimState = ExpertAssignmentState.Claimed
        };
        db.ExpertReviewAssignments.Add(assignment);

        rr.State = ReviewRequestState.InReview;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Expert {ReviewerId} claimed review {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { claimed = true, reviewRequestId };
    }

    public async Task<object> ReleaseReviewAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId
                                      && a.AssignedReviewerId == reviewerId
                                      && a.ClaimState != ExpertAssignmentState.Released, ct)
            ?? throw new InvalidOperationException("No active assignment found for this reviewer.");

        assignment.ClaimState = ExpertAssignmentState.Released;
        assignment.ReleasedAt = DateTimeOffset.UtcNow;

        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct);
        if (rr is not null)
            rr.State = ReviewRequestState.Queued;

        await db.SaveChangesAsync(ct);
        return new { released = true, reviewRequestId };
    }

    // ──────────── Writing Review Detail ────────────

    public async Task<object> GetWritingReviewBundleAsync(string reviewRequestId, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        var attempt = await db.Attempts.FindAsync([rr.AttemptId], ct);
        var user = attempt is not null ? await db.Users.FindAsync([attempt.UserId], ct) : null;
        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && a.ClaimState != ExpertAssignmentState.Released, ct);

        ContentItem? content = null;
        if (attempt?.ContentId is not null)
            content = await db.ContentItems.FindAsync([attempt.ContentId], ct);

        var evaluation = await db.Evaluations
            .Where(e => e.AttemptId == rr.AttemptId)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var aiScores = evaluation is not null
            ? JsonSupport.Deserialize(evaluation.CriterionScoresJson, new Dictionary<string, int>())
            : new Dictionary<string, int>();

        return new
        {
            id = rr.Id,
            learnerId = attempt?.UserId ?? "unknown",
            learnerName = user?.DisplayName ?? "Unknown",
            profession = user?.ActiveProfessionId ?? "nursing",
            subTest = "writing",
            type = "writing",
            aiConfidence = "medium",
            priority = MapPriority(rr.TurnaroundOption),
            slaDue = rr.CreatedAt.AddHours(rr.TurnaroundOption == "express" ? 24 : 48).ToString("o"),
            assignedReviewerId = assignment?.AssignedReviewerId,
            assignedReviewerName = assignment?.AssignedReviewerId != null ? "Expert Reviewer" : (string?)null,
            status = MapReviewRequestToQueueStatus(rr.State, assignment?.ClaimState),
            contentId = attempt?.ContentId,
            attemptId = rr.AttemptId,
            createdAt = rr.CreatedAt.ToString("o"),
            learnerResponse = attempt?.DraftContent ?? "",
            caseNotes = content?.CaseNotes ?? "Patient admitted for assessment. Provide a summary letter to the GP.",
            aiDraftFeedback = evaluation?.FeedbackItemsJson ?? "AI feedback is being generated.",
            aiSuggestedScores = aiScores.Count > 0 ? (object)aiScores : new { purpose = 4, content = 4, conciseness = 3, genre = 4, organization = 4, language = 4 },
            modelAnswer = content?.ModelAnswerJson
        };
    }

    // ──────────── Speaking Review Detail ────────────

    public async Task<object> GetSpeakingReviewBundleAsync(string reviewRequestId, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        var attempt = await db.Attempts.FindAsync([rr.AttemptId], ct);
        var user = attempt is not null ? await db.Users.FindAsync([attempt.UserId], ct) : null;
        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId && a.ClaimState != ExpertAssignmentState.Released, ct);

        ContentItem? content = null;
        if (attempt?.ContentId is not null)
            content = await db.ContentItems.FindAsync([attempt.ContentId], ct);

        var evaluation = await db.Evaluations
            .Where(e => e.AttemptId == rr.AttemptId)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var aiScores = evaluation is not null
            ? JsonSupport.Deserialize(evaluation.CriterionScoresJson, new Dictionary<string, int>())
            : new Dictionary<string, int>();

        var roleCard = content is not null
            ? JsonSupport.Deserialize(content.DetailJson, new { role = "Nurse", setting = "Ward", patient = "Patient", task = "Provide handover", background = "" })
            : new { role = "Nurse", setting = "Ward", patient = "Patient", task = "Provide handover", background = "" };

        return new
        {
            id = rr.Id,
            learnerId = attempt?.UserId ?? "unknown",
            learnerName = user?.DisplayName ?? "Unknown",
            profession = user?.ActiveProfessionId ?? "nursing",
            subTest = "speaking",
            type = "speaking",
            aiConfidence = "medium",
            priority = MapPriority(rr.TurnaroundOption),
            slaDue = rr.CreatedAt.AddHours(rr.TurnaroundOption == "express" ? 24 : 48).ToString("o"),
            assignedReviewerId = assignment?.AssignedReviewerId,
            assignedReviewerName = assignment?.AssignedReviewerId != null ? "Expert Reviewer" : (string?)null,
            status = MapReviewRequestToQueueStatus(rr.State, assignment?.ClaimState),
            contentId = attempt?.ContentId,
            attemptId = rr.AttemptId,
            createdAt = rr.CreatedAt.ToString("o"),
            audioUrl = platformLinks.BuildApiUrl($"/v1/expert/reviews/{Uri.EscapeDataString(reviewRequestId)}/speaking/audio"),
            transcriptLines = JsonSupport.Deserialize(attempt?.TranscriptJson,
                new[]
                {
                    new { id = "etl-1", speaker = "candidate", startTime = 0.0, endTime = 6.0, text = "Sample transcript line." }
                }),
            roleCard,
            aiFlags = evaluation is not null
                ? JsonSupport.Deserialize(evaluation.FeedbackItemsJson, Array.Empty<object>())
                : Array.Empty<object>(),
            aiSuggestedScores = aiScores.Count > 0 ? (object)aiScores : new { intelligibility = 4, fluency = 3, appropriateness = 4, grammar = 4, clinicalCommunication = 4 }
        };
    }

    public async Task<StoredMediaFile> GetSpeakingReviewAudioAsync(string reviewRequestId, string reviewerId, CancellationToken ct)
    {
        await GetMeAsync(reviewerId, ct);

        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw ApiException.NotFound("review_request_not_found", "The requested review does not exist.");

        var attempt = await db.Attempts.FindAsync([rr.AttemptId], ct)
            ?? throw ApiException.NotFound("attempt_not_found", "The requested speaking attempt does not exist.");

        if (string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking attempt.");
        }

        var metadata = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.GetValueOrDefault("contentType")?.ToString();
        return mediaStorage.OpenRead(attempt.AudioObjectKey, contentType);
    }

    // ──────────── Draft Save / Submit ────────────

    public async Task<object> SaveDraftAsync(string reviewRequestId, string reviewerId, ExpertDraftSaveRequest request, CancellationToken ct)
    {
        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(d => d.ReviewRequestId == reviewRequestId && d.ReviewerId == reviewerId, ct);

        if (draft is null)
        {
            draft = new ExpertReviewDraft
            {
                Id = $"erd-{Guid.NewGuid():N}",
                ReviewRequestId = reviewRequestId,
                ReviewerId = reviewerId
            };
            db.ExpertReviewDrafts.Add(draft);
        }

        draft.RubricEntriesJson = JsonSupport.Serialize(request.Scores);
        draft.CriterionCommentsJson = JsonSupport.Serialize(request.CriterionComments);
        draft.FinalCommentDraft = request.FinalComment;
        if (request.AnchoredComments is not null)
            draft.AnchoredCommentsJson = JsonSupport.Serialize(request.AnchoredComments);
        if (request.TimestampComments is not null)
            draft.TimestampCommentsJson = JsonSupport.Serialize(request.TimestampComments);
        draft.Version = request.Version ?? draft.Version + 1;
        draft.State = "editing";
        draft.DraftSavedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return new
        {
            reviewRequestId,
            scores = request.Scores,
            criterionComments = request.CriterionComments,
            finalComment = request.FinalComment,
            comments = (object?)(request.AnchoredComments ?? (object?)request.TimestampComments ?? Array.Empty<object>()),
            savedAt = draft.DraftSavedAt.ToString("o")
        };
    }

    public async Task<object> SubmitWritingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        // Save final scores into the draft
        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(d => d.ReviewRequestId == reviewRequestId && d.ReviewerId == reviewerId, ct);

        if (draft is not null)
        {
            draft.RubricEntriesJson = JsonSupport.Serialize(request.Scores);
            draft.CriterionCommentsJson = JsonSupport.Serialize(request.CriterionComments);
            draft.FinalCommentDraft = request.FinalComment;
            draft.State = "submitted";
            draft.DraftSavedAt = DateTimeOffset.UtcNow;
        }

        rr.State = ReviewRequestState.Completed;
        rr.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Expert {ReviewerId} submitted writing review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> SubmitSpeakingReviewAsync(string reviewRequestId, string reviewerId, ExpertReviewSubmitRequest request, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        var draft = await db.ExpertReviewDrafts
            .FirstOrDefaultAsync(d => d.ReviewRequestId == reviewRequestId && d.ReviewerId == reviewerId, ct);

        if (draft is not null)
        {
            draft.RubricEntriesJson = JsonSupport.Serialize(request.Scores);
            draft.CriterionCommentsJson = JsonSupport.Serialize(request.CriterionComments);
            draft.FinalCommentDraft = request.FinalComment;
            draft.State = "submitted";
            draft.DraftSavedAt = DateTimeOffset.UtcNow;
        }

        rr.State = ReviewRequestState.Completed;
        rr.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Expert {ReviewerId} submitted speaking review for {ReviewRequestId}", reviewerId, reviewRequestId);
        return new { success = true, reviewRequestId };
    }

    public async Task<object> RequestReworkAsync(string reviewRequestId, string reviewerId, ExpertReworkRequest request, CancellationToken ct)
    {
        var rr = await db.ReviewRequests.FindAsync([reviewRequestId], ct)
            ?? throw new InvalidOperationException("Review request not found.");

        rr.State = ReviewRequestState.Queued;

        var assignment = await db.ExpertReviewAssignments
            .FirstOrDefaultAsync(a => a.ReviewRequestId == reviewRequestId
                                      && a.AssignedReviewerId == reviewerId
                                      && a.ClaimState != ExpertAssignmentState.Released, ct);
        if (assignment is not null)
        {
            assignment.ClaimState = ExpertAssignmentState.Released;
            assignment.ReleasedAt = DateTimeOffset.UtcNow;
            assignment.ReasonCode = request.Reason;
        }

        await db.SaveChangesAsync(ct);
        return new { reworkRequested = true, reviewRequestId };
    }

    // ──────────── Learner Profile ────────────

    public async Task<object> GetLearnerProfileAsync(string learnerId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([learnerId], ct)
            ?? throw new InvalidOperationException("Learner not found.");

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == learnerId, ct);

        var attempts = await db.Attempts
            .Where(a => a.UserId == learnerId && a.State == AttemptState.Completed)
            .ToListAsync(ct);

        var subtestGroups = attempts.GroupBy(a => a.SubtestCode).Select(g =>
        {
            var latest = g.OrderByDescending(a => a.SubmittedAt).First();
            return new
            {
                subTest = g.Key,
                latestScore = (int?)latest.ElapsedSeconds,
                latestGrade = (string?)null,
                attempts = g.Count()
            };
        }).ToList();

        // Join through attempts to find review requests for this learner
        var learnerAttemptIds = attempts.Select(a => a.Id).ToList();
        var reviewRequests = await db.ReviewRequests
            .Where(rr => learnerAttemptIds.Contains(rr.AttemptId) && rr.State == ReviewRequestState.Completed)
            .OrderByDescending(rr => rr.CompletedAt)
            .Take(10)
            .ToListAsync(ct);

        var priorReviews = reviewRequests.Select(rr => new
        {
            id = rr.Id,
            type = rr.SubtestCode ?? "writing",
            reviewerName = "Expert Reviewer",
            date = (rr.CompletedAt ?? rr.CreatedAt).ToString("o"),
            overallComment = "Review completed."
        }).ToList();

        return new
        {
            id = user.Id,
            name = user.DisplayName,
            profession = user.ActiveProfessionId ?? "nursing",
            goalScore = goal is not null ? $"{goal.TargetWritingScore ?? 350}+" : "350+",
            examDate = goal?.TargetExamDate?.ToString("o"),
            attemptsCount = attempts.Count,
            joinedAt = user.CreatedAt.ToString("o"),
            totalReviews = reviewRequests.Count,
            subTestScores = subtestGroups,
            priorReviews
        };
    }

    // ──────────── Calibration ────────────

    public async Task<IEnumerable<object>> GetCalibrationCasesAsync(string reviewerId, CancellationToken ct)
    {
        var cases = await db.ExpertCalibrationCases.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var results = await db.ExpertCalibrationResults
            .Where(r => r.ReviewerId == reviewerId)
            .ToDictionaryAsync(r => r.CalibrationCaseId, ct);

        return cases.Select(c =>
        {
            results.TryGetValue(c.Id, out var result);
            return (object)new
            {
                id = c.Id,
                title = c.Title,
                profession = c.ProfessionId,
                subTest = c.SubtestCode,
                type = c.SubtestCode,
                benchmarkScore = c.BenchmarkScore,
                reviewerScore = result?.ReviewerScore,
                status = result is not null ? "completed" : "pending",
                createdAt = c.CreatedAt.ToString("o")
            };
        });
    }

    public async Task<IEnumerable<object>> GetCalibrationNotesAsync(string reviewerId, CancellationToken ct)
    {
        var notes = await db.ExpertCalibrationNotes
            .Where(n => n.ReviewerId == reviewerId || n.ReviewerId == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return notes.Select(n => new
        {
            id = n.Id,
            type = n.Type.ToString().ToLowerInvariant(),
            message = n.Message,
            caseId = n.CaseId,
            createdAt = n.CreatedAt.ToString("o")
        });
    }

    public async Task<object> SubmitCalibrationAsync(string caseId, string reviewerId, ExpertCalibrationSubmitRequest request, CancellationToken ct)
    {
        var calibCase = await db.ExpertCalibrationCases.FindAsync([caseId], ct)
            ?? throw new InvalidOperationException("Calibration case not found.");

        var existing = await db.ExpertCalibrationResults
            .FirstOrDefaultAsync(r => r.CalibrationCaseId == caseId && r.ReviewerId == reviewerId, ct);

        if (existing is not null)
            throw new InvalidOperationException("Calibration already submitted for this case.");

        var totalScore = request.Scores.Values.Sum();
        var benchmarkTotal = calibCase.BenchmarkScore;
        var alignment = benchmarkTotal > 0
            ? Math.Round(100.0 - Math.Abs(totalScore - benchmarkTotal) / (double)benchmarkTotal * 100.0, 1)
            : 100.0;

        var result = new ExpertCalibrationResult
        {
            Id = $"ecr-{Guid.NewGuid():N}",
            CalibrationCaseId = caseId,
            ReviewerId = reviewerId,
            SubmittedRubricJson = JsonSupport.Serialize(request.Scores),
            ReviewerScore = totalScore,
            AlignmentScore = alignment,
            Notes = request.Notes ?? string.Empty,
            SubmittedAt = DateTimeOffset.UtcNow
        };
        db.ExpertCalibrationResults.Add(result);

        var note = new ExpertCalibrationNote
        {
            Id = $"ecn-{Guid.NewGuid():N}",
            Type = CalibrationNoteType.Completed,
            Message = $"Completed {calibCase.Title}. Alignment: {alignment}%.",
            CaseId = caseId,
            ReviewerId = reviewerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ExpertCalibrationNotes.Add(note);

        await db.SaveChangesAsync(ct);
        return new { submitted = true, caseId, alignment };
    }

    // ──────────── Schedule / Availability ────────────

    public async Task<object> GetAvailabilityAsync(string reviewerId, CancellationToken ct)
    {
        var avail = await db.ExpertAvailabilities
            .Where(a => a.ReviewerId == reviewerId)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (avail is null)
        {
            return new
            {
                timezone = "UTC",
                days = DefaultScheduleDays()
            };
        }

        return new
        {
            timezone = avail.Timezone,
            days = JsonSupport.Deserialize(avail.DaysJson, DefaultScheduleDays())
        };
    }

    public async Task<object> SaveAvailabilityAsync(string reviewerId, ExpertAvailabilityUpdateRequest request, CancellationToken ct)
    {
        var avail = await db.ExpertAvailabilities
            .Where(a => a.ReviewerId == reviewerId)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (avail is null)
        {
            avail = new ExpertAvailability
            {
                Id = $"ea-{Guid.NewGuid():N}",
                ReviewerId = reviewerId,
                EffectiveFrom = DateTimeOffset.UtcNow
            };
            db.ExpertAvailabilities.Add(avail);
        }

        avail.Timezone = request.Timezone;
        avail.DaysJson = JsonSupport.Serialize(request.Days);

        var expert = await db.ExpertUsers.FindAsync([reviewerId], ct);
        if (expert is not null)
            expert.Timezone = request.Timezone;

        await db.SaveChangesAsync(ct);

        return new
        {
            timezone = avail.Timezone,
            days = request.Days
        };
    }

    // ──────────── Metrics ────────────

    public async Task<object> GetMetricsAsync(string reviewerId, int days, CancellationToken ct)
    {
        var snapshot = await db.ExpertMetricSnapshots
            .Where(s => s.ReviewerId == reviewerId)
            .OrderByDescending(s => s.WindowEnd)
            .FirstOrDefaultAsync(ct);

        var completedCount = await db.ExpertReviewAssignments
            .CountAsync(a => a.AssignedReviewerId == reviewerId, ct);

        var totalReviews = snapshot?.CompletedReviews ?? completedCount;
        var slaCompliance = snapshot?.SlaHitRate ?? 100.0;
        var calibrationAlignment = snapshot?.CalibrationScore ?? 100.0;
        var reworkRate = snapshot?.ReworkRate ?? 0.0;

        var completionData = Enumerable.Range(0, days).Select(i =>
        {
            var day = DateTimeOffset.UtcNow.AddDays(-(days - 1) + i);
            return new
            {
                day = day.ToString("ddd"),
                count = (int)(totalReviews > 0 ? Math.Max(1, totalReviews / days) : 0)
            };
        }).ToList();

        return new
        {
            metrics = new
            {
                totalReviewsCompleted = totalReviews,
                averageSlaCompliance = slaCompliance,
                averageCalibrationAlignment = calibrationAlignment,
                reworkRate
            },
            completionData
        };
    }

    // ──────────── Helpers ────────────

    private static string MapPriority(string? turnaround)
    {
        return turnaround?.ToLowerInvariant() switch
        {
            "express" => "high",
            "standard" => "normal",
            _ => "normal"
        };
    }

    private static string MapReviewRequestToQueueStatus(ReviewRequestState state, ExpertAssignmentState? claimState)
    {
        if (claimState == ExpertAssignmentState.Claimed)
            return "assigned";

        return state switch
        {
            ReviewRequestState.Queued => "queued",
            ReviewRequestState.InReview => "in_progress",
            ReviewRequestState.Completed => "completed",
            ReviewRequestState.Failed => "blocked",
            ReviewRequestState.Cancelled => "completed",
            _ => "queued"
        };
    }

    private static Dictionary<string, object> DefaultScheduleDays()
    {
        return new Dictionary<string, object>
        {
            ["monday"] = new { active = true, start = "09:00", end = "17:00" },
            ["tuesday"] = new { active = true, start = "09:00", end = "17:00" },
            ["wednesday"] = new { active = true, start = "09:00", end = "17:00" },
            ["thursday"] = new { active = true, start = "09:00", end = "17:00" },
            ["friday"] = new { active = true, start = "09:00", end = "16:00" },
            ["saturday"] = new { active = false, start = "09:00", end = "12:00" },
            ["sunday"] = new { active = false, start = "09:00", end = "12:00" }
        };
    }
}
