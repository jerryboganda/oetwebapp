using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingExemplarAnnotationDto(int Ordinal, int? CharStart, int? CharEnd, string AnnotationType, string? RuleId, string Note);

public sealed record WritingExemplarView(
    Guid Id,
    Guid? ScenarioId,
    string Profession,
    string LetterType,
    string TargetBand,
    string LetterContent,
    IReadOnlyList<WritingExemplarAnnotationDto> Annotations,
    string Status,
    DateTimeOffset? PublishedAt);

public sealed record WritingExemplarFilter(string? Profession, string? LetterType, string? TargetBand, int Take = 20);

public interface IWritingExemplarService
{
    Task<IReadOnlyList<WritingExemplarView>> ListAsync(string userId, WritingExemplarFilter filter, CancellationToken ct);
    Task<WritingExemplarView?> GetAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingExemplarView?> GetClosestToScenarioAsync(string userId, Guid scenarioId, CancellationToken ct);
    Task<WritingExemplarView> CreateAsync(string userId, WritingExemplarView exemplar, CancellationToken ct);
    Task<WritingExemplarView> UpdateAsync(string userId, Guid id, WritingExemplarView exemplar, CancellationToken ct);
    Task<WritingExemplarView> PublishAsync(string userId, Guid id, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingExemplarListResponse> ListExemplarsAsync(string userId, string? profession, string? letterType, int page, int pageSize, CancellationToken ct);
    Task<WritingExemplarResponse?> GetExemplarAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingExemplarResponse?> GetClosestExemplarForScenarioAsync(string userId, Guid scenarioId, CancellationToken ct);
    Task<WritingExemplarResponse?> GetClosestExemplarForSubmissionAsync(string userId, Guid submissionId, CancellationToken ct);
    Task<WritingExemplarListResponse> AdminListExemplarsAsync(string adminUserId, string? profession, string? letterType, string? status, int page, int pageSize, CancellationToken ct);
    Task<WritingExemplarResponse> AdminCreateExemplarAsync(string adminUserId, WritingExemplarUpsertRequest request, CancellationToken ct);
    Task<WritingExemplarResponse?> AdminGetExemplarAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingExemplarResponse?> AdminUpdateExemplarAsync(string adminUserId, Guid id, WritingExemplarUpsertRequest request, CancellationToken ct);
    Task<WritingExemplarResponse?> AdminPublishExemplarAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<bool> AdminDeleteExemplarAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingExemplarTestGradeResponse?> AdminTestGradeExemplarAsync(string adminUserId, Guid id, CancellationToken ct);
}

public sealed class WritingExemplarService(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingExemplarEmbeddingService embeddings,
    IAiGatewayService aiGateway,
    ILogger<WritingExemplarService> logger) : IWritingExemplarService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingExemplarView>> ListAsync(string userId, WritingExemplarFilter filter, CancellationToken ct)
    {
        _ = userId;
        var query = db.WritingExemplars.AsNoTracking().Where(e => e.Status == "published");
        if (!string.IsNullOrWhiteSpace(filter.Profession)) query = query.Where(e => e.Profession == filter.Profession);
        if (!string.IsNullOrWhiteSpace(filter.LetterType)) query = query.Where(e => e.LetterType == filter.LetterType);
        if (!string.IsNullOrWhiteSpace(filter.TargetBand)) query = query.Where(e => e.TargetBand == filter.TargetBand);
        var rows = await query.OrderByDescending(e => e.PublishedAt).Take(filter.Take).ToListAsync(ct);
        var annotations = await LoadAnnotationsAsync(rows.Select(r => r.Id), ct);
        return rows.Select(r => ToView(r, annotations.GetValueOrDefault(r.Id) ?? new List<WritingExemplarAnnotation>())).ToList();
    }

    public async Task<WritingExemplarView?> GetAsync(string userId, Guid id, CancellationToken ct)
    {
        _ = userId;
        return await GetInternalAsync(id, publishedOnly: true, ct);
    }

