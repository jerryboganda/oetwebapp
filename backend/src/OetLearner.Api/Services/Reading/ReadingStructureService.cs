using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading structure service — Slice R2
//
// Owns every write path for ReadingPart / ReadingText / ReadingQuestion.
// Exposes a publish-gate validator whose results the Content Upload
// publish flow consults before flipping a paper to Published.
//
// Policy ref: docs/READING-AUTHORING-POLICY.md §3 for grading strategy
// constraints; canonical OET shape 20/6/16 is enforced here and is
// non-configurable (invariant §11).
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingStructureService
{
    Task<ReadingPart> UpsertPartAsync(ReadingPartUpsert args, string adminId, CancellationToken ct);
    Task<ReadingText> UpsertTextAsync(ReadingTextUpsert args, string adminId, CancellationToken ct);
    Task<ReadingQuestion> UpsertQuestionAsync(ReadingQuestionUpsert args, string adminId, CancellationToken ct);

    Task<bool> RemoveTextAsync(string textId, string adminId, CancellationToken ct);
    Task<bool> RemoveQuestionAsync(string questionId, string adminId, CancellationToken ct);

    /// <summary>Reorder within a part. <paramref name="orderedIds"/> is the
    /// new order; entries keep their current DisplayOrder if not in the list.</summary>
    Task ReorderTextsAsync(string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct);
    Task ReorderQuestionsAsync(string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct);

    /// <summary>Read the full structure for a paper — admin view, includes
    /// correct answers + explanations.</summary>
    Task<ReadingStructure> GetAdminStructureAsync(string paperId, CancellationToken ct);

    /// <summary>Run the publish-gate validator. Returns a structured report
    /// that admin UI can surface row-by-row.</summary>
    Task<ReadingValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct);

    /// <summary>
    /// Bulk variant: validate a set of papers in 2 round-trips total
    /// (parts+questions, then texts) instead of N � 3 in a per-paper loop.
    /// Used by the learner reading-home dashboard to filter publish-ready
    /// papers without an N+1 storm.
    /// </summary>
    Task<IReadOnlyDictionary<string, ReadingValidationReport>> BulkValidatePapersAsync(
        IReadOnlyList<ContentPaper> papers, CancellationToken ct);

    /// <summary>Ensure the three canonical parts (A/B/C) exist with default
    /// item caps. Idempotent; safe to call after paper creation.</summary>
    Task EnsureCanonicalPartsAsync(string paperId, CancellationToken ct);
}

// ── DTOs ──────────────────────────────────────────────────────────────────

public sealed record ReadingPartUpsert(
    string PaperId,
    ReadingPartCode PartCode,
    int? TimeLimitMinutes,
    string? Instructions);

public sealed record ReadingTextUpsert(
    string? Id,
    string ReadingPartId,
    int DisplayOrder,
    string Title,
    string? Source,
    string BodyHtml,
    int WordCount,
    string? TopicTag);

public sealed record ReadingQuestionUpsert(
    string? Id,
    string ReadingPartId,
    string? ReadingTextId,
    int DisplayOrder,
    int Points,
    ReadingQuestionType QuestionType,
    string Stem,
    string OptionsJson,
    string CorrectAnswerJson,
    string? AcceptedSynonymsJson,
    bool CaseSensitive,
    string? ExplanationMarkdown,
    string? SkillTag);

public sealed record ReadingStructure(
    string PaperId,
    IReadOnlyList<ReadingPartView> Parts);

public sealed record ReadingPartView(
    string Id, ReadingPartCode PartCode, int TimeLimitMinutes, int MaxRawScore,
    string? Instructions,
    IReadOnlyList<ReadingText> Texts,
    IReadOnlyList<ReadingQuestion> Questions);

public sealed record ReadingValidationReport(
    bool IsPublishReady,
    IReadOnlyList<ReadingValidationIssue> Issues,
    ReadingValidationCounts Counts);

public sealed record ReadingValidationIssue(
    string Code,
    string Severity, // "error" | "warning"
    string Message,
    string? TargetId);

public sealed record ReadingValidationCounts(
    int PartACount, int PartBCount, int PartCCount, int TotalPoints);

