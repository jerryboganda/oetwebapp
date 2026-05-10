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

    Task<bool> RemoveTextAsync(string paperId, string textId, string adminId, CancellationToken ct);
    Task<bool> RemoveQuestionAsync(string paperId, string questionId, string adminId, CancellationToken ct);

    /// <summary>Reorder within a part. <paramref name="orderedIds"/> is the
    /// new order; entries keep their current DisplayOrder if not in the list.</summary>
    Task ReorderTextsAsync(string paperId, string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct);
    Task ReorderQuestionsAsync(string paperId, string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct);

    /// <summary>Read the full structure for a paper — admin view, includes
    /// correct answers + explanations.</summary>
    Task<ReadingStructure> GetAdminStructureAsync(string paperId, CancellationToken ct);

    Task<ReadingStructureManifest> ExportManifestAsync(string paperId, CancellationToken ct);
    Task<ReadingStructureImportResult> ImportManifestAsync(
        string paperId,
        ReadingStructureManifest manifest,
        bool replaceExisting,
        string adminId,
        CancellationToken ct);

    /// <summary>Run the publish-gate validator. Returns a structured report
    /// that admin UI can surface row-by-row.</summary>
    Task<ReadingValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct);

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

public sealed record ReadingStructureManifest(
    IReadOnlyList<ReadingPartManifest> Parts);

public sealed record ReadingPartManifest(
    ReadingPartCode PartCode,
    int? TimeLimitMinutes,
    string? Instructions,
    IReadOnlyList<ReadingTextManifest> Texts,
    IReadOnlyList<ReadingQuestionManifest> Questions);

public sealed record ReadingTextManifest(
    int DisplayOrder,
    string Title,
    string? Source,
    string BodyHtml,
    int WordCount,
    string? TopicTag);

public sealed record ReadingQuestionManifest(
    int DisplayOrder,
    int Points,
    ReadingQuestionType QuestionType,
    string Stem,
    string OptionsJson,
    string CorrectAnswerJson,
    string? AcceptedSynonymsJson,
    bool CaseSensitive,
    string? ExplanationMarkdown,
    string? SkillTag,
    int? ReadingTextDisplayOrder,
    string? OptionDistractorsJson = null,
    ReadingReviewState ReviewState = ReadingReviewState.Draft);

