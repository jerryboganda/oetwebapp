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
    IAiFeatureRouteResolver routeResolver,
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.AiProviderOptions> providerOptions,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartAAiScoringService> logger,
    IAiPricingResolver? pricingResolver = null) : IListeningPartAAiScoringService
{
    public const string AnthropicProviderCode = "anthropic";
    // UBAG route: mirrors the extraction services — an admin ubag route
    // sends the same gap evidence to the facade with response_format
    // json_object + forced-tool emulation.
    public const string UbagProviderCode = "ubag";

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
        /// <summary>Id of the usage row this physical invocation persisted, or
        /// null when the fail-soft recorder could not commit it. Only a non-null
        /// value may be stamped as the operation's <c>ResultRef</c>.</summary>
        public string? UsageRecordId { get; init; }

        public static ProviderCallOutcome Succeeded(IReadOnlyList<Verdict> verdicts, string? usageRecordId)
            => new(CallDisposition.Success, verdicts, "ok", null, null) { UsageRecordId = usageRecordId };

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

        var attemptNumber = attemptsSpent + 1;

        // ── W2: durable operation BEFORE provider resolution and send ───────────
        // The lease is what makes "one physical call per durable attempt" true
        // across API slots: the operation's resource slot is UNIQUE-indexed on
        // (feature, module, learner, attempt, attemptNumber), so two slots that
        // both read the same pre-increment AiAttemptCount cannot both send. The
        // loser never touches the network, so it can never be billed. It is
        // taken before ResolveProviderAsync so a refused policy cannot even
        // reach credential resolution.
        var lease = await usageRecorder.BeginOperationAsync(new DirectAiOperationRequest
        {
            FeatureCode = AiFeatureCodes.ListeningPartAScore,
            Module = "listening",
            UserId = attempt.UserId,
            ResourceId = attemptId,
            ResourceType = "listening_attempt",
            ResourceVersion = attemptNumber,
            RequestHash = BuildEvidenceHash(items),
            PromptVersion = ToolName,
            ModelRoute = AnthropicProviderCode,
            OperationClass = AiOperationClass.ScoringCritical,
        }, ct);

        if (!lease.CanProceed)
        {
            ApplyDeniedLease(partAAnswers, lease, attemptsSpent, now, attemptId);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Every exit path below owns the lease, so every exit path must
        // reconcile it — a Leased row that is never closed blocks every future
        // authorized attempt at this resource version (see
        // DirectAiOperationReconciler). The explicit branches inside
        // RunLeasedScoringAsync already close the lease themselves on every
        // outcome they know about (unconfigured provider, retryable/terminal
        // failure, success); this wrapper is the safety net for anything that
        // escapes as an exception instead (a save failure, an unexpected
        // parse/transport error) so it can never be left dangling as
        // permanently Leased. The original exception is always re-thrown.
        await DirectAiOperationReconciler.RunAsync(
            usageRecorder, lease, AnthropicProviderCode,
            async () =>
            {
                await RunLeasedScoringAsync(attempt, partAAnswers, qById, items, attemptId, attemptNumber, lease, now, ct);
                return true;
            },
            ct);
    }

    private async Task RunLeasedScoringAsync(
        ListeningAttempt attempt,
        List<ListeningAnswer> partAAnswers,
        Dictionary<string, ListeningQuestion> qById,
        List<GapItem> items,
        string attemptId,
        int attemptNumber,
        DirectAiOperationLease lease,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var provider = await ResolveProviderAsync(ct);
        // Route-aware: an admin ubag route for listening.parta.score diverts
        // to the facade path below (usage recorded against ubag). Otherwise
        // the Anthropic path runs byte-identical to before.
        var ubagRoute = await routeResolver.ResolveAsync(AiFeatureCodes.ListeningPartAScore, ct);
        var useUbag = ubagRoute is not null
            && string.Equals(ubagRoute.ProviderCode, UbagProviderCode, StringComparison.OrdinalIgnoreCase);
        if (provider is null && !useUbag)
        {
            // NOT terminal and NOT an attempt: nothing left the process and an
            // admin can still configure/rotate the platform credential. The
            // cool-off only stops the 20 s re-selection spin.
            var deferUntil = now + ListeningPartAAiRetryPolicy.UnconfiguredProviderCooldown;
            foreach (var a in partAAnswers) a.AiNextAttemptAt = deferUntil;
            await db.SaveChangesAsync(ct);
            await usageRecorder.CompleteOperationAsync(
                lease.OperationId!, AiOperationState.Cancelled, null, AnthropicProviderCode, null, CancellationToken.None,
                lease.BudgetReservation);
            logger.LogDebug(
                "Part A AI scoring deferred for attempt {AttemptId}: anthropic provider/key not configured.",
                attemptId);
            return;
        }

        var outcome = useUbag
            ? await CallUbagVerdictsAsync(items, attempt.UserId, ubagRoute!.Model, lease, ct)
            : await CallClaudeVerdictsAsync(items, attempt.UserId, provider!, lease, ct);
        var servingProviderCode = useUbag ? UbagProviderCode : AnthropicProviderCode;
        var servingModel = useUbag ? (ubagRoute!.Model ?? "chatgpt_web") : provider!.Model;

        if (outcome.Disposition != CallDisposition.Success)
        {
            ApplyFailureOutcome(partAAnswers, outcome, attemptNumber, now, attemptId);
            // The advisory outcome is durable before the operation is closed, so
            // a reconciliation failure can never look like "work still to do".
            // CancellationToken.None: after a paid provider call, a cancelled
            // caller must not leave the operation dangling as in-flight.
            await db.SaveChangesAsync(CancellationToken.None);
            await usageRecorder.CompleteOperationAsync(
                lease.OperationId!,
                outcome.TerminalSkipReason is null ? AiOperationState.RetryScheduled : AiOperationState.FailedTerminal,
                null, servingProviderCode, servingModel, CancellationToken.None,
                lease.BudgetReservation);
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
                a.AiModel = servingModel;
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

        // The advisory verdicts are durable BEFORE the operation is closed —
        // and with CancellationToken.None, because the provider call is already
        // paid for: a caller cancelling now must not cost us the result.
        await db.SaveChangesAsync(CancellationToken.None);
        await usageRecorder.CompleteOperationAsync(
            lease.OperationId!, AiOperationState.Completed, outcome.UsageRecordId,
            servingProviderCode, servingModel, CancellationToken.None,
            lease.BudgetReservation);
        logger.LogInformation(
            "Part A AI advisory review: stamped {Scored}, terminally closed {Closed} of {Total} answers on attempt {AttemptId} (attempt {AttemptNumber}/{MaxAttempts}).",
            scored, closed, partAAnswers.Count, attemptId, attemptNumber, ListeningPartAAiRetryPolicy.MaxAttempts);
    }

    // ── W2 control-plane helpers ────────────────────────────────────────────────

    /// <summary>
    /// Deterministic digest of the grounding evidence that will be sent. Raw
    /// candidate answers and note text are hashed, never persisted, so the
    /// control plane holds no learner content while still distinguishing two
    /// genuinely different requests.
    /// </summary>
    private static string BuildEvidenceHash(IReadOnlyList<GapItem> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items.OrderBy(i => i.Number))
        {
            sb.Append(i.Number).Append('\u001f')
              .Append(i.Context).Append('\u001f')
              .Append(i.UserAnswer).Append('\u001f')
              .Append(i.Canonical).Append('\u001f')
              .Append(string.Join(',', i.Accepted)).Append('\u001f')
              .Append(i.ApprovedRationale).Append('\u001e');
        }

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>
    /// The control plane refused to lease this work. Zero bytes reached the
    /// provider in every branch, so nothing was billed. A policy refusal is
    /// terminal (an admin must re-enable the feature). A duplicate, conflict or
    /// store outage is a BOUNDED cool-off: each denial advances the durable
    /// <c>AiAttemptCount</c> and, after
    /// <see cref="ListeningPartAAiRetryPolicy.MaxLeaseDeniedRounds"/> of them,
    /// the row is parked terminally so the 20 s worker stops re-selecting it
    /// forever. <c>AiScoredAt</c> is never stamped and the deterministic mark is
    /// never touched on any path.
    /// </summary>
    private void ApplyDeniedLease(
        IReadOnlyList<ListeningAnswer> answers,
        DirectAiOperationLease lease,
        int attemptsSpent,
        DateTimeOffset now,
        string attemptId)
    {
        if (lease.Disposition == DirectAiOperationDisposition.PolicyRefused)
        {
            StampTerminal(answers, ListeningPartAAiSkipReasons.PolicyRefused, attemptsSpent);
            logger.LogWarning(
                "Part A AI advisory review refused for attempt {AttemptId} by feature policy ({Reason}); zero provider calls.",
                attemptId, lease.Reason);
            return;
        }

        // An existing operation whose outcome is ambiguous may already have been
        // billed. Deferring would eventually re-run it; parking it now preserves
        // the `indeterminate` never-auto-retry rule end to end.
        if (lease.ExistingState == AiOperationState.Indeterminate)
        {
            StampTerminal(answers, ListeningPartAAiSkipReasons.IndeterminateTimeout, attemptsSpent);
            logger.LogError(
                "Part A AI advisory review parked for attempt {AttemptId}: the existing operation is indeterminate and must never be auto-repeated.",
                attemptId);
            return;
        }

        var deniedRound = attemptsSpent + 1;
        if (deniedRound >= ListeningPartAAiRetryPolicy.MaxLeaseDeniedRounds)
        {
            StampTerminal(answers, ListeningPartAAiSkipReasons.LeaseDenied, deniedRound);
            logger.LogWarning(
                "Part A AI advisory review parked for attempt {AttemptId} after {Rounds} control-plane lease denials ({Disposition}/{Reason}); zero provider calls.",
                attemptId, deniedRound, lease.Disposition, lease.Reason);
            return;
        }

        var deferUntil = now + ListeningPartAAiRetryPolicy.OperationLeaseDeniedCooldown;
        foreach (var a in answers)
        {
            a.AiAttemptCount = deniedRound;
            a.AiNextAttemptAt = deferUntil;
        }

        logger.LogInformation(
            "Part A AI advisory review deferred for attempt {AttemptId}: control plane returned {Disposition} ({Reason}); zero provider calls, denial {Round}/{Max}.",
            attemptId, lease.Disposition, lease.Reason, deniedRound, ListeningPartAAiRetryPolicy.MaxLeaseDeniedRounds);
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
