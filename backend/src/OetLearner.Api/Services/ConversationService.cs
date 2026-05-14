using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

public class ConversationService(
    LearnerDbContext db,
    IOptions<ConversationOptions> conversationOptions,
    Conversation.IConversationOptionsProvider conversationOptionsProvider,
    Conversation.IConversationEntitlementService entitlement)
{
    private readonly ConversationOptions _options = conversationOptions.Value;

    public async Task<object> CreateSessionAsync(string userId, ConversationCreateSessionRequest request, CancellationToken ct)
    {
        var options = await conversationOptionsProvider.GetAsync(ct);
        if (!options.Enabled)
            throw ApiException.Validation("CONVERSATION_DISABLED", "AI Conversation is currently disabled.");

        var taskType = NormaliseTaskType(request.TaskTypeCode);
        if (!options.EnabledTaskTypes.Contains(taskType, StringComparer.OrdinalIgnoreCase))
            throw ApiException.Validation("TASK_TYPE_NOT_ENABLED",
                $"Task type '{taskType}' is not enabled.");

        var ent = await entitlement.CheckAsync(userId, ct);
        if (!ent.Allowed)
            throw ApiException.Validation("ENTITLEMENT_BLOCKED", ent.Reason);

        var profession = (request.Profession ?? "medicine").Trim().ToLowerInvariant();
        var difficulty = (request.Difficulty ?? "medium").Trim().ToLowerInvariant();
        var sourceContent = await ResolvePublishedSpeakingContentAsync(request.ContentId, ct);

        var template = sourceContent is null
            ? await PickTemplateAsync(userId, taskType, profession, ct)
            : null;
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
            ScenarioJson = sourceContent is not null
                ? BuildScenarioJsonFromSpeakingContent(sourceContent, taskType, profession, difficulty)
                : template is not null
                ? BuildScenarioJsonFromTemplate(template)
                : BuildFallbackScenarioJson(taskType, profession, options),
            State = "preparing",
            TurnCount = 0,
            DurationSeconds = 0,
            TranscriptJson = "[]",
            CreatedAt = now,
        };
        db.ConversationSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return MapSession(session, null, options);
    }

    private async Task<ContentItem?> ResolvePublishedSpeakingContentAsync(string? contentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return null;
        }

        var content = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentId.Trim(), ct)
            ?? throw ApiException.NotFound("CONVERSATION_CONTENT_NOT_FOUND", "Conversation source content was not found.");
        if (!string.Equals(content.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase)
            || content.Status != ContentStatus.Published)
        {
            throw ApiException.Conflict("CONVERSATION_CONTENT_NOT_AVAILABLE", "Conversation source content is not available for speaking practice.");
        }

        return content;
    }

    public async Task<object> GetSessionAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");
        var turns = await GetTurnsAsync(sessionId, ct);
        var options = await conversationOptionsProvider.GetAsync(ct);
        return MapSession(session, turns, options);
    }

    public async Task<object> ResumeSessionAsync(
        string userId,
        string sessionId,
        ConversationResumeSessionRequest request,
        CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");

        var turns = await GetTurnsAsync(sessionId, ct);
        var options = await conversationOptionsProvider.GetAsync(ct);
        if (session.State is "completed" or "evaluated" or "evaluating" or "failed" or "abandoned")
        {
            return new
            {
                resumeAllowed = false,
                redirectTo = $"/conversation/{sessionId}/results",
                session = MapSession(session, turns, options),
                turns = MapTurns(turns),
            };
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.ResumeToken))
        {
            var suppliedHash = HashToken(request.ResumeToken);
            var existing = await db.ConversationSessionResumeTokens
                .FirstOrDefaultAsync(t =>
                    t.SessionId == sessionId &&
                    t.UserId == userId &&
                    t.TokenHash == suppliedHash,
                    ct);
            if (existing is null || existing.ExpiresAt <= now || existing.RevokedAt is not null)
                throw ApiException.Validation("RESUME_TOKEN_INVALID", "The conversation resume token is invalid or expired.");
            existing.LastUsedAt = now;
        }

        var token = CreateResumeToken();
        var expiresAt = now.AddMinutes(30);
        db.ConversationSessionResumeTokens.Add(new ConversationSessionResumeToken
        {
            Id = $"crt-{Guid.NewGuid():N}",
            SessionId = sessionId,
            UserId = userId,
            TokenHash = HashToken(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });

        await db.SaveChangesAsync(ct);
        return new
        {
            resumeAllowed = true,
            resumeToken = token,
            resumeTokenExpiresAt = expiresAt,
            session = MapSession(session, turns, options),
            turns = MapTurns(turns),
        };
    }

    public async Task<Conversation.ConversationTranscriptExportResult> ExportTranscriptAsync(
        string userId,
        string sessionId,
        string format,
        Conversation.IConversationTranscriptExportService exporter,
        CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");

        var turns = await GetTurnsAsync(sessionId, ct);
        var evaluation = await db.ConversationEvaluations.AsNoTracking()
            .FirstOrDefaultAsync(e => e.SessionId == sessionId && e.UserId == userId, ct);
        return await exporter.ExportAsync(session, turns, evaluation, format, ct);
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
        var options = await conversationOptionsProvider.GetAsync(ct);
        return MapSession(session, null, options);
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

    public async Task<object> GetTaskTypeCatalogAsync(CancellationToken ct)
    {
        var options = await conversationOptionsProvider.GetAsync(ct);
        var labels = new Dictionary<string, (string Label, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["oet-roleplay"] = ("OET Clinical Role Play", "Practise 5-minute patient role plays used in the OET Speaking sub-test."),
            ["oet-handover"] = ("OET Handover", "Practise structured ISBAR handovers between colleagues."),
        };
        return new
        {
            taskTypes = options.EnabledTaskTypes.Select(code => new
            {
                code,
                label = labels.TryGetValue(code, out var v) ? v.Label : code,
                description = labels.TryGetValue(code, out var v2) ? v2.Description : "",
            }),
            prepDurationSeconds = options.PrepDurationSeconds,
            maxSessionDurationSeconds = options.MaxSessionDurationSeconds,
            maxTurnDurationSeconds = options.MaxTurnDurationSeconds,
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

    private static string BuildScenarioJsonFromSpeakingContent(
        ContentItem content,
        string taskType,
        string profession,
        string difficulty)
    {
        var detail = SpeakingContentStructure.ExtractStructure(content.DetailJson);
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "candidateCard"));
        var interlocutor = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "interlocutorCard"));
        var objectives = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "roleObjectives")));
        var cuePrompts = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(interlocutor, "cuePrompts")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(interlocutor, "prompts")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(interlocutor, "objectives")));
        var patientVoice = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "patientVoice"));
        if (patientVoice.Count == 0)
        {
            patientVoice["emotion"] = SpeakingContentStructure.ReadString(detail, "patientEmotion") ?? "neutral";
            patientVoice["style"] = "Respond as the patient or carer on the hidden interlocutor card. Do not reveal examiner notes unless the candidate elicits them naturally.";
        }

        return JsonSupport.Serialize(new
        {
            contentId = content.Id,
            title = content.Title,
            taskTypeCode = taskType,
            profession,
            difficulty,
            setting = SpeakingContentStructure.ReadString(candidate, "setting")
                      ?? SpeakingContentStructure.ReadString(detail, "setting")
                      ?? "Clinical setting",
            patientRole = SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                          ?? SpeakingContentStructure.ReadString(detail, "patientRole", "patient")
                          ?? "Patient",
            clinicianRole = SpeakingContentStructure.ReadString(candidate, "candidateRole", "role")
                            ?? SpeakingContentStructure.ReadString(detail, "candidateRole", "role")
                            ?? "Candidate",
            context = SpeakingContentStructure.ReadString(candidate, "background")
                      ?? SpeakingContentStructure.ReadString(detail, "background", "caseNotes")
                      ?? content.CaseNotes
                      ?? string.Empty,
            candidateBrief = SpeakingContentStructure.ReadString(candidate, "task", "brief")
                             ?? SpeakingContentStructure.ReadString(detail, "task", "brief")
                             ?? "Complete the role play using patient-centred communication.",
            hiddenPatientProfile = SpeakingContentStructure.ReadString(interlocutor, "patientProfile", "background", "hiddenInformation"),
            cuePrompts,
            objectives,
            expectedOutcomes = SpeakingContentStructure.ReadString(detail, "communicationGoal", "purpose"),
            keyVocabulary = SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "keyVocabulary")),
            patientVoice,
            timeLimitSeconds = SpeakingContentStructure.ReadInt(detail, "roleplayTimeSeconds")
                               ?? SpeakingContentStructure.DefaultRoleplayTimeSeconds,
        });
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private static string BuildFallbackScenarioJson(string taskType, string profession, ConversationOptions options)
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
            timeLimitSeconds = options.MaxSessionDurationSeconds,
        });
    }

    private async Task<List<ConversationTurn>> GetTurnsAsync(string sessionId, CancellationToken ct)
        => await db.ConversationTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TurnNumber)
            .ToListAsync(ct);

    private static object MapSession(ConversationSession s, IEnumerable<ConversationTurn>? turns, ConversationOptions options) => new
    {
        id = s.Id, userId = s.UserId, contentId = s.ContentId, templateId = s.TemplateId,
        examTypeCode = s.ExamTypeCode, subtestCode = s.SubtestCode, taskTypeCode = s.TaskTypeCode,
        profession = s.Profession, scenarioJson = s.ScenarioJson, state = s.State,
        turnCount = s.TurnCount, durationSeconds = s.DurationSeconds,
        transcriptJson = s.TranscriptJson, evaluationId = s.EvaluationId,
        audioConsentVersion = s.AudioConsentVersion,
        recordingConsentAcceptedAt = s.RecordingConsentAcceptedAt,
        vendorConsentAcceptedAt = s.VendorConsentAcceptedAt,
        requiredAudioConsentVersion = options.RealtimeSttConsentVersion,
        audioRetentionDays = options.AudioRetentionDays,
        realtimeSttEnabled = options.RealtimeSttEnabled,
        realtimeAsrProvider = options.RealtimeAsrProvider,
        realtimeSttFallbackToBatch = options.RealtimeSttFallbackToBatch,
        createdAt = s.CreatedAt, startedAt = s.StartedAt, completedAt = s.CompletedAt,
        turns = turns is null ? Array.Empty<object>() : MapTurns(turns),
    };

    private static IEnumerable<object> MapTurns(IEnumerable<ConversationTurn> turns)
        => turns.Select(t => new
        {
            turnNumber = t.TurnNumber,
            role = t.Role,
            content = t.Content,
            audioUrl = t.AudioUrl,
            durationMs = t.DurationMs,
            timestampMs = t.TimestampMs,
            confidence = t.ConfidenceScore,
            analysis = JsonSupport.Deserialize<object>(t.AnalysisJson, new { }),
            createdAt = t.CreatedAt,
        });

    private static string CreateResumeToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public record ConversationCreateSessionRequest(
    string? ContentId, string? ExamFamilyCode, string? TaskTypeCode,
    string? Profession = null, string? Difficulty = null);

public record ConversationResumeSessionRequest(string? ResumeToken = null);
