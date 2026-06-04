using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public interface IWritingDrillService
{
    Task<IReadOnlyList<WritingDrillSummaryResponse>> ListDrillsAsync(string userId, string? skill, CancellationToken ct);
    Task<WritingDrillDetailResponse?> GetDrillAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingDrillAttemptResponse> SubmitDrillAsync(string userId, Guid id, WritingDrillAttemptRequest request, CancellationToken ct);
    Task<WritingDrillListResponse> ListDrillsV2Async(string userId, string? drillType, string? targetSubSkill, string? profession, string? letterType, int? difficulty, bool dueOnly, int page, int pageSize, CancellationToken ct);
    Task<WritingDrillResponse?> GetDrillV2Async(string userId, Guid id, CancellationToken ct);
    Task<WritingDrillAttemptResultResponse?> SubmitDrillAttemptV2Async(string userId, Guid id, WritingDrillAttemptRequestV2 request, CancellationToken ct);
    Task<WritingDrillListResponse> AdminListDrillsAsync(string adminUserId, string? drillType, string? targetSubSkill, string? status, int page, int pageSize, CancellationToken ct);
    Task<WritingDrillResponse> AdminCreateDrillAsync(string adminUserId, WritingDrillUpsertRequest request, CancellationToken ct);
    Task<WritingDrillResponse?> AdminGetDrillAsync(string adminUserId, Guid id, CancellationToken ct);
    Task<WritingDrillResponse?> AdminUpdateDrillAsync(string adminUserId, Guid id, WritingDrillUpsertRequest request, CancellationToken ct);
    Task<bool> AdminDeleteDrillAsync(string adminUserId, Guid id, CancellationToken ct);
}

