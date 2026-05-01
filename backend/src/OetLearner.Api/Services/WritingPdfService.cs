using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetLearner.Api.Services;

/// <summary>
/// Generated PDF artefact for a Writing attempt. Pure managed result — never persisted.
/// Bytes are streamed directly to the caller and the SHA-256 is recorded in audit only.
/// </summary>
public sealed record WritingPdfArtifact(byte[] Bytes, string Filename, string Sha256);

public interface IWritingPdfService
{
    Task<WritingPdfArtifact> GenerateAttemptPdfAsync(
        string attemptId,
        string requestingUserId,
        bool isPrivilegedReviewer,
        CancellationToken cancellationToken);
}

/// <summary>
/// Builds a watermarked PDF for a Writing attempt response. Layered defences:
///   1. Diagonal "PRACTICE COPY · NOT FOR RESALE" overlay rendered on every page
///      at low opacity.
///   2. Per-page footer naming the learner, attempt id, and generation timestamp.
///   3. Last-page invisible HMAC token "WMK:{userId}|{attemptId}|{epochSec}|{HMAC}"
///      keyed on Watermark__HmacSecret. Allows server-side verification of leaks.
///   4. PDF metadata (Title, Author, Subject, Keywords) carrying the same token.
///
/// Produced on demand — never stored. Rate limited and audited by callers.
/// </summary>
public sealed class WritingPdfService : IWritingPdfService
{
    private const string WatermarkHmacConfigKey = "Watermark:HmacSecret";
    private const int RateLimitPerDay = 10;

    private readonly LearnerDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<WritingPdfService> _logger;
    private readonly TimeProvider _clock;

    static WritingPdfService()
    {
        // QuestPDF Community licence is free for organisations under USD 1M ARR.
        // See https://www.questpdf.com/license/community.html — the licence is
        // explicitly accepted here so the library does not throw on first use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public WritingPdfService(
        LearnerDbContext db,
        IConfiguration config,
        ILogger<WritingPdfService> logger,
        TimeProvider? clock = null)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<WritingPdfArtifact> GenerateAttemptPdfAsync(
        string attemptId,
        string requestingUserId,
        bool isPrivilegedReviewer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingUserId);

