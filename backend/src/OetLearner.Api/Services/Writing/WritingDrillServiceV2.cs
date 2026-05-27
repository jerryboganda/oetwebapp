using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingDrillV2Filter(string? SubSkill, string? Profession, string? LetterType, int Take = 30);

public sealed record WritingDrillV2View(
    Guid Id,
    string DrillType,
    string TargetSubSkill,
    string? TargetCanonRuleId,
    int Difficulty,
    string PromptMarkdown,
    string GradingMethod,
    IReadOnlyList<string> Alternatives,
    string? ExpectedAnswer,
    string Status,
    DateTimeOffset? NextDueAt);

public sealed record WritingDrillV2AttemptRequest(string ResponseText, int? TimeSpentSeconds);

public sealed record WritingDrillV2AttemptResult(
    Guid AttemptId,
    bool IsCorrect,
    string FeedbackText,
    double EaseFactor,
    int IntervalDays,
    int Repetitions,
    DateTimeOffset? NextDueAt);

public interface IWritingDrillServiceV2
{
    Task<IReadOnlyList<WritingDrillV2View>> ListAsync(string userId, WritingDrillV2Filter filter, CancellationToken ct);
    Task<WritingDrillV2View?> GetAsync(string userId, Guid id, CancellationToken ct);
    Task<WritingDrillV2AttemptResult> SubmitAttemptAsync(string userId, Guid drillId, WritingDrillV2AttemptRequest request, CancellationToken ct);
    Task<WritingDrillV2View> UpsertAsync(string adminId, WritingDrillV2View drill, CancellationToken ct);
}