public sealed class WritingDrillService(LearnerDbContext db, TimeProvider clock) : IWritingDrillService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingDrillSummaryResponse>> ListDrillsAsync(string userId, string? skill, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var query = db.WritingDrills.AsNoTracking().Where(d => d.Status == "published");
        if (!string.IsNullOrWhiteSpace(skill)) query = query.Where(d => d.TargetSubSkill == skill);
        var drills = await query.OrderBy(d => d.TargetSubSkill).ThenBy(d => d.Difficulty).ToListAsync(ct);
        var attempts = await LoadDrillAttemptsAsync(userId, drills.Select(d => d.Id), ct);
        return drills.Select(d => ToSummary(d, attempts.GetValueOrDefault(d.Id) ?? [])).ToList();
    }

    public async Task<WritingDrillDetailResponse?> GetDrillAsync(string userId, Guid id, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Status == "published", ct);
        if (drill is null) return null;
        var attempts = await LoadDrillAttemptsAsync(userId, [drill.Id], ct);
        return ToDetail(drill, attempts.GetValueOrDefault(drill.Id) ?? []);
    }

    public async Task<WritingDrillAttemptResponse> SubmitDrillAsync(string userId, Guid id, WritingDrillAttemptRequest request, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingDrills.FirstOrDefaultAsync(d => d.Id == id && d.Status == "published", ct)
            ?? throw ApiException.NotFound("writing_drill_not_found", "Writing drill was not found.");
        var response = (request.ResponseText ?? string.Empty).Trim();
        if (response.Length == 0)
        {
            throw ApiException.Validation("writing_drill_response_required", "A drill response is required.");
        }

        var accepted = AcceptedAnswers(drill).Select(NormalizeAnswer).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (accepted.Count == 0)
        {
            throw ApiException.Conflict("writing_drill_answer_key_missing", "This Writing drill is missing an answer key.");
        }
        var normalized = NormalizeAnswer(response);
        var isCorrect = accepted.Contains(normalized);
        var prior = await db.WritingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.DrillId == id)
            .OrderByDescending(a => a.AttemptedAt)
            .FirstOrDefaultAsync(ct);
        var repetitions = isCorrect ? (prior?.Repetitions ?? 0) + 1 : 0;
        var intervalDays = isCorrect ? Math.Clamp(repetitions * 2, 1, 14) : 1;
        var now = clock.GetUtcNow();
        var attempt = new WritingDrillAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = id,
            ResponseText = response,
            IsCorrect = isCorrect,
            FeedbackText = isCorrect ? "Accepted." : "Review the expected pattern and try again.",
            TimeSpentSeconds = request.TimeSpentSeconds,
            Repetitions = repetitions,
            IntervalDays = intervalDays,
            NextDueAt = now.AddDays(intervalDays),
            AttemptedAt = now,
        };
        db.WritingDrillAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return new WritingDrillAttemptResponse(attempt.Id, attempt.IsCorrect, attempt.FeedbackText!, attempt.NextDueAt, attempt.Repetitions);
    }

    public async Task<WritingDrillListResponse> ListDrillsV2Async(
        string userId,
        string? drillType,
        string? targetSubSkill,
        string? profession,
        string? letterType,
        int? difficulty,
        bool dueOnly,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingDrills.AsNoTracking().Where(d => d.Status == "published");
        if (!string.IsNullOrWhiteSpace(drillType)) query = query.Where(d => d.DrillType == drillType);
        if (!string.IsNullOrWhiteSpace(targetSubSkill)) query = query.Where(d => d.TargetSubSkill == targetSubSkill);
        if (difficulty.HasValue) query = query.Where(d => d.Difficulty == difficulty.Value);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(d => d.AppliesToProfessionsJson.Contains(profession) || d.AppliesToProfessionsJson == "[]");
        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(d => d.AppliesToLetterTypesJson.Contains(letterType) || d.AppliesToLetterTypesJson == "[]");
        if (dueOnly)
        {
            var dueIds = await db.WritingDrillAttempts.AsNoTracking()
                .Where(a => a.UserId == userId && a.NextDueAt <= clock.GetUtcNow())
                .Select(a => a.DrillId)
                .Distinct()
                .ToListAsync(ct);
            query = query.Where(d => dueIds.Contains(d.Id));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.TargetSubSkill).ThenBy(d => d.Difficulty)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => ToV2Response(d, includeAnswers: false))
            .ToListAsync(ct);
        return new WritingDrillListResponse(items, total);
    }

    public async Task<WritingDrillResponse?> GetDrillV2Async(string userId, Guid id, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Status == "published", ct);
        return drill is null ? null : ToV2Response(drill, includeAnswers: false);
    }

    public async Task<WritingDrillAttemptResultResponse?> SubmitDrillAttemptV2Async(string userId, Guid id, WritingDrillAttemptRequestV2 request, CancellationToken ct)
    {
        if (request.DrillId != Guid.Empty && request.DrillId != id)
        {
            throw ApiException.Validation("writing_drill_id_mismatch", "Drill id in the route and request must match.");
        }
        var result = await SubmitDrillAsync(userId, id, new WritingDrillAttemptRequest(request.ResponseText, null), ct);
        return new WritingDrillAttemptResultResponse(id, result.IsCorrect, result.FeedbackText, null, result.NextDueAt ?? clock.GetUtcNow().AddDays(1), result.Repetitions <= 0 ? 2.5 : 2.7);
    }

    public async Task<WritingDrillListResponse> AdminListDrillsAsync(string adminUserId, string? drillType, string? targetSubSkill, string? status, int page, int pageSize, CancellationToken ct)
    {
        _ = adminUserId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.WritingDrills.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(drillType)) query = query.Where(d => d.DrillType == drillType);
        if (!string.IsNullOrWhiteSpace(targetSubSkill)) query = query.Where(d => d.TargetSubSkill == targetSubSkill);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.TargetSubSkill).ThenBy(d => d.Difficulty)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => ToV2Response(d, includeAnswers: true))
            .ToListAsync(ct);
        return new WritingDrillListResponse(items, total);
    }

    public async Task<WritingDrillResponse> AdminCreateDrillAsync(string adminUserId, WritingDrillUpsertRequest request, CancellationToken ct)
    {
        var drill = new WritingDrill { Id = Guid.NewGuid(), CreatedAt = clock.GetUtcNow() };
        ApplyUpsert(drill, request);
        db.WritingDrills.Add(drill);
        AddAuditEvent(adminUserId, "WritingDrill", drill.Id.ToString("D"), "writing.drill.created", drill.TargetSubSkill);
        await db.SaveChangesAsync(ct);
        return ToV2Response(drill, includeAnswers: true);
    }

    public async Task<WritingDrillResponse?> AdminGetDrillAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        _ = adminUserId;
        var drill = await db.WritingDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return drill is null ? null : ToV2Response(drill, includeAnswers: true);
    }

    public async Task<WritingDrillResponse?> AdminUpdateDrillAsync(string adminUserId, Guid id, WritingDrillUpsertRequest request, CancellationToken ct)
    {
        var drill = await db.WritingDrills.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (drill is null) return null;
        ApplyUpsert(drill, request);
        AddAuditEvent(adminUserId, "WritingDrill", id.ToString("D"), "writing.drill.updated", drill.TargetSubSkill);
        await db.SaveChangesAsync(ct);
        return ToV2Response(drill, includeAnswers: true);
    }

    public async Task<bool> AdminDeleteDrillAsync(string adminUserId, Guid id, CancellationToken ct)
    {
        var drill = await db.WritingDrills.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (drill is null) return false;
        db.WritingDrills.Remove(drill);
        AddAuditEvent(adminUserId, "WritingDrill", id.ToString("D"), "writing.drill.deleted", drill.TargetSubSkill);
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

    public async Task<IReadOnlyList<WritingCaseNoteDrillSummaryResponse>> ListCaseNoteDrillsAsync(string userId, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drills = await db.WritingCaseNoteDrills.AsNoTracking()
            .Where(d => d.Status == "published" && d.Format == "tag-relevance")
            .OrderBy(d => d.Profession)
            .ThenBy(d => d.Difficulty)
            .ToListAsync(ct);
        var drillIds = drills.Select(d => d.Id).ToList();
        // Project the grouped count into an anonymous type before materialising.
        // A bare GroupBy fed straight into ToDictionaryAsync cannot be translated
        // by the EF Core InMemory provider (and is brittle on relational ones);
        // composing the aggregate into a Select keeps the query server-side on
        // every provider, then we build the lookup client-side.
        var sentenceCounts = (await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => drillIds.Contains(s.DrillId))
            .GroupBy(s => s.DrillId)
            .Select(g => new { DrillId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.DrillId, x => x.Count);
        var attemptCounts = (await db.WritingCaseNoteDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && drillIds.Contains(a.DrillId))
            .GroupBy(a => a.DrillId)
            .Select(g => new { DrillId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.DrillId, x => x.Count);

        return drills.Select(d => new WritingCaseNoteDrillSummaryResponse(
            d.Id, d.Title, d.Profession, d.LetterType, d.Difficulty,
            sentenceCounts.GetValueOrDefault(d.Id), attemptCounts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<WritingCaseNoteDrillDetailResponse?> GetCaseNoteDrillAsync(string userId, Guid id, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingCaseNoteDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Status == "published" && d.Format == "tag-relevance", ct);
        if (drill is null) return null;
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => s.DrillId == id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        var attemptCount = await db.WritingCaseNoteDrillAttempts.AsNoTracking().CountAsync(a => a.UserId == userId && a.DrillId == id, ct);
        return new WritingCaseNoteDrillDetailResponse(
            drill.Id,
            drill.Title,
            drill.Profession,
            drill.LetterType,
            drill.Format,
            drill.CaseNotesMarkdown,
            drill.Difficulty,
            sentences.Count,
            sentences.Select(s => new WritingCaseNoteDrillSentenceResponse(s.Id, s.Ordinal, s.SentenceText)).ToList(),
            attemptCount);
    }

    public async Task<WritingCaseNoteDrillAttemptResponse> SubmitCaseNoteDrillAsync(string userId, Guid id, WritingCaseNoteDrillAttemptRequest request, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingCaseNoteDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Status == "published" && d.Format == "tag-relevance", ct)
            ?? throw ApiException.NotFound("writing_case_note_drill_not_found", "Writing case-note drill was not found.");
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => s.DrillId == drill.Id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        if (sentences.Count == 0) throw ApiException.Conflict("writing_case_note_drill_empty", "This case-note drill has no sentences yet.");

        var feedback = new List<WritingCaseNoteDrillFeedbackResponse>();
        var correct = 0;
        foreach (var sentence in sentences)
        {
            request.Responses.TryGetValue(sentence.Id, out var submittedLabel);
            var isCorrect = string.Equals(NormalizeCaseNoteLabel(submittedLabel), NormalizeCaseNoteLabel(sentence.RelevanceLabel), StringComparison.OrdinalIgnoreCase);
            if (isCorrect) correct += 1;
            feedback.Add(new WritingCaseNoteDrillFeedbackResponse(sentence.Id, isCorrect, NormalizeCaseNoteLabel(sentence.RelevanceLabel), sentence.Rationale));
        }

        var score = Math.Round(correct * 100.0 / sentences.Count, 1);
        var attempt = new WritingCaseNoteDrillAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = drill.Id,
            ResponsesJson = JsonSerializer.Serialize(request.Responses, JsonOptions),
            CorrectCount = correct,
            TotalCount = sentences.Count,
            ScorePercent = score,
            TimeSpentSeconds = request.TimeSpentSeconds,
            AttemptedAt = clock.GetUtcNow(),
        };
        db.WritingCaseNoteDrillAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return new WritingCaseNoteDrillAttemptResponse(attempt.Id, correct, sentences.Count, score, feedback);
    }

    public async Task<WritingCaseNoteDrillListResponseV2> ListCaseNoteDrillsV2Async(string userId, string? profession, string? format, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var query = db.WritingCaseNoteDrills.AsNoTracking().Where(d => d.Status == "published");
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(d => d.Profession == profession);
        if (!string.IsNullOrWhiteSpace(format)) query = query.Where(d => d.Format == format);
        var drills = await query.OrderBy(d => d.Profession).ThenBy(d => d.Difficulty).ToListAsync(ct);
        var drillIds = drills.Select(d => d.Id).ToList();
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => drillIds.Contains(s.DrillId))
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        var byDrill = sentences.GroupBy(s => s.DrillId).ToDictionary(g => g.Key, g => g.ToList());
        var items = drills.Select(d => new WritingCaseNoteDrillResponseV2(
            d.Id,
            d.Format,
            null,
            d.Profession,
            d.CaseNotesMarkdown,
            (byDrill.GetValueOrDefault(d.Id) ?? []).Select(s => new WritingCaseNoteDrillSentenceResponseV2(s.Ordinal, s.SentenceText, null)).ToList(),
            d.Format == "tag-relevance" ? ["essential", "relevant", "omit"] : null,
            d.Status)).ToList();
        return new WritingCaseNoteDrillListResponseV2(items, items.Count);
    }

    public async Task<WritingCaseNoteDrillAttemptResultResponseV2?> SubmitCaseNoteDrillAttemptV2Async(string userId, Guid id, WritingCaseNoteDrillAttemptRequestV2 request, CancellationToken ct)
    {
        await EnsureStarterDrillsAsync(ct);
        var drill = await db.WritingCaseNoteDrills.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Status == "published", ct);
        if (drill is null) return null;
        var sentences = await db.WritingCaseNoteDrillSentences.AsNoTracking()
            .Where(s => s.DrillId == id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
        var selected = request.SelectedIndices.ToHashSet();
        var verdicts = new List<WritingCaseNoteDrillSentenceVerdictResponse>();
        var correct = 0;
        foreach (var sentence in sentences)
        {
            var learnerLabel = selected.Contains(sentence.Ordinal) ? "relevant" : "omit";
            var correctLabel = NormalizeCaseNoteLabel(sentence.RelevanceLabel) == "omit" ? "omit" : "relevant";
            var isCorrect = learnerLabel == correctLabel;
            if (isCorrect) correct += 1;
            verdicts.Add(new WritingCaseNoteDrillSentenceVerdictResponse(sentence.Ordinal, learnerLabel, correctLabel, isCorrect ? "correct" : "review"));
        }
        var score = sentences.Count == 0 ? 0 : (int)Math.Round(correct * 100.0 / sentences.Count);
        db.WritingCaseNoteDrillAttempts.Add(new WritingCaseNoteDrillAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = id,
            ResponsesJson = JsonSerializer.Serialize(request.SelectedIndices, JsonOptions),
            CorrectCount = correct,
            TotalCount = sentences.Count,
            ScorePercent = score,
            AttemptedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return new WritingCaseNoteDrillAttemptResultResponseV2(id, score, verdicts);
    }

    private async Task<Dictionary<Guid, List<WritingDrillAttempt>>> LoadDrillAttemptsAsync(string userId, IEnumerable<Guid> drillIds, CancellationToken ct)
    {
        var ids = drillIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, List<WritingDrillAttempt>>();

        // Fetch raw rows then group client-side. EF Core (and the InMemory test
        // provider) cannot translate `GroupBy(...).ToDictionaryAsync(g => g.Key,
        // g => g.OrderByDescending(...).ToList(), ct)` because the value projector
        // requires a subquery+materialisation per group. The materialised count is
        // bounded by the number of attempts for this learner across the requested
        // drills, which is small in practice.
        var attempts = await db.WritingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.DrillId))
            .ToListAsync(ct);
        return attempts
            .GroupBy(a => a.DrillId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AttemptedAt).ToList());
    }

    private static WritingDrillSummaryResponse ToSummary(WritingDrill drill, IReadOnlyList<WritingDrillAttempt> attempts)
        => new(drill.Id, drill.DrillType, drill.TargetSubSkill, drill.TargetCanonRuleId, drill.Difficulty, 10, DrillTitle(drill), attempts.Count, attempts.FirstOrDefault()?.NextDueAt);

    private static WritingDrillDetailResponse ToDetail(WritingDrill drill, IReadOnlyList<WritingDrillAttempt> attempts)
        => new(drill.Id, drill.DrillType, drill.TargetSubSkill, drill.TargetCanonRuleId, drill.Difficulty, 10, DrillTitle(drill), drill.PromptMarkdown, drill.GradingMethod, attempts.Count, attempts.FirstOrDefault()?.NextDueAt);

    private static string DrillTitle(WritingDrill drill)
        => $"{drill.TargetSubSkill} {drill.DrillType.Replace('_', ' ')}";

    private static IReadOnlyList<string> AcceptedAnswers(WritingDrill drill)
    {
        var answers = new List<string>();
        if (!string.IsNullOrWhiteSpace(drill.ExpectedAnswer)) answers.Add(drill.ExpectedAnswer);
        try
        {
            answers.AddRange(JsonSerializer.Deserialize<List<string>>(drill.AlternativesJson, JsonOptions) ?? []);
        }
        catch
        {
            // Ignore malformed authored alternatives; the prompt still remains usable.
        }
        return answers;
    }

    private static WritingDrillResponse ToV2Response(WritingDrill drill, bool includeAnswers)
        => new(
            drill.Id,
            drill.DrillType,
            "text",
            drill.TargetSubSkill,
            drill.TargetCanonRuleId,
            ParseStringArray(drill.AppliesToProfessionsJson),
            ParseStringArray(drill.AppliesToLetterTypesJson),
            drill.Difficulty,
            drill.PromptMarkdown,
            includeAnswers ? drill.ExpectedAnswer : null,
            includeAnswers ? ParseStringArray(drill.AlternativesJson) : null,
            includeAnswers ? ParseStringArray(drill.GradingConfigJson) : null,
            drill.GradingMethod,
            drill.Status);

    private static void ApplyUpsert(WritingDrill drill, WritingDrillUpsertRequest request)
    {
        drill.DrillType = request.DrillType.Trim();
        drill.TargetSubSkill = request.TargetSubSkill.Trim().ToUpperInvariant();
        drill.TargetCanonRuleId = string.IsNullOrWhiteSpace(request.TargetCanonRuleId) ? null : request.TargetCanonRuleId.Trim();
        drill.AppliesToProfessionsJson = JsonSerializer.Serialize(request.AppliesToProfessions ?? [], JsonOptions);
        drill.AppliesToLetterTypesJson = JsonSerializer.Serialize(request.AppliesToLetterTypes ?? [], JsonOptions);
        drill.Difficulty = request.Difficulty;
        drill.PromptMarkdown = request.PromptMarkdown;
        drill.ExpectedAnswer = request.ExpectedAnswer;
        drill.AlternativesJson = JsonSerializer.Serialize(request.Alternatives ?? [], JsonOptions);
        drill.GradingConfigJson = JsonSerializer.Serialize(request.Options ?? [], JsonOptions);
        drill.GradingMethod = request.GradingMethod.Trim();
        drill.Status = string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status.Trim();
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }

    private static string NormalizeAnswer(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace(".", string.Empty).Replace(",", string.Empty);

    private static string NormalizeCaseNoteLabel(string? value)
        => NormalizeAnswer(value) switch
        {
            "irrelevant" => "omit",
            "maybe" => "relevant",
            var normalized => normalized,
        };

    private async Task EnsureStarterDrillsAsync(CancellationToken ct)
    {
        var hasDrills = await db.WritingDrills.AnyAsync(ct);
        var hasCaseNoteDrills = await db.WritingCaseNoteDrills.AnyAsync(ct);
        if (hasDrills && hasCaseNoteDrills) return;

        var now = clock.GetUtcNow();
        if (!hasDrills)
        {
            db.WritingDrills.AddRange(new[]
            {
                StarterDrill(1, "opening", "W2", "Write one opening purpose sentence for a referral letter.", "I am writing to refer", now),
                StarterDrill(2, "tone", "W6", "Rewrite this sentence in a respectful professional tone: patient is bad at taking tablets.", "has difficulty adhering to medication", now),
                StarterDrill(3, "expansion", "W4", "Turn note fragments into one complete sentence: BP high, dizziness, follow-up needed.", "The patient has raised blood pressure and dizziness and requires follow-up", now),
            });
        }

        if (!hasCaseNoteDrills)
        {
            var drillId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            db.WritingCaseNoteDrills.Add(new WritingCaseNoteDrill
            {
                Id = drillId,
                Title = "Referral relevance starter",
                Profession = "medicine",
                LetterType = "LT-RR",
                Format = "tag-relevance",
                CaseNotesMarkdown = "Starter case-note relevance drill. Replace with approved authored content before final curriculum use.",
                Difficulty = 1,
                Status = "published",
                CreatedAt = now,
            });
            db.WritingCaseNoteDrillSentences.AddRange(new[]
            {
                StarterSentence(drillId, 1, "Recent dizziness with raised blood pressure at review.", "essential", "Directly supports referral purpose."),
                StarterSentence(drillId, 2, "Enjoys gardening at weekends.", "omit", "Not relevant unless linked to care needs."),
                StarterSentence(drillId, 3, "Medication adherence has been inconsistent.", "relevant", "Useful context for the receiving clinician."),
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static WritingDrill StarterDrill(int index, string type, string skill, string prompt, string expected, DateTimeOffset now)
        => new()
        {
            Id = Guid.Parse($"20000000-0000-0000-0000-{index:000000000000}"),
            DrillType = type,
            TargetSubSkill = skill,
            Difficulty = 1,
            PromptMarkdown = $"Starter drill shell. Replace with approved authored content before final curriculum use.\n\n{prompt}",
            ExpectedAnswer = expected,
            AlternativesJson = "[]",
            GradingMethod = "exact",
            Status = "published",
            CreatedAt = now,
        };

    private static WritingCaseNoteDrillSentence StarterSentence(Guid drillId, int ordinal, string text, string label, string rationale)
        => new()
        {
            Id = Guid.Parse($"30000000-0000-0000-0001-{ordinal:000000000000}"),
            DrillId = drillId,
            Ordinal = ordinal,
            SentenceText = text,
            RelevanceLabel = label,
            Rationale = rationale,
        };
}