using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 6 — chunked audio capture for the live Speaking room.
///
/// Each browser MediaRecorder chunk is POSTed to
/// POST /v1/mock-bookings/{id}/recording-chunk?part={n}
/// and persisted via <see cref="IFileStorage"/> at a content-addressed key
/// under <c>mock-bookings/{bookingId}/chunks/</c>. The booking row carries a
/// JSON manifest of accepted chunks; finalize stamps a completion timestamp
/// and writes an audit event.
///
/// Recording is gated on <see cref="MockBooking.ConsentToRecording"/>.
/// All file I/O routes through <see cref="IFileStorage"/> per the
/// content-upload mission-critical rules — never raw <c>File.*</c>.
/// </summary>
public sealed class MockBookingRecordingService
{
    public const long MaxChunkBytes = 8L * 1024 * 1024; // 8 MiB per chunk
    public const int MaxChunks = 240; // ~20 min at 5s/chunk
    public const long MaxTotalBytes = 200L * 1024 * 1024; // 200 MiB safety cap
    private const string Root = "mock-bookings";

    private readonly LearnerDbContext _db;
    private readonly IFileStorage _storage;

    public MockBookingRecordingService(LearnerDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<object> AppendChunkAsync(
        string userId,
        string bookingId,
        int part,
        string? mimeType,
        Stream body,
        CancellationToken ct)
    {
        if (part < 0 || part >= MaxChunks)
            throw ApiException.Validation("invalid_part", $"part must be in [0, {MaxChunks}).");

        var booking = await _db.MockBookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (booking.UserId != userId)
            throw ApiException.Forbidden("forbidden", "You cannot record on this booking.");
        if (!booking.ConsentToRecording)
            throw ApiException.Validation("consent_required", "Recording consent has not been granted for this booking.");
        if (booking.RecordingFinalizedAt is not null)
            throw ApiException.Validation("recording_finalized", "Recording has already been finalised for this booking.");

        var manifest = ReadManifest(booking.RecordingManifestJson);
        var totalBytes = manifest.Chunks.Sum(c => c.Bytes);

        // Idempotent retry handling: if a chunk with the same Part has
        // already been accepted, treat the new request as a duplicate.
        // • Same Part + same SHA → success (no mutation), client just retried.
        // • Same Part + different SHA → reject; the client must pick a new
        //   part index rather than overwrite — prevents a malicious client
        //   from spamming the same part index to exhaust caps.
        // We must read the body to compute the SHA before we can decide,
        // so the cap check below uses an upper-bound (totalBytes + read).
        var existingForPart = manifest.Chunks.FirstOrDefault(c => c.Part == part);

        if (manifest.Chunks.Count >= MaxChunks && existingForPart is null)
            throw ApiException.Validation("chunk_cap_reached", $"Maximum {MaxChunks} chunks per booking.");

        // Buffer with bounded read so we can hash + measure before writing.
        using var buffer = new MemoryStream();
        var buf = new byte[81920];
        long read = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await body.ReadAsync(buf, ct);
            if (n == 0) break;
            read += n;
            if (read > MaxChunkBytes)
                throw ApiException.Validation("chunk_too_large", $"Chunk exceeds {MaxChunkBytes} bytes.");
            if (totalBytes + read > MaxTotalBytes)
                throw ApiException.Validation("recording_too_large", $"Recording exceeds {MaxTotalBytes} bytes.");
            await buffer.WriteAsync(buf.AsMemory(0, n), ct);
        }
        if (read == 0)
            throw ApiException.Validation("empty_chunk", "Empty chunk body.");

        buffer.Position = 0;
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
        buffer.Position = 0;

        // Idempotent retry: same part + same SHA = duplicate, return success without mutation.
        if (existingForPart is not null)
        {
            if (string.Equals(existingForPart.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    part,
                    sha256 = sha,
                    bytes = existingForPart.Bytes,
                    chunkCount = manifest.Chunks.Count,
                    totalBytes,
                    duplicate = true,
                };
            }
            throw ApiException.Validation(
                "part_already_uploaded",
                $"Part {part} has already been uploaded with a different payload. Use the next part index.");
        }

        var safeMime = NormaliseMimeType(mimeType);
        var ext = GuessExtension(safeMime);
        var key = $"{Root}/{bookingId}/chunks/part-{part:000}-{sha}.{ext}";
        if (!_storage.Exists(key))
        {
            await _storage.WriteAsync(key, buffer, ct);
        }

        manifest.Chunks.Add(new RecordingChunk
        {
            Part = part,
            Sha256 = sha,
            Key = key,
            Bytes = read,
            MimeType = safeMime,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        booking.RecordingManifestJson = WriteManifest(manifest);
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new
        {
            part,
            sha256 = sha,
            bytes = read,
            chunkCount = manifest.Chunks.Count,
            totalBytes = totalBytes + read,
        };
    }

    public async Task<object> FinalizeAsync(
        string userId,
        string bookingId,
        long? durationMs,
        CancellationToken ct)
    {
        var booking = await _db.MockBookings.FirstOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw ApiException.NotFound("booking_not_found", "Booking not found.");
        if (booking.UserId != userId)
            throw ApiException.Forbidden("forbidden", "You cannot finalise this booking's recording.");
        if (!booking.ConsentToRecording)
            throw ApiException.Validation("consent_required", "Recording consent has not been granted for this booking.");
        if (booking.RecordingFinalizedAt is not null)
        {
            return Project(booking);
        }

        var manifest = ReadManifest(booking.RecordingManifestJson);
        if (manifest.Chunks.Count == 0)
            throw ApiException.Validation("no_chunks", "No recording chunks have been received.");

        booking.RecordingFinalizedAt = DateTimeOffset.UtcNow;
        booking.RecordingDurationMs = durationMs is > 0 ? durationMs : booking.RecordingDurationMs;
        booking.UpdatedAt = booking.RecordingFinalizedAt.Value;

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = booking.RecordingFinalizedAt.Value,
            ActorId = userId,
            ActorName = userId,
            Action = "mock_booking_recording_finalised",
            ResourceType = "MockBooking",
            ResourceId = booking.Id,
            Details = JsonSupport.Serialize(new
            {
                chunkCount = manifest.Chunks.Count,
                totalBytes = manifest.Chunks.Sum(c => c.Bytes),
                durationMs = booking.RecordingDurationMs,
            }),
        });
        await _db.SaveChangesAsync(ct);

        return Project(booking);
    }

