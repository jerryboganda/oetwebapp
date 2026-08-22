using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class AnswerKeyReportService(LearnerDbContext db)
{
    public const string ScoringSystemUrl = "/admin/content/scoring-system";

    private static readonly HashSet<string> Assessments = new(StringComparer.OrdinalIgnoreCase)
    {
        AnswerKeyReportAssessments.Reading,
        AnswerKeyReportAssessments.Listening,
    };

    private static readonly HashSet<string> ReasonCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        AnswerKeyReportReasonCodes.WrongOfficialAnswer,
        AnswerKeyReportReasonCodes.MissingAcceptedVariant,
        AnswerKeyReportReasonCodes.Other,
    };

    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        AnswerKeyReportStatuses.Open,
        AnswerKeyReportStatuses.Investigating,
        AnswerKeyReportStatuses.Resolved,
        AnswerKeyReportStatuses.Dismissed,
    };

    private static readonly HashSet<string> PendingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        AnswerKeyReportStatuses.Open,
        AnswerKeyReportStatuses.Investigating,
    };

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        AnswerKeyReportStatuses.Resolved,
        AnswerKeyReportStatuses.Dismissed,
    };

    public Task<AnswerKeyReportLearnerDto> CreateReadingAsync(
        string userId,
        string attemptId,
        AnswerKeyReportCreateRequest request,
        CancellationToken ct)
        => CreateAsync(userId, AnswerKeyReportAssessments.Reading, attemptId, request, ct);

    public Task<AnswerKeyReportLearnerDto> CreateListeningAsync(
        string userId,
        string attemptId,
        AnswerKeyReportCreateRequest request,
        CancellationToken ct)
        => CreateAsync(userId, AnswerKeyReportAssessments.Listening, attemptId, request, ct);

    public Task<IReadOnlyList<AnswerKeyReportLearnerDto>> ListReadingForAttemptAsync(
        string userId,
        string attemptId,
        CancellationToken ct)
        => ListForAttemptAsync(userId, AnswerKeyReportAssessments.Reading, attemptId, ct);

    public Task<IReadOnlyList<AnswerKeyReportLearnerDto>> ListListeningForAttemptAsync(
        string userId,
        string attemptId,
        CancellationToken ct)
        => ListForAttemptAsync(userId, AnswerKeyReportAssessments.Listening, attemptId, ct);

    public async Task<IReadOnlyList<AnswerKeyReportAdminDto>> ListAdminAsync(
        string? status,
        string? assessment,
        int limit,
        CancellationToken ct)
    {
        var clamped = limit <= 0 ? 50 : Math.Min(limit, 200);
        var query = db.AssessmentAnswerKeyReports.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalised = status.Trim().ToLowerInvariant();
            if (!Statuses.Contains(normalised))
            {
                throw ApiException.Validation(
                    "answer_key_report_status_invalid",
                    "Status must be one of open, investigating, resolved, dismissed.");
            }

            query = query.Where(x => x.Status == normalised);
        }

        if (!string.IsNullOrWhiteSpace(assessment))
        {
            var normalised = assessment.Trim().ToLowerInvariant();
            if (!Assessments.Contains(normalised))
            {
                throw ApiException.Validation(
                    "answer_key_report_assessment_invalid",
                    "Assessment must be reading or listening.");
            }

            query = query.Where(x => x.Assessment == normalised);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(clamped)
            .ToListAsync(ct);

        var names = await LoadDisplayNamesAsync(rows.Select(x => x.ReporterUserId), ct);
        return rows.Select(row => ToAdminDto(row, names)).ToArray();
    }

    public async Task<AnswerKeyReportAdminDto> GetAdminAsync(string id, CancellationToken ct)
    {
        var report = await db.AssessmentAnswerKeyReports.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("answer_key_report_not_found", "Answer-key report not found.");

        var names = await LoadDisplayNamesAsync([report.ReporterUserId], ct);
        return ToAdminDto(report, names);
    }

    public async Task<AnswerKeyReportAdminDto> UpdateAdminAsync(
        string adminId,
        string id,
        AnswerKeyReportUpdateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status)
            || !Statuses.Contains(request.Status.Trim().ToLowerInvariant()))
        {
            throw ApiException.Validation(
                "answer_key_report_status_invalid",
                "Status must be one of open, investigating, resolved, dismissed.");
        }

        var nextStatus = request.Status.Trim().ToLowerInvariant();
        var report = await db.AssessmentAnswerKeyReports
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw ApiException.NotFound("answer_key_report_not_found", "Answer-key report not found.");

        if (TerminalStatuses.Contains(report.Status)
            && !string.Equals(report.Status, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "answer_key_report_status_locked",
                $"Report is already {report.Status} and cannot transition to {nextStatus}.");
        }

        var now = DateTimeOffset.UtcNow;
        var previousStatus = report.Status;
        var note = NormalizeOptional(request.ResolutionNote, "resolutionNote", 2000);

        report.Status = nextStatus;
        if (note is not null)
        {
            report.ResolutionNote = note;
        }

        if (TerminalStatuses.Contains(nextStatus))
        {
            report.ResolvedAt = now;
            report.ResolvedByAdminId = adminId;
        }
        else
        {
            report.ResolvedAt = null;
            report.ResolvedByAdminId = null;
        }

        report.UpdatedAt = now;
        AddAudit(adminId, adminId, "AnswerKeyReport.Updated", report, new
        {
            previousStatus,
            nextStatus,
            hasResolutionNote = note is not null,
        });
        await db.SaveChangesAsync(ct);

        var names = await LoadDisplayNamesAsync([report.ReporterUserId], ct);
        return ToAdminDto(report, names);
    }

    private async Task<AnswerKeyReportLearnerDto> CreateAsync(
        string userId,
        string assessment,
        string attemptId,
        AnswerKeyReportCreateRequest request,
        CancellationToken ct)
    {
        var questionId = NormalizeRequired(request.QuestionId, "questionId", 64);
        var reasonCode = NormalizeReason(request.ReasonCode);
        var details = NormalizeOptional(request.Details, "details", 2000);
        var context = await LoadQuestionContextAsync(userId, assessment, attemptId, questionId, ct);

        var pendingExists = await db.AssessmentAnswerKeyReports.AnyAsync(
            x => x.ReporterUserId == userId
                && x.Assessment == assessment
                && x.QuestionId == questionId
                && x.AttemptId == attemptId
                && PendingStatuses.Contains(x.Status),
            ct);
        if (pendingExists)
        {
            throw ApiException.Conflict(
                "answer_key_report_already_open",
                "You already have an open report for this question.");
        }

        var now = DateTimeOffset.UtcNow;
        var report = new AssessmentAnswerKeyReport
        {
            Id = $"akr-{Guid.NewGuid():N}",
            Assessment = assessment,
            AttemptId = attemptId,
            PaperId = context.PaperId,
            QuestionId = questionId,
            QuestionNumber = context.QuestionNumber,
            PartCode = context.PartCode,
            QuestionStemSnapshot = Truncate(context.Stem, 512),
            PaperTitleSnapshot = Truncate(context.PaperTitle, 200),
            ReporterUserId = userId,
            LearnerAnswerSnapshot = Truncate(context.LearnerAnswer, 512),
            OfficialAnswerSnapshot = Truncate(context.OfficialAnswer, 512),
            ReasonCode = reasonCode,
            Details = details,
            Status = AnswerKeyReportStatuses.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AssessmentAnswerKeyReports.Add(report);
        AddAudit(userId, userId, "AnswerKeyReport.Created", report, new
        {
            assessment,
            attemptId,
            questionId,
            reasonCode,
        });
        await db.SaveChangesAsync(ct);
        return ToLearnerDto(report);
    }

    private async Task<IReadOnlyList<AnswerKeyReportLearnerDto>> ListForAttemptAsync(
        string userId,
        string assessment,
        string attemptId,
        CancellationToken ct)
    {
        await EnsureOwnSubmittedAttemptAsync(userId, assessment, attemptId, ct);
        var rows = await db.AssessmentAnswerKeyReports.AsNoTracking()
            .Where(x => x.ReporterUserId == userId
                && x.Assessment == assessment
                && x.AttemptId == attemptId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToLearnerDto).ToArray();
    }

    private async Task EnsureOwnSubmittedAttemptAsync(
        string userId,
        string assessment,
        string attemptId,
        CancellationToken ct)
    {
        if (assessment == AnswerKeyReportAssessments.Reading)
        {
            var attempt = await db.ReadingAttempts.AsNoTracking()
                .Select(x => new { x.Id, x.UserId, x.Status })
                .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);
            if (attempt is null)
            {
                throw ApiException.NotFound("answer_key_report_not_found", "Attempt not found.");
            }

            if (attempt.Status != ReadingAttemptStatus.Submitted)
            {
                throw Unavailable();
            }

            return;
        }

        var listening = await db.ListeningAttempts.AsNoTracking()
            .Select(x => new { x.Id, x.UserId, x.Status })
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct);
        if (listening is null)
        {
            throw ApiException.NotFound("answer_key_report_not_found", "Attempt not found.");
        }

        if (listening.Status != ListeningAttemptStatus.Submitted)
        {
            throw Unavailable();
        }
    }

    private async Task<QuestionContext> LoadQuestionContextAsync(
        string userId,
        string assessment,
        string attemptId,
        string questionId,
        CancellationToken ct)
    {
        if (assessment == AnswerKeyReportAssessments.Reading)
        {
            var attempt = await db.ReadingAttempts.AsNoTracking()
                .Select(x => new { x.Id, x.UserId, x.PaperId, x.Status })
                .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct)
                ?? throw ApiException.NotFound("answer_key_report_not_found", "Attempt not found.");
            if (attempt.Status != ReadingAttemptStatus.Submitted)
            {
                throw Unavailable();
            }

            var question = await (
                from q in db.ReadingQuestions.AsNoTracking()
                join part in db.ReadingParts.AsNoTracking() on q.ReadingPartId equals part.Id
                where q.Id == questionId && part.PaperId == attempt.PaperId
                select new
                {
                    q.Stem,
                    q.DisplayOrder,
                    q.CorrectAnswerJson,
                    PartCode = part.PartCode.ToString(),
                }).FirstOrDefaultAsync(ct)
                ?? throw ApiException.NotFound("answer_key_report_not_found", "Question not found.");

            var learnerAnswer = await db.ReadingAnswers.AsNoTracking()
                .Where(x => x.ReadingAttemptId == attemptId && x.ReadingQuestionId == questionId)
                .Select(x => x.UserAnswerJson)
                .FirstOrDefaultAsync(ct);
            var paperTitle = await db.ContentPapers.AsNoTracking()
                .Where(x => x.Id == attempt.PaperId)
                .Select(x => x.Title)
                .FirstOrDefaultAsync(ct);

            return new QuestionContext(
                attempt.PaperId,
                paperTitle ?? string.Empty,
                question.DisplayOrder,
                question.PartCode,
                question.Stem,
                learnerAnswer ?? string.Empty,
                question.CorrectAnswerJson);
        }

        var listeningAttempt = await db.ListeningAttempts.AsNoTracking()
            .Select(x => new { x.Id, x.UserId, x.PaperId, x.Status })
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("answer_key_report_not_found", "Attempt not found.");
        if (listeningAttempt.Status != ListeningAttemptStatus.Submitted)
        {
            throw Unavailable();
        }

        var listeningQuestion = await (
            from q in db.ListeningQuestions.AsNoTracking()
            join part in db.ListeningParts.AsNoTracking() on q.ListeningPartId equals part.Id
            where q.Id == questionId && q.PaperId == listeningAttempt.PaperId
            select new
            {
                q.Stem,
                q.QuestionNumber,
                q.CorrectAnswerJson,
                PartCode = part.PartCode.ToString(),
            }).FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("answer_key_report_not_found", "Question not found.");

        var listeningAnswer = await db.ListeningAnswers.AsNoTracking()
            .Where(x => x.ListeningAttemptId == attemptId && x.ListeningQuestionId == questionId)
            .Select(x => x.UserAnswerJson)
            .FirstOrDefaultAsync(ct);
        var listeningTitle = await db.ContentPapers.AsNoTracking()
            .Where(x => x.Id == listeningAttempt.PaperId)
            .Select(x => x.Title)
            .FirstOrDefaultAsync(ct);

        return new QuestionContext(
            listeningAttempt.PaperId,
            listeningTitle ?? string.Empty,
            listeningQuestion.QuestionNumber,
            listeningQuestion.PartCode,
            listeningQuestion.Stem,
            listeningAnswer ?? string.Empty,
            listeningQuestion.CorrectAnswerJson);
    }

    private async Task<Dictionary<string, string>> LoadDisplayNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken ct)
    {
        var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, string>(0);
        }

        return await db.Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    }

    private void AddAudit(
        string actorId,
        string actorName,
        string action,
        AssessmentAnswerKeyReport report,
        object details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = "AnswerKeyReport",
            ResourceId = report.Id,
            Details = JsonSupport.Serialize(details),
        });
    }

    private static AnswerKeyReportLearnerDto ToLearnerDto(AssessmentAnswerKeyReport report)
        => new(
            report.Id,
            report.Assessment,
            report.AttemptId,
            report.PaperId,
            report.QuestionId,
            report.QuestionNumber,
            report.PartCode,
            report.ReasonCode,
            report.Details,
            report.Status,
            report.CreatedAt);

    private static AnswerKeyReportAdminDto ToAdminDto(
        AssessmentAnswerKeyReport report,
        IReadOnlyDictionary<string, string> displayNames)
    {
        displayNames.TryGetValue(report.ReporterUserId, out var displayName);
        return new AnswerKeyReportAdminDto(
            report.Id,
            report.Assessment,
            report.AttemptId,
            report.PaperId,
            report.PaperTitleSnapshot,
            report.QuestionId,
            report.QuestionNumber,
            report.PartCode,
            report.QuestionStemSnapshot,
            report.LearnerAnswerSnapshot,
            report.OfficialAnswerSnapshot,
            report.ReasonCode,
            report.Details,
            report.Status,
            report.ResolutionNote,
            report.ReporterUserId,
            string.IsNullOrWhiteSpace(displayName) ? report.ReporterUserId : displayName,
            BuildEditorUrl(report.Assessment, report.PaperId, report.QuestionId, report.PartCode),
            ScoringSystemUrl,
            report.ResolvedByAdminId,
            report.ResolvedAt,
            report.CreatedAt,
            report.UpdatedAt);
    }

    internal static string BuildEditorUrl(string assessment, string paperId, string questionId, string partCode)
    {
        if (string.Equals(assessment, AnswerKeyReportAssessments.Listening, StringComparison.OrdinalIgnoreCase))
        {
            if (partCode.StartsWith("A", StringComparison.OrdinalIgnoreCase))
            {
                return $"/admin/content/listening/{paperId}/part-a";
            }

            return $"/admin/content/listening/{paperId}/questions/{questionId}";
        }

        return $"/admin/content/reading/{paperId}/questions";
    }

    private static string NormalizeReason(string? reasonCode)
    {
        var value = NormalizeRequired(reasonCode, "reasonCode", 64);
        if (!ReasonCodes.Contains(value))
        {
            throw ApiException.Validation(
                "answer_key_report_reason_invalid",
                "Choose why this official answer looks wrong.",
                [new ApiFieldError("reasonCode", "invalid", "Choose a valid reason.")]);
        }

        return value.ToLowerInvariant();
    }

    private static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw ApiException.Validation(
                "answer_key_report_invalid",
                $"{field} is required.",
                [new ApiFieldError(field, "required", $"{field} is required.")]);
        }

        if (trimmed.Length > maxLength)
        {
            throw ApiException.Validation(
                "answer_key_report_invalid",
                $"{field} is too long.",
                [new ApiFieldError(field, "too_long", $"{field} must be {maxLength} characters or fewer.")]);
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw ApiException.Validation(
                "answer_key_report_invalid",
                $"{field} is too long.",
                [new ApiFieldError(field, "too_long", $"{field} must be {maxLength} characters or fewer.")]);
        }

        return trimmed;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static ApiException Unavailable()
        => ApiException.Validation(
            "answer_key_report_unavailable",
            "You can report an official answer after the attempt is submitted.");

    private sealed record QuestionContext(
        string PaperId,
        string PaperTitle,
        int QuestionNumber,
        string PartCode,
        string Stem,
        string LearnerAnswer,
        string OfficialAnswer);
}
