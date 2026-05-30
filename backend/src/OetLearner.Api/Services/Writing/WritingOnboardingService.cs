using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingOnboardingState(
    string UserId,
    string CurrentStage,
    bool HasProfile,
    bool DiagnosticCompleted,
    bool ProfileSubStepProfession,
    bool ProfileSubStepGoals,
    bool ProfileSubStepFocus,
    bool ProfileSubStepConfirm,
    int CompletionPercent);

public sealed record WritingOnboardingProfile(
    string Profession,
    string? SubDiscipline,
    int? YearsExperience,
    string TargetBand,
    DateTimeOffset? ExamDate,
    int DaysPerWeek,
    int MinutesPerDay,
    string TargetCountry,
    IReadOnlyList<string> LetterTypeFocus,
    bool OptInCommunity,
    bool OptInLeaderboard,
    bool OptInDataForTraining,
    WritingAccommodationProfileDto? Accommodations);

public sealed record WritingBudgetAssessment(
    int MinutesAvailablePerDay,
    int RecommendedMinutesPerDay,
    int DaysPerWeek,
    int? WeeksToExam,
    int TotalMinutes,
    IReadOnlyList<string> ConflictsWithOtherModules,
    string? Warning);

public interface IWritingOnboardingService
{
    Task<WritingOnboardingState> GetStateAsync(string userId, CancellationToken ct);
    Task<WritingOnboardingState> SaveProfileAsync(string userId, WritingOnboardingProfile profile, CancellationToken ct);
    Task<WritingBudgetAssessment> AssessBudgetAsync(string userId, CancellationToken ct);
    Task<bool> CanProceedToDiagnosticAsync(string userId, CancellationToken ct);

    // ── V2 endpoint contract adapters (WritingV2Contracts shapes) ────────────
    Task<WritingProfileResponseV2> GetProfileAsync(string userId, CancellationToken ct);
    Task<WritingProfileResponseV2> SaveProfileAsync(string userId, WritingProfileUpdateRequest request, CancellationToken ct);
    Task<WritingBudgetAssessmentResponse> GetBudgetResponseAsync(string userId, CancellationToken ct);
    Task<WritingProfileResponseV2> CompleteOnboardingAsync(string userId, CancellationToken ct);
    Task<WritingDiagnosticSessionResponse> StartDiagnosticAsync(string userId, Guid? scenarioId, CancellationToken ct);
    Task<WritingDiagnosticSessionResponse?> GetDiagnosticSessionAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingDiagnosticSessionResponse?> BeginDiagnosticWritingPhaseAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingSubmissionResponse?> SubmitDiagnosticAsync(string userId, Guid sessionId, WritingDiagnosticSubmitRequest request, CancellationToken ct);
    Task<WritingDiagnosticResultsResponse?> GetDiagnosticResultsAsync(string userId, Guid sessionId, CancellationToken ct);
}