    private static object Project(MockBooking b) => new
    {
        bookingId = b.Id,
        recordingFinalizedAt = b.RecordingFinalizedAt,
        recordingDurationMs = b.RecordingDurationMs,
        consentToRecording = b.ConsentToRecording,
    };

    private static RecordingManifest ReadManifest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RecordingManifest();
        try
        {
            return JsonSerializer.Deserialize<RecordingManifest>(json) ?? new RecordingManifest();
        }
        catch
        {
            return new RecordingManifest();
        }
    }

    private static string WriteManifest(RecordingManifest manifest)
        => JsonSerializer.Serialize(manifest);

    private static string NormaliseMimeType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "audio/webm";
        var trimmed = raw.Split(';')[0].Trim();
        return trimmed switch
        {
            "audio/webm" or "audio/ogg" or "audio/mp4" or "audio/mpeg"
                or "audio/mp3" or "audio/wav" or "audio/x-wav" => trimmed,
            _ => "application/octet-stream",
        };
    }

    private static string GuessExtension(string mime) => mime switch
    {
        "audio/mpeg" or "audio/mp3" => "mp3",
        "audio/webm" => "webm",
        "audio/ogg" => "ogg",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/mp4" => "m4a",
        _ => "bin",
    };

    private sealed class RecordingManifest
    {
        public List<RecordingChunk> Chunks { get; set; } = new();
    }

    private sealed class RecordingChunk
    {
        public int Part { get; set; }
        public string Sha256 { get; set; } = "";
        public string Key { get; set; } = "";
        public long Bytes { get; set; }
        public string MimeType { get; set; } = "audio/webm";
        public DateTimeOffset ReceivedAt { get; set; }
    }
}