public sealed class WritingDrillServiceV2(
    LearnerDbContext db,
    TimeProvider clock,
    IAiGatewayService aiGateway,
    ILogger<WritingDrillServiceV2> logger) : IWritingDrillServiceV2
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WritingDrillV2View>> ListAsync(string userId, WritingDrillV2Filter filter, CancellationToken ct)
    {
        var query = db.WritingDrills.AsNoTracking().Where(d => d.Status == "published");
        if (!string.IsNullOrWhiteSpace(filter.SubSkill)) query = query.Where(d => d.TargetSubSkill == filter.SubSkill);
        var drills = await query.OrderBy(d => d.Difficulty).Take(filter.Take).ToListAsync(ct);
        var ids = drills.Select(d => d.Id).ToList();
        var due = await db.WritingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.DrillId))
            .GroupBy(a => a.DrillId)
            .Select(g => new { DrillId = g.Key, Next = g.Max(x => x.NextDueAt) })
            .ToDictionaryAsync(x => x.DrillId, x => x.Next, ct);
        return drills.Select(d => ToView(d, due.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<WritingDrillV2View?> GetAsync(string userId, Guid id, CancellationToken ct)
    {
        var d = await db.WritingDrills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == "published", ct);
        if (d is null) return null;
        var nextDue = await db.WritingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.DrillId == id)
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => a.NextDueAt)
            .FirstOrDefaultAsync(ct);
        return ToView(d, nextDue);
    }

    public async Task<WritingDrillV2AttemptResult> SubmitAttemptAsync(string userId, Guid drillId, WritingDrillV2AttemptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var drill = await db.WritingDrills.FirstOrDefaultAsync(d => d.Id == drillId && d.Status == "published", ct)
            ?? throw ApiException.NotFound("writing_drill_not_found", "Writing drill was not found.");
        var response = (request.ResponseText ?? string.Empty).Trim();
        if (response.Length == 0)
        {
            throw ApiException.Validation("writing_drill_response_required", "A drill response is required.");
        }

        bool isCorrect;
        string feedback;
        switch (drill.GradingMethod.ToLowerInvariant())
        {
            case "regex":
                (isCorrect, feedback) = GradeRegex(drill, response);
                break;
            case "llm":
                (isCorrect, feedback) = await GradeLlmAsync(drill, response, userId, ct);
                break;
            default:
                (isCorrect, feedback) = GradeExact(drill, response);
                break;
        }

        var prior = await db.WritingDrillAttempts
            .Where(a => a.UserId == userId && a.DrillId == drillId)
            .OrderByDescending(a => a.AttemptedAt)
            .FirstOrDefaultAsync(ct);

        var (easeFactor, intervalDays, repetitions) = ComputeSm2(prior, isCorrect);
        var now = clock.GetUtcNow();
        var attempt = new WritingDrillAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DrillId = drillId,
            ResponseText = response,
            IsCorrect = isCorrect,
            FeedbackText = feedback,
            TimeSpentSeconds = request.TimeSpentSeconds,
            EaseFactor = easeFactor,
            IntervalDays = intervalDays,
            Repetitions = repetitions,
            NextDueAt = now.AddDays(intervalDays),
            AttemptedAt = now,
        };
        db.WritingDrillAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return new WritingDrillV2AttemptResult(attempt.Id, attempt.IsCorrect, attempt.FeedbackText!, attempt.EaseFactor, attempt.IntervalDays, attempt.Repetitions, attempt.NextDueAt);
    }

    public async Task<WritingDrillV2View> UpsertAsync(string adminId, WritingDrillV2View drill, CancellationToken ct)
    {
        _ = adminId;
        ArgumentNullException.ThrowIfNull(drill);
        var entity = drill.Id == Guid.Empty ? null : await db.WritingDrills.FirstOrDefaultAsync(d => d.Id == drill.Id, ct);
        var now = clock.GetUtcNow();
        if (entity is null)
        {
            entity = new WritingDrill { Id = drill.Id == Guid.Empty ? Guid.NewGuid() : drill.Id, CreatedAt = now };
            db.WritingDrills.Add(entity);
        }
        entity.DrillType = drill.DrillType;
        entity.TargetSubSkill = drill.TargetSubSkill;
        entity.TargetCanonRuleId = drill.TargetCanonRuleId;
        entity.Difficulty = Math.Clamp(drill.Difficulty, 1, 5);
        entity.PromptMarkdown = drill.PromptMarkdown;
        entity.GradingMethod = string.IsNullOrWhiteSpace(drill.GradingMethod) ? "exact" : drill.GradingMethod;
        entity.AlternativesJson = JsonSerializer.Serialize(drill.Alternatives ?? Array.Empty<string>(), JsonOptions);
        entity.ExpectedAnswer = drill.ExpectedAnswer;
        entity.Status = drill.Status;
        await db.SaveChangesAsync(ct);
        return ToView(entity, null);
    }

    private static (bool IsCorrect, string Feedback) GradeExact(WritingDrill drill, string response)
    {
        var accepted = ReadAccepted(drill).Select(NormalizeAnswer).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (accepted.Count == 0) throw ApiException.Conflict("writing_drill_answer_key_missing", "Drill is missing an answer key.");
        var normalized = NormalizeAnswer(response);
        var ok = accepted.Contains(normalized);
        return (ok, ok ? "Accepted." : "Compare with the expected wording and try again.");
    }

    private static (bool IsCorrect, string Feedback) GradeRegex(WritingDrill drill, string response)
    {
        var pattern = string.IsNullOrWhiteSpace(drill.ExpectedAnswer) ? drill.AlternativesJson : drill.ExpectedAnswer!;
        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            var ok = regex.IsMatch(response);
            return (ok, ok ? "Accepted." : "Response does not match the required pattern.");
        }
        catch (ArgumentException)
        {
            return (false, "Drill regex is invalid; please notify support.");
        }
    }

    private async Task<(bool IsCorrect, string Feedback)> GradeLlmAsync(WritingDrill drill, string response, string userId, CancellationToken ct)
    {
        try
        {
            var prompt = aiGateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Task = AiTaskMode.Score,
            });
            var input = $"Drill prompt:\n{drill.PromptMarkdown}\n\nExpected: {drill.ExpectedAnswer ?? "(open)"}\n\nLearner response:\n{response}\n\nReturn JSON {{ \"correct\": bool, \"feedback\": string }}.";
            var result = await aiGateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = input,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.WritingDrillGradeV1,
                PromptTemplateId = "writing.drill.grade.v1",
                UserId = userId,
            }, ct);
            return ParseLlmDrillResponse(result.Completion);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing LLM drill grading failed; falling back to exact.");
            return GradeExact(drill, response);
        }
    }

    private static (bool IsCorrect, string Feedback) ParseLlmDrillResponse(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return (false, "AI grading returned no content.");
        var start = completion.IndexOf('{');
        var end = completion.LastIndexOf('}');
        if (start < 0 || end <= start) return (false, "AI grading returned malformed JSON.");
        try
        {
            using var doc = JsonDocument.Parse(completion[start..(end + 1)]);
            var correct = doc.RootElement.TryGetProperty("correct", out var cEl) && cEl.ValueKind == JsonValueKind.True;
            var feedback = doc.RootElement.TryGetProperty("feedback", out var fEl) && fEl.ValueKind == JsonValueKind.String ? fEl.GetString() ?? string.Empty : string.Empty;
            return (correct, string.IsNullOrWhiteSpace(feedback) ? (correct ? "Accepted." : "Try again.") : feedback);
        }
        catch (JsonException) { return (false, "AI grading returned malformed JSON."); }
    }

    private static (double Ease, int Interval, int Repetitions) ComputeSm2(WritingDrillAttempt? prior, bool isCorrect)
    {
        var ease = prior?.EaseFactor ?? 2.5;
        var repetitions = prior?.Repetitions ?? 0;
        int interval;
        if (!isCorrect)
        {
            repetitions = 0;
            ease = Math.Max(1.3, ease - 0.2);
            interval = 1;
        }
        else
        {
            repetitions += 1;
            ease = Math.Max(1.3, ease + 0.1);
            interval = repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => (int)Math.Round((prior?.IntervalDays ?? 6) * ease),
            };
            interval = Math.Clamp(interval, 1, 30);
        }
        return (Math.Round(ease, 2), interval, repetitions);
    }

    private static WritingDrillV2View ToView(WritingDrill drill, DateTimeOffset? nextDue)
        => new(drill.Id, drill.DrillType, drill.TargetSubSkill, drill.TargetCanonRuleId, drill.Difficulty, drill.PromptMarkdown,
            drill.GradingMethod, ReadAlternatives(drill), drill.ExpectedAnswer, drill.Status, nextDue);

    private static IReadOnlyList<string> ReadAccepted(WritingDrill drill)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(drill.ExpectedAnswer)) list.Add(drill.ExpectedAnswer!);
        list.AddRange(ReadAlternatives(drill));
        return list;
    }

    private static IReadOnlyList<string> ReadAlternatives(WritingDrill drill)
    {
        try { return JsonSerializer.Deserialize<List<string>>(drill.AlternativesJson, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string NormalizeAnswer(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace(".", string.Empty).Replace(",", string.Empty);
}
