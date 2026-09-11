using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// WS-B2: Authoring service for the unified writing task (the enriched
/// <see cref="WritingScenario"/>). Provides CRUD, lifecycle, publish-readiness
/// validation, and JSON import/export (spec §18).
/// </summary>
public interface IWritingTaskAuthoringService
{
    Task<(IReadOnlyList<WritingTaskDto> Items, int Total)> ListAsync(
        string? profession,
        string? letterType,
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<WritingTaskDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<WritingTaskDto> CreateAsync(WritingTaskUpsertDto request, ClaimsPrincipal user, CancellationToken ct = default);

    Task<WritingTaskDto?> UpdateAsync(Guid id, WritingTaskUpsertDto request, ClaimsPrincipal user, CancellationToken ct = default);

    Task<WritingTaskValidationResult?> ValidateAsync(Guid id, CancellationToken ct = default);

    Task<(WritingTaskDto? Task, WritingTaskValidationResult? Validation)> PublishAsync(Guid id, CancellationToken ct = default);

    Task<WritingTaskDto?> ArchiveAsync(Guid id, CancellationToken ct = default);

    Task<WritingTaskDto?> CloneAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default);

    Task<WritingTaskDto> ImportAsync(WritingTaskImportJson import, ClaimsPrincipal user, CancellationToken ct = default);

    Task<WritingTaskImportJson?> ExportAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Paginated preparation-status audit across tasks: canonical inputs,
    /// rulebook resolvability, Model Answer state/staleness, and the exact
    /// publish-gate blocking codes per task. Backs the admin backfill
    /// workflow; read-only.
    /// </summary>
    Task<(IReadOnlyList<WritingTaskPreparationStatusDto> Items, int Total)> GetPreparationStatusAsync(
        string? status,
        string? profession,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a bulk workflow action (<c>publish</c> | <c>archive</c> |
    /// <c>delete</c> | <c>force-delete</c>) to many tasks at once. Mirrors
    /// <c>ContentPaperService.BulkAsync</c> so the admin UI gets a uniform
    /// count-style result. <c>delete</c> skips a task that has learner data;
    /// <c>force-delete</c> purges submissions, grades, appeals, annotations,
    /// moderation and attempt events first (spec parity with Reading/Listening).
    /// </summary>
    Task<BulkActionResult> BulkAsync(string action, IReadOnlyList<string> ids, CancellationToken ct = default);
}

public sealed class WritingTaskAuthoringService(LearnerDbContext db, ILogger<WritingTaskAuthoringService> logger)
    : IWritingTaskAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(IReadOnlyList<WritingTaskDto> Items, int Total)> ListAsync(
        string? profession,
        string? letterType,
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = db.WritingScenarios.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(profession))
        {
            query = query.Where(s => s.Profession == profession);
        }

        if (!string.IsNullOrWhiteSpace(letterType))
        {
            query = query.Where(s => s.LetterType == letterType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                EF.Functions.ILike(s.Title, $"%{term}%") ||
                EF.Functions.ILike(s.Profession, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);
        var scenarios = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = scenarios.Select(MapToDto).ToList();
        return (items, total);
    }

    public async Task<WritingTaskDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return scenario is null ? null : MapToDto(scenario);
    }

    public async Task<WritingTaskDto> CreateAsync(WritingTaskUpsertDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var actorId = GetUserId(user) ?? "system";
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Status = "draft",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            AuthorId = actorId,
            ContentOwnerId = actorId,
        };

        ApplyUpsert(scenario, request);
        db.WritingScenarios.Add(scenario);

        await db.SaveChangesAsync(ct);
        return MapToDto(scenario);
    }

    public async Task<WritingTaskDto?> UpdateAsync(Guid id, WritingTaskUpsertDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return null;
        }

