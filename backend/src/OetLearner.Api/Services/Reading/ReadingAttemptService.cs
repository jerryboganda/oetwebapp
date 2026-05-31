using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Attempt Service — Slice R4
//
// Lifecycle:
//   Start   → ReadingAttempt row, InProgress, DeadlineAt set, policy snapshot.
//   Autosave → Upserts ReadingAnswer row for (attempt, question).
//   Submit  → Grading service runs; idempotent on replay.
//   Expire  → Background worker or submit-time check flips abandoned attempts.
//
// Policy (see READING-AUTHORING-POLICY.md):
//   - Per-paper attempt cap + cooldown enforced at Start.
//   - Multiple-concurrent-attempts gate enforced at Start.
//   - Policy snapshotted on the attempt so in-flight state is insulated.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingAttemptService
{
    Task<ReadingAttemptStarted> StartAsync(string userId, string paperId, CancellationToken ct);

    /// <summary>
    /// Phase 3: start an attempt in a non-exam mode (Learning / Drill /
    /// MiniTest / ErrorBank). <paramref name="scopeJson"/> is persisted
    /// verbatim on the attempt and must be valid JSON when provided.
    /// Caller is responsible for any question-subset prerequisites; this
    /// service only enforces the per-mode lifecycle rules.
    /// </summary>
    Task<ReadingAttemptStarted> StartInModeAsync(
        string userId,
        string paperId,
        ReadingAttemptMode mode,
        string? scopeJson,
        CancellationToken ct);

    Task<ReadingAttempt> GetAsync(string userId, string attemptId, CancellationToken ct);

    /// <summary>
    /// Autosave one answer.
    /// </summary>
    /// <param name="elapsedMs">Optional milliseconds the learner spent on
    /// this question between focus and save. Server caps at 14_400_000 ms
    /// (4 h). When supplied, the row's <c>ElapsedMs</c> is set to this
    /// value and <c>TotalElapsedMs</c> is incremented atomically. Pass
    /// <c>null</c> (the default) for legacy callers that do not yet
    /// capture timing.</param>
    Task SaveAnswerAsync(
        string userId,
        string attemptId,
        string questionId,
        string userAnswerJson,
        int? elapsedMs = null,
        CancellationToken ct = default);
    Task<ReadingAttemptBreakState> ResumePartABreakAsync(string userId, string attemptId, CancellationToken ct);

    /// <summary>
    /// Submit an attempt for grading. Idempotent: concurrent or replayed
    /// requests for the same (userId, attemptId) return the same
    /// <see cref="ReadingGradingResult"/> and never re-grade or
    /// double-write audit events. The optional
    /// <paramref name="idempotencyKey"/> is the caller-supplied
    /// <c>Idempotency-Key</c> header; when null a deterministic key is
    /// derived from the attempt id.
    /// </summary>
    Task<ReadingGradingResult> SubmitAsync(
        string userId,
        string attemptId,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    Task<int> SweepExpiredAsync(CancellationToken ct);
}

public sealed class ReadingAttemptException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed record ReadingAttemptStarted(
    string AttemptId,
    DateTimeOffset StartedAt,
    DateTimeOffset DeadlineAt,
    DateTimeOffset PartADeadlineAt,
    DateTimeOffset PartBCDeadlineAt,
    int AnsweredCount,
    bool CanResume,
    ReadingResolvedPolicy Policy,
    string PaperTitle,
    int PartATimerMinutes,
    int PartBCTimerMinutes,
    bool PartABreakAvailable,
    bool PartABreakResumed,
    DateTimeOffset? PartBCTimerPausedAt,
    int PartBCPausedSeconds,
    int PartABreakMaxSeconds);

public sealed record ReadingAttemptBreakState(
    string AttemptId,
    DateTimeOffset DeadlineAt,
    DateTimeOffset PartADeadlineAt,
    DateTimeOffset PartBCDeadlineAt,
    bool PartABreakAvailable,
    bool PartABreakResumed,
    DateTimeOffset? PartBCTimerPausedAt,
    int PartBCPausedSeconds,
    int PartABreakMaxSeconds);

public sealed class ReadingAttemptService(
    LearnerDbContext db,
    IReadingPolicyService policyService,
    IReadingGradingService grader,
    IContentEntitlementService entitlements,
    ILogger<ReadingAttemptService> logger) : IReadingAttemptService
{
    public const int PartABreakMaxSeconds = 600;
    private const int MaxIdempotencyRecordKeyLength = 128;
    private const string ReadingExamModeRulebookProfession = "_exam-mode";
    private const string FallbackReadingRulebookVersion = "1.0.0";

    public Task<ReadingAttemptStarted> StartAsync(string userId, string paperId, CancellationToken ct)
        => StartInModeAsync(userId, paperId, ReadingAttemptMode.Exam, scopeJson: null, ct);

    public async Task<ReadingAttemptStarted> StartInModeAsync(
        string userId,
        string paperId,
        ReadingAttemptMode mode,
        string? scopeJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required");

        if (!string.IsNullOrWhiteSpace(scopeJson))
        {
            try { using var _ = JsonDocument.Parse(scopeJson); }
            catch (JsonException)
            {
                throw new ReadingAttemptException("scope_json_invalid", "ScopeJson must be valid JSON.");
            }
        }

        if (IsSubsetMode(mode) && !HasNonEmptyQuestionScope(scopeJson))
        {
            throw new ReadingAttemptException(
                "scope_question_ids_required",
                "Subset practice attempts require at least one scoped question.");
        }

        var isPracticeMode = mode != ReadingAttemptMode.Exam;
        var globalPolicy = await policyService.GetGlobalAsync(ct);

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId
                && p.SubtestCode == "reading"
                && (p.Status == ContentStatus.Published
                    || (globalPolicy.AllowAttemptOnArchivedPaper && p.Status == ContentStatus.Archived)), ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (!await CanLearnerSeePaperAsync(userId, paper, ct))
            throw new InvalidOperationException("Paper not found.");

        // Gate 0 (Phase 3): subscription / content entitlement. Throws
        // ApiException.PaymentRequired with code "content_locked" when the
        // learner's plan does not grant access to this paper. Free papers
        // (tag "access:free") and admins bypass automatically.
        await entitlements.RequireAccessAsync(userId, paper, ct);

        var policy = await policyService.ResolveForUserAsync(userId, ct);

        // Gate 1: archived paper
        if (paper.Status == ContentStatus.Archived && !globalPolicy.AllowAttemptOnArchivedPaper)
            throw new InvalidOperationException("This paper has been archived and cannot be attempted.");

        // Gate 2: user-level block
        var userOverride = await policyService.GetUserOverrideAsync(userId, ct);
        if (IsActiveUserOverride(userOverride) && userOverride is { BlockAttempts: true })
            throw new InvalidOperationException(
                userOverride.Reason ?? "Your account is blocked from starting Reading attempts.");

        // Gate 3: max concurrent attempts. Practice modes are excluded —
        // a learner may run a Learning / Drill / MiniTest / ErrorBank
        // attempt alongside an in-flight Exam attempt without affecting it.
        if (!policy.AllowMultipleConcurrentAttempts && !isPracticeMode)
        {
            var openCount = await db.ReadingAttempts
                .CountAsync(a => a.UserId == userId
                    && a.Status == ReadingAttemptStatus.InProgress
                    && a.Mode == ReadingAttemptMode.Exam, ct);
            if (openCount > 0)
                throw new InvalidOperationException(
                    "You have another Reading attempt in progress. Submit or abandon it first.");
        }

        // Gate 4: per-paper attempt cap + cooldown. Skipped for practice
        // modes — drills and learning runs do not consume an exam attempt.
        if (policy.AttemptsPerPaperPerUser > 0 && !isPracticeMode)
        {
            var paperAttempts = await db.ReadingAttempts
                .Where(a => a.UserId == userId && a.PaperId == paperId
                    && a.Status != ReadingAttemptStatus.Abandoned)
                .OrderByDescending(a => a.StartedAt)
                .Take(policy.AttemptsPerPaperPerUser + 1)
                .ToListAsync(ct);
            if (paperAttempts.Count >= policy.AttemptsPerPaperPerUser)
            {
                throw new InvalidOperationException(
                    $"You have reached the attempt cap ({policy.AttemptsPerPaperPerUser}) for this paper.");
            }
            if (policy.AttemptCooldownMinutes > 0 && paperAttempts.Count > 0)
            {
                var last = paperAttempts[0].SubmittedAt ?? paperAttempts[0].StartedAt;
                var gap = DateTimeOffset.UtcNow - last;
                if (gap.TotalMinutes < policy.AttemptCooldownMinutes)
                {
                    throw new InvalidOperationException(
                        $"Please wait {policy.AttemptCooldownMinutes - Math.Floor(gap.TotalMinutes)} minute(s) before retrying.");
                }
            }
        }

        // Gate 5 DISABLED (product decision): any paper can be attempted,
        // regardless of structural publish-readiness. Re-enable by restoring
        // the ReadingStructureService.ValidatePaperAsync IsPublishReady check.

        var maxRaw = ReadingStructureService.CanonicalMaxRawScore;

        // Timer budget. Exam = canonical Part A + Part B+C. Learning =
        // generous default budget (no hard lock). Drill / MiniTest take
        // their minutes from ScopeJson when present.
        int totalMinutes;
        var miniTestMinutes = TryReadMinutesFromScope(scopeJson);
        switch (mode)
        {
            case ReadingAttemptMode.Learning:
                totalMinutes = Math.Max(60, policy.PartATimerMinutes + policy.PartBCTimerMinutes) * 4;
                break;
            case ReadingAttemptMode.MiniTest when miniTestMinutes is int m && m > 0:
                totalMinutes = m;
                break;
            case ReadingAttemptMode.Drill:
            case ReadingAttemptMode.ErrorBank:
                totalMinutes = miniTestMinutes ?? Math.Max(15, policy.PartATimerMinutes);
                break;
            default:
                totalMinutes = policy.PartATimerMinutes + policy.PartBCTimerMinutes;
                break;
        }

        var now = DateTimeOffset.UtcNow;
        var partADeadline = now.AddMinutes(
            mode == ReadingAttemptMode.Exam ? policy.PartATimerMinutes : totalMinutes);
        var partBCDeadline = now.AddMinutes(totalMinutes);
        var graceSeconds = Math.Max(0, policy.GracePeriodSeconds);
        var initialDeadline = mode == ReadingAttemptMode.Exam
            ? partBCDeadline.AddSeconds(PartABreakMaxSeconds + graceSeconds)
            : partBCDeadline.AddSeconds(graceSeconds);
        var rulebookVersion = await ResolveReadingRulebookVersionAsync(ct);
        var attempt = new ReadingAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PaperId = paperId,
            StartedAt = now,
            LastActivityAt = now,
            DeadlineAt = initialDeadline,
            PartBCTimerPausedAt = mode == ReadingAttemptMode.Exam ? partADeadline : null,
            PartBCPausedSeconds = 0,
            PartABreakUsed = mode != ReadingAttemptMode.Exam,
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = maxRaw,
            PolicySnapshotJson = JsonSerializer.Serialize(policy),
            PaperRevisionId = paper.PublishedRevisionId,
            RulebookVersion = rulebookVersion,
            Mode = mode,
            ScopeJson = scopeJson,
        };
        db.ReadingAttempts.Add(attempt);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now, ActorId = userId, ActorName = userId,
            Action = mode == ReadingAttemptMode.Exam
                ? "ReadingAttemptStarted"
                : $"ReadingAttemptStarted_{mode}",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"paper={paperId}; mode={mode}",
        });
        await db.SaveChangesAsync(ct);

        return new ReadingAttemptStarted(
            AttemptId: attempt.Id,
            StartedAt: attempt.StartedAt,
            DeadlineAt: attempt.DeadlineAt!.Value,
            PartADeadlineAt: partADeadline,
            PartBCDeadlineAt: partBCDeadline,
            AnsweredCount: 0,
            CanResume: true,
            Policy: policy,
            PaperTitle: paper.Title,
            PartATimerMinutes: mode == ReadingAttemptMode.Exam ? policy.PartATimerMinutes : totalMinutes,
            PartBCTimerMinutes: mode == ReadingAttemptMode.Exam ? policy.PartBCTimerMinutes : 0,
            PartABreakAvailable: mode == ReadingAttemptMode.Exam,
            PartABreakResumed: mode != ReadingAttemptMode.Exam,
            PartBCTimerPausedAt: attempt.PartBCTimerPausedAt,
            PartBCPausedSeconds: attempt.PartBCPausedSeconds,
            PartABreakMaxSeconds: mode == ReadingAttemptMode.Exam ? PartABreakMaxSeconds : 0);
    }

    private async Task<string> ResolveReadingRulebookVersionAsync(CancellationToken ct)
    {
        var version = await db.RulebookVersions.AsNoTracking()
            .Where(rulebook => rulebook.Kind == "reading"
                && rulebook.Profession == ReadingExamModeRulebookProfession
                && rulebook.Status == RulebookStatus.Published)
            .OrderByDescending(rulebook => rulebook.PublishedAt ?? rulebook.UpdatedAt)
            .Select(rulebook => rulebook.Version)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(version) ? FallbackReadingRulebookVersion : version;
    }

    private static int? TryReadMinutesFromScope(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (doc.RootElement.TryGetProperty("minutes", out var m)
                && m.ValueKind == JsonValueKind.Number
                && m.TryGetInt32(out var minutes)
                && minutes > 0 && minutes <= 240)
            {
                return minutes;
            }
        }
        catch (JsonException) { /* ignore — fall through */ }
        return null;
    }

    public Task<ReadingAttempt> GetAsync(string userId, string attemptId, CancellationToken ct)
        => db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            .ContinueWith(t => t.Result ?? throw new InvalidOperationException("Attempt not found."), ct);

    /// <summary>Server-side cap on per-save elapsed milliseconds. Defeats
    /// runaway clocks or hostile clients claiming hours per autosave.
    /// 4 hours = 14_400_000 ms.</summary>
    private const int MaxElapsedMsPerSave = 14_400_000;

    public async Task SaveAnswerAsync(
        string userId,
        string attemptId,
        string questionId,
        string userAnswerJson,
        int? elapsedMs = null,
        CancellationToken ct = default)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        if (attempt.Status != ReadingAttemptStatus.InProgress)
            throw new ReadingAttemptException(
                "attempt_not_in_progress",
                $"Cannot save to an attempt that is {attempt.Status}.");

        var now = DateTimeOffset.UtcNow;

        // Deadline respected (inclusive of grace period — DeadlineAt already
        // has it baked in).
        if (attempt.DeadlineAt is DateTimeOffset deadline && now > deadline)
        {
            // Auto-expire on next action — the grader handles idempotency.
            attempt.Status = ReadingAttemptStatus.Expired;
            attempt.LastActivityAt = now;
            attempt.RowVersion++;
            await db.SaveChangesAsync(ct);
            throw new ReadingAttemptException("attempt_deadline_passed", "Attempt deadline has passed.");
        }

        // Validate the question exists and belongs to the paper.
        var q = await db.ReadingQuestions
            .Include(x => x.Part)
            .FirstOrDefaultAsync(x => x.Id == questionId, ct)
            ?? throw new ReadingAttemptException("question_not_found", "Question not found.");
        var owningPaperId = await db.ReadingParts.AsNoTracking()
            .Where(p => p.Id == q.ReadingPartId)
            .Select(p => p.PaperId)
            .FirstOrDefaultAsync(ct);
        if (owningPaperId != attempt.PaperId)
            throw new ReadingAttemptException(
                "question_paper_mismatch",
                "Question does not belong to this attempt's paper.");

        if (IsSubsetMode(attempt.Mode)
            && !IsQuestionInScope(attempt.ScopeJson, questionId))
        {
            throw new ReadingAttemptException(
                "question_out_of_scope",
                "Question is not part of this practice attempt.");
        }

        var resolvedPolicy = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
        var partADeadline = ResolvePartADeadline(attempt, resolvedPolicy);
        var answerWindowDeadline = ResolveAnswerWindowDeadline(attempt, resolvedPolicy, now);
        if (now > answerWindowDeadline)
        {
            throw new ReadingAttemptException(
                "answer_window_closed",
                "The Reading answer window has ended. Submit grace only allows final grading.");
        }

        if (q.Part?.PartCode == ReadingPartCode.A
            && attempt.Mode == ReadingAttemptMode.Exam
            && string.Equals(resolvedPolicy.PartATimerStrictness, "hard_lock", StringComparison.OrdinalIgnoreCase)
            && now > partADeadline)
        {
            throw new ReadingAttemptException(
                "part_a_locked",
                "Part A is locked because the 15-minute window has ended.");
        }

        if (q.Part?.PartCode is ReadingPartCode.B or ReadingPartCode.C
            && attempt.Mode == ReadingAttemptMode.Exam)
        {
            if (now <= partADeadline)
            {
                throw new ReadingAttemptException(
                    "part_bc_not_open",
                    "Parts B and C are not available until the Part A window has ended.");
            }

            if (IsPartABreakPending(attempt, resolvedPolicy, now))
            {
                throw new ReadingAttemptException(
                    "part_bc_break_not_resumed",
                    "Resume the test before answering Parts B and C.");
            }
        }

        // Reject malformed JSON
        try { JsonDocument.Parse(userAnswerJson); }
        catch (JsonException)
        {
            throw new ReadingAttemptException(
                "answer_json_invalid",
                "UserAnswerJson must be valid JSON.");
        }

        now = DateTimeOffset.UtcNow;

        // Phase 1 closure — capture per-save and accumulated time-on-question.
        // Negative or absurd values are discarded silently (client clock skew,
        // hostile payload). Cap at MaxElapsedMsPerSave to bound write impact.
        int? sanitisedElapsedMs = null;
        if (elapsedMs is int e && e > 0)
        {
            sanitisedElapsedMs = Math.Min(e, MaxElapsedMsPerSave);
        }

        // P0-H 2026-05 hardening: TotalElapsedMs increment must be atomic at
        // DB level so two tabs autosaving the same question do not race and
        // lose increments. Strategy:
        //   1. Fast-path SELECT: if the (attempt, question) row already
        //      exists, jump straight to UPDATE. Saves a doomed INSERT on the
        //      common hot path and side-steps the EF Core in-memory
        //      provider's lack of unique-index enforcement (relational
        //      providers still close the race via the unique index).
        //   2. INSERT path: catch DbUpdateException for the case where a
        //      concurrent writer inserted between our SELECT and our INSERT,
        //      then fall through to the UPDATE path.
        //   3. UPDATE uses ExecuteUpdateAsync (SET col = col + @delta) so
        //      the accumulator is incremented by the database, not by EF
        //      change-tracking after a read.
        var existingAnswerCount = await db.ReadingAnswers
            .CountAsync(a => a.ReadingAttemptId == attemptId, ct);
        var existingRow = await db.ReadingAnswers
            .Where(a => a.ReadingAttemptId == attemptId && a.ReadingQuestionId == questionId)
            .Select(a => new { a.Id, a.UserAnswerJson })
            .FirstOrDefaultAsync(ct);
        var existingRowId = existingRow?.Id;
        var previousUserAnswerJson = existingRow?.UserAnswerJson;
        var isNewAnswer = false;

        // Wave 1 — record changed-answer history. A revision is appended when
        // the saved value differs from the currently-stored value (or there
        // was no prior value). Only InProgress attempts reach this point.
        var answerChanged = existingRowId is null
            || !string.Equals(previousUserAnswerJson, userAnswerJson, StringComparison.Ordinal);

        if (existingRowId is null)
        {
            try
            {
                var insertRow = new ReadingAnswer
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ReadingAttemptId = attemptId,
                    ReadingQuestionId = questionId,
                    UserAnswerJson = userAnswerJson,
                    AnsweredAt = now,
                    ElapsedMs = sanitisedElapsedMs,
                    TotalElapsedMs = sanitisedElapsedMs,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.ReadingAnswers.Add(insertRow);
                await db.SaveChangesAsync(ct);
                isNewAnswer = true;
            }
            catch (DbUpdateException)
            {
                // Concurrent insert beat us. Fall through to UPDATE.
                db.ChangeTracker.Clear();
                existingRowId = await db.ReadingAnswers
                    .Where(a => a.ReadingAttemptId == attemptId && a.ReadingQuestionId == questionId)
                    .Select(a => a.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (!isNewAnswer && existingRowId is not null)
        {
            var deltaSql = sanitisedElapsedMs ?? 0;

            // ExecuteUpdateAsync is the atomic, race-safe path on Postgres
            // (and every other relational provider). The EF Core in-memory
            // provider does NOT support it, so we fall back to a tracked
            // load + save there. In-memory tests don't exercise concurrent
            // writes anyway, so the lack of atomicity is acceptable in that
            // path; production is on Postgres.
            if (db.Database.IsRelational())
            {
                var rowsAffected = await db.ReadingAnswers
                    .Where(a => a.ReadingAttemptId == attemptId && a.ReadingQuestionId == questionId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.UserAnswerJson, _ => userAnswerJson)
                        .SetProperty(a => a.AnsweredAt, _ => now)
                        .SetProperty(a => a.UpdatedAt, _ => now)
                        .SetProperty(a => a.IsCorrect, _ => (bool?)null)
                        .SetProperty(a => a.PointsEarned, _ => 0)
                        .SetProperty(a => a.ElapsedMs, _ => sanitisedElapsedMs)
                        .SetProperty(a => a.TotalElapsedMs, a => (a.TotalElapsedMs ?? 0) + deltaSql),
                        ct);
                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException(
                        "Failed to persist Reading answer (concurrent delete?).");
                }
            }
            else
            {
                var tracked = await db.ReadingAnswers.FirstOrDefaultAsync(
                    a => a.ReadingAttemptId == attemptId && a.ReadingQuestionId == questionId, ct)
                    ?? throw new InvalidOperationException(
                        "Failed to persist Reading answer (concurrent delete?).");
                tracked.UserAnswerJson = userAnswerJson;
                tracked.AnsweredAt = now;
                tracked.UpdatedAt = now;
                tracked.IsCorrect = null;
                tracked.PointsEarned = 0;
                if (sanitisedElapsedMs is int delta)
                {
                    tracked.ElapsedMs = delta;
                    tracked.TotalElapsedMs = (tracked.TotalElapsedMs ?? 0) + delta;
                }
            }
        }

        // Update attempt.LastActivityAt + write audit log in a fresh save so
        // it does not get tangled with the answer write path above.
        attempt.LastActivityAt = now;
        attempt.RowVersion++;

        // Wave 1 — append a changed-answer revision row when the value moved.
        // attempt.Status is guaranteed InProgress here (asserted at entry).
        if (answerChanged)
        {
            db.ReadingAnswerRevisions.Add(new ReadingAnswerRevision
            {
                Id = Guid.NewGuid().ToString("N"),
                ReadingAttemptId = attemptId,
                ReadingQuestionId = questionId,
                UserAnswerJson = userAnswerJson,
                RecordedAt = now,
            });
        }

        var elapsedDetail = sanitisedElapsedMs is int dm ? $"; elapsedMs={dm}" : string.Empty;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "ReadingAnswerSaved",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"question={questionId}; answered={(isNewAnswer ? existingAnswerCount + 1 : existingAnswerCount)}{elapsedDetail}",
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Phase 1 closure — kebab-case scope for the
    /// IdempotencyRecord rows that cover Reading attempt submission.</summary>
    private const string SubmitIdempotencyScope = "reading-submit";

    public async Task<ReadingGradingResult> SubmitAsync(
        string userId,
        string attemptId,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        // Build the replay key only after ownership is proven. Client keys
        // are namespaced by user + attempt so a guessed/reused header cannot
        // return another learner's cached grading result.
        var key = BuildSubmitIdempotencyKey(userId, attemptId, idempotencyKey);

        // Fast path — cached grading result from a previous successful submit.
        var existingRecord = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == SubmitIdempotencyScope && r.Key == key, ct);
        if (existingRecord is not null)
        {
            var cached = TryDeserializeGradingResult(existingRecord.ResponseJson);
            if (cached is not null) return cached;
            // Fall through to re-grade if cached JSON is unreadable; this
            // can only happen after a forward-incompatible schema change.
            logger.LogWarning(
                "Reading submit idempotency record {Key} unreadable; re-grading.", key);
        }

        if (attempt.Status is ReadingAttemptStatus.Submitted)
        {
            // Already submitted but no idempotency row (pre-Phase-1 attempts).
            // Backfill the row from a fresh grading so future replays hit cache.
            var graded = await grader.GradeAttemptAsync(attemptId, ct);
            await TryPersistSubmitIdempotencyAsync(key, graded, ct);
            return graded;
        }

        if (attempt.Status is ReadingAttemptStatus.Abandoned)
            throw new InvalidOperationException("This attempt was abandoned and cannot be submitted.");

        var resolvedPolicyForBreak = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
        if (IsPartABreakPending(attempt, resolvedPolicyForBreak, DateTimeOffset.UtcNow))
        {
            throw new ReadingAttemptException(
                "part_bc_break_not_resumed",
                "Resume the test before submitting the Reading attempt.");
        }

        // Expired? OnExpirySubmitPolicy decides.
        var resolved = JsonSerializer.Deserialize<ReadingResolvedPolicy>(attempt.PolicySnapshotJson);
        var expired = attempt.DeadlineAt is DateTimeOffset dl && DateTimeOffset.UtcNow > dl;
        if (expired && resolved?.OnExpirySubmitPolicy == "auto_submit_abandoned")
        {
            attempt.Status = ReadingAttemptStatus.Abandoned;
            attempt.SubmittedAt = DateTimeOffset.UtcNow;
            attempt.RowVersion++;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Attempt expired and was marked abandoned by policy.");
        }
        if (expired && resolved?.OnExpirySubmitPolicy == "keep_open_until_user_submits")
        {
            logger.LogInformation("Attempt {AttemptId} past deadline but policy allows late submit.", attemptId);
        }

        var result = await grader.GradeAttemptAsync(attemptId, ct);

        // Wave 2 — best-effort: fulfil any open assignment that targets this
        // paper for this learner. Never block submit on failure.
        await TryCompleteMatchingAssignmentsAsync(userId, attempt, ct);

        // Persist the cache row AFTER grading succeeded so transient failures
        // do not lock us out of retrying. On unique-index collision (two
        // concurrent submits raced), prefer the row already written by the
        // peer and return its cached result.
        var cachedAfterRace = await TryPersistSubmitIdempotencyAsync(key, result, ct);
        return cachedAfterRace ?? result;
    }

    /// <summary>
    /// Wave 2 — when a learner submits an attempt matching an open Reading
    /// assignment (same paper, still <c>assigned</c>), mark the assignment
    /// <c>completed</c> and record the fulfilling attempt. Best-effort: any
    /// failure is logged and swallowed so it never blocks the submit.
    /// </summary>
    private async Task TryCompleteMatchingAssignmentsAsync(
        string userId, ReadingAttempt attempt, CancellationToken ct)
    {
        try
        {
            var open = await db.ReadingAssignments
                .Where(x => x.AssignedToUserId == userId
                    && x.PaperId == attempt.PaperId
                    && x.Status == "assigned")
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            if (open.Count == 0) return;

            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in open.Where(assignment => AssignmentMatchesAttempt(assignment, attempt)))
            {
                assignment.Status = "completed";
                assignment.CompletedAttemptId = attempt.Id;
                assignment.UpdatedAt = now;
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = now,
                    ActorId = userId,
                    ActorName = userId,
                    Action = "ReadingAssignmentCompleted",
                    ResourceType = "ReadingAssignment",
                    ResourceId = assignment.Id,
                    Details = $"attemptId={attempt.Id} paperId={attempt.PaperId}",
                });
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Reading assignment auto-completion failed for attempt {AttemptId}; ignoring.", attempt.Id);
        }
    }

    private static bool AssignmentMatchesAttempt(ReadingAssignment assignment, ReadingAttempt attempt)
    {
        var kind = (assignment.Kind ?? "full").Trim().ToLowerInvariant();
        if (kind is "full" or "exam" or "retake")
            return attempt.Mode == ReadingAttemptMode.Exam;

        if (kind is "learning") return attempt.Mode == ReadingAttemptMode.Learning;
        if (kind is "mini-test" or "minitest") return attempt.Mode == ReadingAttemptMode.MiniTest;
        if (kind is "error-bank" or "errorbank") return attempt.Mode == ReadingAttemptMode.ErrorBank;
        if (kind is "drill") return attempt.Mode == ReadingAttemptMode.Drill;

        if (kind is "part" or "part-practice")
        {
            return attempt.Mode == ReadingAttemptMode.Drill
                && string.Equals(
                    TryReadPartCode(assignment.ScopeJson),
                    TryReadPartCode(attempt.ScopeJson),
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? TryReadPartCode(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            return doc.RootElement.TryGetProperty("partCode", out var part)
                && part.ValueKind == JsonValueKind.String
                ? part.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ReadingGradingResult?> TryPersistSubmitIdempotencyAsync(
        string key,
        ReadingGradingResult result,
        CancellationToken ct)
    {
        var record = new IdempotencyRecord
        {
            Id = $"idem-{Guid.NewGuid():N}",
            Scope = SubmitIdempotencyScope,
            Key = key,
            ResponseJson = JsonSerializer.Serialize(result),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            // Concurrent submit beat us to the unique (Scope, Key) row.
            db.Entry(record).State = EntityState.Detached;
            var winner = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Scope == SubmitIdempotencyScope && r.Key == key, ct);
            return winner is null ? null : TryDeserializeGradingResult(winner.ResponseJson);
        }
    }

    private static ReadingGradingResult? TryDeserializeGradingResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ReadingGradingResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildSubmitIdempotencyKey(
        string userId,
        string attemptId,
        string? callerSuppliedKey)
    {
        var suffix = string.IsNullOrWhiteSpace(callerSuppliedKey)
            ? "default"
            : callerSuppliedKey.Trim();
        var rawKey = $"{userId}:{attemptId}:{suffix}";
        if (rawKey.Length <= MaxIdempotencyRecordKeyLength)
            return rawKey;

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        var scopedDigestKey = $"{userId}:{attemptId}:sha256:{digest}";
        return scopedDigestKey.Length <= MaxIdempotencyRecordKeyLength
            ? scopedDigestKey
            : $"sha256:{digest}";
    }

    private static bool IsActiveUserOverride(ReadingUserPolicyOverride? userOverride)
        => userOverride is not null
            && (userOverride.ExpiresAt is null || userOverride.ExpiresAt > DateTimeOffset.UtcNow);

    private static DateTimeOffset ResolveAnswerWindowDeadline(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy,
        DateTimeOffset now)
    {
        if (attempt.Mode == ReadingAttemptMode.Exam)
        {
            return attempt.StartedAt
                .AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes)
                .AddSeconds(ResolveEffectivePartBCPausedSeconds(attempt, policy, now));
        }

        if (attempt.DeadlineAt is DateTimeOffset deadline)
        {
            return deadline.AddSeconds(-Math.Max(0, policy.GracePeriodSeconds));
        }

        var minutes = TryReadMinutesFromScope(attempt.ScopeJson)
            ?? (attempt.Mode == ReadingAttemptMode.Learning
                ? Math.Max(60, policy.PartATimerMinutes + policy.PartBCTimerMinutes) * 4
                : policy.PartATimerMinutes + policy.PartBCTimerMinutes);
        return attempt.StartedAt.AddMinutes(minutes);
    }

    private static bool IsSubsetMode(ReadingAttemptMode mode)
        => mode is ReadingAttemptMode.Drill or ReadingAttemptMode.MiniTest or ReadingAttemptMode.ErrorBank;

    private static bool HasNonEmptyQuestionScope(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var questionIds)
                || questionIds.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return questionIds.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsQuestionInScope(string? scopeJson, string questionId)
    {
        if (string.IsNullOrWhiteSpace(scopeJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(scopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var questionIds)
                || questionIds.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return questionIds.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String
                && string.Equals(item.GetString(), questionId, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<ReadingAttemptBreakState> ResumePartABreakAsync(string userId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        if (attempt.Status != ReadingAttemptStatus.InProgress)
        {
            throw new ReadingAttemptException(
                "attempt_not_in_progress",
                $"Cannot resume a break for an attempt that is {attempt.Status}.");
        }

        if (attempt.Mode != ReadingAttemptMode.Exam)
        {
            throw new ReadingAttemptException(
                "optional_break_unavailable",
                "The optional Part A break is only available in Exam mode.");
        }

        if (attempt.PartABreakUsed)
        {
            throw new ReadingAttemptException(
                "optional_break_already_used",
                "The optional Part A break has already been resumed.");
        }

        var policy = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
        var now = DateTimeOffset.UtcNow;
        var partADeadline = ResolvePartADeadline(attempt, policy);
        if (now < partADeadline)
        {
            throw new ReadingAttemptException(
                "optional_break_unavailable",
                "The optional break is available only after Part A has ended.");
        }

        var pausedSeconds = Math.Clamp((int)Math.Floor((now - partADeadline).TotalSeconds), 0, PartABreakMaxSeconds);
        var partBCDeadline = attempt.StartedAt
            .AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes)
            .AddSeconds(pausedSeconds);
        var deadline = partBCDeadline.AddSeconds(Math.Max(0, policy.GracePeriodSeconds));
        if (now > deadline)
        {
            attempt.Status = ReadingAttemptStatus.Expired;
            attempt.LastActivityAt = now;
            attempt.RowVersion++;
            await db.SaveChangesAsync(ct);
            throw new ReadingAttemptException(
                "attempt_deadline_passed",
                "The Reading answer window has ended.");
        }

        attempt.PartABreakUsed = true;
        attempt.PartBCPausedSeconds = pausedSeconds;
        attempt.PartBCTimerPausedAt = null;
        attempt.DeadlineAt = deadline;
        attempt.LastActivityAt = now;
        attempt.RowVersion++;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "ReadingPartABreakResumed",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"pausedSeconds={pausedSeconds}",
        });
        await db.SaveChangesAsync(ct);

        return new ReadingAttemptBreakState(
            AttemptId: attempt.Id,
            DeadlineAt: deadline,
            PartADeadlineAt: partADeadline,
            PartBCDeadlineAt: partBCDeadline,
            PartABreakAvailable: true,
            PartABreakResumed: true,
            PartBCTimerPausedAt: null,
            PartBCPausedSeconds: pausedSeconds,
            PartABreakMaxSeconds: PartABreakMaxSeconds);
    }

    public async Task<int> SweepExpiredAsync(CancellationToken ct)
    {
        var policy = await policyService.GetGlobalAsync(ct);
        if (!policy.AutoExpireWorkerEnabled) return 0;

        var now = DateTimeOffset.UtcNow;
        var inactivityCutoff = now.AddMinutes(-policy.AutoExpireAfterMinutes);
        var candidates = await db.ReadingAttempts
            .Where(a => a.Status == ReadingAttemptStatus.InProgress)
            .ToListAsync(ct);
        var stale = candidates
            .Where(a => a.DeadlineAt <= now || a.LastActivityAt <= inactivityCutoff)
            .Take(500)
            .ToList();
        if (stale.Count == 0) return 0;

        var expiredCount = 0;
        foreach (var a in stale)
        {
            a.Status = policy.OnExpirySubmitPolicy == "auto_submit_graded"
                ? ReadingAttemptStatus.Expired
                : ReadingAttemptStatus.Abandoned;
            a.SubmittedAt = now;
            a.RowVersion++;

            try
            {
                await db.SaveChangesAsync(ct);
                expiredCount++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent autosave modified this attempt since we read it.
                // The learner is still active — skip and let the next sweep
                // re-evaluate. Detach the stale entity so the context stays clean.
                db.Entry(a).State = EntityState.Detached;
                logger.LogInformation(
                    "ReadingAttemptExpireWorker skipped attempt {AttemptId} due to concurrent modification.",
                    a.Id);
            }
        }

        // If we chose auto_submit_graded, grade each expired attempt now.
        if (policy.OnExpirySubmitPolicy == "auto_submit_graded")
        {
            foreach (var a in stale.Where(a => a.Status == ReadingAttemptStatus.Expired))
            {
                try { await grader.GradeAttemptAsync(a.Id, ct); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to grade auto-expired attempt {AttemptId}.", a.Id);
                }
            }
        }
        return expiredCount;
    }

    private static ReadingResolvedPolicy ResolvePolicySnapshot(string json)
    {
        try
        {
            var policy = JsonSerializer.Deserialize<ReadingResolvedPolicy>(json);
            if (policy is not null) return policy;
        }
        catch (JsonException)
        {
            // Fall back to the safe defaults below.
        }

        return new ReadingResolvedPolicy(
            AttemptsPerPaperPerUser: 0,
            AttemptCooldownMinutes: 0,
            PartATimerStrictness: "hard_lock",
            PartATimerMinutes: 15,
            PartBCTimerMinutes: 45,
            GracePeriodSeconds: 10,
            OnExpirySubmitPolicy: "auto_submit_graded",
            CountdownWarnings: new[] { 300, 60, 15 },
            EnabledQuestionTypes: new[]
            {
                nameof(ReadingQuestionType.MatchingTextReference),
                nameof(ReadingQuestionType.ShortAnswer),
                nameof(ReadingQuestionType.SentenceCompletion),
                nameof(ReadingQuestionType.MultipleChoice3),
                nameof(ReadingQuestionType.MultipleChoice4),
            },
            ShortAnswerNormalisation: "trim_only",
            // OET-faithful default. Synonym acceptance is non-standard mode.
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: false,
            UnknownTypeFallbackPolicy: "skip_with_zero",
            ShowExplanationsAfterSubmit: false,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: false,
            SubmitRateLimitPerMinute: 5,
            AutosaveRateLimitPerMinute: 120,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false,
            AllowPaperReadingMode: false);
    }

    private static DateTimeOffset ResolvePartADeadline(ReadingAttempt attempt, ReadingResolvedPolicy policy)
        => attempt.StartedAt.AddMinutes(policy.PartATimerMinutes);

    private static bool IsPartABreakPending(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy,
        DateTimeOffset now)
    {
        if (attempt.Mode != ReadingAttemptMode.Exam || attempt.PartABreakUsed)
        {
            return false;
        }

        var partADeadline = ResolvePartADeadline(attempt, policy);
        var breakWindowEndsAt = partADeadline.AddSeconds(PartABreakMaxSeconds);
        if (attempt.DeadlineAt is DateTimeOffset deadline && deadline < breakWindowEndsAt)
        {
            breakWindowEndsAt = deadline;
        }

        return now >= partADeadline && now < breakWindowEndsAt;
    }

    private static int ResolveEffectivePartBCPausedSeconds(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy,
        DateTimeOffset now)
    {
        var persisted = Math.Clamp(attempt.PartBCPausedSeconds, 0, PartABreakMaxSeconds);
        if (attempt.Mode != ReadingAttemptMode.Exam || attempt.PartABreakUsed)
        {
            return persisted;
        }

        var elapsedBreakSeconds = (int)Math.Floor((now - ResolvePartADeadline(attempt, policy)).TotalSeconds);
        return Math.Clamp(Math.Max(persisted, elapsedBreakSeconds), 0, PartABreakMaxSeconds);
    }

    private async Task<bool> CanLearnerSeePaperAsync(string userId, ContentPaper paper, CancellationToken ct)
    {
        if (paper.AppliesToAllProfessions)
        {
            return true;
        }

        var profession = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.ActiveProfessionId)
            .SingleOrDefaultAsync(ct);

        return !string.IsNullOrWhiteSpace(profession)
            && string.Equals(paper.ProfessionId, profession, StringComparison.OrdinalIgnoreCase);
    }
}
