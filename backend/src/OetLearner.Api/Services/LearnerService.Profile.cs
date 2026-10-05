using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Professions;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public partial class LearnerService
{

    public async Task<object> GetMeAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);
        return BuildMeDto(profile.User, profile.Goal, freeze);
    }

    public async Task<object> UpdateAvatarAsync(string userId, UpdateAvatarRequest request, CancellationToken cancellationToken)
    {
        var avatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        if (avatarUrl is not null)
        {
            if (avatarUrl.Length > 512)
            {
                throw ApiException.Validation(
                    "avatar_url_too_long",
                    "Avatar URL must be 512 characters or fewer.",
                    [new ApiFieldError("avatarUrl", "too_long", "Avatar URL must be 512 characters or fewer.")]);
            }

            // Only our own media route is accepted — this field is later rendered directly,
            // so anything that could resolve to an absolute/external location (protocol-relative,
            // http(s), data:, javascript:, etc.) is rejected to prevent an open redirect or
            // third-party content injection.
            // The prefix check alone still admits traversal (".../v1/media/../../x"),
            // which would resolve away from the media route once the browser
            // normalises it. Same-origin and self-scoped, so not exploitable across
            // users, but there is no legitimate avatar path containing "..".
            if (!avatarUrl.StartsWith("/v1/media/", StringComparison.Ordinal)
                || avatarUrl.Contains("..", StringComparison.Ordinal))
            {
                throw ApiException.Validation(
                    "avatar_url_invalid",
                    "Avatar URL must be a relative /v1/media/ path.",
                    [new ApiFieldError("avatarUrl", "invalid", "Avatar URL must be a relative /v1/media/ path.")]);
            }
        }

        var user = await db.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        user.AvatarUrl = avatarUrl;
        await db.SaveChangesAsync(cancellationToken);

        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);
        return BuildMeDto(profile.User, profile.Goal, freeze);
    }

    public async Task<object> GetBootstrapAsync(string userId, CancellationToken cancellationToken)
    {
        var profile = await EnsureLearnerProfileStateAsync(userId, cancellationToken);
        var onboarding = BuildOnboardingStateDto(profile.User, examDateRequired: !profile.Goal.TargetExamDateSetByUser);
        var goals = GoalDto(profile.Goal);
        var readiness = await GetReadinessForLoadedProfileAsync(profile, cancellationToken);
        var freeze = await GetFreezeStatusForLoadedUserAsync(profile.User, cancellationToken);

        return new
        {
            user = BuildMeDto(profile.User, profile.Goal, freeze),
            onboarding,
            goals,
            readiness,
            freeze,
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
                dashboard = "/dashboard",
                studyPlan = "/study-plan",
                goals = "/goals"
            },
            lastUpdatedAt = profile.User.LastActiveAt
        };
    }

    private static object BuildMeDto(LearnerUser user, LearnerGoal goal, object freeze) => new
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
        avatarUrl = user.AvatarUrl,
        freeze,
        goals = new
        {
            examFamilyCode = goal.ExamFamilyCode,
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

    // Static reference lists embedded in every bootstrap. Read-through cached for 5 minutes
    // with the ProfessionCatalogService pattern (IMemoryCache + Invalidate()): the rows
    // change only on admin taxonomy CRUD, which calls ProfessionCatalogService.Invalidate().
    // memoryCache is null in unit tests that construct LearnerService without it.
    public async Task<IEnumerable<object>> GetProfessionsAsync(CancellationToken cancellationToken)
    {
        if (memoryCache is not null
            && memoryCache.TryGetValue(LearnerReferenceDataCache.ProfessionsKey, out IReadOnlyList<object>? cached)
            && cached is not null)
        {
            return cached;
        }

        var rows = await db.Professions
            .OrderBy(x => x.SortOrder)
            .Select(x => (object)new { professionId = x.Id, code = x.Code, label = x.Label, status = x.Status, sortOrder = x.SortOrder })
            .ToListAsync(cancellationToken);
        memoryCache?.Set(LearnerReferenceDataCache.ProfessionsKey, (IReadOnlyList<object>)rows, LearnerReferenceDataCache.Ttl);
        return rows;
    }

    public async Task<IEnumerable<object>> GetSubtestsAsync(CancellationToken cancellationToken)
    {
        if (memoryCache is not null
            && memoryCache.TryGetValue(LearnerReferenceDataCache.SubtestsKey, out IReadOnlyList<object>? cached)
            && cached is not null)
        {
            return cached;
        }

        var rows = await db.Subtests
            .OrderBy(x => x.Label)
            .Select(x => (object)new { subtestId = x.Id, code = x.Code, label = x.Label, supportsProfessionSpecificContent = x.SupportsProfessionSpecificContent })
            .ToListAsync(cancellationToken);
        memoryCache?.Set(LearnerReferenceDataCache.SubtestsKey, (IReadOnlyList<object>)rows, LearnerReferenceDataCache.Ttl);
        return rows;
    }

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
        // Any row, not SingleOrDefault: learners created before the per-learner creation
        // lock can hold duplicate Goal rows (parallel first visits), which made this 500.
        var examDateSetByUser = await db.Goals.AsNoTracking()
            .AnyAsync(g => g.UserId == userId && g.TargetExamDateSetByUser, cancellationToken);
        return BuildOnboardingStateDto(user, examDateRequired: !examDateSetByUser);
    }

    private static object BuildOnboardingStateDto(LearnerUser user, bool examDateRequired) => new
    {
        completed = user.OnboardingCompleted,
        currentStep = user.OnboardingCurrentStep,
        stepCount = user.OnboardingStepCount,
        canSkip = false,
        startedAt = user.OnboardingStartedAt,
        completedAt = user.OnboardingCompletedAt,
        checkpoint = user.OnboardingCompleted ? "goals" : "welcome",
        resumeRoute = user.OnboardingCompleted ? "/dashboard" : "/onboarding",
        examDateRequired
    };

    public async Task<object> StartOnboardingAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
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
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        user.OnboardingCompleted = true;
        user.OnboardingCurrentStep = user.OnboardingStepCount;
        user.OnboardingCompletedAt = DateTimeOffset.UtcNow;
        user.LastActiveAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "onboarding_completed", new { userId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOnboardingStateAsync(userId, cancellationToken);
    }

    public async Task<object> GetTourStateAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await db.LearnerOnboardingTours.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return TourStateDto(row);
    }

    public async Task<object> MarkTourAsync(string userId, MarkTourRequest request, CancellationToken cancellationToken)
    {
        var tourId = (request.TourId ?? string.Empty).Trim().ToLowerInvariant();
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (tourId.Length == 0)
            throw ApiException.Validation("invalid_tour", "A tour id is required.", [new ApiFieldError("tourId", "required", "Tour id is required.")]);
        if (status is not ("completed" or "skipped" or "dismissed"))
            throw ApiException.Validation("invalid_tour_status", "Status must be completed, skipped, or dismissed.", [new ApiFieldError("status", "invalid", "Use completed | skipped | dismissed.")]);

        var now = DateTimeOffset.UtcNow;
        var row = await db.LearnerOnboardingTours.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new LearnerOnboardingTour
            {
                UserId = userId,
                Role = NormalizeTourRole(request.Role),
                OnboardingVersion = OnboardingTourVersion,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.LearnerOnboardingTours.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(request.Role)) row.Role = NormalizeTourRole(request.Role);

        if (status == "dismissed")
            row.DismissedTipsJson = AddToJsonSet(row.DismissedTipsJson, tourId);
        else if (status == "skipped")
            row.SkippedToursJson = AddToJsonSet(row.SkippedToursJson, tourId);
        else
            ApplyTourCompletion(row, tourId);

        row.LastSeenTourVersion = OnboardingTourVersion;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return TourStateDto(row);
    }

    private static void ApplyTourCompletion(LearnerOnboardingTour row, string tourId)
    {
        switch (tourId)
        {
            case "intro": row.CompletedIntro = true; break;
            case "dashboard":
            case "learner-dashboard": row.CompletedDashboardTour = true; break;
            case "listening": row.CompletedListeningTour = true; break;
            case "reading": row.CompletedReadingTour = true; break;
            case "writing": row.CompletedWritingTour = true; break;
            case "speaking": row.CompletedSpeakingTour = true; break;
            case "admin": row.CompletedAdminTour = true; break;
            case "expert":
            case "tutor": row.CompletedExpertTour = true; break;
            // Unknown ids: the closed column set stays stable; the client also caches
            // completion locally, so an unmapped id is simply not mirrored to a column.
        }
    }

    private static string AddToJsonSet(string json, string value)
    {
        var list = JsonSupport.Deserialize<List<string>>(json, []) ?? [];
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
        return JsonSupport.Serialize(list);
    }

    private static string NormalizeTourRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (r == "tutor") return "expert";
        return r is "expert" or "admin" ? r : "learner";
    }

    private static string? NormalizeExamMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return m.Length == 0 ? null : m;
    }

    private static string? NormalizeConfidence(string? level)
    {
        var l = (level ?? string.Empty).Trim().ToLowerInvariant();
        return l.Length == 0 ? null : l;
    }

    private static object TourStateDto(LearnerOnboardingTour? row) => new
    {
        onboardingVersion = row?.OnboardingVersion ?? OnboardingTourVersion,
        role = row?.Role ?? "learner",
        lastSeenTourVersion = row?.LastSeenTourVersion ?? 0,
        completed = new
        {
            intro = row?.CompletedIntro ?? false,
            dashboard = row?.CompletedDashboardTour ?? false,
            listening = row?.CompletedListeningTour ?? false,
            reading = row?.CompletedReadingTour ?? false,
            writing = row?.CompletedWritingTour ?? false,
            speaking = row?.CompletedSpeakingTour ?? false,
            admin = row?.CompletedAdminTour ?? false,
            expert = row?.CompletedExpertTour ?? false,
        },
        skippedTours = JsonSupport.Deserialize<List<string>>(row?.SkippedToursJson ?? "[]", []),
        dismissedTips = JsonSupport.Deserialize<List<string>>(row?.DismissedTipsJson ?? "[]", []),
    };

    public async Task<object> GetGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return GoalDto(goal);
    }

    public async Task<object> PatchGoalsAsync(string userId, PatchGoalsRequest request, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        var examFamilyCode = NormalizeExamFamilyCode(request.ExamFamilyCode ?? goal.ExamFamilyCode);
        var examFamilyExists = await db.ExamFamilies.AsNoTracking()
            .AnyAsync(x => x.IsActive && x.Code == examFamilyCode, cancellationToken);
        if (!examFamilyExists)
        {
            throw ApiException.Validation(
                "invalid_exam_family",
                $"Exam family '{examFamilyCode}' is not supported.",
                [new ApiFieldError("examFamilyCode", "unsupported", "Choose a supported exam family.")]);
        }

        ValidateScoreRange(request.TargetWritingScore, nameof(request.TargetWritingScore), examFamilyCode);
        ValidateScoreRange(request.TargetSpeakingScore, nameof(request.TargetSpeakingScore), examFamilyCode);
        ValidateScoreRange(request.TargetReadingScore, nameof(request.TargetReadingScore), examFamilyCode);
        ValidateScoreRange(request.TargetListeningScore, nameof(request.TargetListeningScore), examFamilyCode);
        if (request.StudyHoursPerWeek.HasValue && (request.StudyHoursPerWeek.Value < 0 || request.StudyHoursPerWeek.Value > 168))
            throw ApiException.Validation("invalid_study_hours", "Study hours per week must be between 0 and 168.", [new ApiFieldError("studyHoursPerWeek", "out_of_range", "Must be 0–168.")]);
        if (request.PreviousAttempts.HasValue && request.PreviousAttempts.Value < 0)
            throw ApiException.Validation("invalid_previous_attempts", "Previous attempts cannot be negative.", [new ApiFieldError("previousAttempts", "out_of_range", "Must be 0 or greater.")]);

        goal.ExamFamilyCode = examFamilyCode;
        if (!string.IsNullOrWhiteSpace(request.ProfessionId)) goal.ProfessionId = request.ProfessionId;
        if (request.TargetExamDate.HasValue)
        {
            goal.TargetExamDate = request.TargetExamDate;
            goal.TargetExamDateSetByUser = true;
        }
        if (request.OverallGoal is not null) goal.OverallGoal = request.OverallGoal;
        if (request.TargetWritingScore.HasValue) goal.TargetWritingScore = request.TargetWritingScore;
        if (request.TargetSpeakingScore.HasValue) goal.TargetSpeakingScore = request.TargetSpeakingScore;
        if (request.TargetReadingScore.HasValue) goal.TargetReadingScore = request.TargetReadingScore;
        if (request.TargetListeningScore.HasValue) goal.TargetListeningScore = request.TargetListeningScore;
        if (request.PreviousAttempts.HasValue) goal.PreviousAttempts = request.PreviousAttempts.Value;
        if (request.WeakSubtests is not null) goal.WeakSubtestsJson = JsonSupport.Serialize(request.WeakSubtests);
        if (request.StudyHoursPerWeek.HasValue) goal.StudyHoursPerWeek = request.StudyHoursPerWeek.Value;
        if (request.TargetCountry is not null) goal.TargetCountry = TargetCountryOptions.Canonicalize(request.TargetCountry);
        if (request.TargetOrganization is not null) goal.TargetOrganization = request.TargetOrganization;
        if (request.DraftState is not null) goal.DraftStateJson = JsonSupport.Serialize(request.DraftState);
        if (request.TargetExamMode is not null) goal.TargetExamMode = NormalizeExamMode(request.TargetExamMode);
        if (request.ConfidenceLevel is not null) goal.ConfidenceLevel = NormalizeConfidence(request.ConfidenceLevel);

        goal.UpdatedAt = DateTimeOffset.UtcNow;
        await RecordEventAsync(userId, "goals_saved", new { userId, professionId = goal.ProfessionId, targetExamDate = goal.TargetExamDate }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return GoalDto(goal);
    }

    private static void ValidateScoreRange(int? score, string fieldName, string examFamilyCode)
    {
        if (!score.HasValue)
        {
            return;
        }

        var (minimum, maximum, label) = examFamilyCode switch
        {
            "ielts" => (0, 9, "0-9"),
            "pte" => (10, 90, "10-90"),
            _ => (0, 500, "0-500")
        };

        if (score.Value < minimum || score.Value > maximum)
        {
            throw ApiException.Validation(
                "invalid_score_range",
                $"Target score must be between {minimum} and {maximum} for {label} scoring.",
                [new ApiFieldError(fieldName, "out_of_range", $"Must be {minimum}–{maximum} for {label} scoring.")]);
        }
    }

    public async Task<object> SubmitGoalsAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);

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
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        await EnsureUserAsync(userId, cancellationToken);
        var settings = await db.Settings.FirstAsync(x => x.UserId == userId, cancellationToken);
        var goal = await db.Goals.FirstAsync(x => x.UserId == userId, cancellationToken);
        return SettingsDto(settings, goal);
    }

    public async Task<object> GetSettingsSectionAsync(string userId, string section, CancellationToken cancellationToken)
    {
        await EnsureLearnerProfileAsync(userId, cancellationToken);
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
        studyValues["examFamilyCode"] = goal.ExamFamilyCode;

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
        await EnsureLearnerProfileAsync(userId, cancellationToken);
        var user = await EnsureUserAsync(userId, cancellationToken);
        await EnsureLearnerMutationAllowedAsync(userId, cancellationToken);
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
                var newEmail = ReadString(email);
                if (!string.IsNullOrWhiteSpace(newEmail) && !string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    // H1 (security): changing the sign-in/billing email is a sensitive action —
                    // require re-proving the account password, mirroring AuthService.DeleteAccountAsync,
                    // so a hijacked session token alone can't redirect billing/receipt email elsewhere.
                    // Other profile fields (name, phone, profession, etc.) stay password-free.
                    if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                    {
                        throw ApiException.Validation(
                            "current_password_required",
                            "Enter your current password to confirm this email change.",
                            [new ApiFieldError("currentPassword", "current_password_required", "Enter your current password to confirm this email change.")]);
                    }

                    if (passwordHasher is null)
                    {
                        throw ApiException.ServiceUnavailable(
                            "password_hasher_unavailable",
                            "Unable to verify your password right now. Please try again shortly.");
                    }

                    var account = string.IsNullOrEmpty(user.AuthAccountId)
                        ? null
                        : await db.ApplicationUserAccounts.FirstOrDefaultAsync(x => x.Id == user.AuthAccountId, cancellationToken);

                    if (account is null ||
                        passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
                    {
                        throw ApiException.Unauthorized("invalid_current_password", "The current password you entered is incorrect.");
                    }

                    user.Email = newEmail;
                }
            }

            if (request.Values.TryGetValue("professionId", out var professionId))
            {
                var normalizedProfession = await ResolveProfessionChangeAsync(user, goal, professionId, "profile", cancellationToken);
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
                goal.TargetCountry = TargetCountryOptions.Canonicalize(ReadString(targetCountry));
            }

            if (request.Values.TryGetValue("professionId", out var studyProfessionId))
            {
                var normalizedProfession = await ResolveProfessionChangeAsync(user, goal, studyProfessionId, "study", cancellationToken);
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

    /// <summary>
    /// Resolve a learner-requested profession change into the canonical catalog id, or
    /// throw. Profession is one half of the subtest x profession axis that decides which
    /// content a package unlocks, so it is not free-text and it does not stay editable
    /// forever: once the learner has bought something, only an admin can move it.
    /// Returns the value to persist (unchanged when the PATCH is a no-op re-send, which
    /// the settings form does on every save).
    /// </summary>
    private async Task<string?> ResolveProfessionChangeAsync(
        LearnerUser user,
        LearnerGoal goal,
        object? requestedProfessionId,
        string section,
        CancellationToken cancellationToken)
    {
        var current = goal.ProfessionId ?? user.ActiveProfessionId;
        var requested = ReadString(requestedProfessionId)?.Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return current;
        }

        if (string.Equals(requested, current, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var normalized = requested.ToLowerInvariant();
        var catalogEntry = await db.SignupProfessionCatalog.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id.ToLower() == normalized && entry.IsActive, cancellationToken)
            ?? throw ApiException.Validation(
                "unknown_profession",
                $"Unknown profession '{requested}'.",
                [new ApiFieldError("professionId", "unknown", "Choose a profession from the registration list.")]);

        if (await HasCompletedPurchaseAsync(user.Id, cancellationToken))
        {
            throw ApiException.Validation(
                "profession_locked",
                "Your profession is locked because you have already purchased a package. Contact support to request a change.",
                [new ApiFieldError("professionId", "locked", "Contact support to change your profession.")]);
        }

        await RecordEventAsync(user.Id, "profession_changed", new
        {
            userId = user.Id,
            section,
            from = current,
            to = catalogEntry.Id
        }, cancellationToken);
        LogAudit(
            user.Id,
            "Updated",
            "Profession",
            user.Id,
            $"Profession changed from '{current ?? "(none)"}' to '{catalogEntry.Id}' via {section} settings");

        return catalogEntry.Id;
    }

    /// <summary>
    /// True once the learner owns anything they paid for. Reads the Subscriptions table
    /// rather than PaymentTransactions so admin-granted packages (which never produce a
    /// transaction) count too. The only subscription that is NOT a purchase is the
    /// pre-payment checkout scaffold — Pending with the default auto fulfilment — so a
    /// learner who merely opened checkout is not locked, while a paid manual-delivery
    /// order awaiting admin hand-over (Pending + pending_manual) is.
    /// </summary>
    private async Task<bool> HasCompletedPurchaseAsync(string userId, CancellationToken cancellationToken)
        => await db.Subscriptions.AsNoTracking().AnyAsync(subscription =>
            subscription.UserId == userId
            && (subscription.Status != SubscriptionStatus.Pending
                || subscription.FulfilmentStatus != FulfilmentStatuses.Auto), cancellationToken);

    private static object GoalDto(LearnerGoal goal) => new
    {
        goalId = goal.Id,
        userId = goal.UserId,
        examFamilyCode = goal.ExamFamilyCode,
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
        targetExamMode = goal.TargetExamMode,
        confidenceLevel = goal.ConfidenceLevel,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private static object GoalSettingsDto(LearnerGoal goal) => new
    {
        examFamilyCode = goal.ExamFamilyCode,
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
        targetExamMode = goal.TargetExamMode,
        confidenceLevel = goal.ConfidenceLevel,
        draftState = JsonSupport.Deserialize<Dictionary<string, object?>>(goal.DraftStateJson, new Dictionary<string, object?>()),
        submittedAt = goal.SubmittedAt,
        updatedAt = goal.UpdatedAt
    };

    private static object SettingsDto(LearnerSettings settings, LearnerGoal goal)
    {
        var study = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.StudyJson, new Dictionary<string, object?>());
        study["targetExamDate"] = goal.TargetExamDate;
        study["studyHoursPerWeek"] = goal.StudyHoursPerWeek;
        study["targetCountry"] = goal.TargetCountry;
        study["professionId"] = goal.ProfessionId;
        study["examFamilyCode"] = goal.ExamFamilyCode;

        return new
        {
            profile = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.ProfileJson, new Dictionary<string, object?>()),
            goals = GoalSettingsDto(goal),
            notifications = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.NotificationsJson, new Dictionary<string, object?>()),
            privacy = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.PrivacyJson, new Dictionary<string, object?>()),
            accessibility = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AccessibilityJson, new Dictionary<string, object?>()),
            audio = JsonSupport.Deserialize<Dictionary<string, object?>>(settings.AudioJson, new Dictionary<string, object?>()),
            study
        };
    }

    private static void ApplyGoalSettingsPatch(LearnerGoal goal, Dictionary<string, object?> values)
    {
        if (values.TryGetValue("examFamilyCode", out var examFamilyCode))
        {
            goal.ExamFamilyCode = NormalizeExamFamilyCode(ReadString(examFamilyCode) ?? goal.ExamFamilyCode);
        }

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
            goal.TargetCountry = TargetCountryOptions.Canonicalize(ReadString(targetCountry));
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
}
