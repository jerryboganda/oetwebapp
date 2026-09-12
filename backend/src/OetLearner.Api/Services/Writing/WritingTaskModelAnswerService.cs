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
    bool IsStale,
    string? Title = null,
    string? Profession = null,
    string? LetterType = null,
    string? ValidatorVersion = null,
    string? RulePackHash = null,
    DateTimeOffset? ValidatedAt = null,
    int RepairCount = 0,
    int? BodyWordCount = null,
    string VerificationStatus = "unverified",
    JsonElement? ValidationReport = null,
    string? PromptVersion = null);

public sealed record WritingModelAnswerFindingDto(
    string RuleId,
    string Severity,
    string Message,
    string? Quote,
    string? FixSuggestion);

/// <summary>
/// The complete Model Answer gate outcome for one letter text (Addendum Rev8
/// §14): body word count, case-note grounding, every deterministic
/// Model-Answer-mode rule, and the independent semantic validator — all under
/// one recorded validator version + rule-pack fingerprint.
/// </summary>
public sealed record WritingModelAnswerValidationReport(
    Guid ScenarioId,
    bool Passed,
    string? HoldReason,
    int BodyWordCount,
    bool WordCountOk,
    IReadOnlyList<string> UnmappedSentences,
    IReadOnlyList<WritingModelAnswerFindingDto> DeterministicFindings,
    bool SemanticChecked,
    bool? SemanticPassed,
    IReadOnlyList<WritingModelAnswerSemanticViolation> SemanticViolations,
    string? SemanticError,
    string ValidatorVersion,
    string RulePackHash,
    string? RulebookVersion,
    string HouseStyleVersion,
    DateTimeOffset CheckedAt);

public sealed record WritingModelAnswerRevalidationRequest(
    bool Apply,
    bool IncludeSemantic,
    string? Profession,
    int Offset,
    int Limit,
    bool OnlyUnverified);

public sealed record WritingModelAnswerRevalidationItem(
    Guid ScenarioId,
    string Title,
    string Profession,
    string LetterType,
    string StatusBefore,
    bool VisibleBefore,
    string? ValidatorVersionBefore,
    bool Passed,
    string? HoldReason,
    int BodyWordCount,
    IReadOnlyList<WritingModelAnswerFindingDto> Findings,
    IReadOnlyList<string> UnmappedSentences,
    bool SemanticChecked,
    IReadOnlyList<WritingModelAnswerSemanticViolation> SemanticViolations,
    string? SemanticError);

public sealed record WritingModelAnswerRevalidationResult(
    string ValidatorVersion,
    int TotalRows,
    int Checked,
    int Passed,
    int Failed,
    int Offset,
    int Limit,
    bool Applied,
    bool IncludeSemantic,
    IReadOnlyList<WritingModelAnswerRevalidationItem> Items);

