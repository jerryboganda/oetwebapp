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
    Task SaveAnswerAsync(string userId, string attemptId, string questionId, string userAnswerJson, CancellationToken ct);
    Task<ReadingAttemptBreakState> ResumePartABreakAsync(string userId, string attemptId, CancellationToken ct);
    Task<ReadingGradingResult> SubmitAsync(string userId, string attemptId, CancellationToken ct);
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

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId && p.Status == ContentStatus.Published, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        if (!await CanLearnerSeePaperAsync(userId, paper, ct))
            throw new InvalidOperationException("Paper not found.");

        // Gate 0 (Phase 3): subscription / content entitlement. Throws
        // ApiException.PaymentRequired with code "content_locked" when the
        // learner's plan does not grant access to this paper. Free papers
        // (tag "access:free") and admins bypass automatically.
        await entitlements.RequireAccessAsync(userId, paper, ct);

        var policy = await policyService.ResolveForUserAsync(userId, ct);
        var globalPolicy = await policyService.GetGlobalAsync(ct);

        // Gate 1: archived paper
        if (paper.Status == ContentStatus.Archived && !globalPolicy.AllowAttemptOnArchivedPaper)
            throw new InvalidOperationException("This paper has been archived and cannot be attempted.");

        // Gate 2: user-level block
        var userOverride = await policyService.GetUserOverrideAsync(userId, ct);
        if (userOverride is { BlockAttempts: true })
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

        // Gate 5: paper has a validated Reading structure.
        var validation = await new ReadingStructureService(db).ValidatePaperAsync(paperId, ct);
        if (!validation.IsPublishReady)
            throw new ReadingAttemptException(
                "reading_structure_invalid",
                "Paper is not yet authored for Reading attempts.");

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

    public async Task SaveAnswerAsync(string userId, string attemptId, string questionId, string userAnswerJson, CancellationToken ct)
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
        var answerWindowDeadline = ResolveAnswerWindowDeadline(attempt, resolvedPolicy);
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

        var row = await db.ReadingAnswers.FirstOrDefaultAsync(
            a => a.ReadingAttemptId == attemptId && a.ReadingQuestionId == questionId, ct);
        var existingAnswerCount = await db.ReadingAnswers
            .CountAsync(a => a.ReadingAttemptId == attemptId, ct);
        var isNewAnswer = row is null;
        now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ReadingAnswer
            {
                Id = Guid.NewGuid().ToString("N"),
                ReadingAttemptId = attemptId,
                ReadingQuestionId = questionId,
                UserAnswerJson = userAnswerJson,
                AnsweredAt = now,
            };
            db.ReadingAnswers.Add(row);
        }
        else
        {
            row.UserAnswerJson = userAnswerJson;
            row.AnsweredAt = now;
            row.IsCorrect = null; // regrade on submit
            row.PointsEarned = 0;
        }

        attempt.LastActivityAt = now;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = userId,
            ActorName = userId,
            Action = "ReadingAnswerSaved",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"question={questionId}; answered={(isNewAnswer ? existingAnswerCount + 1 : existingAnswerCount)}",
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReadingGradingResult> SubmitAsync(string userId, string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        if (attempt.Status is ReadingAttemptStatus.Submitted)
        {
            // Idempotent — return existing grading.
            return await grader.GradeAttemptAsync(attemptId, ct);
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
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Attempt expired and was marked abandoned by policy.");
        }
        if (expired && resolved?.OnExpirySubmitPolicy == "keep_open_until_user_submits")
        {
            logger.LogInformation("Attempt {AttemptId} past deadline but policy allows late submit.", attemptId);
        }

        return await grader.GradeAttemptAsync(attemptId, ct);
    }

    private static DateTimeOffset ResolveAnswerWindowDeadline(
        ReadingAttempt attempt,
        ReadingResolvedPolicy policy)
    {
        if (attempt.Mode == ReadingAttemptMode.Exam)
        {
            return attempt.StartedAt
                .AddMinutes(policy.PartATimerMinutes + policy.PartBCTimerMinutes)
                .AddSeconds(Math.Clamp(attempt.PartBCPausedSeconds, 0, PartABreakMaxSeconds));
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

        foreach (var a in stale)
        {
            a.Status = policy.OnExpirySubmitPolicy == "auto_submit_graded"
                ? ReadingAttemptStatus.Expired
                : ReadingAttemptStatus.Abandoned;
            a.SubmittedAt = now;
        }
        await db.SaveChangesAsync(ct);

        // If we chose auto_submit_graded, grade each expired attempt now.
        if (policy.OnExpirySubmitPolicy == "auto_submit_graded")
        {
            foreach (var a in stale)
            {
                try { await grader.GradeAttemptAsync(a.Id, ct); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to grade auto-expired attempt {AttemptId}.", a.Id);
                }
            }
        }
        return stale.Count;
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
            AllowResumeAfterExpiry: false);
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