    private async Task<WritingExemplarView?> GetInternalAsync(Guid id, bool publishedOnly, CancellationToken ct)
    {
        var query = db.WritingExemplars.AsNoTracking().Where(e => e.Id == id);
        if (publishedOnly) query = query.Where(e => e.Status == "published");
        var row = await query.FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var ann = await db.WritingExemplarAnnotations.AsNoTracking()
            .Where(a => a.ExemplarId == id)
            .OrderBy(a => a.Ordinal)
            .ToListAsync(ct);
        return ToView(row, ann);
    }

    public async Task<WritingExemplarView?> GetClosestToScenarioAsync(string userId, Guid scenarioId, CancellationToken ct)
    {
        var hits = await embeddings.FindClosestAsync(userId, scenarioId, take: 1, ct);
        if (hits.Count == 0) return null;
        return await GetAsync(userId, hits[0].ExemplarId, ct);
    }

    public async Task<WritingExemplarView> CreateAsync(string userId, WritingExemplarView exemplar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(exemplar);
        var now = clock.GetUtcNow();
        var status = NormalizeStatus(exemplar.Status);
        var entity = new WritingExemplar
        {
            Id = Guid.NewGuid(),
            ScenarioId = exemplar.ScenarioId,
            Profession = exemplar.Profession,
            LetterType = exemplar.LetterType,
            LetterContent = exemplar.LetterContent,
            AnnotationsJson = JsonSerializer.Serialize(exemplar.Annotations ?? Array.Empty<WritingExemplarAnnotationDto>(), JsonOptions),
            TargetBand = string.IsNullOrWhiteSpace(exemplar.TargetBand) ? "A" : exemplar.TargetBand,
            Status = status,
            AuthorId = userId,
            PublishedAt = status == "published" ? now : null,
            CreatedAt = now,
        };
        db.WritingExemplars.Add(entity);
        await AddAnnotationsAsync(entity.Id, exemplar.Annotations, ct);
        AddAuditEvent(userId, "WritingExemplar", entity.Id.ToString("D"), "writing.exemplar.created", exemplar.TargetBand);
        await db.SaveChangesAsync(ct);
        if (status == "published")
        {
            await TryEmbedExemplarAsync(userId, entity.Id, ct);
        }
        return (await GetInternalAsync(entity.Id, publishedOnly: false, ct))!;
    }

    public async Task<WritingExemplarView> UpdateAsync(string userId, Guid id, WritingExemplarView exemplar, CancellationToken ct)
    {
        var entity = await db.WritingExemplars.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw ApiException.NotFound("writing_exemplar_not_found", "Exemplar was not found.");
        entity.ScenarioId = exemplar.ScenarioId;
        entity.Profession = exemplar.Profession;
        entity.LetterType = exemplar.LetterType;
        entity.LetterContent = exemplar.LetterContent;
        entity.TargetBand = string.IsNullOrWhiteSpace(exemplar.TargetBand) ? entity.TargetBand : exemplar.TargetBand;
        var status = NormalizeStatus(exemplar.Status, entity.Status);
        entity.Status = status;
        entity.PublishedAt = status == "published" ? entity.PublishedAt ?? clock.GetUtcNow() : null;
        entity.AnnotationsJson = JsonSerializer.Serialize(exemplar.Annotations ?? Array.Empty<WritingExemplarAnnotationDto>(), JsonOptions);
        var existing = await db.WritingExemplarAnnotations.Where(a => a.ExemplarId == id).ToListAsync(ct);
        db.WritingExemplarAnnotations.RemoveRange(existing);
        await AddAnnotationsAsync(id, exemplar.Annotations, ct);
        AddAuditEvent(userId, "WritingExemplar", id.ToString("D"), "writing.exemplar.updated", exemplar.TargetBand);
        await db.SaveChangesAsync(ct);
        if (status == "published")
        {
            await TryEmbedExemplarAsync(userId, id, ct);
        }
        return (await GetInternalAsync(id, publishedOnly: false, ct))!;
    }

