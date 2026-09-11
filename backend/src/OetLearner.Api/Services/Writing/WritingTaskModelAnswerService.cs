using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingTaskModelAnswerDto(
    Guid ScenarioId,
    string Status,
    bool IsCandidateVisible,
    string? ModelAnswerText,
    IReadOnlyList<string> GroundedFactReferences,
    string? HoldReason,
    string? RulebookVersion,
    string? ModelUsed,
    DateTimeOffset? GeneratedAt,
    string? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    bool IsStale);

/// <summary>
/// Generates and manages the ONE reusable, pre-generated Writing Model Answer
/// per task (spec: "Model Answer — generate once, save permanently, reuse").
/// Admin-triggered only; never called from the candidate submit path. Reuses
/// <see cref="WritingModelAnswerGroundingValidator"/> — the same fact-grounding
/// check the existing per-submission model-answer generator uses — so both
/// paths refuse to publish a sentence that cannot be traced to the case notes.
/// </summary>
public sealed record WritingModelAnswerBatchItemResult(
    Guid ScenarioId,
    string Title,
    string Outcome,
    string? HoldReason);

public sealed record WritingModelAnswerBatchResult(
    int Requested,
    int Generated,
    int Held,
    int Skipped,
    IReadOnlyList<WritingModelAnswerBatchItemResult> Items);

