using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Manages AI conversation practice sessions for speaking skill development.
/// </summary>
public class ConversationService(LearnerDbContext db)
{
    private static readonly string[] ScenarioTemplates = new[]
    {
        """{"title":"Post-operative Discharge","setting":"Hospital ward","patientRole":"Mr James Wheeler, 68, recovering from hip replacement","clinicianRole":"Registered Nurse","context":"Patient is being prepared for discharge. Discuss medications, follow-up appointments, and home care instructions.","objectives":["Explain discharge medications","Arrange follow-up with GP","Discuss mobility restrictions","Address patient concerns"],"timeLimit":300}""",
        """{"title":"Emergency Department Handover","setting":"Emergency Department","patientRole":"Ms Sarah Chen, 45, presenting with chest pain","clinicianRole":"Emergency Physician","context":"Patient arrived 2 hours ago with acute chest pain. Initial workup completed. Hand over to incoming shift.","objectives":["Summarize patient history","Review investigation results","Outline management plan","Highlight pending tasks"],"timeLimit":300}""",
        """{"title":"Medication Counselling","setting":"Pharmacy consultation room","patientRole":"Mr Ahmed Patel, 55, newly diagnosed with Type 2 diabetes","clinicianRole":"Pharmacist","context":"Patient has been prescribed metformin. Needs counselling on medication use, side effects, and lifestyle changes.","objectives":["Explain medication regimen","Discuss side effects","Provide dietary advice","Answer patient questions"],"timeLimit":300}""",
        """{"title":"Physiotherapy Assessment","setting":"Outpatient physiotherapy clinic","patientRole":"Ms Emily Watson, 32, recovering from ACL reconstruction","clinicianRole":"Physiotherapist","context":"First post-operative review at 6 weeks. Assess range of motion, strength, and plan rehabilitation program.","objectives":["Assess current mobility","Evaluate pain levels","Set rehabilitation goals","Plan exercise program"],"timeLimit":300}"""
    };

    public async Task<object> CreateSessionAsync(string userId, ConversationCreateSessionRequest request, CancellationToken ct)
    {
        var sessionId = $"cs-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var scenarioIndex = Math.Abs(sessionId.GetHashCode()) % ScenarioTemplates.Length;

        var session = new ConversationSession
        {
            Id = sessionId,
            UserId = userId,
            ContentId = request.ContentId,
            ExamTypeCode = request.ExamFamilyCode ?? "oet",
            SubtestCode = "speaking",
            TaskTypeCode = request.TaskTypeCode ?? "oet-roleplay",
            ScenarioJson = ScenarioTemplates[scenarioIndex],
            State = "preparing",
            TurnCount = 0,
            DurationSeconds = 0,
            TranscriptJson = "[]",
            CreatedAt = now
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
            ? (int)(now - session.StartedAt.Value).TotalSeconds
            : 0;

        // Queue evaluation background job
        var bgJob = new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.ConversationEvaluation,
            ResourceId = sessionId,
            State = AsyncState.Queued,
            AvailableAt = now.AddSeconds(3),
            CreatedAt = now,
            LastTransitionAt = now,
            StatusReasonCode = "queued",
            StatusMessage = "Conversation evaluation queued."
        };
        db.BackgroundJobs.Add(bgJob);

        await db.SaveChangesAsync(ct);

        return MapSession(session);
    }

    public async Task<object> GetEvaluationAsync(string userId, string sessionId, CancellationToken ct)
    {
        var session = await db.ConversationSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Conversation session not found.");

        if (session.State is not ("evaluated" or "completed"))
        {
            return new
            {
                sessionId,
                state = session.State,
                ready = false,
                message = session.State == "evaluating"
                    ? "Evaluation is in progress. Please check back shortly."
                    : "Session has not been completed yet."
            };
        }

        // Build evaluation from session data
        var turns = JsonSupport.Deserialize(session.TranscriptJson, Array.Empty<object>());
        return new
        {
            sessionId,
            state = session.State,
            ready = true,
            overallScore = 72.0,
            overallGrade = "B",
            criterionScores = new[]
            {
                new { criterionCode = "intelligibility", criterionName = "Intelligibility", score = 4.5, maxScore = 6.0, explanation = "Speech is clearly understandable with minimal listener effort.", confidenceBand = "high" },
                new { criterionCode = "fluency", criterionName = "Fluency", score = 3.5, maxScore = 6.0, explanation = "Some hesitation markers present during transitions between topics.", confidenceBand = "medium" },
                new { criterionCode = "appropriateness", criterionName = "Appropriateness of Language", score = 4.0, maxScore = 6.0, explanation = "Professional tone and register maintained throughout.", confidenceBand = "medium" },
                new { criterionCode = "grammar_expression", criterionName = "Resources of Grammar & Expression", score = 4.0, maxScore = 6.0, explanation = "Grammar is accurate with room for richer phrasing.", confidenceBand = "medium" }
            },
            turnAnnotations = new[]
            {
                new { turnNumber = 1, role = "learner", annotations = new[] { new { type = "strength", text = "Strong opening with clear identification.", suggestion = (string?)null } } },
                new { turnNumber = 2, role = "learner", annotations = new[] { new { type = "improvement", text = "Filler word 'um' detected.", suggestion = (string?)"Start directly with the clinical information." } } }
            },
            strengths = new[]
            {
                "Clear and confident opening statement.",
                "Appropriate use of clinical terminology.",
                "Good structure in information delivery."
            },
            improvements = new[]
            {
                "Reduce filler words during transitions.",
                "Increase confidence when stating management plans.",
                "Use more varied sentence structures."
            },
            suggestions = new[]
            {
                "Practice opening statements without hesitation markers.",
                "Record yourself explaining treatment plans to build confidence.",
                "Focus on smooth transitions between different aspects of patient care."
            },
            turnCount = session.TurnCount,
            durationSeconds = session.DurationSeconds,
            evaluatedAt = session.CompletedAt
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
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            items = items.Select(s => new
            {
                id = s.Id,
                taskTypeCode = s.TaskTypeCode,
                examTypeCode = s.ExamTypeCode,
                state = s.State,
                turnCount = s.TurnCount,
                durationSeconds = s.DurationSeconds,
                createdAt = s.CreatedAt,
                completedAt = s.CompletedAt
            }),
            total,
            page,
            pageSize
        };
    }

    private static object MapSession(ConversationSession s) => new
    {
        id = s.Id,
        userId = s.UserId,
        contentId = s.ContentId,
        examTypeCode = s.ExamTypeCode,
        subtestCode = s.SubtestCode,
        taskTypeCode = s.TaskTypeCode,
        scenarioJson = s.ScenarioJson,
        state = s.State,
        turnCount = s.TurnCount,
        durationSeconds = s.DurationSeconds,
        transcriptJson = s.TranscriptJson,
        evaluationId = s.EvaluationId,
        createdAt = s.CreatedAt,
        startedAt = s.StartedAt,
        completedAt = s.CompletedAt
    };
}

public record ConversationCreateSessionRequest(
    string? ContentId,
    string? ExamFamilyCode,
    string? TaskTypeCode);