public sealed class ReadingStructureService : IReadingStructureService
{
    private readonly LearnerDbContext db;
    private readonly IHtmlSanitizer htmlSanitizer;

    /// <summary>
    /// Primary DI constructor. Always use this in production wiring; it gives
    /// you the full sanitizer. The secondary constructor below exists ONLY for
    /// the legacy code paths that construct this service inline purely to call
    /// <see cref="ValidatePaperAsync"/> (read-only). Those call sites should be
    /// migrated to DI over time — they never hit the sanitize-on-write path, so
    /// the no-op sanitizer in the secondary ctor is safe for them.
    /// </summary>
    public ReadingStructureService(LearnerDbContext db, IHtmlSanitizer htmlSanitizer)
    {
        this.db = db;
        this.htmlSanitizer = htmlSanitizer;
    }

    /// <summary>
    /// Read-only construction helper. DO NOT use this for code paths that call
    /// <c>UpsertTextAsync</c> — it would silently skip sanitization.
    /// </summary>
    public ReadingStructureService(LearnerDbContext db)
        : this(db, new NoOpHtmlSanitizer())
    {
    }

    /// <summary>Canonical OET shape. Non-configurable invariant
    /// (<c>docs/READING-AUTHORING-POLICY.md</c> §11).</summary>
    public static readonly IReadOnlyDictionary<ReadingPartCode, (int Items, int Minutes)> CanonicalShape =
        new Dictionary<ReadingPartCode, (int, int)>
        {
            [ReadingPartCode.A] = (20, 15),
            [ReadingPartCode.B] = (6, 45),   // B+C share 45
            [ReadingPartCode.C] = (16, 45),
        };

    /// <summary>Max raw score for a fully-assembled Reading paper.</summary>
    public const int CanonicalMaxRawScore = 42;