/// <summary>
/// Generates and manages the ONE reusable, pre-generated Writing Model Answer
/// per task (spec: "Model Answer — generate once, save permanently, reuse").
/// Admin-triggered only; never called from the candidate submit path.
/// <para/>
/// Addendum Rev8 (11 Sep 2026) generation workflow, enforced here for EVERY
/// path that can store a candidate-facing answer (generate, background worker,
/// offline import, revalidation): generate → deterministic lint → semantic
/// validation → repair only the failed rules → re-run ALL validators → store
/// only when remaining active-rule violations = 0. The exact validator version
/// and rule-pack fingerprint are recorded; a stored answer is candidate-visible
/// only while it matches the running validator (<see cref="CandidateVisibleVerified"/>).
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
    /// provider spend, then run it through the SAME full gate as
    /// <see cref="GenerateAsync"/> — word count, grounding, every
    /// deterministic rule and the semantic validator — before it can ever be
    /// marked Ready). Never bypasses any check GenerateAsync applies.
    /// </summary>
    Task<WritingTaskModelAnswerDto> ImportAsync(Guid scenarioId, string letterText, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Runs the full Model Answer gate on a candidate text WITHOUT storing
    /// anything (the deterministic lint → repair loop used while drafting).
    /// </summary>
    Task<WritingModelAnswerValidationReport> ValidateAsync(Guid scenarioId, string letterText, bool includeSemantic, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Re-runs the CURRENT validator over saved answers (Addendum Rev8 §9.5:
    /// "Revalidate ALL current candidate-facing Model Answers with the
    /// corrected validator. Do not trust old VERIFIED flags"). With
    /// <c>Apply</c>: passing rows are stamped with the current validator
    /// version; failing rows are held and hidden.
    /// </summary>
    Task<WritingModelAnswerRevalidationResult> RevalidateAsync(WritingModelAnswerRevalidationRequest request, string adminUserId, CancellationToken ct = default);

    Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);
    Task<WritingTaskModelAnswerDto?> RejectAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Preparation-time backfill across published tasks: generates the ONE
    /// reusable Model Answer for every task that lacks a fresh, verified
    /// approved one. Resumable, idempotent, concurrency-safe and rate-limit
    /// aware (sequential provider calls, small bounded batch). Never called
    /// from the candidate submit path.
    /// </summary>
    Task<WritingModelAnswerBatchResult> GenerateMissingAsync(
        string adminUserId,
        int limit,
        bool includeStale,
        CancellationToken ct = default);

    /// <summary>
    /// Single-task worker step for the background exemplar queue (Option C).
    /// Idempotent: a Ready + fresh + verified answer is returned untouched with
    /// outcome <c>ready-skipped</c> and ZERO provider calls.
    /// </summary>
    Task<WritingModelAnswerWorkItemResult> GenerateIfNeededAsync(
        Guid scenarioId,
        string adminUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues background generation jobs (Option C) for published tasks
    /// lacking a fresh, verified approved Model Answer.
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
    ILogger<WritingTaskModelAnswerService> logger,
    IWritingModelAnswerSemanticValidator? semanticValidator = null) : IWritingTaskModelAnswerService
{
    /// <summary>
    /// The ONLY predicate that decides whether a saved Model Answer may be shown
    /// to candidates: Ready + admin-approved + verified with zero violations
    /// under the CURRENTLY RUNNING deterministic validator version (Addendum
    /// Rev8 §14 — a stored VERIFIED flag is invalid after a validator/rule-pack
    /// change until that exact saved answer is revalidated).
    /// </summary>
    public static System.Linq.Expressions.Expression<Func<WritingTaskModelAnswer, bool>> CandidateVisibleVerified
        => a => a.Status == WritingAssessmentModelAnswerStatus.Ready
                && a.IsCandidateVisible
                && a.ValidatorVersion == WritingRuleEngine.ValidatorVersion;

    public static bool IsVerifiedForCandidates(WritingTaskModelAnswer? row)
        => row is not null
           && row.Status == WritingAssessmentModelAnswerStatus.Ready
           && row.IsCandidateVisible
           && string.Equals(row.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal);

    // Must fit AiOperation.PromptVersion, which is [MaxLength(32)] - confirmed
    // via production Npgsql exception (22001: value too long for type
    // character varying(32)) after the longer "writing.model-answer-pregenerate.v1"
    // (35 chars) broke every single pilot generation call.
    private const string PromptVersion = "writing.model-answer-pregen.v2";
    private const string ImportPromptVersion = "writing.model-answer.offline-import.v2";
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
    private const string ThinkingEffort = "max";
    private const int MaxCompletionTokens = 32000;
    // Addendum Rev8 §14: repair only the failed rules, then re-run ALL
    // validators. One generation + up to three targeted repairs.
    private const int MaxGenerationAttempts = 4;

    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    public async Task<WritingTaskModelAnswerDto?> GetAsync(Guid scenarioId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.AsNoTracking().FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario, includeReport: true);
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

        var items = rows.Select(r => ToDto(r, scenarios.GetValueOrDefault(r.ScenarioId), includeReport: true)).ToList();
        return (items, total);
    }

    public async Task<WritingTaskModelAnswerDto> GenerateAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");

        var sentences = await LoadSentencesAsync(scenarioId, ct);

        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new WritingTaskModelAnswer { Id = Guid.NewGuid(), ScenarioId = scenarioId, CreatedAt = now };
            db.WritingTaskModelAnswers.Add(row);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown ?? string.Empty;
        var caseNotesText = BuildCaseNotesText(sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
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
                // Rev8 D14 root cause: the raw "LT-UR" catalogue code never
                // matched letter-type-scoped rulebook rules ("urgent_referral").
                LetterType = WritingLetterTypeTaxonomy.ToLegacyLetterType(scenario.LetterType),
                Task = AiTaskMode.GenerateContent,
            });

            string? letter = null;
            WritingModelAnswerValidationReport? report = null;
            IReadOnlyList<string> factRefs = [];
            string? resolvedModel = null;
            string? rulebookVersion = null;
            var repairs = 0;
            for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
            {
                var userInput = attempt == 0 || letter is null || report is null
                    ? BuildGenerationInput(scenario, profession, taskSnapshot, caseNotesText)
                    : BuildRepairInput(scenario, profession, taskSnapshot, caseNotesText, letter, report);
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
                    UserInput = userInput,
                }, ct);
                resolvedModel = string.IsNullOrWhiteSpace(result.ResolvedModel) ? PinnedModel : result.ResolvedModel;
                rulebookVersion = string.IsNullOrWhiteSpace(result.RulebookVersion) ? rulebookVersion : result.RulebookVersion;

                var parsed = Parse(result.Completion);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.ModelAnswerText))
                {
                    if (letter is null && attempt == MaxGenerationAttempts - 1)
                    {
                        return Hold(row, "model_answer_unreadable");
                    }
                    continue;
                }

                letter = NormaliseLetterText(parsed.ModelAnswerText);
                factRefs = parsed.GroundedFactReferences ?? [];
                report = await RunGateAsync(scenario, sentences, profession, letter, includeSemantic: true, adminUserId, ct);
                if (report.Passed) break;
                if (IsTransientHold(report.HoldReason)) break;
                repairs = attempt + 1;
            }

            if (letter is null || report is null)
            {
                return Hold(row, "model_answer_unreadable");
            }

            if (!report.Passed)
            {
                logger.LogWarning(
                    "Model-answer generation held for scenario {ScenarioId} after {Repairs} repair(s): {HoldReason}; findings {Findings}",
                    scenarioId, repairs, report.HoldReason,
                    string.Join(" | ", report.DeterministicFindings.Select(f => $"{f.RuleId}: {f.Message}")));
                row.ValidationReportJson = SerializeReport(report, letter);
                return Hold(row, report.HoldReason ?? "model_answer_rule_violations");
            }

            StoreVerified(row, letter, report, now, resolvedModel, rulebookVersion, PromptVersion, repairs);
            row.GroundedFactReferencesJson = JsonSerializer.Serialize(factRefs);
            await db.SaveChangesAsync(ct);
            return ToDto(row, scenario, includeReport: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Model-answer pregeneration failed for scenario {ScenarioId}", scenarioId);
            // Diagnostic fix (12 Sep 2026): this hold reason gave zero visible
            // detail via the admin API (no server log access in this
            // deployment), and EVERY worker-triggered regeneration attempt
            // across many professions/letter types landed on it despite the
            // underlying AI call completing normally per AiOperations, with
            // Lint()/RunGateAsync/Parse()/the semantic validator/tool grants
            // all individually ruled out by direct code review. Persist the
            // real exception type + message (bounded) so the next occurrence
            // is diagnosable from GET .../model-answer alone.
            row.ValidationReportJson = JsonSerializer.Serialize(new
            {
                internalError = true,
                exceptionType = ex.GetType().FullName,
                exceptionMessage = Truncate(ex.ToString(), 4000),
                checkedAt = now,
            });
            return Hold(row, "model_answer_generation_failed");
        }
    }

    public async Task<WritingTaskModelAnswerDto> ImportAsync(Guid scenarioId, string letterText, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");

        var sentences = await LoadSentencesAsync(scenarioId, ct);

        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new WritingTaskModelAnswer { Id = Guid.NewGuid(), ScenarioId = scenarioId, CreatedAt = now };
            db.WritingTaskModelAnswers.Add(row);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown ?? string.Empty;
        var caseNotesText = BuildCaseNotesText(sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
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

        var letter = NormaliseLetterText(letterText);
        var report = await RunGateAsync(scenario, sentences, profession, letter, includeSemantic: true, adminUserId, ct);
        if (!report.Passed)
        {
            logger.LogWarning(
                "Imported model-answer held for scenario {ScenarioId}: {HoldReason}; findings {Findings}",
                scenarioId, report.HoldReason,
                string.Join(" | ", report.DeterministicFindings.Select(f => $"{f.RuleId}: {f.Message}")));
            row.ValidationReportJson = SerializeReport(report, letter);
            return Hold(row, report.HoldReason ?? "model_answer_rule_violations");
        }

        StoreVerified(row, letter, report, now, "offline-import:claude-code", report.RulebookVersion, ImportPromptVersion, repairCount: 0);
        row.GroundedFactReferencesJson = "[]";
        await db.SaveChangesAsync(ct);
        return ToDto(row, scenario, includeReport: true);
    }

    public async Task<WritingModelAnswerValidationReport> ValidateAsync(Guid scenarioId, string letterText, bool includeSemantic, string adminUserId, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Writing task was not found.");
        if (!RulebookProfessionParser.TryParse(scenario.Profession, out var profession))
        {
            throw ApiException.Validation("model_answer_profession_pack_unavailable", "The task's profession has no Writing rule pack.");
        }
        var sentences = await LoadSentencesAsync(scenarioId, ct);
        return await RunGateAsync(scenario, sentences, profession, NormaliseLetterText(letterText ?? string.Empty), includeSemantic, adminUserId, ct);
    }

    public async Task<WritingModelAnswerRevalidationResult> RevalidateAsync(WritingModelAnswerRevalidationRequest request, string adminUserId, CancellationToken ct = default)
    {
        var limit = Math.Clamp(request.Limit, 1, request.IncludeSemantic ? 10 : 500);
        var offset = Math.Max(0, request.Offset);

        var scenarioQuery = db.WritingScenarios.AsNoTracking().Where(s => s.Status == "published");
        if (!string.IsNullOrWhiteSpace(request.Profession))
        {
            var p = request.Profession.Trim().ToLowerInvariant();
            scenarioQuery = scenarioQuery.Where(s => s.Profession.ToLower() == p);
        }
        var scenarios = await scenarioQuery.ToDictionaryAsync(s => s.Id, ct);
        var ids = scenarios.Keys.ToList();

        var rowQuery = db.WritingTaskModelAnswers.Where(a => ids.Contains(a.ScenarioId) && a.ModelAnswerText != null);
        if (request.OnlyUnverified)
        {
            rowQuery = rowQuery.Where(a => a.ValidatorVersion != WritingRuleEngine.ValidatorVersion);
        }
        var total = await rowQuery.CountAsync(ct);
        var rows = (await rowQuery.ToListAsync(ct))
            .OrderBy(a => scenarios[a.ScenarioId].Profession, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => scenarios[a.ScenarioId].Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ScenarioId)
            .Skip(offset)
            .Take(limit)
            .ToList();

        var items = new List<WritingModelAnswerRevalidationItem>();
        var passed = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var scenario = scenarios[row.ScenarioId];
            var statusBefore = row.Status.ToString();
            var visibleBefore = row.IsCandidateVisible;
            var versionBefore = row.ValidatorVersion;
            WritingModelAnswerValidationReport report;
            if (!RulebookProfessionParser.TryParse(scenario.Profession, out var profession))
            {
                report = FailedReport(row.ScenarioId, "model_answer_profession_pack_unavailable");
            }
            else
            {
                var sentences = await LoadSentencesAsync(row.ScenarioId, ct);
                report = sentences.Count == 0
                    ? FailedReport(row.ScenarioId, "model_answer_case_notes_unavailable")
                    : await RunGateAsync(scenario, sentences, profession, row.ModelAnswerText!, request.IncludeSemantic, adminUserId, ct);
            }

            if (report.Passed) passed++;
            items.Add(new WritingModelAnswerRevalidationItem(
                row.ScenarioId, scenario.Title, scenario.Profession, scenario.LetterType,
                statusBefore, visibleBefore, versionBefore, report.Passed, report.HoldReason, report.BodyWordCount,
                report.DeterministicFindings, report.UnmappedSentences, report.SemanticChecked,
                report.SemanticViolations, report.SemanticError));

            if (!request.Apply) continue;
            var now = clock.GetUtcNow();
            row.ValidationReportJson = SerializeReport(report, null);
            row.BodyWordCount = report.BodyWordCount;
            row.UpdatedAt = now;
            if (report.Passed && (request.IncludeSemantic || semanticValidator is null))
            {
                // Only a FULL pass (deterministic + semantic) re-verifies a row.
                row.ValidatorVersion = WritingRuleEngine.ValidatorVersion;
                row.RulePackHash = report.RulePackHash;
                row.ValidatedAt = now;
                if (row.Status == WritingAssessmentModelAnswerStatus.HeldForReview
                    && string.Equals(row.HoldReason, "model_answer_revalidation_failed", StringComparison.Ordinal))
                {
                    row.Status = WritingAssessmentModelAnswerStatus.Ready;
                    row.HoldReason = null;
                }
            }
            else if (!report.Passed && !IsTransientHold(report.HoldReason))
            {
                // Never keep a failing answer candidate-visible (it already
                // is not, by CandidateVisibleVerified — this makes the state
                // explicit for admins and the catalogue gates).
                row.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
                row.IsCandidateVisible = false;
                row.HoldReason = "model_answer_revalidation_failed";
                row.ValidatorVersion = null;
            }
        }

        if (request.Apply) await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Model-answer revalidation by {AdminUserId}: checked {Checked} (offset {Offset}), passed {Passed}, applied {Applied}, semantic {Semantic}.",
            adminUserId, items.Count, offset, passed, request.Apply, request.IncludeSemantic);
        return new WritingModelAnswerRevalidationResult(
            WritingRuleEngine.ValidatorVersion, total, items.Count, passed, items.Count - passed,
            offset, limit, request.Apply, request.IncludeSemantic, items);
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
            .Take(500)
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
                // fresh + verified one is reused forever; a visible + stale
                // one is refreshed only on explicit request; an invisible one
                // is awaiting admin approval — regenerating would discard the
                // pending review and burn another provider call.
                if (!existing.IsCandidateVisible)
                {
                    skipped++;
                    items.Add(new WritingModelAnswerBatchItemResult(scenarioId, scenario.Title, "skipped", "awaiting_approval"));
                    continue;
                }
                var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing, ct);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
    /// Single-task background worker step (Option C). A Ready + fresh +
    /// verified answer is returned untouched with outcome <c>ready-skipped</c>
    /// and zero provider calls, so queue redelivery or stuck-job recovery
    /// after a restart can never trigger a duplicate paid AI request.
    /// Awaiting-approval answers are likewise left alone. Otherwise generates
    /// exactly once (including its bounded repair loop).
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
            var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing, ct);
            if (fresh)
            {
                logger.LogInformation(
                    "Model-answer worker skipped scenario {ScenarioId}: answer already Ready, fresh and verified, no provider call.",
                    scenarioId);
                return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "ready-skipped", "already_ready");
            }

            if (!existing.IsCandidateVisible
                && string.Equals(existing.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal))
            {
                return new WritingModelAnswerWorkItemResult(scenarioId, scenario.Title, "skipped", "awaiting_approval");
            }
        }

        WritingTaskModelAnswerDto outcome;
        try
        {
            outcome = await GenerateAsync(scenarioId, adminUserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        => string.Equals(holdReason, "model_answer_generation_failed", StringComparison.Ordinal)
           || string.Equals(holdReason, "model_answer_semantic_validator_unavailable", StringComparison.Ordinal);

    /// <summary>
    /// Enqueues background generation jobs (Option C) for published tasks
    /// lacking a fresh, verified approved Model Answer. Returns immediately:
    /// the worker grinds through jobs with no HTTP timeout pressure. Skips
    /// tasks with an already queued/processing job (no duplicate workflows)
    /// and Ready + fresh + verified tasks. Resumable: re-run to continue.
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
                var fresh = await IsFreshAsync(scenarioId, scenario.TaskPromptMarkdown ?? string.Empty, existing, ct);
                var awaitingApprovalVerified = !existing.IsCandidateVisible
                    && string.Equals(existing.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal);
                if (fresh || awaitingApprovalVerified)
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

    /// <summary>
    /// Fresh = generated from the CURRENT task + case notes AND verified under
    /// the CURRENT validator version. Either drifting makes the answer stale.
    /// </summary>
    private async Task<bool> IsFreshAsync(
        Guid scenarioId,
        string taskPrompt,
        WritingTaskModelAnswer existing,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(existing.SourceContentHash)) return false;
        if (!string.Equals(existing.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal)) return false;
        var sentences = await LoadSentencesAsync(scenarioId, ct);
        var current = ComputeSourceContentHash(
            taskPrompt,
            sentences.Select(s => (s.SentenceText, s.RelevanceLabel)));
        return string.Equals(existing.SourceContentHash, current, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WritingTaskModelAnswerDto?> ApproveAsync(Guid scenarioId, string adminUserId, CancellationToken ct = default)
    {
        var row = await db.WritingTaskModelAnswers.FirstOrDefaultAsync(x => x.ScenarioId == scenarioId, ct);
        if (row is null) return null;
        if (row.Status != WritingAssessmentModelAnswerStatus.Ready)
        {
            throw ApiException.Validation("model_answer_not_ready", "Only a Ready model answer can be approved.");
        }
        if (!string.Equals(row.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal))
        {
            // Addendum Rev8 §14: never publish an answer that has not passed the
            // CURRENT validator with zero violations.
            throw ApiException.Validation("model_answer_not_verified",
                $"This model answer has not been verified under the current Writing validator ({WritingRuleEngine.ValidatorVersion}). Revalidate or regenerate it first.");
        }

        row.IsCandidateVisible = true;
        row.ApprovedByUserId = adminUserId;
        row.ApprovedAt = clock.GetUtcNow();
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        return ToDto(row, scenario, includeReport: true);
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
        return ToDto(row, scenario, includeReport: true);
    }

    // ---------------------------------------------------------------------
    // The full Model Answer gate (Addendum Rev8 §7, §14)
    // ---------------------------------------------------------------------

    // P0 fix (12 Sep 2026): WritingRuleEngine.Lint() itself is now
    // exception-safe (RunDetectorSafely), but the live 224-answer
    // regeneration batch kept failing "model_answer_generation_failed"
    // afterward too - a direct, patient re-test of a task that hit that
    // path reproduced it identically post-fix. The remaining unguarded
    // surface is everything else in the gate that runs against a freshly
    // AI-generated draft none of the hand-written fixtures resemble:
    // WritingModelAnswerWordCounter, WritingModelAnswerGroundingValidator,
    // ExtractPatientAge and WritingCaseNotesMarkerExtractor. Wrap the whole
    // gate, not just Lint(), so any of them failing produces one clear hold
    // reason instead of an opaque, budget-burning "generation_failed" that
    // silently discards every AI call already paid for in this attempt.
    private async Task<WritingModelAnswerValidationReport> RunGateAsync(
        WritingScenario scenario,
        IReadOnlyList<WritingScenarioStructuredSentence> sentences,
        ExamProfession profession,
        string letterText,
        bool includeSemantic,
        string adminUserId,
        CancellationToken ct)
    {
        try
        {
            return await RunGateAsyncCore(scenario, sentences, profession, letterText, includeSemantic, adminUserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Model-answer validation gate threw for scenario {ScenarioId}", scenario.Id);
            return FailedReport(scenario.Id, "model_answer_internal_validation_error");
        }
    }

    private async Task<WritingModelAnswerValidationReport> RunGateAsyncCore(
        WritingScenario scenario,
        IReadOnlyList<WritingScenarioStructuredSentence> sentences,
        ExamProfession profession,
        string letterText,
        bool includeSemantic,
        string adminUserId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var words = WritingModelAnswerWordCounter.CountBodyWords(letterText);
        var wordCountOk = words is >= 180 and <= 200;
        var facts = sentences.Select(s => s.SentenceText).ToArray();
        var grounding = WritingModelAnswerGroundingValidator.Validate(letterText, facts);
        var caseNotesAll = string.Join("\n", facts);

        var lint = WritingRuleEngine.ModelAnswerBlockingFindings(ruleEngine.Lint(new WritingLintInput(
            LetterText: letterText,
            LetterType: scenario.LetterType,
            PatientAge: ExtractPatientAge(caseNotesAll),
            PatientIsMinor: ExtractPatientAge(caseNotesAll) is < 18,
            CaseNotesMarkers: WritingCaseNotesMarkerExtractor.Derive(caseNotesAll),
            Profession: profession,
            IsModelAnswer: true)));
        var findings = lint.Select(f => new WritingModelAnswerFindingDto(
            f.RuleId, f.Severity.ToString().ToLowerInvariant(), f.Message, f.Quote, f.FixSuggestion)).ToList();

        var rulePackHash = ruleEngine.RulePackFingerprint(profession);
        var rulebookVersion = ruleEngine.RulebookVersion(profession);
        var deterministicOk = wordCountOk && grounding.IsGrounded && findings.Count == 0;

        WritingModelAnswerSemanticResult? semantic = null;
        if (includeSemantic && semanticValidator is not null && deterministicOk)
        {
            semantic = await semanticValidator.ValidateAsync(new WritingModelAnswerSemanticRequest(
                scenario.Id,
                profession,
                scenario.LetterType,
                scenario.TaskPromptMarkdown ?? string.Empty,
                BuildCaseNotesText(sentences.Select(s => (s.SentenceText, s.RelevanceLabel))),
                letterText,
                rulePackHash,
                adminUserId), ct);
        }

        string? hold = !wordCountOk ? "model_answer_word_count_out_of_range"
            : !grounding.IsGrounded ? "model_answer_unmapped_sentence"
            : findings.Count > 0 ? "model_answer_rule_violations"
            : semantic is { Unavailable: true } ? "model_answer_semantic_validator_unavailable"
            : semantic is { Passed: false } ? "model_answer_semantic_violations"
            : null;

        return new WritingModelAnswerValidationReport(
            scenario.Id,
            hold is null,
            hold,
            words,
            wordCountOk,
            grounding.UnmappedSentences,
            findings,
            semantic is not null,
            semantic?.Passed,
            semantic?.Violations ?? [],
            semantic?.Error,
            WritingRuleEngine.ValidatorVersion,
            rulePackHash,
            rulebookVersion,
            WritingRev8HouseStyle.Version,
            now);
    }

    private static WritingModelAnswerValidationReport FailedReport(Guid scenarioId, string holdReason)
        => new(scenarioId, false, holdReason, 0, false, [], [], false, null, [], null,
            WritingRuleEngine.ValidatorVersion, string.Empty, null, WritingRev8HouseStyle.Version, DateTimeOffset.UtcNow);

    private void StoreVerified(
        WritingTaskModelAnswer row,
        string letter,
        WritingModelAnswerValidationReport report,
        DateTimeOffset now,
        string? modelUsed,
        string? rulebookVersion,
        string promptVersion,
        int repairCount)
    {
        row.Status = WritingAssessmentModelAnswerStatus.Ready;
        row.IsCandidateVisible = false; // still needs an explicit admin approve
        row.ModelAnswerText = letter;
        row.HoldReason = null;
        row.RulebookVersion = Truncate(rulebookVersion, 32);
        row.PromptVersion = promptVersion;
        row.ModelUsed = Truncate(modelUsed, 128);
        row.GeneratedAt = now;
        row.ApprovedByUserId = null;
        row.ApprovedAt = null;
        row.ValidatorVersion = WritingRuleEngine.ValidatorVersion;
        row.RulePackHash = report.RulePackHash;
        row.ValidatedAt = now;
        row.ValidationReportJson = SerializeReport(report, null);
        row.RepairCount = repairCount;
        row.BodyWordCount = report.BodyWordCount;
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];

    private static string SerializeReport(WritingModelAnswerValidationReport report, string? lastDraft)
        => JsonSerializer.Serialize(new { report, lastDraft }, ReportJson);

    /// <summary>
    /// Stored text is normalised to LF line endings with the outer whitespace
    /// trimmed — interior blank lines (the owner's mandatory spacing) are
    /// preserved exactly.
    /// </summary>
    internal static string NormaliseLetterText(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static int? ExtractPatientAge(string caseNotes)
    {
        var m = Regex.Match(caseNotes ?? string.Empty, @"\b(?:age|aged)\s*:?\s*(\d{1,3})\b|\b(\d{1,3})[\s-]*(?:years?|yrs?)[\s-]*old\b", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return int.TryParse(value, out var age) && age is > 0 and < 120 ? age : null;
    }

    private async Task<List<WritingScenarioStructuredSentence>> LoadSentencesAsync(Guid scenarioId, CancellationToken ct)
        => await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == scenarioId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

    internal static string DesignationFor(ExamProfession profession) => profession switch
    {
        ExamProfession.Medicine => "Doctor",
        ExamProfession.Nursing => "Nurse",
        ExamProfession.Pharmacy => "Pharmacist",
        ExamProfession.Physiotherapy => "Physiotherapist",
        ExamProfession.Dentistry => "Dentist",
        ExamProfession.Dietetics => "Dietitian",
        ExamProfession.OccupationalTherapy => "Occupational Therapist",
        ExamProfession.Optometry => "Optometrist",
        ExamProfession.Podiatry => "Podiatrist",
        ExamProfession.Radiography => "Radiographer",
        ExamProfession.SpeechPathology => "Speech Pathologist",
        ExamProfession.Veterinary => "Veterinarian",
        _ => "the writer's professional designation",
    };

    private static string BuildGenerationInput(WritingScenario scenario, ExamProfession profession, string taskSnapshot, string caseNotesText)
        => $$"""
            Produce the ONE reusable OET Writing MODEL ANSWER for this task. It is shown to every candidate who
            completes this exact task, so it must be a faithful, high-quality exemplar grounded ONLY in the case
            notes below, and it must comply with EVERY owner rule below with zero exceptions.
            Return ONE JSON object only (no prose before or after it):
            {"modelAnswerText":"...","whyThisWorks":["..."],"groundedFactReferences":["..."]}
            In modelAnswerText use \n for a line break and \n\n for a blank line.

            Profession: {{profession}}
            Letter type: {{WritingLetterTypeTaxonomy.ToLegacyLetterType(scenario.LetterType)}}
            Writer role: {{scenario.WriterRole ?? "(see task)"}}
            Date to use for the letter: {{scenario.TodayDate ?? "(see task)"}}
            Recipient (admin-confirmed, if any): {{scenario.RecipientRawText ?? "(see task)"}}
            Sign off after the closing phrase with exactly: {{DesignationFor(profession)}} (unless the task gives the writer's exact name).

            {{WritingRev8HouseStyle.ModelAnswerCanonicalRules}}

            Writing task:
            ---
            {{taskSnapshot}}
            ---
            Case notes (relevant and maybe-relevant sentences, in order):
            ---
            {{caseNotesText}}
            ---
            """;

    private static string BuildRepairInput(
        WritingScenario scenario,
        ExamProfession profession,
        string taskSnapshot,
        string caseNotesText,
        string previousDraft,
        WritingModelAnswerValidationReport report)
    {
        var sb = new StringBuilder();
        if (!report.WordCountOk)
            sb.AppendLine($"- Body word count is {report.BodyWordCount}; it must be 180-200 (introduction to closure inclusive).");
        foreach (var s in report.UnmappedSentences)
            sb.AppendLine($"- Sentence not traceable to the case notes (rewrite it from case-note facts only or remove it): \"{s}\"");
        foreach (var f in report.DeterministicFindings)
            sb.AppendLine($"- [{f.RuleId}] {f.Message}{(string.IsNullOrWhiteSpace(f.Quote) ? "" : $" (at: \"{f.Quote}\")")}");
        foreach (var v in report.SemanticViolations)
            sb.AppendLine($"- [{v.RuleId}] {v.Message}{(string.IsNullOrWhiteSpace(v.Quote) ? "" : $" (at: \"{v.Quote}\")")}");
        return $$"""
            Your previous draft of this OET Writing Model Answer FAILED validation. Repair ONLY the violations
            listed below; keep every other sentence, fact, paragraph and the layout unchanged. Then re-check the
            WHOLE letter against every owner rule before answering. Return ONE JSON object only:
            {"modelAnswerText":"...","whyThisWorks":["..."],"groundedFactReferences":["..."]}

            Violations to repair:
            {{sb}}
            Previous draft:
            ---
            {{previousDraft}}
            ---

            {{BuildGenerationInput(scenario, profession, taskSnapshot, caseNotesText)}}
            """;
    }

    private WritingTaskModelAnswerDto Hold(WritingTaskModelAnswer row, string reason)
    {
        row.Status = WritingAssessmentModelAnswerStatus.HeldForReview;
        row.IsCandidateVisible = false;
        row.HoldReason = reason;
        row.ValidatorVersion = null;
        row.UpdatedAt = clock.GetUtcNow();
        db.SaveChanges();
        return ToDto(row, null, includeReport: true);
    }

    private static WritingTaskModelAnswerDto ToDto(WritingTaskModelAnswer row, WritingScenario? scenario, bool includeReport)
    {
        var refs = ParseFactReferences(row.GroundedFactReferencesJson);
        var verifiedCurrent = string.Equals(row.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal);
        var verification = row.Status switch
        {
            WritingAssessmentModelAnswerStatus.Ready when verifiedCurrent && row.IsCandidateVisible => "verified_published",
            WritingAssessmentModelAnswerStatus.Ready when verifiedCurrent => "verified_awaiting_approval",
            WritingAssessmentModelAnswerStatus.Ready => "unverified_current_rules",
            WritingAssessmentModelAnswerStatus.Rejected => "rejected",
            _ => "held",
        };
        JsonElement? report = null;
        if (includeReport && !string.IsNullOrWhiteSpace(row.ValidationReportJson) && row.ValidationReportJson != "{}")
        {
            try { report = JsonDocument.Parse(row.ValidationReportJson).RootElement.Clone(); }
            catch (JsonException) { report = null; }
        }
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
            IsStale: row.Status == WritingAssessmentModelAnswerStatus.Ready && !verifiedCurrent,
            Title: scenario?.Title,
            Profession: scenario?.Profession,
            LetterType: scenario?.LetterType,
            ValidatorVersion: row.ValidatorVersion,
            RulePackHash: row.RulePackHash,
            ValidatedAt: row.ValidatedAt,
            RepairCount: row.RepairCount,
            BodyWordCount: row.BodyWordCount,
            VerificationStatus: verification,
            ValidationReport: report,
            PromptVersion: row.PromptVersion);
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
    /// generated from. Regeneration is justified only when this drifts (or
    /// the validator version changes) — never during normal candidate
    /// submissions.
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
            var draft = JsonSerializer.Deserialize<Draft>(completion[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            // Tolerate the grounded system prompt's generic GenerateContent
            // envelope {"content": "..."} (either the letter itself or a
            // nested JSON string carrying modelAnswerText).
            if (draft is { ModelAnswerText: null, Content: { Length: > 0 } content })
            {
                var nested = content.TrimStart().StartsWith('{') ? Parse(content) : null;
                return nested ?? draft with { ModelAnswerText = content };
            }
            return draft;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Draft(
        string? ModelAnswerText,
        IReadOnlyList<string>? WhyThisWorks,
        IReadOnlyList<string>? GroundedFactReferences,
        string? Content = null);
}
