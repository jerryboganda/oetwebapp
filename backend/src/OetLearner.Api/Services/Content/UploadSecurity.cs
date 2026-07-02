using System.Buffers.Binary;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Content validation + scanner hook — Slice 8 hardening.
//
// Trust-no-client principle: every uploaded byte is verified by content-type
// magic bytes, not the client-declared Content-Type header. Extension must
// match magic bytes.
//
// The scanner is pluggable so ClamAV / S3 AV / Azure Defender can drop in
// later. Default is a no-op that accepts everything but logs a warning if no
// real scanner is wired in production.
// ═════════════════════════════════════════════════════════════════════════════

public interface IUploadContentValidator
{
    /// <summary>
    /// Inspect the first bytes of a stream. Returns (accepted, detectedMime,
    /// detectedExtension). Stream is rewound if seekable.
    /// </summary>
    Task<UploadValidationResult> ValidateAsync(Stream stream, string declaredExtension, CancellationToken ct);
}

public sealed record UploadValidationResult(
    bool Accepted,
    string? DetectedMime,
    string? DetectedExtension,
    string? Reason);

public sealed class MagicByteValidator : IUploadContentValidator
{
    public async Task<UploadValidationResult> ValidateAsync(Stream stream, string declaredExtension, CancellationToken ct)
    {
        var ext = declaredExtension.TrimStart('.').ToLowerInvariant();
        // Read the first 16 bytes.
        var header = new byte[16];
        var startPos = stream.CanSeek ? stream.Position : -1;
        int read = 0;
        while (read < header.Length)
        {
            var r = await stream.ReadAsync(header.AsMemory(read, header.Length - read), ct);
            if (r == 0) break;
            read += r;
        }
        if (stream.CanSeek) stream.Position = startPos;

        if (read < 4)
            return new(false, null, null, "File too short to identify.");

        // PDF: starts with "%PDF-"
        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D)
        {
            return ext == "pdf"
                ? new(true, "application/pdf", "pdf", null)
                : new(false, "application/pdf", "pdf", $"Declared .{ext} but file is a PDF.");
        }

        // MP3 / ID3: "ID3" or 0xFF 0xFB/0xFA/0xF3/0xF2 frame sync
        if ((header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
            || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0))
        {
            return ext == "mp3"
                ? new(true, "audio/mpeg", "mp3", null)
                : new(false, "audio/mpeg", "mp3", $"Declared .{ext} but file is an MP3.");
        }

        // MP4 / M4A container ftyp header at offset 4
        if (read >= 12 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
        {
            return (ext == "m4a" || ext == "mp4")
                ? new(true, "audio/mp4", ext, null)
                : new(false, "audio/mp4", "m4a", $"Declared .{ext} but file is an MP4/M4A container.");
        }

        // WAV: "RIFF" .... "WAVE"
        if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && read >= 12 && header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)
        {
            return ext == "wav"
                ? new(true, "audio/wav", "wav", null)
                : new(false, "audio/wav", "wav", $"Declared .{ext} but file is WAV.");
        }

        // OGG Vorbis
        if (header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53)
        {
            return ext == "ogg"
                ? new(true, "audio/ogg", "ogg", null)
                : new(false, "audio/ogg", "ogg", $"Declared .{ext} but file is OGG.");
        }

        // WebM / Matroska: EBML header. Browser MediaRecorder commonly emits this.
        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return ext == "webm"
                ? new(true, "audio/webm", "webm", null)
                : new(false, "audio/webm", "webm", $"Declared .{ext} but file is WebM.");
        }

        // PNG
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return ext == "png" ? new(true, "image/png", "png", null) : new(false, "image/png", "png", $"Declared .{ext} but file is PNG.");

        // JPEG
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return (ext == "jpg" || ext == "jpeg") ? new(true, "image/jpeg", "jpg", null) : new(false, "image/jpeg", "jpg", $"Declared .{ext} but file is JPEG.");

        // GIF87a / GIF89a
        if (read >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46
            && header[3] == 0x38 && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return ext == "gif"
                ? new(true, "image/gif", "gif", null)
                : new(false, "image/gif", "gif", $"Declared .{ext} but file is GIF.");
        }

        // WebP: RIFF .... WEBP
        if (read >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return ext == "webp"
                ? new(true, "image/webp", "webp", null)
                : new(false, "image/webp", "webp", $"Declared .{ext} but file is WebP.");
        }

        // ZIP (also used by DOCX, XLSX, PPTX, JAR, ...)
        if (header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07))
            return ext == "zip" ? new(true, "application/zip", "zip", null) : new(false, "application/zip", "zip", $"Declared .{ext} but file is a ZIP container.");

        // Caption tracks (Video Library) are plain text with no magic bytes.
        // Only sniffed when the DECLARED extension is vtt/srt — every binary
        // format above has already been identified and rejected as a mismatch,
        // so this cannot be used to smuggle a binary past the validator.
        if (ext is "vtt" or "srt")
        {
            var textStart = header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF ? 3 : 0; // skip UTF-8 BOM
            if (ext == "vtt")
            {
                var isWebVtt = read >= textStart + 6
                    && header[textStart] == (byte)'W' && header[textStart + 1] == (byte)'E'
                    && header[textStart + 2] == (byte)'B' && header[textStart + 3] == (byte)'V'
                    && header[textStart + 4] == (byte)'T' && header[textStart + 5] == (byte)'T';
                return isWebVtt
                    ? new(true, "text/vtt", "vtt", null)
                    : new(false, null, null, "Declared .vtt but file does not start with a WEBVTT header.");
            }

            // SRT: first meaningful byte is the numeric cue index (digits,
            // possibly preceded by whitespace/newlines).
            var i = textStart;
            while (i < read && (header[i] == (byte)' ' || header[i] == (byte)'\r' || header[i] == (byte)'\n' || header[i] == (byte)'\t'))
            {
                i++;
            }
            return i < read && header[i] is >= (byte)'0' and <= (byte)'9'
                ? new(true, "application/x-subrip", "srt", null)
                : new(false, null, null, "Declared .srt but file does not look like a SubRip track.");
        }

        return new(false, null, null, "Unrecognised file format.");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Scanner hook — Slice 8
// ═════════════════════════════════════════════════════════════════════════════

public interface IUploadScanner
{
    /// <summary>Scan a file. Return (clean, reason). Implementations can
    /// stream through antivirus engines; default no-op passes everything.</summary>
    Task<(bool clean, string? reason)> ScanAsync(Stream stream, string filename, CancellationToken ct);
}

public sealed class NoOpUploadScanner(ILogger<NoOpUploadScanner> logger) : IUploadScanner
{
    private bool _warnedOnce;
    public Task<(bool clean, string? reason)> ScanAsync(Stream stream, string filename, CancellationToken ct)
    {
        if (!_warnedOnce)
        {
            _warnedOnce = true;
            logger.LogWarning(
                "NoOpUploadScanner is active — production deployments should wire a real IUploadScanner (ClamAV, etc.).");
        }
        return Task.FromResult((clean: true, reason: (string?)null));
    }
}
