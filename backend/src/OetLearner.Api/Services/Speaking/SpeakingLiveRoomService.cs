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
    private readonly ILogger<SpeakingLiveRoomService> _logger;

    public SpeakingLiveRoomService(
        LearnerDbContext db,
        ILiveKitGateway gateway,
        IOptions<LiveKitOptions> options,
        ILogger<SpeakingLiveRoomService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

        // If a room already exists for this session (idempotent retry of
        // the same "Start session" tap), return the existing identifiers
        // rather than provisioning a duplicate.
        var existing = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.SpeakingSessionId == speakingSessionId, ct);
        if (existing is not null)
        {
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
            Provider = "livekit",
            RoomName = roomName,
            LearnerIdentity = learnerIdentity,
            TutorIdentity = tutorIdentity,
            LiveKitRoomSid = creation.RoomSid,
            ScheduledStartUtc = session.RolePlayStartedAt ?? now,
            ActualStartUtc = now,
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

        await _db.SaveChangesAsync(ct);

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

        var capabilities = role switch
        {
            SpeakingLiveRoomTokenRole.Learner => new LiveKitTokenCapabilities(
                CanPublishAudio: true,
                CanPublishVideo: false,
                CanSubscribe: true),
            SpeakingLiveRoomTokenRole.Tutor => new LiveKitTokenCapabilities(
                CanPublishAudio: true,
                CanPublishVideo: true,
                CanSubscribe: true),
            SpeakingLiveRoomTokenRole.Observer => new LiveKitTokenCapabilities(
                CanPublishAudio: false,
                CanPublishVideo: false,
                CanSubscribe: true),
            _ => throw new SpeakingLiveRoomInvalidStateException($"Unsupported role '{roleStr}'."),
        };

        var ttl = TimeSpan.FromHours(1);
        var token = await _gateway.MintAccessTokenAsync(room.RoomName, expectedIdentity, capabilities, ttl, ct);

        var now = DateTimeOffset.UtcNow;
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

    // ─────────────────────────────────────────────────────────────────
    // Recording lifecycle
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpeakingLiveRoomRecordingResult> StartRecordingAsync(string liveRoomId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var room = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

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
            : $"{bucket.TrimEnd('/')}/oet-speaking/{room.RoomName}.mp4";

        var egressId = await _gateway.StartEgressAsync(room.RoomName, outputUrl, ct);

        room.EgressId = egressId;
        room.EgressOutputUrl = outputUrl;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

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

    public async Task EndRoomAsync(string userId, string liveRoomId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
        if (string.IsNullOrWhiteSpace(liveRoomId)) throw new ArgumentException("liveRoomId required", nameof(liveRoomId));

        var room = await _db.SpeakingLiveRooms
            .FirstOrDefaultAsync(r => r.Id == liveRoomId, ct)
            ?? throw new SpeakingLiveRoomNotFoundException($"Live room '{liveRoomId}' was not found.");

        var session = await _db.SpeakingSessions
            .AsNoTracking()
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

        room.State = SpeakingLiveRoomState.Ended;
        room.ActualEndUtc = DateTimeOffset.UtcNow;
        room.UpdatedAt = room.ActualEndUtc.Value;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SpeakingLiveRoomService.EndRoom roomId={LiveRoomId} endedBy={UserId}",
            liveRoomId,
            userId);
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
                case "room_finished":
                    if (room.State != SpeakingLiveRoomState.Ended)
                    {
                        room.State = SpeakingLiveRoomState.Ended;
                        room.ActualEndUtc ??= DateTimeOffset.UtcNow;
                    }
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
        var egressId = TryGetString(payload, "egressId")
            ?? TryGetString(payload, "egress_id")
            ?? room.EgressId;

        var outputUrl = TryGetString(payload, "outputUrl")
            ?? TryGetString(payload, "output_url")
            ?? room.EgressOutputUrl
            ?? string.Empty;

        var durationSeconds = TryGetInt32(payload, "durationSeconds")
            ?? TryGetInt32(payload, "duration_seconds")
            ?? 0;

        var sizeBytes = TryGetInt64(payload, "sizeBytes")
            ?? TryGetInt64(payload, "size_bytes")
            ?? 0L;

        // Reuse the existing MediaAsset pipeline: create a placeholder
        // row that the SpeakingAudioRetentionWorker / playback flow can
        // resolve. A follow-up job downloads the actual blob into the
        // platform storage backend.
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
            EgressTrackId = egressId,
            CreatedAt = now,
        });

        _logger.LogInformation(
            "SpeakingLiveRoomService.HandleWebhook recording_finished roomId={LiveRoomId} egressId={EgressId} duration={DurationSeconds}",
            room.Id,
            egressId,
            durationSeconds);
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

        return TryGetString(payload, "roomName")
            ?? TryGetString(payload, "room_name");
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static int? TryGetInt32(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v) ? v : null;
    }

    private static long? TryGetInt64(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v) ? v : null;
    }

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