        ApplyUpsert(scenario, request);
        scenario.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapToDto(scenario);
    }

    public async Task<WritingTaskValidationResult?> ValidateAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return scenario is null ? null : await ValidateAsync(scenario, ct);
    }

    public async Task<(WritingTaskDto? Task, WritingTaskValidationResult? Validation)> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return (null, null);
        }

        // Heal legacy letter-type tokens at publish time: legacy ids resolve
        // to their modern catalogue code and unclear values to Other Letters,
        // so no task is ever published with an unclassifiable letter type.
        if (!string.IsNullOrWhiteSpace(scenario.LetterType))
        {
            scenario.LetterType = WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType(scenario.LetterType);
        }

        var validation = await ValidateAsync(scenario, ct);
        if (!validation.IsPublishReady)
        {
            return (null, validation);
        }

        scenario.Status = "published";
        scenario.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return (MapToDto(scenario), validation);
    }

    public async Task<WritingTaskDto?> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return null;
        }

        scenario.Status = "archived";
        scenario.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapToDto(scenario);
    }

    public async Task<WritingTaskDto?> CloneAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var source = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var clone = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(source.Title) ? source.Title : $"{source.Title} (copy)",
            Profession = source.Profession,
            LetterType = source.LetterType,
            Difficulty = source.Difficulty,
            Status = "draft",
            InternalCode = source.InternalCode,
            TaskPromptMarkdown = source.TaskPromptMarkdown,
            WriterRole = source.WriterRole,
            TodayDate = source.TodayDate,
            ExpectedPurpose = source.ExpectedPurpose,
            ExpectedAction = source.ExpectedAction,
            FixedInstructionsJson = source.FixedInstructionsJson,
            WordGuideMin = source.WordGuideMin,
            WordGuideMax = source.WordGuideMax,
            ReadingTimeSeconds = source.ReadingTimeSeconds,
            WritingTimeSeconds = source.WritingTimeSeconds,
            SimulationModes = source.SimulationModes,
            MarkingMode = source.MarkingMode,
            RetakePolicyJson = source.RetakePolicyJson,
            SourceProvenance = source.SourceProvenance,
            StimulusPdfMediaAssetId = source.StimulusPdfMediaAssetId,
            AnswerSheetPdfMediaAssetId = source.AnswerSheetPdfMediaAssetId,
            AuthorId = GetUserId(user) ?? source.AuthorId ?? "system",
            ContentOwnerId = GetUserId(user) ?? source.ContentOwnerId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.WritingScenarios.Add(clone);

        await db.SaveChangesAsync(ct);
        return MapToDto(clone);
    }

    public async Task<WritingTaskDto> ImportAsync(WritingTaskImportJson import, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var upsert = MapImportToUpsert(import);
        return await CreateAsync(upsert, user, ct);
    }

    public async Task<WritingTaskImportJson?> ExportAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return scenario is null ? null : MapToExportJson(scenario);
    }

    public async Task<(IReadOnlyList<WritingTaskPreparationStatusDto> Items, int Total)> GetPreparationStatusAsync(
        string? status,
        string? profession,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.WritingScenarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(s => s.Profession == profession);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(s => s.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        // Grouped client-side: server-side GroupBy into a dictionary is not
        // translatable by all EF providers (notably the InMemory test
        // provider); this single-query shape works everywhere.
        var sentenceRows = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(x => ids.Contains(x.ScenarioId))
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);
        var sentenceGroups = sentenceRows
            .GroupBy(x => x.ScenarioId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Ordinal).ToList());
        var answers = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => ids.Contains(a.ScenarioId))
            .ToDictionaryAsync(a => a.ScenarioId, ct);

        // Pack resolvability uses the exact runtime resolver so the audit
        // agrees with candidate grading. One batched query keeps this cheap.
        var normalizedProfessions = rows
            .Select(r => (r.Profession ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_'))
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();
        var releasedPacks = await db.WritingAssessmentPackVersions.AsNoTracking()
            .Where(x => normalizedProfessions.Contains(x.Profession)
                && x.Status == WritingAssessmentReleaseStatus.Approved
                && x.CandidateFacing)
            .ToListAsync(ct);
        var releasedByProfession = releasedPacks
            .GroupBy(x => x.Profession)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<WritingTaskPreparationStatusDto>(rows.Count);
        foreach (var s in rows)
        {
            sentenceGroups.TryGetValue(s.Id, out var sentences);
            sentences ??= new List<WritingScenarioStructuredSentence>();
            var hasPrompt = !string.IsNullOrWhiteSpace(s.TaskPromptMarkdown);
            var rulebookResolvable = !string.IsNullOrWhiteSpace(s.Profession)
                && RulebookProfessionParser.TryParse(s.Profession, out _);
            answers.TryGetValue(s.Id, out var answer);
            // Rev8: approved == verified under the CURRENT validator version.
            var answerApproved = WritingTaskModelAnswerService.IsVerifiedForCandidates(answer);
            bool? stale = null;
            if (answer is not null && !string.IsNullOrWhiteSpace(answer.SourceContentHash))
            {
                var current = WritingTaskModelAnswerService.ComputeSourceContentHash(
                    s.TaskPromptMarkdown ?? string.Empty,
                    sentences.Select(x => (x.SentenceText, x.RelevanceLabel)));
                stale = !string.Equals(answer.SourceContentHash, current, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(answer.ValidatorVersion, WritingRuleEngine.ValidatorVersion, StringComparison.Ordinal);
            }

            var normalizedProfession = (s.Profession ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            var packLetterType = WritingLetterTypeTaxonomy.ToPackLetterType(s.LetterType);
            var packResolvable = PackResolvableFromBatch(releasedByProfession, normalizedProfession, packLetterType);

            var caseNotesText = string.Join("\n", sentences
                .Select(x => (x.SentenceText ?? string.Empty).Trim())
                .Where(x => x.Length > 0));
            var understanding = WritingTaskUnderstandingService.Understand(
                s.TaskPromptMarkdown ?? string.Empty,
                caseNotesText,
                packLetterType);

            var blocking = BuildBlockingCodes(
                s.Title,
                s.Profession,
                s.LetterType,
                hasPrompt,
                sentences.Count > 0,
                rulebookResolvable,
                answerApproved,
                s.WordGuideMin,
                s.WordGuideMax,
                packResolvable: packResolvable,
                recipientResolved: !string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal),
                diagnosisPresent: !string.IsNullOrWhiteSpace(understanding.DiagnosisOrPlanEvidence),
                classificationConflicting: understanding.ConflictingEvidence);

            items.Add(new WritingTaskPreparationStatusDto
            {
                ScenarioId = s.Id,
                Title = s.Title,
                Profession = s.Profession,
                LetterType = s.LetterType,
                Status = s.Status,
                HasTaskPrompt = hasPrompt,
                CaseNoteSentenceCount = sentences.Count,
                RulebookResolvable = rulebookResolvable,
                PackResolvable = packResolvable,
                RecipientResolved = !string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal),
                ModelAnswerStatus = answer?.Status.ToString(),
                ModelAnswerApproved = answerApproved,
                ModelAnswerStale = stale,
                PublishReady = blocking.Count == 0,
                BlockingCodes = blocking.ToList(),
            });
        }

        return (items, total);
    }

    // ----- bulk workflow actions (parity with ContentPaperService.BulkAsync) -----

    private static readonly string[] SupportedBulkActions = ["publish", "archive", "delete", "force-delete"];
    private const int BulkErrorCap = 100;

    private enum BulkItemOutcome { Succeeded, Skipped }

    public async Task<BulkActionResult> BulkAsync(string action, IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (Array.IndexOf(SupportedBulkActions, normalized) < 0)
        {
            throw new ArgumentException($"Unknown bulk action '{action}'.", nameof(action));
        }

        var totalRequested = ids?.Count ?? 0;
        var distinct = (ids ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var succeeded = 0;
        var skipped = 0;
        var errors = new List<string>();

        // Single transaction across all per-item mutations so a mid-batch fatal
        // error rolls the whole batch back. InMemory (tests) has no transactions.
        var supportsTransactions = !string.Equals(
            db.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal);
        await using var tx = supportsTransactions ? await db.Database.BeginTransactionAsync(ct) : null;

        foreach (var raw in distinct)
        {
            if (!Guid.TryParse(raw, out var id))
            {
                skipped++;
                continue;
            }

            try
            {
                var outcome = await ApplyBulkOneAsync(normalized, id, ct);
                if (outcome == BulkItemOutcome.Succeeded) succeeded++;
                else skipped++;
            }
            catch (InvalidOperationException ex)
            {
                // Validation / gate failure for this id — record and continue.
                if (errors.Count < BulkErrorCap) errors.Add($"{raw}: {ex.Message}");
                else if (errors.Count == BulkErrorCap) errors.Add($"… and more (cap {BulkErrorCap} reached).");
            }
        }

        if (tx is not null) await tx.CommitAsync(ct);

        return new BulkActionResult(totalRequested, succeeded, skipped, errors.Count, errors.ToArray());
    }

    private async Task<BulkItemOutcome> ApplyBulkOneAsync(string action, Guid id, CancellationToken ct)
    {
        switch (action)
        {
            case "publish":
            {
                var (task, _) = await PublishAsync(id, ct);
                // null => not found OR not publish-ready: a no-op for this id.
                return task is null ? BulkItemOutcome.Skipped : BulkItemOutcome.Succeeded;
            }
            case "archive":
            {
                var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (scenario is null) return BulkItemOutcome.Skipped;
                if (scenario.Status == "archived") return BulkItemOutcome.Skipped;
                scenario.Status = "archived";
                scenario.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return BulkItemOutcome.Succeeded;
            }
            case "delete":
                return await HardDeleteAsync(id, force: false, ct);
            case "force-delete":
                return await HardDeleteAsync(id, force: true, ct);
            default:
                throw new ArgumentException($"Unknown bulk action '{action}'.", nameof(action));
        }
    }

    /// <summary>
    /// Permanently removes a writing task (<see cref="WritingScenario"/>) and its
    /// authoring children. Per the gating decision there is NO archive-first
    /// requirement. Plain delete (<paramref name="force"/> = false) refuses a task
    /// that has learner submissions or attempt events; force-delete first purges
    /// that learner data. Uses load + RemoveRange (not ExecuteDelete) so the
    /// InMemory test provider can exercise the cascade. There are no DB-level FK
    /// constraints between these tables, so delete order is not load-bearing.
    /// </summary>
    private async Task<BulkItemOutcome> HardDeleteAsync(Guid id, bool force, CancellationToken ct)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null) return BulkItemOutcome.Skipped;

        var submissionIds = await db.WritingSubmissions
            .Where(s => s.ScenarioId == id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var hasAttemptEvents = await db.WritingAttemptEvents.AnyAsync(e => e.ScenarioId == id, ct);
        var hasLearnerData = submissionIds.Count > 0 || hasAttemptEvents;

        if (hasLearnerData && !force)
        {
            throw new InvalidOperationException(
                "Task has learner submissions or attempts; use force-delete to purge them.");
        }

        if (force && submissionIds.Count > 0)
        {
            db.WritingFeedbackAnnotations.RemoveRange(
                await db.WritingFeedbackAnnotations.Where(a => submissionIds.Contains(a.SubmissionId)).ToListAsync(ct));
            db.WritingModerations.RemoveRange(
                await db.WritingModerations.Where(m => submissionIds.Contains(m.SubmissionId)).ToListAsync(ct));
            db.WritingScoreAppeals.RemoveRange(
                await db.WritingScoreAppeals.Where(a => submissionIds.Contains(a.SubmissionId)).ToListAsync(ct));
            db.WritingGrades.RemoveRange(
                await db.WritingGrades.Where(g => submissionIds.Contains(g.SubmissionId)).ToListAsync(ct));
            db.WritingSubmissions.RemoveRange(
                await db.WritingSubmissions.Where(s => s.ScenarioId == id).ToListAsync(ct));
        }

        if (force)
        {
            db.WritingAttemptEvents.RemoveRange(
                await db.WritingAttemptEvents.Where(e => e.ScenarioId == id).ToListAsync(ct));
        }

        // Scenario-owned authoring children + any per-scenario visibility override.
        db.WritingScenarioStructuredSentences.RemoveRange(
            await db.WritingScenarioStructuredSentences.Where(x => x.ScenarioId == id).ToListAsync(ct));
        db.WritingScenarioEmbeddings.RemoveRange(
            await db.WritingScenarioEmbeddings.Where(x => x.ScenarioId == id).ToListAsync(ct));
        db.WritingResultVisibilityConfigs.RemoveRange(
            await db.WritingResultVisibilityConfigs.Where(x => x.ScenarioId == id).ToListAsync(ct));

        db.WritingScenarios.Remove(scenario);
        await db.SaveChangesAsync(ct);
        return BulkItemOutcome.Succeeded;
    }

    // ----- mapping helpers -----

    private void ApplyUpsert(WritingScenario scenario, WritingTaskUpsertDto request)
    {
        scenario.Title = request.Title?.Trim() ?? string.Empty;
        scenario.Profession = request.Profession?.Trim() ?? string.Empty;
        // Catalogue taxonomy gate: stored letter types are always valid modern
        // codes by construction. Retired Response (LT-RP) and anything
        // unrecognised normalise to Other Letters (LT-OT); empty is preserved
        // so required-field validation still fires for truly missing values.
        scenario.LetterType = WritingLetterTypeTaxonomy.NormalizeCatalogueLetterTypeOrEmpty(request.LetterType);
        if (request.Difficulty is { } difficulty) scenario.Difficulty = Math.Clamp(difficulty, 1, 5);
        scenario.InternalCode = string.IsNullOrWhiteSpace(request.InternalCode) ? null : request.InternalCode.Trim();
        scenario.WriterRole = request.WriterRole;
        scenario.TodayDate = request.TodayDate;
        scenario.TaskPromptMarkdown = request.TaskPromptMarkdown;
        scenario.ExpectedPurpose = request.ExpectedPurpose;
        scenario.ExpectedAction = request.ExpectedAction;
        scenario.FixedInstructionsJson = SerializeFixedInstructions(request.FixedInstructions);

        if (request.WordGuideMin is { } min) scenario.WordGuideMin = min;
        if (request.WordGuideMax is { } max) scenario.WordGuideMax = max;
        if (request.ReadingTimeSeconds is { } rts) scenario.ReadingTimeSeconds = rts;
        if (request.WritingTimeSeconds is { } wts) scenario.WritingTimeSeconds = wts;
        if (!string.IsNullOrWhiteSpace(request.SimulationModes)) scenario.SimulationModes = request.SimulationModes.Trim();
        if (!string.IsNullOrWhiteSpace(request.MarkingMode)) scenario.MarkingMode = request.MarkingMode.Trim();

        scenario.StimulusPdfMediaAssetId = string.IsNullOrWhiteSpace(request.StimulusPdfMediaAssetId)
            ? null
            : request.StimulusPdfMediaAssetId.Trim();

        scenario.AnswerSheetPdfMediaAssetId = string.IsNullOrWhiteSpace(request.AnswerSheetPdfMediaAssetId)
            ? null
            : request.AnswerSheetPdfMediaAssetId.Trim();

        scenario.SourceProvenance = request.SourceProvenance;

        // Admin-confirmed overrides (see WritingScenario doc-comments): once
        // set, these take precedence over WritingTaskUnderstandingService's
        // heuristic at the publish gate. Deliberately STICKY, unlike the
        // fields above — ApplyUpsert is otherwise a full-replace (any PUT
        // omitting a field wipes it), which would silently re-expose a task
        // to the heuristic's guesses the next time anyone edits any other
        // field. Only set when the request actually carries a value; never
        // cleared by omission.
        if (!string.IsNullOrWhiteSpace(request.RecipientRawText)) scenario.RecipientRawText = request.RecipientRawText.Trim();
        if (!string.IsNullOrWhiteSpace(request.RecipientNormalizedJson)) scenario.RecipientNormalizedJson = request.RecipientNormalizedJson;
        if (!string.IsNullOrWhiteSpace(request.ConfirmedPurposeText)) scenario.ConfirmedPurposeText = request.ConfirmedPurposeText.Trim();

        if (request.IntegrityAcknowledged == true && scenario.IntegrityAcknowledgedAt is null)
        {
            scenario.IntegrityAcknowledgedAt = DateTimeOffset.UtcNow;
            scenario.IntegrityAcknowledgedById ??= scenario.ContentOwnerId;
        }
        else if (request.IntegrityAcknowledged == false)
        {
            scenario.IntegrityAcknowledgedAt = null;
            scenario.IntegrityAcknowledgedById = null;
        }
    }

    /// <summary>
    /// Maps a scenario to the frontend-aligned <see cref="WritingTaskDto"/>.
    /// Public so the tutor/expert marking-context endpoint can reuse the exact
    /// same shape the admin Task Builder serves (lib/writing/types.ts WritingTaskDto).
    /// </summary>
    public static WritingTaskDto MapToDto(WritingScenario scenario)
    {
        var pdfId = scenario.StimulusPdfMediaAssetId;
        return new WritingTaskDto
        {
            Id = scenario.Id,
            InternalCode = scenario.InternalCode,
            Title = scenario.Title,
            Profession = scenario.Profession,
            LetterType = scenario.LetterType,
            Difficulty = scenario.Difficulty,
            Status = scenario.Status,
            Version = scenario.Version,
            WriterRole = scenario.WriterRole,
            TodayDate = scenario.TodayDate,
            TaskPromptMarkdown = scenario.TaskPromptMarkdown,
            ExpectedPurpose = scenario.ExpectedPurpose,
            ExpectedAction = scenario.ExpectedAction,
            FixedInstructions = DeserializeFixedInstructions(scenario.FixedInstructionsJson),
            WordGuideMin = scenario.WordGuideMin,
            WordGuideMax = scenario.WordGuideMax,
            ReadingTimeSeconds = scenario.ReadingTimeSeconds,
            WritingTimeSeconds = scenario.WritingTimeSeconds,
            SimulationModes = scenario.SimulationModes,
            MarkingMode = scenario.MarkingMode,
            SourceProvenance = scenario.SourceProvenance,
            IntegrityAcknowledged = scenario.IntegrityAcknowledgedAt is not null,
            StimulusPdfMediaAssetId = pdfId,
            StimulusPdfDownloadPath = string.IsNullOrWhiteSpace(pdfId) ? null : $"/v1/media/{pdfId}/content",
            AnswerSheetPdfMediaAssetId = scenario.AnswerSheetPdfMediaAssetId,
            RecipientRawText = scenario.RecipientRawText,
            RecipientNormalizedJson = scenario.RecipientNormalizedJson,
            ConfirmedPurposeText = scenario.ConfirmedPurposeText,
            CreatedAt = scenario.CreatedAt,
            UpdatedAt = scenario.UpdatedAt,
        };
    }

    // ----- validation (spec §3/§19.2/§22 + canonical-input publishing gate) -----

    /// <summary>
    /// Production-readiness gate for candidate publication. A task is
    /// publish-ready only when the runtime grading path can serve it WITHOUT
    /// live OCR/PDF extraction: complete canonical case-note text, the exact
    /// Writing Task, a valid catalogue letter type, a resolvable
    /// profession rulebook, and an approved pre-generated Model Answer.
    /// Anything incomplete stays in draft/admin review — broken tasks are
    /// never exposed for candidates to discover.
    /// </summary>
    private async Task<WritingTaskValidationResult> ValidateAsync(WritingScenario scenario, CancellationToken ct)
    {
        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(x => x.ScenarioId == scenario.Id)
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);
        var hasCaseNotes = sentences.Count > 0;
        var rulebookResolvable = !string.IsNullOrWhiteSpace(scenario.Profession)
            && RulebookProfessionParser.TryParse(scenario.Profession, out _);
        var hasApprovedModelAnswer = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => a.ScenarioId == scenario.Id)
            .AnyAsync(WritingTaskModelAnswerService.CandidateVisibleVerified, ct);

        // Same resolver the runtime grading path uses: a task is publishable
        // only if candidate grading can resolve a released pack for it.
        var pack = await WritingAssessmentPreflightService.ResolvePackForCatalogueAsync(
            db, scenario.Profession, scenario.LetterType, ct);

        // Same recipient/task interpretation the runtime depends on: resolve
        // it here, at publication, where an admin can confirm or correct it.
        var caseNotesText = string.Join("\n", sentences
            .Select(x => (x.SentenceText ?? string.Empty).Trim())
            .Where(x => x.Length > 0));
        var packLetterType = WritingLetterTypeTaxonomy.ToPackLetterType(scenario.LetterType);
        var understanding = WritingTaskUnderstandingService.Understand(
            scenario.TaskPromptMarkdown ?? string.Empty,
            caseNotesText,
            packLetterType);

        var codes = BuildBlockingCodes(
            scenario.Title,
            scenario.Profession,
            scenario.LetterType,
            hasTaskPrompt: !string.IsNullOrWhiteSpace(scenario.TaskPromptMarkdown),
            hasCaseNotes: hasCaseNotes,
            rulebookResolvable: rulebookResolvable,
            hasApprovedModelAnswer: hasApprovedModelAnswer,
            scenario.WordGuideMin,
            scenario.WordGuideMax,
            packResolvable: pack is not null,
            // An admin-confirmed override always wins over the heuristic — a
            // task is never blocked (or silently re-guessed) once a human has
            // confirmed or corrected the recipient/purpose in their own words.
            recipientResolved: !string.IsNullOrWhiteSpace(scenario.RecipientRawText)
                || !string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal),
            diagnosisPresent: !string.IsNullOrWhiteSpace(scenario.ConfirmedPurposeText)
                || !string.IsNullOrWhiteSpace(understanding.DiagnosisOrPlanEvidence),
            classificationConflicting: understanding.ConflictingEvidence);

        return new WritingTaskValidationResult
        {
            IsPublishReady = codes.Count == 0,
            Issues = codes.Select(c => Error(c, BlockingMessage(c))).ToList(),
        };
    }

    /// <summary>
    /// Single source of production-readiness blocking codes, shared by the
    /// publish gate (<see cref="ValidateAsync(WritingScenario, CancellationToken)"/>)
    /// and the preparation-status audit (<see cref="GetPreparationStatusAsync"/>)
    /// so both always agree on what "ready" means.
    /// </summary>
    /// <summary>
    /// Batch mirror of the runtime pack fallback in
    /// <see cref="WritingAssessmentPreflightService"/> for the
    /// preparation-status audit (avoids N+1 pack queries across the page).
    /// </summary>
    private static bool PackResolvableFromBatch(
        Dictionary<string, List<WritingAssessmentPackVersion>> releasedByProfession,
        string normalizedProfession,
        string packLetterType)
    {
        if (string.IsNullOrWhiteSpace(normalizedProfession)
            || !releasedByProfession.TryGetValue(normalizedProfession, out var packs)
            || packs.Count == 0)
            return false;
        if (packs.Any(x => string.Equals(x.LetterType, packLetterType, StringComparison.Ordinal)))
            return true;
        // transfer / referral_to_gp genuinely require their detailed pack.
        if (packLetterType is "transfer" or "referral_to_gp")
            return false;
        // Every other letter type grades under the profession's generic
        // packs (Other Letters is first-class); any released profession
        // pack suffices.
        return true;
    }

    private static IReadOnlyList<string> BuildBlockingCodes(
        string? title,
        string? profession,
        string? letterType,
        bool hasTaskPrompt,
        bool hasCaseNotes,
        bool rulebookResolvable,
        bool hasApprovedModelAnswer,
        int wordGuideMin,
        int wordGuideMax,
        bool packResolvable,
        bool recipientResolved,
        bool diagnosisPresent,
        bool classificationConflicting)
    {
        var codes = new List<string>();

        if (string.IsNullOrWhiteSpace(title)) codes.Add("title_required");
        if (string.IsNullOrWhiteSpace(profession)) codes.Add("profession_required");

        if (string.IsNullOrWhiteSpace(letterType))
        {
            codes.Add("letter_type_required");
        }
        else if (!WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(letterType))
        {
            // Defence in depth: ApplyUpsert/import normalise by construction and
            // PublishAsync heals legacy tokens, but any legacy row (e.g. retired
            // LT-RP) reaching the gate another way must be blocked until it is
            // reclassified — never published as-is.
            codes.Add("letter_type_unsupported");
        }

        // Canonical grading inputs: the exact Writing Task text is the grading
        // source — a task without it cannot be scored.
        if (!hasTaskPrompt) codes.Add("written_task_required");

        // Canonical grading inputs: readable case-note sentences must exist at
        // preparation time. A stimulus PDF alone is NOT sufficient — runtime
        // grading never performs live OCR/PDF extraction.
        if (!hasCaseNotes) codes.Add("case_notes_required");

        // The applicable profession-specific Writing Rulebook must resolve.
        if (!string.IsNullOrWhiteSpace(profession) && !rulebookResolvable)
        {
            codes.Add("rulebook_unresolvable");
        }

        // The runtime release gate must resolve a released pack for this
        // exact task (same resolver as candidate grading). Without it the
        // learner would discover the gap 40 minutes later.
        if (!string.IsNullOrWhiteSpace(profession)
            && rulebookResolvable
            && !string.IsNullOrWhiteSpace(letterType)
            && WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(letterType)
            && !packResolvable)
        {
            codes.Add("profession_pack_unapproved");
        }

        // Recipient/task interpretation the runtime depends on: an admin must
        // confirm or correct it before candidates see the task. Never
        // auto-guessed — and never discovered by the learner at submit time.
        if (hasTaskPrompt && hasCaseNotes && !recipientResolved)
        {
            codes.Add("recipient_unresolved");
        }

        if (hasTaskPrompt && hasCaseNotes && !diagnosisPresent)
        {
            codes.Add("diagnosis_or_request_unresolved");
        }

        if (classificationConflicting)
        {
            codes.Add("task_classification_conflict");
        }

        // ONE pre-generated, quality-approved Model Answer must exist before
        // release. Normal candidate submissions reuse it and never trigger a
        // fresh generation.
        if (!hasApprovedModelAnswer) codes.Add("model_answer_not_approved");

        if (wordGuideMin <= 0 || wordGuideMax < wordGuideMin) codes.Add("word_guide_invalid");

        return codes;
    }

    private static string BlockingMessage(string code) => code switch
    {
        "title_required" => "Title is required.",
        "profession_required" => "Profession is required.",
        "letter_type_required" => "Letter type is required.",
        "letter_type_unsupported" => "Letter type is not a valid Writing catalogue letter type.",
        "written_task_required" => "Exact Writing Task text is required before publishing.",
        "case_notes_required" => "Canonical case-note text is required before publishing. Extract it from the stimulus PDF into structured case notes first.",
        "rulebook_unresolvable" => "Profession does not resolve to a supported Writing Rulebook.",
        "profession_pack_unapproved" => "No approved candidate-facing assessment pack is released for this profession and letter type. Approve one before publishing.",
        "recipient_unresolved" => "Recipient could not be resolved from the task and case notes. Confirm or correct the recipient before publishing.",
        "diagnosis_or_request_unresolved" => "No diagnosis, plan, or request evidence was found in the task or case notes. Confirm the clinical purpose before publishing.",
        "task_classification_conflict" => "Task text carries conflicting letter-type signals. Confirm the intended letter type before publishing.",
        "model_answer_not_approved" => "An approved pre-generated Model Answer is required before publishing. Generate it once, quality-check it, then approve it.",
        "word_guide_invalid" => "Word guide must have min > 0 and max >= min.",
        _ => code,
    };

    private static WritingTaskValidationIssue Error(string code, string message) => new()
    {
        Code = code,
        Severity = "error",
        Message = message,
    };

    // ----- import / export (spec §18) -----

    private static WritingTaskUpsertDto MapImportToUpsert(WritingTaskImportJson import)
    {
        return new WritingTaskUpsertDto
        {
            InternalCode = import.InternalCode,
            Title = BuildImportTitle(import),
            Profession = (import.Profession ?? string.Empty).Trim(),
            LetterType = MapTaskTypeToLetterType(import.TaskType),
            WriterRole = import.CaseNotes?.CandidateRole,
            TodayDate = import.CaseNotes?.TodayDate,
            TaskPromptMarkdown = import.WritingTask?.Instruction,
            ExpectedPurpose = import.Marking?.ExpectedPurpose,
            ExpectedAction = import.Marking?.ExpectedAction,
            FixedInstructions = import.WritingTask?.FixedInstructions?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
            WordGuideMin = import.WritingTask?.WordGuide?.Min,
            WordGuideMax = import.WritingTask?.WordGuide?.Max,
            ReadingTimeSeconds = import.Duration?.ReadingTimeSeconds,
            WritingTimeSeconds = import.Duration?.WritingTimeSeconds,
            SourceProvenance = null,
            IntegrityAcknowledged = null,
        };
    }

    private static string BuildImportTitle(WritingTaskImportJson import)
    {
        if (!string.IsNullOrWhiteSpace(import.TaskTitle))
        {
            return import.TaskTitle!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(import.InternalCode))
        {
            return import.InternalCode!.Trim();
        }

        var profession = string.IsNullOrWhiteSpace(import.Profession) ? "Writing" : import.Profession!.Trim();
        var type = string.IsNullOrWhiteSpace(import.TaskType) ? "task" : import.TaskType!.Trim();
        return $"{profession} — {type}";
    }

    private static WritingTaskImportJson MapToExportJson(WritingScenario scenario)
    {
        return new WritingTaskImportJson
        {
            TaskTitle = scenario.Title,
            InternalCode = scenario.InternalCode,
            Profession = scenario.Profession,
            TaskType = scenario.LetterType,
            Duration = new WritingImportDuration
            {
                ReadingTimeSeconds = scenario.ReadingTimeSeconds,
                WritingTimeSeconds = scenario.WritingTimeSeconds,
            },
            CaseNotes = new WritingImportCaseNotes
            {
                TodayDate = scenario.TodayDate,
                CandidateRole = scenario.WriterRole,
            },
            WritingTask = new WritingImportWritingTask
            {
                Instruction = scenario.TaskPromptMarkdown ?? string.Empty,
                FixedInstructions = DeserializeFixedInstructions(scenario.FixedInstructionsJson),
                WordGuide = new WritingImportWordGuide
                {
                    Min = scenario.WordGuideMin,
                    Max = scenario.WordGuideMax,
                },
            },
            Marking = new WritingImportMarking
            {
                ExpectedPurpose = scenario.ExpectedPurpose,
                ExpectedAction = scenario.ExpectedAction,
            },
        };
    }

    /// <summary>
    /// Maps an import task-type label (human or canonical) to a catalogue letter-type code.
    /// Known labels map to their valid modern code; retired Response-like labels
    /// ("response", "reply", "update", "advice …", "LT-RP") and anything
    /// unrecognised map to Other Letters (LT-OT) — never forced into a known
    /// category, and never persisted as LT-RP.
    /// </summary>
    private static string MapTaskTypeToLetterType(string? taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
        {
            return string.Empty;
        }

        var trimmed = taskType.Trim();

        // Already a catalogue code (any case)? Canonicalise to uppercase LT-*.
        if (WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(trimmed))
        {
            return WritingLetterTypeTaxonomy.NormalizeCatalogueLetterType(trimmed);
        }

        var key = trimmed.ToLowerInvariant();

        // Legacy canonical ids from the ContentPaper pipeline.
        if (WritingContentStructure.IsCanonicalLetterType(trimmed))
        {
            return key switch
            {
                "routine_referral" => WritingLetterTypeTaxonomy.RoutineReferral,
                "urgent_referral" => WritingLetterTypeTaxonomy.UrgentReferral,
                "update_discharge" => WritingLetterTypeTaxonomy.Discharge,
                "transfer_letter" => WritingLetterTypeTaxonomy.Transfer,
                "non_medical_referral" => WritingLetterTypeTaxonomy.NonMedical,
                // update_referral_specialist_to_gp has no dedicated catalogue
                // code: route to the explicit fallback instead of guessing.
                _ => WritingLetterTypeTaxonomy.OtherLetters,
            };
        }

        return key switch
        {
            "referral letter" => WritingLetterTypeTaxonomy.RoutineReferral,
            "routine referral" => WritingLetterTypeTaxonomy.RoutineReferral,
            "routine referral letter" => WritingLetterTypeTaxonomy.RoutineReferral,
            "urgent referral letter" => WritingLetterTypeTaxonomy.UrgentReferral,
            "urgent referral" => WritingLetterTypeTaxonomy.UrgentReferral,
            "discharge letter" => WritingLetterTypeTaxonomy.Discharge,
            "discharge summary" => WritingLetterTypeTaxonomy.Discharge,
            "update letter" => WritingLetterTypeTaxonomy.Discharge,
            "transfer letter" => WritingLetterTypeTaxonomy.Transfer,
            // Response-like / advice-like legacy labels: explicit fallback.
            "response" => WritingLetterTypeTaxonomy.OtherLetters,
            "response letter" => WritingLetterTypeTaxonomy.OtherLetters,
            "reply" => WritingLetterTypeTaxonomy.OtherLetters,
            "update" => WritingLetterTypeTaxonomy.OtherLetters,
            "advice letter" => WritingLetterTypeTaxonomy.OtherLetters,
            "letter of advice" => WritingLetterTypeTaxonomy.OtherLetters,
            "advice_letter" => WritingLetterTypeTaxonomy.OtherLetters,
            "other" => WritingLetterTypeTaxonomy.OtherLetters,
            "other letters" => WritingLetterTypeTaxonomy.OtherLetters,
            _ => WritingLetterTypeTaxonomy.OtherLetters,
        };
    }

    // ----- json (de)serialization for the scenario's stored columns -----

    private static string SerializeFixedInstructions(List<string>? instructions)
    {
        var clean = instructions?
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .ToList() ?? new List<string>();
        return JsonSerializer.Serialize(clean, JsonOptions);
    }

    private static List<string> DeserializeFixedInstructions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }
}

