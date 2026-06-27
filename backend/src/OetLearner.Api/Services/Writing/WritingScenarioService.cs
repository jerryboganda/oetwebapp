using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingScenarioFilter(
    string? Profession,
    string? LetterType,
    string? SubDiscipline,
    int? Difficulty,
    bool? DiagnosticOnly,
    int Take = 20);

public sealed record WritingScenarioStructuredSentenceDto(int Ordinal, string SentenceText, string Relevance, string? Notes);

public sealed record WritingScenarioView(
    Guid Id,
    string Title,
    string LetterType,
    string Profession,
    string? SubDiscipline,
    IReadOnlyList<string> Topics,
    int Difficulty,
    IReadOnlyList<WritingScenarioStructuredSentenceDto> CaseNotesStructured,
    bool IsDiagnostic,
    string Status,
    DateTimeOffset CreatedAt,
    string? StimulusPdfMediaAssetId = null);

public interface IWritingScenarioService
{
    Task<IReadOnlyList<WritingScenarioView>> ListAsync(string userId, WritingScenarioFilter filter, CancellationToken ct);
    Task<WritingScenarioView?> GetAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingScenarioView?> PickRandomAsync(string userId, WritingScenarioFilter filter, CancellationToken ct);
    Task<WritingScenarioView> CreateAsync(string userId, WritingScenarioView scenario, CancellationToken ct);
    Task<WritingScenarioView> UpdateAsync(string userId, Guid id, WritingScenarioView scenario, CancellationToken ct);
    Task<WritingScenarioView> ApproveAsync(string userId, Guid id, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingScenarioListResponse> ListScenariosAsync(string userId, string? profession, string? letterType, int? difficulty, bool? isDiagnostic, string? search, int page, int pageSize, CancellationToken ct);
    Task<WritingScenarioResponse?> GetScenarioAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingScenarioResponse?> GetRandomScenarioAsync(string userId, string? profession, string? letterType, CancellationToken ct);
    Task<WritingScenarioListResponse> AdminListScenariosAsync(string adminUserId, string? profession, string? letterType, string? status, string? search, int page, int pageSize, CancellationToken ct);
    Task<WritingScenarioResponse> AdminCreateScenarioAsync(string adminUserId, WritingScenarioUpsertRequest request, CancellationToken ct);
    Task<WritingScenarioResponse?> AdminGetScenarioAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingScenarioResponse?> AdminUpdateScenarioAsync(string adminUserId, Guid id, WritingScenarioUpsertRequest request, CancellationToken ct);
    Task<bool> AdminDeleteScenarioAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingScenarioResponse?> AdminApproveScenarioAsync(string adminUserId, Guid id, CancellationToken ct);
}

public sealed class WritingScenarioService(LearnerDbContext db, TimeProvider clock) : IWritingScenarioService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingScenarioView>> ListAsync(string userId, WritingScenarioFilter filter, CancellationToken ct)
    {
        _ = userId;
        var query = db.WritingScenarios.AsNoTracking().Where(s => s.Status == "published");
        if (!string.IsNullOrWhiteSpace(filter.Profession)) query = query.Where(s => s.Profession.ToLower() == filter.Profession.ToLower());
        if (!string.IsNullOrWhiteSpace(filter.LetterType)) query = query.Where(s => s.LetterType == filter.LetterType);
        if (!string.IsNullOrWhiteSpace(filter.SubDiscipline)) query = query.Where(s => s.SubDiscipline == filter.SubDiscipline);
        if (filter.Difficulty is { } diff) query = query.Where(s => s.Difficulty == diff);
        if (filter.DiagnosticOnly == true) query = query.Where(s => s.IsDiagnostic);
        var rows = await query.OrderBy(s => s.Difficulty).ThenBy(s => s.Title).Take(filter.Take).ToListAsync(ct);
        var sentences = await LoadSentencesAsync(rows.Select(r => r.Id), ct);
        return rows.Select(r => ToView(r, (IReadOnlyList<WritingScenarioStructuredSentence>)(sentences.TryGetValue(r.Id, out var __ss) ? __ss : Array.Empty<WritingScenarioStructuredSentence>()))).ToList();
    }

    public async Task<WritingScenarioView?> GetAsync(string userId, Guid id, CancellationToken ct)
    {
        _ = userId;
        var row = await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return null;
        var sentences = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => s.ScenarioId == id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        return ToView(row, sentences);
    }

    public async Task<WritingScenarioView?> PickRandomAsync(string userId, WritingScenarioFilter filter, CancellationToken ct)
    {
        var list = await ListAsync(userId, filter, ct);
        if (list.Count == 0) return null;
        var rng = new Random(StableSeed(userId, clock.GetUtcNow().Date));
        return list[rng.Next(list.Count)];
    }

