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

    // ══════════════════════════════════════════════════════
    // L3 · Profession-Specific Learning Paths
    // ══════════════════════════════════════════════════════

    public async Task<object> GetLearningPathAsync(string userId, string? professionId, string examTypeCode, CancellationToken ct)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var effectiveProfession = professionId ?? goal?.ProfessionId ?? "nursing";

        var profession = await db.Professions
            .FirstOrDefaultAsync(p => p.Code == effectiveProfession || p.Id == effectiveProfession, ct);

        // Get content items filtered by profession
        var contentItems = await db.ContentItems
            .Where(c => c.Status == ContentStatus.Published && c.ExamTypeCode == examTypeCode &&
                        (c.ProfessionId == effectiveProfession || c.ProfessionId == null))
            .OrderBy(c => c.SubtestCode)
            .ThenBy(c => c.Difficulty == "easy" ? 0 : c.Difficulty == "medium" ? 1 : 2)
            .Take(60)
            .ToListAsync(ct);

        // Get user's completed attempts for progress tracking
        var completedAttemptContentIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => a.ContentId)
            .Distinct()
            .ToListAsync(ct);

        var subtestGroups = contentItems
            .GroupBy(c => c.SubtestCode)
            .Select(g => new
            {
                subtestCode = g.Key,
                totalItems = g.Count(),
                completedItems = g.Count(c => completedAttemptContentIds.Contains(c.Id)),
                progressPercent = g.Count() > 0 ? Math.Round(g.Count(c => completedAttemptContentIds.Contains(c.Id)) * 100.0 / g.Count(), 1) : 0,
                items = g.Take(15).Select(c => new
                {
                    id = c.Id,
                    title = c.Title,
                    difficulty = c.Difficulty,
                    durationMinutes = c.EstimatedDurationMinutes,
                    completed = completedAttemptContentIds.Contains(c.Id),
                    scenarioType = c.ScenarioType
                }).ToList()
            }).ToList();

        return new
        {
            professionCode = effectiveProfession,
            professionLabel = profession?.Label ?? effectiveProfession,
            examTypeCode,
            subtestPaths = subtestGroups,
            overallProgress = subtestGroups.Count > 0
                ? Math.Round(subtestGroups.Average(g => g.progressPercent), 1) : 0,
            totalContent = contentItems.Count,
            nextRecommended = contentItems
                .Where(c => !completedAttemptContentIds.Contains(c.Id))
                .Take(3)
                .Select(c => new { id = c.Id, title = c.Title, subtestCode = c.SubtestCode, difficulty = c.Difficulty })
                .ToList()
        };
    }

    // ══════════════════════════════════════════════════════
    // L5 · Adaptive Weak-Area Remediation
    // ══════════════════════════════════════════════════════

    public async Task<object> GetRemediationProfileAsync(string userId, CancellationToken ct)
    {
        // Analyze criterion scores across recent evaluations to find weak areas
        var userAttemptIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var evaluations = await db.Evaluations
            .Where(e => userAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .OrderByDescending(e => e.GeneratedAt)
            .Take(20)
            .ToListAsync(ct);

        var weakAreas = new List<object>();
        var criterionAggregates = new Dictionary<string, List<double>>();

        foreach (var eval in evaluations)
        {
            var criterionScores = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(eval.CriterionScoresJson, []);
            foreach (var cs in criterionScores)
            {
                var code = cs.TryGetValue("code", out var c) ? c?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(code)) continue;

                var key = $"{eval.SubtestCode}:{code}";
                if (!criterionAggregates.ContainsKey(key))
                    criterionAggregates[key] = [];

                if (cs.TryGetValue("score", out var s) && s is not null)
                {
                    if (s is System.Text.Json.JsonElement je && je.TryGetDouble(out var jd))
                        criterionAggregates[key].Add(jd);
                    else if (double.TryParse(s.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        criterionAggregates[key].Add(parsed);
                }
            }
        }

        // Identify criteria scoring below threshold
        foreach (var (key, scores) in criterionAggregates.OrderBy(kv => kv.Value.Average()))
        {
            var avg = scores.Average();
            if (avg >= 4.0) continue; // Only flag weak criteria (below 4 out of 6)

            var parts = key.Split(':');
            weakAreas.Add(new
            {
                subtestCode = parts[0],
                criterionCode = parts.Length > 1 ? parts[1] : "",
                averageScore = Math.Round(avg, 2),
                evaluationCount = scores.Count,
                trend = scores.Count >= 3
                    ? (scores.TakeLast(3).Average() > scores.Take(3).Average() ? "improving" : "declining")
                    : "insufficient_data"
            });
        }

        // Get available foundation resources for remediation
        var resources = await db.FoundationResources
            .Where(r => r.Status == ContentStatus.Published)
            .OrderBy(r => r.DisplayOrder)
            .Take(20)
            .ToListAsync(ct);

        return new
        {
            evaluationsAnalyzed = evaluations.Count,
            weakAreas = weakAreas.Take(10).ToList(),
            availableResources = resources.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                resourceType = r.ResourceType,
                difficulty = r.Difficulty,
                displayOrder = r.DisplayOrder
            }).ToList(),
            recommendations = weakAreas.Take(3).Select(wa => new
            {
                area = wa,
                suggestedResources = resources
                    .Where(r => r.Difficulty == "easy" || r.Difficulty == "medium")
                    .Take(3)
                    .Select(r => new { id = r.Id, title = r.Title })
                    .ToList()
            }).ToList()
        };
    }

    public async Task<object> StartRemediationSessionAsync(string userId, string subtestCode, string? criterionCode, CancellationToken ct)
    {
        // Find relevant foundation resources for the weak area
        var resources = await db.FoundationResources
            .Where(r => r.Status == ContentStatus.Published)
            .OrderBy(r => r.Difficulty == "easy" ? 0 : r.Difficulty == "medium" ? 1 : 2)
            .ThenBy(r => r.DisplayOrder)
            .Take(5)
            .ToListAsync(ct);

        var sessionId = $"rem-{Guid.NewGuid():N}";

        return new
        {
            sessionId,
            subtestCode,
            criterionCode,
            resources = resources.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                resourceType = r.ResourceType,
                difficulty = r.Difficulty,
                contentBody = r.ContentBody
            }).ToList(),
            startedAt = DateTimeOffset.UtcNow
        };
    }

    // ══════════════════════════════════════════════════════
    // E1 · Smart Next Best Action
    // ══════════════════════════════════════════════════════

    public async Task<object> GetNextBestActionsAsync(string userId, CancellationToken ct)
    {
        var actions = new List<object>();
        var now = DateTimeOffset.UtcNow;

        // 1. Check for incomplete study plan items due today/overdue
        var studyPlan = await db.StudyPlans
            .Where(p => p.UserId == userId && p.State == AsyncState.Completed)
            .OrderByDescending(p => p.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (studyPlan is not null)
        {
            var overdueItems = await db.StudyPlanItems
                .Where(i => i.StudyPlanId == studyPlan.Id && i.Status == StudyPlanItemStatus.NotStarted &&
                            i.DueDate <= DateOnly.FromDateTime(now.UtcDateTime))
                .OrderBy(i => i.DueDate)
                .Take(3)
                .ToListAsync(ct);

            foreach (var item in overdueItems)
            {
                actions.Add(new
                {
                    type = "overdue_task",
                    priority = "high",
                    title = item.Title,
                    subtitle = $"Due {item.DueDate:MMM dd} · {item.DurationMinutes}min",
                    actionUrl = $"/study-plan?highlight={item.Id}",
                    subtestCode = item.SubtestCode
                });
            }
        }

        // 2. Check for pending reviews (tutor feedback ready)
        var pendingReviews = await db.ReviewRequests
            .Where(r => r.State == ReviewRequestState.Completed)
            .Join(db.Attempts, r => r.AttemptId, a => a.Id, (r, a) => new { r, a })
            .Where(x => x.a.UserId == userId)
            .OrderByDescending(x => x.r.CompletedAt)
            .Take(2)
            .ToListAsync(ct);

        foreach (var pr in pendingReviews)
        {
            actions.Add(new
            {
                type = "review_ready",
                priority = "medium",
                title = $"Tutor review ready: {pr.r.SubtestCode}",
                subtitle = "Review your personalized feedback",
                actionUrl = $"/feedback/{pr.r.Id}",
                subtestCode = pr.r.SubtestCode
            });
        }

        // 3. Check weak areas that need attention
        var recentAttemptIds = await db.Attempts
            .Where(a => a.UserId == userId && a.State == AttemptState.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .Take(5)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var recentEvals = await db.Evaluations
            .Where(e => recentAttemptIds.Contains(e.AttemptId) && e.State == AsyncState.Completed)
            .ToListAsync(ct);

        if (recentEvals.Count > 0)
        {
            var weakSubtest = recentEvals
                .GroupBy(e => e.SubtestCode)
                .Select(g => new
                {
                    SubtestCode = g.Key,
                    AvgMid = g.Select(e => GovernedScoreRange(
                            e.SubtestCode,
                            e.ScoreRange,
                            e.ScaledScore,
                            e.ScoreConversionTableVersionKey,
                            e.ScoreConversionPassed))
                        .Select(ParseScoreRangeMid)
                        .Where(score => score.HasValue)
                        .Select(score => (double?)score!.Value)
                        .Average()
                })
                .Where(g => g.AvgMid.HasValue)
                .OrderBy(g => g.AvgMid)
                .FirstOrDefault();

            if (weakSubtest is not null
                && weakSubtest.AvgMid is double weakAverage
                && weakAverage < OetScoring.ScaledPassGradeB)
            {
                actions.Add(new
                {
                    type = "weak_area_practice",
                    priority = "medium",
                    title = $"Practice {weakSubtest.SubtestCode} — your weakest area",
                    subtitle = $"Average score: {weakAverage:F0}/500",
                    actionUrl = $"/practice/{weakSubtest.SubtestCode}",
                    subtestCode = weakSubtest.SubtestCode
                });
            }
        }

        // 4. Goal-based recommendation
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        if (goal?.TargetExamDate is not null)
        {
            var daysUntilExam = (goal.TargetExamDate.Value.ToDateTime(TimeOnly.MinValue) - now.UtcDateTime).TotalDays;
            if (daysUntilExam is > 0 and <= 30)
            {
                actions.Add(new
                {
                    type = "exam_approaching",
                    priority = "high",
                    title = $"Exam in {daysUntilExam:F0} days — intensify practice",
                    subtitle = "Focus on mock exams and timed practice",
                    actionUrl = "/test-day",
                    subtestCode = (string?)null
                });
            }
        }

        // 5. Streak continuation
        actions.Add(new
        {
            type = "daily_goal",
            priority = "low",
            title = "Complete today's study goal",
            subtitle = "Keep your streak going",
            actionUrl = "/dashboard",
            subtestCode = (string?)null
        });

        return new
        {
            actions = actions.Take(5).ToList(),
            generatedAt = now
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E4: Speaking Fluency Timeline
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetFluencyTimelineAsync(string userId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.Attempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.SubtestCode == "speaking", ct)
            ?? throw ApiException.NotFound("ATTEMPT_NOT_FOUND", "Speaking attempt not found.");

        // Parse transcript segments for timing data
        var segments = new List<object>();
        try
        {
            var transcriptItems = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(attempt.TranscriptJson);
            if (transcriptItems is not null)
            {
                double lastEnd = 0;
                int segIdx = 0;
                foreach (var item in transcriptItems)
                {
                    var startSec = item.TryGetValue("startTime", out var st) ? st.GetDouble() : lastEnd;
                    var endSec = item.TryGetValue("endTime", out var et) ? et.GetDouble() : startSec + 1;
                    var text = item.TryGetValue("text", out var tx) ? tx.GetString() ?? "" : "";
                    var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    var duration = endSec - startSec;
                    var wordsPerMinute = duration > 0 ? wordCount / duration * 60.0 : 0;
                    var gap = startSec - lastEnd;

                    // Detect filler words
                    var fillerWords = new[] { "um", "uh", "er", "ah", "like", "you know", "sort of", "kind of" };
                    var fillerCount = fillerWords.Sum(f => System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\b" + f + @"\b").Count);

                    segments.Add(new
                    {
                        index = segIdx++,
                        startTime = Math.Round(startSec, 2),
                        endTime = Math.Round(endSec, 2),
                        text,
                        wordCount,
                        wordsPerMinute = Math.Round(wordsPerMinute, 1),
                        pauseBefore = Math.Round(gap, 2),
                        isPause = gap > 1.5,
                        fillerCount,
                        fluencyRating = fillerCount == 0 && wordsPerMinute >= 100 && wordsPerMinute <= 170 ? "good"
                            : fillerCount > 2 || wordsPerMinute < 80 || wordsPerMinute > 200 ? "poor" : "fair"
                    });
                    lastEnd = endSec;
                }
            }
        }
        catch { /* Transcript parsing failed — return empty timeline */ }

        // Parse analysis JSON for overall fluency metrics
        var analysisData = new Dictionary<string, object>();
        try
        {
            var analysis = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(attempt.AnalysisJson);
            if (analysis is not null)
            {
                if (analysis.TryGetValue("speechRate", out var sr)) analysisData["speechRate"] = sr.GetDouble();
                if (analysis.TryGetValue("pauseCount", out var pc)) analysisData["pauseCount"] = pc.GetInt32();
                if (analysis.TryGetValue("averagePauseDuration", out var apd)) analysisData["averagePauseDuration"] = apd.GetDouble();
            }
        }
        catch { }

        var totalWords = segments.Sum(s => (int)((dynamic)s).wordCount);
        var totalFillers = segments.Sum(s => (int)((dynamic)s).fillerCount);
        var totalDuration = segments.Count > 0 ? (double)((dynamic)segments.Last()).endTime : 0;

        return new
        {
            attemptId,
            totalDurationSeconds = Math.Round(totalDuration, 1),
            totalWords,
            totalFillerWords = totalFillers,
            fillerRatio = totalWords > 0 ? Math.Round(totalFillers * 100.0 / totalWords, 1) : 0,
            averageWordsPerMinute = totalDuration > 0 ? Math.Round(totalWords / totalDuration * 60, 1) : 0,
            pauseCount = segments.Count(s => (bool)((dynamic)s).isPause),
            timeline = segments,
            overallAnalysis = analysisData,
            benchmarks = new
            {
                idealWordsPerMinute = new { min = 120, max = 160 },
                maxAcceptableFillerRatio = 3.0,
                maxAcceptablePauseSeconds = 2.0
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E5: Progress — Comparative Analytics & Percentile Ranking
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetComparativeAnalyticsAsync(string userId, CancellationToken ct)
    {
        var subtests = new[] { "writing", "speaking", "reading", "listening" };
        var results = new List<object>();

        foreach (var subtest in subtests)
        {
            // Get user's latest evaluation scores
            var userEvals = await db.Evaluations
                .Where(e => db.Attempts.Any(a => a.Id == e.AttemptId && a.UserId == userId && a.SubtestCode == subtest))
                .OrderByDescending(e => e.GeneratedAt)
                .Take(5)
                .ToListAsync(ct);

            if (userEvals.Count == 0) continue;

            var userAvg = userEvals
                .Select(e => ParseScoreRangeMid(GovernedScoreRange(
                    e.SubtestCode,
                    e.ScoreRange,
                    e.ScaledScore,
                    e.ScoreConversionTableVersionKey,
                    e.ScoreConversionPassed)))
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            if (userAvg.Count == 0) continue;
            var userScore = userAvg.Average();

            // Get all users' average scores for this subtest (cohort comparison)
            var allScores = await db.Evaluations
                .Where(e => e.SubtestCode == subtest && e.GeneratedAt >= DateTimeOffset.UtcNow.AddDays(-90))
                .Select(e => new
                {
                    e.SubtestCode,
                    e.ScoreRange,
                    e.ScaledScore,
                    e.ScoreConversionTableVersionKey,
                    e.ScoreConversionPassed
                })
                .ToListAsync(ct);

            var allAverages = allScores
                .Select(e => ParseScoreRangeMid(GovernedScoreRange(
                    e.SubtestCode,
                    e.ScoreRange,
                    e.ScaledScore,
                    e.ScoreConversionTableVersionKey,
                    e.ScoreConversionPassed)))
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .OrderBy(s => s)
                .ToList();

            // No cohort in the window → no comparison. Null, never an invented median.
            double? percentile = allAverages.Count > 0
                ? Math.Round(allAverages.Count(s => s <= userScore) * 100.0 / allAverages.Count, 1)
                : null;

            double? cohortAvg = allAverages.Count > 0 ? Math.Round(allAverages.Average(), 1) : null;
            double? cohortMedian = allAverages.Count > 0 ? allAverages[allAverages.Count / 2] : null;

            // Score gap to target
            var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
            double? targetScore = null;
            if (goal is not null)
            {
                targetScore = subtest switch
                {
                    "writing" => goal.TargetWritingScore,
                    "speaking" => goal.TargetSpeakingScore,
                    "reading" => goal.TargetReadingScore,
                    "listening" => goal.TargetListeningScore,
                    _ => null
                };
            }

            results.Add(new
            {
                subtestCode = subtest,
                yourScore = Math.Round(userScore, 1),
                percentile,
                cohortAverage = cohortAvg,
                cohortMedian,
                cohortSize = allAverages.Count,
                targetScore,
                gapToTarget = targetScore.HasValue ? Math.Round(targetScore.Value - userScore, 1) : (double?)null,
                tier = percentile is null ? null
                    : percentile >= 90 ? "top10" : percentile >= 75 ? "top25" : percentile >= 50 ? "aboveMedian" : "belowMedian"
            });
        }

        return new { subtests = results, generatedAt = DateTimeOffset.UtcNow };
    }

    // ═══════════════════════════════════════════════════════════════
    // E6: Mock — Exam Simulation Configuration
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetExamSimulationConfigAsync(string userId, CancellationToken ct)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, ct);
        var examType = OetLearner.Api.Services.Common.ExamCodes.NormalizeOrNull(goal?.ExamTypeCode) ?? OetLearner.Api.Services.Common.ExamCodes.DefaultCode;

        // Count user's completed mocks to determine readiness for simulation
        var completedMocks = await db.Attempts
            .CountAsync(a => a.UserId == userId && a.Context == "mock" && a.State == AttemptState.Completed, ct);

        return new
        {
            examType,
            simulationMode = new
            {
                strictTiming = true,
                noPause = true,
                sequentialSubtests = true,
                noBackNavigation = true,
                showCountdown = true,
                stressIndicators = completedMocks < 3
            },
            subtestTimings = new
            {
                listening = new { durationMinutes = 42, sections = 2 },
                reading = new { durationMinutes = 60, sections = 3 },
                writing = new { durationMinutes = 45, sections = 1 },
                speaking = new { durationMinutes = 20, sections = 2 }
            },
            totalDurationMinutes = 167,
            completedSimulations = completedMocks,
            recommendation = completedMocks < 3
                ? "Complete at least 3 practice mocks before attempting full simulation."
                : completedMocks < 6
                    ? "Good practice base. Try simulation mode to build test-day confidence."
                    : "Strong preparation. Use simulation mode for final exam readiness check.",
            unlocked = completedMocks >= 2
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E9: Study Plan — Auto-Regeneration on Drift Detection
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> DetectStudyPlanDriftAsync(string userId, CancellationToken ct)
    {
        var plan = await db.StudyPlans.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (plan is null) return new { hasPlan = false, drift = (object?)null };

        var items = await db.StudyPlanItems
            .Where(i => i.StudyPlanId == plan.Id)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = items.Where(i => i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted).ToList();
        var completed = items.Where(i => i.Status == StudyPlanItemStatus.Completed).ToList();
        var total = items.Count;
        var expectedCompleted = items.Count(i => i.DueDate <= today);
        var actualCompleted = completed.Count;
        var completionRate = expectedCompleted > 0 ? Math.Round(actualCompleted * 100.0 / expectedCompleted, 1) : 100;
        var driftDays = overdue.Count > 0 ? (today.ToDateTime(TimeOnly.MinValue) - overdue.First().DueDate.ToDateTime(TimeOnly.MinValue)).Days : 0;

        var driftLevel = driftDays > 14 ? "severe" : driftDays > 7 ? "moderate" : driftDays > 3 ? "mild" : "on-track";
        var shouldRegenerate = driftLevel is "severe" or "moderate";

        // Auto-enqueue a regen job when drift is moderate+ AND the last
        // regen was more than 24h ago. Cooldown prevents whiplash while
        // still rescuing learners who fall behind for days. (Phase 3b)
        var autoRegenQueued = false;
        if (shouldRegenerate)
        {
            var hoursSinceLastRegen = (DateTimeOffset.UtcNow - plan.GeneratedAt).TotalHours;
            if (hoursSinceLastRegen >= 24)
            {
                var existingQueued = await db.BackgroundJobs.AnyAsync(
                    j => j.Type == JobType.StudyPlanRegeneration
                        && j.ResourceId == plan.Id
                        && (j.State == AsyncState.Queued || j.State == AsyncState.Processing),
                    ct);

                if (!existingQueued)
                {
                    var payloadJson = JsonSupport.Serialize(new { userId, trigger = "DriftRecovery" });
                    await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, payloadJson: payloadJson, cancellationToken: ct);
                    plan.State = AsyncState.Queued;
                    await db.SaveChangesAsync(ct);
                    autoRegenQueued = true;
                }
            }
        }

        // Breakdown by subtest
        var subtestDrift = items
            .GroupBy(i => i.SubtestCode)
            .Select(g => new
            {
                subtestCode = g.Key,
                total = g.Count(),
                completed = g.Count(i => i.Status == StudyPlanItemStatus.Completed),
                overdue = g.Count(i => i.DueDate < today && i.Status == StudyPlanItemStatus.NotStarted),
                completionRate = g.Count(i => i.DueDate <= today) > 0
                    ? Math.Round(g.Count(i => i.Status == StudyPlanItemStatus.Completed) * 100.0 / g.Count(i => i.DueDate <= today), 1) : 100
            })
            .ToList();

        return new
        {
            hasPlan = true,
            planId = plan.Id,
            drift = new
            {
                level = driftLevel,
                overdueItems = overdue.Count,
                oldestOverdueDays = driftDays,
                completionRate,
                expectedCompleted,
                actualCompleted,
                totalItems = total,
                shouldRegenerate,
                autoRegenQueued,
                recommendation = driftLevel switch
                {
                    "severe" => "You're significantly behind schedule. We strongly recommend regenerating your study plan to align with your current pace.",
                    "moderate" => "You're falling behind. Consider regenerating your plan or catching up on priority items.",
                    "mild" => "Slightly behind but manageable. Focus on overdue items this week.",
                    _ => "Great job! You're on track with your study plan."
                }
            },
            subtestDrift,
            overdueItems = overdue.Select(i => new { i.Id, i.Title, i.SubtestCode, i.DueDate, daysOverdue = (today.ToDateTime(TimeOnly.MinValue) - i.DueDate.ToDateTime(TimeOnly.MinValue)).Days }).Take(10)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // E10: Billing Upgrade Path Visibility
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetBillingUpgradePathAsync(string userId, CancellationToken ct)
    {
        // Get user's current subscription
        var subscription = await db.Subscriptions
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
            .FirstOrDefaultAsync(ct);

        var currentPlanId = subscription?.PlanId;
        var allPlans = await db.BillingPlans
            .Where(p => p.IsVisible && p.Status == BillingPlanStatus.Active)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

        var currentPlan = currentPlanId is not null ? allPlans.FirstOrDefault(p => p.Id == currentPlanId) : null;

        // Get usage stats
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        var reviewsUsedThisMonth = await db.ReviewRequests
            .CountAsync(r => db.Attempts.Any(a => a.Id == r.AttemptId && a.UserId == userId)
                && r.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30), ct);

        var planComparison = allPlans.Select(p =>
        {
            Dictionary<string, object>? entitlements = null;
            try { entitlements = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(p.EntitlementsJson); } catch { }

            return new
            {
                planId = p.Id,
                planCode = p.Code,
                planName = p.Name,
                description = p.Description,
                price = p.Price,
                currency = p.Currency,
                interval = p.Interval,
                includedCredits = p.IncludedCredits,
                trialDays = p.TrialDays,
                isCurrent = p.Id == currentPlanId,
                isUpgrade = currentPlan is not null && p.Price > currentPlan.Price,
                isDowngrade = currentPlan is not null && p.Price < currentPlan.Price,
                entitlements = entitlements ?? new Dictionary<string, object>()
            };
        }).ToList();

        return new
        {
            currentPlan = currentPlan is not null ? new
            {
                planId = currentPlan.Id,
                planName = currentPlan.Name,
                price = currentPlan.Price,
                includedCredits = currentPlan.IncludedCredits
            } : null,
            usage = new
            {
                reviewsUsedThisMonth,
                creditsRemaining = wallet?.CreditBalance ?? 0,
                subscriptionStarted = subscription?.StartedAt,
                subscriptionEnds = subscription?.NextRenewalAt
            },
            plans = planComparison,
            recommendation = currentPlan is null
                ? "Start with a plan to unlock tutor reviews, mock exams, and AI-powered feedback."
                : reviewsUsedThisMonth >= (currentPlan.IncludedCredits * 0.8)
                    ? "You've used most of your included reviews. Consider upgrading for more credits."
                    : "Your current plan is meeting your usage needs."
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // L9: Interleaved Practice Mode
    // ═══════════════════════════════════════════════════════════════

    public async Task<object> GetInterleavedPracticeSessionAsync(string userId, int durationMinutes, CancellationToken ct)
    {
        var targetMinutes = durationMinutes > 0 ? Math.Min(durationMinutes, 60) : 20;
        var subtests = new[] { "reading", "listening", "writing", "speaking" };
        var sessionItems = new List<object>();
        var allocatedMinutes = 0;

        // Get user's weakest areas to weight task selection. Written as an
        // explicit join with a hoisted cutoff (not a correlated Any subquery
        // with an inline DateTimeOffset call) so every provider, including
        // SQLite, can translate it.
        var recencyCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var recentEvals = await (
                from e in db.Evaluations.AsNoTracking()
                join a in db.Attempts.AsNoTracking() on e.AttemptId equals a.Id
                where a.UserId == userId
                    && e.GeneratedAt >= recencyCutoff
                select new
                {
                    e.SubtestCode,
                    e.ScoreRange,
                    e.ScaledScore,
                    e.ScoreConversionTableVersionKey,
                    e.ScoreConversionPassed
                })
            .ToListAsync(ct);

        var subtestScores = recentEvals
            .GroupBy(e => e.SubtestCode)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var scores = g.Select(e => ParseScoreRangeMid(GovernedScoreRange(
                            e.SubtestCode,
                            e.ScoreRange,
                            e.ScaledScore,
                            e.ScoreConversionTableVersionKey,
                            e.ScoreConversionPassed)))
                        .Where(score => score.HasValue)
                        .Select(score => score!.Value)
                        .ToList();
                    return scores.Count > 0 ? scores.Average() : 300.0;
                });

        // Prioritize weaker subtests
        var orderedSubtests = subtests
            .OrderBy(s => subtestScores.GetValueOrDefault(s, 300.0))
            .ToList();
        var availableContentCounts = await db.ContentItems
            .AsNoTracking()
            .Where(content =>
                subtests.Contains(content.SubtestCode)
                && content.Status == ContentStatus.Published)
            .GroupBy(content => content.SubtestCode)
            .Select(group => new { Subtest = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Subtest, row => row.Count, ct);

        int taskIndex = 0;
        while (allocatedMinutes < targetMinutes)
        {
            var subtest = orderedSubtests[taskIndex % orderedSubtests.Count];
            var taskDuration = subtest switch
            {
                "reading" => 8,
                "listening" => 7,
                "writing" => 10,
                "speaking" => 5,
                _ => 7
            };

            if (allocatedMinutes + taskDuration > targetMinutes + 3) break;

            // Get adaptive content for this subtest
            var contentCount = availableContentCounts.GetValueOrDefault(subtest);
            var randomOffset = contentCount > 0
                ? RandomNumberGenerator.GetInt32(contentCount)
                : 0;
            var content = contentCount == 0
                ? null
                : await db.ContentItems
                    .AsNoTracking()
                    .Where(c => c.SubtestCode == subtest && c.Status == ContentStatus.Published)
                    .OrderBy(c => c.Id)
                    .Skip(randomOffset)
                    .Select(c => new { c.Id, c.Title, c.SubtestCode, c.EstimatedDurationMinutes, c.Difficulty })
                    .FirstOrDefaultAsync(ct);

            if (content is not null)
            {
                sessionItems.Add(new
                {
                    order = taskIndex + 1,
                    contentId = content.Id,
                    title = content.Title,
                    subtestCode = content.SubtestCode,
                    taskType = subtest switch
                    {
                        "reading" => "passage-comprehension",
                        "listening" => "audio-exercise",
                        "writing" => "short-response",
                        "speaking" => "pronunciation-drill",
                        _ => "practice"
                    },
                    durationMinutes = taskDuration,
                    difficulty = content.Difficulty,
                    isWeakArea = subtestScores.GetValueOrDefault(subtest, (double)OetScoring.ScaledPassGradeCPlus) < OetScoring.ScaledPassGradeB
                });
                allocatedMinutes += taskDuration;
            }
            taskIndex++;
            if (taskIndex > 20) break; // Safety cap
        }

        return new
        {
            sessionId = Guid.NewGuid().ToString(),
            targetDurationMinutes = targetMinutes,
            actualDurationMinutes = allocatedMinutes,
            taskCount = sessionItems.Count,
            tasks = sessionItems,
            scienceBasis = "Interleaving different skill types in a single session improves long-term retention (Rohrer & Taylor, 2007).",
            tips = new[]
            {
                "Don't skip tasks — the variety is intentional for better learning.",
                "Take 30-second breaks between tasks to reset your focus.",
                "Review any mistakes immediately after each task."
            }
        };
    }
}
