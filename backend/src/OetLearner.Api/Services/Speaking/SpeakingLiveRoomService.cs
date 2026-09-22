using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Phase 3 of the OET Speaking module. Coordinates the lifecycle of a
/// <see cref="SpeakingLiveRoom"/> backing a <see cref="SpeakingSession"/>
/// whose <see cref="SpeakingSession.Mode"/> is <c>LiveTutor</c>.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Provision the underlying LiveKit room (via
///         <see cref="ILiveKitGateway"/>) and persist the
///         <c>SpeakingLiveRoom</c> row.</item>
///   <item>Mint short-lived per-participant access tokens. Capabilities
///         are derived from the requested role
///         (<see cref="SpeakingLiveRoomTokenRole"/>) so the gateway
///         cannot be tricked into letting an observer publish media.</item>
///   <item>Start / stop the egress recording, end the room, and append
///         provider webhook events to the room's append-only log.</item>
/// </list>
/// </summary>
public sealed class SpeakingLiveRoomService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly LearnerDbContext _db;
    private readonly ILiveKitGateway _gateway;
    private readonly IOptions<LiveKitOptions> _options;
    private readonly IOptions<SpeakingComplianceOptions> _complianceOptions;
    private readonly ILogger<SpeakingLiveRoomService> _logger;

    public SpeakingLiveRoomService(
        LearnerDbContext db,
        ILiveKitGateway gateway,
        IOptions<LiveKitOptions> options,
        IOptions<SpeakingComplianceOptions> complianceOptions,
        ILogger<SpeakingLiveRoomService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _complianceOptions = complianceOptions ?? throw new ArgumentNullException(nameof(complianceOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─────────────────────────────────────────────────────────────────
    // Room creation
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Provision a live-tutor room for the given session. The
    /// caller MUST be the session's owner (the learner) — interlocutor
    /// assignment is validated at issue-token time.</summary>
    public async Task<SpeakingLiveRoomCreationResult> CreateRoomForSessionAsync(
        string userId,
        string speakingSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
        if (string.IsNullOrWhiteSpace(speakingSessionId)) throw new ArgumentException("speakingSessionId required", nameof(speakingSessionId));

        var session = await _db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == speakingSessionId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Speaking session '{speakingSessionId}' was not found.");

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            throw new SpeakingLiveRoomForbiddenException(
                $"User '{userId}' is not the owner of session '{speakingSessionId}'.");
        }

        if (session.Mode != SpeakingSessionMode.LiveTutor)
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Speaking session '{speakingSessionId}' is not a live-tutor session.");
        }

        if (IsTerminalSessionState(session.State))
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Speaking session '{speakingSessionId}' is in state {session.State} and cannot create a live room.");
        }

        var bookingId = string.IsNullOrWhiteSpace(session.ExamSessionId)
            ? null
            : await _db.SpeakingExamSessions
                .AsNoTracking()
                .Where(exam => exam.Id == session.ExamSessionId)
                .Select(exam => exam.BookingId)
                .FirstOrDefaultAsync(ct);

        // If a room already exists for this session (idempotent retry of
        // the same "Start session" tap), return the existing identifiers
        // rather than provisioning a duplicate.
        var existing = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.SpeakingSessionId == speakingSessionId, ct);
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.BookingId) && !string.IsNullOrWhiteSpace(bookingId))
            {
                existing.BookingId = bookingId;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            _logger.LogInformation(
                "SpeakingLiveRoomService.CreateRoom returning_existing roomId={LiveRoomId} sessionId={SessionId}",
                existing.Id,
                speakingSessionId);
            return new SpeakingLiveRoomCreationResult(existing.Id, _options.Value.WssUrl, existing.RoomName);
        }

        var roomName = $"oet-speaking-{speakingSessionId}";
        var maxDuration = _options.Value.DefaultMaxDurationSeconds;
        var creation = await _gateway.CreateRoomAsync(roomName, maxDuration, ct);

        var now = DateTimeOffset.UtcNow;
        var liveRoomId = $"lvrm_{Guid.NewGuid():N}";
        var learnerIdentity = $"learner:{session.UserId}";
        var tutorIdentity = !string.IsNullOrWhiteSpace(session.InterlocutorActorId)
            ? $"tutor:{session.InterlocutorActorId}"
            : $"tutor:unassigned:{speakingSessionId}";

        var room = new SpeakingLiveRoom
        {
            Id = liveRoomId,
            SpeakingSessionId = speakingSessionId,
            BookingId = bookingId,
            Provider = "livekit",
            RoomName = roomName,
            LearnerIdentity = learnerIdentity,
            TutorIdentity = tutorIdentity,
            LiveKitRoomSid = creation.RoomSid,
            ScheduledStartUtc = session.RolePlayStartedAt ?? now,
            ActualStartUtc = null,
            State = SpeakingLiveRoomState.Active,
            MaxDurationSeconds = maxDuration,
            RecordingEnabled = _options.Value.EgressEnabled,
            RecordingConsentVersion = session.ConsentVersion,
            WebhookEventsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.SpeakingLiveRooms.Add(room);
        session.LiveRoomId = liveRoomId;
        session.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            var persisted = false;
            try
            {
                persisted = await _db.SpeakingLiveRooms
                    .AsNoTracking()
                    .AnyAsync(r => r.Id == liveRoomId, CancellationToken.None);
            }
            catch (Exception probeException)
            {
                _logger.LogWarning(
                    probeException,
                    "SpeakingLiveRoomService.CreateRoom could not verify persistence after failure roomId={LiveRoomId}",
                    liveRoomId);
            }

            if (!persisted)
            {
                try
                {
                    await _gateway.DeleteRoomAsync(roomName, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "SpeakingLiveRoomService.CreateRoom provider cleanup failed roomName={RoomName}",
                        roomName);
                }
            }

            throw;
        }

        _logger.LogInformation(
            "SpeakingLiveRoomService.CreateRoom created roomId={LiveRoomId} sessionId={SessionId} sid={Sid}",
            liveRoomId,
            speakingSessionId,
            creation.RoomSid);

        return new SpeakingLiveRoomCreationResult(liveRoomId, _options.Value.WssUrl, roomName);
    }

    // ─────────────────────────────────────────────────────────────────
    // Token issuance
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Mint a participant token for an existing room. Throws
    /// <see cref="SpeakingLiveRoomForbiddenException"/> when the
    /// requesting user does not match the role they are asking for.</summary>
    public async Task<SpeakingLiveRoomTokenResult> IssueTokenAsync(
        string userId,
        string liveRoomId,
        string roleStr,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var role = ParseRole(roleStr);

        var room = await _db.SpeakingLiveRooms
            .Include(r => r.SpeakingSession)
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

        if (room.State is SpeakingLiveRoomState.Ended or SpeakingLiveRoomState.Failed)
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Live room '{liveRoomId}' is in state {room.State} and cannot issue new tokens.");
        }

        var session = await _db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException(
                $"Speaking session '{room.SpeakingSessionId}' was not found for room '{liveRoomId}'.");

        // Authorisation: a learner can only mint a Learner token for
        // their own session; a tutor (interlocutor) can only mint a
        // Tutor token when they are the assigned interlocutor.
        var expectedIdentity = role switch
        {
            SpeakingLiveRoomTokenRole.Learner => room.LearnerIdentity,
            SpeakingLiveRoomTokenRole.Tutor => room.TutorIdentity,
            SpeakingLiveRoomTokenRole.Observer => $"observer:{userId}",
            _ => throw new SpeakingLiveRoomInvalidStateException($"Unsupported role '{roleStr}'."),
        };

        switch (role)
        {
            case SpeakingLiveRoomTokenRole.Learner:
                if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
                {
                    throw new SpeakingLiveRoomForbiddenException(
                        $"User '{userId}' is not the learner for room '{liveRoomId}'.");
                }
                break;
            case SpeakingLiveRoomTokenRole.Tutor:
                if (string.IsNullOrWhiteSpace(session.InterlocutorActorId)
                    || !string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal))
                {
                    throw new SpeakingLiveRoomForbiddenException(
                        $"User '{userId}' is not the assigned tutor for room '{liveRoomId}'.");
                }
                break;
            case SpeakingLiveRoomTokenRole.Observer:
                // Observer access is allowed for any authenticated user
                // already on the room (e.g. an admin reviewing live).
                // Tighten this if the platform later adds observer ACLs.
                break;
        }

        await EnsureParticipantConsentAsync(userId, role, ct);

        var capabilities = role switch
        {
            SpeakingLiveRoomTokenRole.Learner => new LiveKitTokenCapabilities(
                CanPublishAudio: true,
                CanPublishVideo: true,
                CanSubscribe: true,
                CanManageRoom: false),
            SpeakingLiveRoomTokenRole.Tutor => new LiveKitTokenCapabilities(
                CanPublishAudio: true,
                CanPublishVideo: true,
                CanSubscribe: true,
                CanManageRoom: true),
            SpeakingLiveRoomTokenRole.Observer => new LiveKitTokenCapabilities(
                CanPublishAudio: false,
                CanPublishVideo: false,
                CanSubscribe: true),
            _ => throw new SpeakingLiveRoomInvalidStateException($"Unsupported role '{roleStr}'."),
        };

        var ttl = TimeSpan.FromHours(1);
        var token = await _gateway.MintAccessTokenAsync(room.RoomName, expectedIdentity, capabilities, ttl, ct);

        var now = DateTimeOffset.UtcNow;
        room.ActualStartUtc ??= now;
        if (!string.IsNullOrWhiteSpace(room.BookingId))
        {
            var booking = await _db.PrivateSpeakingBookings
                .FirstOrDefaultAsync(item => item.Id == room.BookingId, ct);
            if (booking is not null
                && booking.Status is (PrivateSpeakingBookingStatus.Confirmed
                    or PrivateSpeakingBookingStatus.ZoomCreated))
            {
                booking.Status = PrivateSpeakingBookingStatus.InProgress;
                booking.UpdatedAt = now;
            }
            await SyncMockBookingStateAsync(room.BookingId, MockBookingStatuses.InProgress, MockLiveRoomStates.InProgress, now, ct);
        }
        var record = new SpeakingLiveRoomToken
        {
            Id = $"lvrt_{Guid.NewGuid():N}",
            LiveRoomId = room.Id,
            Identity = expectedIdentity,
            IssuedAt = now,
            ExpiresAt = now.Add(ttl),
            Role = role,
            Capabilities = SerialiseCapabilities(capabilities),
        };
        _db.SpeakingLiveRoomTokens.Add(record);
        room.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SpeakingLiveRoomService.IssueToken roomId={LiveRoomId} role={Role} identity={Identity}",
            liveRoomId,
            role,
            expectedIdentity);

        return new SpeakingLiveRoomTokenResult(
            Token: token,
            ExpiresAt: record.ExpiresAt,
            Capabilities: capabilities);
    }

    private async Task EnsureParticipantConsentAsync(
        string userId,
        SpeakingLiveRoomTokenRole role,
        CancellationToken ct)
    {
        if (role == SpeakingLiveRoomTokenRole.Observer)
        {
            return;
        }

        var currentRecordingVersion = _complianceOptions.Value.CurrentConsentVersion;
        var currentLiveVideoVersion = _complianceOptions.Value.CurrentLiveVideoConsentVersion;
        var required = new[]
        {
            (SpeakingComplianceConsentTypes.Recording, currentRecordingVersion),
            (SpeakingComplianceConsentTypes.TutorReview, currentRecordingVersion),
            (SpeakingComplianceConsentTypes.Retention, currentRecordingVersion),
            (SpeakingComplianceConsentTypes.LiveVideoWithTutor, currentLiveVideoVersion),
        };

        foreach (var (consentType, consentVersion) in required)
        {
            var accepted = await _db.SpeakingComplianceConsents
                .AsNoTracking()
                .AnyAsync(consent => consent.UserId == userId
                    && consent.RevokedAt == null
                    && consent.ConsentType == consentType
                    && consent.ConsentVersion == consentVersion, ct);
            if (accepted)
            {
                continue;
            }

            throw new SpeakingLiveRoomInvalidStateException(
                $"The {role.ToString().ToLowerInvariant()} must accept current live-room consent before joining.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Recording lifecycle
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpeakingLiveRoomRecordingResult> StartRecordingAsync(string liveRoomId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var room = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

        if (room.State is SpeakingLiveRoomState.Ended or SpeakingLiveRoomState.Failed)
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Live room '{liveRoomId}' is in state {room.State} and cannot start recording.");
        }

        if (!room.RecordingEnabled)
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Live room '{liveRoomId}' is not configured for recording.");
        }

        var session = await _db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct);
        if (session is null || string.IsNullOrWhiteSpace(session.UserId))
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Live room '{liveRoomId}' is missing learner session context.");
        }

        var learnerId = session.UserId;
        room.ActualStartUtc ??= DateTimeOffset.UtcNow;

        var recordingConsentVersion = _complianceOptions.Value.CurrentConsentVersion;
        var liveVideoConsentVersion = _complianceOptions.Value.CurrentLiveVideoConsentVersion;
        var hasRequiredConsent = await _db.SpeakingComplianceConsents.AsNoTracking()
            .AnyAsync(c => c.UserId == learnerId
                           && c.RevokedAt == null
                           && c.ConsentType == SpeakingComplianceConsentTypes.Recording
                           && c.ConsentVersion == recordingConsentVersion, ct)
            && await _db.SpeakingComplianceConsents.AsNoTracking()
                .AnyAsync(c => c.UserId == learnerId
                               && c.RevokedAt == null
                               && c.ConsentType == SpeakingComplianceConsentTypes.LiveVideoWithTutor
                               && c.ConsentVersion == liveVideoConsentVersion, ct);
        if (!hasRequiredConsent)
        {
            throw new SpeakingLiveRoomInvalidStateException(
                $"Live room '{liveRoomId}' requires current learner recording and live-video consent before recording can start.");
        }

        if (!string.IsNullOrWhiteSpace(room.EgressId))
        {
            _logger.LogInformation(
                "SpeakingLiveRoomService.StartRecording already_recording roomId={LiveRoomId} egressId={EgressId}",
                liveRoomId,
                room.EgressId);
            return new SpeakingLiveRoomRecordingResult(room.EgressId, room.EgressOutputUrl ?? string.Empty);
        }

        var bucket = _options.Value.EgressBucket;
        var outputUrl = string.IsNullOrWhiteSpace(bucket)
            ? $"livekit://egress/{room.RoomName}.mp4"
            : $"{(bucket.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) ? bucket.TrimEnd('/') : $"s3://{bucket.TrimEnd('/')}")}/oet-speaking/{room.RoomName}.mp4";

        var egressId = await _gateway.StartEgressAsync(room.RoomName, outputUrl, ct);

        room.EgressId = egressId;
        room.EgressOutputUrl = outputUrl;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            string? persistedEgressId = null;
            try
            {
                persistedEgressId = await _db.SpeakingLiveRooms
                    .AsNoTracking()
                    .Where(r => r.Id == room.Id)
                    .Select(r => r.EgressId)
                    .FirstOrDefaultAsync(CancellationToken.None);
            }
            catch (Exception probeException)
            {
                _logger.LogWarning(
                    probeException,
                    "SpeakingLiveRoomService.StartRecording could not verify persistence after failure roomId={LiveRoomId}",
                    liveRoomId);
            }

            if (string.IsNullOrWhiteSpace(persistedEgressId))
            {
                try
                {
                    await _gateway.StopEgressAsync(egressId, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "SpeakingLiveRoomService.StartRecording provider cleanup failed egressId={EgressId}",
                        egressId);
                }
            }

            throw;
        }

        _logger.LogInformation(
            "SpeakingLiveRoomService.StartRecording started roomId={LiveRoomId} egressId={EgressId}",
            liveRoomId,
            egressId);

        return new SpeakingLiveRoomRecordingResult(egressId, outputUrl);
    }

    public async Task<bool> StopRecordingAsync(string liveRoomId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var room = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

        if (string.IsNullOrWhiteSpace(room.EgressId))
        {
            return false;
        }

        var stopped = await _gateway.StopEgressAsync(room.EgressId, ct);
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SpeakingLiveRoomService.StopRecording stopped roomId={LiveRoomId} egressId={EgressId} result={Stopped}",
            liveRoomId,
            room.EgressId,
            stopped);

        return stopped;
    }

    // ─────────────────────────────────────────────────────────────────
    // End-of-room
    // ─────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────
    // Booking → room glue (P6 task 4)
    //
    // Spec: when a MockBooking confirms a SubtestCode == "speaking"
    // session, the live room should be provisioned at
    // (ScheduledStartAt - 5min) via a background job, and torn down at
    // (ScheduledStartAt + DefaultMaxDurationSeconds).
    //
    // The hosted lifecycle worker calls this method shortly before the
    // scheduled booking and on every retry until the room is available.
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpeakingLiveRoomCreationResult?> ProvisionForBookingAsync(
        string bookingId,
        string learnerUserId,
        string speakingSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bookingId)) throw new ArgumentException("bookingId required", nameof(bookingId));
        if (string.IsNullOrWhiteSpace(learnerUserId)) throw new ArgumentException("learnerUserId required", nameof(learnerUserId));
        if (string.IsNullOrWhiteSpace(speakingSessionId)) throw new ArgumentException("speakingSessionId required", nameof(speakingSessionId));

        var booking = await _db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Booking '{bookingId}' was not found.");
        if (!string.Equals(booking.LearnerUserId, learnerUserId, StringComparison.Ordinal))
        {
            throw new SpeakingLiveRoomForbiddenException(
                $"Booking '{bookingId}' does not belong to learner '{learnerUserId}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var maxDurationSeconds = Math.Max(60, booking.DurationMinutes * 60);

        // Skip if a room already exists for this booking (idempotent).
        var existing = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);
        if (existing is not null)
        {
            existing.ScheduledStartUtc = booking.SessionStartUtc;
            existing.MaxDurationSeconds = maxDurationSeconds;
            existing.UpdatedAt = now;
            if (now >= booking.SessionStartUtc
                && now < booking.SessionStartUtc.AddMinutes(Math.Max(1, booking.DurationMinutes))
                && (booking.Status is PrivateSpeakingBookingStatus.Confirmed or PrivateSpeakingBookingStatus.ZoomCreated))
            {
                booking.Status = PrivateSpeakingBookingStatus.InProgress;
                booking.UpdatedAt = now;
                await SyncMockBookingStateAsync(booking.Id, MockBookingStatuses.InProgress, MockLiveRoomStates.InProgress, now, ct);
                _db.PrivateSpeakingAuditLogs.Add(new PrivateSpeakingAuditLog
                {
                    Id = $"psaudit_{Guid.NewGuid():N}",
                    BookingId = booking.Id,
                    ActorId = "system",
                    ActorRole = "system",
                    Action = "livekit_room_join_window_started",
                    Details = JsonSerializer.Serialize(new { roomId = existing.Id, roomName = existing.RoomName }),
                    CreatedAt = now,
                });
            }
            await _db.SaveChangesAsync(ct);
            return new SpeakingLiveRoomCreationResult(existing.Id, _options.Value.WssUrl, existing.RoomName);
        }

        var result = await CreateRoomForSessionAsync(learnerUserId, speakingSessionId, ct);

        // Stamp the room with the booking id so a follow-up scheduler
        // tick can find rooms it provisioned.
        var room = await _db.SpeakingLiveRooms.FirstAsync(r => r.Id == result.LiveRoomId, ct);
        room.BookingId = bookingId;
        room.ScheduledStartUtc = booking.SessionStartUtc;
        room.MaxDurationSeconds = maxDurationSeconds;
        room.UpdatedAt = now;
        if (now >= booking.SessionStartUtc
            && now < booking.SessionStartUtc.AddMinutes(Math.Max(1, booking.DurationMinutes))
            && (booking.Status is PrivateSpeakingBookingStatus.Confirmed or PrivateSpeakingBookingStatus.ZoomCreated))
        {
            booking.Status = PrivateSpeakingBookingStatus.InProgress;
            booking.UpdatedAt = now;
            await SyncMockBookingStateAsync(booking.Id, MockBookingStatuses.InProgress, MockLiveRoomStates.InProgress, now, ct);
            _db.PrivateSpeakingAuditLogs.Add(new PrivateSpeakingAuditLog
            {
                Id = $"psaudit_{Guid.NewGuid():N}",
                BookingId = booking.Id,
                ActorId = "system",
                ActorRole = "system",
                Action = "livekit_room_provisioned",
                Details = JsonSerializer.Serialize(new { roomId = room.Id, roomName = room.RoomName }),
                CreatedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SpeakingLiveRoomService.ProvisionForBooking bookingId={BookingId} roomId={LiveRoomId}",
            bookingId,
            room.Id);

        return result;
    }

    /// <summary>
    /// Tears down rooms that have outlived their scheduled or actual
    /// start plus the configured duration cap. The hosted lifecycle worker
    /// calls this on every sweep so provider cleanup and session completion
    /// remain idempotent.
    /// </summary>
    public async Task TearDownExpiredRoomsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _db.SpeakingLiveRooms
            .Where(r => r.State == SpeakingLiveRoomState.Active)
            .ToListAsync(ct);

        foreach (var room in candidates)
        {
            var cap = (room.ActualStartUtc ?? room.ScheduledStartUtc).AddSeconds(room.MaxDurationSeconds);
            if (now < cap) continue;

            _logger.LogInformation(
                "SpeakingLiveRoomService.TearDownExpired roomId={LiveRoomId} startedAt={Started} cap={Cap}",
                room.Id,
                room.ActualStartUtc,
                cap);

            try
            {
                if (!string.IsNullOrWhiteSpace(room.EgressId))
                {
                    await _gateway.StopEgressAsync(room.EgressId, ct);
                }
                await _gateway.DeleteRoomAsync(room.RoomName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SpeakingLiveRoomService.TearDownExpired provider_cleanup_failed roomId={LiveRoomId}",
                    room.Id);
                continue;
            }

            room.State = SpeakingLiveRoomState.Ended;
            room.ActualEndUtc = now;
            room.UpdatedAt = now;
            var session = await _db.SpeakingSessions
                .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct);
            if (session is not null && session.State != SpeakingSessionState.Finished)
            {
                session.State = SpeakingSessionState.Finished;
                session.EndedAt ??= now;
                session.UpdatedAt = now;
            }
            if (!string.IsNullOrWhiteSpace(room.BookingId))
            {
                var booking = await _db.PrivateSpeakingBookings
                    .FirstOrDefaultAsync(b => b.Id == room.BookingId, ct);
                if (booking is not null
                    && booking.Status == PrivateSpeakingBookingStatus.InProgress
                    && booking.AttendanceVerified)
                {
                    booking.Status = PrivateSpeakingBookingStatus.Completed;
                    booking.CompletedAt ??= now;
                    booking.UpdatedAt = now;
                    await SyncMockBookingStateAsync(booking.Id, MockBookingStatuses.Completed, MockLiveRoomStates.Completed, now, ct);
                }
            }
        }

        if (candidates.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task EndRoomAsync(string userId, string liveRoomId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var room = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

        var session = await _db.SpeakingSessions
            .FirstOrDefaultAsync(s => s.Id == room.SpeakingSessionId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException(
                $"Speaking session '{room.SpeakingSessionId}' was not found.");

        var isOwner = string.Equals(session.UserId, userId, StringComparison.Ordinal);
        var isAssignedTutor = !string.IsNullOrWhiteSpace(session.InterlocutorActorId)
            && string.Equals(session.InterlocutorActorId, userId, StringComparison.Ordinal);

        if (!isOwner && !isAssignedTutor)
        {
            throw new SpeakingLiveRoomForbiddenException(
                $"User '{userId}' may not end live room '{liveRoomId}'.");
        }

        if (room.State == SpeakingLiveRoomState.Ended)
        {
            return;
        }

        var endedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(room.EgressId))
        {
            await _gateway.StopEgressAsync(room.EgressId, ct);
        }
        await _gateway.DeleteRoomAsync(room.RoomName, ct);
        room.State = SpeakingLiveRoomState.Ended;
        room.ActualEndUtc = endedAt;
        room.UpdatedAt = endedAt;

        if (session.State != SpeakingSessionState.Finished)
        {
            session.State = SpeakingSessionState.Finished;
            session.EndedAt = endedAt;
            session.UpdatedAt = endedAt;
            if (session.RolePlayStartedAt is not null)
            {
                session.ElapsedSeconds = (int)Math.Max(0, (endedAt - session.RolePlayStartedAt.Value).TotalSeconds);
            }
        }
        if (!string.IsNullOrWhiteSpace(room.BookingId))
        {
            var booking = await _db.PrivateSpeakingBookings
                .FirstOrDefaultAsync(b => b.Id == room.BookingId, ct);
        if (booking is not null
            && booking.AttendanceVerified
            && (booking.Status is PrivateSpeakingBookingStatus.Confirmed
                or PrivateSpeakingBookingStatus.ZoomCreated
                or PrivateSpeakingBookingStatus.InProgress))
            {
                booking.Status = PrivateSpeakingBookingStatus.Completed;
                booking.CompletedAt ??= endedAt;
                booking.UpdatedAt = endedAt;
                await SyncMockBookingStateAsync(booking.Id, MockBookingStatuses.Completed, MockLiveRoomStates.Completed, endedAt, ct);
                _db.PrivateSpeakingAuditLogs.Add(new PrivateSpeakingAuditLog
                {
                    Id = $"psaudit_{Guid.NewGuid():N}",
                    BookingId = booking.Id,
                    ActorId = userId,
                    ActorRole = isAssignedTutor ? "tutor" : "learner",
                    Action = "livekit_room_completed",
                    Details = JsonSerializer.Serialize(new { roomId = room.Id, endedAt }),
                    CreatedAt = endedAt,
                });
            }
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SpeakingLiveRoomService.EndRoom roomId={LiveRoomId} endedBy={UserId}",
            liveRoomId,
            userId);
    }

    private async Task SyncMockBookingStateAsync(
        string bookingId,
        string status,
        string liveRoomState,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var booking = await _db.MockBookings
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct);
        if (booking is null) return;

        booking.Status = status;
        booking.LiveRoomState = liveRoomState;
        booking.UpdatedAt = now;
        if (status == MockBookingStatuses.Completed)
        {
            booking.CompletedAt ??= now;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Webhook handler
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Append a verified provider webhook event to the room's
    /// <c>WebhookEventsJson</c> log and react to lifecycle events:
    /// <list type="bullet">
    ///   <item><c>recording_finished</c> → create a
    ///         <see cref="SpeakingRecording"/> row referencing the
    ///         egress output URL.</item>
    /// </list>
    /// The caller is expected to perform signature verification BEFORE
    /// invoking this method; see <see cref="ILiveKitGateway.VerifyWebhookSignature"/>.
    /// </summary>
    public async Task HandleWebhookAsync(string eventType, string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("eventType required", nameof(eventType));
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        JsonDocument? doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            _logger.LogWarning(
                "SpeakingLiveRoomService.HandleWebhook invalid_json eventType={EventType}",
                eventType);
            return;
        }

        try
        {
            var roomName = ExtractRoomName(doc.RootElement);
            if (string.IsNullOrWhiteSpace(roomName))
            {
                _logger.LogInformation(
                    "SpeakingLiveRoomService.HandleWebhook no_room_name eventType={EventType}",
                    eventType);
                return;
            }

            var room = await _db.SpeakingLiveRooms
                .FirstOrDefaultAsync(r => r.RoomName == roomName, ct);
            if (room is null)
            {
                _logger.LogInformation(
                    "SpeakingLiveRoomService.HandleWebhook unknown_room eventType={EventType} roomName={RoomName}",
                    eventType,
                    roomName);
                return;
            }

            AppendWebhookEvent(room, eventType, payload);

            switch (eventType)
            {
                case "recording_finished":
                case "egress_ended":
                    await HandleRecordingFinishedAsync(room, doc.RootElement, ct);
                    break;
                case "participant_joined":
                    await HandleParticipantPresenceAsync(room, doc.RootElement, joined: true, ct);
                    break;
                case "participant_left":
                    await HandleParticipantPresenceAsync(room, doc.RootElement, joined: false, ct);
                    break;
                case "room_finished":
                    await HandleProviderRoomFinishedAsync(room, ct);
                    break;
            }

            room.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            doc.Dispose();
        }
    }

    private async Task HandleRecordingFinishedAsync(SpeakingLiveRoom room, JsonElement payload, CancellationToken ct)
    {
        var egressInfo = TryGetObject(payload, "egressInfo", "egress_info") ?? payload;
        var fileResult = TryGetFirstObject(egressInfo, "fileResults", "file_results");

        var egressId = TryGetString(egressInfo, "egressId", "egress_id")
            ?? room.EgressId;

        var outputUrl = TryGetString(fileResult, "location", "outputUrl", "output_url")
            ?? TryGetString(egressInfo, "outputUrl", "output_url")
            ?? room.EgressOutputUrl
            ?? string.Empty;

        var durationNanoseconds = TryGetInt64(fileResult, "duration");
        var durationSeconds = durationNanoseconds is > 0
            ? (int)Math.Clamp(Math.Round(durationNanoseconds.Value / 1_000_000_000d), 0, int.MaxValue)
            : TryGetInt32(egressInfo, "durationSeconds", "duration_seconds") ?? 0;

        var sizeBytes = TryGetInt64(fileResult, "size", "sizeBytes", "size_bytes")
            ?? TryGetInt64(egressInfo, "sizeBytes", "size_bytes")
            ?? 0L;

        var effectiveEgressId = egressId ?? room.EgressId;
        if (!string.IsNullOrWhiteSpace(effectiveEgressId)
            && await _db.SpeakingRecordings.AsNoTracking().AnyAsync(
                recording => recording.SpeakingSessionId == room.SpeakingSessionId
                    && recording.Source == SpeakingRecordingSource.LiveKitEgress
                    && recording.EgressTrackId == effectiveEgressId,
                ct))
        {
            _logger.LogInformation(
                "SpeakingLiveRoomService.HandleWebhook duplicate_recording roomId={LiveRoomId} egressId={EgressId}",
                room.Id,
                effectiveEgressId);
            return;
        }

        // Reuse the existing MediaAsset pipeline. The readiness worker marks
        // this row playable only after the configured storage provider can
        // see the object written by LiveKit Egress.
        var now = DateTimeOffset.UtcNow;
        var mediaAssetId = $"masset_{Guid.NewGuid():N}";
        _db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = $"{room.RoomName}.mp4",
            MimeType = "video/mp4",
            Format = "mp4",
            SizeBytes = sizeBytes,
            DurationSeconds = durationSeconds > 0 ? durationSeconds : null,
            StoragePath = outputUrl,
            Status = MediaAssetStatus.Processing,
            MediaKind = "video",
            UploadedAt = now,
        });

        _db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = $"rec_{Guid.NewGuid():N}",
            SpeakingSessionId = room.SpeakingSessionId,
            MediaAssetId = mediaAssetId,
            Kind = SpeakingRecordingKind.Mixed,
            Source = SpeakingRecordingSource.LiveKitEgress,
            DurationSeconds = durationSeconds,
            SizeBytes = sizeBytes,
            Sha256 = string.Empty,
            MimeType = "video/mp4",
            ConsentVersion = room.RecordingConsentVersion,
            IsArchived = false,
            EgressTrackId = effectiveEgressId,
            CreatedAt = now,
        });

        _logger.LogInformation(
            "SpeakingLiveRoomService.HandleWebhook recording_finished roomId={LiveRoomId} egressId={EgressId} duration={DurationSeconds}",
            room.Id,
            egressId,
            durationSeconds);
    }

    private async Task HandleParticipantPresenceAsync(
        SpeakingLiveRoom room,
        JsonElement payload,
        bool joined,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(room.BookingId)) return;

        var participant = TryGetObject(payload, "participant");
        var identity = TryGetString(participant, "identity");
        if (!string.Equals(identity, room.LearnerIdentity, StringComparison.Ordinal)) return;

        var booking = await _db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.Id == room.BookingId, ct);
        if (booking is null) return;

        var now = DateTimeOffset.UtcNow;
        if (joined)
        {
            booking.AttendanceJoinedAt ??= now;
            booking.AttendanceVerified = true;
        }
        else
        {
            booking.AttendanceLeftAt = now;
        }
        booking.UpdatedAt = now;
    }

    private async Task HandleProviderRoomFinishedAsync(
        SpeakingLiveRoom room,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        room.State = SpeakingLiveRoomState.Ended;
        room.ActualEndUtc ??= now;

        var session = await _db.SpeakingSessions
            .FirstOrDefaultAsync(item => item.Id == room.SpeakingSessionId, ct);
        if (session is not null && session.State != SpeakingSessionState.Finished)
        {
            session.State = SpeakingSessionState.Finished;
            session.EndedAt ??= now;
            session.UpdatedAt = now;
        }

        if (string.IsNullOrWhiteSpace(room.BookingId)) return;

        var booking = await _db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.Id == room.BookingId, ct);
        if (booking is null || !booking.AttendanceVerified) return;
        if (booking.Status is not (PrivateSpeakingBookingStatus.Confirmed
            or PrivateSpeakingBookingStatus.ZoomCreated
            or PrivateSpeakingBookingStatus.InProgress)) return;

        booking.Status = PrivateSpeakingBookingStatus.Completed;
        booking.CompletedAt ??= now;
        booking.UpdatedAt = now;
        await SyncMockBookingStateAsync(booking.Id, MockBookingStatuses.Completed, MockLiveRoomStates.Completed, now, ct);
    }

    private void AppendWebhookEvent(SpeakingLiveRoom room, string eventType, string payload)
    {
        var existing = SafeParseList(room.WebhookEventsJson);
        existing.Add(new WebhookEventEntry(
            EventType: eventType,
            ReceivedAt: DateTimeOffset.UtcNow,
            PayloadJson: payload));
        room.WebhookEventsJson = JsonSerializer.Serialize(existing, JsonOpts);
    }

    private static List<WebhookEventEntry> SafeParseList(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return new List<WebhookEventEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<WebhookEventEntry>>(json, JsonOpts)
                ?? new List<WebhookEventEntry>();
        }
        catch (JsonException)
        {
            return new List<WebhookEventEntry>();
        }
    }

    private static string? ExtractRoomName(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (payload.TryGetProperty("room", out var room))
        {
            if (room.ValueKind == JsonValueKind.String)
            {
                return room.GetString();
            }

            if (room.ValueKind == JsonValueKind.Object && room.TryGetProperty("name", out var roomName))
            {
                return roomName.GetString();
            }
        }

        var roomName = TryGetString(payload, "roomName", "room_name");
        if (!string.IsNullOrWhiteSpace(roomName)) return roomName;

        var egressInfo = TryGetObject(payload, "egressInfo", "egress_info");
        return TryGetString(egressInfo, "roomName", "room_name");
    }

    private static JsonElement? TryGetObject(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Object)
            {
                return prop;
            }
        }
        return null;
    }

    private static JsonElement? TryGetFirstObject(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object) return item;
            }
        }
        return null;
    }

    private static string? TryGetString(JsonElement? el, params string[] names)
    {
        if (el is null || el.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (el.Value.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private static int? TryGetInt32(JsonElement? el, params string[] names)
    {
        var value = TryGetInt64(el, names);
        if (value is null || value.Value < int.MinValue || value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static long? TryGetInt64(JsonElement? el, params string[] names)
    {
        if (el is null || el.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (el.Value.TryGetProperty(name, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                && prop.TryGetInt64(out var value))
            {
                return value;
            }
        }
        return null;
    }

    private static bool IsTerminalSessionState(SpeakingSessionState state) =>
        state is SpeakingSessionState.Finished or SpeakingSessionState.Cancelled or SpeakingSessionState.Expired;

    private static SpeakingLiveRoomTokenRole ParseRole(string roleStr) => roleStr?.Trim().ToLowerInvariant() switch
    {
        "learner" => SpeakingLiveRoomTokenRole.Learner,
        "tutor" => SpeakingLiveRoomTokenRole.Tutor,
        "observer" => SpeakingLiveRoomTokenRole.Observer,
        _ => throw new SpeakingLiveRoomInvalidStateException(
            $"Unknown role '{roleStr}'. Expected 'learner', 'tutor', or 'observer'."),
    };

    private static string SerialiseCapabilities(LiveKitTokenCapabilities caps)
    {
        var parts = new List<string>();
        if (caps.CanSubscribe) parts.Add("subscribe");
        if (caps.CanPublishAudio) parts.Add("publish_audio");
        if (caps.CanPublishVideo) parts.Add("publish_video");
        return string.Join(',', parts);
    }

    private sealed record WebhookEventEntry(string EventType, DateTimeOffset ReceivedAt, string PayloadJson);
}

// ─────────────────────────────────────────────────────────────────────
// Result + exception types
// ─────────────────────────────────────────────────────────────────────

public sealed record SpeakingLiveRoomCreationResult(string LiveRoomId, string LivekitWssUrl, string RoomName);

public sealed record SpeakingLiveRoomTokenResult(
    string Token,
    DateTimeOffset ExpiresAt,
    LiveKitTokenCapabilities Capabilities);

public sealed record SpeakingLiveRoomRecordingResult(string EgressId, string OutputUrl);

public class SpeakingLiveRoomNotFoundException : Exception
{
    public SpeakingLiveRoomNotFoundException(string message) : base(message) { }
}

public class SpeakingLiveRoomForbiddenException : Exception
{
    public SpeakingLiveRoomForbiddenException(string message) : base(message) { }
}

public class SpeakingLiveRoomInvalidStateException : Exception
{
    public SpeakingLiveRoomInvalidStateException(string message) : base(message) { }
}
