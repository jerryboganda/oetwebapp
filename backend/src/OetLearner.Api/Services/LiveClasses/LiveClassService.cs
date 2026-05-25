using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.LiveClasses;

public sealed class LiveClassService(
    LearnerDbContext db,
    ZoomMeetingService zoomMeetingService,
    NotificationService notificationService,
    TimeProvider timeProvider,
    ILogger<LiveClassService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LiveClassListItemDto>> ListCatalogAsync(
        LiveClassListQuery query,
        string? learnerUserId,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var classesQuery = db.LiveClasses
            .AsNoTracking()
            .Include(liveClass => liveClass.Sessions.Where(session => session.ScheduledEndAt >= now.AddHours(-6)))
            .Where(liveClass => liveClass.Status == LiveClassStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.ProfessionTrack))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.ProfessionTrack == query.ProfessionTrack || liveClass.ProfessionTrack == "All");
        }

        if (!string.IsNullOrWhiteSpace(query.Type) && Enum.TryParse<LiveClassType>(query.Type, true, out var type))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.TutorProfileId))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.TutorProfileId == query.TutorProfileId);
        }

        if (query.From.HasValue)
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.Sessions.Any(session => session.ScheduledStartAt >= query.From.Value));
        }

        if (query.To.HasValue)
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.Sessions.Any(session => session.ScheduledStartAt <= query.To.Value));
        }

        var enrolledSessionIds = await GetActiveEnrollmentSessionIdsAsync(learnerUserId, ct);
        var classes = await classesQuery
            .OrderBy(liveClass => liveClass.Sessions.Min(session => (DateTimeOffset?)session.ScheduledStartAt) ?? DateTimeOffset.MaxValue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return classes.Select(liveClass => MapListItem(liveClass, enrolledSessionIds, now)).ToList();
    }

    public async Task<LiveClassDetailDto> GetClassAsync(string idOrSlug, string? learnerUserId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var liveClass = await db.LiveClasses
            .AsNoTracking()
            .Include(item => item.Sessions)
            .FirstOrDefaultAsync(item => item.Id == idOrSlug || item.Slug == idOrSlug, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");

        if (liveClass.Status != LiveClassStatus.Published && string.IsNullOrWhiteSpace(learnerUserId))
        {
            throw ApiException.NotFound("live_class_not_found", "Live class not found.");
        }

        var enrolledSessionIds = await GetActiveEnrollmentSessionIdsAsync(learnerUserId, ct);
        return MapDetail(liveClass, enrolledSessionIds, now);
    }

    public async Task<IReadOnlyList<LiveClassListItemDto>> ListAdminClassesAsync(LiveClassListQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var now = timeProvider.GetUtcNow();

        var classesQuery = db.LiveClasses
            .AsNoTracking()
            .Include(liveClass => liveClass.Sessions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.ProfessionTrack))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.ProfessionTrack == query.ProfessionTrack);
        }

        if (!string.IsNullOrWhiteSpace(query.Type) && Enum.TryParse<LiveClassType>(query.Type, true, out var type))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.TutorProfileId))
        {
            classesQuery = classesQuery.Where(liveClass => liveClass.TutorProfileId == query.TutorProfileId);
        }

        var classes = await classesQuery
            .OrderByDescending(liveClass => liveClass.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return classes.Select(liveClass => MapListItem(liveClass, [], now)).ToList();
    }

    public async Task<LiveClassDetailDto> CreateAdminClassAsync(AdminLiveClassUpsertRequest request, string adminId, string adminName, CancellationToken ct)
    {
        ValidateAdminRequest(request);

        var now = timeProvider.GetUtcNow();
        var durationMinutes = Math.Clamp(request.DurationMinutes, 15, 360);
        var scheduledEnd = request.ScheduledStartAt.AddMinutes(durationMinutes);
        var classId = $"LC-{Guid.NewGuid():N}";
        var sessionId = $"LCS-{Guid.NewGuid():N}";
        var tutor = string.IsNullOrWhiteSpace(request.TutorProfileId)
            ? null
            : await db.PrivateSpeakingTutorProfiles.AsNoTracking().FirstOrDefaultAsync(profile => profile.Id == request.TutorProfileId, ct)
                ?? throw ApiException.Validation("tutor_not_found", "Selected tutor profile was not found.");

        var liveClass = new LiveClass
        {
            Id = classId,
            Slug = await EnsureUniqueSlugAsync(Slugify(request.Title), ct),
            Title = request.Title.Trim(),
            TitleAr = NormalizeOptional(request.TitleAr),
            Description = request.Description.Trim(),
            DescriptionAr = NormalizeOptional(request.DescriptionAr),
            Type = ParseClassType(request.Type),
            ProfessionTrack = NormalizeOptional(request.ProfessionTrack) ?? "All",
            Level = NormalizeOptional(request.Level) ?? "All",
            TutorProfileId = tutor?.Id,
            TutorDisplayName = tutor?.DisplayName,
            DefaultDurationMinutes = durationMinutes,
            DefaultCapacity = Math.Max(1, request.Capacity),
            CreditCost = Math.Max(0, request.CreditCost),
            Status = request.AutoPublish ? LiveClassStatus.Published : LiveClassStatus.Draft,
            CoverImageUrl = NormalizeOptional(request.CoverImageUrl),
            TagsJson = JsonSerializer.Serialize(request.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [], JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };

        var session = new LiveClassSession
        {
            Id = sessionId,
            LiveClassId = classId,
            ScheduledStartAt = request.ScheduledStartAt,
            ScheduledEndAt = scheduledEnd,
            Capacity = Math.Max(1, request.Capacity),
            Status = LiveClassSessionStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now,
        };

        liveClass.Sessions.Add(session);
        db.LiveClasses.Add(liveClass);
        db.LiveClassSessions.Add(session);
        WriteAudit(adminId, adminName, "LiveClassCreated", "LiveClass", classId, new { liveClass.Title, sessionId });
        await db.SaveChangesAsync(ct);

        await ProvisionZoomMeetingAsync(session.Id, ct);

        var created = await db.LiveClasses.AsNoTracking().Include(item => item.Sessions).FirstAsync(item => item.Id == classId, ct);
        return MapDetail(created, [], now);
    }

    public async Task<LiveClassDetailDto> PublishClassAsync(string liveClassId, string adminId, string adminName, CancellationToken ct)
    {
        var liveClass = await db.LiveClasses.Include(item => item.Sessions).FirstOrDefaultAsync(item => item.Id == liveClassId, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");
        liveClass.Status = LiveClassStatus.Published;
        liveClass.UpdatedAt = timeProvider.GetUtcNow();
        WriteAudit(adminId, adminName, "LiveClassPublished", "LiveClass", liveClass.Id, new { liveClass.Title });
        await db.SaveChangesAsync(ct);
        return MapDetail(liveClass, [], timeProvider.GetUtcNow());
    }

    public async Task<LiveClassDetailDto> UpdateSessionAsync(string sessionId, AdminLiveClassSessionUpdateRequest request, string adminId, string adminName, CancellationToken ct)
    {
        var session = await db.LiveClassSessions.Include(item => item.LiveClass).FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");
        if (session.Status is LiveClassSessionStatus.Live or LiveClassSessionStatus.Completed)
        {
            throw ApiException.Conflict("live_class_session_locked", "Live or completed sessions cannot be rescheduled.");
        }

        if (request.Capacity.HasValue)
        {
            if (request.Capacity.Value < session.EnrolledCount)
            {
                throw ApiException.Validation("capacity_below_enrolled", "Capacity cannot be lower than current enrollment count.");
            }

            session.Capacity = Math.Max(1, request.Capacity.Value);
        }

        if (request.ScheduledStartAt.HasValue || request.DurationMinutes.HasValue)
        {
            var start = request.ScheduledStartAt ?? session.ScheduledStartAt;
            var duration = Math.Clamp(request.DurationMinutes ?? (int)(session.ScheduledEndAt - session.ScheduledStartAt).TotalMinutes, 15, 360);
            session.ScheduledStartAt = start;
            session.ScheduledEndAt = start.AddMinutes(duration);
            session.DurationMinutes = duration;
            await ProvisionZoomMeetingAsync(session.Id, ct);
        }

        session.UpdatedAt = timeProvider.GetUtcNow();
        WriteAudit(adminId, adminName, "LiveClassSessionUpdated", "LiveClassSession", session.Id, new { session.ScheduledStartAt, session.Capacity });
        await db.SaveChangesAsync(ct);
        var liveClass = await db.LiveClasses.AsNoTracking().Include(item => item.Sessions).FirstAsync(item => item.Id == session.LiveClassId, ct);
        return MapDetail(liveClass, [], timeProvider.GetUtcNow());
    }

    public async Task<LiveClassEnrollmentDto> EnrollAsync(string sessionId, string learnerUserId, string? idempotencyKey, CancellationToken ct)
    {
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"live-class-enroll:{sessionId}:{learnerUserId}"
            : idempotencyKey.Trim();

        var existing = await db.LiveClassEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(enrollment => enrollment.IdempotencyKey == normalizedIdempotencyKey
                || (enrollment.ClassSessionId == sessionId && enrollment.UserId == learnerUserId), ct);
        if (existing is not null && existing.Status == LiveClassEnrollmentStatus.Active)
        {
            return MapEnrollment(existing);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await db.LiveClassSessions
            .Include(item => item.LiveClass)
            .FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        var now = timeProvider.GetUtcNow();
        if (session.LiveClass.Status != LiveClassStatus.Published || session.Status != LiveClassSessionStatus.Scheduled)
        {
            throw ApiException.Conflict("live_class_not_enrollable", "This live class is not open for enrollment.");
        }

        if (session.ScheduledEndAt <= now)
        {
            throw ApiException.Conflict("live_class_session_past", "This live class session has already ended.");
        }

        if (session.EnrolledCount >= session.Capacity)
        {
            await AddToWaitlistAsync(session.Id, learnerUserId, now, ct);
            await transaction.CommitAsync(ct);
            throw ApiException.Conflict("live_class_full", "This class is full. You have been added to the waitlist.");
        }

        var cost = Math.Max(0, session.LiveClass.CreditCost);
        Guid? debitTransactionId = null;
        if (cost > 0)
        {
            debitTransactionId = await DebitWalletForEnrollmentAsync(learnerUserId, cost, session, normalizedIdempotencyKey, now, ct);
        }

        var enrollment = new LiveClassEnrollment
        {
            Id = $"LCE-{Guid.NewGuid():N}",
            ClassSessionId = session.Id,
            UserId = learnerUserId,
            EnrolledAt = now,
            CreditsCharged = cost,
            WalletTransactionId = debitTransactionId,
            Status = LiveClassEnrollmentStatus.Active,
            IdempotencyKey = normalizedIdempotencyKey,
        };

        session.EnrolledCount++;
        session.UpdatedAt = now;
        db.LiveClassEnrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await NotifyLearnerEnrollmentAsync(enrollment, session, ct);
        return MapEnrollment(enrollment);
    }

    public async Task<LiveClassEnrollmentDto> CancelEnrollmentAsync(string sessionId, string learnerUserId, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var enrollment = await db.LiveClassEnrollments
            .Include(item => item.ClassSession)
            .ThenInclude(session => session.LiveClass)
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId, ct)
            ?? throw ApiException.NotFound("live_class_enrollment_not_found", "Enrollment not found.");

        if (enrollment.Status != LiveClassEnrollmentStatus.Active)
        {
            return MapEnrollment(enrollment);
        }

        var now = timeProvider.GetUtcNow();
        var hoursUntilStart = (enrollment.ClassSession.ScheduledStartAt - now).TotalHours;
        var refundCredits = CalculateRefundCredits(enrollment.CreditsCharged, hoursUntilStart);

        enrollment.Status = refundCredits > 0 ? LiveClassEnrollmentStatus.Refunded : LiveClassEnrollmentStatus.Cancelled;
        enrollment.CancelledAt = now;
        enrollment.CancellationReason = NormalizeOptional(reason);
        enrollment.ClassSession.EnrolledCount = Math.Max(0, enrollment.ClassSession.EnrolledCount - 1);
        enrollment.ClassSession.UpdatedAt = now;

        if (refundCredits > 0)
        {
            enrollment.RefundWalletTransactionId = await CreditWalletRefundAsync(
                learnerUserId,
                refundCredits,
                enrollment.ClassSession,
                $"live-class-refund:{enrollment.Id}",
                now,
                ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapEnrollment(enrollment);
    }

    public async Task<LiveClassJoinTokenResponse> CreateLearnerJoinTokenAsync(string sessionId, string learnerUserId, CancellationToken ct)
    {
        var enrollment = await db.LiveClassEnrollments
            .AsNoTracking()
            .Include(item => item.ClassSession)
            .ThenInclude(session => session.LiveClass)
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId && item.Status == LiveClassEnrollmentStatus.Active, ct)
            ?? throw ApiException.Forbidden("live_class_not_enrolled", "You must be enrolled before joining this class.");

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == learnerUserId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "Learner profile not found.");

        return CreateJoinToken(enrollment.ClassSession, learner.DisplayName, learner.Email, role: 0);
    }

    public async Task<LiveClassJoinTokenResponse> CreateExpertJoinTokenAsync(string sessionId, string expertUserId, CancellationToken ct)
    {
        var session = await db.LiveClassSessions
            .AsNoTracking()
            .Include(item => item.LiveClass)
            .ThenInclude(liveClass => liveClass.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        if (session.LiveClass.TutorProfile?.ExpertUserId != expertUserId)
        {
            throw ApiException.Forbidden("live_class_not_assigned", "This live class is assigned to another tutor.");
        }

        var expert = await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(user => user.Id == expertUserId, ct)
            ?? throw ApiException.NotFound("expert_not_found", "Expert profile not found.");

        return CreateJoinToken(session, expert.DisplayName, expert.Email, role: 1);
    }

    public async Task<IReadOnlyList<LiveClassListItemDto>> ListLearnerEnrollmentsAsync(string learnerUserId, bool upcoming, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await db.LiveClassEnrollments
            .AsNoTracking()
            .Include(enrollment => enrollment.ClassSession)
            .ThenInclude(session => session.LiveClass)
            .Where(enrollment => enrollment.UserId == learnerUserId
                && enrollment.Status == LiveClassEnrollmentStatus.Active
                && (upcoming ? enrollment.ClassSession.ScheduledEndAt >= now : enrollment.ClassSession.ScheduledEndAt < now))
            .OrderBy(enrollment => enrollment.ClassSession.ScheduledStartAt)
            .Select(enrollment => enrollment.ClassSession.LiveClass)
            .Distinct()
            .ToListAsync(ct);

        var enrolledSessionIds = await GetActiveEnrollmentSessionIdsAsync(learnerUserId, ct);
        return sessions.Select(liveClass => MapListItem(liveClass, enrolledSessionIds, now)).ToList();
    }

    public async Task<LiveClassRecordingDto> GetRecordingForLearnerAsync(string sessionId, string learnerUserId, CancellationToken ct)
    {
        var hasAccess = await db.LiveClassEnrollments.AsNoTracking().AnyAsync(enrollment =>
            enrollment.ClassSessionId == sessionId
            && enrollment.UserId == learnerUserId
            && enrollment.Status != LiveClassEnrollmentStatus.Cancelled, ct);
        if (!hasAccess)
        {
            throw ApiException.Forbidden("live_class_recording_forbidden", "Recording access is limited to enrolled learners.");
        }

        var recording = await db.LiveClassRecordings.AsNoTracking().FirstOrDefaultAsync(item => item.ClassSessionId == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_recording_not_found", "Recording is not available yet.");
        return MapRecording(recording);
    }

    public async Task<LiveClassAnalyticsDto> GetAnalyticsAsync(CancellationToken ct)
    {
        var totalClasses = await db.LiveClasses.CountAsync(ct);
        var upcoming = await db.LiveClassSessions.CountAsync(item => item.Status == LiveClassSessionStatus.Scheduled, ct);
        var live = await db.LiveClassSessions.CountAsync(item => item.Status == LiveClassSessionStatus.Live, ct);
        var completed = await db.LiveClassSessions.CountAsync(item => item.Status == LiveClassSessionStatus.Completed, ct);
        var enrollments = await db.LiveClassEnrollments.CountAsync(ct);
        var attended = await db.LiveClassAttendances.Select(item => item.UserId + ":" + item.ClassSessionId).Distinct().CountAsync(ct);
        var failures = await db.LiveClassRecordings.CountAsync(item => item.Status == LiveClassRecordingStatus.Failed, ct);
        var attendanceRate = enrollments == 0 ? 0 : Math.Round((double)attended / enrollments * 100, 1);
        return new LiveClassAnalyticsDto(totalClasses, upcoming, live, completed, enrollments, attended, attendanceRate, failures);
    }

    public async Task<object?> HandleZoomWebhookAsync(string rawBody, IHeaderDictionary headers, CancellationToken ct)
    {
        var verification = zoomMeetingService.TryBuildWebhookUrlValidationResponse(rawBody);
        if (verification is not null)
        {
            return verification;
        }

        if (!zoomMeetingService.VerifyWebhookSignature(rawBody, headers))
        {
            throw ApiException.Unauthorized("zoom_webhook_invalid_signature", "Zoom webhook signature verification failed.");
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var existing = await db.LiveClassWebhookEvents.AsNoTracking().FirstOrDefaultAsync(item => item.PayloadHash == payloadHash, ct);
        if (existing is not null)
        {
            return new { ok = true, duplicate = true };
        }

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        var eventType = root.TryGetProperty("event", out var eventProperty) ? eventProperty.GetString() ?? "unknown" : "unknown";
        var webhookEvent = new LiveClassWebhookEvent
        {
            Id = $"LCW-{Guid.NewGuid():N}",
            EventType = eventType,
            PayloadHash = payloadHash,
            RawPayload = rawBody,
            Status = LiveClassWebhookStatus.Processing,
            ReceivedAt = timeProvider.GetUtcNow(),
        };
        db.LiveClassWebhookEvents.Add(webhookEvent);

        try
        {
            await ApplyZoomWebhookAsync(eventType, root, ct);
            webhookEvent.Status = LiveClassWebhookStatus.Processed;
            webhookEvent.ProcessedAt = timeProvider.GetUtcNow();
        }
        catch (Exception ex)
        {
            webhookEvent.Status = LiveClassWebhookStatus.Failed;
            webhookEvent.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            logger.LogWarning(ex, "Failed to process Zoom webhook {EventType}", eventType);
        }

        await db.SaveChangesAsync(ct);
        return new { ok = true };
    }

    public async Task CancelSessionAsync(string sessionId, string adminId, string adminName, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await db.LiveClassSessions
            .Include(item => item.LiveClass)
            .Include(item => item.Enrollments)
            .FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        if (session.Status == LiveClassSessionStatus.Cancelled)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        session.Status = LiveClassSessionStatus.Cancelled;
        session.CancellationReason = NormalizeOptional(reason);
        session.UpdatedAt = now;

        foreach (var enrollment in session.Enrollments.Where(item => item.Status == LiveClassEnrollmentStatus.Active))
        {
            enrollment.Status = enrollment.CreditsCharged > 0 ? LiveClassEnrollmentStatus.Refunded : LiveClassEnrollmentStatus.Cancelled;
            enrollment.CancelledAt = now;
            enrollment.CancellationReason = "Class cancelled by admin.";
            if (enrollment.CreditsCharged > 0)
            {
                enrollment.RefundWalletTransactionId = await CreditWalletRefundAsync(enrollment.UserId, enrollment.CreditsCharged, session, $"live-class-admin-cancel:{enrollment.Id}", now, ct);
            }
        }

        session.EnrolledCount = 0;
        WriteAudit(adminId, adminName, "LiveClassSessionCancelled", "LiveClassSession", session.Id, new { reason });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (session.ZoomMeetingId.HasValue)
        {
            try
            {
                await zoomMeetingService.DeleteMeetingAsync(session.ZoomMeetingId.Value, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete Zoom meeting for cancelled live class session {SessionId}", session.Id);
            }
        }
    }

    private async Task ProvisionZoomMeetingAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.LiveClassSessions.Include(item => item.LiveClass).FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        var duration = Math.Max(15, (int)Math.Ceiling((session.ScheduledEndAt - session.ScheduledStartAt).TotalMinutes));
        try
        {
            var meeting = await zoomMeetingService.CreateMeetingAsync(
                session.LiveClass.Title,
                session.ScheduledStartAt,
                duration,
                "UTC",
                ct);
            session.ZoomMeetingId = meeting.MeetingId;
            session.ZoomMeetingNumber = meeting.MeetingId.ToString(CultureInfo.InvariantCulture);
            session.ZoomJoinUrl = meeting.JoinUrl;
            session.ZoomStartUrl = meeting.StartUrl;
            session.ZoomPasscode = meeting.Password;
            session.ZoomError = null;
        }
        catch (Exception ex)
        {
            session.ZoomRetryCount++;
            session.ZoomError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            logger.LogWarning(ex, "Failed to provision Zoom meeting for live class session {SessionId}", session.Id);
        }

        session.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task<HashSet<string>> GetActiveEnrollmentSessionIdsAsync(string? learnerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(learnerUserId))
        {
            return [];
        }

        return await db.LiveClassEnrollments.AsNoTracking()
            .Where(enrollment => enrollment.UserId == learnerUserId && enrollment.Status == LiveClassEnrollmentStatus.Active)
            .Select(enrollment => enrollment.ClassSessionId)
            .ToHashSetAsync(ct);
    }

    private static LiveClassListItemDto MapListItem(LiveClass liveClass, HashSet<string> enrolledSessionIds, DateTimeOffset now)
        => new(
            liveClass.Id,
            liveClass.Slug,
            liveClass.Title,
            liveClass.TitleAr,
            liveClass.Description,
            liveClass.DescriptionAr,
            liveClass.Type.ToString(),
            liveClass.ProfessionTrack,
            liveClass.Level,
            liveClass.TutorProfileId,
            liveClass.TutorDisplayName,
            liveClass.CreditCost,
            liveClass.Status.ToString(),
            liveClass.CoverImageUrl,
            liveClass.Sessions
                .OrderBy(session => session.ScheduledStartAt)
                .Select(session => MapSessionSummary(liveClass, session, enrolledSessionIds.Contains(session.Id), now))
                .ToList());

    private static LiveClassDetailDto MapDetail(LiveClass liveClass, HashSet<string> enrolledSessionIds, DateTimeOffset now)
        => new(
            liveClass.Id,
            liveClass.Slug,
            liveClass.Title,
            liveClass.TitleAr,
            liveClass.Description,
            liveClass.DescriptionAr,
            liveClass.Type.ToString(),
            liveClass.ProfessionTrack,
            liveClass.Level,
            liveClass.TutorProfileId,
            liveClass.TutorDisplayName,
            liveClass.DefaultDurationMinutes,
            liveClass.DefaultCapacity,
            liveClass.CreditCost,
            liveClass.Status.ToString(),
            liveClass.CoverImageUrl,
            DeserializeStringArray(liveClass.TagsJson),
            liveClass.Sessions
                .OrderBy(session => session.ScheduledStartAt)
                .Select(session => MapSessionSummary(liveClass, session, enrolledSessionIds.Contains(session.Id), now))
                .ToList());

    private static LiveClassSessionSummaryDto MapSessionSummary(LiveClass liveClass, LiveClassSession session, bool isEnrolled, DateTimeOffset now)
        => new(
            session.Id,
            session.ScheduledStartAt,
            session.ScheduledEndAt,
            session.Capacity,
            session.EnrolledCount,
            session.Status.ToString(),
            isEnrolled,
            isEnrolled && session.ScheduledStartAt <= now.AddMinutes(30) && session.ScheduledEndAt >= now.AddMinutes(-15),
            liveClass.CreditCost);

    private static LiveClassEnrollmentDto MapEnrollment(LiveClassEnrollment enrollment)
        => new(
            enrollment.Id,
            enrollment.ClassSessionId,
            enrollment.UserId,
            enrollment.EnrolledAt,
            enrollment.CreditsCharged,
            enrollment.Status.ToString(),
            enrollment.CancelledAt,
            enrollment.CancellationReason);

    private static LiveClassRecordingDto MapRecording(LiveClassRecording recording)
        => new(
            recording.Id,
            recording.ClassSessionId,
            recording.Status.ToString(),
            recording.S3VideoKey,
            recording.S3TranscriptKey,
            recording.TranscriptText,
            recording.AiSummary,
            recording.AiSummaryAr,
            DeserializeChapters(recording.ChaptersJson),
            DeserializeStringArray(recording.ActionItemsJson),
            recording.ExpiresAt);

    private LiveClassJoinTokenResponse CreateJoinToken(LiveClassSession session, string displayName, string? email, int role)
    {
        var meetingNumber = session.ZoomMeetingNumber ?? session.ZoomMeetingId?.ToString(CultureInfo.InvariantCulture)
            ?? throw ApiException.ServiceUnavailable("zoom_meeting_not_ready", "Zoom meeting is not ready yet.");
        var expiresAt = timeProvider.GetUtcNow().AddHours(2);
        var signature = zoomMeetingService.GenerateMeetingSdkSignature(meetingNumber, role, expiresAt);
        return new LiveClassJoinTokenResponse(
            "zoom",
            zoomMeetingService.MeetingSdkKey,
            signature,
            meetingNumber,
            displayName,
            email,
            role,
            session.ZoomPasscode,
            null,
            role == 0 ? session.ZoomJoinUrl : session.ZoomStartUrl,
            expiresAt);
    }

    private async Task<Guid> DebitWalletForEnrollmentAsync(
        string learnerUserId,
        int amount,
        LiveClassSession session,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(item => item.UserId == learnerUserId, ct)
            ?? throw ApiException.PaymentRequired("wallet_not_found", "Wallet not found. Add credits before enrolling.");
        if (wallet.CreditBalance < amount)
        {
            throw ApiException.PaymentRequired("insufficient_credits", "You do not have enough credits to enroll in this class.");
        }

        wallet.CreditBalance -= amount;
        wallet.LastUpdatedAt = now;
        var walletTransaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "live_class_debit",
            Amount = -amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = "live_class",
            ReferenceId = session.Id,
            IdempotencyKey = idempotencyKey,
            Description = $"Live class enrollment: {session.LiveClass.Title}",
            CreatedBy = "system",
            CreatedAt = now,
        };
        db.WalletTransactions.Add(walletTransaction);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = learnerUserId,
            ActorName = learnerUserId,
            Action = "LiveClassWalletDebited",
            ResourceType = "Wallet",
            ResourceId = wallet.Id,
            Details = JsonSerializer.Serialize(new { sessionId = session.Id, amount, walletTransaction.Id }, JsonOptions),
        });
        return walletTransaction.Id;
    }

    private async Task<Guid> CreditWalletRefundAsync(
        string learnerUserId,
        int amount,
        LiveClassSession session,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(item => item.UserId == learnerUserId, ct)
            ?? throw ApiException.NotFound("wallet_not_found", "Wallet not found for refund.");
        wallet.CreditBalance += amount;
        wallet.LastUpdatedAt = now;
        var walletTransaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "refund",
            Amount = amount,
            BalanceAfter = wallet.CreditBalance,
            ReferenceType = "live_class",
            ReferenceId = session.Id,
            IdempotencyKey = idempotencyKey,
            Description = $"Live class refund: {session.LiveClass.Title}",
            CreatedBy = "system",
            CreatedAt = now,
        };
        db.WalletTransactions.Add(walletTransaction);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = learnerUserId,
            ActorName = learnerUserId,
            Action = "LiveClassWalletRefunded",
            ResourceType = "Wallet",
            ResourceId = wallet.Id,
            Details = JsonSerializer.Serialize(new { sessionId = session.Id, amount, walletTransaction.Id }, JsonOptions),
        });
        return walletTransaction.Id;
    }

    private async Task AddToWaitlistAsync(string sessionId, string learnerUserId, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await db.LiveClassWaitlistEntries.AnyAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId, ct);
        if (exists)
        {
            return;
        }

        var nextPosition = await db.LiveClassWaitlistEntries.Where(item => item.ClassSessionId == sessionId).Select(item => (int?)item.Position).MaxAsync(ct) ?? 0;
        db.LiveClassWaitlistEntries.Add(new LiveClassWaitlistEntry
        {
            Id = $"LCW-{Guid.NewGuid():N}",
            ClassSessionId = sessionId,
            UserId = learnerUserId,
            Position = nextPosition + 1,
            JoinedWaitlistAt = now,
        });
    }

    private async Task NotifyLearnerEnrollmentAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct)
    {
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerClassBookingConfirmed,
            enrollment.UserId,
            "live_class_enrollment",
            enrollment.Id,
            enrollment.EnrolledAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            new Dictionary<string, object?>
            {
                ["classTitle"] = session.LiveClass.Title,
                ["sessionTime"] = session.ScheduledStartAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                ["classId"] = session.LiveClassId,
                ["sessionId"] = session.Id,
            },
            ct);
    }

    private async Task ApplyZoomWebhookAsync(string eventType, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("object", out var zoomObject))
        {
            return;
        }

        var meetingId = TryReadLong(zoomObject, "id");
        if (!meetingId.HasValue)
        {
            return;
        }

        var session = await db.LiveClassSessions.FirstOrDefaultAsync(item => item.ZoomMeetingId == meetingId.Value, ct);
        if (session is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        switch (eventType)
        {
            case "meeting.started":
                session.Status = LiveClassSessionStatus.Live;
                session.ActualStartAt ??= now;
                session.UpdatedAt = now;
                break;
            case "meeting.ended":
                session.Status = LiveClassSessionStatus.Completed;
                session.ActualEndAt = now;
                if (session.ActualStartAt.HasValue)
                {
                    session.DurationMinutes = Math.Max(0, (int)(now - session.ActualStartAt.Value).TotalMinutes);
                }
                session.UpdatedAt = now;
                break;
            case "recording.completed":
                await UpsertRecordingPlaceholderAsync(session, zoomObject, now, ct);
                break;
            case "meeting.participant_joined":
                await UpsertAttendanceAsync(session, zoomObject, now, joined: true, ct);
                break;
            case "meeting.participant_left":
                await UpsertAttendanceAsync(session, zoomObject, now, joined: false, ct);
                break;
        }
    }

    private async Task UpsertRecordingPlaceholderAsync(LiveClassSession session, JsonElement zoomObject, DateTimeOffset now, CancellationToken ct)
    {
        var recordingId = TryReadString(zoomObject, "uuid") ?? TryReadString(zoomObject, "recording_play_passcode") ?? $"zoom-{session.ZoomMeetingId}";
        var existing = await db.LiveClassRecordings.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id, ct);
        if (existing is null)
        {
            existing = new LiveClassRecording
            {
                Id = $"LCR-{Guid.NewGuid():N}",
                ClassSessionId = session.Id,
                ZoomRecordingId = recordingId,
                Status = LiveClassRecordingStatus.Pending,
                RecordedAt = now,
                ExpiresAt = now.AddDays(365),
            };
            db.LiveClassRecordings.Add(existing);
            session.RecordingId = existing.Id;
        }
        else
        {
            existing.ZoomRecordingId ??= recordingId;
            existing.Status = existing.Status == LiveClassRecordingStatus.Ready ? existing.Status : LiveClassRecordingStatus.Pending;
        }
    }

    private async Task UpsertAttendanceAsync(LiveClassSession session, JsonElement zoomObject, DateTimeOffset now, bool joined, CancellationToken ct)
    {
        if (!zoomObject.TryGetProperty("participant", out var participant))
        {
            return;
        }

        var email = TryReadString(participant, "user_email");
        var user = string.IsNullOrWhiteSpace(email)
            ? null
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Email == email, ct);
        if (user is null)
        {
            return;
        }

        var enrollment = await db.LiveClassEnrollments.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id && item.UserId == user.Id, ct);
        var attendance = await db.LiveClassAttendances.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id && item.UserId == user.Id, ct);
        if (attendance is null && joined)
        {
            attendance = new LiveClassAttendance
            {
                Id = $"LCA-{Guid.NewGuid():N}",
                ClassSessionId = session.Id,
                UserId = user.Id,
                EnrollmentId = enrollment?.Id,
                JoinedAt = now,
                ZoomParticipantUuid = TryReadString(participant, "user_id"),
            };
            db.LiveClassAttendances.Add(attendance);
        }
        else if (attendance is not null && !joined)
        {
            attendance.LeftAt = now;
            attendance.DurationSeconds = Math.Max(attendance.DurationSeconds, (int)(now - attendance.JoinedAt).TotalSeconds);
        }
    }

    private static int CalculateRefundCredits(int creditsCharged, double hoursUntilStart)
    {
        if (creditsCharged <= 0 || hoursUntilStart < 1)
        {
            return 0;
        }

        return hoursUntilStart >= 24 ? creditsCharged : (int)Math.Floor(creditsCharged / 2.0);
    }

    private static LiveClassType ParseClassType(string value)
        => Enum.TryParse<LiveClassType>(value, true, out var type) ? type : LiveClassType.GroupClass;

    private static void ValidateAdminRequest(AdminLiveClassUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw ApiException.Validation("live_class_title_required", "Class title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw ApiException.Validation("live_class_description_required", "Class description is required.");
        }

        if (request.ScheduledStartAt <= DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            throw ApiException.Validation("live_class_start_in_past", "Class start time must be in the future.");
        }
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? $"live-class-{Guid.NewGuid():N}"[..24] : baseSlug;
        var candidate = slug;
        var suffix = 2;
        while (await db.LiveClasses.AnyAsync(item => item.Slug == candidate, ct))
        {
            candidate = $"{slug}-{suffix++}";
        }
        return candidate;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    private void WriteAudit(string actorId, string actorName, string action, string resourceType, string resourceId, object details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = actorId,
            ActorName = actorName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = JsonSerializer.Serialize(details, JsonOptions),
        });
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<LiveClassRecordingChapterDto> DeserializeChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<LiveClassRecordingChapterDto[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static long? TryReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var numericValue))
        {
            return numericValue;
        }
        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }
        return null;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}