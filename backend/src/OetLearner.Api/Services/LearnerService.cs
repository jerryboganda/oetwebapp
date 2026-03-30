using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed record GeneratedDownloadFile(Stream Stream, string ContentType, string FileName);

public class LearnerService(LearnerDbContext db, ILogger<LearnerService> logger, MediaStorageService mediaStorage, PlatformLinkService platformLinks)
{
    public async Task<object> GetMeAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);

        return new
        {
            userId = user.Id,
            role = user.Role,
            displayName = user.DisplayName,
            email = user.Email,
            timezone = user.Timezone,
            locale = user.Locale,
            createdAt = user.CreatedAt,
            lastActiveAt = user.LastActiveAt,
            currentPlanId = user.CurrentPlanId,
            activeProfessionId = user.ActiveProfessionId,
            goals = new
            {
                professionId = goal.ProfessionId,
                targetExamDate = goal.TargetExamDate,
                targetScoresBySubtest = new
                {
                    writing = goal.TargetWritingScore,
                    speaking = goal.TargetSpeakingScore,
                    reading = goal.TargetReadingScore,
                    listening = goal.TargetListeningScore
                }
            }
        };
    }

    public async Task<object> GetBootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var onboarding = await GetOnboardingStateAsync(userId, cancellationToken);
        var goals = await GetGoalsAsync(userId, cancellationToken);
        var readiness = await GetReadinessAsync(userId, cancellationToken);

        return new
        {
            user = await GetMeAsync(userId, cancellationToken),
            onboarding,
            goals,
            readiness,
            permissions = new
            {
                canRequestReview = true,
                canViewTranscript = true,
                canPurchaseExtras = true,
                canResumeAttempt = true,
                eligibilityReasonCodes = Array.Empty<string>()
            },
            reference = new
            {
                professions = await GetProfessionsAsync(cancellationToken),
                subtests = await GetSubtestsAsync(cancellationToken)
            },
            links = new
            {
                dashboard = "/app/dashboard",
                studyPlan = "/app/study-plan",
                goals = "/app/goals"
            },
            lastUpdatedAt = user.LastActiveAt
        };
    }

    public async Task<IEnumerable<object>> GetProfessionsAsync(CancellationToken cancellationToken) =>
        await db.Professions
            .OrderBy(x => x.SortOrder)
            .Select(x => (object)new { professionId = x.Id, code = x.Code, label = x.Label, status = x.Status, sortOrder = x.SortOrder })
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<object>> GetSubtestsAsync(CancellationToken cancellationToken) =>
        await db.Subtests
            .OrderBy(x => x.Label)
            .Select(x => (object)new { subtestId = x.Id, code = x.Code, label = x.Label, supportsProfessionSpecificContent = x.SupportsProfessionSpecificContent })
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<object>> GetCriteriaAsync(string? subtest, CancellationToken cancellationToken)
    {
        var query = db.Criteria.AsQueryable();
        if (!string.IsNullOrWhiteSpace(subtest))
        {
            query = query.Where(x => x.SubtestCode == subtest);
        }

        return await query
            .OrderBy(x => x.SubtestCode)
            .ThenBy(x => x.SortOrder)
            .Select(x => (object)new
            {
                criterionId = x.Id,
                subtest = x.SubtestCode,
                code = x.Code,
                label = x.Label,
                description = x.Description,
                sortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);
    }

    public object GetFilters(string surface)
    {
        return surface.ToLowerInvariant() switch
        {
            "writing" => new
            {
                surface,
                groups = new[]
                {
                    new { id = "profession", label = "Profession", options = new[] { new { id = "nursing", label = "Nursing" }, new { id = "medicine", label = "Medicine" } } },
                    new { id = "difficulty", label = "Difficulty", options = new[] { new { id = "easy", label = "Easy" }, new { id = "medium", label = "Medium" }, new { id = "hard", label = "Hard" } } }
                }
            },
            "speaking" => new
            {
                surface,
                groups = new[]
                {
                    new { id = "mode", label = "Mode", options = new[] { new { id = "ai", label = "AI Roleplay" }, new { id = "exam", label = "Exam Mode" }, new { id = "self", label = "Self Practice" } } },
                    new { id = "scenario", label = "Scenario", options = new[] { new { id = "handover", label = "Handover" }, new { id = "consultation", label = "Consultation" } } }
                }
            },
            _ => new
            {
                surface,
                groups = new[]
                {
                    new { id = "subtest", label = "Sub-test", options = new[] { new { id = "writing", label = "Writing" }, new { id = "speaking", label = "Speaking" }, new { id = "reading", label = "Reading" }, new { id = "listening", label = "Listening" } } }
                }
            }
        };
    }

    public async Task<object> GetOnboardingStateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        return new
        {
            completed = user.OnboardingCompleted,
            currentStep = user.OnboardingCurrentStep,
            stepCount = user.OnboardingStepCount,
            canSkip = false,
            startedAt = user.OnboardingStartedAt,
            completedAt = user.OnboardingCompletedAt,
            checkpoint = user.OnboardingCompleted ? "goals" : "welcome",
            resumeRoute = user.OnboardingCompleted ? "/app/dashboard" : "/app/onboarding"
        };
    }

    public async Task<object> StartOnboardingAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        user.OnboardingStartedAt ??= DateTimeOffset.UtcNow;
        user.OnboardingCurrentStep = Math.Max(user.OnboardingCurrentStep, 1);
        user.LastActiveAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "onboarding_started", new { userId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOnboardingStateAsync(userId, cancellationToken);
    }

    public async Task<object> CompleteOnboardingAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        user.OnboardingCompleted = true;
        user.OnboardingCurrentStep = user.OnboardingStepCount;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        user.LastActiveAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "onboarding_completed", new { userId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOnboardingStateAsync(userId, cancellationToken);
    }

    public async Task<object> GetGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return GoalDto(goal);
    }

    public async Task<object> PatchGoalsAsync(string userId, PatchGoalsRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);

        // Validate score ranges (OET scores: 0-500)
        ValidateScoreRange(request.TargetWritingScore, nameof(request.TargetWritingScore));
        ValidateScoreRange(request.TargetSpeakingScore, nameof(request.TargetSpeakingScore));
        ValidateScoreRange(request.TargetReadingScore, nameof(request.TargetReadingScore));
        ValidateScoreRange(request.TargetListeningScore, nameof(request.TargetListeningScore));
        if (request.StudyHoursPerWeek.HasValue && (request.StudyHoursPerWeek.Value < 0 || request.StudyHoursPerWeek.Value > 168))
            throw ApiException.Validation("invalid_study_hours", "Study hours per week must be between 0 and 168.", [new ApiFieldError("studyHoursPerWeek", "out_of_range", "Must be 0–168.")]);
        if (request.PreviousAttempts.HasValue && request.PreviousAttempts.Value < 0)
            throw ApiException.Validation("invalid_previous_attempts", "Previous attempts cannot be negative.", [new ApiFieldError("previousAttempts", "out_of_range", "Must be 0 or greater.")]);

        if (!string.IsNullOrWhiteSpace(request.ProfessionId)) goal.ProfessionId = request.ProfessionId;
        if (request.TargetExamDate.HasValue) goal.TargetExamDate = request.TargetExamDate;
        if (request.OverallGoal is not null) goal.OverallGoal = request.OverallGoal;
        if (request.TargetWritingScore.HasValue) goal.TargetWritingScore = request.TargetWritingScore;
        if (request.TargetSpeakingScore.HasValue) goal.TargetSpeakingScore = request.TargetSpeakingScore;
        if (request.TargetReadingScore.HasValue) goal.TargetReadingScore = request.TargetReadingScore;
        if (request.TargetListeningScore.HasValue) goal.TargetListeningScore = request.TargetListeningScore;
        if (request.PreviousAttempts.HasValue) goal.PreviousAttempts = request.PreviousAttempts.Value;
        if (request.WeakSubtests is not null) goal.WeakSubtestsJson = JsonSupport.Serialize(request.WeakSubtests);
        if (request.StudyHoursPerWeek.HasValue) goal.StudyHoursPerWeek = request.StudyHoursPerWeek.Value;
        if (request.TargetCountry is not null) goal.TargetCountry = request.TargetCountry;
        if (request.TargetOrganization is not null) goal.TargetOrganization = request.TargetOrganization;
        if (request.DraftState is not null) goal.DraftStateJson = JsonSupport.Serialize(request.DraftState);

        goal.UpdatedAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "goals_saved", new { userId, professionId = goal.ProfessionId, targetExamDate = goal.TargetExamDate }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return GoalDto(goal);
    }

    private static void ValidateScoreRange(int? score, string fieldName)
    {
        if (score.HasValue && (score.Value < 0 || score.Value > 500))
            throw ApiException.Validation("invalid_score_range", $"Target score must be between 0 and 500.", [new ApiFieldError(fieldName, "out_of_range", "Must be 0–500.")]);
    }

    public async Task<object> SubmitGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
            goal.SubmittedAt = DateTimeOffset.UtcNow;
            goal.UpdatedAt = DateTimeOffset.UtcNow;

            var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
            plan.State = AsyncState.Queued;
            await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, cancellationToken: cancellationToken);
            await RecordEventAsync(userId, "goals_saved", new { userId, professionId = goal.ProfessionId, submitted = true }, cancellationToken);
            LogAudit(userId, "Submitted", "Goals", goal.Id.ToString(), "Goals submitted, triggering study plan regeneration");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new
            {
                goals = GoalDto(goal),
                studyPlanRegeneration = new
                {
                    state = ToAsyncState(plan.State),
                    nextPollAfterMs = 2000,
                    planId = plan.Id
                }
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<object> GetSettingsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return SettingsDto(settings, goal);
    }

    public async Task<object> GetSettingsSectionAsync(string userId, string section, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var profileValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>());
        profileValues["displayName"] = user.DisplayName;
        profileValues["email"] = user.Email;
        profileValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;

        var studyValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
        studyValues["targetExamDate"] = goal.TargetExamDate;
        studyValues["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
        studyValues["targetCountry"] = goal.TargetCountry;
        studyValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;

        return section.ToLowerInvariant() switch
        {
            "profile" => new { section = "profile", values = profileValues },
            "notifications" => new { section = "notifications", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.NotificationsJson, new Dictionary<string, object?>()) },
            "privacy" => new { section = "privacy", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.PrivacyJson, new Dictionary<string, object?>()) },
            "accessibility" => new { section = "accessibility", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AccessibilityJson, new Dictionary<string, object?>()) },
            "audio" => new { section = "audio", values = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AudioJson, new Dictionary<string, object?>()) },
            "study" => new { section = "study", values = studyValues },
            "goals" => new { section = "goals", values = GoalSettingsDto(await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken)) },
            _ => throw ApiException.Validation(
                "unknown_settings_section",
                $"Unknown settings section '{section}'.",
                [new ApiFieldError("section", "unknown_section", "Choose a supported settings section.")])
        };
    }

    public async Task<object> PatchSettingsSectionAsync(string userId, string section, PatchSectionRequest request, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        if (section.Equals("goals", StringComparison.OrdinalIgnoreCase))
        {
            ApplyGoalSettingsPatch(goal, request.Values);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "goals" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "goals", "Updated goal settings");
            await db.SaveChangesAsync(cancellationToken);
            return new { section = "goals", values = GoalSettingsDto(goal) };
        }

        if (section.Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            var mergedProfile = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>());
            foreach (var (key, value) in request.Values)
            {
                mergedProfile[key] = value;
            }

            if (request.Values.TryGetValue("displayName", out var displayName))
            {
                user.DisplayName = ReadString(displayName) ?? user.DisplayName;
            }

            if (request.Values.TryGetValue("email", out var email))
            {
                user.Email = ReadString(email) ?? user.Email;
            }

            if (request.Values.TryGetValue("professionId", out var professionId))
            {
                var normalizedProfession = ReadString(professionId) ?? goal.ProfessionId ?? user.ActiveProfessionId;
                if (!string.IsNullOrWhiteSpace(normalizedProfession))
                {
                    goal.ProfessionId = normalizedProfession;
                    user.ActiveProfessionId = normalizedProfession;
                    mergedProfile["professionId"] = normalizedProfession;
                }
            }

            settings.ProfileJson = JsonSupport.Serialize(mergedProfile);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "profile" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "profile", "Updated learner profile settings");
            await db.SaveChangesAsync(cancellationToken);
            return new
            {
                section = "profile",
                values = new Dictionary<string, object?>(mergedProfile)
                {
                    ["displayName"] = user.DisplayName,
                    ["email"] = user.Email,
                    ["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId
                }
            };
        }

        if (section.Equals("study", StringComparison.OrdinalIgnoreCase))
        {
            var studyValues = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
            foreach (var (key, value) in request.Values)
            {
                studyValues[key] = value;
            }

            if (request.Values.TryGetValue("targetExamDate", out var targetExamDate))
            {
                goal.TargetExamDate = ReadDateOnly(targetExamDate) ?? goal.TargetExamDate;
            }

            if (request.Values.TryGetValue("studyHoursPerWeek", out var studyHoursPerWeek))
            {
                goal.StudyHoursPerWeek = ReadInt(studyHoursPerWeek) ?? goal.StudyHoursPerWeek;
            }

            if (request.Values.TryGetValue("targetCountry", out var targetCountry))
            {
                goal.TargetCountry = ReadString(targetCountry) ?? goal.TargetCountry;
            }

            if (request.Values.TryGetValue("professionId", out var studyProfessionId))
            {
                var normalizedProfession = ReadString(studyProfessionId) ?? goal.ProfessionId ?? user.ActiveProfessionId;
                if (!string.IsNullOrWhiteSpace(normalizedProfession))
                {
                    goal.ProfessionId = normalizedProfession;
                    user.ActiveProfessionId = normalizedProfession;
                    studyValues["professionId"] = normalizedProfession;
                }
            }

            settings.StudyJson = JsonSupport.Serialize(studyValues);
            goal.UpdatedAt = DateTimeOffset.UtcNow;
            await RecordEventAsync(userId, "settings_changed", new { userId, section = "study" }, cancellationToken);
            LogAudit(userId, "Updated", "Settings", "study", "Updated study settings");
            await db.SaveChangesAsync(cancellationToken);
            studyValues["targetExamDate"] = goal.TargetExamDate;
            studyValues["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
            studyValues["targetCountry"] = goal.TargetCountry;
            studyValues["professionId"] = goal.ProfessionId ?? user.ActiveProfessionId;
            return new { section = "study", values = studyValues };
        }

        var merged = section.ToLowerInvariant() switch
        {
            "notifications" => settings.NotificationsJson = MergeJsonSection(settings.NotificationsJson, request.Values),
            "privacy" => settings.PrivacyJson = MergeJsonSection(settings.PrivacyJson, request.Values),
            "accessibility" => settings.AccessibilityJson = MergeJsonSection(settings.AccessibilityJson, request.Values),
            "audio" => settings.AudioJson = MergeJsonSection(settings.AudioJson, request.Values),
            _ => throw ApiException.Validation(
                "unknown_settings_section",
                $"Unknown settings section '{section}'.",
                [new ApiFieldError("section", "unknown_section", "Choose a supported settings section.")])
        };

        await RecordEventAsync(userId, "settings_changed", new { userId, section = section.ToLowerInvariant() }, cancellationToken);
        LogAudit(userId, "Updated", "Settings", section.ToLowerInvariant(), $"Updated {section} settings");
        await db.SaveChangesAsync(cancellationToken);
        return new { section, values = JsonSupport.Deserialize<Dictionary<string, object?>>(merged, new Dictionary<string, object?>()) };
    }

    public async Task<object> GetDiagnosticOverviewAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var session = await db.DiagnosticSessions.Where(x => x.UserId == userId).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return new
            {
                diagnosticId = (string?)null,
                state = "not_started",
                estimatedTotalMinutes = 120,
                disclaimer = "Diagnostic results are training estimates only and are not official OET scores.",
                resumable = false,
                startRoute = "/app/diagnostic",
                subtests = new[]
                {
                    new { subtest = "writing", estimatedDurationMinutes = 45, state = "not_started", route = "/app/diagnostic/writing" },
                    new { subtest = "speaking", estimatedDurationMinutes = 20, state = "not_started", route = "/app/diagnostic/speaking" },
                    new { subtest = "reading", estimatedDurationMinutes = 30, state = "not_started", route = "/app/diagnostic/reading" },
                    new { subtest = "listening", estimatedDurationMinutes = 25, state = "not_started", route = "/app/diagnostic/listening" }
                }
            };
        }

        var subtests = await db.DiagnosticSubtests.Where(x => x.DiagnosticSessionId == session.Id).ToListAsync(cancellationToken);

        return new
        {
            diagnosticId = session.Id,
            state = ToApiState(session.State),
            estimatedTotalMinutes = subtests.Sum(x => x.EstimatedDurationMinutes),
            disclaimer = "Diagnostic results are training estimates only and are not official OET scores.",
            resumable = session.State is AttemptState.InProgress or AttemptState.Paused,
            startRoute = "/app/diagnostic",
            subtests = subtests.OrderBy(x => DiagnosticSubtestOrder(x.SubtestCode)).Select(x => new
            {
                subtest = x.SubtestCode,
                estimatedDurationMinutes = x.EstimatedDurationMinutes,
                state = ToApiState(x.State),
                route = $"/app/diagnostic/{x.SubtestCode}"
            })
        };
    }

    public async Task<object> CreateOrResumeDiagnosticAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var active = await db.DiagnosticSessions.Where(x => x.UserId == userId && x.State == AttemptState.InProgress).FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            if (active.ExpiresAt.HasValue && DateTimeOffset.UtcNow > active.ExpiresAt.Value)
            {
                active.State = AttemptState.Abandoned;
                await db.SaveChangesAsync(cancellationToken);
                // Fall through to create a new session
            }
            else
            {
                return await GetDiagnosticAttemptAsync(userId, active.Id, cancellationToken);
            }
        }

        var id = $"diag-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var session = new DiagnosticSession
        {
            Id = id,
            UserId = userId,
            State = AttemptState.InProgress,
            StartedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        db.DiagnosticSessions.Add(session);
        db.DiagnosticSubtests.AddRange(
            new DiagnosticSubtestStatus { Id = $"{id}-w", DiagnosticSessionId = id, SubtestCode = "writing", State = AttemptState.NotStarted, EstimatedDurationMinutes = 45 },
            new DiagnosticSubtestStatus { Id = $"{id}-s", DiagnosticSessionId = id, SubtestCode = "speaking", State = AttemptState.NotStarted, EstimatedDurationMinutes = 20 },
            new DiagnosticSubtestStatus { Id = $"{id}-r", DiagnosticSessionId = id, SubtestCode = "reading", State = AttemptState.NotStarted, EstimatedDurationMinutes = 30 },
            new DiagnosticSubtestStatus { Id = $"{id}-l", DiagnosticSessionId = id, SubtestCode = "listening", State = AttemptState.NotStarted, EstimatedDurationMinutes = 25 }
        );
        await RecordEventAsync(userId, "diagnostic_started", new { userId, diagnosticId = id }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetDiagnosticAttemptAsync(userId, id, cancellationToken);
    }

    public async Task<object> GetDiagnosticAttemptAsync(string userId, string diagnosticId, CancellationToken cancellationToken)
    {
        var session = await GetDiagnosticSessionOwnedByUserAsync(userId, diagnosticId, cancellationToken);
        var subtests = await db.DiagnosticSubtests.Where(x => x.DiagnosticSessionId == diagnosticId).ToListAsync(cancellationToken);
        return new
        {
            diagnosticId = session.Id,
            state = ToApiState(session.State),
            startedAt = session.StartedAt,
            completedAt = session.CompletedAt,
            subtests = subtests.OrderBy(x => DiagnosticSubtestOrder(x.SubtestCode)).Select(x => new
            {
                id = x.Id,
                subtest = x.SubtestCode,
                state = ToApiState(x.State),
                estimatedDurationMinutes = x.EstimatedDurationMinutes,
                attemptId = x.AttemptId
            })
        };
    }

    public async Task<object> GetDiagnosticHubAsync(string userId, string diagnosticId, CancellationToken cancellationToken)
    {
        var session = await GetDiagnosticSessionOwnedByUserAsync(userId, diagnosticId, cancellationToken);
        var subtests = await db.DiagnosticSubtests.Where(x => x.DiagnosticSessionId == diagnosticId).ToListAsync(cancellationToken);
        var completed = subtests.Count(x => x.State == AttemptState.Completed);

        return new
        {
            diagnosticId = session.Id,
            state = ToApiState(session.State),
            completedCount = completed,
            totalCount = subtests.Count,
            progressPercent = subtests.Count == 0 ? 0 : (int)Math.Round(completed * 100.0 / subtests.Count),
            cards = subtests.OrderBy(x => DiagnosticSubtestOrder(x.SubtestCode)).Select(x => new
            {
                subtest = x.SubtestCode,
                state = ToApiState(x.State),
                routeHint = x.State == AttemptState.Completed ? "/app/diagnostic/results" : $"/app/diagnostic/{x.SubtestCode}",
                estimatedDurationMinutes = x.EstimatedDurationMinutes,
                attemptId = x.AttemptId
            })
        };
    }

    public async Task<object> GetDiagnosticResultsAsync(string userId, string diagnosticId, CancellationToken cancellationToken)
    {
        var session = await GetDiagnosticSessionOwnedByUserAsync(userId, diagnosticId, cancellationToken);
        var diagnosticSubtests = await db.DiagnosticSubtests
            .Where(x => x.DiagnosticSessionId == diagnosticId)
            .ToListAsync(cancellationToken);
        var readiness = await db.ReadinessSnapshots.Where(x => x.UserId == session.UserId).OrderByDescending(x => x.ComputedAt).FirstAsync(cancellationToken);
        var attemptIds = diagnosticSubtests
            .Where(x => !string.IsNullOrWhiteSpace(x.AttemptId))
            .Select(x => x.AttemptId!)
            .ToList();
        var evaluations = await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .ToListAsync(cancellationToken);
        var readinessPayload = JsonSupport.Deserialize<Dictionary<string, object?>>(readiness.PayloadJson, new Dictionary<string, object?>());
        var readinessBySubtest = readinessPayload.TryGetValue("subTests", out var subtestsValue) && subtestsValue is not null
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(subtestsValue), [])
            : [];

        var results = diagnosticSubtests
            .OrderBy(x => DiagnosticSubtestOrder(x.SubtestCode))
            .Select(subtestStatus =>
        {
            var eval = evaluations.FirstOrDefault(x => x.AttemptId == subtestStatus.AttemptId);
            if (eval is null)
            {
                return (object)new
                {
                    subTest = ToDisplaySubtest(subtestStatus.SubtestCode),
                    state = ToApiState(subtestStatus.State),
                    scoreRange = (string?)null,
                    confidence = "unknown",
                    strengths = Array.Empty<string>(),
                    issues = Array.Empty<string>(),
                    readiness = 0,
                    criterionBreakdown = Array.Empty<object>()
                };
            }

            var score = eval.ScoreRange;
            var criterionBreakdown = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(eval.CriterionScoresJson, []);
            var readinessMatch = readinessBySubtest.FirstOrDefault(x => string.Equals(x.GetValueOrDefault("name")?.ToString(), ToDisplaySubtest(eval.SubtestCode), StringComparison.OrdinalIgnoreCase));
            return (object)new
            {
                subTest = ToDisplaySubtest(eval.SubtestCode),
                state = ToAsyncState(eval.State),
                scoreRange = score,
                confidence = eval.ConfidenceBand.ToString(),
                strengths = JsonSupport.Deserialize<List<string>>(eval.StrengthsJson, []),
                issues = JsonSupport.Deserialize<List<string>>(eval.IssuesJson, []),
                readiness = readinessMatch is not null && int.TryParse(readinessMatch.GetValueOrDefault("readiness")?.ToString(), out var value) ? value : 60,
                criterionBreakdown = criterionBreakdown.Select(x => new
                {
                    name = CriterionLabelFromCode(x.GetValueOrDefault("criterionCode")?.ToString()),
                    score = ParseCriterionScore(x.GetValueOrDefault("scoreRange")?.ToString()),
                    maxScore = 6,
                    grade = ScoreRangeToGrade(x.GetValueOrDefault("scoreRange")?.ToString()),
                    explanation = x.GetValueOrDefault("explanation")?.ToString() ?? string.Empty,
                    anchoredComments = Array.Empty<object>(),
                    omissions = Array.Empty<string>(),
                    unnecessaryDetails = Array.Empty<string>(),
                    revisionSuggestions = Array.Empty<string>(),
                    strengths = Array.Empty<string>(),
                    issues = Array.Empty<string>()
                })
            };
        })
        .ToList();

        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        var planJob = await db.BackgroundJobs
            .Where(x => x.Type == JobType.StudyPlanRegeneration && x.ResourceId == plan.Id)
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new
        {
            diagnosticId,
            disclaimer = "These are training estimates only and should not be interpreted as official OET results.",
            readiness = readinessPayload,
            results,
            topWeakCriteria = evaluations
                .SelectMany(x => JsonSupport.Deserialize<List<Dictionary<string, object?>>>(x.CriterionScoresJson, []))
                .Take(4),
            recommendedIntensity = new { hoursPerWeek = 12, rationale = "Your current readiness suggests concentrated writing and speaking practice over the next 6-8 weeks." },
            firstStudyWeek = new[]
            {
                new { day = "Day 1", action = "Writing task focused on discharge summaries", route = "/app/writing/tasks/wt-001" },
                new { day = "Day 2", action = "Speaking fluency drill with roleplay", route = "/app/speaking/task/st-001" },
                new { day = "Day 3", action = "Reading detail extraction practice", route = "/app/reading/task/rt-001" }
            },
            upgradePrompt = new { shouldShow = true, reason = "Expert review can validate your highest-priority writing and speaking gaps." },
            studyPlan = new
            {
                planId = plan.Id,
                state = ToAsyncState(plan.State),
                nextPollAfterMs = plan.State is AsyncState.Queued or AsyncState.Processing ? 2000 : (int?)null,
                statusReasonCode = planJob?.StatusReasonCode,
                statusMessage = planJob?.StatusMessage,
                retryAfterMs = planJob?.RetryAfterMs
            }
        };
    }

    public async Task<object> GetDashboardAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var readiness = await GetReadinessAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var plan = await GetStudyPlanAsync(userId, cancellationToken);
        var activePlan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var latestEvaluation = await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var latestAttempt = latestEvaluation is null
            ? null
            : await db.Attempts.FirstAsync(x => x.Id == latestEvaluation.AttemptId, cancellationToken);
        var pendingReviews = await db.ReviewRequests
            .Where(x => attemptIds.Contains(x.AttemptId) && (x.State == ReviewRequestState.Submitted || x.State == ReviewRequestState.Queued || x.State == ReviewRequestState.InReview))
            .CountAsync(cancellationToken);
        var streak = Math.Min(7, await db.Attempts.CountAsync(x => x.UserId == userId, cancellationToken));
        var todaysTasks = await db.StudyPlanItems
            .Where(x => x.StudyPlanId == activePlan.Id)
            .OrderBy(x => x.DueDate)
            .Take(2)
            .ToListAsync(cancellationToken);

        return new
        {
            cards = new
            {
                readiness,
                examDate = new { value = goal.TargetExamDate, route = "/app/goals" },
                todaysTasks = todaysTasks.Select(StudyPlanItemDto),
                latestEvaluatedSubmission = latestEvaluation is null || latestAttempt is null
                    ? null
                    : new { evaluationId = latestEvaluation.Id, attemptId = latestAttempt.Id, subtest = latestEvaluation.SubtestCode, scoreRange = latestEvaluation.ScoreRange, route = $"/app/{latestEvaluation.SubtestCode}/result/{latestEvaluation.Id}" },
                weakCriteria = latestEvaluation is null
                    ? new List<Dictionary<string, object?>>()
                    : JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestEvaluation.CriterionScoresJson, []),
                momentum = new { streakDays = streak, completionRate = 0.78 },
                nextMockRecommendation = new { title = "Full OET Mock Test", route = "/app/mocks", rationale = "A full mock will confirm whether Writing gains are transferring under pressure." },
                pendingExpertReviews = new { count = pendingReviews, route = "/app/reviews" }
            },
            primaryActions = new[]
            {
                new { id = "resume-study-plan", label = "Resume Study Plan", route = "/app/study-plan" },
                new { id = "start-next-task", label = "Start Next Task", route = "/app/writing/tasks/wt-001" },
                new { id = "view-latest-feedback", label = "View Latest Feedback", route = latestEvaluation is null ? "/app/writing" : $"/app/writing/result/{latestEvaluation.Id}" }
            },
            partialData = latestEvaluation is null,
            lastUpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<object> GetStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        var items = await db.StudyPlanItems.Where(x => x.StudyPlanId == plan.Id).OrderBy(x => x.DueDate).ToListAsync(cancellationToken);
        var latestJob = await db.BackgroundJobs
            .Where(x => x.Type == JobType.StudyPlanRegeneration && x.ResourceId == plan.Id)
            .OrderByDescending(x => x.LastTransitionAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new
        {
            planId = plan.Id,
            version = plan.Version,
            generatedAt = plan.GeneratedAt,
            state = ToAsyncState(plan.State),
            checkpoint = plan.Checkpoint,
            weakSkillFocus = plan.WeakSkillFocus,
            items = items.Select(StudyPlanItemDto).ToList(),
            statusReasonCode = latestJob?.StatusReasonCode,
            statusMessage = latestJob?.StatusMessage,
            retryAfterMs = latestJob?.RetryAfterMs,
            lastTransitionAt = latestJob?.LastTransitionAt
        };
    }

    public async Task<object> RegenerateStudyPlanAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var plan = await GetActiveStudyPlanEntityAsync(userId, cancellationToken);
        plan.State = AsyncState.Queued;
        await QueueJobAsync(JobType.StudyPlanRegeneration, resourceId: plan.Id, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { planId = plan.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> CompleteStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Completed;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_completed", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> SkipStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Skipped;
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_skipped", new { itemId = item.Id, planId = item.StudyPlanId, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> RescheduleStudyPlanItemAsync(string userId, string itemId, StudyPlanRescheduleRequest request, CancellationToken cancellationToken)
    {
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.Rescheduled;
        item.DueDate = request.DueDate ?? item.DueDate.AddDays(1);
        var plan = await db.StudyPlans.FirstAsync(x => x.Id == item.StudyPlanId, cancellationToken);
        await RecordEventAsync(plan.UserId, "study_plan_item_rescheduled", new { itemId = item.Id, planId = item.StudyPlanId, dueDate = item.DueDate, subtest = item.SubtestCode }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> ResetStudyPlanItemAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.Status = StudyPlanItemStatus.NotStarted;
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> SwapStudyPlanItemAsync(string userId, string itemId, StudyPlanSwapRequest request, CancellationToken cancellationToken)
    {
        var item = await GetStudyPlanItemOwnedByUserAsync(userId, itemId, cancellationToken);
        item.ContentId = request.ReplacementContentId ?? item.ContentId;
        await db.SaveChangesAsync(cancellationToken);
        return StudyPlanItemDto(item);
    }

    public async Task<object> GetReadinessAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var snapshot = await db.ReadinessSnapshots.Where(x => x.UserId == userId).OrderByDescending(x => x.ComputedAt).FirstAsync(cancellationToken);
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(snapshot.PayloadJson, new Dictionary<string, object?>());
        payload["snapshotId"] = snapshot.Id;
        payload["computedAt"] = snapshot.ComputedAt;
        payload["snapshotVersion"] = snapshot.Version;
        await RecordEventAsync(userId, "readiness_viewed", new { userId, snapshotId = snapshot.Id, computedAt = snapshot.ComputedAt }, cancellationToken);
        return payload;
    }

    public async Task<object> GetProgressAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var submissions = await db.Attempts.Where(x => x.UserId == userId && x.State == AttemptState.Completed).ToListAsync(cancellationToken);
        var attemptIds = submissions.Select(x => x.Id).ToList();
        var evaluations = await db.Evaluations
            .Where(x => x.State == AsyncState.Completed && attemptIds.Contains(x.AttemptId))
            .OrderBy(x => x.GeneratedAt)
            .ToListAsync(cancellationToken);
        var criterionTrend = evaluations
            .SelectMany(evaluation =>
                JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, [])
                    .Select(criterion => new
                    {
                        criterionCode = criterion.GetValueOrDefault("criterionCode")?.ToString(),
                        criterionLabel = CriterionLabelFromCode(criterion.GetValueOrDefault("criterionCode")?.ToString()),
                        score = ParseCriterionScore(criterion.GetValueOrDefault("scoreRange")?.ToString()),
                        generatedAt = evaluation.GeneratedAt,
                        subtest = evaluation.SubtestCode
                    }))
            .ToList();
        var reviews = await db.ReviewRequests
            .Where(x => attemptIds.Contains(x.AttemptId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var completedReviews = reviews.Where(x => x.CompletedAt.HasValue).ToList();
        var averageTurnaroundHours = completedReviews.Count == 0
            ? (double?)null
            : Math.Round(completedReviews.Average(x => (x.CompletedAt!.Value - x.CreatedAt).TotalHours), 1);

        return new
        {
            trend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = x.ScoreRange, generatedAt = x.GeneratedAt }),
            subtestTrend = evaluations.Select((x, index) => new { week = $"Week {index + 1}", subtest = x.SubtestCode, scoreRange = x.ScoreRange, generatedAt = x.GeneratedAt }),
            criterionTrend,
            completion = new[]
            {
                new { day = "Mon", completed = 3 },
                new { day = "Tue", completed = 2 },
                new { day = "Wed", completed = 4 },
                new { day = "Thu", completed = 1 },
                new { day = "Fri", completed = 3 },
                new { day = "Sat", completed = 5 },
                new { day = "Sun", completed = 2 }
            },
            submissionVolume = new[]
            {
                new { week = "W1", submissions = 4 },
                new { week = "W2", submissions = 6 },
                new { week = "W3", submissions = 5 },
                new { week = "W4", submissions = 8 },
                new { week = "W5", submissions = 7 }
            },
            reviewUsage = new
            {
                totalRequests = reviews.Count,
                completedRequests = completedReviews.Count,
                averageTurnaroundHours,
                creditsConsumed = reviews.Count(x => string.Equals(x.PaymentSource, "credits", StringComparison.OrdinalIgnoreCase))
            },
            totals = new { completedAttempts = submissions.Count, completedEvaluations = evaluations.Count },
            freshness = new
            {
                generatedAt = DateTimeOffset.UtcNow,
                usesFallbackSeries = evaluations.Count == 0
            }
        };
    }

    public async Task<object> GetSubmissionsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attempts = await db.Attempts.Where(x => x.UserId == userId).OrderByDescending(x => x.SubmittedAt ?? x.StartedAt).ToListAsync(cancellationToken);
        var items = new List<object>();

        foreach (var attempt in attempts)
        {
            var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
            var eval = await db.Evaluations.Where(x => x.AttemptId == attempt.Id).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
            var review = await db.ReviewRequests.Where(x => x.AttemptId == attempt.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            var canRequestReview = attempt.State == AttemptState.Completed && attempt.SubtestCode is "writing" or "speaking";
            items.Add(new
            {
                submissionId = attempt.Id,
                contentId = content.Id,
                taskName = content.Title,
                subtest = content.SubtestCode,
                attemptDate = attempt.SubmittedAt ?? attempt.StartedAt,
                scoreEstimate = eval?.ScoreRange,
                reviewStatus = review is null ? "not_requested" : ToReviewRequestState(review.State),
                evaluationId = eval?.Id,
                state = ToApiState(attempt.State),
                comparisonGroupId = attempt.ComparisonGroupId,
                canRequestReview,
                actions = new
                {
                    reopenFeedbackRoute = $"/app/submissions/{attempt.Id}",
                    compareRoute = $"/app/submissions/compare?leftId={attempt.Id}",
                    requestReviewRoute = canRequestReview ? $"/app/submissions/{attempt.Id}?requestReview=1" : null
                }
            });
        }

        return new { items, nextCursor = (string?)null };
    }

    public async Task<object> CompareSubmissionsAsync(string userId, string? leftId, string? rightId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attempts = await db.Attempts
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);
        var left = leftId is not null ? await GetAttemptOwnedByUserAsync(userId, leftId, cancellationToken) : attempts.ElementAtOrDefault(0);
        Attempt? right = null;
        if (rightId is not null)
        {
            right = await GetAttemptOwnedByUserAsync(userId, rightId, cancellationToken);
        }
        else if (left is not null)
        {
            right = attempts
                .Where(candidate => candidate.Id != left.Id)
                .Where(candidate =>
                    (!string.IsNullOrWhiteSpace(left.ComparisonGroupId) && string.Equals(candidate.ComparisonGroupId, left.ComparisonGroupId, StringComparison.Ordinal))
                    || (candidate.SubtestCode == left.SubtestCode && candidate.ContentId == left.ContentId)
                    || (!string.IsNullOrWhiteSpace(left.ParentAttemptId) && candidate.Id == left.ParentAttemptId)
                    || (!string.IsNullOrWhiteSpace(candidate.ParentAttemptId) && candidate.ParentAttemptId == left.Id))
                .OrderByDescending(candidate => candidate.SubmittedAt ?? candidate.StartedAt)
                .FirstOrDefault();
        }
        else
        {
            right = attempts.Skip(1).FirstOrDefault();
        }

        if (left is null || right is null)
        {
            return new { canCompare = false, reason = "Need at least two submissions to compare." };
        }

        var leftEval = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == left.Id, cancellationToken);
        var rightEval = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == right.Id, cancellationToken);

        return new
        {
            canCompare = true,
            left = new { attemptId = left.Id, evaluationId = leftEval?.Id, scoreRange = leftEval?.ScoreRange, subtest = left.SubtestCode },
            right = new { attemptId = right.Id, evaluationId = rightEval?.Id, scoreRange = rightEval?.ScoreRange, subtest = right.SubtestCode },
            summary = "The more recent submission shows stronger structure and slightly improved score confidence.",
            comparisonGroupId = left.ComparisonGroupId ?? right.ComparisonGroupId
        };
    }

    public async Task<object> GetWritingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var tasks = await GetTasksBySubtestAsync("writing", cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId && x.SubtestCode == "writing").Select(x => x.Id).ToListAsync(cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var attempts = await db.Attempts
            .Where(x => x.UserId == userId && x.SubtestCode == "writing")
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToListAsync(cancellationToken);
        var draftAttempt = await db.Attempts
            .Where(x => x.UserId == userId && x.SubtestCode == "writing" && x.State == AttemptState.InProgress)
            .OrderByDescending(x => x.LastClientSyncAt ?? x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var latestEvaluation = await db.Evaluations
            .Where(x => x.SubtestCode == "writing" && attemptIds.Contains(x.AttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var criterionDrillLibrary = latestEvaluation is not null
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(latestEvaluation.CriterionScoresJson, [])
                .OrderBy(x => ParseCriterionScore(x.GetValueOrDefault("scoreRange")?.ToString()))
                .Take(3)
                .Select(x => new
                {
                    criterionCode = x.GetValueOrDefault("criterionCode")?.ToString(),
                    criterionLabel = CriterionLabelFromCode(x.GetValueOrDefault("criterionCode")?.ToString()),
                    rationale = x.GetValueOrDefault("explanation")?.ToString() ?? "Target this criterion with a focused writing drill.",
                    route = $"/app/writing/tasks?criterion={x.GetValueOrDefault("criterionCode")}"
                })
                .ToList<object>()
            : tasks.Take(3).Select(task => task).ToList();
        var practiceLibrary = tasks.Take(4).ToList();
        var recommendedTask = practiceLibrary.FirstOrDefault();
        var evaluations = await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .ToListAsync(cancellationToken);
        var evaluationByAttemptId = evaluations
            .GroupBy(x => x.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());

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
                    route = evaluation?.Id is null ? $"/app/writing/attempt/{attempt.Id}" : $"/app/writing/result/{evaluation.Id}"
                };
            }),
            reviewCredits = new
            {
                available = wallet.CreditBalance,
                route = "/app/reviews",
                billingRoute = "/app/billing"
            },
            fullMockEntry = new
            {
                title = "Full OET Mock Test",
                route = "/app/mocks",
                rationale = "Use a full mock to confirm whether writing gains are transferring under timed conditions."
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluation is null ? null : await GetWritingEvaluationSummaryAsync(userId, latestEvaluation.Id, cancellationToken),
            actions = new[]
            {
                new { label = "Browse Writing Tasks", route = "/app/writing/tasks" },
                new { label = draftAttempt is null ? "Start Writing Task" : "Resume Draft", route = draftAttempt is null ? "/app/writing/tasks" : $"/app/writing/attempt/{draftAttempt.Id}" }
            }
        };
    }

    public async Task<List<object>> GetWritingTasksAsync(CancellationToken cancellationToken) => await GetTasksBySubtestAsync("writing", cancellationToken);

    public async Task<object> GetWritingTaskAsync(string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing", cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing task not found.");
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

    public async Task<object> CreateWritingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken)
        => await CreateAttemptAsync(userId, request, "writing", cancellationToken);

    public async Task<object> GetWritingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task<object> UpdateWritingDraftAsync(string userId, string attemptId, DraftUpdateRequest request, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
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

    public async Task<object> HeartbeatAttemptAsync(string userId, string attemptId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        attempt.ElapsedSeconds = request.ElapsedSeconds;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.DeviceType)) attempt.DeviceType = request.DeviceType;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, elapsedSeconds = attempt.ElapsedSeconds, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    public async Task<object> SubmitWritingAttemptAsync(string userId, string attemptId, SubmitAttemptRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("writing-submit", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == attemptId, cancellationToken);
            return new { attemptId = attempt.Id, evaluationId = existing?.Id, state = existing is null ? "queued" : ToAsyncState(existing.State) };
        }

        if (request.Content is not null) attempt.DraftContent = request.Content;
        if (string.IsNullOrWhiteSpace(attempt.DraftContent))
        {
            throw ApiException.Validation(
                "writing_content_required",
                "Writing content is required before submission.",
                [new ApiFieldError("content", "required", "Enter your response before submitting.")]);
        }

        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);

        var evaluation = new Evaluation
        {
            Id = $"we-{Guid.NewGuid():N}",
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
        var response = new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000 };
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "writing", contentId = attempt.ContentId }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("writing-submit", request.IdempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<object> GetWritingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);

        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            subtest = evaluation.SubtestCode,
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            gradeRange = evaluation.GradeRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            generatedAt = evaluation.GeneratedAt,
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = evaluation.LearnerDisclaimer,
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
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var evaluation = await db.Evaluations.Where(x => x.AttemptId == attemptId).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        var related = await db.Attempts.Where(x => x.ParentAttemptId == attemptId && x.UserId == userId).OrderByDescending(x => x.StartedAt).ToListAsync(cancellationToken);
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
            actions = new[] { new { label = "Submit Revision", route = $"/app/writing/revision/{attemptId}" } }
        };
    }

    public async Task<object> SubmitWritingRevisionAsync(string userId, string attemptId, RevisionSubmitRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("writing-revision-submit", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var baseAttempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("writing-revision-submit", request.IdempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<object> GetWritingModelAnswerAsync(string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "writing", cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Writing model answer not found.");
        return new
        {
            contentId = item.Id,
            title = item.Title,
            professionId = item.ProfessionId,
            payload = JsonSupport.Deserialize<Dictionary<string, object?>>(item.ModelAnswerJson, new Dictionary<string, object?>())
        };
    }

    public async Task<object> GetSpeakingHomeAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var tasks = await GetTasksBySubtestAsync("speaking", cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId && x.SubtestCode == "speaking").Select(x => x.Id).ToListAsync(cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var attempts = await db.Attempts
            .Where(x => x.UserId == userId && x.SubtestCode == "speaking")
            .OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
            .Take(4)
            .ToListAsync(cancellationToken);
        var latestEvaluation = await db.Evaluations.Where(x => x.SubtestCode == "speaking" && attemptIds.Contains(x.AttemptId)).OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        var commonIssues = latestEvaluation is null
            ? new[] { "Build smoother openings for role plays.", "Keep the professional tone consistent." }
            : JsonSupport.Deserialize<List<string>>(latestEvaluation.IssuesJson, [])
                .DefaultIfEmpty("Build smoother openings for role plays.")
                .ToArray();
        var evaluationByAttempt = await db.Evaluations
            .Where(x => attemptIds.Contains(x.AttemptId))
            .OrderByDescending(x => x.GeneratedAt)
            .ToListAsync(cancellationToken);
        var evaluationLookup = evaluationByAttempt
            .GroupBy(x => x.AttemptId)
            .ToDictionary(group => group.Key, group => group.First());
        return new
        {
            recommendedRolePlay = tasks.FirstOrDefault(),
            commonIssuesToImprove = commonIssues,
            drillGroups = new object[]
            {
                new
                {
                    id = "pronunciation",
                    title = "Pronunciation drills",
                    items = new[]
                    {
                        new { id = "sp-drill-1", title = "Stress important treatment words", route = "/app/speaking/review/sa-001" }
                    }
                },
                new
                {
                    id = "empathy_clarification",
                    title = "Empathy and clarification drills",
                    items = new[]
                    {
                        new { id = "sp-drill-2", title = "Clarify concerns without losing structure", route = "/app/speaking/tasks" }
                    }
                }
            },
            pastAttempts = attempts.Select(attempt =>
            {
                evaluationLookup.TryGetValue(attempt.Id, out var evaluation);
                return new
                {
                    attemptId = attempt.Id,
                    state = ToApiState(attempt.State),
                    scoreEstimate = evaluation?.ScoreRange,
                    route = evaluation?.Id is null ? $"/app/speaking/attempt/{attempt.Id}" : $"/app/speaking/result/{evaluation.Id}"
                };
            }),
            reviewCredits = new
            {
                available = wallet.CreditBalance,
                route = "/app/reviews"
            },
            featuredTasks = tasks.Take(3),
            latestEvaluation = latestEvaluation is null ? null : await GetSpeakingEvaluationSummaryAsync(userId, latestEvaluation.Id, cancellationToken),
            tips = new[]
            {
                "Use the mic check before longer speaking sessions.",
                "Prioritise professional tone and smooth transitions."
            }
        };
    }

    public async Task<List<object>> GetSpeakingTasksAsync(CancellationToken cancellationToken) => await GetTasksBySubtestAsync("speaking", cancellationToken);

    public async Task<object> GetSpeakingTaskAsync(string contentId, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == "speaking", cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", "Speaking task not found.");
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["title"] = item.Title,
            ["professionId"] = item.ProfessionId,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["scenarioType"] = item.ScenarioType,
            ["modeSupport"] = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, [])
        }, detail);
    }

    public async Task<object> CreateSpeakingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken)
        => await CreateAttemptAsync(userId, request, "speaking", cancellationToken);

    public async Task<object> GetSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    public async Task<object> CreateSpeakingUploadSessionAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var uploadId = $"upload-{Guid.NewGuid():N}";
        var upload = new UploadSession
        {
            Id = uploadId,
            AttemptId = attemptId,
            UploadUrl = platformLinks.BuildApiUrl($"/v1/speaking/upload-sessions/{uploadId}/content"),
            StorageKey = $"audio/{attemptId}/{uploadId}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            State = UploadState.Pending
        };
        db.UploadSessions.Add(upload);
        await db.SaveChangesAsync(cancellationToken);
        return new
        {
            uploadSessionId = upload.Id,
            uploadUrl = upload.UploadUrl,
            storageKey = upload.StorageKey,
            expiresAt = upload.ExpiresAt,
            httpMethod = "PUT",
            signed = false,
            requiresAuth = true
        };
    }

    public async Task<object> UploadSpeakingAudioAsync(
        string userId,
        string uploadSessionId,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var upload = await GetUploadSessionOwnedByUserAsync(userId, uploadSessionId, cancellationToken);
        if (upload.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw ApiException.Conflict(
                "upload_session_expired",
                "This upload session has expired. Create a new upload session and try again.",
                [new ApiFieldError("uploadSessionId", "expired", "Request a new upload session before uploading audio.")]);
        }

        if (!mediaStorage.IsAllowedAudioContentType(contentType))
        {
            throw ApiException.Validation(
                "unsupported_audio_content_type",
                "Only supported audio formats can be uploaded.",
                [new ApiFieldError("audio", "unsupported_content_type", "Upload a browser-recorded WebM or another supported audio format.")]);
        }

        var bytesWritten = await mediaStorage.SaveAsync(upload.StorageKey, content, cancellationToken);
        if (bytesWritten == 0)
        {
            throw ApiException.Validation(
                "empty_audio_upload",
                "The uploaded audio file was empty.",
                [new ApiFieldError("audio", "empty", "Record some audio before uploading.")]);
        }

        upload.State = UploadState.Uploaded;
        await db.SaveChangesAsync(cancellationToken);

        return new
        {
            uploadSessionId = upload.Id,
            storageKey = upload.StorageKey,
            state = "uploaded",
            sizeBytes = bytesWritten,
            contentType = contentType
        };
    }

    public async Task<object> CompleteSpeakingUploadAsync(string userId, string attemptId, UploadCompleteRequest request, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var upload = await GetUploadSessionForCompletionAsync(userId, attemptId, request, cancellationToken);
        if (upload.State != UploadState.Uploaded || !mediaStorage.Exists(upload.StorageKey))
        {
            throw ApiException.Validation(
                "upload_binary_missing",
                "Upload the audio file before marking the speaking upload as complete.",
                [new ApiFieldError("audio", "missing", "Send the recording bytes to the upload URL first.")]);
        }

        var resolvedContentType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType;
        var storedLength = mediaStorage.GetLength(upload.StorageKey);
        attempt.AudioUploadState = UploadState.Uploaded;
        attempt.AudioObjectKey = upload.StorageKey;
        attempt.AudioMetadataJson = JsonSupport.Serialize(new
        {
            fileName = request.FileName ?? $"{attemptId}.webm",
            sizeBytes = storedLength,
            reportedSizeBytes = request.SizeBytes,
            durationSeconds = request.DurationSeconds,
            captureMethod = request.CaptureMethod ?? "browser-recording",
            contentType = resolvedContentType
        });

        var existingTranscriptionJob = await db.BackgroundJobs
            .Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription && x.State != AsyncState.Failed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingTranscriptionJob is null)
        {
            await QueueJobAsync(JobType.SpeakingTranscription, attemptId: attemptId, resourceId: attemptId, cancellationToken: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new { attemptId, audioUploadState = "uploaded", processingState = "queued", canSubmit = true };
    }

    public async Task<object> SubmitSpeakingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (attempt.State is AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstOrDefaultAsync(x => x.AttemptId == attemptId, cancellationToken);
            return new { attemptId, evaluationId = existing?.Id, state = existing is null ? "queued" : ToAsyncState(existing.State) };
        }

        if (attempt.AudioUploadState != UploadState.Uploaded)
        {
            throw ApiException.Validation(
                "speaking_audio_required",
                "Upload audio before submitting this speaking attempt.",
                [new ApiFieldError("audio", "required", "Complete the audio upload before submission.")]);
        }

        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Evaluating, cancellationToken);
        var evaluation = new Evaluation
        {
            Id = $"se-{Guid.NewGuid():N}",
            AttemptId = attempt.Id,
            SubtestCode = "speaking",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            ConfidenceBand = ConfidenceBand.Low,
            StrengthsJson = "[]",
            IssuesJson = "[]",
            CriterionScoresJson = "[]",
            FeedbackItemsJson = "[]",
            ModelExplanationSafe = "Speaking evaluation queued.",
            LearnerDisclaimer = "Training estimate pending.",
            StatusReasonCode = "queued",
            StatusMessage = "Speaking evaluation queued.",
            Retryable = true,
            RetryAfterMs = 2000,
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await QueueJobAsync(JobType.SpeakingEvaluation, attemptId: attempt.Id, resourceId: evaluation.Id, cancellationToken: cancellationToken);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest = "speaking", contentId = attempt.ContentId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> GetSpeakingProcessingAsync(string userId, string attemptId, CancellationToken cancellationToken)
    {
        await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var evaluation = await db.Evaluations.Where(x => x.AttemptId == attemptId).OrderByDescending(x => x.LastTransitionAt).FirstOrDefaultAsync(cancellationToken);
        var transcriptionJob = await db.BackgroundJobs.Where(x => x.AttemptId == attemptId && x.Type == JobType.SpeakingTranscription).OrderByDescending(x => x.LastTransitionAt).FirstOrDefaultAsync(cancellationToken);
        return new
        {
            attemptId,
            transcription = transcriptionJob is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["state"] = ToAsyncState(transcriptionJob.State), ["statusReasonCode"] = transcriptionJob.StatusReasonCode, ["retryAfterMs"] = transcriptionJob.RetryAfterMs },
            evaluation = evaluation is null
                ? (object)new Dictionary<string, object?> { ["state"] = "idle", ["evaluationId"] = null, ["statusReasonCode"] = null, ["retryAfterMs"] = null }
                : new Dictionary<string, object?> { ["evaluationId"] = evaluation.Id, ["state"] = ToAsyncState(evaluation.State), ["statusReasonCode"] = evaluation.StatusReasonCode, ["retryAfterMs"] = evaluation.RetryAfterMs }
        };
    }

    public async Task<object> GetSpeakingEvaluationSummaryAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);

        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            taskTitle = content.Title,
            subtest = "speaking",
            state = ToAsyncState(evaluation.State),
            scoreRange = evaluation.ScoreRange,
            confidenceBand = evaluation.ConfidenceBand.ToString().ToLowerInvariant(),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            generatedAt = evaluation.GeneratedAt,
            nextDrill = new { id = "fluency-drill-001", title = "Fluency: Transition Phrases", description = "Practise moving between handover sections without fillers." },
            modelExplanationSafe = evaluation.ModelExplanationSafe,
            learnerDisclaimer = evaluation.LearnerDisclaimer
        };
    }

    public async Task<object> GetSpeakingReviewAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        return new
        {
            summary = await GetSpeakingEvaluationSummaryAsync(userId, evaluationId, cancellationToken),
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            audioAvailable = !string.IsNullOrWhiteSpace(attempt.AudioObjectKey),
            audioUrl = string.IsNullOrWhiteSpace(attempt.AudioObjectKey)
                ? null
                : platformLinks.BuildApiUrl($"/v1/speaking/evaluations/{Uri.EscapeDataString(evaluationId)}/audio")
        };
    }

    public async Task<StoredMediaFile> GetSpeakingEvaluationAudioAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        if (string.IsNullOrWhiteSpace(attempt.AudioObjectKey))
        {
            throw ApiException.NotFound("audio_not_found", "No uploaded audio is available for this speaking evaluation.");
        }

        var metadata = JsonSupport.Deserialize(attempt.AudioMetadataJson, new Dictionary<string, object?>());
        var contentType = metadata.TryGetValue("contentType", out var value) ? value?.ToString() : null;
        return mediaStorage.OpenRead(attempt.AudioObjectKey, contentType);
    }

    public object SaveDeviceCheck(DeviceCheckRequest request) => new
    {
        state = request.MicrophoneGranted && request.NetworkStable ? "passed" : "attention_required",
        microphoneGranted = request.MicrophoneGranted,
        networkStable = request.NetworkStable,
        deviceType = request.DeviceType ?? "unknown",
        checkedAt = DateTimeOffset.UtcNow
    };

    public async Task<object> GetReadingHomeAsync(CancellationToken cancellationToken)
    {
        var tasks = await GetTasksBySubtestAsync("reading", cancellationToken);
        return new
        {
            featuredTasks = tasks,
            intro = "Reading practice focuses on rapid detail extraction and inference control.",
            entryPoints = new object[]
            {
                new { id = "part-a", title = "Part A", route = (string?)null, available = false },
                new { id = "part-b", title = "Part B", route = (string?)null, available = false },
                new { id = "part-c", title = "Part C", route = "/app/reading/task/rt-001", available = true }
            },
            speedDrills = new[]
            {
                new { id = "reading-speed-1", title = "Timed detail scanning", route = "/app/reading/task/rt-001" }
            },
            accuracyDrills = new[]
            {
                new { id = "reading-accuracy-1", title = "Named concept accuracy drill", route = "/app/reading/task/rt-001" }
            },
            explanations = new[]
            {
                new { id = "reading-explanations", title = "Review answer explanations", route = "/app/history" }
            },
            mockSets = new[]
            {
                new { id = "reading-mock-set", title = "Reading mock set", route = "/app/mocks" }
            }
        };
    }

    public async Task<object> GetReadingTaskAsync(string contentId, CancellationToken cancellationToken) => await GetGenericTaskAsync(contentId, "reading", cancellationToken);
    public async Task<object> CreateReadingAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken) => await CreateAttemptAsync(userId, request, "reading", cancellationToken);
    public async Task<object> GetReadingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await GetWritingAttemptAsync(userId, attemptId, cancellationToken);
    public async Task<object> UpdateReadingAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken) => await UpdateAnswersAsync(userId, attemptId, request, cancellationToken);
    public async Task<object> SubmitReadingAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await SubmitObjectiveAttemptAsync(userId, attemptId, "reading", cancellationToken);
    public async Task<object> GetReadingEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken) => await GetObjectiveEvaluationAsync(userId, evaluationId, cancellationToken);

    public async Task<object> GetListeningHomeAsync(CancellationToken cancellationToken)
    {
        var tasks = await GetTasksBySubtestAsync("listening", cancellationToken);
        return new
        {
            featuredTasks = tasks,
            intro = "Listening practice emphasises accurate capture of numbers, frequencies, and changes in plan.",
            partCollections = new[]
            {
                new { id = "listening-practice", title = "Practice sets", route = "/app/listening/player/lt-001" }
            },
            transcriptBackedReview = new
            {
                title = "Transcript-backed review",
                route = "/app/listening/review/lt-001",
                availableAfterAttempt = true
            },
            distractorDrills = new[]
            {
                new { id = "listening-drill-distractor_confusion", title = "Frequency distractor drill", route = "/app/listening/drills/listening-drill-distractor_confusion" }
            },
            mockSets = new[]
            {
                new { id = "listening-mock-set", title = "Listening mock set", route = "/app/mocks" }
            },
            accessPolicyHints = new
            {
                transcriptReveal = "per_item_post_attempt",
                rationale = "Transcript snippets are revealed only when the task allows it after submission."
            }
        };
    }

    public async Task<object> GetListeningTaskAsync(string contentId, CancellationToken cancellationToken) => await GetGenericTaskAsync(contentId, "listening", cancellationToken);
    public async Task<object> CreateListeningAttemptAsync(string userId, CreateAttemptRequest request, CancellationToken cancellationToken) => await CreateAttemptAsync(userId, request, "listening", cancellationToken);
    public async Task<object> GetListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await GetWritingAttemptAsync(userId, attemptId, cancellationToken);
    public async Task<object> UpdateListeningAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken) => await UpdateAnswersAsync(userId, attemptId, request, cancellationToken);
    public async Task<object> SubmitListeningAttemptAsync(string userId, string attemptId, CancellationToken cancellationToken) => await SubmitObjectiveAttemptAsync(userId, attemptId, "listening", cancellationToken);
    public async Task<object> GetListeningEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken) => await GetObjectiveEvaluationAsync(userId, evaluationId, cancellationToken);
    public Task<object> GetListeningDrillAsync(string drillId, CancellationToken cancellationToken) => Task.FromResult(BuildListeningDrill(drillId));

    public async Task<object> GetMocksAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attempts = await db.MockAttempts.Where(x => x.UserId == userId).OrderByDescending(x => x.StartedAt).ToListAsync(cancellationToken);
        var reports = await db.MockReports.Where(x => attempts.Select(a => a.Id).Contains(x.MockAttemptId)).OrderByDescending(x => x.GeneratedAt).ToListAsync(cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        return new
        {
            collections = new
            {
                fullMocks = new[]
                {
                    new { id = "full-practice", title = "Full OET Mock", mode = "practice", route = "/app/mocks/setup?type=full&mode=practice" },
                    new { id = "full-exam", title = "Full OET Mock", mode = "exam", route = "/app/mocks/setup?type=full&mode=exam" }
                },
                subTestMocks = new[]
                {
                    new { id = "reading-only", title = "Reading-only Mock", subtest = "reading", route = "/app/mocks/setup?type=sub&subtest=reading" },
                    new { id = "listening-only", title = "Listening-only Mock", subtest = "listening", route = "/app/mocks/setup?type=sub&subtest=listening" },
                    new { id = "writing-only", title = "Writing-only Mock", subtest = "writing", route = "/app/mocks/setup?type=sub&subtest=writing" },
                    new { id = "speaking-only", title = "Speaking-only Mock", subtest = "speaking", route = "/app/mocks/setup?type=sub&subtest=speaking" }
                }
            },
            purchasedMockReviews = new
            {
                availableCredits = wallet.CreditBalance,
                supportedSubtests = new[] { "writing", "speaking" },
                route = "/app/reviews"
            },
            recommendedNextMock = new
            {
                title = "Full OET Mock Test",
                route = "/app/mocks",
                rationale = "A full mock will show whether recent study-plan work is improving your cross-subtest readiness."
            },
            attempts = attempts.Select(x => new { mockAttemptId = x.Id, state = ToApiState(x.State), startedAt = x.StartedAt, reportId = x.ReportId }),
            reports = reports.Select(x => JsonSupport.Deserialize<Dictionary<string, object?>>(x.PayloadJson, new Dictionary<string, object?>())),
            options = GetMockOptionsAsync(cancellationToken)
        };
    }

    public object GetMockOptionsAsync(CancellationToken cancellationToken) => new
    {
        options = new object[]
        {
            new { id = "full-practice", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "practice", includeReview = false, strictTimer = false, reviewSelection = "none" },
            new { id = "full-exam", title = "Full OET Mock", type = "full", subType = (string?)null, mode = "exam", includeReview = false, strictTimer = true, reviewSelection = "none" },
            new { id = "writing-only", title = "Writing-only Mock", type = "sub", subType = "writing", mode = "exam", includeReview = true, strictTimer = true, reviewSelection = "current_subtest" }
        }
    };

    public async Task<object> CreateMockAttemptAsync(string userId, MockAttemptCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var id = $"mock-attempt-{Guid.NewGuid():N}";
        var reviewSelection = NormalizeMockReviewSelection(request.MockType, request.SubType, request.IncludeReview, request.ReviewSelection);
        var config = new
        {
            mockType = request.MockType,
            subType = request.SubType,
            mode = request.Mode,
            profession = request.Profession,
            includeReview = request.IncludeReview,
            strictTimer = request.StrictTimer,
            reviewSelection
        };
        var attempt = new MockAttempt
        {
            Id = id,
            UserId = userId,
            ConfigJson = JsonSupport.Serialize(config),
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.MockAttempts.Add(attempt);
        await RecordEventAsync(userId, "mock_started", new { mockAttemptId = attempt.Id, type = request.MockType, subType = request.SubType, mode = request.Mode, reviewSelection }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new
        {
            mockAttemptId = attempt.Id,
            state = ToApiState(attempt.State),
            config,
            sectionStates = MockAttemptSections(attempt.Id, config),
            resumeRoute = $"/app/mocks/player/{attempt.Id}",
            reportRoute = (string?)null
        };
    }

    public async Task<object> GetMockAttemptAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, cancellationToken);
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.ConfigJson, new Dictionary<string, object?>());
        return new
        {
            mockAttemptId = attempt.Id,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            config,
            sectionStates = MockAttemptSections(attempt.Id, config),
            resumeRoute = $"/app/mocks/player/{attempt.Id}",
            reportRoute = attempt.ReportId is null ? null : $"/app/mocks/report/{attempt.ReportId}",
            reportId = attempt.ReportId
        };
    }

    public async Task<object> SubmitMockAttemptAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
    {
        var attempt = await GetMockAttemptOwnedByUserAsync(userId, mockAttemptId, cancellationToken);
        attempt.State = AttemptState.Evaluating;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        await QueueJobAsync(JobType.MockReportGeneration, resourceId: attempt.Id, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { mockAttemptId = attempt.Id, state = "queued", nextPollAfterMs = 2000 };
    }

    public async Task<object> GetMockReportAsync(string userId, string reportId, CancellationToken cancellationToken)
    {
        var report = await GetMockReportOwnedByUserAsync(userId, reportId, cancellationToken);
        var payload = JsonSupport.Deserialize<Dictionary<string, object?>>(report.PayloadJson, new Dictionary<string, object?>());
        payload["state"] = ToAsyncState(report.State);
        payload["generatedAt"] = report.GeneratedAt;
        payload["studyPlanUpdateCta"] = new
        {
            label = "Update study plan",
            route = "/app/study-plan"
        };
        return payload;
    }

    public async Task<object> GetReviewsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var attemptIds = await db.Attempts.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var reviews = await db.ReviewRequests.Where(x => attemptIds.Contains(x.AttemptId)).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return new
        {
            items = reviews.Select(x => new
            {
                reviewRequestId = x.Id,
                attemptId = x.AttemptId,
                subtest = x.SubtestCode,
                state = ToReviewRequestState(x.State),
                turnaroundOption = x.TurnaroundOption,
                focusAreas = JsonSupport.Deserialize<List<string>>(x.FocusAreasJson, []),
                createdAt = x.CreatedAt,
                completedAt = x.CompletedAt
            })
        };
    }

    public async Task<object> GetReviewEligibilityAsync(string userId, string? attemptId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        Attempt? attempt = null;
        if (!string.IsNullOrWhiteSpace(attemptId))
        {
            attempt = await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken);
        }

        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var canRequest = attempt is not null && attempt.State == AttemptState.Completed && attempt.SubtestCode is "writing" or "speaking";
        var reasons = new List<string>();
        if (attempt is null) reasons.Add("attempt_not_found");
        else if (attempt.State != AttemptState.Completed) reasons.Add("attempt_not_completed");
        else if (attempt.SubtestCode is not ("writing" or "speaking")) reasons.Add("unsupported_subtest");

        return new
        {
            attemptId,
            canRequestReview = canRequest,
            canPurchaseExtras = true,
            availableCredits = wallet.CreditBalance,
            turnaroundOptions = new[]
            {
                new { id = "standard", label = "Standard", time = "48-72 hours", cost = 1, description = "Detailed written feedback within three business days." },
                new { id = "express", label = "Express", time = "24 hours", cost = 2, description = "Priority turnaround within 24 hours." }
            },
            eligibilityReasonCodes = reasons
        };
    }

    public async Task<object> CreateReviewRequestAsync(string userId, ReviewRequestCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("review-request", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var attempt = await GetAttemptOwnedByUserAsync(userId, request.AttemptId, cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        var cost = request.TurnaroundOption == "express" ? 2 : 1;
        var state = request.PaymentSource == "credits" && wallet.CreditBalance >= cost
            ? ReviewRequestState.Queued
            : ReviewRequestState.AwaitingPayment;

        if (attempt.SubtestCode is not ("writing" or "speaking") || attempt.State != AttemptState.Completed)
        {
            throw ApiException.Validation(
                "review_not_eligible",
                "This attempt is not eligible for expert review.",
                [new ApiFieldError("attemptId", "not_eligible", "Only completed writing and speaking attempts can be sent for review.")]);
        }

        if (!string.Equals(request.Subtest, attempt.SubtestCode, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "review_subtest_mismatch",
                "The review request subtest does not match the attempt.",
                [new ApiFieldError("subtest", "mismatch", "Use the same subtest as the selected attempt.")]);
        }

        if (string.Equals(request.PaymentSource, "credits", StringComparison.OrdinalIgnoreCase) && wallet.CreditBalance < cost)
        {
            throw ApiException.Validation(
                "insufficient_credits",
                "You do not have enough review credits for this request.",
                [new ApiFieldError("paymentSource", "insufficient_credits", "Buy more credits or choose a different payment flow.")]);
        }

        if (request.PaymentSource == "credits" && wallet.CreditBalance >= cost)
        {
            wallet.CreditBalance -= cost;
            wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        var review = new ReviewRequest
        {
            Id = $"review-{Guid.NewGuid():N}",
            AttemptId = request.AttemptId,
            SubtestCode = request.Subtest,
            State = state,
            TurnaroundOption = request.TurnaroundOption,
            FocusAreasJson = JsonSupport.Serialize(request.FocusAreas),
            LearnerNotes = request.LearnerNotes ?? string.Empty,
            PaymentSource = request.PaymentSource,
            PriceSnapshot = cost,
            CreatedAt = DateTimeOffset.UtcNow,
            EligibilitySnapshotJson = JsonSupport.Serialize(new { canRequestReview = true, availableCredits = wallet.CreditBalance })
        };

        db.ReviewRequests.Add(review);

        await RecordEventAsync(userId, "review_requested", new { reviewRequestId = review.Id, attemptId = review.AttemptId, subtest = review.SubtestCode, turnaroundOption = review.TurnaroundOption }, cancellationToken);
        LogAudit(userId, "Created", "ReviewRequest", review.Id, $"Expert review requested for {review.SubtestCode} attempt {review.AttemptId}, cost={cost} credits");
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("review-request", request.IdempotencyKey, new { reviewRequestId = review.Id }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetReviewRequestAsync(userId, review.Id, cancellationToken);
    }

    public async Task<object> GetReviewRequestAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await GetReviewRequestOwnedByUserAsync(userId, reviewRequestId, cancellationToken);
        return new
        {
            reviewRequestId = review.Id,
            attemptId = review.AttemptId,
            subtest = review.SubtestCode,
            state = ToReviewRequestState(review.State),
            turnaroundOption = review.TurnaroundOption,
            focusAreas = JsonSupport.Deserialize<List<string>>(review.FocusAreasJson, []),
            learnerNotes = review.LearnerNotes,
            paymentSource = review.PaymentSource,
            priceSnapshot = review.PriceSnapshot,
            createdAt = review.CreatedAt,
            completedAt = review.CompletedAt,
            eligibilitySnapshot = JsonSupport.Deserialize<Dictionary<string, object?>>(review.EligibilitySnapshotJson, new Dictionary<string, object?>())
        };
    }

    public async Task<object> GetBillingSummaryAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId, cancellationToken);
        return new
        {
            subscriptionId = subscription.Id,
            planId = subscription.PlanId,
            status = ToSubscriptionState(subscription.Status),
            nextRenewalAt = subscription.NextRenewalAt,
            startedAt = subscription.StartedAt,
            changedAt = subscription.ChangedAt,
            price = new { amount = subscription.PriceAmount, currency = subscription.Currency, interval = subscription.Interval },
            wallet = new { walletId = wallet.Id, creditBalance = wallet.CreditBalance, ledgerSummary = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(wallet.LedgerSummaryJson, []) },
            entitlements = new
            {
                productiveSkillReviewsEnabled = true,
                supportedReviewSubtests = new[] { "writing", "speaking" },
                invoiceDownloadsAvailable = true
            }
        };
    }

    public async Task<object> GetBillingPlansAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);
        var plans = BillingPlanCatalog();
        var currentPlan = plans.FirstOrDefault(plan => string.Equals(plan.PlanId, subscription.PlanId, StringComparison.Ordinal)) ?? plans[0];
        return new
        {
            currentPlanId = subscription.PlanId,
            items = plans.Select(plan => new
            {
                planId = plan.PlanId,
                label = plan.Label,
                tier = plan.Tier,
                description = plan.Description,
                price = new { amount = plan.Amount, currency = subscription.Currency, interval = subscription.Interval },
                reviewCredits = plan.ReviewCredits,
                mockReportsIncluded = true,
                canChangeTo = !string.Equals(plan.PlanId, subscription.PlanId, StringComparison.Ordinal),
                changeDirection = string.Equals(plan.PlanId, subscription.PlanId, StringComparison.Ordinal) ? "current" : ComparePlanTier(plan.Tier, currentPlan.Tier),
                badge = plan.Badge
            })
        };
    }

    public async Task<object> GetBillingChangePreviewAsync(string userId, string targetPlanId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var subscription = await db.Subscriptions.FirstAsync(x => x.UserId == userId, cancellationToken);
        var currentPlan = BillingPlanCatalog().FirstOrDefault(plan => string.Equals(plan.PlanId, subscription.PlanId, StringComparison.Ordinal)) ?? BillingPlanCatalog()[0];
        var targetPlan = BillingPlanCatalog().FirstOrDefault(plan => string.Equals(plan.PlanId, targetPlanId, StringComparison.Ordinal))
            ?? throw ApiException.Validation(
                "unknown_plan",
                $"Unknown billing plan '{targetPlanId}'.",
                [new ApiFieldError("targetPlanId", "unknown", "Choose a supported billing plan.")]);

        var delta = targetPlan.Amount - currentPlan.Amount;
        var direction = delta >= 0 ? "upgrade" : "downgrade";
        return new
        {
            currentPlanId = currentPlan.PlanId,
            targetPlanId = targetPlan.PlanId,
            direction,
            proratedAmount = Math.Round(Math.Abs(delta) / 2m, 2),
            effectiveAt = subscription.NextRenewalAt,
            summary = direction == "upgrade"
                ? $"Switching to {targetPlan.Label} increases your monthly plan by {Math.Abs(delta):0.00} {subscription.Currency}."
                : $"Switching to {targetPlan.Label} lowers your monthly plan by {Math.Abs(delta):0.00} {subscription.Currency}.",
            currentCreditsIncluded = currentPlan.ReviewCredits,
            targetCreditsIncluded = targetPlan.ReviewCredits
        };
    }

    public async Task<object> GetInvoicesAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var invoices = await db.Invoices.Where(x => x.UserId == userId).OrderByDescending(x => x.IssuedAt).ToListAsync(cancellationToken);
        return new
        {
            items = invoices.Select(x => new
            {
                invoiceId = x.Id,
                date = x.IssuedAt,
                amount = x.Amount,
                currency = x.Currency,
                status = x.Status,
                description = x.Description,
                downloadUrl = platformLinks.BuildApiUrl($"/v1/billing/invoices/{Uri.EscapeDataString(x.Id)}/download")
            }),
            nextCursor = (string?)null
        };
    }

    public async Task<GeneratedDownloadFile> GetInvoiceDownloadAsync(string userId, string invoiceId, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var invoice = await db.Invoices.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == invoiceId, cancellationToken)
            ?? throw ApiException.NotFound("invoice_not_found", "Invoice not found.");

        var content = string.Join(Environment.NewLine, new[]
        {
            "OET Prep Invoice",
            $"Invoice ID: {invoice.Id}",
            $"Issued At: {invoice.IssuedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"Status: {invoice.Status}",
            $"Amount: {invoice.Amount:0.00} {invoice.Currency}",
            $"Description: {invoice.Description}"
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new GeneratedDownloadFile(new MemoryStream(bytes), "text/plain", $"{invoice.Id}.txt");
    }

    public object GetReviewOptions() => new
    {
        items = new[]
        {
            new { id = "standard", label = "Standard Review", turnaround = "48-72 hours", price = 1, currency = "credit", description = "Detailed expert review with criterion-level notes." },
            new { id = "express", label = "Express Review", turnaround = "24 hours", price = 2, currency = "credit", description = "Priority expert review returned within a day." }
        }
    };

    public object GetExtras() => new
    {
        items = new[]
        {
            new { id = "credits-3", productType = "review_credits", quantity = 3, price = 29.99m, currency = "AUD", description = "Pack of 3 expert review credits." },
            new { id = "credits-5", productType = "review_credits", quantity = 5, price = 44.99m, currency = "AUD", description = "Pack of 5 expert review credits." }
        }
    };

    public async Task<object> CreateCheckoutSessionAsync(string userId, CheckoutSessionCreateRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await GetIdempotentResponseAsync("checkout-session", request.IdempotencyKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var normalizedProductType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProductType is not ("review_credits" or "plan_upgrade" or "plan_downgrade"))
        {
            throw ApiException.Validation(
                "unsupported_checkout_product",
                $"Unsupported checkout product '{request.ProductType}'.",
                [new ApiFieldError("productType", "unsupported", "Only supported learner checkout products can be purchased.")]);
        }

        if (request.Quantity <= 0)
        {
            throw ApiException.Validation(
                "invalid_checkout_quantity",
                "Checkout quantity must be greater than zero.",
                [new ApiFieldError("quantity", "invalid", "Choose a checkout quantity greater than zero.")]);
        }

        if (normalizedProductType is "plan_upgrade" or "plan_downgrade" && string.IsNullOrWhiteSpace(request.PriceId))
        {
            throw ApiException.Validation(
                "target_plan_required",
                "A target plan id is required for plan changes.",
                [new ApiFieldError("priceId", "required", "Choose the plan you want to switch to.")]);
        }

        var checkoutSessionId = $"checkout-{Guid.NewGuid():N}";
        object response = new
        {
            checkoutSessionId,
            productType = normalizedProductType,
            quantity = request.Quantity,
            targetPlanId = request.PriceId,
            checkoutUrl = platformLinks.BuildCheckoutUrl(checkoutSessionId, normalizedProductType, request.Quantity),
            state = "created"
        };
        await RecordEventAsync(userId, "subscription_changed", new { productType = normalizedProductType, quantity = request.Quantity, targetPlanId = request.PriceId }, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await SaveIdempotentResponseAsync("checkout-session", request.IdempotencyKey, response, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    private async Task<LearnerUser> EnsureUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is not null)
        {
            return user;
        }

        logger.LogInformation("Creating learner shell for new user {UserId}", userId);
        user = new LearnerUser
        {
            Id = userId,
            Role = "learner",
            DisplayName = "Learner",
            Email = platformLinks.BuildFallbackEmail(userId),
            Timezone = "UTC",
            Locale = "en-AU",
            ActiveProfessionId = "nursing",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };
        var planId = $"plan-{Guid.NewGuid():N}";
        db.Users.Add(user);
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = "nursing",
            StudyHoursPerWeek = 6,
            WeakSubtestsJson = "[]",
            DraftStateJson = "{}",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Settings.Add(new LearnerSettings { Id = Guid.NewGuid(), UserId = userId });
        db.StudyPlans.Add(new StudyPlan { Id = planId, UserId = userId, GeneratedAt = DateTimeOffset.UtcNow, Checkpoint = "Complete onboarding and goals", WeakSkillFocus = "Diagnostic pending" });
        db.ReadinessSnapshots.Add(new ReadinessSnapshot { Id = $"rs-{Guid.NewGuid():N}", UserId = userId, ComputedAt = DateTimeOffset.UtcNow, PayloadJson = JsonSupport.Serialize(new { targetDate = (string?)null, weeksRemaining = 0, overallRisk = "moderate", recommendedStudyHours = 6, weakestLink = "Diagnostic pending", subTests = Array.Empty<object>(), blockers = new[] { new { id = 1, title = "Complete a diagnostic", description = "The first readiness snapshot becomes more reliable once diagnostic evidence exists." } }, evidence = new { mocksCompleted = 0, practiceQuestions = 0, expertReviews = 0, recentTrend = "Insufficient data", lastUpdated = DateTimeOffset.UtcNow } }) });
        db.Subscriptions.Add(new Subscription { Id = $"sub-{Guid.NewGuid():N}", UserId = userId, PlanId = "starter-monthly", Status = SubscriptionStatus.Trial, NextRenewalAt = DateTimeOffset.UtcNow.AddDays(14), StartedAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow, PriceAmount = 0m });
        db.Wallets.Add(new Wallet { Id = $"wallet-{Guid.NewGuid():N}", UserId = userId, CreditBalance = 0, LastUpdatedAt = DateTimeOffset.UtcNow, LedgerSummaryJson = "[]" });
        user.CurrentPlanId = planId;
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private static object GoalDto(LearnerGoal goal) => new
    {
        goalId = goal.Id,
        userId = goal.UserId,
        professionId = goal.ProfessionId,
        targetExamDate = goal.TargetExamDate,
        overallGoal = goal.OverallGoal,
        targetScoresBySubtest = new
        {
            writing = goal.TargetWritingScore,
            speaking = goal.TargetSpeakingScore,
            reading = goal.TargetReadingScore,
            listening = goal.TargetListeningScore
        },
        previousAttemptSummary = goal.PreviousAttempts,
        weakSubtestSelfReport = JsonSupport.Deserialize<List<string>>(goal.WeakSubtestsJson, []),
        studyHoursPerWeek = goal.StudyHoursPerWeek,
        targetCountry = goal.TargetCountry,
        targetOrganization = goal.TargetOrganization,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private static object GoalSettingsDto(LearnerGoal goal) => new
    {
        professionId = goal.ProfessionId,
        targetExamDate = goal.TargetExamDate,
        overallGoal = goal.OverallGoal,
        targetScoresBySubtest = new
        {
            writing = goal.TargetWritingScore,
            speaking = goal.TargetSpeakingScore,
            reading = goal.TargetReadingScore,
            listening = goal.TargetListeningScore
        },
        previousAttempts = goal.PreviousAttempts,
        weakSubtests = JsonSupport.Deserialize<List<string>>(goal.WeakSubtestsJson, []),
        studyHoursPerWeek = goal.StudyHoursPerWeek,
        targetCountry = goal.TargetCountry,
        targetOrganization = goal.TargetOrganization,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private async Task<UploadSession> GetUploadSessionOwnedByUserAsync(string userId, string uploadSessionId, CancellationToken cancellationToken)
    {
        var upload = await db.UploadSessions.FirstOrDefaultAsync(x => x.Id == uploadSessionId, cancellationToken)
            ?? throw ApiException.NotFound("upload_session_not_found", "The requested upload session was not found.");

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private async Task<UploadSession> GetUploadSessionForCompletionAsync(
        string userId,
        string attemptId,
        UploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        UploadSession? upload = null;
        if (!string.IsNullOrWhiteSpace(request.UploadSessionId))
        {
            upload = await GetUploadSessionOwnedByUserAsync(userId, request.UploadSessionId, cancellationToken);
            if (!string.Equals(upload.AttemptId, attemptId, StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "upload_attempt_mismatch",
                    "The upload session does not belong to this speaking attempt.",
                    [new ApiFieldError("uploadSessionId", "mismatch", "Use the upload session created for this speaking attempt.")]);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.StorageKey))
        {
            upload = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId && x.StorageKey == request.StorageKey)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            upload = await db.UploadSessions
                .Where(x => x.AttemptId == attemptId)
                .OrderByDescending(x => x.ExpiresAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (upload is null)
        {
            throw ApiException.Validation(
                "upload_session_required",
                "Create and use an upload session before completing the speaking upload.",
                [new ApiFieldError("uploadSessionId", "required", "Create a speaking upload session first.")]);
        }

        await GetAttemptOwnedByUserAsync(userId, upload.AttemptId, cancellationToken);
        return upload;
    }

    private static object SettingsDto(LearnerSettings settings, LearnerGoal goal) => new
    {
        profile = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>()),
        goals = GoalSettingsDto(goal),
        notifications = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.NotificationsJson, new Dictionary<string, object?>()),
        privacy = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.PrivacyJson, new Dictionary<string, object?>()),
        accessibility = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AccessibilityJson, new Dictionary<string, object?>()),
        audio = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AudioJson, new Dictionary<string, object?>()),
        study = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>())
    };

    private static void ApplyGoalSettingsPatch(LearnerGoal goal, Dictionary<string, object?> values)
    {
        if (values.TryGetValue("professionId", out var professionId))
        {
            goal.ProfessionId = ReadString(professionId) ?? goal.ProfessionId;
        }

        if (values.TryGetValue("targetExamDate", out var targetExamDate))
        {
            goal.TargetExamDate = ReadDateOnly(targetExamDate) ?? goal.TargetExamDate;
        }

        if (values.TryGetValue("overallGoal", out var overallGoal))
        {
            goal.OverallGoal = ReadString(overallGoal) ?? goal.OverallGoal;
        }

        if (values.TryGetValue("targetScoresBySubtest", out var targetScores))
        {
            var scores = ReadObject(targetScores);
            if (scores is not null)
            {
                goal.TargetWritingScore = ReadInt(scores.GetValueOrDefault("writing")) ?? goal.TargetWritingScore;
                goal.TargetSpeakingScore = ReadInt(scores.GetValueOrDefault("speaking")) ?? goal.TargetSpeakingScore;
                goal.TargetReadingScore = ReadInt(scores.GetValueOrDefault("reading")) ?? goal.TargetReadingScore;
                goal.TargetListeningScore = ReadInt(scores.GetValueOrDefault("listening")) ?? goal.TargetListeningScore;
            }
        }

        if (values.TryGetValue("previousAttempts", out var previousAttempts))
        {
            goal.PreviousAttempts = ReadInt(previousAttempts) ?? goal.PreviousAttempts;
        }

        if (values.TryGetValue("previousAttemptSummary", out var previousAttemptSummary))
        {
            goal.PreviousAttempts = ReadInt(previousAttemptSummary) ?? goal.PreviousAttempts;
        }

        if (values.TryGetValue("weakSubtests", out var weakSubtests))
        {
            var parsed = ReadStringList(weakSubtests);
            if (parsed is not null)
            {
                goal.WeakSubtestsJson = JsonSupport.Serialize(parsed);
            }
        }

        if (values.TryGetValue("weakSubtestSelfReport", out var weakSubtestSelfReport))
        {
            var parsed = ReadStringList(weakSubtestSelfReport);
            if (parsed is not null)
            {
                goal.WeakSubtestsJson = JsonSupport.Serialize(parsed);
            }
        }

        if (values.TryGetValue("studyHoursPerWeek", out var studyHoursPerWeek))
        {
            goal.StudyHoursPerWeek = ReadInt(studyHoursPerWeek) ?? goal.StudyHoursPerWeek;
        }

        if (values.TryGetValue("targetCountry", out var targetCountry))
        {
            goal.TargetCountry = ReadString(targetCountry) ?? goal.TargetCountry;
        }

        if (values.TryGetValue("targetOrganization", out var targetOrganization))
        {
            goal.TargetOrganization = ReadString(targetOrganization) ?? goal.TargetOrganization;
        }

        if (values.TryGetValue("draftState", out var draftState))
        {
            var draftStateObject = ReadObject(draftState);
            if (draftStateObject is not null)
            {
                goal.DraftStateJson = JsonSupport.Serialize(draftStateObject);
            }
        }
    }

    private async Task<Attempt> GetAttemptOwnedByUserAsync(string userId, string attemptId, CancellationToken cancellationToken)
        => await db.Attempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, cancellationToken)
           ?? throw ApiException.NotFound("attempt_not_found", "Attempt not found.");

    private async Task<Evaluation> GetEvaluationOwnedByUserAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await db.Evaluations.FirstOrDefaultAsync(x => x.Id == evaluationId, cancellationToken)
                         ?? throw ApiException.NotFound("evaluation_not_found", "Evaluation not found.");
        await GetAttemptOwnedByUserAsync(userId, evaluation.AttemptId, cancellationToken);
        return evaluation;
    }

    private async Task<DiagnosticSession> GetDiagnosticSessionOwnedByUserAsync(string userId, string diagnosticId, CancellationToken cancellationToken)
        => await db.DiagnosticSessions.FirstOrDefaultAsync(x => x.Id == diagnosticId && x.UserId == userId, cancellationToken)
           ?? throw ApiException.NotFound("diagnostic_not_found", "Diagnostic session not found.");

    private async Task<StudyPlanItem> GetStudyPlanItemOwnedByUserAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var item = await db.StudyPlanItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken)
                   ?? throw ApiException.NotFound("study_plan_item_not_found", "Study plan item not found.");
        var plan = await db.StudyPlans.FirstOrDefaultAsync(x => x.Id == item.StudyPlanId && x.UserId == userId, cancellationToken);
        if (plan is null)
        {
            throw ApiException.NotFound("study_plan_item_not_found", "Study plan item not found.");
        }

        return item;
    }

    private async Task<MockAttempt> GetMockAttemptOwnedByUserAsync(string userId, string mockAttemptId, CancellationToken cancellationToken)
        => await db.MockAttempts.FirstOrDefaultAsync(x => x.Id == mockAttemptId && x.UserId == userId, cancellationToken)
           ?? throw ApiException.NotFound("mock_attempt_not_found", "Mock attempt not found.");

    private async Task<MockReport> GetMockReportOwnedByUserAsync(string userId, string reportId, CancellationToken cancellationToken)
    {
        var report = await db.MockReports.FirstOrDefaultAsync(x => x.Id == reportId, cancellationToken)
                     ?? throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");
        var mockAttempt = await db.MockAttempts.FirstOrDefaultAsync(x => x.Id == report.MockAttemptId && x.UserId == userId, cancellationToken);
        if (mockAttempt is null)
        {
            throw ApiException.NotFound("mock_report_not_found", "Mock report not found.");
        }

        return report;
    }

    private async Task<ReviewRequest> GetReviewRequestOwnedByUserAsync(string userId, string reviewRequestId, CancellationToken cancellationToken)
    {
        var review = await db.ReviewRequests.FirstOrDefaultAsync(x => x.Id == reviewRequestId, cancellationToken)
                     ?? throw ApiException.NotFound("review_request_not_found", "Review request not found.");
        await GetAttemptOwnedByUserAsync(userId, review.AttemptId, cancellationToken);
        return review;
    }

    private static string? ReadString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        _ => value.ToString()
    };

    private static int? ReadInt(object? value) => value switch
    {
        null => null,
        int number => number,
        long number => (int)number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var parsed) => parsed,
        _ when int.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static DateOnly? ReadDateOnly(object? value)
    {
        var text = ReadString(value);
        return DateOnly.TryParse(text, out var parsed) ? parsed : null;
    }

    private static List<string>? ReadStringList(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        }

        if (value is IEnumerable<object?> objects)
        {
            return objects.Select(ReadString).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList();
        }

        return null;
    }

    private static Dictionary<string, object?>? ReadObject(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return JsonSupport.Deserialize<Dictionary<string, object?>>(element.GetRawText(), new Dictionary<string, object?>());
        }

        return JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(value), new Dictionary<string, object?>());
    }

    private async Task<List<object>> GetTasksBySubtestAsync(string subtest, CancellationToken cancellationToken)
    {
        var items = await db.ContentItems.Where(x => x.SubtestCode == subtest && x.Status == ContentStatus.Published).OrderBy(x => x.Title).ToListAsync(cancellationToken);
        return items.Select(item => (object)new
        {
            contentId = item.Id,
            contentType = item.ContentType,
            subtest = item.SubtestCode,
            professionId = item.ProfessionId,
            title = item.Title,
            difficulty = item.Difficulty,
            estimatedDurationMinutes = item.EstimatedDurationMinutes,
            criteriaFocus = JsonSupport.Deserialize<List<string>>(item.CriteriaFocusJson, []),
            scenarioType = item.ScenarioType,
            modeSupport = JsonSupport.Deserialize<List<string>>(item.ModeSupportJson, []),
            publishedRevisionId = item.PublishedRevisionId,
            status = ToContentStatus(item.Status)
        }).ToList();
    }

    private async Task<object> CreateAttemptAsync(string userId, CreateAttemptRequest request, string subtest, CancellationToken cancellationToken)
    {
        await EnsureUserAsync(userId, cancellationToken);
        var context = request.Context ?? "practice";
        var mode = request.Mode ?? (subtest is "reading" or "listening" ? "exam" : "practice");
        var existing = await db.Attempts
            .Where(x => x.UserId == userId
                        && x.ContentId == request.ContentId
                        && x.SubtestCode == subtest
                        && x.Context == context
                        && x.State == AttemptState.InProgress)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return await GetAttemptAsync(existing.Id, cancellationToken);
        }

        var attempt = new Attempt
        {
            Id = $"{subtest[..1]}a-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = request.ContentId,
            SubtestCode = subtest,
            Context = context,
            Mode = mode,
            State = AttemptState.InProgress,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceType = request.DeviceType ?? "web",
            ParentAttemptId = request.ParentAttemptId,
            ComparisonGroupId = $"{subtest}-{request.ContentId}"
        };
        db.Attempts.Add(attempt);
        await LearnerWorkflowCoordinator.AttachAttemptToDiagnosticAsync(db, attempt, cancellationToken);
        await RecordEventAsync(userId, "task_started", new { attemptId = attempt.Id, contentId = attempt.ContentId, subtest = attempt.SubtestCode, mode = attempt.Mode, context = attempt.Context }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAttemptAsync(attempt.Id, cancellationToken);
    }

    private async Task<object> GetAttemptAsync(string attemptId, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.FirstAsync(x => x.Id == attemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());

        return new
        {
            attemptId = attempt.Id,
            userId = attempt.UserId,
            contentId = attempt.ContentId,
            subtest = attempt.SubtestCode,
            context = attempt.Context,
            mode = attempt.Mode,
            state = ToApiState(attempt.State),
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            completedAt = attempt.CompletedAt,
            elapsedSeconds = attempt.ElapsedSeconds,
            draftVersion = attempt.DraftVersion,
            parentAttemptId = attempt.ParentAttemptId,
            comparisonGroupId = attempt.ComparisonGroupId,
            deviceType = attempt.DeviceType,
            lastClientSyncAt = attempt.LastClientSyncAt,
            draftContent = attempt.DraftContent,
            scratchpad = attempt.Scratchpad,
            checklist = JsonSupport.Deserialize<Dictionary<string, bool>>(attempt.ChecklistJson, new Dictionary<string, bool>()),
            answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>()),
            audioUploadState = ToUploadState(attempt.AudioUploadState),
            transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []),
            analysis = JsonSupport.Deserialize<Dictionary<string, object?>>(attempt.AnalysisJson, new Dictionary<string, object?>()),
            content = Merge(new Dictionary<string, object?>
            {
                ["contentId"] = content.Id,
                ["title"] = content.Title,
                ["subtest"] = content.SubtestCode,
                ["professionId"] = content.ProfessionId,
                ["difficulty"] = content.Difficulty,
                ["estimatedDurationMinutes"] = content.EstimatedDurationMinutes,
                ["caseNotes"] = content.CaseNotes,
                ["scenarioType"] = content.ScenarioType,
                ["criteriaFocus"] = JsonSupport.Deserialize<List<string>>(content.CriteriaFocusJson, [])
            }, detail)
        };
    }

    private async Task<object> GetGenericTaskAsync(string contentId, string subtest, CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == contentId && x.SubtestCode == subtest, cancellationToken)
                   ?? throw ApiException.NotFound("content_not_found", $"{ToDisplaySubtest(subtest)} task not found.");
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(item.DetailJson, new Dictionary<string, object?>());
        return Merge(new Dictionary<string, object?>
        {
            ["contentId"] = item.Id,
            ["title"] = item.Title,
            ["difficulty"] = item.Difficulty,
            ["estimatedDurationMinutes"] = item.EstimatedDurationMinutes,
            ["subtest"] = item.SubtestCode,
            ["scenarioType"] = item.ScenarioType
        }, detail);
    }

    private async Task<object> UpdateAnswersAsync(string userId, string attemptId, AnswersUpdateRequest request, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        var current = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        foreach (var (key, value) in request.Answers)
        {
            current[key] = value;
        }

        attempt.AnswersJson = JsonSupport.Serialize(current);
        attempt.LastClientSyncAt = DateTimeOffset.UtcNow;
        attempt.State = AttemptState.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, answers = current, lastClientSyncAt = attempt.LastClientSyncAt };
    }

    private async Task<object> SubmitObjectiveAttemptAsync(string userId, string attemptId, string subtest, CancellationToken cancellationToken)
    {
        var attempt = await GetAttemptOwnedByUserAsync(userId, attemptId, cancellationToken);
        if (attempt.State == AttemptState.Completed)
        {
            var existing = await db.Evaluations.FirstAsync(x => x.AttemptId == attempt.Id, cancellationToken);
            return new { attemptId = attempt.Id, evaluationId = existing.Id, state = "completed" };
        }

        attempt.State = AttemptState.Completed;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.CompletedAt = DateTimeOffset.UtcNow;

        var evaluation = new Evaluation
        {
            Id = $"{subtest[..1]}e-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            AttemptId = attempt.Id,
            SubtestCode = subtest,
            State = AsyncState.Completed,
            ScoreRange = subtest == "reading" ? "67%" : "66%",
            GradeRange = "C+",
            ConfidenceBand = ConfidenceBand.High,
            StrengthsJson = JsonSupport.Serialize(new[] { "You captured the main idea correctly.", "Your answer flow remained controlled under time pressure." }),
            IssuesJson = JsonSupport.Serialize(new[] { "One exact-detail distractor still caused an error." }),
            CriterionScoresJson = JsonSupport.Serialize(new[] { new { criterionCode = "detail_capture", scoreRange = "2/3", confidenceBand = "high", explanation = "Most exact details were captured correctly." } }),
            FeedbackItemsJson = JsonSupport.Serialize(new[] { new { feedbackItemId = $"{attempt.Id}-objective-1", criterionCode = "detail_capture", type = "answer_feedback", anchor = new { questionId = "2" }, message = "Focus on exact quantities, not inferred frequency.", severity = "medium", suggestedFix = "Underline number and range language." } }),
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelExplanationSafe = "This objective result is based on answer accuracy only.",
            LearnerDisclaimer = "Practice estimate only.",
            StatusReasonCode = "completed",
            StatusMessage = "Result ready.",
            LastTransitionAt = DateTimeOffset.UtcNow
        };
        db.Evaluations.Add(evaluation);
        await RecordEventAsync(attempt.UserId, "task_submitted", new { attemptId = attempt.Id, evaluationId = evaluation.Id, subtest, contentId = attempt.ContentId }, cancellationToken);
        await LearnerWorkflowCoordinator.UpdateDiagnosticProgressAsync(db, attempt, AttemptState.Completed, cancellationToken);
        await LearnerWorkflowCoordinator.QueueStudyPlanRegenerationAsync(db, attempt.UserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new { attemptId = attempt.Id, evaluationId = evaluation.Id, state = "completed" };
    }

    private async Task<object> GetObjectiveEvaluationAsync(string userId, string evaluationId, CancellationToken cancellationToken)
    {
        var evaluation = await GetEvaluationOwnedByUserAsync(userId, evaluationId, cancellationToken);
        var attempt = await db.Attempts.FirstAsync(x => x.Id == evaluation.AttemptId, cancellationToken);
        var content = await db.ContentItems.FirstAsync(x => x.Id == attempt.ContentId, cancellationToken);
        await RecordEventAsync(userId, "evaluation_viewed", new { evaluationId = evaluation.Id, attemptId = attempt.Id, subtest = evaluation.SubtestCode }, cancellationToken);
        var detail = JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>());
        var questions = detail.TryGetValue("questions", out var questionsValue)
            ? JsonSupport.Deserialize<List<Dictionary<string, object?>>>(JsonSupport.Serialize(questionsValue), [])
            : [];
        var answers = JsonSupport.Deserialize<Dictionary<string, string?>>(attempt.AnswersJson, new Dictionary<string, string?>());
        var itemReview = questions
            .Select(question => ObjectiveItemReviewDto(content.SubtestCode, question, answers))
            .ToList();
        var errorClusters = ObjectiveErrorClusters(content.SubtestCode, itemReview);
        return new
        {
            evaluationId = evaluation.Id,
            attemptId = attempt.Id,
            taskId = content.Id,
            title = content.Title,
            subtest = content.SubtestCode,
            score = evaluation.ScoreRange,
            gradeRange = evaluation.GradeRange,
            state = ToAsyncState(evaluation.State),
            strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []),
            issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []),
            feedbackItems = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.FeedbackItemsJson, []),
            itemReview,
            errorClusters,
            recommendedNextDrill = ObjectiveRecommendedDrill(content.SubtestCode, errorClusters),
            transcriptAccess = content.SubtestCode == "listening"
                ? ObjectiveTranscriptAccess(itemReview)
                : null,
            generatedAt = evaluation.GeneratedAt
        };
    }

    private async Task<StudyPlan> GetActiveStudyPlanEntityAsync(string userId, CancellationToken cancellationToken)
        => await db.StudyPlans.Where(x => x.UserId == userId).OrderByDescending(x => x.GeneratedAt).FirstAsync(cancellationToken);

    private async Task QueueJobAsync(JobType type, string? attemptId = null, string? resourceId = null, CancellationToken cancellationToken = default)
    {
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"job-{Guid.NewGuid():N}",
            Type = type,
            State = AsyncState.Queued,
            AttemptId = attemptId,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = DateTimeOffset.UtcNow.AddSeconds(1),
            LastTransitionAt = DateTimeOffset.UtcNow,
            StatusReasonCode = "queued",
            StatusMessage = "Queued",
            Retryable = true,
            RetryAfterMs = 2000
        });
        await Task.CompletedTask;
    }

    private async Task RecordEventAsync(string userId, string eventName, object payload, CancellationToken cancellationToken)
    {
        db.AnalyticsEvents.Add(new AnalyticsEventRecord
        {
            Id = $"evt-{Guid.NewGuid():N}",
            UserId = userId,
            EventName = eventName,
            PayloadJson = JsonSupport.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow
        });
        await Task.CompletedTask;
    }

    private void LogAudit(string userId, string action, string resourceType, string? resourceId, string? details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = userId,
            ActorName = $"learner:{userId}",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details
        });
    }

    private async Task<Dictionary<string, object?>?> GetIdempotentResponseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        return record is null
            ? null
            : JsonSupport.Deserialize<Dictionary<string, object?>>(record.ResponseJson, new Dictionary<string, object?>());
    }

    private async Task SaveIdempotentResponseAsync(string scope, string key, object response, CancellationToken cancellationToken)
    {
        var exists = await db.IdempotencyRecords.AnyAsync(x => x.Scope == scope && x.Key == key, cancellationToken);
        if (exists)
        {
            return;
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = scope,
            Key = key,
            ResponseJson = JsonSupport.Serialize(response),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static object StudyPlanItemDto(StudyPlanItem item) => new
    {
        itemId = item.Id,
        title = item.Title,
        subtest = item.SubtestCode,
        durationMinutes = item.DurationMinutes,
        rationale = item.Rationale,
        dueDate = item.DueDate,
        status = ToStudyPlanItemState(item.Status),
        section = item.Section,
        contentId = item.ContentId,
        itemType = item.ItemType
    };

    private static string MergeJsonSection(string currentJson, Dictionary<string, object?> values)
    {
        var current = JsonSupport.Deserialize<Dictionary<string, object?>>(currentJson, new Dictionary<string, object?>());
        foreach (var (key, value) in values)
        {
            current[key] = value;
        }

        return JsonSupport.Serialize(current);
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> baseValues, Dictionary<string, object?> extra)
    {
        foreach (var (key, value) in extra)
        {
            baseValues[key] = value;
        }

        return baseValues;
    }

    private static string ToApiState(AttemptState state) => state switch
    {
        AttemptState.NotStarted => "not_started",
        AttemptState.InProgress => "in_progress",
        AttemptState.Paused => "paused",
        AttemptState.Submitted => "submitted",
        AttemptState.Evaluating => "evaluating",
        AttemptState.Completed => "completed",
        AttemptState.Failed => "failed",
        AttemptState.Abandoned => "abandoned",
        _ => "unknown"
    };

    private static string ToDisplaySubtest(string code) => code.ToLowerInvariant() switch
    {
        "writing" => "Writing",
        "speaking" => "Speaking",
        "reading" => "Reading",
        "listening" => "Listening",
        _ => code
    };

    private sealed record BillingPlanDefinition(
        string PlanId,
        string Label,
        string Tier,
        string Description,
        decimal Amount,
        int ReviewCredits,
        string Badge);

    private static IReadOnlyList<BillingPlanDefinition> BillingPlanCatalog() =>
    [
        new(
            "starter-monthly",
            "Starter Monthly",
            "starter",
            "Core OET practice with AI evaluation and learner analytics.",
            0m,
            0,
            "Current default"),
        new(
            "premium-monthly",
            "Premium Monthly",
            "premium",
            "Adds productive-skill review capacity and richer mock support for active preparation.",
            49.99m,
            3,
            "Most popular"),
        new(
            "intensive-monthly",
            "Intensive Monthly",
            "intensive",
            "Higher review capacity for repeated writing and speaking feedback before the exam window.",
            79.99m,
            6,
            "Fast-track")
    ];

    private static int BillingPlanTierRank(string tier) => tier.ToLowerInvariant() switch
    {
        "starter" => 0,
        "premium" => 1,
        "intensive" => 2,
        _ => 0
    };

    private static string ComparePlanTier(string targetTier, string currentTier)
    {
        var delta = BillingPlanTierRank(targetTier) - BillingPlanTierRank(currentTier);
        return delta switch
        {
            > 0 => "upgrade",
            < 0 => "downgrade",
            _ => "current"
        };
    }

    private static string NormalizeMockReviewSelection(string mockType, string? subType, bool includeReview, string? reviewSelection)
    {
        var normalizedMockType = (mockType ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedSubType = (subType ?? string.Empty).Trim().ToLowerInvariant();
        var requestedSelection = (reviewSelection ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizedMockType == "full")
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "none",
                "writing",
                "speaking",
                "writing_and_speaking"
            };

            if (allowed.Contains(requestedSelection))
            {
                return requestedSelection;
            }

            return includeReview ? "writing_and_speaking" : "none";
        }

        var productiveSubtest = normalizedSubType is "writing" or "speaking";
        if (!productiveSubtest)
        {
            return "none";
        }

        return requestedSelection == "current_subtest"
            ? "current_subtest"
            : includeReview ? "current_subtest" : "none";
    }

    private static object[] MockAttemptSections(string attemptId, object configSource)
    {
        var config = JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(configSource), new Dictionary<string, object?>());
        var mockType = ReadString(config.GetValueOrDefault("mockType")) ?? "full";
        var subType = ReadString(config.GetValueOrDefault("subType"));
        var reviewSelection = ReadString(config.GetValueOrDefault("reviewSelection")) ?? "none";

        IEnumerable<string> subtests = string.Equals(mockType, "full", StringComparison.OrdinalIgnoreCase)
            ? ["reading", "listening", "writing", "speaking"]
            : string.IsNullOrWhiteSpace(subType) ? ["reading"] : [subType];

        return subtests.Select(subtest => new
        {
            id = subtest,
            title = $"{ToDisplaySubtest(subtest)} section",
            state = "ready",
            reviewAvailable = subtest is "writing" or "speaking",
            reviewSelected = reviewSelection == "writing_and_speaking"
                || (reviewSelection == "writing" && subtest == "writing")
                || (reviewSelection == "speaking" && subtest == "speaking")
                || (reviewSelection == "current_subtest" && string.Equals(subtest, subType, StringComparison.OrdinalIgnoreCase)),
            launchRoute = $"/app/mocks/player/{attemptId}?section={Uri.EscapeDataString(subtest)}"
        }).ToArray<object>();
    }

    private static object BuildListeningDrill(string drillId)
    {
        var normalizedId = (drillId ?? string.Empty).Trim().ToLowerInvariant();
        var detail = normalizedId switch
        {
            "listening-drill-distractor_confusion" => new
            {
                drillId = normalizedId,
                title = "Distractor Control Drill",
                focusLabel = "Speaker intent and change-of-plan control",
                description = "Use short consultation clips to separate what was suggested first from what was finally agreed.",
                errorType = "distractor_confusion",
                estimatedMinutes = 12,
                highlights = new[]
                {
                    "Track corrected instructions instead of the first option you hear.",
                    "Notice when a clinician rules out a medication or follow-up plan.",
                    "Review transcript evidence only after you commit to an answer."
                }
            },
            "listening-drill-numbers_and_frequencies" => new
            {
                drillId = normalizedId,
                title = "Numbers and Frequencies Drill",
                focusLabel = "Medication, dosage, and appointment precision",
                description = "Practise capturing exact numbers, timings, and dosage language in fast clinical audio.",
                errorType = "numbers_and_frequencies",
                estimatedMinutes = 10,
                highlights = new[]
                {
                    "Distinguish similar-sounding numbers before replaying.",
                    "Lock onto frequency phrases such as once daily and every second day.",
                    "Use replay snippets to verify quantities, not whole conversations."
                }
            },
            _ => new
            {
                drillId = string.IsNullOrWhiteSpace(normalizedId) ? "listening-drill-detail_capture" : normalizedId,
                title = "Exact Detail Capture Drill",
                focusLabel = "Referral detail and key-clue accuracy",
                description = "Rebuild listening accuracy by isolating the exact clinical detail that changed the answer.",
                errorType = "detail_capture",
                estimatedMinutes = 11,
                highlights = new[]
                {
                    "Identify which detail actually answers the question.",
                    "Separate symptoms, plans, and history without blending them.",
                    "Review the transcript clue that justified the correct answer."
                }
            }
        };

        return new
        {
            detail.drillId,
            detail.title,
            detail.focusLabel,
            detail.description,
            detail.errorType,
            detail.estimatedMinutes,
            detail.highlights,
            launchRoute = $"/app/listening/player/lt-001?drill={Uri.EscapeDataString(detail.drillId)}",
            reviewRoute = $"/app/listening/review/lt-001?drill={Uri.EscapeDataString(detail.drillId)}"
        };
    }

    private static int DiagnosticSubtestOrder(string? code) => code?.ToLowerInvariant() switch
    {
        "writing" => 0,
        "speaking" => 1,
        "reading" => 2,
        "listening" => 3,
        _ => 99
    };

    private static object ObjectiveItemReviewDto(string subtest, Dictionary<string, object?> question, Dictionary<string, string?> answers)
    {
        var questionId = question.GetValueOrDefault("id")?.ToString() ?? string.Empty;
        var learnerAnswer = answers.GetValueOrDefault(questionId);
        var correctAnswer = question.GetValueOrDefault("correctAnswer")?.ToString();
        var isCorrect = MatchesObjectiveAnswer(learnerAnswer, correctAnswer);
        var transcriptAllowed = question.TryGetValue("allowTranscriptReveal", out var allowTranscriptRevealValue) && ReadBool(allowTranscriptRevealValue) == true;
        return new
        {
            questionId,
            number = ReadInt(question.GetValueOrDefault("number")) ?? 0,
            prompt = question.GetValueOrDefault("text")?.ToString(),
            type = question.GetValueOrDefault("type")?.ToString(),
            learnerAnswer,
            correctAnswer,
            isCorrect,
            explanation = question.GetValueOrDefault("explanation")?.ToString(),
            errorType = isCorrect ? (string?)null : ObjectiveErrorType(subtest, question),
            options = ReadStringList(question.GetValueOrDefault("options")) ?? [],
            transcript = subtest == "listening" && transcriptAllowed
                ? new
                {
                    allowed = true,
                    excerpt = question.GetValueOrDefault("transcriptExcerpt")?.ToString(),
                    distractorExplanation = question.GetValueOrDefault("distractorExplanation")?.ToString()
                }
                : null
        };
    }

    private static IEnumerable<object> ObjectiveErrorClusters(string subtest, IEnumerable<object> itemReview)
    {
        var clusterSeed = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .Where(item => item.GetValueOrDefault("isCorrect")?.ToString() == bool.FalseString || string.Equals(item.GetValueOrDefault("isCorrect")?.ToString(), "false", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.GetValueOrDefault("errorType")?.ToString() ?? "accuracy")
            .Select(group => new
            {
                errorType = group.Key,
                label = ObjectiveErrorTypeLabel(group.Key),
                count = group.Count(),
                subtest,
                affectedQuestionIds = group.Select(item => item.GetValueOrDefault("questionId")?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))
            });

        return clusterSeed.Any()
            ? clusterSeed
            : new object[]
            {
                new
                {
                    errorType = subtest == "listening" ? "distractor_confusion" : "detail_capture",
                    label = ObjectiveErrorTypeLabel(subtest == "listening" ? "distractor_confusion" : "detail_capture"),
                    count = 0,
                    subtest,
                    affectedQuestionIds = Array.Empty<string>()
                }
            };
    }

    private static object ObjectiveRecommendedDrill(string subtest, IEnumerable<object> errorClusters)
    {
        var firstCluster = errorClusters
            .Select(cluster => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(cluster), new Dictionary<string, object?>()))
            .FirstOrDefault();
        var errorType = firstCluster?.GetValueOrDefault("errorType")?.ToString() ?? (subtest == "listening" ? "distractor_confusion" : "detail_capture");
        var listeningDrillId = $"listening-drill-{errorType}";
        return new
        {
            id = subtest == "listening" ? listeningDrillId : $"{subtest}-drill-{errorType}",
            title = subtest == "listening" ? "Listening distractor drill" : "Reading exact-detail drill",
            rationale = $"Focus next on {ObjectiveErrorTypeLabel(errorType).ToLowerInvariant()} to strengthen your {ToDisplaySubtest(subtest)} accuracy.",
            route = subtest == "listening"
                ? $"/app/listening/drills/{Uri.EscapeDataString(listeningDrillId)}"
                : "/app/reading/task/rt-001"
        };
    }

    private static object ObjectiveTranscriptAccess(IEnumerable<object> itemReview)
    {
        var items = itemReview
            .Select(item => JsonSupport.Deserialize<Dictionary<string, object?>>(JsonSupport.Serialize(item), new Dictionary<string, object?>()))
            .ToList();
        var allowedQuestionIds = items
            .Where(item =>
            {
                var transcript = ReadObject(item.GetValueOrDefault("transcript"));
                return transcript?.GetValueOrDefault("allowed")?.ToString() == bool.TrueString || string.Equals(transcript?.GetValueOrDefault("allowed")?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            })
            .Select(item => item.GetValueOrDefault("questionId")?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        return new
        {
            policy = "per_item_post_attempt",
            state = allowedQuestionIds.Count == 0 ? "restricted" : "partial",
            allowedQuestionIds,
            reason = "Transcript snippets are revealed only on items that explicitly allow post-attempt transcript support."
        };
    }

    private static bool MatchesObjectiveAnswer(string? learnerAnswer, string? correctAnswer)
        => string.Equals(NormalizeObjectiveAnswer(learnerAnswer), NormalizeObjectiveAnswer(correctAnswer), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectiveAnswer(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

    private static string ObjectiveErrorType(string subtest, Dictionary<string, object?> question)
    {
        var type = question.GetValueOrDefault("type")?.ToString();
        if (subtest == "listening" && !string.IsNullOrWhiteSpace(question.GetValueOrDefault("distractorExplanation")?.ToString()))
        {
            return "distractor_confusion";
        }

        return (subtest, type) switch
        {
            ("reading", "mcq") => "named_concept_miss",
            ("reading", _) => "detail_capture",
            ("listening", _) => "detail_capture",
            _ => "accuracy"
        };
    }

    private static string ObjectiveErrorTypeLabel(string errorType) => errorType switch
    {
        "named_concept_miss" => "Named concept recognition",
        "distractor_confusion" => "Distractor control",
        "detail_capture" => "Exact detail capture",
        _ => "Accuracy"
    };

    private static string CriterionLabelFromCode(string? code) => code switch
    {
        "purpose" => "Purpose",
        "content" => "Content",
        "conciseness" => "Conciseness & Clarity",
        "genre" => "Genre & Style",
        "organization" => "Organisation & Layout",
        "language" => "Language",
        "intelligibility" => "Intelligibility",
        "fluency" => "Fluency",
        "appropriateness" => "Appropriateness of Language",
        "grammar_expression" => "Resources of Grammar and Expression",
        _ => code ?? "Criterion"
    };

    private static int ParseCriterionScore(string? scoreRange)
    {
        if (string.IsNullOrWhiteSpace(scoreRange)) return 0;
        var digits = new string(scoreRange.TakeWhile(ch => char.IsDigit(ch) || ch == '-').ToArray());
        if (digits.Contains('-'))
        {
            var parts = digits.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
            {
                return (a + b) / 2;
            }
        }

        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string ScoreRangeToGrade(string? scoreRange)
    {
        var score = ParseCriterionScore(scoreRange);
        return score switch
        {
            >= 5 => "B+",
            4 => "B",
            3 => "C+",
            2 => "C",
            _ => "D"
        };
    }

    private static string ToAsyncState(AsyncState state) => state switch
    {
        AsyncState.Idle => "idle",
        AsyncState.Queued => "queued",
        AsyncState.Processing => "processing",
        AsyncState.Completed => "completed",
        AsyncState.Failed => "failed",
        _ => "unknown"
    };

    private static string ToStudyPlanItemState(StudyPlanItemStatus status) => status switch
    {
        StudyPlanItemStatus.NotStarted => "not_started",
        StudyPlanItemStatus.InProgress => "in_progress",
        StudyPlanItemStatus.Completed => "completed",
        StudyPlanItemStatus.Skipped => "skipped",
        StudyPlanItemStatus.Rescheduled => "rescheduled",
        _ => "unknown"
    };

    private static string ToReviewRequestState(ReviewRequestState status) => status switch
    {
        ReviewRequestState.Draft => "draft",
        ReviewRequestState.Submitted => "submitted",
        ReviewRequestState.AwaitingPayment => "awaiting_payment",
        ReviewRequestState.Queued => "queued",
        ReviewRequestState.InReview => "in_review",
        ReviewRequestState.Completed => "completed",
        ReviewRequestState.Failed => "failed",
        ReviewRequestState.Cancelled => "cancelled",
        _ => "unknown"
    };

    private static string ToSubscriptionState(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial => "trial",
        SubscriptionStatus.Active => "active",
        SubscriptionStatus.PastDue => "past_due",
        SubscriptionStatus.Cancelled => "cancelled",
        _ => "unknown"
    };

    private static string ToUploadState(UploadState status) => status switch
    {
        UploadState.Pending => "pending",
        UploadState.InProgress => "in_progress",
        UploadState.Uploaded => "uploaded",
        UploadState.Failed => "failed",
        _ => "unknown"
    };

    private static string ToContentStatus(ContentStatus status) => status switch
    {
        ContentStatus.Draft => "draft",
        ContentStatus.Published => "published",
        ContentStatus.Archived => "archived",
        _ => "unknown"
    };

    private static bool? ReadBool(object? value) => value switch
    {
        null => null,
        bool boolean => boolean,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var parsed) => parsed,
        _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
        _ => null
    };

    private static string AttemptFeedbackRoute(string subtest, string evaluationId) => subtest switch
    {
        "writing" => $"/app/writing/result/{evaluationId}",
        "speaking" => $"/app/speaking/result/{evaluationId}",
        "reading" => $"/app/reading/task/{evaluationId}",
        "listening" => $"/app/listening/task/{evaluationId}",
        _ => $"/app/history/{evaluationId}"
    };
}