    public async Task<WritingExemplarView> PublishAsync(string userId, Guid id, CancellationToken ct)
    {
        var entity = await db.WritingExemplars.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw ApiException.NotFound("writing_exemplar_not_found", "Exemplar was not found.");
        entity.Status = "published";
        entity.PublishedAt = clock.GetUtcNow();
        AddAuditEvent(userId, "WritingExemplar", id.ToString("D"), "writing.exemplar.published", entity.TargetBand);
        await db.SaveChangesAsync(ct);
        await TryEmbedExemplarAsync(userId, id, ct);
        return (await GetInternalAsync(id, publishedOnly: false, ct))!;
    }

    private async Task TryEmbedExemplarAsync(string userId, Guid id, CancellationToken ct)
    {
        try
        {
            await embeddings.EmbedExemplarAsync(userId, id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exemplar embed failed at publish; will retry on next reindex tick.");
        }
    }

    private async Task<Dictionary<Guid, List<WritingExemplarAnnotation>>> LoadAnnotationsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        if (list.Count == 0) return new Dictionary<Guid, List<WritingExemplarAnnotation>>();
        return await db.WritingExemplarAnnotations.AsNoTracking()
            .Where(a => list.Contains(a.ExemplarId))
            .OrderBy(a => a.Ordinal)
            .GroupBy(a => a.ExemplarId)
            .ToDictionaryAsync(g => g.Key, g => g.OrderBy(a => a.Ordinal).ToList(), ct);
    }

    private async Task AddAnnotationsAsync(Guid exemplarId, IReadOnlyList<WritingExemplarAnnotationDto> annotations, CancellationToken ct)
    {
        if (annotations is null || annotations.Count == 0) return;
        var now = clock.GetUtcNow();
        foreach (var a in annotations)
        {
            db.WritingExemplarAnnotations.Add(new WritingExemplarAnnotation
            {
                Id = Guid.NewGuid(),
                ExemplarId = exemplarId,
                Ordinal = a.Ordinal,
                CharStart = a.CharStart,
                CharEnd = a.CharEnd,
                AnnotationType = string.IsNullOrWhiteSpace(a.AnnotationType) ? "note" : a.AnnotationType,
                RuleId = a.RuleId,
                Note = a.Note,
                CreatedAt = now,
            });
        }
        await Task.CompletedTask;
    }

