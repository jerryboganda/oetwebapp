using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using System.Text;

namespace OetLearner.Api.Services;

public partial class AdminService
{
    // ════════════════════════════════════════════
    //  Grammar Lessons
    // ════════════════════════════════════════════

    public async Task<object> GetGrammarLessonsAsync(
        string? profession, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.GrammarLessons.AsQueryable();

        if (!string.IsNullOrWhiteSpace(profession))
            query = query.Where(g => g.ExamTypeCode == profession);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(g => g.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => g.Title.Contains(search) || (g.Description != null && g.Description.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(query, g => g.Id, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total,
            page,
            pageSize,
            items = items.Select(g => new
            {
                g.Id,
                g.Title,
                profession = g.ExamTypeCode,
                g.Category,
                g.Description,
                difficulty = g.Level,
                estimatedDurationMinutes = g.EstimatedMinutes,
                g.SortOrder,
                g.Status
            })
        };
    }

    public async Task<object> GetGrammarLessonDetailAsync(string lessonId, CancellationToken ct)
    {
        var g = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        return new
        {
            g.Id,
            g.Title,
            profession = g.ExamTypeCode,
            g.Description,
            content = g.ContentHtml,
            difficulty = g.Level,
            estimatedDurationMinutes = g.EstimatedMinutes,
            g.SortOrder,
            g.Category,
            g.PrerequisiteLessonId,
            g.ExercisesJson,
            g.Status
        };
    }

    public async Task<object> CreateGrammarLessonAsync(
        string adminId, string adminName, AdminGrammarLessonCreateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var id = $"GRM-{Guid.NewGuid():N}"[..12];
        var entity = new GrammarLesson
        {
            Id = id,
            Title = request.Title,
            ExamTypeCode = request.ProfessionId ?? "oet",
            Category = request.Category ?? string.Empty,
            Description = request.Description,
            ContentHtml = request.Content,
            Level = request.Difficulty ?? "intermediate",
            EstimatedMinutes = request.EstimatedDurationMinutes ?? 15,
            SortOrder = request.SortOrder ?? 0,
            Status = "draft"
        };
        db.GrammarLessons.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "GrammarLesson", id, $"Created grammar lesson: {request.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, entity.Title, entity.Status };
    }

    public async Task<object> UpdateGrammarLessonAsync(
        string adminId, string adminName, string lessonId, AdminGrammarLessonUpdateRequest request, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        if (request.Title is not null) entity.Title = request.Title;
        if (request.ProfessionId is not null) entity.ExamTypeCode = request.ProfessionId;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.Description is not null) entity.Description = request.Description;
        if (request.Content is not null) entity.ContentHtml = request.Content;
        if (request.Difficulty is not null) entity.Level = request.Difficulty;
        if (request.EstimatedDurationMinutes is not null) entity.EstimatedMinutes = request.EstimatedDurationMinutes.Value;
        if (request.SortOrder is not null) entity.SortOrder = request.SortOrder.Value;
        if (request.Status is not null) entity.Status = request.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "GrammarLesson", lessonId, $"Updated grammar lesson: {entity.Title}", ct);

        return new { id = lessonId, entity.Status };
    }

    public async Task<object> ArchiveGrammarLessonAsync(
        string adminId, string adminName, string lessonId, CancellationToken ct)
    {
        var entity = await db.GrammarLessons.FirstOrDefaultAsync(x => x.Id == lessonId, ct)
            ?? throw ApiException.NotFound("GRAMMAR_NOT_FOUND", $"Grammar lesson '{lessonId}' not found.");

        entity.Status = "archived";
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "GrammarLesson", lessonId, $"Archived grammar lesson: {entity.Title}", ct);

