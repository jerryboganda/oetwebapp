using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// WS-B2: Authoring service for the unified writing task (the enriched
/// <see cref="WritingScenario"/> plus its content checklist items and a linked
/// model-answer <see cref="WritingExemplar"/>). Provides CRUD, lifecycle,
/// publish-readiness validation, and JSON import/export (spec §18).
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

        var items = new List<WritingTaskDto>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            items.Add(await BuildDtoAsync(scenario, ct));
        }

        return (items, total);
    }

    public async Task<WritingTaskDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return scenario is null ? null : await BuildDtoAsync(scenario, ct);
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

        ReplaceChecklist(scenario.Id, MergeChecklists(request), now);
        await UpsertModelAnswerAsync(scenario, request.ModelAnswerText, request.ModelAnswerParagraphs, user, ct);

        await db.SaveChangesAsync(ct);
        return await BuildDtoAsync(scenario, ct);
    }

    public async Task<WritingTaskDto?> UpdateAsync(Guid id, WritingTaskUpsertDto request, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        ApplyUpsert(scenario, request);
        scenario.UpdatedAt = now;

        ReplaceChecklist(scenario.Id, MergeChecklists(request), now);
        await UpsertModelAnswerAsync(scenario, request.ModelAnswerText, request.ModelAnswerParagraphs, user, ct);

        await db.SaveChangesAsync(ct);
        return await BuildDtoAsync(scenario, ct);
    }

    /// <summary>Combines the contract's split key/irrelevant checklist arrays into the
    /// single persisted checklist; RequiredStatus discriminates them on the way out.</summary>
    private static List<WritingContentChecklistItemDto> MergeChecklists(WritingTaskUpsertDto request)
    {
        var merged = new List<WritingContentChecklistItemDto>();
        if (request.KeyContentChecklist is { } keys)
        {
            merged.AddRange(keys.Select(k => k with
            {
                RequiredStatus = string.Equals(k.RequiredStatus, "optional", StringComparison.OrdinalIgnoreCase)
                    ? "optional"
                    : "required",
            }));
        }

        if (request.IrrelevantContentChecklist is { } irrelevant)
        {
            merged.AddRange(irrelevant.Select(r => r with { RequiredStatus = "irrelevant" }));
        }

        return merged;
    }

    public async Task<WritingTaskValidationResult?> ValidateAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return null;
        }

        var checklist = await db.WritingContentChecklistItems
            .AsNoTracking()
            .Where(c => c.ScenarioId == id)
            .ToListAsync(ct);

        var exemplar = await LoadExemplarAsync(scenario, ct);
        return Validate(scenario, checklist, exemplar);
    }

    public async Task<(WritingTaskDto? Task, WritingTaskValidationResult? Validation)> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return (null, null);
        }

        var checklist = await db.WritingContentChecklistItems
            .Where(c => c.ScenarioId == id)
            .ToListAsync(ct);
        var exemplar = await LoadExemplarAsync(scenario, ct);

        var validation = Validate(scenario, checklist, exemplar);
        if (!validation.IsPublishReady)
        {
            return (null, validation);
        }

        scenario.Status = "published";
        scenario.UpdatedAt = DateTimeOffset.UtcNow;
        if (exemplar is not null)
        {
            exemplar.Status = "published";
        }

        await db.SaveChangesAsync(ct);
        return (await BuildDtoAsync(scenario, ct), validation);
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
        return await BuildDtoAsync(scenario, ct);
    }

    public async Task<WritingTaskDto?> CloneAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var source = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null)
        {
            return null;
        }

        var sourceChecklist = await db.WritingContentChecklistItems
            .AsNoTracking()
            .Where(c => c.ScenarioId == id)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct);
        var sourceExemplar = await LoadExemplarAsync(source, ct);

        var now = DateTimeOffset.UtcNow;
        var clone = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(source.Title) ? source.Title : $"{source.Title} (copy)",
            Profession = source.Profession,
            LetterType = source.LetterType,
            CaseNotesMarkdown = source.CaseNotesMarkdown,
            Status = "draft",
            InternalCode = source.InternalCode,
            TaskPromptMarkdown = source.TaskPromptMarkdown,
            WriterRole = source.WriterRole,
            TodayDate = source.TodayDate,
            RecipientJson = source.RecipientJson,
            ExpectedPurpose = source.ExpectedPurpose,
            ExpectedAction = source.ExpectedAction,
            CaseNoteSectionsJson = source.CaseNoteSectionsJson,
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
            AuthorId = GetUserId(user) ?? source.AuthorId ?? "system",
            ContentOwnerId = GetUserId(user) ?? source.ContentOwnerId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.WritingScenarios.Add(clone);

        for (var i = 0; i < sourceChecklist.Count; i++)
        {
            var c = sourceChecklist[i];
            db.WritingContentChecklistItems.Add(new WritingContentChecklistItem
            {
                Id = Guid.NewGuid(),
                ScenarioId = clone.Id,
                ItemText = c.ItemText,
                Category = c.Category,
                Importance = c.Importance,
                RequiredStatus = c.RequiredStatus,
                LinkedCaseNoteSection = c.LinkedCaseNoteSection,
                ExpectedRepresentation = c.ExpectedRepresentation,
                CommonError = c.CommonError,
                Ordinal = i,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        // Deep-copy the model answer (scenario-level Version/PreviousVersionId set above).
        var paragraphs = ReadParagraphs(sourceExemplar);
        await UpsertModelAnswerAsync(clone, sourceExemplar?.LetterContent, paragraphs, user, ct);

        await db.SaveChangesAsync(ct);
        return await BuildDtoAsync(clone, ct);
    }

    public async Task<WritingTaskDto> ImportAsync(WritingTaskImportJson import, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var upsert = MapImportToUpsert(import);
        return await CreateAsync(upsert, user, ct);
    }

    public async Task<WritingTaskImportJson?> ExportAsync(Guid id, CancellationToken ct = default)
    {
        var scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario is null)
        {
            return null;
        }

        var checklist = await db.WritingContentChecklistItems
            .AsNoTracking()
            .Where(c => c.ScenarioId == id)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct);
        var exemplar = await LoadExemplarAsync(scenario, ct);

        return MapToExportJson(scenario, checklist, exemplar);
    }

    // ----- mapping helpers -----

    private void ApplyUpsert(WritingScenario scenario, WritingTaskUpsertDto request)
    {
        scenario.Title = request.Title?.Trim() ?? string.Empty;
        scenario.Profession = request.Profession?.Trim() ?? string.Empty;
        scenario.LetterType = request.LetterType?.Trim() ?? string.Empty;
        if (request.Difficulty is { } difficulty) scenario.Difficulty = Math.Clamp(difficulty, 1, 5);
        scenario.InternalCode = string.IsNullOrWhiteSpace(request.InternalCode) ? null : request.InternalCode.Trim();
        scenario.WriterRole = request.WriterRole;
        scenario.TodayDate = request.TodayDate;
        scenario.TaskPromptMarkdown = request.TaskPromptMarkdown;
        scenario.CaseNotesMarkdown = request.CaseNotesMarkdown ?? scenario.CaseNotesMarkdown ?? string.Empty;
        scenario.ExpectedPurpose = request.ExpectedPurpose;
        scenario.ExpectedAction = request.ExpectedAction;
        scenario.CaseNoteSectionsJson = SerializeCaseNoteSections(request.CaseNoteSections);
        scenario.RecipientJson = SerializeRecipient(request.Recipient);
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

        scenario.SourceProvenance = request.SourceProvenance;

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

    private void ReplaceChecklist(Guid scenarioId, List<WritingContentChecklistItemDto>? items, DateTimeOffset now)
    {
        var existing = db.WritingContentChecklistItems.Where(c => c.ScenarioId == scenarioId);
        db.WritingContentChecklistItems.RemoveRange(existing);

        if (items is null || items.Count == 0)
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var dto = items[i];
            if (string.IsNullOrWhiteSpace(dto.ItemText))
            {
                continue;
            }

            db.WritingContentChecklistItems.Add(new WritingContentChecklistItem
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenarioId,
                ItemText = dto.ItemText.Trim(),
                Category = dto.Category?.Trim() ?? string.Empty,
                Importance = NormalizeImportance(dto.Importance),
                RequiredStatus = NormalizeRequiredStatus(dto.RequiredStatus),
                LinkedCaseNoteSection = dto.LinkedCaseNoteSection,
                ExpectedRepresentation = dto.ExpectedRepresentation,
                CommonError = dto.CommonError,
                Ordinal = dto.Ordinal != 0 ? dto.Ordinal : i,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private async Task UpsertModelAnswerAsync(
        WritingScenario scenario,
        string? modelAnswerText,
        List<WritingModelAnswerParagraphDto>? paragraphs,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var annotations = SerializeAnnotations(paragraphs);

        WritingExemplar? exemplar = null;
        if (scenario.ModelAnswerExemplarId is { } exemplarId)
        {
            exemplar = await db.WritingExemplars.FirstOrDefaultAsync(e => e.Id == exemplarId, ct);
        }

        if (exemplar is null)
        {
            exemplar = new WritingExemplar
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                Status = "draft",
                AuthorId = GetUserId(user) ?? "system",
                TargetBand = "A",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.WritingExemplars.Add(exemplar);
            scenario.ModelAnswerExemplarId = exemplar.Id;
        }

        exemplar.LetterContent = modelAnswerText ?? string.Empty;
        exemplar.AnnotationsJson = annotations;
        exemplar.Profession = scenario.Profession;
        exemplar.LetterType = scenario.LetterType;
        exemplar.ScenarioId = scenario.Id;
    }

    private async Task<WritingExemplar?> LoadExemplarAsync(WritingScenario scenario, CancellationToken ct)
    {
        if (scenario.ModelAnswerExemplarId is not { } exemplarId)
        {
            return null;
        }

        return await db.WritingExemplars.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exemplarId, ct);
    }

    private async Task<WritingTaskDto> BuildDtoAsync(WritingScenario scenario, CancellationToken ct)
    {
        var checklist = await db.WritingContentChecklistItems
            .AsNoTracking()
            .Where(c => c.ScenarioId == scenario.Id)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct);
        var exemplar = await LoadExemplarAsync(scenario, ct);
        return MapToDto(scenario, checklist, exemplar);
    }

    private static WritingTaskDto MapToDto(
        WritingScenario scenario,
        List<WritingContentChecklistItem> checklist,
        WritingExemplar? exemplar)
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
            CaseNotesMarkdown = scenario.CaseNotesMarkdown,
            CaseNoteSections = DeserializeCaseNoteSections(scenario.CaseNoteSectionsJson),
            Recipient = DeserializeRecipient(scenario.RecipientJson),
            ExpectedPurpose = scenario.ExpectedPurpose,
            ExpectedAction = scenario.ExpectedAction,
            FixedInstructions = DeserializeFixedInstructions(scenario.FixedInstructionsJson),
            WordGuideMin = scenario.WordGuideMin,
            WordGuideMax = scenario.WordGuideMax,
            ReadingTimeSeconds = scenario.ReadingTimeSeconds,
            WritingTimeSeconds = scenario.WritingTimeSeconds,
            SimulationModes = scenario.SimulationModes,
            MarkingMode = scenario.MarkingMode,
            ModelAnswerText = string.IsNullOrEmpty(exemplar?.LetterContent) ? null : exemplar!.LetterContent,
            ModelAnswerParagraphs = ReadParagraphs(exemplar),
            KeyContentChecklist = checklist
                .Where(c => !string.Equals(c.RequiredStatus, "irrelevant", StringComparison.OrdinalIgnoreCase))
                .Select(MapChecklistDto)
                .ToList(),
            IrrelevantContentChecklist = checklist
                .Where(c => string.Equals(c.RequiredStatus, "irrelevant", StringComparison.OrdinalIgnoreCase))
                .Select(MapChecklistDto)
                .ToList(),
            SourceProvenance = scenario.SourceProvenance,
            IntegrityAcknowledged = scenario.IntegrityAcknowledgedAt is not null,
            StimulusPdfMediaAssetId = pdfId,
            StimulusPdfDownloadPath = string.IsNullOrWhiteSpace(pdfId) ? null : $"/v1/media/{pdfId}/content",
            CreatedAt = scenario.CreatedAt,
            UpdatedAt = scenario.UpdatedAt,
        };
    }

    private static WritingContentChecklistItemDto MapChecklistDto(WritingContentChecklistItem c) => new()
    {
        Id = c.Id,
        ItemText = c.ItemText,
        Category = c.Category,
        Importance = c.Importance,
        RequiredStatus = c.RequiredStatus,
        LinkedCaseNoteSection = c.LinkedCaseNoteSection,
        ExpectedRepresentation = c.ExpectedRepresentation,
        CommonError = c.CommonError,
        Ordinal = c.Ordinal,
    };

    // ----- validation (spec §3/§19.2/§22) -----

    private static WritingTaskValidationResult Validate(
        WritingScenario scenario,
        List<WritingContentChecklistItem> checklist,
        WritingExemplar? exemplar)
    {
        var issues = new List<WritingTaskValidationIssue>();

        if (string.IsNullOrWhiteSpace(scenario.Title))
        {
            issues.Add(Error("title_required", "Title is required."));
        }

        if (string.IsNullOrWhiteSpace(scenario.Profession))
        {
            issues.Add(Error("profession_required", "Profession is required."));
        }

        if (string.IsNullOrWhiteSpace(scenario.LetterType))
        {
            issues.Add(Error("letter_type_required", "Letter type is required."));
        }
        else if (!string.IsNullOrWhiteSpace(scenario.Profession)
            && !WritingContentStructure.IsLetterTypeAllowedForProfession(scenario.Profession, scenario.LetterType))
        {
            issues.Add(Error("letter_type_not_allowed", $"Letter type '{scenario.LetterType}' is not allowed for profession '{scenario.Profession}'."));
        }

        if (string.IsNullOrWhiteSpace(scenario.TaskPromptMarkdown))
        {
            issues.Add(Error("task_prompt_required", "Task prompt (writing instruction) is required."));
        }

        var hasCaseNoteSections = !string.IsNullOrWhiteSpace(scenario.CaseNoteSectionsJson)
            && DeserializeCaseNoteSections(scenario.CaseNoteSectionsJson).Count > 0;
        if (!hasCaseNoteSections && string.IsNullOrWhiteSpace(scenario.CaseNotesMarkdown))
        {
            issues.Add(Error("case_notes_required", "Case notes are required (at least one section or case-notes markdown)."));
        }

        var recipient = DeserializeRecipient(scenario.RecipientJson);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Name) || string.IsNullOrWhiteSpace(recipient.Role))
        {
            issues.Add(Error("recipient_required", "Recipient name and role are required."));
        }

        if (string.IsNullOrWhiteSpace(exemplar?.LetterContent))
        {
            issues.Add(Error("model_answer_required", "A model answer is required."));
        }

        if (scenario.WordGuideMin <= 0 || scenario.WordGuideMax < scenario.WordGuideMin)
        {
            issues.Add(Error("word_guide_invalid", "Word guide must have min > 0 and max >= min."));
        }

        if (!checklist.Any(c => string.Equals(c.RequiredStatus, "required", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(Error("key_content_required", "At least one required key-content checklist item is needed."));
        }

        if (string.IsNullOrWhiteSpace(scenario.SourceProvenance))
        {
            issues.Add(Error("source_provenance_required", "Source provenance must be recorded."));
        }

        if (scenario.IntegrityAcknowledgedAt is null)
        {
            issues.Add(Error("integrity_not_acknowledged", "Content integrity must be acknowledged before publishing."));
        }

        return new WritingTaskValidationResult
        {
            IsPublishReady = !issues.Any(i => i.Severity == "error"),
            Issues = issues,
        };
    }

    private static WritingTaskValidationIssue Error(string code, string message) => new()
    {
        Code = code,
        Severity = "error",
        Message = message,
    };

    // ----- import / export (spec §18) -----

    private static WritingTaskUpsertDto MapImportToUpsert(WritingTaskImportJson import)
    {
        var ordinal = 0;
        var keyItems = new List<WritingContentChecklistItemDto>();
        if (import.Marking?.KeyContentChecklist is { } keys)
        {
            foreach (var k in keys)
            {
                if (string.IsNullOrWhiteSpace(k.ItemText)) continue;
                keyItems.Add(new WritingContentChecklistItemDto
                {
                    ItemText = k.ItemText!.Trim(),
                    Category = k.Category?.Trim() ?? string.Empty,
                    Importance = NormalizeImportance(k.Importance),
                    RequiredStatus = string.Equals(k.RequiredStatus, "optional", StringComparison.OrdinalIgnoreCase) ? "optional" : "required",
                    LinkedCaseNoteSection = k.LinkedCaseNoteSection,
                    ExpectedRepresentation = k.ExpectedRepresentation,
                    CommonError = k.CommonError,
                    Ordinal = ordinal++,
                });
            }
        }

        var irrelevantItems = new List<WritingContentChecklistItemDto>();
        if (import.Marking?.IrrelevantContentChecklist is { } irrelevant)
        {
            foreach (var r in irrelevant)
            {
                if (string.IsNullOrWhiteSpace(r.ItemText)) continue;
                irrelevantItems.Add(new WritingContentChecklistItemDto
                {
                    ItemText = r.ItemText!.Trim(),
                    Category = r.Category?.Trim() ?? string.Empty,
                    Importance = "low",
                    RequiredStatus = "irrelevant",
                    Ordinal = ordinal++,
                });
            }
        }

        var caseNoteSections = import.CaseNotes?.Sections?
            .Select(s => new WritingCaseNoteSectionDto
            {
                Heading = s.Heading?.Trim() ?? string.Empty,
                Items = s.Items?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList() ?? new List<string>(),
            })
            .ToList();

        var recipient = import.WritingTask?.Recipient;

        return new WritingTaskUpsertDto
        {
            InternalCode = import.InternalCode,
            Title = BuildImportTitle(import),
            Profession = (import.Profession ?? string.Empty).Trim(),
            LetterType = MapTaskTypeToLetterType(import.TaskType),
            WriterRole = import.CaseNotes?.CandidateRole,
            TodayDate = import.CaseNotes?.TodayDate,
            TaskPromptMarkdown = import.WritingTask?.Instruction,
            CaseNoteSections = caseNoteSections,
            Recipient = recipient is null ? null : new WritingRecipientDto
            {
                Name = recipient.Name ?? string.Empty,
                Role = recipient.Role ?? string.Empty,
                Organisation = recipient.Organisation,
                Address = recipient.Address,
            },
            ExpectedPurpose = import.Marking?.ExpectedPurpose,
            ExpectedAction = import.Marking?.ExpectedAction,
            FixedInstructions = import.WritingTask?.FixedInstructions?.Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
            WordGuideMin = import.WritingTask?.WordGuide?.Min,
            WordGuideMax = import.WritingTask?.WordGuide?.Max,
            ReadingTimeSeconds = import.Duration?.ReadingTimeSeconds,
            WritingTimeSeconds = import.Duration?.WritingTimeSeconds,
            ModelAnswerText = string.IsNullOrWhiteSpace(import.Marking?.ModelAnswer) ? null : import.Marking!.ModelAnswer!.Trim(),
            KeyContentChecklist = keyItems,
            IrrelevantContentChecklist = irrelevantItems,
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

    private WritingTaskImportJson MapToExportJson(
        WritingScenario scenario,
        List<WritingContentChecklistItem> checklist,
        WritingExemplar? exemplar)
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
                Sections = DeserializeCaseNoteSections(scenario.CaseNoteSectionsJson)
                    .Select(s => new WritingImportCaseNoteSection
                    {
                        Heading = s.Heading,
                        Items = s.Items,
                    })
                    .ToList(),
            },
            WritingTask = new WritingImportWritingTask
            {
                Instruction = scenario.TaskPromptMarkdown ?? string.Empty,
                Recipient = DeserializeRecipient(scenario.RecipientJson) is { } r
                    ? new WritingImportRecipient
                    {
                        Name = r.Name,
                        Role = r.Role,
                        Organisation = r.Organisation,
                        Address = r.Address,
                    }
                    : null,
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
                KeyContentChecklist = checklist
                    .Where(c => !string.Equals(c.RequiredStatus, "irrelevant", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new WritingImportChecklistItem
                    {
                        ItemText = c.ItemText,
                        Category = c.Category,
                        Importance = c.Importance,
                        RequiredStatus = c.RequiredStatus,
                        LinkedCaseNoteSection = c.LinkedCaseNoteSection,
                        ExpectedRepresentation = c.ExpectedRepresentation,
                        CommonError = c.CommonError,
                    })
                    .ToList(),
                IrrelevantContentChecklist = checklist
                    .Where(c => string.Equals(c.RequiredStatus, "irrelevant", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new WritingImportChecklistItem
                    {
                        ItemText = c.ItemText,
                        Category = c.Category,
                        Importance = c.Importance,
                        RequiredStatus = c.RequiredStatus,
                    })
                    .ToList(),
                ModelAnswer = string.IsNullOrEmpty(exemplar?.LetterContent) ? null : exemplar!.LetterContent,
            },
        };
    }

    /// <summary>
    /// Maps an import task-type label (human or canonical) to a canonical letter-type id.
    /// </summary>
    private static string MapTaskTypeToLetterType(string? taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
        {
            return string.Empty;
        }

        var trimmed = taskType.Trim();
        var key = trimmed.ToLowerInvariant();

        // Already a canonical id?
        if (WritingContentStructure.IsCanonicalLetterType(trimmed))
        {
            return key;
        }

        return key switch
        {
            "referral letter" => "routine_referral",
            "routine referral" => "routine_referral",
            "routine referral letter" => "routine_referral",
            "urgent referral letter" => "urgent_referral",
            "urgent referral" => "urgent_referral",
            "discharge letter" => "update_discharge",
            "discharge summary" => "update_discharge",
            "update letter" => "update_discharge",
            "transfer letter" => "transfer_letter",
            "advice letter" => "advice_letter",
            "letter of advice" => "advice_letter",
            _ => key.Replace(' ', '_'),
        };
    }

    // ----- json (de)serialization for the scenario's stored columns -----

    private static string? SerializeCaseNoteSections(List<WritingCaseNoteSectionDto>? sections)
    {
        if (sections is null || sections.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(sections, JsonOptions);
    }

    private static List<WritingCaseNoteSectionDto> DeserializeCaseNoteSections(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<WritingCaseNoteSectionDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<WritingCaseNoteSectionDto>>(json, JsonOptions)
                ?? new List<WritingCaseNoteSectionDto>();
        }
        catch (JsonException)
        {
            return new List<WritingCaseNoteSectionDto>();
        }
    }

    private static string? SerializeRecipient(WritingRecipientDto? recipient)
    {
        if (recipient is null
            || (string.IsNullOrWhiteSpace(recipient.Name)
                && string.IsNullOrWhiteSpace(recipient.Role)
                && string.IsNullOrWhiteSpace(recipient.Organisation)
                && string.IsNullOrWhiteSpace(recipient.Address)))
        {
            return null;
        }

        return JsonSerializer.Serialize(recipient, JsonOptions);
    }

    private static WritingRecipientDto? DeserializeRecipient(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WritingRecipientDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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

    // ----- model-answer annotations (structured paragraphs) -----
    // Paragraphs are stored under a "paragraphs" key in the exemplar's AnnotationsJson;
    // task-level versioning lives on the WritingScenario entity (Version/PreviousVersionId).

    private static string SerializeAnnotations(List<WritingModelAnswerParagraphDto>? paragraphs)
    {
        var payload = new ExemplarAnnotations
        {
            Paragraphs = paragraphs?
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => new WritingModelAnswerParagraphDto { Heading = p.Heading, Text = p.Text.Trim() })
                .ToList() ?? new List<WritingModelAnswerParagraphDto>(),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static List<WritingModelAnswerParagraphDto> ReadParagraphs(WritingExemplar? exemplar)
    {
        if (exemplar is null || string.IsNullOrWhiteSpace(exemplar.AnnotationsJson))
        {
            return new List<WritingModelAnswerParagraphDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<ExemplarAnnotations>(exemplar.AnnotationsJson, JsonOptions)?.Paragraphs
                ?? new List<WritingModelAnswerParagraphDto>();
        }
        catch (JsonException)
        {
            return new List<WritingModelAnswerParagraphDto>();
        }
    }

    private static string NormalizeImportance(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "high" => "high",
        "low" => "low",
        _ => "medium",
    };

    private static string NormalizeRequiredStatus(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "optional" => "optional",
        "irrelevant" => "irrelevant",
        _ => "required",
    };

    private static string? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }

    private sealed class ExemplarAnnotations
    {
        [JsonPropertyName("paragraphs")]
        public List<WritingModelAnswerParagraphDto> Paragraphs { get; set; } = new();
    }
}

// ===== DTOs (mirror lib/writing/types.ts; camelCase via ASP.NET web defaults) =====

public sealed record WritingRecipientDto
{
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? Organisation { get; init; }
    public string? Address { get; init; }
}

public sealed record WritingCaseNoteSectionDto
{
    public string Heading { get; init; } = string.Empty;
    public List<string> Items { get; init; } = new();
}

public sealed record WritingContentChecklistItemDto
{
    public Guid? Id { get; init; }
    public string ItemText { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Importance { get; init; } = "medium";
    public string RequiredStatus { get; init; } = "required";
    public string? LinkedCaseNoteSection { get; init; }
    public string? ExpectedRepresentation { get; init; }
    public string? CommonError { get; init; }
    public int Ordinal { get; init; }
}

public sealed record WritingModelAnswerParagraphDto
{
    public string? Heading { get; init; }
    public string Text { get; init; } = string.Empty;
}

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
    public string? CaseNotesMarkdown { get; init; }
    public List<WritingCaseNoteSectionDto> CaseNoteSections { get; init; } = new();
    public WritingRecipientDto? Recipient { get; init; }
    public string? ExpectedPurpose { get; init; }
    public string? ExpectedAction { get; init; }
    public List<string> FixedInstructions { get; init; } = new();
    public int WordGuideMin { get; init; }
    public int WordGuideMax { get; init; }
    public int ReadingTimeSeconds { get; init; }
    public int WritingTimeSeconds { get; init; }
    public string SimulationModes { get; init; } = "both";
    public string MarkingMode { get; init; } = "tutor";
    public string? ModelAnswerText { get; init; }
    public List<WritingModelAnswerParagraphDto> ModelAnswerParagraphs { get; init; } = new();
    public List<WritingContentChecklistItemDto> KeyContentChecklist { get; init; } = new();
    public List<WritingContentChecklistItemDto> IrrelevantContentChecklist { get; init; } = new();
    public string? SourceProvenance { get; init; }
    public bool IntegrityAcknowledged { get; init; }
    public string? StimulusPdfMediaAssetId { get; init; }
    public string? StimulusPdfDownloadPath { get; init; }
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
    public string? CaseNotesMarkdown { get; init; }
    public List<WritingCaseNoteSectionDto>? CaseNoteSections { get; init; }
    public WritingRecipientDto? Recipient { get; init; }
    public string? ExpectedPurpose { get; init; }
    public string? ExpectedAction { get; init; }
    public List<string>? FixedInstructions { get; init; }
    public int? WordGuideMin { get; init; }
    public int? WordGuideMax { get; init; }
    public int? ReadingTimeSeconds { get; init; }
    public int? WritingTimeSeconds { get; init; }
    public string? SimulationModes { get; init; }
    public string? MarkingMode { get; init; }
    public string? ModelAnswerText { get; init; }
    public List<WritingModelAnswerParagraphDto>? ModelAnswerParagraphs { get; init; }
    public List<WritingContentChecklistItemDto>? KeyContentChecklist { get; init; }
    public List<WritingContentChecklistItemDto>? IrrelevantContentChecklist { get; init; }
    public string? SourceProvenance { get; init; }
    public bool? IntegrityAcknowledged { get; init; }
    public string? StimulusPdfMediaAssetId { get; init; }
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

public sealed record WritingImportRecipient
{
    public string? Name { get; init; }
    public string? Role { get; init; }
    public string? Organisation { get; init; }
    public string? Address { get; init; }
}

public sealed record WritingImportCaseNotes
{
    public string? TodayDate { get; init; }
    public string? CandidateRole { get; init; }
    public List<WritingImportCaseNoteSection>? Sections { get; init; }
}

public sealed record WritingImportCaseNoteSection
{
    public string? Heading { get; init; }
    public List<string>? Items { get; init; }
}

public sealed record WritingImportWritingTask
{
    public string Instruction { get; init; } = string.Empty;
    public WritingImportRecipient? Recipient { get; init; }
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
    public List<WritingImportChecklistItem>? KeyContentChecklist { get; init; }
    public List<WritingImportChecklistItem>? IrrelevantContentChecklist { get; init; }
    public string? ModelAnswer { get; init; }
}

public sealed record WritingImportChecklistItem
{
    public string? ItemText { get; init; }
    public string? Category { get; init; }
    public string? Importance { get; init; }
    public string? RequiredStatus { get; init; }
    public string? LinkedCaseNoteSection { get; init; }
    public string? ExpectedRepresentation { get; init; }
    public string? CommonError { get; init; }
}
