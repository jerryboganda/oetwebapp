using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Records granular Writing attempt events (spec §17.7, table
/// writing_attempt_events). Ingestion is defensive: a single malformed event is
/// skipped rather than failing the whole batch, batch size and payload length
/// are capped, and unknown event types are ignored (not counted).
/// </summary>
public interface IWritingAttemptEventService
{
    Task<int> RecordAsync(string userId, IReadOnlyList<WritingAttemptEventInput> events, CancellationToken ct);
}

/// <summary>Normalised input for a single Writing attempt event.</summary>
public sealed record WritingAttemptEventInput(
    string EventType,
    DateTimeOffset? Timestamp,
    string Mode,
    string? SessionId,
    Guid? ScenarioId,
    Guid? SubmissionId,
    string? PayloadJson);

public sealed class WritingAttemptEventService(
    LearnerDbContext db,
    ILogger<WritingAttemptEventService> logger) : IWritingAttemptEventService
{
    private const int MaxBatch = 50;
    private const int MaxPayloadChars = 4096;

    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.Ordinal)
    {
        "attempt_started",
        "reading_started",
        "reading_ended",
        "writing_started",
        "response_typed",
        "auto_saved",
        "paste",
        "focus_lost",
        "submit_clicked",
        "timer_expired",
        "attempt_locked",
    };

    public async Task<int> RecordAsync(string userId, IReadOnlyList<WritingAttemptEventInput> events, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0;
        if (events is null || events.Count == 0) return 0;

        var batch = events.Count > MaxBatch ? events.Take(MaxBatch).ToList() : events;
        var rows = new List<WritingAttemptEvent>(batch.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var ev in batch)
        {
            try
            {
                if (ev is null) continue;
                if (string.IsNullOrWhiteSpace(ev.EventType) || !AllowedEventTypes.Contains(ev.EventType)) continue;

                var payload = string.IsNullOrWhiteSpace(ev.PayloadJson) ? "{}" : ev.PayloadJson;
                if (payload.Length > MaxPayloadChars) payload = payload[..MaxPayloadChars];

                var sessionId = string.IsNullOrWhiteSpace(ev.SessionId) ? null : ev.SessionId;
                if (sessionId is { Length: > 64 }) sessionId = sessionId[..64];

                rows.Add(new WritingAttemptEvent
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubmissionId = ev.SubmissionId,
                    SessionId = sessionId,
                    ScenarioId = ev.ScenarioId,
                    Mode = string.Equals(ev.Mode, "paper", StringComparison.OrdinalIgnoreCase) ? "paper" : "computer",
                    EventType = ev.EventType,
                    Timestamp = ev.Timestamp ?? now,
                    PayloadJson = payload,
                    CreatedAt = now,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipped malformed Writing attempt event for user {UserId}.", userId);
            }
        }

        if (rows.Count == 0) return 0;

        db.WritingAttemptEvents.AddRange(rows);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recorded {Count} Writing attempt event(s) for user {UserId}.", rows.Count, userId);
        return rows.Count;
    }
}