// ===== DTOs (mirror lib/writing/types.ts; camelCase via ASP.NET web defaults) =====

public sealed record WritingTaskDto
{
    public Guid Id { get; init; }
    public string? InternalCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string LetterType { get; init; } = string.Empty;
    public int Difficulty { get; init; }
    public string Status { get; init; } = "draft";
    public int Version { get; init; }
    public string? WriterRole { get; init; }
    public string? TodayDate { get; init; }
    public string? TaskPromptMarkdown { get; init; }
    public string? ExpectedPurpose { get; init; }
    public string? ExpectedAction { get; init; }
    public List<string> FixedInstructions { get; init; } = new();
    public int WordGuideMin { get; init; }
    public int WordGuideMax { get; init; }
    public int ReadingTimeSeconds { get; init; }
    public int WritingTimeSeconds { get; init; }
    public string SimulationModes { get; init; } = "both";
    public string MarkingMode { get; init; } = "tutor";
    public string? SourceProvenance { get; init; }
    public bool IntegrityAcknowledged { get; init; }
    public string? StimulusPdfMediaAssetId { get; init; }
    public string? StimulusPdfDownloadPath { get; init; }
    public string? AnswerSheetPdfMediaAssetId { get; init; }
    public string? RecipientRawText { get; init; }
    public string? RecipientNormalizedJson { get; init; }
    public string? ConfirmedPurposeText { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record WritingTaskUpsertDto
{
    public string? InternalCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string LetterType { get; init; } = string.Empty;
    public int? Difficulty { get; init; }
    public string? WriterRole { get; init; }
    public string? TodayDate { get; init; }
    public string? TaskPromptMarkdown { get; init; }
    public string? ExpectedPurpose { get; init; }
    public string? ExpectedAction { get; init; }
    public List<string>? FixedInstructions { get; init; }
    public int? WordGuideMin { get; init; }
    public int? WordGuideMax { get; init; }
    public int? ReadingTimeSeconds { get; init; }
    public int? WritingTimeSeconds { get; init; }
    public string? SimulationModes { get; init; }
    public string? MarkingMode { get; init; }
    public string? SourceProvenance { get; init; }
    public bool? IntegrityAcknowledged { get; init; }
    public string? StimulusPdfMediaAssetId { get; init; }
    public string? AnswerSheetPdfMediaAssetId { get; init; }
    public string? RecipientRawText { get; init; }
    public string? RecipientNormalizedJson { get; init; }
    public string? ConfirmedPurposeText { get; init; }
}

public sealed record WritingTaskValidationIssue
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = "error";
    public string Message { get; init; } = string.Empty;
}

public sealed record WritingTaskValidationResult
{
    public bool IsPublishReady { get; init; }
    public List<WritingTaskValidationIssue> Issues { get; init; } = new();
}

/// <summary>
/// One row of the preparation-status audit: everything the publish gate and
/// the runtime grading path require, per task. Powers the admin backfill
/// workflow and the machine-readable preparation report.
/// </summary>
public sealed record WritingTaskPreparationStatusDto
{
    public Guid ScenarioId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string LetterType { get; init; } = string.Empty;
    public string Status { get; init; } = "draft";
    public bool HasTaskPrompt { get; init; }
    public int CaseNoteSentenceCount { get; init; }
    public bool RulebookResolvable { get; init; }
    public bool PackResolvable { get; init; }
    public bool RecipientResolved { get; init; }
    public string? ModelAnswerStatus { get; init; }
    public bool ModelAnswerApproved { get; init; }
    public bool? ModelAnswerStale { get; init; }
    public bool PublishReady { get; init; }
    public List<string> BlockingCodes { get; init; } = new();
}

// ----- import/export JSON shape (spec §18; mirrors WritingTaskImportJson) -----

// Mirrors lib/writing/types.ts WritingTaskImportJson (spec §18 envelope).
public sealed record WritingTaskImportJson
{
    public string TaskTitle { get; init; } = string.Empty;
    public string? InternalCode { get; init; }
    public string Profession { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public WritingImportDuration? Duration { get; init; }
    public WritingImportCaseNotes? CaseNotes { get; init; }
    public WritingImportWritingTask? WritingTask { get; init; }
    public WritingImportMarking? Marking { get; init; }
}

public sealed record WritingImportDuration
{
    public int? ReadingTimeSeconds { get; init; }
    public int? WritingTimeSeconds { get; init; }
}

public sealed record WritingImportCaseNotes
{
    public string? TodayDate { get; init; }
    public string? CandidateRole { get; init; }
}

public sealed record WritingImportWritingTask
{
    public string Instruction { get; init; } = string.Empty;
    public List<string>? FixedInstructions { get; init; }
    public WritingImportWordGuide? WordGuide { get; init; }
}

public sealed record WritingImportWordGuide
{
    public int? Min { get; init; }
    public int? Max { get; init; }
}

public sealed record WritingImportMarking
{
    public string? ExpectedPurpose { get; init; }
    public string? ExpectedAction { get; init; }
}
