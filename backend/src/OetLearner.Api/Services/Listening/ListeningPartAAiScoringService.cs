using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A — post-submit AI advisory review (Claude Sonnet 4.6).
//
// ADDITIVE + NON-BLOCKING. The deterministic grader (ListeningGradingService)
// stays the score of record. This service adds a SEPARATE per-gap AI judgement
// (lenient on paraphrase / word-form / spelling, the way a human OET marker is)
// onto each Part A fill-in-the-blank answer, surfaced to the learner review and
// the tutor checking flow. It runs after submit (via the background worker) and
// NEVER throws into the candidate submission path. The deterministic server mark
// remains authoritative; these fields are tutor-only advisory metadata.
//
// W0 (2026-08-27, incident INC-2026-CLAUDE-01) — cost/reliability repair:
//   • Approved/effective rationales are resolved BEFORE any provider resolution
//     or invocation. Zero eligible evidence is terminal `skipped_no_evidence`
//     with zero HTTP calls and no AiScoredAt.
//   • A locally valid but empty/unmatchable response is terminal too — replaying
//     identical evidence would only buy the same unusable answer again.
//   • Every physical call is durably counted. At most three attempts are ever
//     SCHEDULED per answer, honouring Retry-After or jittered backoff, and no
//     retry at all on auth/config/model rejections or an ambiguous outcome.
//     This bounds the loop; it is not yet a global exactly-once guarantee,
//     because the worker still runs in both API slots and two slots can race
//     the same answer. Cross-slot exactly-once arrives with W4 coordinator
//     database leasing.
//   • Raw provider error bodies are never persisted (they can echo candidate
//     text); only a sanitized error class is recorded.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningPartAAiScoringService
{
    /// <summary>Score every not-yet-AI-scored, not-yet-terminally-skipped Part A
    /// short-answer answer on a submitted attempt in one batched Claude call.
    /// Idempotent, best-effort, and cost-bounded: it makes at most one physical
    /// provider call per invocation and schedules at most
    /// <see cref="ListeningPartAAiRetryPolicy.MaxAttempts"/> attempts per answer.
    /// Concurrent callers (the worker runs in both API slots) are not yet
    /// serialised here — cross-slot exactly-once is supplied by the W4
    /// coordinator's database leasing.</summary>
    Task ScoreAttemptAsync(string attemptId, CancellationToken ct);
}