public sealed class WritingOnboardingService(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingSubmissionEvaluationPipeline pipeline,
    IWritingPathwayServiceV2 pathwayService,
    ILogger<WritingOnboardingService> logger,
    IWritingAttemptEventService? attemptEvents = null)
    : IWritingOnboardingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DiagnosticReadingPhaseSeconds = 5 * 60;
    private const int DiagnosticWritingPhaseSeconds = 40 * 60;
    private static readonly TimeSpan DiagnosticSessionLifetime = TimeSpan.FromHours(2);

    public async Task<WritingOnboardingState> GetStateAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var diagnosticDone = profile is not null && await DiagnosticCompletedAsync(userId, ct);
        return BuildState(userId, profile, diagnosticDone);
    }

    public async Task<WritingOnboardingState> SaveProfileAsync(string userId, WritingOnboardingProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var now = clock.GetUtcNow();
        var entity = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (entity is null)
        {
            entity = new LearnerWritingProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
            };
            db.LearnerWritingProfiles.Add(entity);
        }

        entity.Profession = NormalizeProfession(profile.Profession);
        entity.SubDiscipline = string.IsNullOrWhiteSpace(profile.SubDiscipline) ? null : profile.SubDiscipline.Trim();
        entity.YearsExperience = profile.YearsExperience is null ? null : Math.Clamp(profile.YearsExperience.Value, 0, 70);
        entity.TargetBand = NormalizeTargetBand(profile.TargetBand);
        entity.ExamDate = profile.ExamDate;
        entity.DaysPerWeek = Math.Clamp(profile.DaysPerWeek, 1, 7);
        entity.MinutesPerDay = Math.Clamp(profile.MinutesPerDay, 15, 180);
        entity.TargetCountry = NormalizeCountry(profile.TargetCountry);
        entity.LetterTypeFocusJson = JsonSerializer.Serialize(NormalizeLetterTypes(profile.LetterTypeFocus), JsonOptions);
        entity.OptInCommunity = profile.OptInCommunity;
        entity.OptInLeaderboard = profile.OptInLeaderboard;
        entity.OptInDataForTraining = profile.OptInDataForTraining;
        entity.AccommodationProfileJson = profile.Accommodations is null
            ? "{}"
            : JsonSerializer.Serialize(profile.Accommodations, JsonOptions);
        if (entity.OnboardingCompletedAt is null && IsProfileComplete(entity))
        {
            entity.OnboardingCompletedAt = now;
            entity.CurrentStage = "diagnostic";
        }
        entity.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        var diagnosticDone = await DiagnosticCompletedAsync(userId, ct);
        return BuildState(userId, entity, diagnosticDone);
    }

    public async Task<WritingBudgetAssessment> AssessBudgetAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var minutesPerDay = profile?.MinutesPerDay ?? 45;
        var daysPerWeek = profile?.DaysPerWeek ?? 5;
        var weeksToExam = WeeksRemaining(profile?.ExamDate, clock.GetUtcNow());
        var totalMinutes = (weeksToExam ?? 10) * daysPerWeek * minutesPerDay;

        var conflicts = new List<string>();
        try
        {
            var todayUtc = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var studyPlanMinutesToday = await db.StudyPlanItems.AsNoTracking()
                .Where(i => i.DueDate == todayUtc)
                .SumAsync(i => (int?)i.DurationMinutes ?? 0, ct);
            if (studyPlanMinutesToday + minutesPerDay > 240)
            {
                conflicts.Add("study_plan_overlap");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Writing budget assess: study plan integration unavailable.");
        }

        var recommended = (weeksToExam, minutesPerDay) switch
        {
            (<= 4, < 60) => 60,
            (<= 8, < 45) => 45,
            _ => minutesPerDay,
        };

        string? warning = null;
        if (recommended > minutesPerDay)
        {
            warning = $"Recommended at least {recommended} minutes per day to reach your target band before the exam.";
        }

        return new WritingBudgetAssessment(
            MinutesAvailablePerDay: minutesPerDay,
            RecommendedMinutesPerDay: recommended,
            DaysPerWeek: daysPerWeek,
            WeeksToExam: weeksToExam,
            TotalMinutes: totalMinutes,
            ConflictsWithOtherModules: conflicts,
            Warning: warning);
    }

    public async Task<bool> CanProceedToDiagnosticAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return profile is not null && IsProfileComplete(profile);
    }

    private static WritingOnboardingState BuildState(string userId, LearnerWritingProfile? profile, bool diagnosticDone)
    {
        if (profile is null)
        {
            return new WritingOnboardingState(userId, "onboarding", false, false, false, false, false, false, 0);
        }

        var professionDone = !string.IsNullOrWhiteSpace(profile.Profession);
        var goalsDone = !string.IsNullOrWhiteSpace(profile.TargetBand) && profile.DaysPerWeek > 0 && profile.MinutesPerDay > 0;
        var focusDone = !string.Equals(profile.LetterTypeFocusJson, "[]", StringComparison.Ordinal);
        var confirmDone = profile.OnboardingCompletedAt is not null;
        var completion = 100 * (
            (professionDone ? 1 : 0)
            + (goalsDone ? 1 : 0)
            + (focusDone ? 1 : 0)
            + (confirmDone ? 1 : 0)) / 4;

        var stage = profile.CurrentStage;
        if (string.IsNullOrWhiteSpace(stage))
        {
            stage = confirmDone ? (diagnosticDone ? "foundation" : "diagnostic") : "onboarding";
        }
        return new WritingOnboardingState(userId, stage, true, diagnosticDone,
            professionDone, goalsDone, focusDone, confirmDone, completion);
    }

    private async Task<bool> DiagnosticCompletedAsync(string userId, CancellationToken ct)
    {
        return await db.WritingSubmissions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.Mode == "diagnostic" && s.Status == "graded", ct);
    }

    private static bool IsProfileComplete(LearnerWritingProfile profile)
        => !string.IsNullOrWhiteSpace(profile.Profession)
           && !string.IsNullOrWhiteSpace(profile.TargetBand)
           && profile.DaysPerWeek > 0
           && profile.MinutesPerDay > 0
           && !string.Equals(profile.LetterTypeFocusJson, "[]", StringComparison.Ordinal);

    private static int? WeeksRemaining(DateTimeOffset? examDate, DateTimeOffset now)
    {
        if (examDate is null) return null;
        var diff = (examDate.Value - now).TotalDays / 7.0;
        return Math.Max(0, (int)Math.Ceiling(diff));
    }

    private static string NormalizeProfession(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        return v switch
        {
            "medicine" or "pharmacy" or "nursing" or "other" => v,
            _ => "medicine",
        };
    }

    private static string NormalizeTargetBand(string value)
        => (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "A" => "A",
            "B+" => "B+",
            _ => "B",
        };

    private static string NormalizeCountry(string value)
        => string.IsNullOrWhiteSpace(value) ? "GB" : value.Trim().ToUpperInvariant();

    private static List<string> NormalizeLetterTypes(IReadOnlyList<string>? focus)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LT-RR", "LT-UR", "LT-DG", "LT-TR", "LT-RP", "LT-NM" };
        return (focus ?? Array.Empty<string>())
            .Select(v => v.Trim().ToUpperInvariant())
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters — translate to/from WritingV2Contracts shapes.
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingProfileResponseV2> GetProfileAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var diagnosticDone = profile is not null && await DiagnosticCompletedAsync(userId, ct);
        return BuildProfileResponse(userId, profile, diagnosticDone);
    }

    public async Task<WritingProfileResponseV2> SaveProfileAsync(string userId, WritingProfileUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset? examDate = null;
        if (!string.IsNullOrWhiteSpace(request.ExamDate)
            && DateTimeOffset.TryParse(request.ExamDate, out var parsed))
        {
            examDate = parsed;
        }
        var profile = new WritingOnboardingProfile(
            Profession: request.Profession,
            SubDiscipline: request.SubDiscipline,
            YearsExperience: request.YearsExperience,
            TargetBand: request.TargetBand,
            ExamDate: examDate,
            DaysPerWeek: request.DaysPerWeek,
            MinutesPerDay: request.MinutesPerDay,
            TargetCountry: string.IsNullOrWhiteSpace(request.TargetCountry) ? "GB" : request.TargetCountry,
            LetterTypeFocus: request.LetterTypeFocus ?? Array.Empty<string>(),
            OptInCommunity: request.OptInCommunity ?? false,
            OptInLeaderboard: request.OptInLeaderboard ?? false,
            OptInDataForTraining: request.OptInDataForTraining ?? false,
            Accommodations: null);
        await SaveProfileAsync(userId, profile, ct);
        return await GetProfileAsync(userId, ct);
    }

    public async Task<WritingBudgetAssessmentResponse> GetBudgetResponseAsync(string userId, CancellationToken ct)
    {
        var legacy = await AssessBudgetAsync(userId, ct);
        var min = Math.Max(15, legacy.MinutesAvailablePerDay - 15);
        var max = Math.Min(180, legacy.RecommendedMinutesPerDay + 30);
        return new WritingBudgetAssessmentResponse(
            MinutesAvailablePerDay: legacy.MinutesAvailablePerDay,
            MinutesPerDayMin: min,
            MinutesPerDayMax: max,
            DaysPerWeek: legacy.DaysPerWeek,
            WeeksToExam: legacy.WeeksToExam,
            TotalMinutes: legacy.TotalMinutes,
            ConflictsWithOtherModules: legacy.ConflictsWithOtherModules);
    }

    public async Task<WritingProfileResponseV2> CompleteOnboardingAsync(string userId, CancellationToken ct)
    {
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_profile_missing", "Complete Writing onboarding first.");
        if (profile.OnboardingCompletedAt is null)
        {
            profile.OnboardingCompletedAt = clock.GetUtcNow();
            profile.CurrentStage = "diagnostic";
            profile.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return await GetProfileAsync(userId, ct);
    }

    public async Task<WritingDiagnosticSessionResponse> StartDiagnosticAsync(string userId, Guid? scenarioId, CancellationToken ct)
    {
        Guid resolvedScenarioId;
        if (scenarioId.HasValue && scenarioId.Value != Guid.Empty)
        {
            var exists = await db.WritingScenarios.AsNoTracking()
                .AnyAsync(s => s.Id == scenarioId.Value && s.IsDiagnostic, ct);
            if (!exists)
            {
                throw ApiException.NotFound("writing_diagnostic_scenario_not_found", "Diagnostic scenario was not found.");
            }
            resolvedScenarioId = scenarioId.Value;
        }
        else
        {
            var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
            var profession = profile?.Profession ?? "medicine";
            var picked = await db.WritingScenarios.AsNoTracking()
                .Where(s => s.IsDiagnostic && s.Status == "published" && s.Profession == profession)
                .OrderBy(s => s.Difficulty)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct);
            if (picked == Guid.Empty)
            {
                picked = await db.WritingScenarios.AsNoTracking()
                    .Where(s => s.IsDiagnostic && s.Status == "published")
                    .OrderBy(s => s.Difficulty)
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(ct);
            }
            if (picked == Guid.Empty)
            {
                throw ApiException.NotFound("writing_diagnostic_scenario_unavailable", "No diagnostic scenarios are available.");
            }
            resolvedScenarioId = picked;
        }

        var now = clock.GetUtcNow();
        var session = new WritingDiagnosticSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ScenarioId = resolvedScenarioId,
            StartedAt = now,
            ReadingPhaseEndedAt = null,
            SubmissionId = null,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + DiagnosticSessionLifetime,
        };
        db.WritingDiagnosticSessions.Add(session);
        await db.SaveChangesAsync(ct);
        // §17.7 — diagnostic attempt lifecycle (best-effort, simulation mode defaults to computer).
        await SafeEmitAsync(userId, "attempt_started", session.Id, session.ScenarioId, "reading", ct);
        await SafeEmitAsync(userId, "reading_started", session.Id, session.ScenarioId, "reading", ct);
        return BuildDiagnosticSessionResponse(session, now);
    }

    public async Task<WritingDiagnosticSessionResponse?> GetDiagnosticSessionAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await LoadActiveSessionAsync(userId, sessionId, tracked: false, ct);
        return session is null ? null : BuildDiagnosticSessionResponse(session, clock.GetUtcNow());
    }

    public async Task<WritingDiagnosticSessionResponse?> BeginDiagnosticWritingPhaseAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await LoadActiveSessionAsync(userId, sessionId, tracked: true, ct);
        if (session is null) return null;
        if (session.ReadingPhaseEndedAt is null)
        {
            var now = clock.GetUtcNow();
            session.ReadingPhaseEndedAt = now;
            session.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            // §17.7 — reading→writing transition (best-effort).
            await SafeEmitAsync(userId, "reading_ended", session.Id, session.ScenarioId, "writing", ct);
            await SafeEmitAsync(userId, "writing_started", session.Id, session.ScenarioId, "writing", ct);
        }
        return BuildDiagnosticSessionResponse(session, clock.GetUtcNow());
    }

    public async Task<WritingSubmissionResponse?> SubmitDiagnosticAsync(string userId, Guid sessionId, WritingDiagnosticSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await LoadActiveSessionAsync(userId, sessionId, tracked: true, ct);
        if (session is null) return null;

        var submissionId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            UserId: userId,
            ScenarioId: session.ScenarioId,
            Mode: "diagnostic",
            GradingTier: "express",
            InputSource: "typed",
            LetterContent: request.LetterContent,
            TimeSpentSeconds: request.TimeSpentSeconds,
            StartedAt: session.StartedAt,
            IsRevision: false,
            OriginalSubmissionId: null), ct);
        try
        {
            await pipeline.EvaluateAsync(submissionId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing diagnostic grading failed for submission {SubmissionId}; results endpoint will retry.", submissionId);
        }

        session.SubmissionId = submissionId;
        session.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // §17.7 — diagnostic submitted/locked lifecycle (best-effort).
        await SafeEmitAsync(userId, "submit_clicked", session.Id, session.ScenarioId, "submitted", ct, submissionId);
        await SafeEmitAsync(userId, "attempt_locked", session.Id, session.ScenarioId, "submitted", ct, submissionId);

        var sub = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        return sub is null ? null : ToSubmissionResponse(sub);
    }

    public async Task<WritingDiagnosticResultsResponse?> GetDiagnosticResultsAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        // Submitted sessions remain queryable past ExpiresAt for audit; only
        // gate by ownership + presence of a SubmissionId here.
        var session = await db.WritingDiagnosticSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return null;
        if (session.SubmissionId is null) return null;

        var sub = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.SubmissionId, ct);
        if (sub is null) return null;
        var grade = await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.SubmissionId == session.SubmissionId, ct);
        if (grade is null) return null;
        var canon = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == session.SubmissionId)
            .ToListAsync(ct);
        var rules = await db.WritingCanonRules.AsNoTracking()
            .Where(r => canon.Select(v => v.RuleId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RuleText, ct);

        var pathwayPreview = await pathwayService.GetPathwayAsync(userId, ct);
        var gradeResponse = WritingV2ResponseMapper.ToGradeResponse(grade, canon, rules, null);
        return new WritingDiagnosticResultsResponse(sessionId, sub.Id, gradeResponse, pathwayPreview);
    }

    /// <summary>
    /// Load a diagnostic session row scoped to <paramref name="userId"/> for
    /// multi-tenant isolation. Returns null if the row is missing or expired.
    /// A row whose <see cref="WritingDiagnosticSession.SubmissionId"/> is set
    /// counts as active for as long as the cron retains it (submitted rows
    /// are kept for audit), so the expiry gate only applies when no submission
    /// has been created yet.
    /// </summary>
    private async Task<WritingDiagnosticSession?> LoadActiveSessionAsync(string userId, Guid sessionId, bool tracked, CancellationToken ct)
    {
        var query = db.WritingDiagnosticSessions.AsQueryable();
        if (!tracked) query = query.AsNoTracking();
        var session = await query.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return null;
        if (session.SubmissionId is null && session.ExpiresAt <= clock.GetUtcNow()) return null;
        return session;
    }

    /// <summary>
    /// Emit a Writing attempt event for a diagnostic session without ever throwing
    /// into the caller (spec §17.7 — fire-and-forget-safe). Diagnostic sessions carry
    /// no simulation-mode column, so mode defaults to "computer".
    /// </summary>
    private async Task SafeEmitAsync(string userId, string eventType, Guid sessionId, Guid scenarioId, string status, CancellationToken ct, Guid? submissionId = null)
    {
        if (attemptEvents is null) return;
        try
        {
            await attemptEvents.RecordAsync(
                userId,
                new[]
                {
                    new WritingAttemptEventInput(
                        eventType,
                        clock.GetUtcNow(),
                        "computer",
                        sessionId.ToString(),
                        scenarioId,
                        submissionId,
                        $"{{\"context\":\"diagnostic\",\"status\":\"{status}\"}}"),
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to emit Writing attempt event {EventType} for diagnostic session {SessionId}.", eventType, sessionId);
        }
    }

    private WritingProfileResponseV2 BuildProfileResponse(string userId, LearnerWritingProfile? profile, bool diagnosticDone)
    {
        if (profile is null)
        {
            return new WritingProfileResponseV2(
                UserId: userId,
                CurrentStage: "onboarding",
                Profession: string.Empty,
                SubDiscipline: null,
                YearsExperience: null,
                TargetBand: "B",
                ExamDate: null,
                DaysPerWeek: 5,
                MinutesPerDay: 45,
                TargetCountry: "GB",
                LetterTypeFocus: Array.Empty<string>(),
                ReadinessScore: null,
                PredictedScore: null,
                OnboardingCompletedAt: null,
                PathwayGeneratedAt: null,
                WeeksRemaining: null,
                DiagnosticCompleted: false,
                OptInCommunity: false,
                OptInLeaderboard: false,
                OptInDataForTraining: false,
                AccommodationProfile: null,
                CanonVersionPinned: null);
        }

        var focus = DeserializeStringList(profile.LetterTypeFocusJson);
        WritingAccommodationProfileDto? accommodations = null;
        if (!string.IsNullOrWhiteSpace(profile.AccommodationProfileJson) && profile.AccommodationProfileJson != "{}")
        {
            try
            {
                accommodations = JsonSerializer.Deserialize<WritingAccommodationProfileDto>(profile.AccommodationProfileJson, JsonOptions);
            }
            catch (JsonException)
            {
                accommodations = null;
            }
        }
        int? weeksRemaining = profile.ExamDate is null ? null : Math.Max(0, (int)Math.Ceiling((profile.ExamDate.Value - clock.GetUtcNow()).TotalDays / 7.0));
        return new WritingProfileResponseV2(
            UserId: userId,
            CurrentStage: string.IsNullOrWhiteSpace(profile.CurrentStage) ? "onboarding" : profile.CurrentStage,
            Profession: profile.Profession,
            SubDiscipline: profile.SubDiscipline,
            YearsExperience: profile.YearsExperience,
            TargetBand: profile.TargetBand,
            ExamDate: profile.ExamDate?.ToString("O"),
            DaysPerWeek: profile.DaysPerWeek,
            MinutesPerDay: profile.MinutesPerDay,
            TargetCountry: profile.TargetCountry,
            LetterTypeFocus: focus,
            ReadinessScore: profile.CurrentReadinessScore,
            PredictedScore: profile.PredictedScore,
            OnboardingCompletedAt: profile.OnboardingCompletedAt,
            PathwayGeneratedAt: profile.PathwayGeneratedAt,
            WeeksRemaining: weeksRemaining,
            DiagnosticCompleted: diagnosticDone,
            OptInCommunity: profile.OptInCommunity,
            OptInLeaderboard: profile.OptInLeaderboard,
            OptInDataForTraining: profile.OptInDataForTraining,
            AccommodationProfile: accommodations,
            CanonVersionPinned: profile.CanonVersionPinned);
    }

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static WritingSubmissionResponse ToSubmissionResponse(WritingSubmission s)
        => new(
            Id: s.Id,
            UserId: s.UserId,
            ScenarioId: s.ScenarioId,
            Mode: s.Mode,
            LetterContent: s.LetterContent,
            ContentHash: s.LetterContentHash,
            WordCount: s.WordCount,
            TimeSpentSeconds: s.TimeSpentSeconds,
            StartedAt: s.StartedAt,
            SubmittedAt: s.SubmittedAt,
            IsRevision: s.IsRevision,
            OriginalSubmissionId: s.OriginalSubmissionId,
            Status: s.Status,
            GradingTier: s.GradingTier,
            InputSource: s.InputSource);

    private WritingDiagnosticSessionResponse BuildDiagnosticSessionResponse(WritingDiagnosticSession session, DateTimeOffset now)
    {
        var phase = session.SubmissionId is not null
            ? "submitted"
            : session.ReadingPhaseEndedAt is not null
                ? "writing"
                : "reading";
        var readingRemaining = session.ReadingPhaseEndedAt is not null
            ? 0
            : Math.Max(0, DiagnosticReadingPhaseSeconds - (int)(now - session.StartedAt).TotalSeconds);
        var writingRemaining = session.ReadingPhaseEndedAt is null
            ? DiagnosticWritingPhaseSeconds
            : Math.Max(0, DiagnosticWritingPhaseSeconds - (int)(now - session.ReadingPhaseEndedAt.Value).TotalSeconds);
        return new WritingDiagnosticSessionResponse(
            Id: session.Id,
            ScenarioId: session.ScenarioId,
            Phase: phase,
            ReadingSecondsRemaining: readingRemaining,
            WritingSecondsRemaining: writingRemaining,
            StartedAt: session.StartedAt,
            ReadingPhaseEndedAt: session.ReadingPhaseEndedAt,
            SubmittedAt: null,
            SubmissionId: session.SubmissionId);
    }
}