        var attempt = await _db.Attempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken)
            ?? throw ApiException.NotFound("writing_attempt_not_found", "That writing attempt does not exist.");

        if (!string.Equals(attempt.SubtestCode, "writing", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("writing_attempt_not_found", "That writing attempt does not exist.");
        }

        if (!isPrivilegedReviewer && !string.Equals(attempt.UserId, requestingUserId, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden("writing_pdf_forbidden", "You are not authorised to download this attempt.");
        }

        // Rate limit: max RateLimitPerDay generations per (actor, attempt) per rolling 24h.
        var since = _clock.GetUtcNow().AddHours(-24);
        var resourceId = $"writing-attempt:{attempt.Id}";
        var recentCount = await _db.AuditEvents
            .AsNoTracking()
            .CountAsync(e =>
                e.Action == "WritingPdfDownloaded"
                && e.ActorId == requestingUserId
                && e.ResourceType == "WritingAttempt"
                && e.ResourceId == attempt.Id
                && e.OccurredAt >= since,
                cancellationToken);

        if (recentCount >= RateLimitPerDay)
        {
            throw ApiException.TooManyRequests(
                "writing_pdf_rate_limited",
                "You've reached today's PDF download limit for this attempt. Please try again tomorrow.");
        }

        var owner = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == attempt.UserId, cancellationToken);
        var ownerDisplayName = owner?.DisplayName ?? "Learner";

        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var epochSec = new DateTimeOffset(generatedAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var token = BuildWatermarkToken(attempt.UserId, attempt.Id, epochSec);
        var attemptShort = attempt.Id.Length > 8 ? attempt.Id[..8] : attempt.Id;

        var bytes = RenderPdf(
            ownerDisplayName,
            attempt,
            attemptShort,
            generatedAt,
            token);

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Audit (also feeds the rate-limit window).
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = _clock.GetUtcNow(),
            ActorId = requestingUserId,
            ActorName = requestingUserId,
            Action = "WritingPdfDownloaded",
            ResourceType = "WritingAttempt",
            ResourceId = attempt.Id,
            Details = $"sha256={sha256};privileged={(isPrivilegedReviewer ? "1" : "0")};size={bytes.Length}",
        });
        await _db.SaveChangesAsync(cancellationToken);

        var filename = $"writing-{attemptShort}-practice.pdf";
        return new WritingPdfArtifact(bytes, filename, sha256);
    }

    private byte[] RenderPdf(
        string ownerDisplayName,
        Attempt attempt,
        string attemptShort,
        DateTime generatedAtUtc,
        string watermarkToken)
    {
        var draftBody = string.IsNullOrWhiteSpace(attempt.DraftContent)
            ? "(No content was submitted for this attempt.)"
            : attempt.DraftContent;
        var generatedIso = generatedAtUtc.ToString("u", CultureInfo.InvariantCulture);
        var footerLine =
            $"Generated for {ownerDisplayName} · attempt {attemptShort} · {generatedIso} · Practice — not for resale or redistribution.";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(Fonts.Calibri));

                // Diagonal watermark on every page (rendered as background).
                page.Background()
                    .AlignCenter().AlignMiddle()
                    .Rotate(-30)
                    .Text("PRACTICE COPY · NOT FOR RESALE")
                    .FontSize(58).Bold().FontColor(Colors.Grey.Lighten2);

                page.Header().Column(col =>
                {
                    col.Item().Text("OET Writing — Practice Submission").FontSize(16).Bold();
                    col.Item().Text($"Learner: {ownerDisplayName}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Attempt: {attemptShort}    Generated: {generatedIso}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text("Submitted response").FontSize(12).Bold();
                    col.Item().Text(draftBody).LineHeight(1.4f);
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                    col.Item().PaddingTop(4).Text(footerLine)
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });

            // Last page: invisible HMAC token rendered in white-on-white at 1pt
            // to survive print → PDF round-trips. PDF metadata below also carries
            // the same token, double-belt for forensics.
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.Content().Column(col =>
                {
                    col.Item().Text("Practice copy — end of document.")
                        .FontSize(8).FontColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(4)
                        .Text(watermarkToken)
                        .FontSize(1)
                        .FontColor(Colors.White);
                });
            });
        });

        doc.WithMetadata(new DocumentMetadata
        {
            Title = $"OET Writing Practice — {attemptShort}",
            Author = "OET Practice Platform",
            Subject = "Practice writing response (watermarked)",
            Keywords = $"oet,writing,practice,{watermarkToken}",
            Creator = "OET Practice Platform",
            Producer = "OET Practice Platform",
            CreationDate = new DateTimeOffset(generatedAtUtc, TimeSpan.Zero),
            ModifiedDate = new DateTimeOffset(generatedAtUtc, TimeSpan.Zero),
        });

        return doc.GeneratePdf();
    }

    private string BuildWatermarkToken(string userId, string attemptId, long epochSec)
    {
        var secret = _config[WatermarkHmacConfigKey];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // First-run safety: log a warning but still produce a deterministic
            // (per-process) token so generation does not break. Operators are
            // expected to set Watermark__HmacSecret in production.
            _logger.LogWarning(
                "Watermark__HmacSecret is not configured. Falling back to a process-local secret. Set the value in production for forensic verification.");
            secret = ProcessLocalFallbackSecret;
        }

        var payload = $"{userId}|{attemptId}|{epochSec}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var sigHex = Convert.ToHexString(sig).ToLowerInvariant();
        return $"WMK:{payload}|{sigHex}";
    }

    private static readonly string ProcessLocalFallbackSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