    public async Task<WritingScenarioView> CreateAsync(string userId, WritingScenarioView scenario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var now = clock.GetUtcNow();
        var entity = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = scenario.Title,
            LetterType = scenario.LetterType,
            Profession = scenario.Profession,
            SubDiscipline = scenario.SubDiscipline,
            TopicsJson = JsonSerializer.Serialize(scenario.Topics ?? Array.Empty<string>(), JsonOptions),
            Difficulty = Math.Clamp(scenario.Difficulty, 1, 5),
            IsDiagnostic = scenario.IsDiagnostic,
            Status = "draft",
            AuthorId = userId,
            CreatedAt = now,
        };
        db.WritingScenarios.Add(entity);
        await PersistSentencesAsync(entity.Id, scenario.CaseNotesStructured, ct);
        AddAuditEvent(userId, "WritingScenario", entity.Id.ToString("D"), "writing.scenario.created", scenario.Title);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, entity.Id, ct) ?? throw new InvalidOperationException("Scenario not found after create.");
    }

    public async Task<WritingScenarioView> UpdateAsync(string userId, Guid id, WritingScenarioView scenario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var entity = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");

        entity.Title = scenario.Title;
        entity.LetterType = scenario.LetterType;
        entity.Profession = scenario.Profession;
        entity.SubDiscipline = scenario.SubDiscipline;
        entity.TopicsJson = JsonSerializer.Serialize(scenario.Topics ?? Array.Empty<string>(), JsonOptions);
        entity.Difficulty = Math.Clamp(scenario.Difficulty, 1, 5);
        entity.IsDiagnostic = scenario.IsDiagnostic;
        entity.Version += 1;

        var existing = await db.WritingScenarioStructuredSentences
            .Where(s => s.ScenarioId == id)
            .ToListAsync(ct);
        db.WritingScenarioStructuredSentences.RemoveRange(existing);
        await PersistSentencesAsync(id, scenario.CaseNotesStructured, ct);
        AddAuditEvent(userId, "WritingScenario", id.ToString("D"), "writing.scenario.updated", scenario.Title);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(userId, id, ct))!;
    }

    public async Task<WritingScenarioView> ApproveAsync(string userId, Guid id, CancellationToken ct)
    {
        var entity = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw ApiException.NotFound("writing_scenario_not_found", "Scenario was not found.");
        entity.Status = "published";
        entity.ApprovedById = userId;
        entity.PublishedAt = clock.GetUtcNow();
        AddAuditEvent(userId, "WritingScenario", id.ToString("D"), "writing.scenario.approved", entity.Title);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(userId, id, ct))!;
    }

    private async Task<Dictionary<Guid, List<WritingScenarioStructuredSentence>>> LoadSentencesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        if (list.Count == 0) return new Dictionary<Guid, List<WritingScenarioStructuredSentence>>();
        return await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(s => list.Contains(s.ScenarioId))
            .OrderBy(s => s.Ordinal)
            .GroupBy(s => s.ScenarioId)
            .ToDictionaryAsync(g => g.Key, g => g.OrderBy(s => s.Ordinal).ToList(), ct);
    }

    private async Task PersistSentencesAsync(Guid scenarioId, IReadOnlyList<WritingScenarioStructuredSentenceDto> sentences, CancellationToken ct)
    {
        if (sentences.Count == 0) return;
        var now = clock.GetUtcNow();
        foreach (var s in sentences)
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenarioId,
                Ordinal = s.Ordinal,
                SentenceText = s.SentenceText,
                RelevanceLabel = NormalizeRelevance(s.Relevance),
                Notes = s.Notes,
                CreatedAt = now,
            });
        }
        await Task.CompletedTask;
    }

    private static WritingScenarioView ToView(WritingScenario row, IReadOnlyList<WritingScenarioStructuredSentence> sentences)
    {
        var topics = SafeDeserializeList(row.TopicsJson);
        return new WritingScenarioView(
            row.Id,
            row.Title,
            row.LetterType,
            row.Profession,
            row.SubDiscipline,
            topics,
            row.Difficulty,
            sentences.Select(s => new WritingScenarioStructuredSentenceDto(s.Ordinal, s.SentenceText, s.RelevanceLabel, s.Notes)).ToList(),
            row.IsDiagnostic,
            row.Status,
            row.CreatedAt,
            row.StimulusPdfMediaAssetId);
    }

    private static string NormalizeRelevance(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "relevant" => "relevant",
            "maybe" => "maybe",
            "irrelevant" => "irrelevant",
            "omit" => "irrelevant",
            "essential" => "relevant",
            _ => "relevant",
        };

    private static IReadOnlyList<string> SafeDeserializeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static int StableSeed(string userId, DateTime date)
        => (userId ?? string.Empty).GetHashCode(StringComparison.Ordinal) ^ date.GetHashCode();

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingScenarioListResponse> ListScenariosAsync(string userId, string? profession, string? letterType, int? difficulty, bool? isDiagnostic, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingScenarios.AsNoTracking().Where(s => s.Status == "published");
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(s => s.Profession.ToLower() == profession.ToLower());
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(s => s.LetterType == letterType);
        if (difficulty is { } diff) query = query.Where(s => s.Difficulty == diff);
        if (isDiagnostic == true) query = query.Where(s => s.IsDiagnostic);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search}%";
            query = query.Where(s => EF.Functions.ILike(s.Title, like));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(s => s.Difficulty).ThenBy(s => s.Title)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var sentences = await LoadSentencesAsync(rows.Select(r => r.Id), ct);
        var items = rows.Select(r => WritingV2ResponseMapper.ToResponse(ToView(r, (IReadOnlyList<WritingScenarioStructuredSentence>)(sentences.TryGetValue(r.Id, out var __ss) ? __ss : Array.Empty<WritingScenarioStructuredSentence>())))).ToList();
        return new WritingScenarioListResponse(items, total);
    }

    public async Task<WritingScenarioResponse?> GetScenarioAsync(string userId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(userId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingScenarioResponse?> GetRandomScenarioAsync(string userId, string? profession, string? letterType, CancellationToken ct)
    {
        var view = await PickRandomAsync(userId, new WritingScenarioFilter(profession, letterType, null, null, null, 50), ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingScenarioListResponse> AdminListScenariosAsync(string adminUserId, string? profession, string? letterType, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        _ = adminUserId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingScenarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(s => s.Profession == profession);
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(s => s.LetterType == letterType);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search}%";
            query = query.Where(s => EF.Functions.ILike(s.Title, like));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var sentences = await LoadSentencesAsync(rows.Select(r => r.Id), ct);
        var items = rows.Select(r => WritingV2ResponseMapper.ToResponse(ToView(r, (IReadOnlyList<WritingScenarioStructuredSentence>)(sentences.TryGetValue(r.Id, out var __ss) ? __ss : Array.Empty<WritingScenarioStructuredSentence>())))).ToList();
        return new WritingScenarioListResponse(items, total);
    }

    public async Task<WritingScenarioResponse> AdminCreateScenarioAsync(string adminUserId, WritingScenarioUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var view = ToScenarioView(Guid.Empty, request);
        var saved = await CreateAsync(adminUserId, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<WritingScenarioResponse?> AdminGetScenarioAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var view = await GetAsync(adminUserId, id, ct);
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingScenarioResponse?> AdminUpdateScenarioAsync(string adminUserId, Guid id, WritingScenarioUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await db.WritingScenarios.AsNoTracking().AnyAsync(s => s.Id == id, ct);
        if (!existing) return null;
        var view = ToScenarioView(id, request);
        var saved = await UpdateAsync(adminUserId, id, view, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    public async Task<bool> AdminDeleteScenarioAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var entity = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return false;
        db.WritingScenarios.Remove(entity);
        AddAuditEvent(adminUserId, "WritingScenario", id.ToString("D"), "writing.scenario.deleted", entity.Title);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<WritingScenarioResponse?> AdminApproveScenarioAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var existing = await db.WritingScenarios.AsNoTracking().AnyAsync(s => s.Id == id, ct);
        if (!existing) return null;
        var saved = await ApproveAsync(adminUserId, id, ct);
        return WritingV2ResponseMapper.ToResponse(saved);
    }

    private static WritingScenarioView ToScenarioView(Guid id, WritingScenarioUpsertRequest req)
        => new(
            Id: id,
            Title: req.Title,
            LetterType: req.LetterType,
            Profession: req.Profession,
            SubDiscipline: req.SubDiscipline,
            Topics: req.Topics ?? Array.Empty<string>(),
            Difficulty: req.Difficulty,
            CaseNotesStructured: (req.CaseNotesStructured ?? Array.Empty<WritingScenarioStructuredSentenceResponse>())
                .Select(s => new WritingScenarioStructuredSentenceDto(s.Index, s.Text, s.Relevance, null))
                .ToList(),
            IsDiagnostic: req.IsDiagnostic ?? false,
            Status: string.IsNullOrWhiteSpace(req.Status) ? "draft" : req.Status!,
            CreatedAt: DateTimeOffset.UtcNow);
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
}
