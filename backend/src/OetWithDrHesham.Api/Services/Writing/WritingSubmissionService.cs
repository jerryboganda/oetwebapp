using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

public interface IWritingSubmissionService
{
    Task<WritingSubmissionResponse> CreateSubmissionAsync(string userId, WritingSubmissionCreateRequest request, CancellationToken ct);
    Task<WritingSubmissionResponse?> GetSubmissionAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingGradeResponseV2?> GetSubmissionGradeAsync(string userId, Guid submissionId, CancellationToken ct);

    /// <summary>
    /// Resolves the owning scenario's answer-sheet PDF download path for a submitted letter.
    /// Owner-gated and post-submission only — the answer sheet is never exposed on the live
    /// exam surface, only revealed on the results page after the learner has submitted.
    /// Returns null when not owned or no answer sheet is attached.
    /// </summary>
    Task<string?> GetAnswerSheetDownloadPathAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingSubmissionResponse?> ReviseSubmissionAsync(string userId, Guid originalSubmissionId, WritingReviseRequest request, CancellationToken ct);

    /// <summary>
    /// Resolves the Case Notes stimulus PDF path + the learner's highlight snapshot for a
    /// submission, for the results page and tutor marking surface. Pass <paramref name="ownerUserId"/>
    /// to owner-gate (learner results); pass null when the caller is already authorized as staff
    /// (tutor marking). Returns null when the submission is absent or not owned.
    /// </summary>
    Task<WritingCaseNotesResponse?> GetCaseNotesAsync(Guid submissionId, string? ownerUserId, CancellationToken ct);
}

