using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Ai;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.Writing.Configuration;

namespace OetWithDrHesham.Api.Services.Writing;

public sealed record WritingOcrJobView(
    Guid Id,
    Guid? SubmissionId,
    string Status,
    string Provider,
    double? ConfidenceScore,
    string? ExtractedText,
    IReadOnlyList<string> ImageUrls,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public interface IWritingOcrService
{
    Task<WritingOcrJobView> EnqueueAsync(string userId, Guid? submissionId, IReadOnlyList<string> imageUrls, CancellationToken ct);
    Task<WritingOcrJobView?> GetAsync(string userId, Guid jobId, CancellationToken ct);
    Task<WritingOcrJobView> ProcessAsync(string userId, Guid jobId, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingOcrJobResponse> QueueOcrJobAsync(string userId, IReadOnlyList<IFormFile> files, Guid? submissionId, CancellationToken ct);
    Task<WritingOcrJobResponse?> GetJobAsync(string userId, Guid jobId, CancellationToken ct);
}

/// <summary>
/// Local Tesseract first; Google Cloud Vision fallback when configured and
/// Tesseract confidence drops below 95%. If neither is configured, the job is
/// marked <c>manual_required</c> so the learner can transcribe by hand.
///
/// Tesseract requires native binaries at runtime — <c>libtesseract.so</c> on
/// Linux (installed via the <c>tesseract-ocr</c> apt package in the Docker
/// image) or <c>tesseract.exe</c> on Windows — plus a <c>tessdata</c>
/// directory containing trained language data (<c>eng.traineddata</c>). The
/// directory path is read from <c>Writing:TessdataPath</c> and defaults to
/// the standard Linux container path. If either the managed NuGet wrapper
/// or the native binaries are unavailable at runtime, <see
/// cref="RunTesseractAsync"/> logs a warning and returns zero confidence so
/// the state machine continues to GCV or <c>manual_required</c>.
/// </summary>
public sealed class WritingOcrService(
    LearnerDbContext db,
    IHttpClientFactory httpClientFactory,
    IFileStorage storage,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    IRuntimeSettingsProvider settingsProvider,
    IOcrService ocrService,
    IAiProviderRegistry providerRegistry,
    ILogger<WritingOcrService> logger) : IWritingOcrService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxFileCount = 5;
    private const long MaxFileBytes = 8L * 1024 * 1024;
    private const long MaxJobBytes = 30L * 1024 * 1024;

    public async Task<WritingOcrJobView> EnqueueAsync(string userId, Guid? submissionId, IReadOnlyList<string> imageUrls, CancellationToken ct)
    {
        if (imageUrls is null || imageUrls.Count == 0)
        {
            throw ApiException.Validation("writing_ocr_no_images", "At least one image URL is required.");
        }
        var now = clock.GetUtcNow();
        var job = new WritingOcrJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SubmissionId = submissionId,
            Status = "pending",
            Provider = "tesseract",
            ImageUrlsJson = JsonSerializer.Serialize(imageUrls, JsonOptions),
            CreatedAt = now,
        };
        db.WritingOcrJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return ToView(job);
    }

    public async Task<WritingOcrJobView?> GetAsync(string userId, Guid jobId, CancellationToken ct)
    {
        var job = await db.WritingOcrJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);
        return job is null ? null : ToView(job);
    }

    public async Task<WritingOcrJobView> ProcessAsync(string userId, Guid jobId, CancellationToken ct)
    {
        var job = await db.WritingOcrJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_ocr_job_not_found", "OCR job was not found.");
        if (job.Status is "completed" or "manual_required") return ToView(job);
        var now = clock.GetUtcNow();
        job.Status = "processing";
        job.StartedAt = now;
        await db.SaveChangesAsync(ct);

        // DB-over-env runtime knobs (OcrEnabled + decrypted GcvApiKey). The host
        // TessdataPath stays env-only (read from IOptions below).
        var opts = (await settingsProvider.GetAsync(ct)).Writing;
        if (!opts.OcrEnabled)
        {
            job.Status = "manual_required";
            job.ErrorMessage = "OCR is disabled. Please transcribe manually.";
            job.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return ToView(job);
        }

        var images = DeserializeStringList(job.ImageUrlsJson);
        try
        {
            // Preferred OCR — Mistral OCR (canonical provider) when the
            // mistral-ocr row is keyed. Falls through to the Tesseract → GCV
            // chain when not configured or when it yields no text.
            var mistralText = await RunMistralOcrAsync(userId, images, ct);
            if (!string.IsNullOrWhiteSpace(mistralText))
            {
                Complete(job, "mistral-ocr", mistralText!, 0.99);
                await db.SaveChangesAsync(ct);
                return ToView(job);
            }

            var tesseractResult = await RunTesseractAsync(images, ct);
            if (tesseractResult.ConfidenceScore >= 0.95)
            {
                Complete(job, "tesseract", tesseractResult.Text, tesseractResult.ConfidenceScore);
            }
            else if (!string.IsNullOrWhiteSpace(opts.GcvApiKey))
            {
                var gcvResult = await RunGoogleCloudVisionAsync(images, opts.GcvApiKey!, ct);
                Complete(job, "gcv", gcvResult.Text, gcvResult.ConfidenceScore);
            }
            else if (!string.IsNullOrWhiteSpace(tesseractResult.Text))
            {
                Complete(job, "tesseract", tesseractResult.Text, tesseractResult.ConfidenceScore);
            }
            else
            {
                job.Status = "manual_required";
                job.ErrorMessage = "OCR providers unavailable or low confidence. Please transcribe manually.";
                job.CompletedAt = clock.GetUtcNow();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing OCR job {JobId} failed.", job.Id);
            job.Status = "failed";
            job.ErrorMessage = "OCR failed. Please try again or transcribe manually.";
            job.CompletedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        return ToView(job);
    }

    private void Complete(WritingOcrJob job, string provider, string text, double confidence)
    {
        job.Provider = provider;
        job.ExtractedText = text;
        job.ConfidenceScore = confidence;
        job.Status = "completed";
        job.CompletedAt = clock.GetUtcNow();
    }

    /// <summary>OCR each handwritten-essay image via Mistral OCR (canonical
    /// provider) and concatenate the Markdown. Returns null when the
    /// <c>mistral-ocr</c> row has no key (so the caller falls through to the
    /// Tesseract → GCV chain) or when nothing could be extracted. Fail-soft —
    /// any error logs and returns null. Each page is recorded as an
    /// <c>AiUsageRecord</c> via <see cref="IOcrService"/>.</summary>
    private async Task<string?> RunMistralOcrAsync(string userId, IReadOnlyList<string> imageUrls, CancellationToken ct)
    {
        if (imageUrls.Count == 0) return null;
        try
        {
            var key = await providerRegistry.GetPlatformKeyAsync(MistralOcrClient.ProviderCode, ct);
            if (string.IsNullOrEmpty(key)) return null;

            var sb = new System.Text.StringBuilder();
            foreach (var url in imageUrls)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await TryLoadImageBytesAsync(url, ct);
                if (bytes is null || bytes.Length == 0) continue;
                var markdown = await ocrService.OcrToMarkdownAsync(
                    bytes, GuessImageMime(url), AiFeatureCodes.OcrWritingHandwriting, userId, ct);
                sb.AppendLine(markdown);
                sb.AppendLine();
            }
            var text = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mistral OCR (writing handwriting) failed; falling through to Tesseract/GCV.");
            return null;
        }
    }

    private static string GuessImageMime(string url)
    {
        var path = url.Split('?', 2)[0].ToLowerInvariant();
        return Path.GetExtension(path) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            _ => "image/jpeg",
        };
    }

    private async Task<OcrResult> RunTesseractAsync(IReadOnlyList<string> imageUrls, CancellationToken ct)
    {
        if (imageUrls.Count == 0)
        {
            return new OcrResult(string.Empty, 0);
        }

        // Resolve tessdata path: explicit config wins, else the standard
        // Linux container path. If neither resolves to an existing directory
        // we degrade to zero confidence so the state machine can fall
        // through to GCV / manual.
        var tessdataPath = options.Value.TessdataPath;
        if (string.IsNullOrWhiteSpace(tessdataPath))
        {
            tessdataPath = "/usr/share/tesseract-ocr/5/tessdata";
        }
        if (!Directory.Exists(tessdataPath))
        {
            logger.LogWarning("Tesseract tessdata directory not found at {Path}; falling through to next OCR provider.", tessdataPath);
            return new OcrResult(string.Empty, 0);
        }

        try
        {
            using var engine = new Tesseract.TesseractEngine(tessdataPath, "eng", Tesseract.EngineMode.Default);
            var aggregateText = new System.Text.StringBuilder();
            var confidences = new List<float>();
            var processed = 0;
            foreach (var url in imageUrls)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await TryLoadImageBytesAsync(url, ct);
                if (bytes is null || bytes.Length == 0)
                {
                    continue;
                }
                try
                {
                    using var pix = Tesseract.Pix.LoadFromMemory(bytes);
                    using var page = engine.Process(pix);
                    var text = page.GetText() ?? string.Empty;
                    var confidence = page.GetMeanConfidence();
                    aggregateText.AppendLine(text);
                    confidences.Add(confidence);
                    processed++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Tesseract OCR failed for image {Url}; continuing with remaining images.", url);
                }
            }
            if (processed == 0)
            {
                return new OcrResult(string.Empty, 0);
            }
            var avgConfidence = confidences.Count == 0 ? 0.0 : confidences.Average();
            return new OcrResult(aggregateText.ToString().Trim(), avgConfidence);
        }
        catch (DllNotFoundException ex)
        {
            // Native libtesseract / leptonica missing — degrade gracefully.
            logger.LogWarning(ex, "Tesseract native binaries not available; falling through to next OCR provider.");
            return new OcrResult(string.Empty, 0);
        }
        catch (TypeInitializationException ex)
        {
            logger.LogWarning(ex, "Tesseract engine could not initialise; falling through to next OCR provider.");
            return new OcrResult(string.Empty, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tesseract OCR provider failed unexpectedly; falling through to next provider.");
            return new OcrResult(string.Empty, 0);
        }
    }

    private async Task<byte[]?> TryLoadImageBytesAsync(string url, CancellationToken ct)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == "storage")
                {
                    return await ReadStorageBytesAsync(uri.AbsolutePath.TrimStart('/'), ct);
                }
                if (uri.Scheme is "http" or "https")
                {
                    var client = httpClientFactory.CreateClient("writing-ocr-tesseract");
                    using var response = await client.GetAsync(uri, ct);
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsByteArrayAsync(ct);
                }
                return null;
            }
            if (await storage.ExistsAsync(url, ct))
            {
                return await ReadStorageBytesAsync(url, ct);
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Tesseract image fetch failed for {Url}.", url);
            return null;
        }
    }

    private async Task<byte[]> ReadStorageBytesAsync(string key, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private async Task<OcrResult> RunGoogleCloudVisionAsync(IReadOnlyList<string> imageUrls, string apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("writing-ocr-gcv");
        var aggregateText = new System.Text.StringBuilder();
        double minConfidence = 1.0;
        foreach (var url in imageUrls)
        {
            var bytes = await TryLoadImageBytesAsync(url, ct);
            var imagePayload = bytes is { Length: > 0 }
                ? (object)new { content = Convert.ToBase64String(bytes) }
                : new { source = new { imageUri = url } };
            var payload = new
            {
                requests = new[]
                {
                    new
                    {
                        image = imagePayload,
                        features = new[] { new { type = "DOCUMENT_TEXT_DETECTION" } },
                    },
                },
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"https://vision.googleapis.com/v1/images:annotate?key={apiKey}", content, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Array) continue;
            foreach (var r in responses.EnumerateArray())
            {
                if (r.TryGetProperty("fullTextAnnotation", out var fta) && fta.TryGetProperty("text", out var textEl))
                {
                    aggregateText.AppendLine(textEl.GetString());
                }
                if (r.TryGetProperty("textAnnotations", out var tas) && tas.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ta in tas.EnumerateArray())
                    {
                        if (ta.TryGetProperty("confidence", out var cEl) && cEl.TryGetDouble(out var c))
                        {
                            minConfidence = Math.Min(minConfidence, c);
                        }
                    }
                }
            }
        }
        return new OcrResult(aggregateText.ToString().Trim(), minConfidence);
    }

    private static WritingOcrJobView ToView(WritingOcrJob job)
        => new(job.Id, job.SubmissionId, job.Status, job.Provider, job.ConfidenceScore, job.ExtractedText,
            DeserializeStringList(job.ImageUrlsJson), job.ErrorMessage, job.CreatedAt, job.CompletedAt);

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private readonly record struct OcrResult(string Text, double ConfidenceScore);

    // ─────────────────────────────────────────────────────────────────────────
    // V2 endpoint adapters
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<WritingOcrJobResponse> QueueOcrJobAsync(string userId, IReadOnlyList<IFormFile> files, Guid? submissionId, CancellationToken ct)
    {
        if (submissionId is { } sid)
        {
            var ownsSubmission = await db.WritingSubmissions.AsNoTracking().AnyAsync(s => s.Id == sid && s.UserId == userId, ct);
            if (!ownsSubmission)
            {
                throw ApiException.NotFound("writing_submission_not_found", "Submission was not found for this learner.");
            }
        }
        if (files.Count > MaxFileCount)
        {
            throw ApiException.Validation("writing_ocr_too_many_files", $"Upload at most {MaxFileCount} images per OCR job.");
        }
        var totalBytes = files.Sum(f => f.Length);
        if (totalBytes > MaxJobBytes)
        {
            throw ApiException.Validation("writing_ocr_job_too_large", "OCR upload is too large.");
        }
        var urls = new List<string>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            ValidateUploadFile(file);
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            if (!LooksLikeSupportedImage(buffer.ToArray()))
            {
                throw ApiException.Validation("writing_ocr_invalid_image", "OCR uploads must be JPEG, PNG, or WebP images.");
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
            var extension = SafeExtension(file.FileName);
            var key = $"writing/ocr/{userId}/{hash}{extension}";
            buffer.Position = 0;
            await storage.WriteAsync(key, buffer, ct);
            urls.Add($"storage:///{key}");
        }
        var view = await EnqueueAsync(userId, submissionId, urls, ct);
        return WritingV2ResponseMapper.ToResponse(view);
    }

    public async Task<WritingOcrJobResponse?> GetJobAsync(string userId, Guid jobId, CancellationToken ct)
    {
        var view = await GetAsync(userId, jobId, ct);
        if (view?.Status == "pending")
        {
            view = await ProcessAsync(userId, jobId, ct);
        }
        return view is null ? null : WritingV2ResponseMapper.ToResponse(view);
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" ? extension : ".bin";
    }

    private static void ValidateUploadFile(IFormFile file)
    {
        if (file.Length > MaxFileBytes)
        {
            throw ApiException.Validation("writing_ocr_file_too_large", "Each OCR image must be 8 MB or smaller.");
        }
        var contentType = file.ContentType.Trim().ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            throw ApiException.Validation("writing_ocr_invalid_content_type", "OCR uploads must be JPEG, PNG, or WebP images.");
        }
    }

    private static bool LooksLikeSupportedImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isPng = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        var isWebp = bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        return isJpeg || isPng || isWebp;
    }
}