public sealed record ReadingStructureImportResult(
    ReadingStructure Structure,
    ReadingValidationReport Report);

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

    private static readonly IReadOnlyDictionary<ReadingPartCode, int> CanonicalTextCounts =
        new Dictionary<ReadingPartCode, int>
        {
            [ReadingPartCode.A] = 4,
            [ReadingPartCode.B] = 6,
            [ReadingPartCode.C] = 2,
        };

    /// <summary>Max raw score for a fully-assembled Reading paper.</summary>
    public const int CanonicalMaxRawScore = 42;

    public async Task EnsureCanonicalPartsAsync(string paperId, CancellationToken ct)
    {
        await EnsureReadingPaperAsync(paperId, ct);
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
        await EnsureReadingPaperAsync(args.PaperId, ct);
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
        await EnsurePartBelongsToReadingPaperAsync(args.ReadingPartId, ct);
        ReadingText? row = null;
        if (!string.IsNullOrWhiteSpace(args.Id))
            row = await db.ReadingTexts.FirstOrDefaultAsync(t => t.Id == args.Id, ct);
        if (row is not null && !string.Equals(row.ReadingPartId, args.ReadingPartId, StringComparison.Ordinal))
            throw new InvalidOperationException("Existing Reading text belongs to a different part.");
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
        await EnsurePartBelongsToReadingPaperAsync(args.ReadingPartId, ct);
        if (!string.IsNullOrWhiteSpace(args.ReadingTextId))
        {
            var textPartId = await db.ReadingTexts.AsNoTracking()
                .Where(t => t.Id == args.ReadingTextId)
                .Select(t => t.ReadingPartId)
                .FirstOrDefaultAsync(ct);
            if (textPartId is null)
                throw new InvalidOperationException("Reading text not found.");
            if (!string.Equals(textPartId, args.ReadingPartId, StringComparison.Ordinal))
                throw new InvalidOperationException("Reading question text must belong to the same part.");
        }

        // Validate JSON shapes before writing.
        ValidateQuestionPayload(args.QuestionType, args.OptionsJson, args.CorrectAnswerJson,
            args.AcceptedSynonymsJson);

        ReadingQuestion? row = null;
        if (!string.IsNullOrWhiteSpace(args.Id))
            row = await db.ReadingQuestions.FirstOrDefaultAsync(q => q.Id == args.Id, ct);
        if (row is not null && !string.Equals(row.ReadingPartId, args.ReadingPartId, StringComparison.Ordinal))
            throw new InvalidOperationException("Existing Reading question belongs to a different part.");
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

    public async Task<bool> RemoveTextAsync(string paperId, string textId, string adminId, CancellationToken ct)
    {
        await EnsureReadingPaperAsync(paperId, ct);
        var row = await db.ReadingTexts
            .Include(t => t.Part)
            .FirstOrDefaultAsync(t => t.Id == textId && t.Part != null && t.Part.PaperId == paperId, ct);
        if (row is null) return false;
        db.ReadingTexts.Remove(row);
        await WriteAuditAsync("ReadingTextRemoved", textId, row.Title, adminId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveQuestionAsync(string paperId, string questionId, string adminId, CancellationToken ct)
    {
        await EnsureReadingPaperAsync(paperId, ct);
        var row = await db.ReadingQuestions
            .Include(q => q.Part)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.Part != null && q.Part.PaperId == paperId, ct);
        if (row is null) return false;
        db.ReadingQuestions.Remove(row);
        await WriteAuditAsync("ReadingQuestionRemoved", questionId, row.Stem, adminId, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderTextsAsync(string paperId, string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct)
    {
        await EnsurePartBelongsToPaperAsync(paperId, partId, ct);
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

    public async Task ReorderQuestionsAsync(string paperId, string partId, IReadOnlyList<string> orderedIds, string adminId, CancellationToken ct)
    {
        await EnsurePartBelongsToPaperAsync(paperId, partId, ct);
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

    public async Task<ReadingStructureManifest> ExportManifestAsync(string paperId, CancellationToken ct)
    {
        var structure = await GetAdminStructureAsync(paperId, ct);
        return new ReadingStructureManifest(structure.Parts
            .OrderBy(p => p.PartCode)
            .Select(part =>
            {
                var textOrderById = part.Texts.ToDictionary(t => t.Id, t => t.DisplayOrder);
                return new ReadingPartManifest(
                    part.PartCode,
                    part.TimeLimitMinutes,
                    part.Instructions,
                    part.Texts.OrderBy(t => t.DisplayOrder)
                        .Select(t => new ReadingTextManifest(
                            t.DisplayOrder,
                            t.Title,
                            t.Source,
                            t.BodyHtml,
                            t.WordCount,
                            t.TopicTag))
                        .ToList(),
                    part.Questions.OrderBy(q => q.DisplayOrder)
                        .Select(q => new ReadingQuestionManifest(
                            q.DisplayOrder,
                            q.Points,
                            q.QuestionType,
                            q.Stem,
                            q.OptionsJson,
                            q.CorrectAnswerJson,
                            q.AcceptedSynonymsJson,
                            q.CaseSensitive,
                            q.ExplanationMarkdown,
                            q.SkillTag,
                            q.ReadingTextId is not null && textOrderById.TryGetValue(q.ReadingTextId, out var order)
                                ? order
                                : null,
                            q.OptionDistractorsJson,
                            q.ReviewState))
                        .ToList());
            })
            .ToList());
    }

    public async Task<ReadingStructureImportResult> ImportManifestAsync(
        string paperId,
        ReadingStructureManifest manifest,
        bool replaceExisting,
        string adminId,
        CancellationToken ct)
    {
        if (manifest is null)
            throw new InvalidOperationException("Reading manifest is required.");
        ValidateManifest(manifest, replaceExisting);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EnsureCanonicalPartsAsync(paperId, ct);

        if (replaceExisting)
        {
            var hasAttempts = await db.ReadingAttempts.AsNoTracking()
                .AnyAsync(a => a.PaperId == paperId, ct);
            if (hasAttempts)
                throw new InvalidOperationException(
                    "Cannot replace Reading structure after learner attempts exist. Retire this paper and import into a new revision instead.");

            var existing = await db.ReadingParts
                .Where(p => p.PaperId == paperId)
                .Include(p => p.Texts)
                .Include(p => p.Questions)
                .ToListAsync(ct);
            db.ReadingQuestions.RemoveRange(existing.SelectMany(p => p.Questions));
            db.ReadingTexts.RemoveRange(existing.SelectMany(p => p.Texts));
            await WriteAuditAsync("ReadingStructureManifestCleared", paperId, "replaceExisting=true", adminId, ct);
            await db.SaveChangesAsync(ct);
        }

        foreach (var partManifest in manifest.Parts.OrderBy(p => p.PartCode))
        {
            var part = await UpsertPartAsync(new ReadingPartUpsert(
                paperId,
                partManifest.PartCode,
                partManifest.TimeLimitMinutes,
                partManifest.Instructions ?? string.Empty), adminId, ct);

            var textIdByDisplayOrder = new Dictionary<int, string>();
            foreach (var textManifest in partManifest.Texts.OrderBy(t => t.DisplayOrder))
            {
                var text = await UpsertTextAsync(new ReadingTextUpsert(
                    null,
                    part.Id,
                    textManifest.DisplayOrder,
                    textManifest.Title,
                    textManifest.Source,
                    textManifest.BodyHtml,
                    textManifest.WordCount,
                    textManifest.TopicTag), adminId, ct);
                textIdByDisplayOrder[text.DisplayOrder] = text.Id;
            }

            foreach (var questionManifest in partManifest.Questions.OrderBy(q => q.DisplayOrder))
            {
                var readingTextId = questionManifest.ReadingTextDisplayOrder is int textOrder
                    && textIdByDisplayOrder.TryGetValue(textOrder, out var textId)
                        ? textId
                        : null;

                await UpsertQuestionAsync(new ReadingQuestionUpsert(
                    null,
                    part.Id,
                    readingTextId,
                    questionManifest.DisplayOrder,
                    questionManifest.Points,
                    questionManifest.QuestionType,
                    questionManifest.Stem,
                    questionManifest.OptionsJson,
                    questionManifest.CorrectAnswerJson,
                    questionManifest.AcceptedSynonymsJson,
                    questionManifest.CaseSensitive,
                    questionManifest.ExplanationMarkdown,
                    questionManifest.SkillTag), adminId, ct);
            }

            // Phase 4 — patch the metadata fields the upsert DTO doesn't carry.
            // We do this in a second pass so we can match by (part, displayOrder)
            // without round-tripping new IDs through the upsert path.
            var importedQuestions = await db.ReadingQuestions
                .Where(q => q.ReadingPartId == part.Id)
                .ToListAsync(ct);
            foreach (var qm in partManifest.Questions)
            {
                var row = importedQuestions.FirstOrDefault(q => q.DisplayOrder == qm.DisplayOrder);
                if (row is null) continue;
                if (qm.OptionDistractorsJson is { Length: > 0 })
                    row.OptionDistractorsJson = qm.OptionDistractorsJson;
                row.ReviewState = qm.ReviewState;
            }
            await db.SaveChangesAsync(ct);
        }

        await WriteAuditAsync("ReadingStructureManifestImported", paperId,
            $"replaceExisting={replaceExisting}; parts={manifest.Parts.Count}", adminId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ReadingStructureImportResult(
            await GetAdminStructureAsync(paperId, ct),
            await ValidatePaperAsync(paperId, ct));
    }

    public async Task<ReadingValidationReport> ValidatePaperAsync(string paperId, CancellationToken ct)
    {
        var issues = new List<ReadingValidationIssue>();
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null)
        {
            return new(false,
                new[] { new ReadingValidationIssue("paper_missing", "error", "Paper not found.", null) },
                new(0, 0, 0, 0));
        }
        if (!string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("wrong_subtest", "error",
                $"Paper subtest is '{paper.SubtestCode}', expected 'reading'.", null));
        }

        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == paperId)
            .Include(p => p.Questions)
            .ToListAsync(ct);

        int partA = 0, partB = 0, partC = 0, totalPoints = 0;
        foreach (var part in parts)
        {
            var questionCount = part.Questions.Count;
            var (expected, expectedMinutes) = CanonicalShape[part.PartCode];
            var texts = await db.ReadingTexts.AsNoTracking()
                .Where(t => t.ReadingPartId == part.Id)
                .OrderBy(t => t.DisplayOrder)
                .ToListAsync(ct);
            if (part.TimeLimitMinutes != expectedMinutes)
            {
                issues.Add(new(
                    Code: $"part_{part.PartCode}_time_limit",
                    Severity: "error",
                    Message: $"Part {part.PartCode} has a {part.TimeLimitMinutes}-minute limit, expected {expectedMinutes} minute(s).",
                    TargetId: part.Id));
            }
            var expectedTextCount = CanonicalTextCounts[part.PartCode];
            if (texts.Count != expectedTextCount)
            {
                issues.Add(new(
                    Code: $"part_{part.PartCode}_text_count",
                    Severity: "error",
                    Message: $"Part {part.PartCode} has {texts.Count} text unit(s), expected {expectedTextCount}.",
                    TargetId: part.Id));
            }
            if (!HasContiguousDisplayOrders(texts.Select(t => t.DisplayOrder)))
            {
                issues.Add(new(
                    Code: $"part_{part.PartCode}_text_order",
                    Severity: "error",
                    Message: $"Part {part.PartCode} text display orders must be unique and contiguous from 1.",
                    TargetId: part.Id));
            }
            if (!HasContiguousDisplayOrders(part.Questions.Select(q => q.DisplayOrder)))
            {
                issues.Add(new(
                    Code: $"part_{part.PartCode}_question_order",
                    Severity: "error",
                    Message: $"Part {part.PartCode} question display orders must be unique and contiguous from 1.",
                    TargetId: part.Id));
            }
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
            var textIds = texts.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var q in part.Questions)
            {
                totalPoints += q.Points;
                if (q.Points != 1)
                {
                    issues.Add(new(
                        Code: "question_points_not_one",
                        Severity: "error",
                        Message: $"Part {part.PartCode} question {q.DisplayOrder} is worth {q.Points} point(s), expected 1.",
                        TargetId: q.Id));
                }
                if (!IsQuestionTypeAllowedForPart(part.PartCode, q.QuestionType))
                {
                    issues.Add(new(
                        Code: $"part_{part.PartCode}_question_type",
                        Severity: "error",
                        Message: $"Part {part.PartCode} question {q.DisplayOrder} uses {q.QuestionType}, which is not valid for this part.",
                        TargetId: q.Id));
                }
                if (part.PartCode is ReadingPartCode.B or ReadingPartCode.C
                    && (q.ReadingTextId is null || !textIds.Contains(q.ReadingTextId)))
                {
                    issues.Add(new(
                        Code: $"part_{part.PartCode}_question_text_required",
                        Severity: "error",
                        Message: $"Part {part.PartCode} question {q.DisplayOrder} must reference one of this part's text units.",
                        TargetId: q.Id));
                }
                if (part.PartCode == ReadingPartCode.A)
                {
                    var expectedType = ExpectedPartAQuestionType(q.DisplayOrder);
                    if (expectedType is null)
                    {
                        issues.Add(new(
                            Code: "part_A_question_order",
                            Severity: "error",
                            Message: $"Part A question {q.DisplayOrder} is outside the official 1-20 range.",
                            TargetId: q.Id));
                    }
                    else if (q.QuestionType != expectedType)
                    {
                        issues.Add(new(
                            Code: "part_A_question_sequence",
                            Severity: "error",
                            Message: $"Part A question {q.DisplayOrder} must be {expectedType}, not {q.QuestionType}.",
                            TargetId: q.Id));
                    }
                    if (!string.IsNullOrWhiteSpace(q.AcceptedSynonymsJson))
                    {
                        issues.Add(new(
                            Code: "part_A_synonyms_forbidden",
                            Severity: "error",
                            Message: $"Part A question {q.DisplayOrder} must not define accepted synonyms; strict marking uses the authored answer only.",
                            TargetId: q.Id));
                    }
                }
                try
                {
                    ValidateQuestionPayload(q.QuestionType, q.OptionsJson, q.CorrectAnswerJson, q.AcceptedSynonymsJson);
                }
                catch (Exception ex)
                {
                    issues.Add(new("question_payload_invalid", "error", ex.Message, q.Id));
                }
            }

            if (part.PartCode is ReadingPartCode.B or ReadingPartCode.C)
            {
                var expectedPerText = part.PartCode == ReadingPartCode.B ? 1 : 8;
                var questionsByText = part.Questions
                    .Where(q => q.ReadingTextId is not null)
                    .GroupBy(q => q.ReadingTextId!)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                foreach (var text in texts)
                {
                    var count = questionsByText.TryGetValue(text.Id, out var n) ? n : 0;
                    if (count != expectedPerText)
                    {
                        issues.Add(new(
                            Code: $"part_{part.PartCode}_questions_per_text",
                            Severity: "error",
                            Message: $"Part {part.PartCode} text {text.DisplayOrder} has {count} linked question(s), expected {expectedPerText}.",
                            TargetId: text.Id));
                    }
                }
            }

            if (part.PartCode == ReadingPartCode.A && questionCount > 0 && part.Questions.All(q => q.ReadingTextId is null))
            {
                issues.Add(new($"part_{part.PartCode}_no_texts", "warning",
                    $"No questions in Part {part.PartCode} reference a text — OK for matching, suspicious for MCQ.",
                    part.Id));
            }

            // Every text must have a source for copyright
            foreach (var t in texts.Where(x => string.IsNullOrWhiteSpace(x.Source)))
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

        // Phase 4 — every question must be Published before paper publish.
        // Anything else surfaces as a single warning per non-published item
        // so admins can use the validate report as a publish-readiness checklist.
        var unpublished = parts.SelectMany(p => p.Questions)
            .Where(q => q.ReviewState != ReadingReviewState.Published)
            .ToList();
        foreach (var q in unpublished)
        {
            issues.Add(new(
                Code: "question_not_published",
                Severity: "error",
                Message: $"Question {q.DisplayOrder} is in {q.ReviewState}; advance to Published before paper publish.",
                TargetId: q.Id));
        }

        var counts = new ReadingValidationCounts(partA, partB, partC, totalPoints);
        var isReady = !issues.Any(i => i.Severity == "error");
        return new ReadingValidationReport(isReady, issues, counts);
    }

    private static bool HasContiguousDisplayOrders(IEnumerable<int> orders)
    {
        var list = orders.OrderBy(x => x).ToList();
        if (list.Count == 0) return true;
        return list.SequenceEqual(Enumerable.Range(1, list.Count));
    }

    private static bool IsQuestionTypeAllowedForPart(ReadingPartCode partCode, ReadingQuestionType questionType) => partCode switch
    {
        ReadingPartCode.A => questionType is ReadingQuestionType.MatchingTextReference
            or ReadingQuestionType.ShortAnswer
            or ReadingQuestionType.SentenceCompletion,
        ReadingPartCode.B => questionType == ReadingQuestionType.MultipleChoice3,
        ReadingPartCode.C => questionType == ReadingQuestionType.MultipleChoice4,
        _ => false,
    };

    private static ReadingQuestionType? ExpectedPartAQuestionType(int displayOrder) => displayOrder switch
    {
        >= 1 and <= 7 => ReadingQuestionType.MatchingTextReference,
        >= 8 and <= 14 => ReadingQuestionType.ShortAnswer,
        >= 15 and <= 20 => ReadingQuestionType.SentenceCompletion,
        _ => null,
    };

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
                    if (correct.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException("Matching CorrectAnswerJson must be a single string letter.");
                    var ans = correct.GetString() ?? string.Empty;
                    if (!new[] { "A", "B", "C", "D" }.Contains(ans, StringComparer.Ordinal))
                        throw new InvalidOperationException("Matching answer must be one of A,B,C,D.");
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

    private static void ValidateManifest(ReadingStructureManifest manifest, bool replaceExisting)
    {
        if (manifest.Parts is null || manifest.Parts.Count == 0)
            throw new InvalidOperationException("Reading manifest must contain at least one part.");

        var duplicatePart = manifest.Parts
            .GroupBy(p => p.PartCode)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicatePart is not null)
            throw new InvalidOperationException($"Reading manifest contains duplicate Part {duplicatePart.Key} sections.");

        if (replaceExisting)
        {
            foreach (var code in CanonicalShape.Keys)
            {
                if (!manifest.Parts.Any(p => p.PartCode == code))
                    throw new InvalidOperationException($"Replacement manifest must include Part {code}.");
            }
        }

        foreach (var part in manifest.Parts)
        {
            if (part.Texts is null)
                throw new InvalidOperationException($"Part {part.PartCode} must include a texts array.");
            if (part.Questions is null)
                throw new InvalidOperationException($"Part {part.PartCode} must include a questions array.");
            if (!CanonicalShape.ContainsKey(part.PartCode))
                throw new InvalidOperationException($"Unsupported Reading part code {part.PartCode}.");
            if (part.Texts.Any(t => t.DisplayOrder <= 0))
                throw new InvalidOperationException($"Part {part.PartCode} contains a text with an invalid display order.");
            if (part.Questions.Any(q => q.DisplayOrder <= 0))
                throw new InvalidOperationException($"Part {part.PartCode} contains a question with an invalid display order.");
            if (part.Texts.GroupBy(t => t.DisplayOrder).Any(g => g.Count() > 1))
                throw new InvalidOperationException($"Part {part.PartCode} contains duplicate text display orders.");
            if (part.Questions.GroupBy(q => q.DisplayOrder).Any(g => g.Count() > 1))
                throw new InvalidOperationException($"Part {part.PartCode} contains duplicate question display orders.");
            foreach (var question in part.Questions.Where(q => q.ReadingTextDisplayOrder is not null))
            {
                if (!part.Texts.Any(t => t.DisplayOrder == question.ReadingTextDisplayOrder))
                    throw new InvalidOperationException(
                        $"Part {part.PartCode} question {question.DisplayOrder} references missing text display order {question.ReadingTextDisplayOrder}.");
            }
        }
    }

    private async Task EnsureReadingPaperAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null)
            throw new InvalidOperationException("Paper not found.");
        if (!string.Equals(paper.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Paper subtest is '{paper.SubtestCode}', expected 'reading'.");
    }

    private async Task EnsurePartBelongsToReadingPaperAsync(string partId, CancellationToken ct)
    {
        var paperId = await db.ReadingParts.AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => p.PaperId)
            .FirstOrDefaultAsync(ct);
        if (paperId is null)
            throw new InvalidOperationException("Reading part not found.");
        await EnsureReadingPaperAsync(paperId, ct);
    }

    private async Task EnsurePartBelongsToPaperAsync(string paperId, string partId, CancellationToken ct)
    {
        await EnsureReadingPaperAsync(paperId, ct);
        var exists = await db.ReadingParts.AsNoTracking()
            .AnyAsync(p => p.Id == partId && p.PaperId == paperId, ct);
        if (!exists)
            throw new InvalidOperationException("Reading part does not belong to this paper.");
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