public interface IWritingTaskModelAnswerService
{
    Task<WritingTaskModelAnswerDto?> GetAsync(Guid scenarioId, CancellationToken ct = default);
    Task<(IReadOnlyList<WritingTaskModelAnswerDto> Items, int Total)> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto> GenerateAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Certifies an offline-drafted Model Answer (the "platform validate" half
    /// of the hybrid generation route: draft outside the platform at no
    /// provider spend, then run it through the SAME grounding + word-count +
    /// deterministic-rule gate as <see cref="GenerateAsync"/> before it can
    /// ever be marked Ready). Never bypasses any check GenerateAsync applies.
    /// </summary>
    Task<WritingTaskModelAnswerDto> ImportAsync(Guid scenarioId, string letterText, string adminUserId, CancellationToken ct = default);

    Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto?> RejectAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Preparation-time backfill across published tasks: generates the ONE
    /// reusable Model Answer for every task that lacks a fresh approved one.
    /// Resumable (re-run to continue), idempotent (Ready + fresh answers are
    /// skipped, never regenerated), concurrency-safe (one row per task,
    /// processed strictly sequentially), and rate-limit aware (sequential
    /// provider calls, small bounded batch). Never called from the candidate
    /// submit path.
    /// </summary>
    Task<WritingModelAnswerBatchResult> GenerateMissingAsync(
        string adminUserId,
        int limit,
        bool includeStale,
        CancellationToken ct = default);

    /// <summary>
    /// Single-task worker step for the background exemplar queue (Option C).
    /// Idempotent: a Ready + fresh answer is returned untouched with outcome
    /// <c>ready-skipped</c> and ZERO provider calls, so queue redelivery or
    /// stuck-job recovery after a restart can never trigger a duplicate paid
    /// AI request. Otherwise generates exactly once via
    /// <see cref="GenerateAsync"/>.
    /// </summary>
    Task<WritingModelAnswerWorkItemResult> GenerateIfNeededAsync(
        Guid scenarioId,
        string adminUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues background generation jobs (Option C) for published tasks
    /// lacking a fresh approved Model Answer. Fast and side-effect free
    /// beyond the job rows: the worker grinds through them without any HTTP
    /// timeout pressure. Skips tasks that already have a queued/processing
    /// job (no duplicate workflows) and tasks already Ready + fresh.
    /// </summary>
    Task<WritingModelAnswerEnqueueResult> EnqueueMissingAsync(
        string adminUserId,
        int limit,
        CancellationToken ct = default);
}

/// <summary>Outcome of one background exemplar work item.</summary>
public sealed record WritingModelAnswerWorkItemResult(
    Guid ScenarioId,
    string Title,
    string Outcome,
    string? HoldReason);

/// <summary>Outcome of one background enqueue call.</summary>
public sealed record WritingModelAnswerEnqueueResult(
    int Requested,
    int Enqueued,
    int Skipped,
    IReadOnlyList<WritingModelAnswerWorkItemResult> Items);

public sealed class WritingTaskModelAnswerService(
    LearnerDbContext db,
    IAiGatewayService gateway,
    WritingRuleEngine ruleEngine,
    TimeProvider clock,
    ILogger<WritingTaskModelAnswerService> logger) : IWritingTaskModelAnswerService
{
    // Must fit AiOperation.PromptVersion, which is [MaxLength(32)] - confirmed
    // via production Npgsql exception (22001: value too long for type
    // character varying(32)) after the longer "writing.model-answer-pregenerate.v1"
    // (35 chars) broke every single pilot generation call.
    private const string PromptVersion = "writing.model-answer-pregen.v1";
    // Pinned per owner instruction: Model Answers are candidate-facing exemplar
    // content, generated once per task, so quality is prioritised over the
    // platform's default (cheaper) provider. Provider/Model set explicitly on
    // the request bypass feature-route resolution (AiGatewayService.cs).
    private const string PinnedProvider = "anthropic";
    private const string PinnedModel = "claude-sonnet-5";
    // Owner instruction: this is one-time content-authoring, not per-candidate
    // grading, so pay for max reasoning quality/effort. claude-sonnet-5 uses
    // adaptive thinking (no manual token budget) - "max" is a valid
    // output_config.effort value, confirmed live against the Anthropic API.
    // MaxTokens just needs generous headroom for thinking + the ~200-word
    // letter + JSON wrapper; 32000 verified accepted for this model.
    private const string ThinkingEffort = "max";
    private const int MaxCompletionTokens = 32000;

    public async Task<WritingTaskModelAnswerDto?> GetAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.AsNoTracking().FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    public async Task<(IReadOnlyList<WritingTaskModelAnswerDto> Items, int Total)> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingTaskModelAnswers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<WritingAssessmentModelAnswerStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(x => x.Status == parsed);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var scenarioIds = rows.Select(r => r.ScenarioId).ToList();
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => scenarioIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var items = rows.Select(r => ToDto(r, scenarios.GetValueOrDefault(r.ScenarioId))).ToList();
        return (items, total);
    }

    public async Task<WritingTaskModelAnswerDto> GenerateAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");

        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == scenarioId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new WritingTaskModelAnswer { Id = Guid.NewGuid(), ScenarioId = scenarioId, CreatedAt = now };
            db.WritingTaskModelAnswers.Add(row);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown ?? string.Empty;
        var caseNotesText = BuildCaseNotesText(sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
        var allFacts = sentences.Select(s => s.SentenceText).ToArray();
        row.SourceContentHash = ComputeSourceContentHash(taskSnapshot, caseNotesText);
        row.UpdatedAt = now;

        if (sentences.Count == 0)
        {
            return Hold(row, "model_answer_case_notes_unavailable");
        }

        if (!RulebookProfessionParser.TryParse(scenario.Profession, out var profession))
        {
            return Hold(row, "model_answer_profession_pack_unavailable");
        }

        try
        {
            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = profession,
                LetterType = scenario.LetterType,
                Task = AiTaskMode.GenerateContent,
            });

            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                Provider = PinnedProvider,
                Model = PinnedModel,
                Temperature = 0.1,
                MaxTokens = MaxCompletionTokens,
                EnableExtendedThinking = true,
                ThinkingEffort = ThinkingEffort,
                FeatureCode = AiFeatureCodes.WritingModelAnswerPregenerate,
                PromptTemplateId = PromptVersion,
                UserId = adminUserId,
                AssessmentContext = AiAssessmentContext.Practice,
                ResourceId = scenarioId.ToString("D"),
                ResourceType = "writing_task_model_answer",
                UserInput = $$"""
                    Produce the ONE reusable OET Writing model answer for this task. It will be
                    shown to every candidate who completes this exact task, so it must be a
                    faithful, high-quality exemplar grounded ONLY in the case notes below.
                    Return one JSON object only:
                    {"modelAnswerText":"...","whyThisWorks":["..."],"groundedFactReferences":["..."]}
                    The model answer must be approximately 180-200 words, recipient-appropriate
                    per the writing task instruction, and use only facts present in the case
                    notes. Do not add diagnoses, tests, treatment, dates, or requests that are
                    not in the case notes.

                    Writing task:
                    ---
                    {{taskSnapshot}}
                    ---
                    Case notes:
                    ---
                    {{caseNotesText}}
                    ---
                    """,
            }, ct);

            var parsed = Parse(result.Completion);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ModelAnswerText))
            {
                return Hold(row, "model_answer_unreadable");
            }

            var words = WritingModelAnswerWordCounter.CountBodyWords(parsed.ModelAnswerText);
            if (words < 180 || words > 200)
            {
                return Hold(row, "model_answer_word_count_out_of_range");
            }

            var grounding = WritingModelAnswerGroundingValidator.Validate(parsed.ModelAnswerText, allFacts);
            if (!grounding.IsGrounded)
            {
                return Hold(row, "model_answer_unmapped_sentence");
            }

            // Global Model Answer Formatting & Sign-Off Rules (owner addendum,
            // 2026-09-06): "VERIFIED must mean zero unresolved violations" —
            // a Model Answer must never be marked Ready while any Critical
            // deterministic finding (brackets, invented sign-off name, date
            // format, structural rules, etc.) remains. Never force Ready.
            var lintFindings = ruleEngine.Lint(new WritingLintInput(
                LetterText: parsed.ModelAnswerText,
                LetterType: scenario.LetterType,
                Profession: profession,
                IsModelAnswer: true));
            var criticalFindings = lintFindings.Where(f => f.Severity == RuleSeverity.Critical).ToList();
            if (criticalFindings.Count > 0)
            {
                logger.LogWarning(
                    "Model-answer rule violations for scenario {ScenarioId}: {Findings}",
                    scenarioId, string.Join(" | ", criticalFindings.Select(f => $"{f.RuleId}: {f.Message}")));
                return Hold(row, "model_answer_rule_violations");
            }

            row.Status = WritingAssessmentModelAnswerStatus.Ready;
            row.IsCandidateVisible = false; // still needs an explicit admin approve
            row.ModelAnswerText = parsed.ModelAnswerText.Trim();
            row.GroundedFactReferencesJson = JsonSerializer.Serialize(parsed.GroundedFactReferences ?? []);
            row.HoldReason = null;
            row.RulebookVersion = string.IsNullOrWhiteSpace(result.RulebookVersion) ? null : result.RulebookVersion;
            row.PromptVersion = PromptVersion;
            row.ModelUsed = string.IsNullOrWhiteSpace(result.ResolvedModel) ? PinnedModel : result.ResolvedModel;
            row.GeneratedAt = now;
            row.ApprovedByUserId = null;
            row.ApprovedAt = null;

            await db.SaveChangesAsync(ct);
            return ToDto(row, scenario);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model-answer pregeneration failed for scenario {ScenarioId}", scenarioId);
            return Hold(row, "model_answer_generation_failed");
        }
    }

    public async Task<WritingTaskModelAnswerDto> ImportAsync(Guid scenarioId, string letterText, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");

        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == scenarioId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new WritingTaskModelAnswer { Id = Guid.NewGuid(), ScenarioId = scenarioId, CreatedAt = now };
            db.WritingTaskModelAnswers.Add(row);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown ?? string.Empty;
        var caseNotesText = BuildCaseNotesText(sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
        var allFacts = sentences.Select(s => s.SentenceText).ToArray();
        row.SourceContentHash = ComputeSourceContentHash(taskSnapshot, caseNotesText);
        row.UpdatedAt = now;

        if (sentences.Count == 0)
        {
            return Hold(row, "model_answer_case_notes_unavailable");
        }
        if (!RulebookProfessionParser.TryParse(scenario.Profession, out var profession))
        {
            return Hold(row, "model_answer_profession_pack_unavailable");
        }
        if (string.IsNullOrWhiteSpace(letterText))
        {
            return Hold(row, "model_answer_unreadable");
        }

        var trimmedLetter = letterText.Trim();
        var words = WritingModelAnswerWordCounter.CountBodyWords(trimmedLetter);
        if (words < 180 || words > 200)
        {
            return Hold(row, "model_answer_word_count_out_of_range");
        }

        var grounding = WritingModelAnswerGroundingValidator.Validate(trimmedLetter, allFacts);
        if (!grounding.IsGrounded)
        {
            return Hold(row, "model_answer_unmapped_sentence");
        }

        var lintFindings = ruleEngine.Lint(new WritingLintInput(
            LetterText: trimmedLetter,
            LetterType: scenario.LetterType,
            Profession: profession,
            IsModelAnswer: true));
        var criticalFindings = lintFindings.Where(f => f.Severity == RuleSeverity.Critical).ToList();
        if (criticalFindings.Count > 0)
        {
            logger.LogWarning(
                "Imported model-answer rule violations for scenario {ScenarioId}: {Findings}",
                scenarioId, string.Join(" | ", criticalFindings.Select(f => $"{f.RuleId}: {f.Message}")));
            return Hold(row, "model_answer_rule_violations");
        }

        row.Status = WritingAssessmentModelAnswerStatus.Ready;
        row.IsCandidateVisible = false; // still needs an explicit admin approve
        row.ModelAnswerText = trimmedLetter;
        row.GroundedFactReferencesJson = "[]";
        row.HoldReason = null;
        row.RulebookVersion = null;
        row.PromptVersion = "writing.model-answer.offline-import.v1";
        row.ModelUsed = "offline-import:claude-code";
        row.GeneratedAt = now;
        row.ApprovedByUserId = null;
        row.ApprovedAt = null;

        await db.SaveChangesAsync(ct);
        return ToDto(row, scenario);
    }

    public async Task<WritingModelAnswerBatchResult> GenerateMissingAsync(
        string adminUserId,
        int limit,
        bool includeStale,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 25);
        var candidateIds = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .OrderBy(s => s.UpdatedAt)
            .Select(s => s.Id)
            .Take(200)
            .ToListAsync(ct);

        var answers = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => candidateIds.Contains(a.ScenarioId))
            .ToDictionaryAsync(a => a.ScenarioId, ct);

        var items = new List<WritingModelAnswerBatchItemResult>();
        var generated = 0;
        var held = 0;
        var skipped = 0;

        foreach (var scenarioId in candidateIds)
        {
            if (items.Count >= limit) break;
            ct.ThrowIfCancellationRequested();

            var scenario = await db.WritingScenarios.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
            if (scenario is null)
            {
                skipped++;
                continue;
            }

            answers.TryGetValue(scenarioId, out var existing);
            if (existing is not null
                && existing.Status == WritingAssessmentModelAnswerStatus.Ready)
            {
                // Ready answers are never regenerated blindly: a visible +
                // fresh one is reused forever; a visible + stale one is
                // refreshed only on explicit request; an invisible one is
                // awaiting admin approval — regenerating would discard the
                // pending review and burn another provider call.
                if (!existing.IsCandidateVisible)
                {
                    skipped++;
                    items.Add(new WritingModelAnswerBatchItemResult(scenarioId, scenario.Title, "skipped", "awaiting_approval"));
                    continue;
                }
                var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing.SourceContentHash, ct);
                if (fresh || !includeStale)
                {
                    skipped++;
                    items.Add(new WritingModelAnswerBatchItemResult(
                        scenarioId, scenario.Title, "skipped", fresh ? "already_ready" : "stale_refresh_not_requested"));
                    continue;
                }
            }

            WritingTaskModelAnswerDto outcome;
            try
            {
                outcome = await GenerateAsync(scenarioId, adminUserId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Model-answer batch generation threw for scenario {ScenarioId}; continuing batch.", scenarioId);
                held++;
                items.Add(new WritingModelAnswerBatchItemResult(scenarioId, scenario.Title, "held", "model_answer_generation_failed"));
                continue;
            }

            if (string.Equals(outcome.Status, WritingAssessmentModelAnswerStatus.Ready.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                generated++;
                items.Add(new WritingModelAnswerBatchItemResult(scenarioId, scenario.Title, "generated", null));
            }
            else
            {
                held++;
                items.Add(new WritingModelAnswerBatchItemResult(scenarioId, scenario.Title, "held", outcome.HoldReason));
            }
        }

        return new WritingModelAnswerBatchResult(items.Count, generated, held, skipped, items);
    }

    /// <summary>
    /// Single-task background worker step (Option C). A Ready + fresh answer
    /// is returned untouched with outcome <c>ready-skipped</c> and zero
    /// provider calls, so queue redelivery or stuck-job recovery after a
    /// restart can never trigger a duplicate paid AI request. Awaiting-approval
    /// answers are likewise left alone. Otherwise generates exactly once.
    /// </summary>
    public async Task<WritingModelAnswerWorkItemResult> GenerateIfNeededAsync(
        Guid scenarioId,
        string adminUserId,
        CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        if (scenario is null)
        {
            return new WritingModelAnswerWorkItemResult(scenarioId, "(task removed)", "skipped", "scenario_not_found");
        }

        var existing = await db.WritingTaskModelAnswers.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ScenarioId == scenarioId, ct);
        if (existing is not null && existing.Status == WritingAssessmentModelAnswerStatus.Ready)
        {
            var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing.SourceContentHash, ct);
            if (fresh)
            {
                logger.LogInformation(
                    "Model-answer worker skipped scenario {ScenarioId}: answer already Ready and fresh, no provider call.",
                    scenarioId);
                return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "ready-skipped", "already_ready");
            }

            if (!existing.IsCandidateVisible)
            {
                return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "skipped", "awaiting_approval");
            }
        }

        WritingTaskModelAnswerDto outcome;
        try
        {
            outcome = await GenerateAsync(scenarioId, adminUserId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Model-answer worker threw for scenario {ScenarioId}.", scenarioId);
            return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "held", "model_answer_generation_failed");
        }

        if (string.Equals(outcome.Status, WritingAssessmentModelAnswerStatus.Ready.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Model-answer worker generated Ready answer for scenario {ScenarioId} ({Title}).",
                scenarioId, scenario.Title);
            return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "generated", null);
        }

        logger.LogWarning(
            "Model-answer worker held scenario {ScenarioId} ({Title}): {HoldReason}.",
            scenarioId, scenario.Title, outcome.HoldReason);
        return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "held", outcome.HoldReason);
    }

    /// <summary>
    /// Returns true for hold reasons that indicate a transient infrastructure
    /// failure (retry with backoff is worthwhile) as opposed to a content or
    /// quality verdict (retrying is pointless without a content change).
    /// </summary>
    internal static bool IsTransientHold(string? holdReason)
        => string.Equals(holdReason, "model_answer_generation_failed", StringComparison.Ordinal);

    /// <summary>
    /// Enqueues background generation jobs (Option C) for published tasks
    /// lacking a fresh approved Model Answer. Returns immediately: the
    /// worker grinds through jobs with no HTTP timeout pressure. Skips tasks
    /// with an already queued/processing job (no duplicate workflows) and
    /// Ready + fresh tasks. Resumable: re-run to continue where it stopped.
    /// </summary>
    public async Task<WritingModelAnswerEnqueueResult> EnqueueMissingAsync(
        string adminUserId,
        int limit,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var now = clock.GetUtcNow();
        var candidateIds = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .OrderBy(s => s.UpdatedAt)
            .Select(s => s.Id)
            .Take(500)
            .ToListAsync(ct);
        if (candidateIds.Count == 0)
        {
            return new WritingModelAnswerEnqueueResult(0, 0, 0, []);
        }

        var answers = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => candidateIds.Contains(a.ScenarioId))
            .ToDictionaryAsync(a => a.ScenarioId, ct);
        var busyScenarioIds = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Type == JobType.WritingModelAnswerGeneration
                && (j.State == AsyncState.Queued || j.State == AsyncState.Processing))
            .Select(j => j.ResourceId)
            .ToListAsync(ct);
        var busy = busyScenarioIds.Where(x => x is not null).ToHashSet();

        var items = new List<WritingModelAnswerWorkItemResult>();
        var enqueued = 0;
        var skipped = 0;
        foreach (var scenarioId in candidateIds)
        {
            if (enqueued >= limit) break;
            ct.ThrowIfCancellationRequested();

            var key = scenarioId.ToString("D");
            if (busy.Contains(key))
            {
                skipped++;
                continue;
            }

            var scenario = await db.WritingScenarios.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
            if (scenario is null)
            {
                skipped++;
                continue;
            }

            if (answers.TryGetValue(scenarioId, out var existing)
                && existing.Status == WritingAssessmentModelAnswerStatus.Ready)
            {
                var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing.SourceContentHash, ct);
                if (fresh || !existing.IsCandidateVisible)
                {
                    skipped++;
                    continue;
                }
            }

            // Deterministic job id (one per task): concurrent enqueuers
            // collapse onto the same row instead of duplicating paid work.
            // Saved per row so one duplicate never rolls back the batch.
            var job = new BackgroundJobItem
            {
                Id = $"jb-wr-model-answer-{scenarioId:N}",
                Type = JobType.WritingModelAnswerGeneration,
                State = AsyncState.Queued,
                ResourceId = key,
                PayloadJson = JsonSerializer.Serialize(new { scenarioId = key, requestedBy = adminUserId, requestedAt = now }),
                StatusReasonCode = "queued",
                StatusMessage = $"Model Answer generation queued for '{scenario.Title}'.",
                CreatedAt = now,
                AvailableAt = now,
                LastTransitionAt = now,
            };
            db.BackgroundJobs.Add(job);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.Entry(job).State = EntityState.Detached;
                skipped++;
                continue;
            }

            busy.Add(key);
            enqueued++;
            items.Add(new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "enqueued", null));
        }

        logger.LogInformation(
            "Model-answer enqueue by {AdminUserId}: {Enqueued} enqueued, {Skipped} skipped.",
            adminUserId, enqueued, skipped);
        return new WritingModelAnswerEnqueueResult(candidateIds.Count, enqueued, skipped, items);
    }

    private async Task<bool> IsFreshAsync(
        Guid scenarioId,
        string taskPrompt,
        string? sourceContentHash,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceContentHash)) return false;
        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == scenarioId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        var current = ComputeSourceContentHash(
            taskPrompt,
            sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
        return string.Equals(sourceContentHash, current, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        if (row.Status != WritingAssessmentModelAnswerStatus.Ready)
        {
            throw ApiException.Validation("model_answer_not_ready", "Only a Ready model answer can be approved.");
        }

        row.IsCandidateVisible = true;
        row.ApprovedByUserId = adminUserId;
        row.ApprovedAt = clock.GetUtcNow();
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    public async Task<WritingTaskModelAnswerDto?> RejectAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;

        row.Status = WritingAssessmentModelAnswerStatus.Rejected;
        row.IsCandidateVisible = false;
        row.HoldReason = "rejected_by_admin";
        row.ApprovedByUserId = adminUserId;
        row.ApprovedAt = clock.GetUtcNow();
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario);
    }

    private WritingTaskModelAnswerDto Hold(WritingTaskModelAnswer row, string reason)
    {
        row.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
        row.IsCandidateVisible = false;
        row.HoldReason = reason;
        row.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges();
        return ToDto(row, null);
    }

    private static WritingTaskModelAnswerDto ToDto(WritingTaskModelAnswer row, WritingScenario? scenario)
    {
        _ = scenario; // reserved: a full staleness check needs the current case-notes
                      // text too (not just the scenario row), so it is intentionally
                      // not computed on this cheap list/get projection today. The
                      // stored SourceContentHash is enough for a future admin action
                      // to detect drift by recomputing over the live case notes.
        var refs = ParseFactReferences(row.GroundedFactReferencesJson);
        return new WritingTaskModelAnswerDto(
            row.ScenarioId,
            row.Status.ToString(),
            row.IsCandidateVisible,
            row.ModelAnswerText,
            refs,
            row.HoldReason,
            row.RulebookVersion,
            row.ModelUsed,
            row.GeneratedAt,
            row.ApprovedByUserId,
            row.ApprovedAt,
            IsStale: false);
    }

    private static IReadOnlyList<string> ParseFactReferences(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    /// <summary>
    /// Canonical case-note rendering shared by generation and staleness
    /// checks: only <c>relevant</c>/<c>maybe</c> sentences, in order.
    /// </summary>
    internal static string BuildCaseNotesText(IEnumerable<(string Text, string? Relevance)> sentences)
        => string.Join("\n", sentences
            .Where(s => s.Relevance is "relevant" or "maybe")
            .Select(s => $"- {s.Text}"));

    /// <summary>
    /// Hash of the exact source content a Model Answer was (or would be)
    /// generated from. Regeneration is justified only when this drifts —
    /// never during normal candidate submissions.
    /// </summary>
    internal static string ComputeSourceContentHash(
        string taskSnapshot,
        IEnumerable<(string Text, string? Relevance)> sentences)
        => ComputeHash($"{taskSnapshot ?? string.Empty}\n---\n{BuildCaseNotesText(sentences)}");

    internal static string ComputeSourceContentHash(string taskSnapshot, string caseNotesText)
        => ComputeHash($"{taskSnapshot ?? string.Empty}\n---\n{caseNotesText ?? string.Empty}");

    private static Draft? Parse(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<Draft>(completion[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Draft(
        string? ModelAnswerText,
        IReadOnlyList<string>? WhyThisWorks,
        IReadOnlyList<string>? GroundedFactReferences);
}