public sealed partial class ListeningPartAAiScoringService(
    LearnerDbContext db,
    IAiProviderRegistry registry,
    IHttpClientFactory httpClientFactory,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartAAiScoringService> logger) : IListeningPartAAiScoringService
{
    public const string AnthropicProviderCode = "anthropic";

    private sealed record GapItem(int Number, string Context, string UserAnswer, string Canonical, IReadOnlyList<string> Accepted, string ApprovedRationale);
    private sealed record Verdict(int Number, string? Verdict_, string? Rationale);

    private enum CallDisposition { Success, Retryable, Terminal }

    /// <summary>Classified result of one physical provider invocation. Carries no
    /// raw provider body — only a short, sanitized error class.</summary>
    private sealed record ProviderCallOutcome(
        CallDisposition Disposition,
        IReadOnlyList<Verdict> Verdicts,
        string ErrorClass,
        string? TerminalSkipReason,
        TimeSpan? RetryAfter)
    {
        public static ProviderCallOutcome Succeeded(IReadOnlyList<Verdict> verdicts)
            => new(CallDisposition.Success, verdicts, "ok", null, null);

        public static ProviderCallOutcome Retry(string errorClass, TimeSpan? retryAfter)
            => new(CallDisposition.Retryable, Array.Empty<Verdict>(), errorClass, null, retryAfter);

        public static ProviderCallOutcome Terminal(string errorClass, string skipReason)
            => new(CallDisposition.Terminal, Array.Empty<Verdict>(), errorClass, skipReason, null);
    }

    public async Task ScoreAttemptAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ListeningAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Status != ListeningAttemptStatus.Submitted) return;

        // AiScoredAt = "an AI verdict exists"; AiSkipReason = "terminally closed".
        // Both are idempotency guards, so neither class of row is reconsidered.
        var answers = await db.ListeningAnswers
            .Where(a => a.ListeningAttemptId == attemptId
                && a.AiScoredAt == null
                && a.AiSkipReason == null)
            .ToListAsync(ct);
        if (answers.Count == 0) return;

        var questionIds = answers.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questions = await db.ListeningQuestions
            .Where(q => questionIds.Contains(q.Id)
                && (q.QuestionType == ListeningQuestionType.ShortAnswer
                    || q.QuestionType == ListeningQuestionType.FillInBlank))
            .ToListAsync(ct);
        if (questions.Count == 0) return;
        var qById = questions.ToDictionary(q => q.Id);

        var partAAnswers = answers.Where(a => qById.ContainsKey(a.ListeningQuestionId)).ToList();
        if (partAAnswers.Count == 0) return;

        var now = clock.GetUtcNow();

        // Bounded durable attempts. The worker query already excludes capped and
        // future-scheduled rows; this is defence in depth because
        // ScoreAttemptAsync is a public entry point.
        var attemptsSpent = partAAnswers.Max(a => a.AiAttemptCount);
        if (attemptsSpent >= ListeningPartAAiRetryPolicy.MaxAttempts)
        {
            StampTerminal(partAAnswers, ListeningPartAAiSkipReasons.RetriesExhausted, attemptsSpent);
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Part A AI advisory review closed attempt {AttemptId} after {Attempts} durable attempts.",
                attemptId, attemptsSpent);
            return;
        }
        if (partAAnswers.Any(a => a.AiNextAttemptAt is { } next && next > now)) return;

        // ── Evidence resolution happens BEFORE any provider work ────────────────
        // This ordering is the incident fix: an attempt with no effective approved
        // rationale must never reach ResolveProviderAsync, let alone the network.
        var rationaleByQuestionId = await db.AssessmentRationales.AsNoTracking()
            .Where(r => r.Assessment == "listening"
                && r.Status == AssessmentGovernanceStatus.Effective
                && questionIds.Contains(r.QuestionRevisionId))
            .ToDictionaryAsync(r => r.QuestionRevisionId, r => r.RationaleText, ct);

        var extractIds = questions
            .Where(q => !string.IsNullOrEmpty(q.ListeningExtractId))
            .Select(q => q.ListeningExtractId!)
            .Distinct()
            .ToList();
        var notesByExtract = await db.ListeningExtracts
            .Where(e => extractIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.NotesBodyMarkdown ?? string.Empty, ct);

        var items = partAAnswers
            .Select(a => (a, q: qById[a.ListeningQuestionId]))
            .OrderBy(x => x.q.QuestionNumber)
            .Select(x => new GapItem(
                Number: x.q.QuestionNumber,
                Context: x.q.ListeningExtractId is { } eid && notesByExtract.TryGetValue(eid, out var nb) ? nb : string.Empty,
                UserAnswer: TryReadString(x.a.UserAnswerJson) ?? string.Empty,
                Canonical: TryReadString(x.q.CorrectAnswerJson) ?? string.Empty,
                Accepted: ParseAccepted(x.q.AcceptedSynonymsJson),
                ApprovedRationale: rationaleByQuestionId.GetValueOrDefault(x.q.Id, string.Empty)))
            .Where(x => !string.IsNullOrWhiteSpace(x.ApprovedRationale))
            .ToList();

        if (items.Count == 0)
        {
            // Terminal: zero eligible evidence => zero provider invocation, no
            // AiScoredAt, no retry. Approving a rationale later does NOT re-arm
            // this row by itself: re-running it is new, versioned work and is a
            // W5 coordinator requirement (see
            // docs/ops/postmortem-INC-2026-CLAUDE-01.md, follow-up 9). Clearing
            // AiSkipReason by hand is not a supported shortcut.
            StampTerminal(partAAnswers, ListeningPartAAiSkipReasons.NoEvidence, attemptsSpent);
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Part A AI advisory review skipped attempt {AttemptId}: no effective approved rationale for {Gaps} gaps (zero provider calls).",
                attemptId, partAAnswers.Count);
            return;
        }

        var provider = await ResolveProviderAsync(ct);
        if (provider is null)
        {
            // NOT terminal and NOT an attempt: nothing left the process and an
            // admin can still configure/rotate the platform credential. The
            // cool-off only stops the 20 s re-selection spin.
            var deferUntil = now + ListeningPartAAiRetryPolicy.UnconfiguredProviderCooldown;
            foreach (var a in partAAnswers) a.AiNextAttemptAt = deferUntil;
            await db.SaveChangesAsync(ct);
            logger.LogDebug(
                "Part A AI scoring deferred for attempt {AttemptId}: anthropic provider/key not configured.",
                attemptId);
            return;
        }

        var attemptNumber = attemptsSpent + 1;
        var outcome = await CallClaudeVerdictsAsync(items, attempt.UserId, provider, ct);

        if (outcome.Disposition != CallDisposition.Success)
        {
            ApplyFailureOutcome(partAAnswers, outcome, attemptNumber, now, attemptId);
            await db.SaveChangesAsync(ct);
            return;
        }

        var verdictByNumber = outcome.Verdicts
            .GroupBy(v => v.Number)
            .ToDictionary(g => g.Key, g => g.First());

        // The gaps that were actually in the prompt. A verdict is persisted ONLY
        // for a number in this set: a provider that answers about a gap it was
        // never given has no grounding evidence for it, so that answer is
        // discarded and the row is closed `skipped_no_evidence` — never stamped
        // with AiScoredAt.
        var eligibleNumbers = items.Select(i => i.Number).ToHashSet();

        var scored = 0;
        var closed = 0;
        foreach (var a in partAAnswers)
        {
            var q = qById[a.ListeningQuestionId];
            a.AiAttemptCount = attemptNumber;

            var isEligible = eligibleNumbers.Contains(q.QuestionNumber);
            if (isEligible && verdictByNumber.TryGetValue(q.QuestionNumber, out var v))
            {
                // Advisory only — IsCorrect / PointsEarned / MissReason untouched.
                a.AiVerdict = NormalizeVerdict(v.Verdict_);
                a.AiRationale = Truncate(v.Rationale ?? string.Empty, 1024);
                a.AiScoredAt = now;
                a.AiModel = provider.Model;
                a.AiNextAttemptAt = null;
                scored++;
                continue;
            }

            // The call succeeded but produced nothing usable for this gap (or
            // produced an out-of-prompt number we refuse to trust). Close it
            // terminally rather than re-queueing a second paid call for the
            // exact same evidence.
            a.AiSkipReason = isEligible
                ? ListeningPartAAiSkipReasons.NoMatchingVerdicts
                : ListeningPartAAiSkipReasons.NoEvidence;
            a.AiNextAttemptAt = null;
            closed++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Part A AI advisory review: stamped {Scored}, terminally closed {Closed} of {Total} answers on attempt {AttemptId} (attempt {AttemptNumber}/{MaxAttempts}).",
            scored, closed, partAAnswers.Count, attemptId, attemptNumber, ListeningPartAAiRetryPolicy.MaxAttempts);
    }

    // ── Terminal / retry bookkeeping ────────────────────────────────────────────

    /// <summary>Close answers permanently. <c>AiScoredAt</c> is deliberately left
    /// untouched (a skip is not an AI score) and no deterministic field is read
    /// or written.</summary>
    private static void StampTerminal(IEnumerable<ListeningAnswer> answers, string reason, int attemptCount)
    {
        foreach (var a in answers)
        {
            a.AiSkipReason = reason;
            a.AiAttemptCount = attemptCount;
            a.AiNextAttemptAt = null;
        }
    }

    private void ApplyFailureOutcome(
        List<ListeningAnswer> answers,
        ProviderCallOutcome outcome,
        int attemptNumber,
        DateTimeOffset now,
        string attemptId)
    {
        if (outcome.TerminalSkipReason is { } terminal)
        {
            StampTerminal(answers, terminal, attemptNumber);
            logger.LogError(
                "Part A AI advisory review terminally failed for attempt {AttemptId} ({ErrorClass}); marked {SkipReason}. No retry scheduled.",
                attemptId, outcome.ErrorClass, terminal);
            return;
        }

        if (attemptNumber >= ListeningPartAAiRetryPolicy.MaxAttempts)
        {
            StampTerminal(answers, ListeningPartAAiSkipReasons.RetriesExhausted, attemptNumber);
            logger.LogError(
                "Part A AI advisory review exhausted {MaxAttempts} attempts for attempt {AttemptId} ({ErrorClass}).",
                ListeningPartAAiRetryPolicy.MaxAttempts, attemptId, outcome.ErrorClass);
            return;
        }

        var delay = ListeningPartAAiRetryPolicy.NextDelay(attemptNumber, outcome.RetryAfter);
        var nextAttemptAt = now + delay;
        foreach (var a in answers)
        {
            a.AiAttemptCount = attemptNumber;
            a.AiNextAttemptAt = nextAttemptAt;
        }
        logger.LogWarning(
            "Part A AI advisory review attempt {AttemptNumber}/{MaxAttempts} failed for attempt {AttemptId} ({ErrorClass}); next attempt at {NextAttemptAt:o}.",
            attemptNumber, ListeningPartAAiRetryPolicy.MaxAttempts, attemptId, outcome.ErrorClass, nextAttemptAt);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string? TryReadString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(json);
            return el.ValueKind == JsonValueKind.String ? el.GetString() : json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static IReadOnlyList<string> ParseAccepted(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeVerdict(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "correct" => "correct",
            _ => "incorrect",
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
