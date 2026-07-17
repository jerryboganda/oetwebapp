using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OetWithDrHesham.Api.Services;

public sealed record SpeakingPdfArtifact(byte[] Bytes, string Filename, string Sha256);

public interface ISpeakingPdfService
{
    Task<SpeakingPdfArtifact> GenerateEvaluationPdfAsync(
        string evaluationId,
        string requestingUserId,
        bool isExpertReviewer,
        bool isAdminReviewer,
        CancellationToken cancellationToken);
}

public sealed class SpeakingPdfService : ISpeakingPdfService
{
    private const string WatermarkHmacConfigKey = "Watermark:HmacSecret";
    private const int RateLimitPerDay = 10;

    private readonly LearnerDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SpeakingPdfService> _logger;
    private readonly TimeProvider _clock;

    static SpeakingPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public SpeakingPdfService(
        LearnerDbContext db,
        IConfiguration config,
        ILogger<SpeakingPdfService> logger,
        TimeProvider? clock = null)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<SpeakingPdfArtifact> GenerateEvaluationPdfAsync(
        string evaluationId,
        string requestingUserId,
        bool isExpertReviewer,
        bool isAdminReviewer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingUserId);

        var evaluation = await _db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evaluationId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_evaluation_not_found", "That speaking evaluation does not exist.");

        if (!string.Equals(evaluation.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("speaking_evaluation_not_found", "That speaking evaluation does not exist.");
        }

        if (evaluation.State != AsyncState.Completed)
        {
            throw ApiException.Conflict("speaking_pdf_not_ready", "The speaking result is not ready to export yet.");
        }

        var attempt = await _db.Attempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == evaluation.AttemptId, cancellationToken)
            ?? throw ApiException.NotFound("speaking_attempt_not_found", "That speaking attempt does not exist.");

