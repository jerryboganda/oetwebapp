using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class ConversationService(
    LearnerDbContext db,
    IOptions<ConversationOptions> conversationOptions,
    Conversation.IConversationEntitlementService entitlement)
{
    private readonly ConversationOptions _options = conversationOptions.Value;

    public async Task<object> CreateSessionAsync(string userId, ConversationCreateSessionRequest request, CancellationToken ct)
    {
        if (!_options.Enabled)
            throw ApiException.Validation("CONVERSATION_DISABLED", "AI Conversation is currently disabled.");

        var taskType = NormaliseTaskType(request.TaskTypeCode);
        if (!_options.EnabledTaskTypes.Contains(taskType, StringComparer.OrdinalIgnoreCase))
            throw ApiException.Validation("TASK_TYPE_NOT_ENABLED",
                $"Task type '{taskType}' is not enabled.");

        var ent = await entitlement.CheckAsync(userId, ct);
        if (!ent.Allowed)
            throw ApiException.Validation("ENTITLEMENT_BLOCKED", ent.Reason);

        var profession = (request.Profession ?? "medicine").Trim().ToLowerInvariant();
        var difficulty = (request.Difficulty ?? "medium").Trim().ToLowerInvariant();

        var template = await PickTemplateAsync(userId, taskType, profession, ct);
        _ = difficulty;

        var sessionId = $"cs-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var session = new ConversationSession
        {
            Id = sessionId,
            UserId = userId,
            ContentId = request.ContentId,
            TemplateId = template?.Id,
            ExamTypeCode = "oet",
            SubtestCode = "speaking",
            TaskTypeCode = taskType,
            Profession = profession,
            ScenarioJson = template is not null
                ? BuildScenarioJsonFromTemplate(template)
                : BuildFallbackScenarioJson(taskType, profession),
            State = "preparing",
            TurnCount = 0,
            DurationSeconds = 0,
            TranscriptJson = "[]",
            CreatedAt = now,
        };
        db.ConversationSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return MapSession(session);
    }

    public async Task<object> GetSessionAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");
        return MapSession(session);
    }

    public async Task<object> CompleteSessionAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");

        if (session.State is "completed" or "evaluated")
            throw ApiException.Validation("ALREADY_COMPLETED", "Session is already completed.");

        var now = DateTimeOffset.UtcNow;
        session.State = "evaluating";
        session.CompletedAt = now;
        session.DurationSeconds = session.StartedAt.HasValue
            ? (int)(now - session.StartedAt.Value).TotalSeconds : 0;

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.ConversationEvaluation,
            ResourceId = sessionId,
            State = AsyncState.Queued,
            AvailableAt = now.AddSeconds(1),
            CreatedAt = now,
            LastTransitionAt = now,
            StatusReasonCode = "queued",
            StatusMessage = "Conversation evaluation queued.",
        });

        await db.SaveChangesAsync(ct);
        return MapSession(session);
    }

    public async Task<object> GetEvaluationAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");

        if (session.State is not ("evaluated" or "completed"))
            return new { sessionId, state = session.State, ready = false,
                message = session.State == "evaluating"
                    ? "Evaluation is in progress. Please check back shortly."
                    : "Session has not been completed yet." };

        var evaluation = await db.ConversationEvaluations.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SessionId == sessionId, ct);
        if (evaluation is null)
            return new { sessionId, state = session.State, ready = false,
                message = "Evaluation is finalising, please check back in a moment." };

        var annotations = await db.ConversationTurnAnnotations.AsNoTracking()
            .Where(a => a.EvaluationId == evaluation.Id)
            .OrderBy(a => a.TurnNumber).ToListAsync(ct);

        var turns = await db.ConversationTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TurnNumber).ToListAsync(ct);

        return new
        {
            sessionId,
            state = session.State,
            ready = true,
            scaledScore = evaluation.OverallScaled,
            scaledMax = 500,
            passScaled = 350,
            passed = evaluation.Passed,
            overallGrade = evaluation.OverallGrade,
            criteria = JsonSupport.Deserialize<object[]>(evaluation.CriteriaJson, Array.Empty<object>()),
            turnAnnotations = annotations.Select(a => new
            {
                id = a.Id, turnNumber = a.TurnNumber, type = a.Type,
                category = a.Category, ruleId = a.RuleId, evidence = a.Evidence, suggestion = a.Suggestion,
            }),
            strengths = JsonSupport.Deserialize<string[]>(evaluation.StrengthsJson, Array.Empty<string>()),
            improvements = JsonSupport.Deserialize<string[]>(evaluation.ImprovementsJson, Array.Empty<string>()),
            suggestedPractice = JsonSupport.Deserialize<string[]>(evaluation.SuggestedPracticeJson, Array.Empty<string>()),
            appliedRuleIds = JsonSupport.Deserialize<string[]>(evaluation.AppliedRuleIdsJson, Array.Empty<string>()),
            rulebookVersion = evaluation.RulebookVersion,
            advisory = evaluation.Advisory,
            turnCount = session.TurnCount,
            durationSeconds = session.DurationSeconds,
            evaluatedAt = evaluation.CreatedAt,
            turns = turns.Select(t => new
            {
                turnNumber = t.TurnNumber, role = t.Role, content = t.Content,
                audioUrl = t.AudioUrl, durationMs = t.DurationMs, confidence = t.ConfidenceScore,
            }),
        };
    }

    public async Task<object> GetHistoryAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 10 : pageSize;

        var query = db.ConversationSessions.Where(s => s.UserId == userId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var sessionIds = items.Select(s => s.Id).ToArray();
        var evalMap = await db.ConversationEvaluations.AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId))
            .ToDictionaryAsync(e => e.SessionId, ct);

        return new
        {
            items = items.Select(s => new
            {
                id = s.Id, taskTypeCode = s.TaskTypeCode, examTypeCode = s.ExamTypeCode,
                profession = s.Profession, state = s.State, turnCount = s.TurnCount,
                durationSeconds = s.DurationSeconds, createdAt = s.CreatedAt, completedAt = s.CompletedAt,
                scaledScore = evalMap.TryGetValue(s.Id, out var e) ? (int?)e.OverallScaled : null,
                overallGrade = evalMap.TryGetValue(s.Id, out var e2) ? e2.OverallGrade : null,
                passed = evalMap.TryGetValue(s.Id, out var e3) ? (bool?)e3.Passed : null,
            }),
            total, page, pageSize,
        };
    }

    public async Task<object> GetEntitlementAsync(string userId, CancellationToken ct)
    {
        var ent = await entitlement.CheckAsync(userId, ct);
        return new
        {
            allowed = ent.Allowed,
            tier = ent.Tier,
            remaining = ent.Remaining == int.MaxValue ? -1 : ent.Remaining,
            limit = ent.LimitPerWindow == int.MaxValue ? -1 : ent.LimitPerWindow,
            windowDays = ent.WindowDays,
            resetAt = ent.ResetAt,
            reason = ent.Reason,
        };
    }

    public object GetTaskTypeCatalog()
    {
        var labels = new Dictionary<string, (string Label, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["oet-roleplay"] = ("OET Clinical Role Play", "Practise 5-minute patient role plays used in the OET Speaking sub-test."),
            ["oet-handover"] = ("OET Handover", "Practise structured ISBAR handovers between colleagues."),
        };
        return new
        {
            taskTypes = _options.EnabledTaskTypes.Select(code => new
            {
                code,
                label = labels.TryGetValue(code, out var v) ? v.Label : code,
                description = labels.TryGetValue(code, out var v2) ? v2.Description : "",
            }),
            prepDurationSeconds = _options.PrepDurationSeconds,
            maxSessionDurationSeconds = _options.MaxSessionDurationSeconds,
            maxTurnDurationSeconds = _options.MaxTurnDurationSeconds,
        };
    }

    private static string NormaliseTaskType(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "" => "oet-roleplay",
            "ielts-part2" or "ielts-part1" => "oet-roleplay",
            _ => value,
        };
    }

    private async Task<ConversationTemplate?> PickTemplateAsync(
        string userId, string taskType, string profession, CancellationToken ct)
    {
        var filtered = await db.ConversationTemplates
            .Where(t => t.Status == "published" && t.TaskTypeCode == taskType
                && (t.ProfessionId == profession || t.ProfessionId == null))
            .ToListAsync(ct);
        if (filtered.Count == 0) return null;

        var ranked = filtered
            .OrderByDescending(t => t.ProfessionId == profession)
            .ToList();

        var recent = await db.ConversationSessions
            .Where(s => s.UserId == userId && s.TemplateId != null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.TemplateId!).Take(5).ToListAsync(ct);
        var unseen = ranked.Where(t => !recent.Contains(t.Id)).ToList();
        if (unseen.Count > 0) ranked = unseen;

        var rng = new Random((userId + taskType + DateTime.UtcNow.DayOfYear).GetHashCode());
        return ranked[rng.Next(ranked.Count)];
    }

    private static string BuildScenarioJsonFromTemplate(ConversationTemplate t)
    {
        return JsonSupport.Serialize(new
        {
            templateId = t.Id,
            title = t.Title,
            taskTypeCode = t.TaskTypeCode,
            profession = t.ProfessionId,
            difficulty = t.Difficulty,
            setting = t.PatientContext,
            patientRole = t.RoleDescription,
            clinicianRole = t.RoleDescription,
            context = t.Scenario,
            expectedOutcomes = t.ExpectedOutcomes,
            objectives = JsonSupport.Deserialize<string[]>(t.ObjectivesJson, Array.Empty<string>()),
            expectedRedFlags = JsonSupport.Deserialize<string[]>(t.ExpectedRedFlagsJson, Array.Empty<string>()),
            keyVocabulary = JsonSupport.Deserialize<string[]>(t.KeyVocabularyJson, Array.Empty<string>()),
            patientVoice = JsonSupport.Deserialize<Dictionary<string, object?>>(t.PatientVoiceJson, new Dictionary<string, object?>()),
            timeLimitSeconds = t.EstimatedDurationSeconds,
        });
    }

    private string BuildFallbackScenarioJson(string taskType, string profession)
    {
        var title = taskType == "oet-handover" ? "Shift Handover" : "Clinical Role Play";
        return JsonSupport.Serialize(new
        {
            title, taskTypeCode = taskType, profession, difficulty = "medium",
            setting = "Clinical setting",
            patientRole = "A patient presenting with a common clinical complaint.",
            clinicianRole = "You are the clinician seeing the patient.",
            context = "No CMS template is currently published for this combination. Practise with a generic brief.",
            objectives = new[]
            {
                "Greet the patient professionally.",
                "Elicit history using open questions.",
                "Explain the plan in plain English.",
                "Offer safety-netting and close the encounter.",
            },
            timeLimitSeconds = _options.MaxSessionDurationSeconds,
        });
    }

    private static object MapSession(ConversationSession s) => new
    {
        id = s.Id, userId = s.UserId, contentId = s.ContentId, templateId = s.TemplateId,
        examTypeCode = s.ExamTypeCode, subtestCode = s.SubtestCode, taskTypeCode = s.TaskTypeCode,
        profession = s.Profession, scenarioJson = s.ScenarioJson, state = s.State,
        turnCount = s.TurnCount, durationSeconds = s.DurationSeconds,
        transcriptJson = s.TranscriptJson, evaluationId = s.EvaluationId,
        createdAt = s.CreatedAt, startedAt = s.StartedAt, completedAt = s.CompletedAt,
    };
}

public record ConversationCreateSessionRequest(
    string? ContentId, string? ExamFamilyCode, string? TaskTypeCode,
    string? Profession = null, string? Difficulty = null);