        return new { id = lessonId, status = "archived" };
    }

    // ════════════════════════════════════════════
    //  Vocabulary Items
    // ════════════════════════════════════════════

    public async Task<object> GetVocabularyItemsAsync(
        string? profession, string? category, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.VocabularyTerms.AsQueryable();

        if (!string.IsNullOrWhiteSpace(profession))
            query = query.Where(v => v.ProfessionId == profession);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(v => v.Category == category);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(v => v.Term.Contains(search) || v.Definition.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(query, v => v.Id, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total,
            page,
            pageSize,
            items = items.Select(v => new
            {
                v.Id,
                v.Term,
                v.Definition,
                v.ProfessionId,
                v.Category,
                v.Difficulty,
                v.ExampleSentence,
                v.Status
            })
        };
    }

    public async Task<object> GetVocabularyItemDetailAsync(string itemId, CancellationToken ct)
    {
        var v = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw ApiException.NotFound("VOCABULARY_NOT_FOUND", $"Vocabulary item '{itemId}' not found.");

        return new
        {
            v.Id,
            v.Term,
            v.Definition,
            v.ExampleSentence,
            v.ContextNotes,
            v.ExamTypeCode,
            v.ProfessionId,
            v.Category,
            v.Difficulty,
            v.IpaPronunciation,
            v.AudioUrl,
            v.AudioMediaAssetId,
            v.ImageUrl,
            v.SynonymsJson,
            v.CollocationsJson,
            v.RelatedTermsJson,
            v.SourceProvenance,
            v.Status,
            v.CreatedAt,
            v.UpdatedAt
        };
    }

    public async Task<object> CreateVocabularyItemAsync(
        string adminId, string adminName, AdminVocabularyItemCreateRequestV2 request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        ValidateVocabularyPayload(request.Term, request.Definition, request.ExampleSentence, request.Category,
            request.Status, request.SourceProvenance, request.IpaPronunciation, request.AudioUrl);

        var id = $"VOC-{Guid.NewGuid():N}"[..12];
        var entity = new VocabularyTerm
        {
            Id = id,
            Term = request.Term.Trim(),
            Definition = request.Definition.Trim(),
            ExampleSentence = request.ExampleSentence.Trim(),
            ContextNotes = string.IsNullOrWhiteSpace(request.ContextNotes) ? null : request.ContextNotes.Trim(),
            ExamTypeCode = string.IsNullOrWhiteSpace(request.ExamTypeCode) ? "oet" : request.ExamTypeCode,
            ProfessionId = request.ProfessionId,
            Category = request.Category.Trim(),
            Difficulty = request.Difficulty ?? "medium",
            IpaPronunciation = request.IpaPronunciation,
            AudioUrl = request.AudioUrl,
            AudioMediaAssetId = request.AudioMediaAssetId,
            ImageUrl = request.ImageUrl,
            SynonymsJson = JsonSupport.Serialize(request.Synonyms ?? Array.Empty<string>()),
            CollocationsJson = JsonSupport.Serialize(request.Collocations ?? Array.Empty<string>()),
            RelatedTermsJson = JsonSupport.Serialize(request.RelatedTerms ?? Array.Empty<string>()),
            SourceProvenance = request.SourceProvenance,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.VocabularyTerms.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "VocabularyTerm", id, $"Created vocabulary item: {request.Term}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, entity.Term, entity.Status };
    }

    public async Task<object> UpdateVocabularyItemAsync(
        string adminId, string adminName, string itemId, AdminVocabularyItemUpdateRequestV2 request, CancellationToken ct)
    {
        var entity = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw ApiException.NotFound("VOCABULARY_NOT_FOUND", $"Vocabulary item '{itemId}' not found.");

        if (request.Term is not null) entity.Term = request.Term.Trim();
        if (request.Definition is not null) entity.Definition = request.Definition.Trim();
        if (request.ExampleSentence is not null) entity.ExampleSentence = request.ExampleSentence.Trim();
        if (request.ContextNotes is not null) entity.ContextNotes = request.ContextNotes.Trim();
        if (request.ExamTypeCode is not null) entity.ExamTypeCode = request.ExamTypeCode;
        if (request.ProfessionId is not null) entity.ProfessionId = request.ProfessionId;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.IpaPronunciation is not null) entity.IpaPronunciation = request.IpaPronunciation;
        if (request.AudioUrl is not null) entity.AudioUrl = request.AudioUrl;
        if (request.AudioMediaAssetId is not null) entity.AudioMediaAssetId = request.AudioMediaAssetId;
        if (request.ImageUrl is not null) entity.ImageUrl = request.ImageUrl;
        if (request.Synonyms is not null) entity.SynonymsJson = JsonSupport.Serialize(request.Synonyms);
        if (request.Collocations is not null) entity.CollocationsJson = JsonSupport.Serialize(request.Collocations);
        if (request.RelatedTerms is not null) entity.RelatedTermsJson = JsonSupport.Serialize(request.RelatedTerms);
        if (request.SourceProvenance is not null) entity.SourceProvenance = request.SourceProvenance;

        if (request.Status is not null)
        {
            if (request.Status == "active")
            {
                EnforceVocabularyPublishGate(entity);
            }
            entity.Status = request.Status;
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "VocabularyTerm", itemId, $"Updated vocabulary item: {entity.Term}", ct);

        return new { id = itemId, entity.Status };
    }

    private static void ValidateVocabularyPayload(
        string term, string definition, string example, string category,
        string? status, string? sourceProvenance, string? ipa, string? audioUrl)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw ApiException.Validation("VOCAB_TERM_REQUIRED", "Term is required.");
        if (string.IsNullOrWhiteSpace(definition))
            throw ApiException.Validation("VOCAB_DEFINITION_REQUIRED", "Definition is required.");
        if (string.IsNullOrWhiteSpace(example))
            throw ApiException.Validation("VOCAB_EXAMPLE_REQUIRED", "Example sentence is required.");
        if (string.IsNullOrWhiteSpace(category))
            throw ApiException.Validation("VOCAB_CATEGORY_REQUIRED", "Category is required.");

        if (status == "active")
        {
            EnforceVocabularyPublishGate(term, definition, example, category, sourceProvenance, ipa, audioUrl);
        }
    }

    private static void EnforceVocabularyPublishGate(VocabularyTerm e)
        => EnforceVocabularyPublishGate(e.Term, e.Definition, e.ExampleSentence, e.Category, e.SourceProvenance, e.IpaPronunciation, e.AudioUrl);

    private static void EnforceVocabularyPublishGate(
        string term, string definition, string example, string category,
        string? sourceProvenance, string? ipa, string? audioUrl)
    {
        var missing = new List<ApiFieldError>();
        if (string.IsNullOrWhiteSpace(term)) missing.Add(new ApiFieldError("term", "REQUIRED", "Term is required."));
        if (string.IsNullOrWhiteSpace(definition)) missing.Add(new ApiFieldError("definition", "REQUIRED", "Definition is required."));
        if (string.IsNullOrWhiteSpace(example)) missing.Add(new ApiFieldError("exampleSentence", "REQUIRED", "Example sentence is required."));
        if (string.IsNullOrWhiteSpace(category)) missing.Add(new ApiFieldError("category", "REQUIRED", "Category is required."));
        if (string.IsNullOrWhiteSpace(sourceProvenance)) missing.Add(new ApiFieldError("sourceProvenance", "REQUIRED", "Source provenance is required before publishing."));

        var medicalCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "medical", "anatomy", "pharmacology", "procedures", "symptoms", "conditions", "diagnostics"
        };
        if (!string.IsNullOrWhiteSpace(category) && medicalCategories.Contains(category)
            && string.IsNullOrWhiteSpace(ipa) && string.IsNullOrWhiteSpace(audioUrl))
        {
            missing.Add(new ApiFieldError("pronunciation", "REQUIRED_EITHER",
                "Medical categories require either ipaPronunciation or audioUrl before publishing."));
        }

        if (missing.Count > 0)
            throw ApiException.Validation("VOCAB_PUBLISH_GATE",
                "Cannot publish: vocabulary term is missing required fields.", missing);
    }

    public async Task<object> GetVocabularyCategoriesAdminAsync(
        string? examTypeCode, string? professionId, CancellationToken ct)
    {
        var query = db.VocabularyTerms.AsQueryable();
        if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrEmpty(professionId)) query = query.Where(t => t.ProfessionId == professionId);
        var rows = await query
            .GroupBy(t => new { t.Category, t.Status })
            .Select(g => new { g.Key.Category, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var groups = rows
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                category = g.Key,
                active = g.Where(x => x.Status == "active").Sum(x => x.Count),
                draft = g.Where(x => x.Status == "draft").Sum(x => x.Count),
                archived = g.Where(x => x.Status == "archived").Sum(x => x.Count),
                total = g.Sum(x => x.Count)
            })
            .OrderByDescending(x => x.total)
            .ThenBy(x => x.category)
            .ToList();

        return new { examTypeCode, professionId, categories = groups };
    }

    public async Task<object> DeleteVocabularyItemAsync(
        string adminId, string adminName, string itemId, CancellationToken ct)
    {
        var entity = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw ApiException.NotFound("VOCABULARY_NOT_FOUND", $"Vocabulary item '{itemId}' not found.");

        // Soft delete preferred — learner vocab lists may reference this term.
        var referenced = await db.LearnerVocabularies.AnyAsync(lv => lv.TermId == itemId, ct);
        if (referenced)
        {
            entity.Status = "archived";
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Archived", "VocabularyTerm", itemId,
                $"Archived vocabulary item (referenced by learners): {entity.Term}", ct);
            return new { id = itemId, archived = true, deleted = false };
        }

        db.VocabularyTerms.Remove(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Deleted", "VocabularyTerm", itemId,
            $"Deleted vocabulary item: {entity.Term}", ct);

        return new { id = itemId, archived = false, deleted = true };
    }

    // ── CSV import (RFC-4180 aware) ─────────────────────────────────────

    public async Task<AdminVocabularyImportPreviewResponse> PreviewVocabularyImportAsync(
        IFormFile file, CancellationToken ct)
    {
        var rows = await ParseCsvAsync(file, ct);
        var preview = new List<AdminVocabularyImportPreviewRow>(rows.Count);
        var warnings = new List<string>();
        var termsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid = 0; var invalid = 0; var dups = 0;

        foreach (var r in rows)
        {
            var (ok, err) = ValidateCsvRow(r);
            var duplicate = r.Term is not null && !termsSeen.Add(r.Term.ToLowerInvariant());
            if (duplicate) { dups++; err = "Duplicate row in this file."; ok = false; }
            if (ok) valid++; else invalid++;
            preview.Add(new AdminVocabularyImportPreviewRow(
                LineNumber: r.LineNumber,
                Valid: ok,
                Term: r.Term,
                Definition: r.Definition,
                Category: r.Category,
                Difficulty: r.Difficulty,
                ProfessionId: r.ProfessionId,
                ExampleSentence: r.ExampleSentence,
                Error: err));
        }

        if (valid == 0 && rows.Count > 0) warnings.Add("No rows passed validation.");
        if (dups > 0) warnings.Add($"{dups} duplicate rows detected in the file.");

        return new AdminVocabularyImportPreviewResponse(
            TotalRows: rows.Count,
            ValidRows: valid,
            InvalidRows: invalid,
            DuplicateRows: dups,
            Rows: preview,
            Warnings: warnings);
    }

    public async Task<AdminVocabularyImportResponse> BulkImportVocabularyV2Async(
        string adminId, string adminName, IFormFile file, bool dryRun, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var rows = await ParseCsvAsync(file, ct);

        var imported = 0; var skipped = 0; var duplicates = 0; var failed = 0;
        var errors = new List<string>();

        foreach (var r in rows)
        {
            var (ok, err) = ValidateCsvRow(r);
            if (!ok)
            {
                failed++;
                if (errors.Count < 20) errors.Add($"Row {r.LineNumber}: {err}");
                continue;
            }

            var existing = await db.VocabularyTerms.FirstOrDefaultAsync(
                t => t.Term == r.Term && t.ExamTypeCode == (r.ExamTypeCode ?? "oet") && t.ProfessionId == r.ProfessionId, ct);
            if (existing is not null)
            {
                duplicates++;
                continue;
            }

            if (!dryRun)
            {
                var id = $"VOC-{Guid.NewGuid():N}"[..12];
                db.VocabularyTerms.Add(new VocabularyTerm
                {
                    Id = id,
                    Term = r.Term!.Trim(),
                    Definition = r.Definition!.Trim(),
                    ExampleSentence = string.IsNullOrWhiteSpace(r.ExampleSentence) ? string.Empty : r.ExampleSentence!.Trim(),
                    ContextNotes = r.ContextNotes,
                    ExamTypeCode = r.ExamTypeCode ?? "oet",
                    ProfessionId = r.ProfessionId,
                    Category = r.Category ?? "medical",
                    Difficulty = r.Difficulty ?? "medium",
                    IpaPronunciation = r.IpaPronunciation,
                    AudioUrl = r.AudioUrl,
                    SynonymsJson = string.IsNullOrWhiteSpace(r.SynonymsRaw) ? "[]" : JsonSupport.Serialize(SplitList(r.SynonymsRaw!)),
                    CollocationsJson = "[]",
                    RelatedTermsJson = "[]",
                    SourceProvenance = string.IsNullOrWhiteSpace(r.SourceProvenance)
                        ? $"CSV import by {adminName} on {DateTimeOffset.UtcNow:yyyy-MM-dd}"
                        : r.SourceProvenance,
                    Status = "draft",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            imported++;
        }

        skipped = duplicates + failed;
        if (!dryRun)
        {
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Bulk Import", "VocabularyTerm", "bulk",
                $"Imported {imported} vocabulary items, skipped {skipped} (dup={duplicates}, failed={failed}).", ct);
            await CommitIfOwnedAsync(tx, ct);
        }

        return new AdminVocabularyImportResponse(imported, skipped, duplicates, failed, errors);
    }

    private static IReadOnlyList<string> SplitList(string raw)
        => raw.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record CsvVocabRow(
        int LineNumber,
        string? Term,
        string? Definition,
        string? ExampleSentence,
        string? Category,
        string? Difficulty,
        string? ProfessionId,
        string? ExamTypeCode,
        string? IpaPronunciation,
        string? AudioUrl,
        string? ContextNotes,
        string? SynonymsRaw,
        string? SourceProvenance);

    private static (bool Ok, string? Error) ValidateCsvRow(CsvVocabRow r)
    {
        if (string.IsNullOrWhiteSpace(r.Term)) return (false, "Empty 'term'.");
        if (string.IsNullOrWhiteSpace(r.Definition)) return (false, "Empty 'definition'.");
        if (r.Term.Length > 128) return (false, "Term exceeds 128 characters.");
        if (r.Definition.Length > 1024) return (false, "Definition exceeds 1024 characters.");
        return (true, null);
    }

    private static async Task<List<CsvVocabRow>> ParseCsvAsync(IFormFile file, CancellationToken ct)
    {
        var rows = new List<CsvVocabRow>();
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, leaveOpen: false);
        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw ApiException.Validation("INVALID_CSV", "CSV file is empty or missing header row.");

        var headers = ParseVocabCsvLine(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                var idx = Array.IndexOf(headers, n.ToLowerInvariant());
                if (idx >= 0) return idx;
            }
            return -1;
        }

        var ti = Col("term"); var di = Col("definition");
        if (ti < 0 || di < 0)
            throw ApiException.Validation("INVALID_CSV", "CSV must have 'Term' and 'Definition' columns.");
        var ei = Col("examplesentence", "example");
        var ci = Col("category");
        var dfi = Col("difficulty");
        var pi = Col("professionid", "profession");
        var exi = Col("examtypecode", "examtype");
        var ipi = Col("ipapronunciation", "ipa", "pronunciation");
        var ai = Col("audiourl", "audio");
        var cni = Col("contextnotes", "context");
        var sy = Col("synonyms");
        var sp = Col("sourceprovenance", "provenance");

        string? line;
        var lineNum = 1;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = ParseVocabCsvLine(line);
            string? Get(int idx) => idx >= 0 && cols.Count > idx ? cols[idx].Trim() : null;

            rows.Add(new CsvVocabRow(
                LineNumber: lineNum,
                Term: Get(ti),
                Definition: Get(di),
                ExampleSentence: Get(ei),
                Category: Get(ci),
                Difficulty: Get(dfi),
                ProfessionId: Get(pi),
                ExamTypeCode: Get(exi),
                IpaPronunciation: Get(ipi),
                AudioUrl: Get(ai),
                ContextNotes: Get(cni),
                SynonymsRaw: Get(sy),
                SourceProvenance: Get(sp)));
        }
        return rows;
    }

    /// <summary>RFC 4180 CSV line parser with quote escaping.</summary>
    private static List<string> ParseVocabCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    public async Task<object> BulkImportVocabularyAsync(
        string adminId, string adminName, IFormFile file, CancellationToken ct)
    {
        // Backward-compat thin wrapper — defaults to dryRun=false.
        var res = await BulkImportVocabularyV2Async(adminId, adminName, file, dryRun: false, ct);
        return new
        {
            imported = res.Imported,
            skipped = res.Skipped,
            duplicates = res.Duplicates,
            failedRows = res.FailedRows,
            errors = res.Errors.Take(20),
        };
    }

    // ════════════════════════════════════════════
    //  Conversation Templates
    // ════════════════════════════════════════════

    public async Task<object> GetConversationTemplatesAsync(
        string? profession, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ConversationTemplates.AsQueryable();

        if (!string.IsNullOrWhiteSpace(profession))
            query = query.Where(t => t.ProfessionId == profession);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Title.Contains(search) || t.Scenario.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(query, t => t.CreatedAt, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total, page, pageSize,
            items = items.Select(t => new
            {
                t.Id, t.Title, t.TaskTypeCode, t.ProfessionId, t.Difficulty,
                estimatedDurationSeconds = t.EstimatedDurationSeconds,
                t.Status, t.PublishedAtUtc, t.CreatedAt, t.UpdatedAt,
            })
        };
    }

    public async Task<object> GetConversationTemplateDetailAsync(string templateId, CancellationToken ct)
    {
        var t = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        return new
        {
            t.Id, t.Title, t.TaskTypeCode, t.ProfessionId, t.Scenario, t.RoleDescription,
            t.PatientContext, t.ExpectedOutcomes, t.Difficulty,
            estimatedDurationSeconds = t.EstimatedDurationSeconds,
            objectives = JsonSupport.Deserialize<string[]>(t.ObjectivesJson, Array.Empty<string>()),
            expectedRedFlags = JsonSupport.Deserialize<string[]>(t.ExpectedRedFlagsJson, Array.Empty<string>()),
            keyVocabulary = JsonSupport.Deserialize<string[]>(t.KeyVocabularyJson, Array.Empty<string>()),
            patientVoice = JsonSupport.Deserialize<Dictionary<string, object?>>(t.PatientVoiceJson, new Dictionary<string, object?>()),
            t.Status, t.PublishedAtUtc, t.CreatedAt, t.UpdatedAt,
            t.CreatedByUserId, t.UpdatedByUserId,
        };
    }

    public async Task<object> CreateConversationTemplateAsync(
        string adminId, string adminName, AdminConversationTemplateCreateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var id = $"CVT-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;
        var entity = new ConversationTemplate
        {
            Id = id,
            Title = request.Title,
            TaskTypeCode = string.IsNullOrWhiteSpace(request.TaskTypeCode) ? "oet-roleplay" : request.TaskTypeCode,
            ProfessionId = request.ProfessionId,
            Scenario = request.Scenario,
            RoleDescription = request.RoleDescription,
            PatientContext = request.PatientContext,
            ExpectedOutcomes = request.ExpectedOutcomes,
            ObjectivesJson = JsonSupport.Serialize(request.Objectives ?? Array.Empty<string>()),
            ExpectedRedFlagsJson = JsonSupport.Serialize(request.ExpectedRedFlags ?? Array.Empty<string>()),
            KeyVocabularyJson = JsonSupport.Serialize(request.KeyVocabulary ?? Array.Empty<string>()),
            PatientVoiceJson = request.PatientVoice is null ? "{}" : JsonSupport.Serialize(request.PatientVoice),
            Difficulty = request.Difficulty ?? "medium",
            EstimatedDurationSeconds = request.EstimatedDurationSeconds ?? 300,
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = adminId,
            UpdatedByUserId = adminId,
        };
        db.ConversationTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "ConversationTemplate", id, $"Created conversation template: {request.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, entity.Title, entity.Status };
    }

    public async Task<object> UpdateConversationTemplateAsync(
        string adminId, string adminName, string templateId, AdminConversationTemplateUpdateRequest request, CancellationToken ct)
    {
        var entity = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        if (request.Title is not null) entity.Title = request.Title;
        if (request.TaskTypeCode is not null) entity.TaskTypeCode = request.TaskTypeCode;
        if (request.ProfessionId is not null) entity.ProfessionId = request.ProfessionId;
        if (request.Scenario is not null) entity.Scenario = request.Scenario;
        if (request.RoleDescription is not null) entity.RoleDescription = request.RoleDescription;
        if (request.PatientContext is not null) entity.PatientContext = request.PatientContext;
        if (request.ExpectedOutcomes is not null) entity.ExpectedOutcomes = request.ExpectedOutcomes;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.EstimatedDurationSeconds is not null) entity.EstimatedDurationSeconds = request.EstimatedDurationSeconds.Value;
        if (request.Objectives is not null) entity.ObjectivesJson = JsonSupport.Serialize(request.Objectives);
        if (request.ExpectedRedFlags is not null) entity.ExpectedRedFlagsJson = JsonSupport.Serialize(request.ExpectedRedFlags);
        if (request.KeyVocabulary is not null) entity.KeyVocabularyJson = JsonSupport.Serialize(request.KeyVocabulary);
        if (request.PatientVoice is not null) entity.PatientVoiceJson = JsonSupport.Serialize(request.PatientVoice);
        if (request.Status is not null) entity.Status = request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedByUserId = adminId;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "ConversationTemplate", templateId, $"Updated conversation template: {entity.Title}", ct);

        return new { id = templateId, entity.Status };
    }

    public async Task<object> PublishConversationTemplateAsync(
        string adminId, string adminName, string templateId, CancellationToken ct)
    {
        var entity = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(entity.Title)) issues.Add("title_required");
        if (string.IsNullOrWhiteSpace(entity.Scenario)) issues.Add("scenario_required");
        if (string.IsNullOrWhiteSpace(entity.RoleDescription)) issues.Add("role_description_required");
        if (string.IsNullOrWhiteSpace(entity.PatientContext)) issues.Add("patient_context_required");
        var objectives = JsonSupport.Deserialize<string[]>(entity.ObjectivesJson, Array.Empty<string>());
        if (objectives.Length < 3) issues.Add("objectives_min_3");
        if (entity.EstimatedDurationSeconds <= 0) issues.Add("duration_required");
        if (!new[] { "oet-roleplay", "oet-handover" }.Contains(entity.TaskTypeCode, StringComparer.OrdinalIgnoreCase))
            issues.Add("task_type_invalid");
        if (issues.Count > 0)
            throw ApiException.Validation("PUBLISH_GATE_FAILED", string.Join(", ", issues));

        entity.Status = "published";
        entity.PublishedAtUtc = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedByUserId = adminId;
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "ConversationTemplate", templateId, $"Published conversation template: {entity.Title}", ct);

        return new { id = templateId, status = "published", publishedAtUtc = entity.PublishedAtUtc };
    }

    public async Task<object> ArchiveConversationTemplateAsync(
        string adminId, string adminName, string templateId, CancellationToken ct)
    {
        var entity = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        entity.Status = "archived";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedByUserId = adminId;
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "ConversationTemplate", templateId, $"Archived conversation template: {entity.Title}", ct);

        return new { id = templateId, status = "archived" };
    }

    // ════════════════════════════════════════════
    //  Pronunciation Drills
    // ════════════════════════════════════════════

    public async Task<object> GetPronunciationDrillsAsync(
        string? profession, string? difficulty, string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PronunciationDrills.AsQueryable();

        if (!string.IsNullOrWhiteSpace(profession))
            query = query.Where(d => d.Profession == profession);
        if (!string.IsNullOrWhiteSpace(difficulty))
            query = query.Where(d => d.Difficulty == difficulty);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.Label.Contains(search) || d.TargetPhoneme.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(query, d => d.Id, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total,
            page,
            pageSize,
            items = items.Select(d => new
            {
                d.Id,
                word = d.Label,
                label = d.Label,
                phoneticTranscription = d.TargetPhoneme,
                targetPhoneme = d.TargetPhoneme,
                d.Profession,
                d.Focus,
                d.PrimaryRuleId,
                d.AudioModelUrl,
                d.AudioModelAssetId,
                d.Difficulty,
                d.Status,
                d.OrderIndex,
                d.UpdatedAt
            })
        };
    }

    public async Task<object> GetPronunciationDrillDetailAsync(string drillId, CancellationToken ct)
    {
        var d = await db.PronunciationDrills.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", $"Pronunciation drill '{drillId}' not found.");

        return new
        {
            d.Id,
            word = d.Label,
            label = d.Label,
            phoneticTranscription = d.TargetPhoneme,
            targetPhoneme = d.TargetPhoneme,
            d.Profession,
            d.Focus,
            d.PrimaryRuleId,
            audioUrl = d.AudioModelUrl,
            d.AudioModelUrl,
            d.AudioModelAssetId,
            d.ExampleWordsJson,
            d.MinimalPairsJson,
            d.SentencesJson,
            d.TipsHtml,
            d.Difficulty,
            d.Status,
            d.OrderIndex,
            d.CreatedAt,
            d.UpdatedAt
        };
    }

    public async Task<object> CreatePronunciationDrillAsync(
        string adminId, string adminName, AdminPronunciationDrillCreateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var id = $"PRN-{Guid.NewGuid():N}"[..12];
        var entity = new PronunciationDrill
        {
            Id = id,
            Label = request.Word,
            TargetPhoneme = request.PhoneticTranscription ?? "",
            Profession = string.IsNullOrWhiteSpace(request.Profession) ? "all" : request.Profession!,
            Focus = string.IsNullOrWhiteSpace(request.Focus) ? "phoneme" : request.Focus!,
            PrimaryRuleId = request.PrimaryRuleId,
            AudioModelUrl = request.AudioUrl,
            AudioModelAssetId = request.AudioModelAssetId,
            ExampleWordsJson = request.ExampleWordsJson ?? "[]",
            MinimalPairsJson = request.MinimalPairsJson ?? "[]",
            SentencesJson = request.SentencesJson ?? "[]",
            TipsHtml = request.TipsHtml ?? "",
            Difficulty = request.Difficulty ?? "medium",
            Status = string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status!,
            OrderIndex = request.OrderIndex ?? 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.PronunciationDrills.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "PronunciationDrill", id, $"Created pronunciation drill: {request.Word}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, word = entity.Label, entity.Status };
    }

    public async Task<object> UpdatePronunciationDrillAsync(
        string adminId, string adminName, string drillId, AdminPronunciationDrillUpdateRequest request, CancellationToken ct)
    {
        var entity = await db.PronunciationDrills.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", $"Pronunciation drill '{drillId}' not found.");

        if (request.Word is not null) entity.Label = request.Word;
        if (request.PhoneticTranscription is not null) entity.TargetPhoneme = request.PhoneticTranscription;
        if (request.Profession is not null) entity.Profession = request.Profession;
        if (request.Focus is not null) entity.Focus = request.Focus;
        if (request.PrimaryRuleId is not null) entity.PrimaryRuleId = request.PrimaryRuleId;
        if (request.AudioUrl is not null) entity.AudioModelUrl = request.AudioUrl;
        if (request.AudioModelAssetId is not null) entity.AudioModelAssetId = request.AudioModelAssetId;
        if (request.ExampleWordsJson is not null) entity.ExampleWordsJson = request.ExampleWordsJson;
        if (request.MinimalPairsJson is not null) entity.MinimalPairsJson = request.MinimalPairsJson;
        if (request.SentencesJson is not null) entity.SentencesJson = request.SentencesJson;
        if (request.TipsHtml is not null) entity.TipsHtml = request.TipsHtml;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.Status is not null)
        {
            if (request.Status == "active")
            {
                // Publish gate: require minimum content.
                if (string.IsNullOrWhiteSpace(entity.TargetPhoneme)
                    || string.IsNullOrWhiteSpace(entity.Label)
                    || string.IsNullOrWhiteSpace(entity.TipsHtml)
                    || JsonSupport.Deserialize(entity.ExampleWordsJson, new List<string>()).Count < 3
                    || JsonSupport.Deserialize(entity.SentencesJson, new List<string>()).Count < 1)
                {
                    throw ApiException.Validation("DRILL_PUBLISH_GATE",
                        "Cannot publish: drill requires phoneme, label, tips, at least 3 example words, and at least 1 sentence.");
                }
            }
            entity.Status = request.Status;
        }
        if (request.OrderIndex.HasValue) entity.OrderIndex = request.OrderIndex.Value;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "PronunciationDrill", drillId, $"Updated pronunciation drill: {entity.Label}", ct);

        return new { id = drillId, entity.Status };
    }

    public async Task<object> ArchivePronunciationDrillAsync(
        string adminId, string adminName, string drillId, CancellationToken ct)
    {
        var entity = await db.PronunciationDrills.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", $"Pronunciation drill '{drillId}' not found.");

        entity.Status = "archived";
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "PronunciationDrill", drillId, $"Archived pronunciation drill: {entity.Label}", ct);

        return new { id = drillId, status = "archived" };
    }

    // ════════════════════════════════════════════
    //  Notification Templates
    // ════════════════════════════════════════════

    public async Task<object> GetNotificationTemplatesAsync(
        string? channel, string? category, int page, int pageSize, CancellationToken ct)
    {
        var query = db.NotificationTemplates.AsQueryable();

        if (!string.IsNullOrWhiteSpace(channel))
            query = query.Where(n => n.Channel == channel);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(n => n.Category == category);

        var total = await query.CountAsync(ct);
        var items = await ToOrderedListDescendingAsync(query, n => n.UpdatedAt, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total,
            page,
            pageSize,
            items = items.Select(n => new
            {
                n.Id,
                n.EventKey,
                n.Channel,
                n.Category,
                n.SubjectTemplate,
                n.IsActive,
                n.UpdatedAt
            })
        };
    }

    public async Task<object> GetNotificationTemplateDetailAsync(string templateId, CancellationToken ct)
    {
        var n = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("NOTIFICATION_TEMPLATE_NOT_FOUND", $"Notification template '{templateId}' not found.");

        return new
        {
            n.Id,
            n.EventKey,
            n.Channel,
            n.Category,
            n.SubjectTemplate,
            n.BodyTemplate,
            n.IsActive,
            n.CreatedAt,
            n.UpdatedAt
        };
    }

    public async Task<object> CreateNotificationTemplateAsync(
        string adminId, string adminName, AdminNotificationTemplateCreateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var id = $"NTF-{Guid.NewGuid():N}"[..12];
        var now = DateTimeOffset.UtcNow;
        var entity = new NotificationTemplate
        {
            Id = id,
            EventKey = request.EventKey,
            Channel = request.Channel,
            Category = request.Category,
            SubjectTemplate = request.SubjectTemplate,
            BodyTemplate = request.BodyTemplate,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.NotificationTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "NotificationTemplate", id, $"Created notification template: {request.EventKey}/{request.Channel}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, entity.EventKey, entity.Channel, entity.IsActive };
    }

    public async Task<object> UpdateNotificationTemplateAsync(
        string adminId, string adminName, string templateId, AdminNotificationTemplateUpdateRequest request, CancellationToken ct)
    {
        var entity = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("NOTIFICATION_TEMPLATE_NOT_FOUND", $"Notification template '{templateId}' not found.");

        if (request.SubjectTemplate is not null) entity.SubjectTemplate = request.SubjectTemplate;
        if (request.BodyTemplate is not null) entity.BodyTemplate = request.BodyTemplate;
        if (request.IsActive is not null) entity.IsActive = request.IsActive.Value;
        if (request.Category is not null) entity.Category = request.Category;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "NotificationTemplate", templateId, $"Updated notification template: {entity.EventKey}/{entity.Channel}", ct);

        return new { id = templateId, entity.IsActive };
    }

    public async Task<object> DeleteNotificationTemplateAsync(
        string adminId, string adminName, string templateId, CancellationToken ct)
    {
        var entity = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("NOTIFICATION_TEMPLATE_NOT_FOUND", $"Notification template '{templateId}' not found.");

        db.NotificationTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Deleted", "NotificationTemplate", templateId, $"Deleted notification template: {entity.EventKey}/{entity.Channel}", ct);

        return new { id = templateId, deleted = true };
    }

    // ════════════════════════════════════════════
    //  Free Tier Management
    // ════════════════════════════════════════════

    public async Task<object> GetFreeTierConfigAsync(CancellationToken ct)
    {
        var config = await db.FreeTierConfigs.FirstOrDefaultAsync(ct);
        if (config is null)
        {
            config = new FreeTierConfig
            {
                Id = $"FTC-{Guid.NewGuid():N}"[..12],
                Enabled = true,
                MaxWritingAttempts = 3,
                MaxSpeakingAttempts = 3,
                MaxReadingAttempts = 5,
                MaxListeningAttempts = 5,
                TrialDurationDays = 7,
                ShowUpgradePrompts = true,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.FreeTierConfigs.Add(config);
            await db.SaveChangesAsync(ct);
        }

        return new
        {
            config.Id,
            config.Enabled,
            config.MaxWritingAttempts,
            config.MaxSpeakingAttempts,
            config.MaxReadingAttempts,
            config.MaxListeningAttempts,
            config.TrialDurationDays,
            config.ShowUpgradePrompts,
            config.UpdatedAt
        };
    }

    public async Task<object> UpdateFreeTierConfigAsync(
        string adminId, string adminName, AdminFreeTierConfigUpdateRequest request, CancellationToken ct)
    {
        var config = await db.FreeTierConfigs.FirstOrDefaultAsync(ct);
        if (config is null)
        {
            config = new FreeTierConfig
            {
                Id = $"FTC-{Guid.NewGuid():N}"[..12],
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.FreeTierConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.MaxWritingAttempts = request.MaxWritingAttempts;
        config.MaxSpeakingAttempts = request.MaxSpeakingAttempts;
        config.MaxReadingAttempts = request.MaxReadingAttempts;
        config.MaxListeningAttempts = request.MaxListeningAttempts;
        config.TrialDurationDays = request.TrialDurationDays;
        config.ShowUpgradePrompts = request.ShowUpgradePrompts;
        config.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "FreeTierConfig", config.Id, "Updated free tier configuration", ct);

        return new
        {
            config.Id,
            config.Enabled,
            config.MaxWritingAttempts,
            config.MaxSpeakingAttempts,
            config.MaxReadingAttempts,
            config.MaxListeningAttempts,
            config.TrialDurationDays,
            config.ShowUpgradePrompts,
            config.UpdatedAt
        };
    }

    public async Task<object> GetFreeTierUsageStatsAsync(int page, int pageSize, CancellationToken ct)
    {
        var config = await db.FreeTierConfigs.FirstOrDefaultAsync(ct);
        var trialDays = config?.TrialDurationDays ?? 7;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-trialDays);

        var freeUsersQuery = db.Set<ApplicationUserAccount>()
            .Where(u => u.Role == "learner" && u.DeletedAt == null && u.CreatedAt >= cutoff);

        var totalFreeUsers = await freeUsersQuery.CountAsync(ct);

        var recentSignups = await freeUsersQuery
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.CreatedAt
            })
            .ToListAsync(ct);

        return new
        {
            totalFreeUsers,
            trialDurationDays = trialDays,
            enabled = config?.Enabled ?? true,
            page,
            pageSize,
            users = recentSignups
        };
    }
}