        if (!string.Equals(attempt.SubtestCode, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("speaking_evaluation_not_found", "That speaking evaluation does not exist.");
        }

        var isOwner = string.Equals(attempt.UserId, requestingUserId, StringComparison.Ordinal);
        var isAssignedExpert = isExpertReviewer
            && await HasAssignedSpeakingReviewAsync(attempt.Id, requestingUserId, cancellationToken);

        if (!isOwner && !isAdminReviewer && !isAssignedExpert)
        {
            throw ApiException.Forbidden("speaking_pdf_forbidden", "You are not authorised to download this speaking result.");
        }

        var since = _clock.GetUtcNow().AddHours(-24);
        var recentCount = await _db.AuditEvents
            .AsNoTracking()
            .CountAsync(e =>
                e.Action == "SpeakingPdfDownloaded"
                && e.ActorId == requestingUserId
                && e.ResourceType == "SpeakingEvaluation"
                && e.ResourceId == evaluation.Id
                && e.OccurredAt >= since,
                cancellationToken);

        if (recentCount >= RateLimitPerDay)
        {
            throw ApiException.TooManyRequests(
                "speaking_pdf_rate_limited",
                "You've reached today's PDF download limit for this speaking result. Please try again tomorrow.");
        }

        var content = await _db.ContentItems
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == attempt.ContentId, cancellationToken);
        var owner = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == attempt.UserId, cancellationToken);

        var generatedAt = _clock.GetUtcNow().UtcDateTime;
        var epochSec = new DateTimeOffset(generatedAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var token = BuildWatermarkToken(attempt.UserId, evaluation.Id, epochSec);
        var evaluationShort = evaluation.Id.Length > 8 ? evaluation.Id[..8] : evaluation.Id;

        var bytes = RenderPdf(
            owner?.DisplayName ?? "Learner",
            attempt,
            evaluation,
            content,
            evaluationShort,
            generatedAt,
            token);

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = _clock.GetUtcNow(),
            ActorId = requestingUserId,
            ActorName = requestingUserId,
            Action = "SpeakingPdfDownloaded",
            ResourceType = "SpeakingEvaluation",
            ResourceId = evaluation.Id,
            Details = $"sha256={sha256};owner={(isOwner ? "1" : "0")};expert={(isAssignedExpert ? "1" : "0")};admin={(isAdminReviewer ? "1" : "0")};attempt={attempt.Id};learner={attempt.UserId};transcript=1;audio=0;size={bytes.Length}",
        });
        await _db.SaveChangesAsync(cancellationToken);

        return new SpeakingPdfArtifact(bytes, $"speaking-{evaluationShort}-practice.pdf", sha256);
    }

    private async Task<bool> HasAssignedSpeakingReviewAsync(string attemptId, string expertId, CancellationToken cancellationToken)
        => await _db.ExpertReviewAssignments
            .AsNoTracking()
            .Join(_db.ReviewRequests.AsNoTracking(),
                assignment => assignment.ReviewRequestId,
                review => review.Id,
                (assignment, review) => new { assignment, review })
            .AnyAsync(row => row.review.AttemptId == attemptId
                && row.review.SubtestCode == "speaking"
                && row.assignment.AssignedReviewerId == expertId
                && (row.assignment.ClaimState == ExpertAssignmentState.Assigned
                    || row.assignment.ClaimState == ExpertAssignmentState.Claimed),
                cancellationToken);

    private byte[] RenderPdf(
        string ownerDisplayName,
        Attempt attempt,
        Evaluation evaluation,
        ContentItem? content,
        string evaluationShort,
        DateTime generatedAtUtc,
        string watermarkToken)
    {
        var generatedIso = generatedAtUtc.ToString("u", CultureInfo.InvariantCulture);
        var footerLine =
            $"Generated for {ownerDisplayName} - evaluation {evaluationShort} - {generatedIso} - Practice - not for resale or redistribution.";
        var roleCard = BuildRoleCard(content);
        var transcript = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(attempt.TranscriptJson, []);
        var criteria = JsonSupport.Deserialize<List<Dictionary<string, object?>>>(evaluation.CriterionScoresJson, []);
        var strengths = JsonSupport.Deserialize<List<string>>(evaluation.StrengthsJson, []);
        var issues = JsonSupport.Deserialize<List<string>>(evaluation.IssuesJson, []);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(42);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Calibri));

                page.Background()
                    .AlignCenter().AlignMiddle()
                    .Rotate(-30)
                    .Text("PRACTICE COPY - NOT OFFICIAL")
                    .FontSize(54).Bold().FontColor(Colors.Grey.Lighten2);

                page.Header().Column(col =>
                {
                    col.Item().Text("OET Speaking - Practice Result Summary").FontSize(16).Bold();
                    col.Item().Text($"Learner: {ownerDisplayName}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Evaluation: {evaluationShort}    Generated: {generatedIso}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(evaluation.LearnerDisclaimer).FontSize(9).Italic().FontColor(Colors.Grey.Darken2);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(cell => SummaryCell(cell, "Task", content?.Title ?? attempt.ContentId));
                        row.RelativeItem().Element(cell => SummaryCell(cell, "Score range", evaluation.ScoreRange));
                        row.RelativeItem().Element(cell => SummaryCell(cell, "Confidence", evaluation.ConfidenceBand.ToString()));
                    });

                    col.Item().Text("Role card").FontSize(12).Bold();
                    col.Item().Text($"Setting: {roleCard.Setting}").FontSize(9);
                    col.Item().Text($"Patient: {roleCard.Patient}").FontSize(9);
                    col.Item().Text($"Task: {roleCard.Task}").FontSize(9);
                    if (!string.IsNullOrWhiteSpace(roleCard.Background))
                    {
                        col.Item().Text(roleCard.Background).FontSize(9).LineHeight(1.25f);
                    }
                    if (roleCard.Tasks.Count > 0)
                    {
                        col.Item().Column(list =>
                        {
                            list.Spacing(2);
                            foreach (var task in roleCard.Tasks.Take(8))
                            {
                                list.Item().Text($"- {task}").FontSize(9);
                            }
                        });
                    }

                    AddBulletSection(col, "Key strengths", strengths);
                    AddBulletSection(col, "Top improvements", issues);

                    if (criteria.Count > 0)
                    {
                        col.Item().Text("Criterion scores").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(4);
                            });
                            HeaderCell(table, "Criterion");
                            HeaderCell(table, "Score");
                            HeaderCell(table, "Feedback");
                            foreach (var criterion in criteria.Take(12))
                            {
                                BodyCell(table, CriterionLabel(ReadString(criterion, "criterionCode") ?? "Criterion"));
                                BodyCell(table, CriterionScore(criterion));
                                BodyCell(table, ReadString(criterion, "explanation", "descriptor") ?? string.Empty);
                            }
                        });
                    }

                    if (transcript.Count > 0)
                    {
                        col.Item().Text("Transcript").FontSize(12).Bold();
                        col.Item().Column(lines =>
                        {
                            lines.Spacing(4);
                            foreach (var line in transcript.Take(40))
                            {
                                var speaker = ReadString(line, "speaker") ?? "speaker";
                                var text = ReadString(line, "text") ?? string.Empty;
                                lines.Item().Text(t =>
                                {
                                    t.DefaultTextStyle(s => s.FontSize(9).LineHeight(1.25f));
                                    t.Span($"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(speaker)}: ").Bold();
                                    t.Span(text);
                                });
                            }
                        });
                    }
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

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.Content().Column(col =>
                {
                    col.Item().Text("Practice copy - end of document.")
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
            Title = $"OET Speaking Practice - {evaluationShort}",
            Author = "OET Practice Platform",
            Subject = "Practice speaking result summary (watermarked)",
            Keywords = $"oet,speaking,practice,{watermarkToken}",
            Creator = "OET Practice Platform",
            Producer = "OET Practice Platform",
            CreationDate = new DateTimeOffset(generatedAtUtc, TimeSpan.Zero),
            ModifiedDate = new DateTimeOffset(generatedAtUtc, TimeSpan.Zero),
        });

        return doc.GeneratePdf();
    }

    private static void SummaryCell(IContainer container, string label, string value)
        => container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value).FontSize(11).Bold();
        });

    private static void AddBulletSection(ColumnDescriptor col, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        col.Item().Text(title).FontSize(12).Bold();
        col.Item().Column(list =>
        {
            list.Spacing(2);
            foreach (var item in items.Take(8))
            {
                list.Item().Text($"- {item}").FontSize(9);
            }
        });
    }

    private static void HeaderCell(TableDescriptor table, string text)
        => table.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(text).FontSize(8).Bold();

    private static void BodyCell(TableDescriptor table, string text)
        => table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(text).FontSize(8);

    private SpeakingRoleCardSummary BuildRoleCard(ContentItem? content)
    {
        if (content is null)
        {
            return new SpeakingRoleCardSummary("Clinical setting", "Patient", "Complete the role play using patient-centred communication.", string.Empty, []);
        }

        var detail = SpeakingContentStructure.ToDictionary(JsonSupport.Deserialize<Dictionary<string, object?>>(content.DetailJson, new Dictionary<string, object?>()));
        var candidate = SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "candidateCard"));
        var setting = SpeakingContentStructure.ReadString(candidate, "setting")
                      ?? SpeakingContentStructure.ReadString(detail, "setting")
                      ?? "Clinical setting";
        var patient = SpeakingContentStructure.ReadString(candidate, "patientRole", "patient")
                      ?? SpeakingContentStructure.ReadString(detail, "patientRole", "patient")
                      ?? "Patient";
        var task = SpeakingContentStructure.ReadString(candidate, "task", "brief")
                   ?? SpeakingContentStructure.ReadString(detail, "task", "brief")
                   ?? "Complete the role play using patient-centred communication.";
        var background = SpeakingContentStructure.ReadString(candidate, "background")
                         ?? SpeakingContentStructure.ReadString(detail, "background", "caseNotes")
                         ?? content.CaseNotes
                         ?? string.Empty;
        var tasks = FirstNonEmptyList(
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(candidate, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "tasks")),
            SpeakingContentStructure.ReadStringList(SpeakingContentStructure.ReadValue(detail, "roleObjectives")));
        return new SpeakingRoleCardSummary(setting, patient, task, background, tasks);
    }

    private static List<string> FirstNonEmptyList(params List<string>[] lists)
        => lists.FirstOrDefault(list => list.Count > 0) ?? [];

    private static string? ReadString(IReadOnlyDictionary<string, object?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!source.TryGetValue(key, out var value))
            {
                var match = source.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                value = match.Value;
            }

            var text = value switch
            {
                null => null,
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
                JsonElement { ValueKind: JsonValueKind.Number } e => e.ToString(),
                JsonElement { ValueKind: JsonValueKind.True } => "true",
                JsonElement { ValueKind: JsonValueKind.False } => "false",
                _ => value.ToString()
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static string CriterionScore(IReadOnlyDictionary<string, object?> criterion)
    {
        var range = ReadString(criterion, "scoreRange");
        if (!string.IsNullOrWhiteSpace(range))
        {
            return range;
        }

        var score = ReadString(criterion, "score");
        var max = ReadString(criterion, "max");
        return string.IsNullOrWhiteSpace(max) ? score ?? string.Empty : $"{score}/{max}";
    }

    private static string CriterionLabel(string code)
    {
        var spaced = string.Concat(code.Select((ch, index) => index > 0 && char.IsUpper(ch) ? $" {ch}" : ch.ToString()))
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(spaced)
            ? "Criterion"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    private string BuildWatermarkToken(string userId, string evaluationId, long epochSec)
    {
        var secret = _config[WatermarkHmacConfigKey];
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning(
                "Watermark__HmacSecret is not configured. Falling back to a process-local secret. Set the value in production for forensic verification.");
            secret = ProcessLocalFallbackSecret;
        }

        var payload = $"{userId}|{evaluationId}|{epochSec}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"WMK:{payload}|{Convert.ToHexString(sig).ToLowerInvariant()}";
    }

    private static readonly string ProcessLocalFallbackSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

sealed record SpeakingRoleCardSummary(
    string Setting,
    string Patient,
    string Task,
    string Background,
    IReadOnlyList<string> Tasks);