/// <summary>
/// Learner-facing submission orchestration. Persists a <see cref="WritingSubmission"/>
/// via the V2 evaluation pipeline, then returns immediately while grading runs
/// asynchronously. Ownership is enforced on every method: a learner can only
/// see/revise their own submissions.
/// </summary>
public sealed class WritingSubmissionService(
    LearnerDbContext db,
    IWritingSubmissionEvaluationPipeline pipeline,
    ILogger<WritingSubmissionService> logger,
    IWritingCaseNoteHighlightService highlightStore,
    IWritingAttemptEventService? attemptEvents = null) : IWritingSubmissionService
{
    private const string EmptyHighlights = "{}";
    public async Task<WritingSubmissionResponse> CreateSubmissionAsync(string userId, WritingSubmissionCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scenarioExists = await db.WritingScenarios.AsNoTracking().AnyAsync(s => s.Id == request.ScenarioId, ct);
        if (!scenarioExists)
        {
            throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");
        }

        // Submission lock (§17.7): once a non-revision submission for this learner+scenario
        // has reached a submitted/locked state, reject further creates so a re-submit cannot
        // overwrite a locked attempt. Revisions go through ReviseSubmissionAsync intentionally.
        var alreadyLocked = await db.WritingSubmissions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId
                && s.ScenarioId == request.ScenarioId
                && !s.IsRevision
                && (s.Status == "submitted" || s.Status == "graded" || s.Status == "locked"), ct);
        if (alreadyLocked)
        {
            throw ApiException.Conflict(
                "writing_submission_locked",
                "You have already submitted this task. Submitted attempts are locked; use revise to try again.");
        }

        var simulationMode = NormalizeSimulationMode(request.SimulationMode);
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(0, request.TimeSpentSeconds));
        var submissionId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            UserId: userId,
            ScenarioId: request.ScenarioId,
            Mode: NormalizeMode(request.Mode),
            GradingTier: "express",
            InputSource: NormalizeInputSource(request.InputSource),
            LetterContent: request.LetterContent,
            TimeSpentSeconds: request.TimeSpentSeconds,
            StartedAt: startedAt,
            IsRevision: false,
            OriginalSubmissionId: null), ct);
        var outcome = await pipeline.EvaluateAsync(submissionId, ct);
        await EnsureGradeForSubmissionAsync(submissionId, outcome, ct);

        var entity = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new InvalidOperationException("Submission missing after create.");

        // Snapshot the learner's Case Notes highlights onto the submission (for the results
        // page + tutor review) and keep the per-(user,scenario) store in sync. Prefer the
        // request value (the live marks at submit time); otherwise fall back to whatever the
        // learner has already saved for the scenario.
        var requestedHighlights = request.CaseNoteHighlightsJson?.Trim();
        var highlightsJson = !string.IsNullOrEmpty(requestedHighlights) && requestedHighlights != EmptyHighlights
            ? requestedHighlights
            : await highlightStore.GetAsync(userId, request.ScenarioId, ct);
        if (!string.IsNullOrWhiteSpace(highlightsJson) && highlightsJson != EmptyHighlights)
        {
            await db.WritingSubmissions
                .Where(s => s.Id == submissionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CaseNoteHighlightsJson, highlightsJson), ct);
            entity.CaseNoteHighlightsJson = highlightsJson;
            await highlightStore.SaveAsync(userId, request.ScenarioId, highlightsJson, ct);
        }

        // §17.7 — the submission is now persisted in a graded/locked state. Emit the
        // lifecycle markers carrying the simulation mode (paper|computer) in payload,
        // best-effort so event logging never fails the submit.
        var payload = $"{{\"simulationMode\":\"{simulationMode}\",\"wordCount\":{entity.WordCount}}}";
        await SafeEmitAsync(userId, "writing_started", simulationMode, entity.ScenarioId, entity.Id, payload, ct);
        await SafeEmitAsync(userId, "submit_clicked", simulationMode, entity.ScenarioId, entity.Id, payload, ct);
        await SafeEmitAsync(userId, "attempt_locked", simulationMode, entity.ScenarioId, entity.Id, payload, ct);

        return WritingV2ResponseMapper.ToSubmissionResponse(entity);
    }

    public async Task<WritingSubmissionResponse?> GetSubmissionAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var s = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId && x.UserId == userId, ct);
        return s is null ? null : WritingV2ResponseMapper.ToSubmissionResponse(s);
    }

    public async Task<string?> GetAnswerSheetDownloadPathAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        // Owner-gated, post-submission only: resolve via the submission so the answer sheet is
        // never reachable from the live exam-surface scenario response.
        var scenarioId = await db.WritingSubmissions.AsNoTracking()
            .Where(x => x.Id == submissionId && x.UserId == userId)
            .Select(x => (Guid?)x.ScenarioId)
            .FirstOrDefaultAsync(ct);
        if (scenarioId is null) return null;

        var assetId = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Id == scenarioId.Value)
            .Select(s => s.AnswerSheetPdfMediaAssetId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(assetId) ? null : $"/v1/media/{assetId}/content";
    }

    public async Task<WritingCaseNotesResponse?> GetCaseNotesAsync(Guid submissionId, string? ownerUserId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null) return null;
        // Owner gate for the learner results page; tutors pass null (already staff-authorized).
        if (ownerUserId is not null && !string.Equals(submission.UserId, ownerUserId, StringComparison.Ordinal))
        {
            return null;
        }

        var assetId = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Id == submission.ScenarioId)
            .Select(s => s.StimulusPdfMediaAssetId)
            .FirstOrDefaultAsync(ct);
        var path = string.IsNullOrWhiteSpace(assetId) ? null : $"/v1/media/{assetId}/content";
        // Prefer the submission's own snapshot; fall back to the learner's per-scenario
        // store (highlights persist forever per user+scenario, so submit paths that don't
        // snapshot — e.g. mock/paper — still surface the learner's marks here).
        var highlights = submission.CaseNoteHighlightsJson;
        if (string.IsNullOrWhiteSpace(highlights) || highlights == EmptyHighlights)
        {
            highlights = await highlightStore.GetAsync(submission.UserId, submission.ScenarioId, ct);
        }
        return new WritingCaseNotesResponse(path, string.IsNullOrWhiteSpace(highlights) ? EmptyHighlights : highlights);
    }

    public async Task<WritingGradeResponseV2?> GetSubmissionGradeAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var s = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId && x.UserId == userId, ct);
        if (s is null) return null;
        var grade = await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);
        if (grade is null) return null;
        var violations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == submissionId)
            .ToListAsync(ct);
        var ruleIds = violations.Select(v => v.RuleId).Distinct().ToList();
        var ruleText = await db.WritingCanonRules.AsNoTracking()
            .Where(r => ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RuleText, ct);

        return WritingV2ResponseMapper.ToGradeResponse(grade, violations, ruleText);
    }

    public async Task<WritingSubmissionResponse?> ReviseSubmissionAsync(string userId, Guid originalSubmissionId, WritingReviseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var original = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == originalSubmissionId && x.UserId == userId, ct);
        if (original is null) return null;
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(0, request.TimeSpentSeconds));
        var newId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
            UserId: userId,
            ScenarioId: original.ScenarioId,
            Mode: original.Mode,
            GradingTier: original.GradingTier,
            InputSource: original.InputSource,
            LetterContent: request.LetterContent,
            TimeSpentSeconds: request.TimeSpentSeconds,
            StartedAt: startedAt,
            IsRevision: true,
            OriginalSubmissionId: originalSubmissionId), ct);
        var outcome = await pipeline.EvaluateAsync(newId, ct);
        await EnsureGradeForSubmissionAsync(newId, outcome, ct);
        var entity = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == newId, ct)
            ?? throw new InvalidOperationException("Revision submission missing after create.");
        return WritingV2ResponseMapper.ToSubmissionResponse(entity);
    }

    private async Task EnsureGradeForSubmissionAsync(Guid submissionId, WritingSubmissionGradeOutcome outcome, CancellationToken ct)
    {
        if (await db.WritingGrades.AsNoTracking().AnyAsync(g => g.SubmissionId == submissionId, ct)) return;
        var reused = await db.WritingGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == outcome.GradeId, ct);
        if (reused is null) return;
        db.WritingGrades.Add(new WritingGrade
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            C1Purpose = reused.C1Purpose,
            C2Content = reused.C2Content,
            C3Conciseness = reused.C3Conciseness,
            C4Genre = reused.C4Genre,
            C5Organisation = reused.C5Organisation,
            C6Language = reused.C6Language,
            RawTotal = reused.RawTotal,
            EstimatedBand = reused.EstimatedBand,
            BandLabel = reused.BandLabel,
            PerCriterionFeedbackJson = reused.PerCriterionFeedbackJson,
            TopThreePrioritiesJson = reused.TopThreePrioritiesJson,
            ConfidenceFlag = reused.ConfidenceFlag,
            ModelUsed = reused.ModelUsed,
            CanonVersion = reused.CanonVersion,
            GradedAt = reused.GradedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var reusedViolations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == reused.SubmissionId)
            .ToListAsync(ct);
        foreach (var violation in reusedViolations)
        {
            db.WritingCanonViolations.Add(new WritingCanonViolation
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                RuleId = violation.RuleId,
                Severity = violation.Severity,
                Snippet = violation.Snippet,
                LineNumber = violation.LineNumber,
                CharStart = violation.CharStart,
                CharEnd = violation.CharEnd,
                SuggestedFix = violation.SuggestedFix,
                Disputed = violation.Disputed,
                DisputeResolution = violation.DisputeResolution,
                DetectedAt = violation.DetectedAt,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Simulation mode is paper | computer; anything else defaults to computer.</summary>
    private static string NormalizeSimulationMode(string? simulationMode)
        => string.Equals(simulationMode, "paper", StringComparison.OrdinalIgnoreCase) ? "paper" : "computer";

    /// <summary>
    /// Emit a Writing attempt event without ever throwing into the caller
    /// (spec §17.7 — server-side lifecycle events are fire-and-forget-safe).
    /// </summary>
    private async Task SafeEmitAsync(
        string userId,
        string eventType,
        string simulationMode,
        Guid scenarioId,
        Guid submissionId,
        string payloadJson,
        CancellationToken ct)
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
                        DateTimeOffset.UtcNow,
                        simulationMode,
                        SessionId: null,
                        scenarioId,
                        submissionId,
                        payloadJson),
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to emit Writing attempt event {EventType} for submission {SubmissionId}.", eventType, submissionId);
        }
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "practice";
        return mode.Trim().ToLowerInvariant() switch
        {
            "practice" => "practice",
            "coached" => "coached",
            "timed" => "timed",
            "diagnostic" => "diagnostic",
            "mock" => "mock",
            "revision" => "revision",
            _ => throw ApiException.Validation("writing_submission_invalid_mode", "Unsupported writing submission mode."),
        };
    }

    private static string NormalizeInputSource(string? inputSource)
    {
        if (string.IsNullOrWhiteSpace(inputSource)) return "editor";
        return inputSource.Trim().ToLowerInvariant() switch
        {
            "editor" or "typed" => "editor",
            "paper-ocr" => "paper-ocr",
            "voice-draft" => "voice-draft",
            _ => throw ApiException.Validation("writing_submission_invalid_input_source", "Unsupported writing input source."),
        };
    }
}