    private static WritingExemplarView ToView(WritingExemplar row, IReadOnlyList<WritingExemplarAnnotation> annotations)
    {
        return new WritingExemplarView(
            row.Id,
            row.ScenarioId,
            row.Profession,
            row.LetterType,
            row.TargetBand,
            row.LetterContent,
            annotations.Select(a => new WritingExemplarAnnotationDto(a.Ordinal, a.CharStart, a.CharEnd, a.AnnotationType, a.RuleId, a.Note)).ToList(),
            row.Status,
            row.PublishedAt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingExemplarListResponse> ListExemplarsAsync(string userId, string? profession, string? letterType, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingExemplars.AsNoTracking().Where(e => e.Status == "published");
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(e => e.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(e => e.LetterType == letterType);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var annotations = await LoadAnnotationsAsync(rows.Select(r => r.Id), ct);
        var items = rows.Select(r => WritingV2ResponseMapper.ToResponse(ToView(r, annotations.GetValueOrDefault(r.Id) ?? new List<WritingExemplarAnnotation>()))).ToList();
        return new WritingExemplarListResponse(items, total);
    }

    public async Task<WritingExemplarResponse?> GetExemplarAsync(string userId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(userId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingExemplarResponse?> GetClosestExemplarForScenarioAsync(string userId, Guid scenarioId, CancellationToken ct)
    {
        var view = await GetClosestToScenarioAsync(userId, scenarioId, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingExemplarResponse?> GetClosestExemplarForSubmissionAsync(string userId, Guid submissionId, CancellationToken ct)
    {
        var submission = await db.WritingSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.UserId == userId, ct);
        if (submission is null) return null;
        return await GetClosestExemplarForScenarioAsync(userId, submission.ScenarioId, ct);
    }

    public async Task<WritingExemplarListResponse> AdminListExemplarsAsync(string adminUserId, string? profession, string? letterType, string? status, int page, int pageSize, CancellationToken ct)
    {
        _ = adminUserId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingExemplars.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(e => e.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(e => e.LetterType == letterType);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var annotations = await LoadAnnotationsAsync(rows.Select(r => r.Id), ct);
        var items = rows.Select(r => WritingV2ResponseMapper.ToResponse(ToView(r, annotations.GetValueOrDefault(r.Id) ?? new List<WritingExemplarAnnotation>()))).ToList();
        return new WritingExemplarListResponse(items, total);
    }

    public async Task<WritingExemplarResponse> AdminCreateExemplarAsync(string adminUserId, WritingExemplarUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = ToExemplarView(Guid.Empty, request);
        var saved = await CreateAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingExemplarResponse?> AdminGetExemplarAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        _ = adminUserId;
        var view = await GetInternalAsync(id, publishedOnly: false, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingExemplarResponse?> AdminUpdateExemplarAsync(string adminUserId, Guid id, WritingExemplarUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exists = await db.WritingExemplars.AsNoTracking().AnyAsync(e => e.Id == id, ct);
        if (!exists) return null;
        var view = ToExemplarView(id, request);
        var saved = await UpdateAsync(adminUserId, id, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingExemplarResponse?> AdminPublishExemplarAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var exists = await db.WritingExemplars.AsNoTracking().AnyAsync(e => e.Id == id, ct);
        if (!exists) return null;
        var saved = await PublishAsync(adminUserId, id, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<bool> AdminDeleteExemplarAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var entity = await db.WritingExemplars.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        db.WritingExemplars.Remove(entity);
        AddAuditEvent(adminUserId, "WritingExemplar", id.ToString("D"), "writing.exemplar.deleted", entity.TargetBand);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private void AddAuditEvent(string actorId, string resourceType, string resourceId, string action, string? details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            ActorName = string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            OccurredAt = clock.GetUtcNow(),
        });
    }

    public async Task<WritingExemplarTestGradeResponse?> AdminTestGradeExemplarAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        // Calls the real Writing rubric template (writing.score.v1) through
        // the AI gateway against the exemplar letter. Nothing is persisted —
        // this is a dry-run quality check that the editor uses to confirm an
        // exemplar grades at A-band (RawTotal >= 36) before publishing.
        // PassesQualityBar uses the same 36 threshold as the OET A boundary
        // so the badge in the admin UI tracks the actual publish criterion.
        var exemplar = await db.WritingExemplars.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (exemplar is null) return null;

        WritingScenario? scenario = null;
        if (exemplar.ScenarioId is Guid scenarioId)
        {
            scenario = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct);
        }

        AiGroundedPrompt prompt;
        try
        {
            prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = NormaliseLetterTypeForRulebook(scenario?.LetterType ?? exemplar.LetterType ?? "routine_referral"),
                Profession = ParseProfession(scenario?.Profession ?? exemplar.Profession ?? "medicine"),
                Task = AiTaskMode.Score,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exemplar test-grade prompt build failed for {ExemplarId}", id);
            throw ApiException.ServiceUnavailable("writing_exemplar_test_unavailable", "Writing grading prompt is misconfigured.", retryable: true);
        }

        AiGatewayResult result;
        try
        {
            result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = BuildExemplarRubricInput(exemplar, scenario),
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingGrade,
                PromptTemplateId = "writing.score.v1",
                UserId = adminUserId,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exemplar test-grade AI call failed for {ExemplarId}", id);
            throw ApiException.ServiceUnavailable("writing_exemplar_test_failed", "Writing grading service is temporarily unavailable. Please retry.", retryable: true);
        }

        var rubric = ParseExemplarRubric(result);
        var rawTotal = (short)(rubric.C1 + rubric.C2 + rubric.C3 + rubric.C4 + rubric.C5 + rubric.C6);
        var bandLabel = OetBandLabel(rawTotal);
        var passes = rawTotal >= 36;
        var canonVersion = await ResolveCanonVersionAsync(ct);
        var perCriterion = ParsePerCriterion(rubric.PerCriterionFeedbackJson);
        var priorities = ParseTopPriorities(rubric.TopThreePrioritiesJson);

        var grade = new WritingGradeResponseV2(
            Id: Guid.Empty,
            SubmissionId: Guid.Empty,
            C1Purpose: rubric.C1,
            C2Content: rubric.C2,
            C3Conciseness: rubric.C3,
            C4Genre: rubric.C4,
            C5Organisation: rubric.C5,
            C6Language: rubric.C6,
            RawTotal: rawTotal,
            EstimatedBand: rubric.EstimatedBand,
            BandLabel: bandLabel,
            PerCriterion: perCriterion,
            TopThreePriorities: priorities,
            ConfidenceFlag: rubric.ConfidenceFlag,
            ModelUsed: rubric.ModelUsed,
            CanonVersion: canonVersion,
            CanonViolations: Array.Empty<WritingCanonViolationResponse>(),
            ExemplarComparison: null,
            RevisionInvite: passes
                ? new WritingRevisionInviteResponse(false, "Exemplar passes the publish quality bar.")
                : new WritingRevisionInviteResponse(true, "Exemplar does not meet the A-band publish bar (RawTotal >= 36)."),
            GradedAt: clock.GetUtcNow());
        return new WritingExemplarTestGradeResponse(id, grade, PassesQualityBar: passes);
    }

    private static string BuildExemplarRubricInput(WritingExemplar exemplar, WritingScenario? scenario)
    {
        var sb = new StringBuilder();
        if (scenario is not null)
        {
            sb.AppendLine($"Scenario: {scenario.Title}");
            sb.AppendLine($"Profession: {scenario.Profession}");
            sb.AppendLine($"Letter type: {scenario.LetterType}");
            sb.AppendLine();
            sb.AppendLine("Case notes:");
            sb.AppendLine("---");
            sb.AppendLine(scenario.CaseNotesMarkdown);
            sb.AppendLine("---");
        }
        else
        {
            sb.AppendLine($"Profession: {exemplar.Profession}");
            sb.AppendLine($"Letter type: {exemplar.LetterType}");
            sb.AppendLine();
            sb.AppendLine("Case notes: (not available — exemplar has no linked scenario)");
        }
        sb.AppendLine();
        sb.AppendLine($"This is an admin exemplar dry-run test. Target band: {exemplar.TargetBand}.");
        sb.AppendLine("Candidate letter:");
        sb.AppendLine("---");
        sb.AppendLine(exemplar.LetterContent);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Score on the 6 OET Writing criteria. Return JSON { c1, c2, c3, c4, c5, c6, rawTotal, estimatedBand, bandLabel, perCriterion, topThreePriorities, confidenceFlag, modelUsed }.");
        return sb.ToString();
    }

    private static ExemplarRubricResult ParseExemplarRubric(AiGatewayResult result)
    {
        var completion = result.Completion ?? string.Empty;
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        var fallback = new ExemplarRubricResult(0, 0, 0, 0, 0, 0, 0, "{}", "[]", "low", "writing.score.v1");
        if (start < 0 || end <= start) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            int Get(string name, int max) => doc.RootElement.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? Math.Clamp(v, 0, max) : 0;
            var c1 = Get("c1", 3);
            var c2 = Get("c2", 7);
            var c3 = Get("c3", 7);
            var c4 = Get("c4", 7);
            var c5 = Get("c5", 7);
            var c6 = Get("c6", 7);
            var estimated = doc.RootElement.TryGetProperty("estimatedBand", out var ebEl) && ebEl.TryGetInt32(out var eb) ? eb : 200;
            var perCriterion = doc.RootElement.TryGetProperty("perCriterion", out var pcEl) ? pcEl.GetRawText() : "{}";
            var topThree = doc.RootElement.TryGetProperty("topThreePriorities", out var ttEl) ? ttEl.GetRawText() : "[]";
            var confidence = doc.RootElement.TryGetProperty("confidenceFlag", out var cfEl) && cfEl.ValueKind == JsonValueKind.String ? cfEl.GetString() ?? "medium" : "medium";
            var model = doc.RootElement.TryGetProperty("modelUsed", out var muEl) && muEl.ValueKind == JsonValueKind.String ? muEl.GetString() ?? "writing.score.v1" : "writing.score.v1";
            return new ExemplarRubricResult(c1, c2, c3, c4, c5, c6, estimated, perCriterion, topThree, confidence, model);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static IReadOnlyDictionary<string, WritingPerCriterionFeedbackResponse> ParsePerCriterion(string json)
    {
        var result = new Dictionary<string, WritingPerCriterionFeedbackResponse>();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions) ?? new();
            foreach (var (key, el) in raw)
            {
                int score = 0;
                string feedback = string.Empty;
                string? exemplar = null;
                var cited = new List<string>();
                if (el.ValueKind == JsonValueKind.Object)
                {
                    if (el.TryGetProperty("score", out var sEl) && sEl.TryGetInt32(out var s)) score = s;
                    if (el.TryGetProperty("feedback", out var fEl) && fEl.ValueKind == JsonValueKind.String) feedback = fEl.GetString() ?? string.Empty;
                    if (el.TryGetProperty("exemplarFix", out var eEl) && eEl.ValueKind == JsonValueKind.String) exemplar = eEl.GetString();
                    if (el.TryGetProperty("citedRuleIds", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in cEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var v = item.GetString();
                                if (!string.IsNullOrWhiteSpace(v)) cited.Add(v);
                            }
                        }
                    }
                }
                result[key] = new WritingPerCriterionFeedbackResponse(score, feedback, exemplar, cited);
            }
        }
        catch (JsonException) { /* ignore — return empty */ }
        return result;
    }

    private static IReadOnlyList<string> ParseTopPriorities(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private async Task<string> ResolveCanonVersionAsync(CancellationToken ct)
    {
        var max = await db.WritingCanonRules.AsNoTracking().MaxAsync(r => (int?)r.Version, ct);
        return $"v{max ?? 1}";
    }

    private static string OetBandLabel(int rawTotal)
    {
        if (rawTotal >= 38) return "A";
        if (rawTotal >= 34) return "B+";
        if (rawTotal >= 30) return "B";
        if (rawTotal >= 24) return "C+";
        if (rawTotal >= 18) return "C";
        if (rawTotal >= 12) return "D";
        return "E";
    }

    private static ExamProfession ParseProfession(string raw)
        => RulebookProfessionParser.TryParse(raw, out var p) ? p : ExamProfession.Medicine;

    private static string NormaliseLetterTypeForRulebook(string v)
        => v.ToUpperInvariant() switch
        {
            "LT-RR" => "routine_referral",
            "LT-UR" => "urgent_referral",
            "LT-DG" => "discharge",
            "LT-TR" => "transfer",
            "LT-RP" => "advice_to_patient",
            "LT-NM" => "non_medical",
            _ => v.ToLowerInvariant(),
        };

    private sealed record ExemplarRubricResult(int C1, int C2, int C3, int C4, int C5, int C6, int EstimatedBand,
        string PerCriterionFeedbackJson, string TopThreePrioritiesJson, string ConfidenceFlag, string ModelUsed);

    private static string NormalizeStatus(string? requested, string fallback = "draft")
    {
        var candidate = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim().ToLowerInvariant();
        return candidate is "draft" or "published" or "archived" ? candidate : fallback;
    }

    private static WritingExemplarView ToExemplarView(Guid id, WritingExemplarUpsertRequest req)
        => new(
            Id: id,
            ScenarioId: req.ScenarioId,
            Profession: req.Profession,
            LetterType: req.LetterType,
            TargetBand: req.TargetBand,
            LetterContent: req.LetterContent,
            Annotations: (req.Annotations ?? Array.Empty<WritingExemplarAnnotationResponse>())
                .Select((a, i) => new WritingExemplarAnnotationDto(i + 1, a.CharStart, a.CharEnd, "note", a.RuleId, a.Note))
                .ToList(),
            Status: string.IsNullOrWhiteSpace(req.Status) ? "draft" : req.Status!,
            PublishedAt: null);
}
