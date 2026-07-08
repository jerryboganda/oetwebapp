using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing.Events;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingMockTemplate(Guid Id, Guid ScenarioId, string Title, int Difficulty, string Status);

public sealed record WritingMockSessionView(
    Guid Id,
    Guid MockId,
    Guid ScenarioId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ReadingPhaseEndedAt,
    DateTimeOffset? SubmittedAt,
    Guid? SubmissionId,
    int ReadingSecondsRemaining,
    int WritingSecondsRemaining,
    bool IsPractice);

public interface IWritingMockService
{
    Task<IReadOnlyList<WritingMockTemplate>> ListAsync(string userId, CancellationToken ct);
    Task<WritingMockTemplate> CreateAsync(string adminId, WritingMockTemplate template, CancellationToken ct);
    Task<WritingMockSessionView> StartSessionAsync(string userId, Guid mockId, bool isPractice = false, CancellationToken ct = default);
    Task<WritingMockSessionView?> GetSessionAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingMockSessionView> BeginWritingAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingMockSessionView> SubmitSessionAsync(string userId, Guid sessionId, Guid submissionId, CancellationToken ct);
    Task<WritingMockSessionView> AbandonSessionAsync(string userId, Guid sessionId, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingMockListResponse> ListMocksAsync(string userId, CancellationToken ct);
    Task<WritingMockSessionResponse> StartMockAsync(string userId, WritingMockStartRequest request, CancellationToken ct);
    Task<WritingMockSessionResponse?> BeginMockWritingAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingMockSessionResponse?> GetMockSessionAsync(string userId, Guid sessionId, CancellationToken ct);
    Task<WritingSubmissionResponse?> SubmitMockAsync(string userId, Guid sessionId, WritingMockSubmitRequest request, CancellationToken ct);
    Task<WritingMockResultsResponse?> GetMockResultsAsync(string userId, Guid sessionId, CancellationToken ct);
}

public sealed class WritingMockService(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingEventBus events,
    IWritingSubmissionEvaluationPipeline pipeline,
    IWritingTutorReviewService tutorReview,
    ILogger<WritingMockService> logger,
    IWritingAttemptEventService? attemptEvents = null) : IWritingMockService
{
    private const int ReadingPhaseSeconds = 5 * 60;
    private const int WritingPhaseSeconds = 40 * 60;

    public async Task<IReadOnlyList<WritingMockTemplate>> ListAsync(string userId, CancellationToken ct)
    {
        _ = userId;
        var rows = await db.WritingMocks.AsNoTracking()
            .Where(m => m.Status == "published")
            .OrderBy(m => m.Difficulty)
            .ToListAsync(ct);
        return rows.Select(r => new WritingMockTemplate(r.Id, r.ScenarioId, r.Title, r.Difficulty, r.Status)).ToList();
    }

    public async Task<WritingMockTemplate> CreateAsync(string adminId, WritingMockTemplate template, CancellationToken ct)
    {
        _ = adminId;
        ArgumentNullException.ThrowIfNull(template);
        var now = clock.GetUtcNow();
        var entity = await db.WritingMocks.FirstOrDefaultAsync(m => m.Id == template.Id, ct);
        if (entity is null)
        {
            entity = new WritingMock { Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id, CreatedAt = now };
            db.WritingMocks.Add(entity);
        }
        entity.ScenarioId = template.ScenarioId;
        entity.Title = template.Title;
        entity.Difficulty = Math.Clamp(template.Difficulty, 1, 5);
        entity.Status = string.IsNullOrWhiteSpace(template.Status) ? "draft" : template.Status;
        await db.SaveChangesAsync(ct);
        return new WritingMockTemplate(entity.Id, entity.ScenarioId, entity.Title, entity.Difficulty, entity.Status);
    }

    public async Task<WritingMockSessionView> StartSessionAsync(string userId, Guid mockId, bool isPractice = false, CancellationToken ct = default)
    {
        var mock = await db.WritingMocks.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mockId && m.Status == "published", ct)
            ?? throw ApiException.NotFound("writing_mock_not_found", "Mock template was not found.");
        var now = clock.GetUtcNow();
        var session = new WritingMockSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MockId = mockId,
            StartedAt = now,
            Status = "reading",
            CreatedAt = now,
            IsPractice = isPractice,
        };
        db.WritingMockSessions.Add(session);
        await db.SaveChangesAsync(ct);
        // §17.7 — best-effort lifecycle events (paper|computer simulation, default computer).
        await SafeEmitAsync(userId, "attempt_started", session.Id, mock.ScenarioId, "mock", session.Status, ct);
        await SafeEmitAsync(userId, "reading_started", session.Id, mock.ScenarioId, "mock", session.Status, ct);
        return BuildView(session, mock.ScenarioId, clock.GetUtcNow());
    }

    public async Task<WritingMockSessionView?> GetSessionAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.WritingMockSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return null;
        var scenarioId = await db.WritingMocks.AsNoTracking().Where(m => m.Id == session.MockId).Select(m => m.ScenarioId).FirstOrDefaultAsync(ct);
        return BuildView(session, scenarioId, clock.GetUtcNow());
    }

    public async Task<WritingMockSessionView> BeginWritingAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await OwnedSessionAsync(userId, sessionId, ct);
        if (session.Status is "submitted" or "abandoned")
        {
            throw ApiException.Validation("writing_mock_session_locked", "Mock session is already finished.");
        }
        var now = clock.GetUtcNow();
        var readingEndsAt = session.StartedAt.AddSeconds(ReadingPhaseSeconds);
        if (!session.IsPractice && now < readingEndsAt)
        {
            throw ApiException.Validation("writing_mock_reading_window_active", "Mock reading window is still active.");
        }
        if (session.ReadingPhaseEndedAt is null)
        {
            session.ReadingPhaseEndedAt = now < readingEndsAt ? now : readingEndsAt;
        }
        session.Status = "writing";
        await db.SaveChangesAsync(ct);
        var scenarioId = await db.WritingMocks.AsNoTracking().Where(m => m.Id == session.MockId).Select(m => m.ScenarioId).FirstOrDefaultAsync(ct);
        // §17.7 — reading→writing transition lifecycle events (best-effort).
        await SafeEmitAsync(userId, "reading_ended", session.Id, scenarioId, "mock", session.Status, ct);
        await SafeEmitAsync(userId, "writing_started", session.Id, scenarioId, "mock", session.Status, ct);
        return BuildView(session, scenarioId, now);
    }

    public async Task<WritingMockSessionView> SubmitSessionAsync(string userId, Guid sessionId, Guid submissionId, CancellationToken ct)
    {
        var session = await OwnedSessionAsync(userId, sessionId, ct);
        if (session.Status == "submitted") return BuildView(session, await ScenarioIdAsync(session.MockId, ct), clock.GetUtcNow());
        var scenarioId = await ScenarioIdAsync(session.MockId, ct);
        var submission = await db.WritingSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId && s.Mode == "mock" && s.ScenarioId == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_mock_submission_not_found", "Mock submission was not found for this learner and scenario.");
        var now = clock.GetUtcNow();
        session.SubmissionId = submissionId;
        session.SubmittedAt = now;
        session.Status = "submitted";
        await db.SaveChangesAsync(ct);

        var readingSeconds = session.ReadingPhaseEndedAt is null
            ? ReadingPhaseSeconds
            : Math.Max(0, (int)(session.ReadingPhaseEndedAt.Value - session.StartedAt).TotalSeconds);
        var writingSeconds = session.ReadingPhaseEndedAt is null
            ? 0
            : Math.Max(0, (int)(now - session.ReadingPhaseEndedAt.Value).TotalSeconds);

        try
        {
            await events.PublishAsync(new WritingMockCompleted(
                UserId: userId,
                MockSessionId: session.Id,
                MockId: session.MockId,
                SubmissionId: submissionId,
                ReadingPhaseSeconds: readingSeconds,
                WritingPhaseSeconds: writingSeconds,
                OccurredAt: now), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing mock event publish failed for session {SessionId}.", session.Id);
        }
        // §17.7 — submission transitioned to submitted/locked: emit lifecycle markers (best-effort).
        await SafeEmitAsync(userId, "submit_clicked", session.Id, scenarioId, "mock", session.Status, submissionId, ct);
        await SafeEmitAsync(userId, "attempt_locked", session.Id, scenarioId, "mock", session.Status, submissionId, ct);
        return BuildView(session, scenarioId, now);
    }

    public async Task<WritingMockSessionView> AbandonSessionAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await OwnedSessionAsync(userId, sessionId, ct);
        session.Status = "abandoned";
        await db.SaveChangesAsync(ct);
        return BuildView(session, await ScenarioIdAsync(session.MockId, ct), clock.GetUtcNow());
    }

    private async Task<WritingMockSession> OwnedSessionAsync(string userId, Guid sessionId, CancellationToken ct)
        => await db.WritingMockSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
           ?? throw ApiException.NotFound("writing_mock_session_not_found", "Mock session was not found.");

    private async Task<bool> TryTransitionToGradingAsync(string userId, Guid sessionId, WritingMockSession session, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.WritingMockSessions
                .Where(s => s.Id == sessionId && s.UserId == userId && s.Status == "writing")
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, "grading"), ct);
            if (updated == 0) return false;
            session.Status = "grading";
            return true;
        }

        session.Status = "grading";
        await db.SaveChangesAsync(ct);
        return true;
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
            CreatedAt = clock.GetUtcNow(),
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

    private async Task<Guid> ScenarioIdAsync(Guid mockId, CancellationToken ct)
        => await db.WritingMocks.AsNoTracking().Where(m => m.Id == mockId).Select(m => m.ScenarioId).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Emit a Writing attempt event for a mock session without ever throwing into the
    /// caller (spec §17.7 — server-side lifecycle events are fire-and-forget-safe).
    /// Mock sessions carry no simulation-mode column, so mode defaults to "computer".
    /// </summary>
    private Task SafeEmitAsync(string userId, string eventType, Guid sessionId, Guid scenarioId, string context, string status, CancellationToken ct)
        => SafeEmitAsync(userId, eventType, sessionId, scenarioId, context, status, submissionId: null, ct);

    private async Task SafeEmitAsync(string userId, string eventType, Guid sessionId, Guid scenarioId, string context, string status, Guid? submissionId, CancellationToken ct)
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
                        $"{{\"context\":\"{context}\",\"status\":\"{status}\"}}"),
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to emit Writing attempt event {EventType} for mock session {SessionId}.", eventType, sessionId);
        }
    }

    private static WritingMockSessionView BuildView(WritingMockSession session, Guid scenarioId, DateTimeOffset now)
    {
        var readingRemaining = session.ReadingPhaseEndedAt is { } ended
            ? 0
            : Math.Max(0, ReadingPhaseSeconds - (int)(now - session.StartedAt).TotalSeconds);
        var writingRemaining = session.ReadingPhaseEndedAt is null
            ? WritingPhaseSeconds
            : Math.Max(0, WritingPhaseSeconds - (int)(now - session.ReadingPhaseEndedAt.Value).TotalSeconds);
        return new WritingMockSessionView(
            session.Id, session.MockId, scenarioId, session.Status,
            session.StartedAt, session.ReadingPhaseEndedAt, session.SubmittedAt,
            session.SubmissionId, readingRemaining, writingRemaining,
            session.IsPractice);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingMockListResponse> ListMocksAsync(string userId, CancellationToken ct)
    {
        var rows = await ListAsync(userId, ct);
        return new WritingMockListResponse(rows.Select(WritingV2ResponseMapper.ToResponse).ToList());
    }

    public async Task<WritingMockSessionResponse> StartMockAsync(string userId, WritingMockStartRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = await StartSessionAsync(userId, request.MockId, request.IsPractice, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingMockSessionResponse?> GetMockSessionAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var view = await GetSessionAsync(userId, sessionId, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingMockSessionResponse?> BeginMockWritingAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var exists = await db.WritingMockSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (!exists) return null;
        var view = await BeginWritingAsync(userId, sessionId, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingSubmissionResponse?> SubmitMockAsync(string userId, Guid sessionId, WritingMockSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await db.WritingMockSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return null;
        if (session.Status == "submitted")
        {
            if (session.SubmissionId is null) return null;
            var existing = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.SubmissionId && s.UserId == userId && s.Mode == "mock", ct);
            return existing is null ? null : WritingV2ResponseMapper.ToSubmissionResponse(existing);
        }
        if (session.Status != "writing")
        {
            throw ApiException.Validation("writing_mock_not_in_writing_phase", "Mock writing window has not started.");
        }
        var writingStartedAt = session.ReadingPhaseEndedAt ?? session.StartedAt.AddSeconds(ReadingPhaseSeconds);
        var now = clock.GetUtcNow();
        if (now > writingStartedAt.AddSeconds(WritingPhaseSeconds))
        {
            throw ApiException.Validation("writing_mock_writing_window_expired", "Mock writing window has expired.");
        }
        if (!await TryTransitionToGradingAsync(userId, sessionId, session, ct))
        {
            var current = await db.WritingMockSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (current?.Status == "submitted" && current.SubmissionId is { } submittedId)
            {
                var existing = await db.WritingSubmissions.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == submittedId && s.UserId == userId && s.Mode == "mock", ct);
                return existing is null ? null : WritingV2ResponseMapper.ToSubmissionResponse(existing);
            }
            throw ApiException.Validation("writing_mock_session_locked", "Mock session is already being submitted.");
        }
        var scenarioId = await db.WritingMocks.AsNoTracking().Where(m => m.Id == session.MockId).Select(m => m.ScenarioId).FirstOrDefaultAsync(ct);
        Guid submissionId;
        try
        {
            submissionId = await pipeline.CreateSubmissionAsync(new WritingSubmissionGradeContext(
                UserId: userId,
                ScenarioId: scenarioId,
                Mode: "mock",
                GradingTier: "express",
                InputSource: "typed",
                LetterContent: request.LetterContent,
                TimeSpentSeconds: Math.Clamp((int)(now - writingStartedAt).TotalSeconds, 0, WritingPhaseSeconds),
                StartedAt: writingStartedAt,
                IsRevision: false,
                OriginalSubmissionId: null), ct);

            // ── No AI for mock Writing ────────────────────────────────────────
            // Mock Writing is graded by a human examiner — NEVER by AI. We do not
            // call pipeline.EvaluateAsync (which would invoke the AI rubric and
            // write a WritingGrade band). Instead the submission is parked as
            // "awaiting human review" and routed to the tutor marking queue.
            var submission = await db.WritingSubmissions.FirstAsync(s => s.Id == submissionId, ct);
            submission.Status = WritingSubmissionStatuses.AwaitingReview;
            await db.SaveChangesAsync(ct);
            await tutorReview.EnsureMockReviewAssignmentAsync(userId, submissionId, ct);

            await SubmitSessionAsync(userId, sessionId, submissionId, ct);
        }
        catch
        {
            session.Status = "writing";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        var sub = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        return sub is null ? null : WritingV2ResponseMapper.ToSubmissionResponse(sub);
    }

    public async Task<WritingMockResultsResponse?> GetMockResultsAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        var session = await GetSessionAsync(userId, sessionId, ct);
        if (session is null || session.SubmissionId is null) return null;
        var submission = await db.WritingSubmissions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == session.SubmissionId && s.UserId == userId && s.Mode == "mock", ct);
        if (submission is null) return null;
        var grade = await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == submission.Id)
            .OrderByDescending(g => g.AppealedByGradeId != null || g.TutorReviewId != null)
            .ThenByDescending(g => g.GradedAt)
            .FirstOrDefaultAsync(ct);
        if (grade is null)
        {
            // Mock Writing awaiting human examiner marking — no AI band exists.
            // Return an explicit "awaiting_review" projection (not null) so the
            // results page can poll and show the pending-marking state.
            return new WritingMockResultsResponse(
                WritingV2ResponseMapper.ToResponse(session),
                Grade: null,
                Status: WritingSubmissionStatuses.AwaitingReview);
        }
        var violations = await db.WritingCanonViolations.AsNoTracking()
            .Where(v => v.SubmissionId == submission.Id)
            .ToListAsync(ct);
        var ruleText = await db.WritingCanonRules.AsNoTracking()
            .Where(r => violations.Select(v => v.RuleId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RuleText, ct);
        return new WritingMockResultsResponse(
            WritingV2ResponseMapper.ToResponse(session),
            WritingV2ResponseMapper.ToGradeResponse(grade, violations, ruleText));
    }
}
