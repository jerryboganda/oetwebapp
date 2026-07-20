using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Classes;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.LiveClasses;

public sealed class LiveClassService(
    LearnerDbContext db,
    ZoomMeetingService zoomMeetingService,
    WalletService walletService,
    NotificationService notificationService,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<LiveClassService> logger,
    IClassNotificationService? classNotifications = null,
    PrivateSpeakingService? privateSpeakingService = null)
{
    // Wave A3 reminder cascade. Order matters: scheduling code iterates the array and
    // de-dupes per enrollment by (lead → resourceId), so changing the order would
    // re-shuffle the dedupe keys in BackgroundJobs and could lose existing reminders
    // on the next deployment.
    private static readonly int[] ReminderLeadMinutes = [1440, 60, 10];
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

        if (liveClass.Status != LiveClassStatus.Published)
        {
            throw ApiException.NotFound("live_class_not_found", "Live class not found.");
        }

        var enrolledSessionIds = await GetActiveEnrollmentSessionIdsAsync(learnerUserId, ct);
        return MapDetail(liveClass, enrolledSessionIds, now);
    }

    public async Task<AdminLiveClassDetailDto> GetAdminClassDetailAsync(string idOrSlug, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var liveClass = await db.LiveClasses
            .AsNoTracking()
            .Include(lc => lc.Sessions)
            .FirstOrDefaultAsync(lc => lc.Id == idOrSlug || lc.Slug == idOrSlug, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");
        return MapAdminDetail(liveClass, now);
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
        await QueueSessionReminderJobAsync(session, now, ct);
        await db.SaveChangesAsync(ct);

        await ProvisionZoomMeetingAsync(session.Id, ct);

        var created = await db.LiveClasses.AsNoTracking().Include(item => item.Sessions).FirstAsync(item => item.Id == classId, ct);
        return MapDetail(created, [], now);
    }

    public async Task<LiveClassDetailDto> PublishClassAsync(string liveClassId, string adminId, string adminName, CancellationToken ct)
    {
        var liveClass = await db.LiveClasses.Include(item => item.Sessions).FirstOrDefaultAsync(item => item.Id == liveClassId, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");
        var now = timeProvider.GetUtcNow();
        liveClass.Status = LiveClassStatus.Published;
        liveClass.UpdatedAt = now;
        foreach (var session in liveClass.Sessions.Where(item => item.Status == LiveClassSessionStatus.Scheduled))
        {
            await QueueSessionReminderJobAsync(session, now, ct);
        }

        WriteAudit(adminId, adminName, "LiveClassPublished", "LiveClass", liveClass.Id, new { liveClass.Title });
        await db.SaveChangesAsync(ct);
        return MapDetail(liveClass, [], now);
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
            var previousZoomMeetingId = session.ZoomMeetingId;
            session.ScheduledStartAt = start;
            session.ScheduledEndAt = start.AddMinutes(duration);
            session.DurationMinutes = duration;
            session.ZoomMeetingId = null;
            session.ZoomMeetingNumber = null;
            session.ZoomJoinUrl = null;
            session.ZoomStartUrl = null;
            session.ZoomPasscode = null;
            session.ZoomError = null;
            await QueueSessionReminderJobAsync(session, timeProvider.GetUtcNow(), ct);

            if (previousZoomMeetingId.HasValue)
            {
                try
                {
                    await zoomMeetingService.DeleteMeetingAsync(previousZoomMeetingId.Value, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete replaced Zoom meeting for live class session {SessionId}", session.Id);
                }
            }

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
            .FirstOrDefaultAsync(enrollment => enrollment.ClassSessionId == sessionId
                && enrollment.UserId == learnerUserId
                && (enrollment.IdempotencyKey == normalizedIdempotencyKey
                    || enrollment.Status == LiveClassEnrollmentStatus.Active), ct);
        if (existing is not null && existing.Status == LiveClassEnrollmentStatus.Active)
        {
            return MapEnrollment(existing);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
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

        var enrollment = await db.LiveClassEnrollments
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId, ct);
        if (enrollment is not null && enrollment.Status == LiveClassEnrollmentStatus.Active)
        {
            return MapEnrollment(enrollment);
        }

        var cost = Math.Max(0, session.LiveClass.CreditCost);
        Guid? debitTransactionId = null;
        if (cost > 0)
        {
            var previousChargeAnchor = enrollment?.WalletTransactionId?.ToString("N")
                ?? (enrollment?.CancelledAt ?? enrollment?.EnrolledAt ?? now).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var debitIdempotencyKey = enrollment is null || enrollment.Status == LiveClassEnrollmentStatus.Active
                ? normalizedIdempotencyKey
                : $"{normalizedIdempotencyKey}:reenroll:{previousChargeAnchor}";
            debitTransactionId = await DebitWalletForEnrollmentAsync(learnerUserId, cost, session, debitIdempotencyKey, now, ct);
        }

        if (enrollment is null)
        {
            enrollment = new LiveClassEnrollment
            {
                Id = $"LCE-{Guid.NewGuid():N}",
                ClassSessionId = session.Id,
                UserId = learnerUserId,
            };
            db.LiveClassEnrollments.Add(enrollment);
        }

        enrollment.EnrolledAt = now;
        enrollment.CancelledAt = null;
        enrollment.CancellationReason = null;
        enrollment.CreditsCharged = cost;
        enrollment.WalletTransactionId = debitTransactionId;
        enrollment.RefundWalletTransactionId = null;
        enrollment.Status = LiveClassEnrollmentStatus.Active;
        enrollment.IdempotencyKey = normalizedIdempotencyKey;

        session.EnrolledCount++;
        session.UpdatedAt = now;
        await ScheduleEnrollmentReminderCascadeAsync(enrollment, session, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await NotifyLearnerEnrollmentAsync(enrollment, session, ct);
        return MapEnrollment(enrollment);
    }

    public async Task<LiveClassEnrollmentDto> CancelEnrollmentAsync(string sessionId, string learnerUserId, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
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

        // Slot has opened — promote the next waitlisted learner if any.
        await PromoteFromWaitlistAsync(sessionId, enrollment.ClassSession, now, ct);

        await CancelEnrollmentReminderCascadeAsync(enrollment.Id, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await NotifyLearnerCancellationAsync(enrollment, enrollment.ClassSession, refundCredits, cancelledByTutor: false, ct);
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

        var now = timeProvider.GetUtcNow();
        if (!IsJoinWindowOpen(enrollment.ClassSession, now))
        {
            throw ApiException.Conflict("live_class_join_window_closed", "Live class joins open 30 minutes before start and close 15 minutes after the scheduled end.");
        }

        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == learnerUserId, ct)
            ?? throw ApiException.NotFound("learner_not_found", "Learner profile not found.");

        return await CreateJoinTokenAsync(enrollment.ClassSession, learner.DisplayName, learner.Email, role: 0, ct);
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

        if (session.LiveClass.Status != LiveClassStatus.Published || session.Status is not (LiveClassSessionStatus.Scheduled or LiveClassSessionStatus.Live))
        {
            throw ApiException.Conflict("live_class_not_hostable", "This live class is not open for hosting.");
        }

        var now = timeProvider.GetUtcNow();
        if (!IsJoinWindowOpen(session, now))
        {
            throw ApiException.Conflict("live_class_join_window_closed", "Live class host access opens 30 minutes before start and closes 15 minutes after the scheduled end.");
        }

        var expert = await db.ExpertUsers.AsNoTracking().FirstOrDefaultAsync(user => user.Id == expertUserId, ct)
            ?? throw ApiException.NotFound("expert_not_found", "Expert profile not found.");

        return await CreateJoinTokenAsync(session, expert.DisplayName, expert.Email, role: 1, ct);
    }

    public async Task<IReadOnlyList<LiveClassListItemDto>> ListExpertClassesAsync(string expertUserId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var classes = await db.LiveClasses
            .AsNoTracking()
            .Include(liveClass => liveClass.TutorProfile)
            .Include(liveClass => liveClass.Sessions)
            .Where(liveClass => liveClass.TutorProfile != null && liveClass.TutorProfile.ExpertUserId == expertUserId)
            .Where(liveClass => liveClass.Status == LiveClassStatus.Published)
            .OrderBy(liveClass => liveClass.Sessions.Min(session => (DateTimeOffset?)session.ScheduledStartAt) ?? DateTimeOffset.MaxValue)
            .ToListAsync(ct);

        return classes
            .Select(liveClass => MapListItem(liveClass, [], now, includeJoinAvailabilityWithoutEnrollment: true))
            .ToList();
    }

    public async Task<IReadOnlyList<LiveClassListItemDto>> ListLearnerEnrollmentsAsync(string learnerUserId, bool upcoming, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var classIds = await db.LiveClassEnrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.UserId == learnerUserId
                && (upcoming
                    ? enrollment.Status == LiveClassEnrollmentStatus.Active
                    : enrollment.Status == LiveClassEnrollmentStatus.Active
                      || enrollment.Status == LiveClassEnrollmentStatus.Attended
                      || enrollment.Status == LiveClassEnrollmentStatus.NoShow)
                && (upcoming ? enrollment.ClassSession.ScheduledEndAt >= now : enrollment.ClassSession.ScheduledEndAt < now))
            .OrderBy(enrollment => enrollment.ClassSession.ScheduledStartAt)
            .Select(enrollment => enrollment.ClassSession.LiveClassId)
            .Distinct()
            .ToListAsync(ct);

        if (classIds.Count == 0)
        {
            return [];
        }

        var enrolledSessionIds = await db.LiveClassEnrollments.AsNoTracking()
            .Where(enrollment => enrollment.UserId == learnerUserId
                && (upcoming
                    ? enrollment.Status == LiveClassEnrollmentStatus.Active
                    : enrollment.Status == LiveClassEnrollmentStatus.Active
                      || enrollment.Status == LiveClassEnrollmentStatus.Attended
                      || enrollment.Status == LiveClassEnrollmentStatus.NoShow)
                && (upcoming ? enrollment.ClassSession.ScheduledEndAt >= now : enrollment.ClassSession.ScheduledEndAt < now))
            .Select(enrollment => enrollment.ClassSessionId)
            .ToHashSetAsync(ct);

        var classes = await db.LiveClasses
            .AsNoTracking()
            .Include(liveClass => liveClass.Sessions)
            .Where(liveClass => classIds.Contains(liveClass.Id))
            .ToListAsync(ct);
        var classOrder = classIds.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
        foreach (var liveClass in classes)
        {
            liveClass.Sessions = liveClass.Sessions
                .Where(session => enrolledSessionIds.Contains(session.Id))
                .ToList();
        }

        return classes
            .OrderBy(liveClass => classOrder.GetValueOrDefault(liveClass.Id, int.MaxValue))
            .Select(liveClass => MapListItem(liveClass, enrolledSessionIds, now))
            .ToList();
    }

    public async Task<LiveClassRecordingDto> GetRecordingForLearnerAsync(string sessionId, string learnerUserId, CancellationToken ct)
    {
        var hasAccess = await db.LiveClassEnrollments.AsNoTracking().AnyAsync(enrollment =>
            enrollment.ClassSessionId == sessionId
            && enrollment.UserId == learnerUserId
            && (enrollment.Status == LiveClassEnrollmentStatus.Active || enrollment.Status == LiveClassEnrollmentStatus.Attended), ct);
        if (!hasAccess)
        {
            throw ApiException.Forbidden("live_class_recording_forbidden", "Recording access is limited to enrolled learners.");
        }

        var recording = await db.LiveClassRecordings.AsNoTracking().FirstOrDefaultAsync(item => item.ClassSessionId == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_recording_not_found", "Recording is not available yet.");
        var now = timeProvider.GetUtcNow();
        if (recording.Status != LiveClassRecordingStatus.Ready || string.IsNullOrWhiteSpace(recording.S3VideoKey))
        {
            throw ApiException.NotFound("live_class_recording_not_ready", "Recording is not available yet.");
        }

        if (recording.ExpiresAt.HasValue && recording.ExpiresAt.Value <= now)
        {
            throw ApiException.NotFound("live_class_recording_expired", "This recording has expired.");
        }

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
        if (!await zoomMeetingService.VerifyWebhookSignatureAsync(rawBody, headers, ct))
        {
            throw ApiException.Unauthorized("zoom_webhook_invalid_signature", "Zoom webhook signature verification failed.");
        }

        object? verification = null;
        try
        {
            verification = await zoomMeetingService.TryBuildWebhookUrlValidationResponseAsync(rawBody, ct);
        }
        catch (JsonException)
        {
            verification = null;
        }

        if (verification is not null)
        {
            return verification;
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        var eventType = root.TryGetProperty("event", out var eventProperty) ? eventProperty.GetString() ?? "unknown" : "unknown";
        var now = timeProvider.GetUtcNow();
        var webhookEvent = await db.LiveClassWebhookEvents.FirstOrDefaultAsync(item => item.PayloadHash == payloadHash, ct);
        if (webhookEvent is not null && webhookEvent.Status == LiveClassWebhookStatus.Processed)
        {
            return new { ok = true, duplicate = true };
        }

        if (webhookEvent is not null && webhookEvent.Status == LiveClassWebhookStatus.Processing && webhookEvent.ReceivedAt > now.AddMinutes(-10))
        {
            throw ApiException.ServiceUnavailable("zoom_webhook_processing", "Zoom webhook is already being processed and should be retried later.");
        }

        if (webhookEvent is null)
        {
            webhookEvent = new LiveClassWebhookEvent
            {
                Id = $"LCW-{Guid.NewGuid():N}",
                PayloadHash = payloadHash,
            };
            db.LiveClassWebhookEvents.Add(webhookEvent);
        }

        webhookEvent.EventType = eventType;
        webhookEvent.RawPayload = BuildWebhookReceipt(root, payloadHash);
        webhookEvent.Status = LiveClassWebhookStatus.Processing;
        webhookEvent.ErrorMessage = null;
        webhookEvent.ReceivedAt = now;
        webhookEvent.ProcessedAt = null;

        Exception? processingException = null;
        try
        {
            await ApplyZoomWebhookAsync(eventType, root, ct);
            webhookEvent.Status = LiveClassWebhookStatus.Processed;
            webhookEvent.ProcessedAt = timeProvider.GetUtcNow();
        }
        catch (Exception ex)
        {
            processingException = ex;
            webhookEvent.Status = LiveClassWebhookStatus.Failed;
            webhookEvent.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            logger.LogWarning(ex, "Failed to process Zoom webhook {EventType}", eventType);
        }

        await db.SaveChangesAsync(ct);
        if (processingException is not null)
        {
            throw ApiException.ServiceUnavailable("zoom_webhook_processing_failed", "Zoom webhook could not be processed and should be retried.");
        }

        return new { ok = true };
    }

    public async Task CancelSessionAsync(string sessionId, string adminId, string adminName, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
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
        var cancelledEnrollments = new List<LiveClassEnrollment>();

        foreach (var enrollment in session.Enrollments.Where(item => item.Status == LiveClassEnrollmentStatus.Active))
        {
            enrollment.Status = enrollment.CreditsCharged > 0 ? LiveClassEnrollmentStatus.Refunded : LiveClassEnrollmentStatus.Cancelled;
            enrollment.CancelledAt = now;
            enrollment.CancellationReason = "Class cancelled by admin.";
            cancelledEnrollments.Add(enrollment);
            if (enrollment.CreditsCharged > 0)
            {
                enrollment.RefundWalletTransactionId = await CreditWalletRefundAsync(enrollment.UserId, enrollment.CreditsCharged, session, $"live-class-admin-cancel:{enrollment.Id}", now, ct);
            }
        }

        session.EnrolledCount = 0;
        foreach (var enrollment in cancelledEnrollments)
        {
            await CancelEnrollmentReminderCascadeAsync(enrollment.Id, ct);
        }

        // The 30-minute legacy session reminder job is keyed on the session id, so cancel
        // it as well — the per-enrollment cascade has fully replaced it.
        await CancelLegacySessionReminderAsync(session.Id, ct);

        WriteAudit(adminId, adminName, "LiveClassSessionCancelled", "LiveClassSession", session.Id, new { reason });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var enrollment in cancelledEnrollments)
        {
            await NotifyLearnerCancellationAsync(enrollment, session, enrollment.CreditsCharged, cancelledByTutor: true, ct);
        }

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

    public async Task<LiveClassSessionSummaryDto> AddSessionAsync(
        string liveClassId,
        AdminLiveClassSessionAddRequest request,
        string adminId,
        string adminName,
        CancellationToken ct)
    {
        var liveClass = await db.LiveClasses.FirstOrDefaultAsync(item => item.Id == liveClassId, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");

        if (liveClass.Status == LiveClassStatus.Archived)
        {
            throw ApiException.Conflict("live_class_archived", "Sessions cannot be added to an archived class.");
        }

        if (request.ScheduledStartAt <= DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            throw ApiException.Validation("live_class_start_in_past", "Session start time must be in the future.");
        }

        var now = timeProvider.GetUtcNow();
        var durationMinutes = Math.Clamp(request.DurationMinutes ?? liveClass.DefaultDurationMinutes, 15, 360);
        var scheduledEnd = request.ScheduledStartAt.AddMinutes(durationMinutes);
        var capacity = Math.Max(1, request.Capacity ?? liveClass.DefaultCapacity);

        var session = new LiveClassSession
        {
            Id = $"LCS-{Guid.NewGuid():N}",
            LiveClassId = liveClass.Id,
            ScheduledStartAt = request.ScheduledStartAt,
            ScheduledEndAt = scheduledEnd,
            Capacity = capacity,
            Status = LiveClassSessionStatus.Scheduled,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.LiveClassSessions.Add(session);
        WriteAudit(adminId, adminName, "LiveClassSessionAdded", "LiveClassSession", session.Id, new { liveClassId, session.ScheduledStartAt, session.Capacity });
        await QueueSessionReminderJobAsync(session, now, ct);
        await db.SaveChangesAsync(ct);

        await ProvisionZoomMeetingAsync(session.Id, ct);

        return MapSessionSummary(liveClass, session, false, now);
    }

    public async Task RetryZoomProvisioningAsync(string sessionId, string adminId, string adminName, CancellationToken ct)
    {
        var session = await db.LiveClassSessions.FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        if (session.ZoomMeetingId.HasValue && session.ZoomError is null)
        {
            throw ApiException.Validation("zoom_already_provisioned", "Session already has a Zoom meeting.");
        }

        session.ZoomRetryCount = 0;
        session.ZoomError = null;
        session.UpdatedAt = timeProvider.GetUtcNow();
        WriteAudit(adminId, adminName, "ZoomRetryRequested", "LiveClassSession", session.Id, new { session.ZoomRetryCount });
        await db.SaveChangesAsync(ct);

        await ProvisionZoomMeetingAsync(sessionId, ct);
    }

    // -----------------------------------------------------------------
    // Wave A1 — waitlist join/leave, transcript fetch, tutor-owned class
    // mutation. These methods extend the service rather than spawn new
    // ones per plan §11.4 (single LiveClassService owns the class graph).
    // -----------------------------------------------------------------

    public async Task<LiveClassWaitlistEntry> JoinWaitlistAsync(string sessionId, string learnerUserId, CancellationToken ct)
    {
        var session = await db.LiveClassSessions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        var alreadyEnrolled = await db.LiveClassEnrollments.AsNoTracking()
            .AnyAsync(enrollment => enrollment.ClassSessionId == sessionId
                && enrollment.UserId == learnerUserId
                && enrollment.Status == LiveClassEnrollmentStatus.Active, ct);
        if (alreadyEnrolled)
        {
            throw ApiException.Conflict("live_class_already_enrolled", "You are already enrolled in this session.");
        }

        var now = timeProvider.GetUtcNow();
        if (session.ScheduledEndAt <= now)
        {
            throw ApiException.Conflict("live_class_session_past", "This live class session has already ended.");
        }

        var existing = await db.LiveClassWaitlistEntries
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var nextPosition = await db.LiveClassWaitlistEntries
            .Where(item => item.ClassSessionId == sessionId)
            .Select(item => (int?)item.Position)
            .MaxAsync(ct) ?? 0;
        var entry = new LiveClassWaitlistEntry
        {
            Id = $"LCW-{Guid.NewGuid():N}",
            ClassSessionId = sessionId,
            UserId = learnerUserId,
            Position = nextPosition + 1,
            JoinedWaitlistAt = now,
        };
        db.LiveClassWaitlistEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task LeaveWaitlistAsync(string sessionId, string learnerUserId, CancellationToken ct)
    {
        var entry = await db.LiveClassWaitlistEntries
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId && item.UserId == learnerUserId, ct);
        if (entry is null)
        {
            return;
        }

        var removedPosition = entry.Position;
        db.LiveClassWaitlistEntries.Remove(entry);

        var trailing = await db.LiveClassWaitlistEntries
            .Where(item => item.ClassSessionId == sessionId && item.Position > removedPosition)
            .ToListAsync(ct);
        foreach (var item in trailing)
        {
            item.Position--;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<(string TranscriptText, DateTimeOffset? ProcessedAt)> GetTranscriptForLearnerAsync(
        string sessionId,
        string learnerUserId,
        CancellationToken ct)
    {
        var hasAccess = await db.LiveClassEnrollments.AsNoTracking().AnyAsync(enrollment =>
            enrollment.ClassSessionId == sessionId
            && enrollment.UserId == learnerUserId
            && (enrollment.Status == LiveClassEnrollmentStatus.Active || enrollment.Status == LiveClassEnrollmentStatus.Attended), ct);
        if (!hasAccess)
        {
            throw ApiException.Forbidden("live_class_transcript_forbidden", "Transcript access is limited to enrolled learners.");
        }

        var recording = await db.LiveClassRecordings.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ClassSessionId == sessionId, ct);
        if (recording is null || string.IsNullOrWhiteSpace(recording.TranscriptText))
        {
            throw ApiException.NotFound("live_class_transcript_not_ready", "Transcript is not available yet.");
        }

        return (recording.TranscriptText, recording.ProcessedAt);
    }

    public async Task<IReadOnlyList<TutorAttendanceLineDto>> GetSessionAttendanceForTutorAsync(
        string sessionId,
        string tutorUserId,
        CancellationToken ct)
    {
        var session = await db.LiveClassSessions.AsNoTracking()
            .Include(item => item.LiveClass)
            .ThenInclude(liveClass => liveClass.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        if (session.LiveClass.TutorProfile?.ExpertUserId != tutorUserId)
        {
            throw ApiException.Forbidden("live_class_not_assigned", "This live class is assigned to another tutor.");
        }

        var rows = await db.LiveClassAttendances.AsNoTracking()
            .Where(attendance => attendance.ClassSessionId == sessionId)
            .OrderBy(attendance => attendance.JoinedAt)
            .ToListAsync(ct);

        // Attendance rows only carry the learner's user id — resolve display
        // names in one batched lookup (same dictionary pattern as
        // ListeningExpertService) so the tutor console can show real names.
        var userIds = rows.Select(row => row.UserId).Distinct().ToList();
        var displayNames = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);

        return rows.Select(row => new TutorAttendanceLineDto(
            row.UserId,
            displayNames.GetValueOrDefault(row.UserId),
            row.JoinedAt,
            row.LeftAt,
            row.DurationSeconds)).ToList();
    }

    // ── Tutor-portal object-level authorization ──────────────────────────
    // Tutor routes under /v1/tutor/me/classes/* reuse the admin-surface mutators
    // (Create/Update/Cancel/AddSession), which load by id with NO owner predicate.
    // These helpers scope a tutor's actions to classes they own — keyed on
    // LiveClass.TutorProfile.ExpertUserId, the same owner model used by
    // ListExpertClassesAsync and GetSessionAttendanceForTutorAsync.

    /// <summary>
    /// Create a live class owned by the calling tutor. Resolves the tutor's
    /// PrivateSpeakingTutorProfile and stamps it on the class so the ownership
    /// guards (and the tutor's "my classes" list) can recognise it. The legacy
    /// tutor create passed TutorProfileId: null, which left classes unowned.
    /// </summary>
    public async Task<LiveClassDetailDto> CreateTutorClassAsync(AdminLiveClassUpsertRequest request, string tutorUserId, string actorName, CancellationToken ct)
    {
        var profileId = await db.PrivateSpeakingTutorProfiles.AsNoTracking()
            .Where(profile => profile.ExpertUserId == tutorUserId)
            .Select(profile => (string?)profile.Id)
            .FirstOrDefaultAsync(ct)
            ?? await ProvisionClassHostProfileAsync(tutorUserId, ct);
        return await CreateAdminClassAsync(request with { TutorProfileId = profileId }, tutorUserId, actorName, ct);
    }

    /// <summary>
    /// PrivateSpeakingTutorProfile doubles as the class-owner anchor
    /// (LiveClass.TutorProfileId), but it is normally admin-provisioned for the
    /// 1:1 private-speaking marketplace — so without this fallback no tutor
    /// could ever create a class ("tutor_profile_required" with no self-serve
    /// path to satisfy it). Provision a minimal INACTIVE profile: IsActive=false
    /// keeps the tutor out of learner 1:1 discovery (which filters on IsActive)
    /// until an admin activates them, while unblocking class hosting.
    /// </summary>
    private async Task<string> ProvisionClassHostProfileAsync(string tutorUserId, CancellationToken ct)
    {
        var expert = await db.ExpertUsers.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == tutorUserId, ct)
            ?? throw ApiException.Validation("tutor_profile_required", "Set up your tutor profile before creating live classes.");

        var now = timeProvider.GetUtcNow();
        var profile = new PrivateSpeakingTutorProfile
        {
            Id = $"pstp-{Guid.NewGuid():N}",
            ExpertUserId = tutorUserId,
            DisplayName = expert.DisplayName,
            Timezone = string.IsNullOrWhiteSpace(expert.Timezone) ? "UTC" : expert.Timezone,
            SpecialtiesJson = string.IsNullOrWhiteSpace(expert.SpecialtiesJson) ? "[]" : expert.SpecialtiesJson,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PrivateSpeakingTutorProfiles.Add(profile);
        WriteAudit(tutorUserId, expert.DisplayName, "ClassHostProfileProvisioned", "PrivateSpeakingTutorProfile", profile.Id,
            new { profile.ExpertUserId, profile.IsActive });
        await db.SaveChangesAsync(ct);
        return profile.Id;
    }

    /// <summary>
    /// Update class-level metadata of a tutor-owned live class. Ownership is
    /// enforced inline (LiveClass.TutorProfile.ExpertUserId — the same owner
    /// model as EnsureTutorOwnsClassAsync): a missing class surfaces NotFound,
    /// a class owned by another tutor surfaces Forbidden. Null request fields
    /// are left unchanged (partial PATCH, mirroring UpdateSessionAsync);
    /// explicit empty strings clear the optional AR/cover fields.
    /// </summary>
    public async Task<LiveClassDetailDto> UpdateTutorClassAsync(
        string classId,
        TutorClassUpdateRequest request,
        string tutorUserId,
        string actorName,
        CancellationToken ct)
    {
        var liveClass = await db.LiveClasses
            .Include(item => item.Sessions)
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == classId, ct)
            ?? throw ApiException.NotFound("live_class_not_found", "Live class not found.");

        if (liveClass.TutorProfile?.ExpertUserId != tutorUserId)
        {
            throw ApiException.Forbidden("live_class_not_assigned", "This live class is assigned to another tutor.");
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            liveClass.Title = request.Title.Trim();
        }

        if (request.TitleAr is not null)
        {
            liveClass.TitleAr = NormalizeOptional(request.TitleAr);
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            liveClass.Description = request.Description.Trim();
        }

        if (request.DescriptionAr is not null)
        {
            liveClass.DescriptionAr = NormalizeOptional(request.DescriptionAr);
        }

        if (request.CoverImageUrl is not null)
        {
            liveClass.CoverImageUrl = NormalizeOptional(request.CoverImageUrl);
        }

        if (request.CreditCost.HasValue)
        {
            liveClass.CreditCost = Math.Max(0, request.CreditCost.Value);
        }

        if (request.DefaultCapacity.HasValue)
        {
            liveClass.DefaultCapacity = Math.Max(1, request.DefaultCapacity.Value);
        }

        if (request.DefaultDurationMinutes.HasValue)
        {
            liveClass.DefaultDurationMinutes = Math.Clamp(request.DefaultDurationMinutes.Value, 15, 360);
        }

        if (request.Tags is not null)
        {
            liveClass.TagsJson = JsonSerializer.Serialize(
                request.Tags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                JsonOptions);
        }

        var now = timeProvider.GetUtcNow();
        liveClass.UpdatedAt = now;
        WriteAudit(tutorUserId, actorName, "LiveClassUpdated", "LiveClass", liveClass.Id,
            new { liveClass.Title, liveClass.CreditCost, liveClass.DefaultCapacity, liveClass.DefaultDurationMinutes });
        await db.SaveChangesAsync(ct);
        return MapDetail(liveClass, [], now);
    }

    /// <summary>Throws Forbidden unless the class is owned by <paramref name="tutorUserId"/>.</summary>
    public async Task EnsureTutorOwnsClassAsync(string classId, string tutorUserId, CancellationToken ct)
    {
        var row = await db.LiveClasses.AsNoTracking()
            .Where(liveClass => liveClass.Id == classId)
            .Select(liveClass => new { OwnerId = liveClass.TutorProfile != null ? liveClass.TutorProfile.ExpertUserId : null })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return; // class doesn't exist — let the core method surface NotFound
        }
        if (row.OwnerId != tutorUserId)
        {
            throw ApiException.Forbidden("live_class_not_assigned", "This live class is assigned to another tutor.");
        }
    }

    /// <summary>Throws Forbidden unless the session's class is owned by <paramref name="tutorUserId"/>.</summary>
    public async Task EnsureTutorOwnsSessionAsync(string sessionId, string tutorUserId, CancellationToken ct)
    {
        var row = await db.LiveClassSessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => new { OwnerId = session.LiveClass.TutorProfile != null ? session.LiveClass.TutorProfile.ExpertUserId : null })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return; // session doesn't exist — let the core method surface NotFound
        }
        if (row.OwnerId != tutorUserId)
        {
            throw ApiException.Forbidden("live_class_not_assigned", "This live class is assigned to another tutor.");
        }
    }

    private async Task ProvisionZoomMeetingAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.LiveClassSessions.Include(item => item.LiveClass).FirstOrDefaultAsync(item => item.Id == sessionId, ct)
            ?? throw ApiException.NotFound("live_class_session_not_found", "Live class session not found.");

        if (!await zoomMeetingService.IsEnabledAsync(ct))
        {
            session.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Zoom integration disabled; leaving live class session {SessionId} unprovisioned.", session.Id);
            return;
        }

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

    private static LiveClassListItemDto MapListItem(
        LiveClass liveClass,
        HashSet<string> enrolledSessionIds,
        DateTimeOffset now,
        bool includeJoinAvailabilityWithoutEnrollment = false)
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
                .Select(session => MapSessionSummary(liveClass, session, enrolledSessionIds.Contains(session.Id), now, includeJoinAvailabilityWithoutEnrollment))
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

    private static LiveClassSessionSummaryDto MapSessionSummary(
        LiveClass liveClass,
        LiveClassSession session,
        bool isEnrolled,
        DateTimeOffset now,
        bool includeJoinAvailabilityWithoutEnrollment = false)
        => new(
            session.Id,
            session.ScheduledStartAt,
            session.ScheduledEndAt,
            session.Capacity,
            session.EnrolledCount,
            session.Status.ToString(),
            isEnrolled,
            // Join also requires the Zoom meeting to exist: the join-token
            // endpoint rejects with zoom_meeting_not_ready otherwise, so a
            // time-only flag showed a Join button that could only fail.
            (isEnrolled || includeJoinAvailabilityWithoutEnrollment) && IsJoinWindowOpen(session, now) && session.ZoomMeetingId is not null,
            liveClass.CreditCost);

    private static bool IsJoinWindowOpen(LiveClassSession session, DateTimeOffset now)
        => session.ScheduledStartAt <= now.AddMinutes(30) && session.ScheduledEndAt >= now.AddMinutes(-15);

    private static string BuildWebhookReceipt(JsonElement root, string payloadHash)
    {
        var eventType = root.TryGetProperty("event", out var eventProperty) ? eventProperty.GetString() ?? "unknown" : "unknown";
        string? objectId = null;
        string? objectUuid = null;
        if (root.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("object", out var payloadObject))
        {
            objectId = payloadObject.TryGetProperty("id", out var idProperty) ? idProperty.ToString() : null;
            objectUuid = payloadObject.TryGetProperty("uuid", out var uuidProperty) ? uuidProperty.GetString() : null;
        }

        return JsonSerializer.Serialize(new
        {
            eventType,
            objectId,
            objectUuid,
            payloadHash,
        });
    }

    private static AdminLiveClassSessionSummaryDto MapAdminSessionSummary(LiveClass liveClass, LiveClassSession session, DateTimeOffset now)
        => new(
            session.Id,
            session.ScheduledStartAt,
            session.ScheduledEndAt,
            session.Capacity,
            session.EnrolledCount,
            session.Status.ToString(),
            false,
            false,
            liveClass.CreditCost,
            session.ZoomMeetingId,
            session.ZoomError);

    private static AdminLiveClassDetailDto MapAdminDetail(LiveClass liveClass, DateTimeOffset now)
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
                .Select(session => MapAdminSessionSummary(liveClass, session, now))
                .ToList());

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

    private LiveClassRecordingDto MapRecording(LiveClassRecording recording)
        => new(
            recording.Id,
            recording.ClassSessionId,
            recording.Status.ToString(),
            ResolveRecordingReadUrl(recording.S3VideoKey),
            ResolveRecordingReadUrl(recording.S3TranscriptKey),
            recording.TranscriptText,
            recording.AiSummary,
            recording.AiSummaryAr,
            DeserializeChapters(recording.ChaptersJson),
            DeserializeStringArray(recording.ActionItemsJson),
            recording.ExpiresAt);

    private string? ResolveRecordingReadUrl(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }

        return fileStorage.ResolveReadUrl(storageKey, TimeSpan.FromHours(2))?.ToString();
    }

    private async Task<LiveClassJoinTokenResponse> CreateJoinTokenAsync(
        LiveClassSession session,
        string displayName,
        string? email,
        int role,
        CancellationToken ct)
    {
        var meetingNumber = session.ZoomMeetingNumber ?? session.ZoomMeetingId?.ToString(CultureInfo.InvariantCulture)
            ?? throw ApiException.ServiceUnavailable("zoom_meeting_not_ready", "Zoom meeting is not ready yet.");
        var now = timeProvider.GetUtcNow();
        var expiresAt = Min(now.AddHours(2), session.ScheduledEndAt.AddMinutes(15));
        var signature = await zoomMeetingService.GenerateMeetingSdkSignatureAsync(meetingNumber, role, expiresAt, ct);
        var sdkKey = await zoomMeetingService.GetMeetingSdkKeyAsync(ct);

        // Hosts (role=1) need a ZAK token to land in the meeting as host.
        // Without ZAK the embedded SDK treats them as a participant even when
        // the signature carries role=1. Best-effort: a null token degrades to
        // participant behaviour, which the existing tests already cover.
        string? zak = null;
        if (role == 1)
        {
            var hostZoomUserId = await ResolveHostZoomUserIdAsync(session, ct);
            if (!string.IsNullOrWhiteSpace(hostZoomUserId))
            {
                try
                {
                    zak = await zoomMeetingService.GetZakTokenAsync(hostZoomUserId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch ZAK token for session {SessionId}", session.Id);
                }
            }
        }

        return new LiveClassJoinTokenResponse(
            "zoom",
            sdkKey,
            signature,
            meetingNumber,
            displayName,
            email,
            role,
            session.ZoomPasscode,
            zak,
            role == 0 ? session.ZoomJoinUrl : session.ZoomStartUrl,
            expiresAt);
    }

    /// <summary>
    /// Resolve the Zoom user id that owns the host slot for this session.
    /// Prefers the per-tutor <c>Tutors.ZoomUserId</c> when the live class is
    /// owned by a Wave A1 Tutor row matched via the underlying tutor profile
    /// expert user id; null falls back to the platform default host.
    /// </summary>
    private async Task<string?> ResolveHostZoomUserIdAsync(LiveClassSession session, CancellationToken ct)
    {
        var expertUserId = session.LiveClass?.TutorProfile?.ExpertUserId;
        if (string.IsNullOrWhiteSpace(expertUserId))
        {
            return null;
        }

        var zoomUserId = await db.Tutors
            .AsNoTracking()
            .Where(tutor => tutor.UserId == expertUserId && tutor.IsActive)
            .Select(tutor => tutor.ZoomUserId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(zoomUserId) ? null : zoomUserId;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;

    private async Task<Guid> DebitWalletForEnrollmentAsync(
        string learnerUserId,
        int amount,
        LiveClassSession session,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == learnerUserId, ct)
            ?? throw ApiException.PaymentRequired("wallet_not_found", "Wallet not found. Add credits before enrolling.");
        if (wallet.CreditBalance < amount)
        {
            throw ApiException.PaymentRequired("insufficient_credits", "You do not have enough credits to enroll in this class.");
        }

        try
        {
            var transaction = await walletService.DebitAsync(
                wallet.Id,
                amount,
                "live_class_debit",
                "live_class",
                session.Id,
                $"Live class enrollment: {session.LiveClass.Title}",
                "system",
                idempotencyKey,
                ct);
            return transaction.Id;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient credits", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.PaymentRequired("insufficient_credits", "You do not have enough credits to enroll in this class.");
        }
    }

    private async Task<Guid> CreditWalletRefundAsync(
        string learnerUserId,
        int amount,
        LiveClassSession session,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == learnerUserId, ct)
            ?? throw ApiException.NotFound("wallet_not_found", "Wallet not found for refund.");
        var transaction = await walletService.CreditAsync(
            wallet.Id,
            amount,
            "refund",
            "live_class",
            session.Id,
            $"Live class refund: {session.LiveClass.Title}",
            "system",
            idempotencyKey,
            ct);
        return transaction.Id;
    }

    private async Task PromoteFromWaitlistAsync(string sessionId, LiveClassSession session, DateTimeOffset now, CancellationToken ct)
    {
        var waitlistEntry = await db.LiveClassWaitlistEntries
            .Where(item => item.ClassSessionId == sessionId && !item.NotifiedOfOpening)
            .OrderBy(item => item.Position)
            .FirstOrDefaultAsync(ct);

        if (waitlistEntry is null)
        {
            return;
        }

        // Check if the promoted learner has sufficient credits; skip them if not.
        var cost = session.LiveClass.CreditCost;
        if (cost > 0)
        {
            var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == waitlistEntry.UserId, ct);
            if (wallet is null || wallet.CreditBalance < cost)
            {
                waitlistEntry.NotifiedOfOpening = true;
                return;
            }
        }

        // Create the enrollment.
        var enrollment = new LiveClassEnrollment
        {
            Id = $"LCE-{Guid.NewGuid():N}",
            ClassSessionId = session.Id,
            UserId = waitlistEntry.UserId,
            EnrolledAt = now,
            CreditsCharged = cost,
            Status = LiveClassEnrollmentStatus.Active,
            IdempotencyKey = $"waitlist-promote:{waitlistEntry.Id}",
        };

        // Deduct credits if required.
        if (cost > 0)
        {
            enrollment.WalletTransactionId = await DebitWalletForEnrollmentAsync(
                waitlistEntry.UserId,
                cost,
                session,
                enrollment.IdempotencyKey,
                now,
                ct);
        }

        db.LiveClassEnrollments.Add(enrollment);
        session.EnrolledCount++;
        session.UpdatedAt = now;

        // Mark this waitlist entry as processed and compact positions for those still waiting.
        var promotedPosition = waitlistEntry.Position;
        waitlistEntry.NotifiedOfOpening = true;

        var remaining = await db.LiveClassWaitlistEntries
            .Where(item => item.ClassSessionId == sessionId && item.Position > promotedPosition && !item.NotifiedOfOpening)
            .ToListAsync(ct);
        foreach (var entry in remaining)
        {
            entry.Position--;
        }

        await NotifyLearnerEnrollmentAsync(enrollment, session, ct);
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

    private async Task QueueSessionReminderJobAsync(LiveClassSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (session.Status != LiveClassSessionStatus.Scheduled || session.ScheduledEndAt <= now)
        {
            return;
        }

        var availableAt = session.ScheduledStartAt.AddMinutes(-30);
        if (availableAt < now)
        {
            availableAt = now;
        }

        var existing = await db.BackgroundJobs.FirstOrDefaultAsync(job =>
            job.Type == JobType.LiveClassSessionReminderDispatch
            && job.ResourceId == session.Id
            && job.State == AsyncState.Queued,
            ct);

        if (existing is not null)
        {
            existing.AvailableAt = availableAt;
            existing.LastTransitionAt = now;
            existing.PayloadJson = JsonSerializer.Serialize(new { sessionId = session.Id }, JsonOptions);
            existing.StatusReasonCode = "queued";
            existing.StatusMessage = "Live class reminder queued.";
            return;
        }

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.LiveClassSessionReminderDispatch,
            State = AsyncState.Queued,
            ResourceId = session.Id,
            PayloadJson = JsonSerializer.Serialize(new { sessionId = session.Id }, JsonOptions),
            StatusReasonCode = "queued",
            StatusMessage = "Live class reminder queued.",
            CreatedAt = now,
            AvailableAt = availableAt,
            LastTransitionAt = now,
        });
    }

    private async Task NotifyLearnerEnrollmentAsync(LiveClassEnrollment enrollment, LiveClassSession session, CancellationToken ct)
    {
        // Legacy generic "class booking confirmed" — kept for non-live-class callers and
        // analytics dashboards that still pivot on this event key.
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

        // Wave A3 — class-specific enrollment confirmation with calendar invite.
        if (classNotifications is not null)
        {
            await classNotifications.SendEnrollmentConfirmedAsync(enrollment, session, ct);
        }
    }

    private async Task NotifyLearnerCancellationAsync(LiveClassEnrollment enrollment, LiveClassSession session, int refundCredits, bool cancelledByTutor, CancellationToken ct)
    {
        // Legacy event — preserves existing dashboards / templates.
        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerLiveClassCancelled,
            enrollment.UserId,
            "live_class_enrollment",
            enrollment.Id,
            enrollment.CancelledAt?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            new Dictionary<string, object?>
            {
                ["classTitle"] = session.LiveClass.Title,
                ["sessionTime"] = session.ScheduledStartAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                ["classId"] = session.LiveClassId,
                ["sessionId"] = session.Id,
                ["refundCredits"] = refundCredits,
                ["reason"] = enrollment.CancellationReason,
            },
            ct);

        if (classNotifications is not null)
        {
            await classNotifications.SendCancellationAsync(enrollment, session, cancelledByTutor, refundCredits, ct);
        }
    }

    private async Task ScheduleEnrollmentReminderCascadeAsync(LiveClassEnrollment enrollment, LiveClassSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (session.Status != LiveClassSessionStatus.Scheduled || session.ScheduledStartAt <= now)
        {
            return;
        }

        foreach (var leadMinutes in ReminderLeadMinutes)
        {
            var availableAt = session.ScheduledStartAt.AddMinutes(-leadMinutes);
            if (availableAt <= now)
            {
                // Already past the lead window — skip this leg of the cascade but keep
                // any later legs (so re-enrollment within the last 24h still gets the
                // 1h and 10min pushes).
                continue;
            }

            var resourceKey = BuildReminderResourceKey(enrollment.Id, leadMinutes);
            var existing = await db.BackgroundJobs.FirstOrDefaultAsync(job =>
                job.Type == JobType.LiveClassSessionReminderDispatch
                && job.ResourceId == resourceKey
                && job.State == AsyncState.Queued,
                ct);

            var payload = JsonSerializer.Serialize(new
            {
                enrollmentId = enrollment.Id,
                sessionId = session.Id,
                leadMinutes,
            }, JsonOptions);

            if (existing is not null)
            {
                existing.AvailableAt = availableAt;
                existing.LastTransitionAt = now;
                existing.PayloadJson = payload;
                existing.StatusReasonCode = "queued";
                existing.StatusMessage = $"Live class T-{leadMinutes} reminder queued.";
                continue;
            }

            db.BackgroundJobs.Add(new BackgroundJobItem
            {
                Id = $"bg-{Guid.NewGuid():N}",
                Type = JobType.LiveClassSessionReminderDispatch,
                State = AsyncState.Queued,
                ResourceId = resourceKey,
                PayloadJson = payload,
                StatusReasonCode = "queued",
                StatusMessage = $"Live class T-{leadMinutes} reminder queued.",
                CreatedAt = now,
                AvailableAt = availableAt,
                LastTransitionAt = now,
            });
        }
    }

    private async Task CancelEnrollmentReminderCascadeAsync(string enrollmentId, CancellationToken ct)
    {
        var keys = ReminderLeadMinutes
            .Select(lead => BuildReminderResourceKey(enrollmentId, lead))
            .ToArray();
        var jobs = await db.BackgroundJobs
            .Where(job => job.Type == JobType.LiveClassSessionReminderDispatch
                && keys.Contains(job.ResourceId!)
                && job.State == AsyncState.Queued)
            .ToListAsync(ct);
        foreach (var job in jobs)
        {
            db.BackgroundJobs.Remove(job);
        }
    }

    private async Task CancelLegacySessionReminderAsync(string sessionId, CancellationToken ct)
    {
        var jobs = await db.BackgroundJobs
            .Where(job => job.Type == JobType.LiveClassSessionReminderDispatch
                && job.ResourceId == sessionId
                && job.State == AsyncState.Queued)
            .ToListAsync(ct);
        foreach (var job in jobs)
        {
            db.BackgroundJobs.Remove(job);
        }
    }

    internal static string BuildReminderResourceKey(string enrollmentId, int leadMinutes)
        => $"{enrollmentId}:T{leadMinutes}";

    private async Task QueueNoShowPingAsync(LiveClassSession session, DateTimeOffset now, CancellationToken ct)
    {
        // Fire ~5 minutes after meeting.started so we have time to receive at least one
        // participant_joined webhook before pinging anyone.
        var availableAt = now.AddMinutes(5);
        var existing = await db.BackgroundJobs.FirstOrDefaultAsync(job =>
            job.Type == JobType.LiveClassNoShowPingDispatch
            && job.ResourceId == session.Id
            && job.State == AsyncState.Queued,
            ct);

        var payload = JsonSerializer.Serialize(new
        {
            sessionId = session.Id,
        }, JsonOptions);

        if (existing is not null)
        {
            existing.AvailableAt = availableAt;
            existing.LastTransitionAt = now;
            existing.PayloadJson = payload;
            existing.StatusReasonCode = "queued";
            existing.StatusMessage = "Live class no-show ping queued.";
            return;
        }

        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.LiveClassNoShowPingDispatch,
            State = AsyncState.Queued,
            ResourceId = session.Id,
            PayloadJson = payload,
            StatusReasonCode = "queued",
            StatusMessage = "Live class no-show ping queued.",
            CreatedAt = now,
            AvailableAt = availableAt,
            LastTransitionAt = now,
        });
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

        // T5 — the same Zoom app posts every meeting event to this single webhook,
        // so dispatch participant join/left to Private Speaking attendance tracking
        // too. It no-ops unless the meeting belongs to a Private Speaking booking,
        // so a Live Class meeting is unaffected (and vice-versa).
        if (privateSpeakingService is not null)
        {
            await privateSpeakingService.ApplyZoomAttendanceWebhookAsync(eventType, root, ct);
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
                await QueueNoShowPingAsync(session, now, ct);
                break;
            case "meeting.ended":
                session.Status = LiveClassSessionStatus.Completed;
                session.ActualEndAt = now;
                if (session.ActualStartAt.HasValue)
                {
                    session.DurationMinutes = Math.Max(0, (int)(now - session.ActualStartAt.Value).TotalMinutes);
                }
                session.UpdatedAt = now;
                await FinalizeAttendanceAsync(session.Id, now, ct);
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
        // Prefer the opaque MP4 recording file id from recording_files; never persist raw Zoom download URLs.
        string? mp4RecordingId = null;
        if (zoomObject.TryGetProperty("recording_files", out var recordingFiles) && recordingFiles.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in recordingFiles.EnumerateArray())
            {
                var fileType = TryReadString(file, "file_type");
                if (string.Equals(fileType, "MP4", StringComparison.OrdinalIgnoreCase))
                {
                    mp4RecordingId = TryReadString(file, "id") ?? TryReadString(file, "recording_id");
                    break;
                }
            }
        }

        var recordingId = mp4RecordingId ?? TryReadString(zoomObject, "uuid") ?? $"zoom-{session.ZoomMeetingId}";
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
            existing.ZoomRecordingId = mp4RecordingId ?? existing.ZoomRecordingId ?? recordingId;
            existing.Status = existing.Status == LiveClassRecordingStatus.Ready ? existing.Status : LiveClassRecordingStatus.Pending;
        }

        // Queue a background job to download the recording from Zoom cloud storage.
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.LiveClassRecordingDownload,
            State = AsyncState.Queued,
            ResourceId = existing.Id,
            PayloadJson = JsonSerializer.Serialize(new { recordingId = existing.Id, sessionId = session.Id }, JsonOptions),
            StatusReasonCode = "queued",
            StatusMessage = "Recording download queued.",
            CreatedAt = now,
            AvailableAt = now,
        });
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
        else if (attendance is not null && joined && attendance.LeftAt.HasValue)
        {
            attendance.JoinedAt = now;
            attendance.LeftAt = null;
            attendance.ZoomParticipantUuid = TryReadString(participant, "user_id") ?? attendance.ZoomParticipantUuid;
        }
        else if (attendance is not null && !joined)
        {
            attendance.LeftAt = now;
            attendance.DurationSeconds += Math.Max(0, (int)(now - attendance.JoinedAt).TotalSeconds);
        }

        if (enrollment is not null
            && session.Status == LiveClassSessionStatus.Completed
            && enrollment.Status == LiveClassEnrollmentStatus.NoShow)
        {
            enrollment.Status = LiveClassEnrollmentStatus.Attended;
            enrollment.CancellationReason = null;
        }
    }

    private async Task FinalizeAttendanceAsync(string sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var openAttendances = await db.LiveClassAttendances
            .Where(attendance => attendance.ClassSessionId == sessionId && attendance.LeftAt == null)
            .ToListAsync(ct);
        foreach (var attendance in openAttendances)
        {
            attendance.LeftAt = now;
            attendance.DurationSeconds += Math.Max(0, (int)(now - attendance.JoinedAt).TotalSeconds);
        }

        var enrollments = await db.LiveClassEnrollments
            .Where(enrollment => enrollment.ClassSessionId == sessionId && enrollment.Status == LiveClassEnrollmentStatus.Active)
            .ToListAsync(ct);
        if (enrollments.Count == 0)
        {
            return;
        }

        var attendedUserIds = await db.LiveClassAttendances
            .Where(attendance => attendance.ClassSessionId == sessionId)
            .Select(attendance => attendance.UserId)
            .ToHashSetAsync(ct);

        foreach (var enrollment in enrollments)
        {
            enrollment.Status = attendedUserIds.Contains(enrollment.UserId)
                ? LiveClassEnrollmentStatus.Attended
                : LiveClassEnrollmentStatus.NoShow;
            if (enrollment.Status == LiveClassEnrollmentStatus.NoShow)
            {
                enrollment.CancellationReason = "Marked no-show after Zoom meeting ended.";
            }
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