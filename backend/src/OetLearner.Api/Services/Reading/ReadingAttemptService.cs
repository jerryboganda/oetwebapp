using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

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
    Task<ReadingAttempt> GetAsync(string userId, string attemptId, CancellationToken ct);
    Task SaveAnswerAsync(string userId, string attemptId, string questionId, string userAnswerJson, CancellationToken ct);
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
    int PartBCTimerMinutes);

public sealed class ReadingAttemptService(
    LearnerDbContext db,
    IReadingPolicyService policyService,
    IReadingGradingService grader,
    ILogger<ReadingAttemptService> logger) : IReadingAttemptService
{
    public async Task<ReadingAttemptStarted> StartAsync(string userId, string paperId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required");

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

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

        // Gate 3: max concurrent attempts
        if (!policy.AllowMultipleConcurrentAttempts)
        {
            var openCount = await db.ReadingAttempts
                .CountAsync(a => a.UserId == userId && a.Status == ReadingAttemptStatus.InProgress, ct);
            if (openCount > 0)
                throw new InvalidOperationException(
                    "You have another Reading attempt in progress. Submit or abandon it first.");
        }

        // Gate 4: per-paper attempt cap + cooldown
        if (policy.AttemptsPerPaperPerUser > 0)
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

        // Timer budget: Part A minutes + Part B+C shared minutes
        var totalMinutes = policy.PartATimerMinutes + policy.PartBCTimerMinutes;
        var now = DateTimeOffset.UtcNow;
        var partADeadline = now.AddMinutes(policy.PartATimerMinutes);
        var partBCDeadline = now.AddMinutes(totalMinutes);
        var attempt = new ReadingAttempt
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PaperId = paperId,
            StartedAt = now,
            LastActivityAt = now,
            DeadlineAt = partBCDeadline.AddSeconds(policy.GracePeriodSeconds),
            Status = ReadingAttemptStatus.InProgress,
            MaxRawScore = maxRaw,
            PolicySnapshotJson = JsonSerializer.Serialize(policy),
            PaperRevisionId = paper.PublishedRevisionId,
        };
        db.ReadingAttempts.Add(attempt);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now, ActorId = userId, ActorName = userId,
            Action = "ReadingAttemptStarted",
            ResourceType = "ReadingAttempt",
            ResourceId = attempt.Id,
            Details = $"paper={paperId}",
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
            PartATimerMinutes: policy.PartATimerMinutes,
            PartBCTimerMinutes: policy.PartBCTimerMinutes);
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

        // Deadline respected (inclusive of grace period — DeadlineAt already
        // has it baked in).
        if (attempt.DeadlineAt is DateTimeOffset deadline && DateTimeOffset.UtcNow > deadline)
        {
            // Auto-expire on next action — the grader handles idempotency.
            attempt.Status = ReadingAttemptStatus.Expired;
            attempt.LastActivityAt = DateTimeOffset.UtcNow;
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

        var resolvedPolicy = ResolvePolicySnapshot(attempt.PolicySnapshotJson);
        if (q.Part?.PartCode == ReadingPartCode.A
            && string.Equals(resolvedPolicy.PartATimerStrictness, "hard_lock", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.UtcNow > attempt.StartedAt.AddMinutes(resolvedPolicy.PartATimerMinutes))
        {
            throw new ReadingAttemptException(
                "part_a_locked",
                "Part A is locked because the 15-minute window has ended.");
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
        var now = DateTimeOffset.UtcNow;
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
            ShortAnswerNormalisation: "trim_collapse_case_insensitive",
            ShortAnswerAcceptSynonyms: true,
            MatchingAllowPartialCredit: true,
            UnknownTypeFallbackPolicy: "skip_with_zero",
            ShowExplanationsAfterSubmit: true,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: true,
            SubmitRateLimitPerMinute: 5,
            AutosaveRateLimitPerMinute: 120,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false);
    }
}