    public async Task EnsureCanonicalPartsAsync(string paperId, CancellationToken ct)
    {
        var existing = await db.ReadingParts
            .Where(p => p.PaperId == paperId)
            .Select(p => p.PartCode)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var (code, (items, minutes)) in CanonicalShape)
        {
            if (existing.Contains(code)) continue;
            db.ReadingParts.Add(new ReadingPart
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paperId,
                PartCode = code,
                TimeLimitMinutes = minutes,
                MaxRawScore = items,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    public async Task<ReadingPart> UpsertPartAsync(ReadingPartUpsert args, string adminId, CancellationToken ct)
    {
        var row = await db.ReadingParts
            .FirstOrDefaultAsync(p => p.PaperId == args.PaperId && p.PartCode == args.PartCode, ct);
        var now = DateTimeOffset.UtcNow;
        var (_, defaultMins) = CanonicalShape[args.PartCode];
        if (row is null)
        {
            row = new ReadingPart
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = args.PaperId,
                PartCode = args.PartCode,
                TimeLimitMinutes = args.TimeLimitMinutes ?? defaultMins,
                MaxRawScore = CanonicalShape[args.PartCode].Items,
                Instructions = args.Instructions,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ReadingParts.Add(row);
        }
        else
        {
            if (args.TimeLimitMinutes is int mins) row.TimeLimitMinutes = mins;
            if (args.Instructions is not null) row.Instructions = args.Instructions;
            row.UpdatedAt = now;
        }
        await WriteAuditAsync("ReadingPartUpserted", row.Id, $"{args.PartCode}", adminId, ct);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<ReadingText> UpsertTextAsync(ReadingTextUpsert args, string adminId, CancellationToken ct)
    {
        ReadingText? row = null;
        if (!string.IsNullOrWhiteSpace(args.Id))
            row = await db.ReadingTexts.FirstOrDefaultAsync(t => t.Id == args.Id, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ReadingText
            {
                Id = Guid.NewGuid().ToString("N"),
                ReadingPartId = args.ReadingPartId,
                DisplayOrder = args.DisplayOrder,
                Title = args.Title.Trim(),
                Source = args.Source,
                BodyHtml = htmlSanitizer.SanitizePassage(args.BodyHtml),
                WordCount = args.WordCount,
                TopicTag = args.TopicTag,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ReadingTexts.Add(row);
        }
        else
        {
            row.ReadingPartId = args.ReadingPartId;
            row.DisplayOrder = args.DisplayOrder;
            row.Title = args.Title.Trim();
            row.Source = args.Source;
            row.BodyHtml = htmlSanitizer.SanitizePassage(args.BodyHtml);
            row.WordCount = args.WordCount;
            row.TopicTag = args.TopicTag;
            row.UpdatedAt = now;
        }
        await WriteAuditAsync("ReadingTextUpserted", row.Id, row.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<ReadingQuestion> UpsertQuestionAsync(ReadingQuestionUpsert args, string adminId, CancellationToken ct)
    {
        // Validate JSON shapes before writing.
        ValidateQuestionPayload(args.QuestionType, args.OptionsJson, args.CorrectAnswerJson,
            args.AcceptedSynonymsJson);

        ReadingQuestion? row = null;
        if (!string.IsNullOrWhiteSpace(args.Id))
            row = await db.ReadingQuestions.FirstOrDefaultAsync(q => q.Id == args.Id, ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ReadingQuestion
            {
                Id = Guid.NewGuid().ToString("N"),
                ReadingPartId = args.ReadingPartId,
                ReadingTextId = args.ReadingTextId,
                DisplayOrder = args.DisplayOrder,
                Points = Math.Max(1, args.Points),
                QuestionType = args.QuestionType,
                Stem = args.Stem.Trim(),
                OptionsJson = args.OptionsJson,
                CorrectAnswerJson = args.CorrectAnswerJson,
                AcceptedSynonymsJson = args.AcceptedSynonymsJson,
                CaseSensitive = args.CaseSensitive,
                ExplanationMarkdown = args.ExplanationMarkdown,
                SkillTag = args.SkillTag,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ReadingQuestions.Add(row);
        }
        else
        {
            row.ReadingPartId = args.ReadingPartId;
            row.ReadingTextId = args.ReadingTextId;
            row.DisplayOrder = args.DisplayOrder;
            row.Points = Math.Max(1, args.Points);
            row.QuestionType = args.QuestionType;
            row.Stem = args.Stem.Trim();
            row.OptionsJson = args.OptionsJson;
            row.CorrectAnswerJson = args.CorrectAnswerJson;
            row.AcceptedSynonymsJson = args.AcceptedSynonymsJson;
            row.CaseSensitive = args.CaseSensitive;
            row.ExplanationMarkdown = args.ExplanationMarkdown;
            row.SkillTag = args.SkillTag;
            row.UpdatedAt = now;
        }
        await WriteAuditAsync("ReadingQuestionUpserted", row.Id,
            $"type={args.QuestionType} order={args.DisplayOrder}", adminId, ct);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<bool> RemoveTextAsync(string textId, string adminId, CancellationToken ct)
    {
        var row = await db.ReadingTexts.FirstOrDefaultAsync(t => t.Id == textId, ct);
        if (row is null) return false;
        db.ReadingTexts.Remove(row);
        await WriteAuditAsync("ReadingTextRemoved", textId, row.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveQuestionAsync(string questionId, string adminId, CancellationToken ct)
    {
        var row = await db.ReadingQuestions.FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (row is null) return false;
        db.ReadingQuestions.Remove(row);
        await WriteAuditAsync("ReadingQuestionRemoved", questionId, row.Stem, adminId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderTextsAsync(string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct)
    {
        var texts = await db.ReadingTexts.Where(t => t.ReadingPartId == partId).ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var t = texts.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (t is null) continue;
            t.DisplayOrder = i + 1;
            t.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await WriteAuditAsync("ReadingTextsReordered", partId, null, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderQuestionsAsync(string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct)
    {
        var qs = await db.ReadingQuestions.Where(q => q.ReadingPartId == partId).ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var q = qs.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (q is null) continue;
            q.DisplayOrder = i + 1;
            q.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await WriteAuditAsync("ReadingQuestionsReordered", partId, null, adminId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReadingStructure> GetAdminStructureAsync(string paperId, CancellationToken ct)
    {
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .Include(p => p.Texts.OrderBy(t => t.DisplayOrder))
            .Include(p => p.Questions.OrderBy(q => q.DisplayOrder))
            .OrderBy(p => p.PartCode)
            .ToListAsync(ct);
        return new ReadingStructure(paperId, parts
            .Select(p => new ReadingPartView(
                p.Id, p.PartCode, p.TimeLimitMinutes, p.MaxRawScore,
                p.Instructions,
                p.Texts.OrderBy(t => t.DisplayOrder).ToList(),
                p.Questions.OrderBy(q => q.DisplayOrder).ToList()))
            .ToList());
    }

    public async Task<ReadingValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null)
        {
            return new(false,
                new[] { new ReadingValidationIssue("paper_missing", "error", "Paper not found.", null) },
                new(0, 0, 0, 0));
        }

        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .Include(p => p.Questions)
            .ToListAsync(ct);

        var partIds = parts.Select(p => p.Id).ToList();
        var texts = await db.ReadingTexts.AsNoTracking()
            .Where(t => partIds.Contains(t.ReadingPartId))
            .ToListAsync(ct);

        return ValidatePreloaded(paper, parts, texts);
    }

    public async Task<IReadOnlyDictionary<string, ReadingValidationReport>> BulkValidatePapersAsync(
        IReadOnlyList<ContentPaper> papers, CancellationToken ct)
    {
        var result = new Dictionary<string, ReadingValidationReport>(papers.Count);
        if (papers.Count == 0) return result;

        var paperIds = papers.Select(p => p.Id).ToList();

        // Two round-trips total, regardless of N.
        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => paperIds.Contains(p.PaperId))
            .Include(p => p.Questions)
            .ToListAsync(ct);

        var partIds = parts.Select(p => p.Id).ToList();
        var texts = partIds.Count == 0
            ? new List<ReadingText>()
            : await db.ReadingTexts.AsNoTracking()
                .Where(t => partIds.Contains(t.ReadingPartId))
                .ToListAsync(ct);

        var partsByPaper = parts.GroupBy(p => p.PaperId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReadingPart>)g.ToList());
        var textsByPart = texts.GroupBy(t => t.ReadingPartId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ReadingText>)g.ToList());

        foreach (var paper in papers)
        {
            var paperParts = partsByPaper.TryGetValue(paper.Id, out var pp) ? pp : Array.Empty<ReadingPart>();
            var paperTexts = paperParts
                .SelectMany(p => textsByPart.TryGetValue(p.Id, out var tt) ? tt : Array.Empty<ReadingText>())
                .ToList();
            result[paper.Id] = ValidatePreloaded(paper, paperParts, paperTexts);
        }
        return result;
    }

    /// <summary>
    /// Pure validation over preloaded data. Shared by both the per-paper
    /// (<see cref="ValidatePaperAsync"/>) and bulk
    /// (<see cref="BulkValidatePapersAsync"/>) entry points so the rules
    /// stay in one place.
    /// </summary>
    private static ReadingValidationReport ValidatePreloaded(
        ContentPaper paper,
        IReadOnlyList<ReadingPart> parts,
        IReadOnlyList<ReadingText> texts)
    {
        var issues = new List<ReadingValidationIssue>();
        if (!string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("wrong_subtest", "error",
                $"Paper subtest is '{paper.SubtestCode}', expected 'reading'.", null));
        }

        var textsByPart = texts.GroupBy(t => t.ReadingPartId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int partA = 0, partB = 0, partC = 0, totalPoints = 0;
        foreach (var part in parts)
        {
            var questionCount = part.Questions.Count;
            var (expected, _) = CanonicalShape[part.PartCode];
            if (questionCount != expected)
            {
                issues.Add(new(
                    Code: $"part_{part.PartCode}_item_count",
                    Severity: "error",
                    Message: $"Part {part.PartCode} has {questionCount} item(s), expected {expected}.",
                    TargetId: part.Id));
            }
            switch (part.PartCode)
            {
                case ReadingPartCode.A: partA = questionCount; break;
                case ReadingPartCode.B: partB = questionCount; break;
                case ReadingPartCode.C: partC = questionCount; break;
            }
            foreach (var q in part.Questions)
            {
                totalPoints += q.Points;
                try
                {
                    ValidateQuestionPayload(q.QuestionType, q.OptionsJson, q.CorrectAnswerJson, q.AcceptedSynonymsJson);
                }
                catch (Exception ex)
                {
                    issues.Add(new("question_payload_invalid", "error", ex.Message, q.Id));
                }
            }

            if (questionCount > 0 && part.Questions.All(q => q.ReadingTextId is null))
            {
                issues.Add(new($"part_{part.PartCode}_no_texts", "warning",
                    $"No questions in Part {part.PartCode} reference a text � OK for matching, suspicious for MCQ.",
                    part.Id));
            }

            // Every text must have a source for copyright
            var partTexts = textsByPart.TryGetValue(part.Id, out var tt) ? tt : new List<ReadingText>();
            foreach (var t in partTexts.Where(x => string.IsNullOrWhiteSpace(x.Source)))
            {
                issues.Add(new("text_missing_source", "error",
                    $"Text '{t.Title}' is missing a Source attribution.", t.Id));
            }
        }

        // Ensure all 3 canonical parts exist
        foreach (var code in CanonicalShape.Keys)
        {
            if (!parts.Any(p => p.PartCode == code))
                issues.Add(new($"part_{code}_missing", "error", $"Part {code} is missing.", null));
        }

        // Total points must equal 42
        if (totalPoints != CanonicalMaxRawScore)
            issues.Add(new("total_points_mismatch", "error",
                $"Total Reading points = {totalPoints}, must equal {CanonicalMaxRawScore}.", null));

        var counts = new ReadingValidationCounts(partA, partB, partC, totalPoints);
        var isReady = !issues.Any(i => i.Severity == "error");
        return new ReadingValidationReport(isReady, issues, counts);
    }

    // ── JSON shape validation ────────────────────────────────────────────
    //
    // Enforces the contract on CorrectAnswerJson / OptionsJson / AcceptedSynonymsJson
    // at write time so bad data never lands in the DB.

    public static void ValidateQuestionPayload(
        ReadingQuestionType type, string optionsJson, string correctAnswerJson, string? synonymsJson)
    {
        JsonElement options, correct;
        try { options = JsonDocument.Parse(optionsJson).RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("OptionsJson is not valid JSON."); }
        try { correct = JsonDocument.Parse(correctAnswerJson).RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("CorrectAnswerJson is not valid JSON."); }

        switch (type)
        {
            case ReadingQuestionType.MultipleChoice3:
            case ReadingQuestionType.MultipleChoice4:
                {
                    if (options.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException("MCQ OptionsJson must be a JSON array.");
                    var expectedCount = type == ReadingQuestionType.MultipleChoice3 ? 3 : 4;
                    if (options.GetArrayLength() != expectedCount)
                        throw new InvalidOperationException($"MCQ{expectedCount} must have exactly {expectedCount} options.");
                    if (correct.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException("MCQ CorrectAnswerJson must be a single string letter.");
                    var ans = correct.GetString() ?? string.Empty;
                    var valid = type == ReadingQuestionType.MultipleChoice3
                        ? new[] { "A", "B", "C" } : new[] { "A", "B", "C", "D" };
                    if (!valid.Contains(ans, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"MCQ answer must be one of {string.Join(",", valid)}.");
                    break;
                }
            case ReadingQuestionType.MatchingTextReference:
                {
                    if (correct.ValueKind != JsonValueKind.String && correct.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException("Matching CorrectAnswerJson must be a string or array of strings.");
                    break;
                }
            case ReadingQuestionType.ShortAnswer:
            case ReadingQuestionType.SentenceCompletion:
                {
                    if (correct.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException("Short-answer CorrectAnswerJson must be a string.");
                    if (!string.IsNullOrWhiteSpace(synonymsJson))
                    {
                        try
                        {
                            var syns = JsonDocument.Parse(synonymsJson).RootElement;
                            if (syns.ValueKind != JsonValueKind.Array)
                                throw new InvalidOperationException("AcceptedSynonymsJson must be a JSON array of strings.");
                        }
                        catch (JsonException)
                        {
                            throw new InvalidOperationException("AcceptedSynonymsJson is not valid JSON.");
                        }
                    }
                    break;
                }
            default:
                throw new InvalidOperationException($"Unknown question type {type}.");
        }
    }

    private Task WriteAuditAsync(string action, string resourceId, string? details, string adminId, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = action,
            ResourceType = "ReadingAuthoring",
            ResourceId = resourceId,
            Details = details,
        });
        return Task.CompletedTask;
    }
}
