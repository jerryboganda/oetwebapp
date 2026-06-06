using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Common;
using System.Security.Cryptography;
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
            Description = request.Description ?? string.Empty,
            ContentHtml = request.Content ?? string.Empty,
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
        string? profession, string? category, string? status, string? search, int page, int pageSize, CancellationToken ct,
        string? recallSet = null)
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

        var normalisedSet = OetLearner.Api.Domain.RecallSetCodes.Normalise(recallSet);
        if (normalisedSet is not null)
        {
            var needle = $"\"{normalisedSet}\"";
            query = query.Where(v => v.RecallSetCodesJson.Contains(needle));
        }

        var total = await query.CountAsync(ct);
        var freePreviewTotal = await db.VocabularyTerms.CountAsync(v => v.IsFreePreview, ct);
        var items = await ToOrderedListDescendingAsync(query, v => v.Id, ct, skip: (page - 1) * pageSize, take: pageSize);

        return new
        {
            total,
            page,
            pageSize,
            freePreviewTotal,
            items = items.Select(v => new
            {
                v.Id,
                v.Term,
                v.Definition,
                v.ProfessionId,
                v.Category,
                v.ExampleSentence,
                v.AmericanSpelling,
                v.Status,
                v.IsFreePreview,
                hasAudio = v.AudioMediaAssetId != null || !string.IsNullOrWhiteSpace(v.AudioUrl),
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
            v.IpaPronunciation,
            v.AmericanSpelling,
            v.AudioUrl,
            v.AudioSlowUrl,
            v.AudioSentenceUrl,
            v.AudioMediaAssetId,
            v.ImageUrl,
            v.SynonymsJson,
            v.CollocationsJson,
            v.RelatedTermsJson,
            v.RecallSetCodesJson,
            v.CommonMistakesJson,
            v.SimilarSoundingJson,
            v.SourceProvenance,
            v.Status,
            v.IsFreePreview,
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
            ExamTypeCode = ExamCodes.Normalize(request.ExamTypeCode),
            ProfessionId = request.ProfessionId,
            Category = request.Category.Trim(),
            IpaPronunciation = request.IpaPronunciation,
            AmericanSpelling = CleanOptional(request.AmericanSpelling),
            AudioUrl = CleanOptional(request.AudioUrl),
            AudioSlowUrl = CleanOptional(request.AudioSlowUrl),
            AudioSentenceUrl = CleanOptional(request.AudioSentenceUrl),
            AudioMediaAssetId = CleanOptional(request.AudioMediaAssetId),
            ImageUrl = request.ImageUrl,
            SynonymsJson = JsonSupport.Serialize(request.Synonyms ?? Array.Empty<string>()),
            CollocationsJson = JsonSupport.Serialize(request.Collocations ?? Array.Empty<string>()),
            RelatedTermsJson = JsonSupport.Serialize(request.RelatedTerms ?? Array.Empty<string>()),
            RecallSetCodesJson = JsonSupport.Serialize(NormaliseRecallSetCodes(request.RecallSetCodes)),
            CommonMistakesJson = JsonSupport.Serialize(request.CommonMistakes ?? Array.Empty<string>()),
            SimilarSoundingJson = JsonSupport.Serialize(request.SimilarSounding ?? Array.Empty<string>()),
            SourceProvenance = request.SourceProvenance,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "draft" : request.Status,
            IsFreePreview = request.IsFreePreview ?? false,
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
        if (request.ExamTypeCode is not null) entity.ExamTypeCode = ExamCodes.Normalize(request.ExamTypeCode);
        if (request.ProfessionId is not null) entity.ProfessionId = request.ProfessionId;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.IpaPronunciation is not null) entity.IpaPronunciation = request.IpaPronunciation;
        if (request.AmericanSpelling is not null) entity.AmericanSpelling = CleanOptional(request.AmericanSpelling);
        if (request.AudioUrl is not null) entity.AudioUrl = CleanOptional(request.AudioUrl);
        if (request.AudioSlowUrl is not null) entity.AudioSlowUrl = CleanOptional(request.AudioSlowUrl);
        if (request.AudioSentenceUrl is not null) entity.AudioSentenceUrl = CleanOptional(request.AudioSentenceUrl);
        if (request.AudioMediaAssetId is not null) entity.AudioMediaAssetId = CleanOptional(request.AudioMediaAssetId);
        if (request.ImageUrl is not null) entity.ImageUrl = request.ImageUrl;
        if (request.Synonyms is not null) entity.SynonymsJson = JsonSupport.Serialize(request.Synonyms);
        if (request.Collocations is not null) entity.CollocationsJson = JsonSupport.Serialize(request.Collocations);
        if (request.RelatedTerms is not null) entity.RelatedTermsJson = JsonSupport.Serialize(request.RelatedTerms);
        if (request.RecallSetCodes is not null) entity.RecallSetCodesJson = JsonSupport.Serialize(NormaliseRecallSetCodes(request.RecallSetCodes));
        if (request.CommonMistakes is not null) entity.CommonMistakesJson = JsonSupport.Serialize(request.CommonMistakes);
        if (request.SimilarSounding is not null) entity.SimilarSoundingJson = JsonSupport.Serialize(request.SimilarSounding);
        if (request.SourceProvenance is not null) entity.SourceProvenance = request.SourceProvenance;
        if (request.IsFreePreview is not null) entity.IsFreePreview = request.IsFreePreview.Value;

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

    private static IReadOnlyList<string> NormaliseRecallSetCodes(IReadOnlyList<string>? codes)
    {
        if (codes is null || codes.Count == 0) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(codes.Count);
        foreach (var raw in codes)
        {
            var normalised = OetLearner.Api.Domain.RecallSetCodes.Normalise(raw);
            if (normalised is null)
                throw ApiException.Validation("VOCAB_RECALL_SET_INVALID",
                    $"Unknown recall-set code '{raw}'. Allowed: {string.Join(", ", OetLearner.Api.Domain.RecallSetCodes.All)}.");
            if (seen.Add(normalised)) result.Add(normalised);
        }
        return result;
    }

    private static void EnforceVocabularyPublishGate(
        string term, string definition, string example, string category,
        string? sourceProvenance, string? ipa, string? audioUrl)
    {
        var missing = new List<ApiFieldError>();
        if (string.IsNullOrWhiteSpace(term)) missing.Add(new ApiFieldError("term", "REQUIRED", "Term is required."));
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
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
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

    /// <summary>
    /// Admin view of the recall-set registry: canonical metadata plus per-set
    /// counts split by status (active / draft / archived).
    /// </summary>
    public async Task<object> GetRecallSetsAdminAsync(
        string? examTypeCode, string? professionId, CancellationToken ct)
    {
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
        var query = db.VocabularyTerms.AsQueryable();
        if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrEmpty(professionId)) query = query.Where(t => t.ProfessionId == professionId);

        var rows = await query
            .Where(t => t.RecallSetCodesJson != null && t.RecallSetCodesJson != "[]")
            .Select(t => new { t.RecallSetCodesJson, t.Status })
            .ToListAsync(ct);

        var counts = new Dictionary<string, (int active, int draft, int archived)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(row.RecallSetCodesJson) ?? new List<string>();
                foreach (var raw in list)
                {
                    var c = OetLearner.Api.Domain.RecallSetCodes.Normalise(raw);
                    if (c is null) continue;
                    (int active, int draft, int archived) t = counts.TryGetValue(c, out var current)
                        ? current
                        : (0, 0, 0);
                    counts[c] = row.Status switch
                    {
                        "active" => (t.active + 1, t.draft, t.archived),
                        "draft" => (t.active, t.draft + 1, t.archived),
                        "archived" => (t.active, t.draft, t.archived + 1),
                        _ => t,
                    };
                }
            }
            catch { /* malformed row ignored */ }
        }

        // Read tags from the DB-managed RecallSetTags table (seeded with the
        // 3 canonical codes on first boot; admins can add/edit/archive more
        // from /admin/content/vocabulary/recall-set-tags). Fall back to the
        // static RecallSetCodes.Metadata only if the table is empty so the UI
        // never appears blank in an unseeded test environment.
        var tagRows = await db.RecallSetTags.AsNoTracking()
            .Where(t => t.IsActive)
            .Where(t => t.ExamTypeCode == null
                        || examTypeCode == null
                        || t.ExamTypeCode == examTypeCode)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.DisplayName)
            .ToListAsync(ct);

        var sets = (tagRows.Count > 0
            ? tagRows.Select(t => new
            {
                code = t.Code,
                displayName = t.DisplayName,
                shortLabel = t.ShortLabel ?? t.Code,
                description = t.Description ?? string.Empty,
                sortOrder = t.SortOrder,
            })
            : OetLearner.Api.Domain.RecallSetCodes.Metadata
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    code = m.Code,
                    displayName = m.DisplayName,
                    shortLabel = (string?)m.ShortLabel,
                    description = (string?)m.Description ?? string.Empty,
                    sortOrder = m.SortOrder,
                }))
            .Select(s =>
            {
                var (active, draft, archived) = counts.TryGetValue(s.code, out var n) ? n : (0, 0, 0);
                return new
                {
                    s.code,
                    s.displayName,
                    s.shortLabel,
                    s.description,
                    s.sortOrder,
                    active,
                    draft,
                    archived,
                    total = active + draft + archived,
                };
            })
            .ToList();

        return new { examTypeCode, professionId, sets };
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

    public async Task<AdminVocabularyBulkDeleteResponse> DeleteVocabularyItemsBulkAsync(
        string adminId, string adminName, AdminVocabularyBulkDeleteRequest request, CancellationToken ct)
    {
        var requestedIds = request.ItemIds
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedIds.Count == 0)
            throw ApiException.Validation("VOCABULARY_BULK_DELETE_EMPTY", "Select at least one vocabulary item to delete.");

        if (requestedIds.Count > 1000)
            throw ApiException.Validation("VOCABULARY_BULK_DELETE_LIMIT", "Bulk delete is limited to 1000 vocabulary items at a time.");

        var deleted = 0;
        var archived = 0;
        var errors = new List<string>();

        foreach (var itemId in requestedIds)
        {
            var entity = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (entity is null)
            {
                errors.Add($"{itemId}: not found");
                continue;
            }

            var referenced = await db.LearnerVocabularies.AnyAsync(lv => lv.TermId == itemId, ct);
            if (referenced)
            {
                entity.Status = "archived";
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                await LogAuditAsync(adminId, adminName, "Archived", "VocabularyTerm", itemId,
                    $"Bulk archived vocabulary item (referenced by learners): {entity.Term}", ct);
                archived++;
                continue;
            }

            var term = entity.Term;
            db.VocabularyTerms.Remove(entity);
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Deleted", "VocabularyTerm", itemId,
                $"Bulk deleted vocabulary item: {term}", ct);
            deleted++;
        }

        return new AdminVocabularyBulkDeleteResponse(
            TotalRequested: request.ItemIds.Count,
            Deleted: deleted,
            Archived: archived,
            Failed: errors.Count,
            Errors: errors);
    }

    // ── Bulk activate (publish) ─────────────────────────────────────────

    public async Task<object> BulkActivateVocabularyAsync(
        string adminId, string adminName, IReadOnlyList<string> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
            throw ApiException.Validation("VOCABULARY_BULK_ACTIVATE_EMPTY", "Select at least one vocabulary item to publish.");
        if (itemIds.Count > 2000)
            throw ApiException.Validation("VOCABULARY_BULK_ACTIVATE_LIMIT", "Bulk publish is limited to 2000 vocabulary items at a time.");

        var ids = itemIds.Select(id => id.Trim()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await db.VocabularyTerms.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        var activated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var entity in entities)
        {
            if (entity.Status == "active") { skipped++; continue; }
            try
            {
                EnforceVocabularyPublishGate(entity);
                entity.Status = "active";
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                activated++;
            }
            catch (ApiException ex)
            {
                errors.Add($"{entity.Term}: {ex.Message}");
            }
        }

        if (activated > 0)
            await db.SaveChangesAsync(ct);

        return new { totalRequested = itemIds.Count, activated, skipped, failed = errors.Count, errors };
    }

    // ── Bulk free-preview toggle ────────────────────────────────────────

    /// <summary>
    /// Bulk set or clear the free-preview flag on the given vocabulary terms.
    /// Free-preview terms are the only terms a non-subscribed learner can
    /// access in the Recall Vocabulary Bank. Admin-curated — no automatic cap.
    /// </summary>
    public async Task<AdminVocabularyBulkPreviewResponse> SetVocabularyFreePreviewBulkAsync(
        string adminId, string adminName, AdminVocabularyBulkPreviewRequest request, CancellationToken ct)
    {
        var errors = new List<string>();
        var ids = (request.ItemIds ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count > 5000)
            throw ApiException.Validation("VOCABULARY_BULK_PREVIEW_LIMIT", "Bulk free-preview is limited to 5000 vocabulary items at a time.");

        var updated = 0;
        if (ids.Count > 0)
        {
            var entities = await db.VocabularyTerms.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var found = entities.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in ids.Where(id => !found.Contains(id)))
                errors.Add($"Item '{missing}' not found.");

            foreach (var entity in entities)
            {
                if (entity.IsFreePreview == request.IsFreePreview) continue;
                entity.IsFreePreview = request.IsFreePreview;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync(ct);
                await LogAuditAsync(adminId, adminName,
                    request.IsFreePreview ? "EnabledFreePreview" : "DisabledFreePreview",
                    "VocabularyTerm", string.Join(",", found.Take(50)),
                    $"Bulk free-preview={request.IsFreePreview} applied to {updated} term(s).", ct);
            }
        }

        var freePreviewTotal = await db.VocabularyTerms.CountAsync(t => t.IsFreePreview, ct);
        return new AdminVocabularyBulkPreviewResponse(ids.Count, updated, errors.Count, freePreviewTotal, errors);
    }

    // ── Bulk archive ────────────────────────────────────────────────────

    public async Task<object> BulkArchiveVocabularyAsync(
        string adminId, string adminName, IReadOnlyList<string> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
            throw ApiException.Validation("VOCABULARY_BULK_ARCHIVE_EMPTY", "Select at least one vocabulary item to archive.");
        if (itemIds.Count > 2000)
            throw ApiException.Validation("VOCABULARY_BULK_ARCHIVE_LIMIT", "Bulk archive is limited to 2000 vocabulary items at a time.");

        var ids = itemIds.Select(id => id.Trim()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await db.VocabularyTerms.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        var archived = 0;
        var skipped = 0;

        foreach (var entity in entities)
        {
            if (entity.Status == "archived") { skipped++; continue; }
            entity.Status = "archived";
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            archived++;
        }

        if (archived > 0)
            await db.SaveChangesAsync(ct);

        return new { totalRequested = itemIds.Count, archived, skipped };
    }

    // ── Bulk set to draft ───────────────────────────────────────────────

    public async Task<object> BulkDraftVocabularyAsync(
        string adminId, string adminName, IReadOnlyList<string> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
            throw ApiException.Validation("VOCABULARY_BULK_DRAFT_EMPTY", "Select at least one vocabulary item.");
        if (itemIds.Count > 2000)
            throw ApiException.Validation("VOCABULARY_BULK_DRAFT_LIMIT", "Bulk draft is limited to 2000 items at a time.");

        var ids = itemIds.Select(id => id.Trim()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entities = await db.VocabularyTerms.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        var drafted = 0;
        var skipped = 0;

        foreach (var entity in entities)
        {
            if (entity.Status == "draft") { skipped++; continue; }
            entity.Status = "draft";
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            drafted++;
        }

        if (drafted > 0)
            await db.SaveChangesAsync(ct);

        return new { totalRequested = itemIds.Count, drafted, skipped };
    }

    // ── Audio generation progress (global) ──────────────────────────────

    public async Task<object> GetVocabularyAudioProgressAsync(CancellationToken ct)
    {
        var total = await db.VocabularyTerms.CountAsync(ct);
        var withAudio = await db.VocabularyTerms.CountAsync(t => t.AudioMediaAssetId != null || (t.AudioUrl != null && t.AudioUrl != ""), ct);
        var pending = total - withAudio;

        return new { total, withAudio, pending, percentComplete = total > 0 ? Math.Round((double)withAudio / total * 100, 1) : 100.0 };
    }

    /// <summary>
    /// Resume audio generation for ALL terms without audio — regardless of
    /// recall set membership. Use when the background worker stalled (e.g.
    /// after a container restart) and the normal backfill endpoint skips
    /// recall-set terms.
    /// </summary>
    public async Task<object> ResumeVocabularyAudioAsync(CancellationToken ct)
    {
        if (vocabularyAudioQueue is null)
            return new { enqueued = 0, skipped = "queue-not-configured" };

        var rows = await db.VocabularyTerms.AsNoTracking()
            .Where(t => t.AudioMediaAssetId == null
                && (t.AudioUrl == null || t.AudioUrl == ""))
            .OrderBy(t => t.Id)
            .Take(5000)
            .Select(t => new { t.Id, t.Term })
            .ToListAsync(ct);

        var enqueued = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Term)) continue;
            await vocabularyAudioQueue.EnqueueAsync(
                new OetLearner.Api.Services.Vocabulary.VocabularyAudioJob(
                    TermId: row.Id,
                    Text: row.Term,
                    Voice: null,
                    Locale: "en-GB",
                    BatchId: string.Empty),
                ct);
            enqueued++;
        }
        return new { enqueued };
    }

    // ── CSV import (RFC-4180 aware) ─────────────────────────────────────

    public async Task<AdminVocabularyImportPreviewResponse> PreviewVocabularyImportAsync(
        IFormFile file, string? importBatchId, string? recallSetCode, CancellationToken ct)
    {
        // Recall set tag is a required categorisation for the bulk upload —
        // every imported row inherits this practice-collection label so the
        // admin can filter/maintain them later. Validate that the chosen
        // code corresponds to a known (active) row in RecallSetTags or one of
        // the canonical static codes (in case the DB hasn't been seeded yet).
        await EnsureRecallSetCodeOrThrowAsync(recallSetCode, ct);
        var batchId = NormalizeImportBatchId(importBatchId);
        var rows = await ParseCsvAsync(file, ct);
        var validation = await BuildVocabularyImportValidationContextAsync(ct);
        var preview = new List<AdminVocabularyImportPreviewRow>(rows.Count);
        var warnings = new List<string>();
        // Maps dedupe-key → line number of FIRST occurrence so subsequent
        // duplicates can cite where the original lives in the file.
        var firstSeenLine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var valid = 0; var invalid = 0; var dups = 0; var existingConflicts = 0;

        foreach (var r in rows)
        {
            var (ok, err) = ValidateCsvRow(r, batchId, validation);
            if (ok)
            {
                var duplicateKey = PreviewDuplicateKey(r);
                if (firstSeenLine.TryGetValue(duplicateKey, out var firstLine))
                {
                    // In-CSV duplicate. Silently deduped — first occurrence
                    // wins; emit a warning with the stable code so admins can
                    // see why the row was skipped. The publish path is not
                    // affected because no DB row is written for skipped dups.
                    // Duplicates are NOT counted as invalid — they are a
                    // separate category and should not block dry-run/commit.
                    dups++;
                    err = $"duplicate-in-csv (duplicate of line {firstLine})";
                    warnings.Add($"line {r.LineNumber}: duplicate-in-csv (duplicate of line {firstLine})");
                    // Mark row as not-valid for display purposes but don't
                    // count it as "invalid" — it's just a duplicate.
                    preview.Add(new AdminVocabularyImportPreviewRow(
                        LineNumber: r.LineNumber,
                        Valid: false,
                        Term: r.Term,
                        Definition: r.Definition,
                        Category: r.Category,
                        ProfessionId: r.ProfessionId,
                        AmericanSpelling: r.AmericanSpelling,
                        ExampleSentence: r.ExampleSentence,
                        Error: err));
                    continue;
                }
                else
                {
                    firstSeenLine[duplicateKey] = r.LineNumber;
                }
            }
            if (ok)
            {
                var existing = await FindExistingVocabularyTermForImportAsync(BuildImportKey(r), ct);
                if (existing is not null && string.IsNullOrWhiteSpace(r.ExistingId))
                {
                    existingConflicts++;
                    dups++;
                    err = "duplicate-in-db: existing term will have frequency incremented on commit.";
                    // DB duplicates are not invalid — they get frequency-merged.
                    preview.Add(new AdminVocabularyImportPreviewRow(
                        LineNumber: r.LineNumber,
                        Valid: false,
                        Term: r.Term,
                        Definition: r.Definition,
                        Category: r.Category,
                        ProfessionId: r.ProfessionId,
                        AmericanSpelling: r.AmericanSpelling,
                        ExampleSentence: r.ExampleSentence,
                        Error: err));
                    continue;
                }
                else if (existing is not null && !string.IsNullOrWhiteSpace(r.ExistingId))
                {
                    // Row has an Id column — will be upserted (updated) on commit.
                }
            }
            if (ok) valid++; else invalid++;
            preview.Add(new AdminVocabularyImportPreviewRow(
                LineNumber: r.LineNumber,
                Valid: ok,
                Term: r.Term,
                Definition: r.Definition,
                Category: r.Category,
                ProfessionId: r.ProfessionId,
                AmericanSpelling: r.AmericanSpelling,
                ExampleSentence: r.ExampleSentence,
                Error: err));
        }

        if (valid == 0 && rows.Count > 0) warnings.Add("No rows passed validation.");
        if (dups > 0) warnings.Add($"{dups} duplicate or conflict row(s) detected.");
        if (existingConflicts > 0) warnings.Add($"{existingConflicts} row(s) already exist in the database and require conflict review before update.");

        return new AdminVocabularyImportPreviewResponse(
            ImportBatchId: batchId,
            TotalRows: rows.Count,
            ValidRows: valid,
            InvalidRows: invalid,
            DuplicateRows: dups,
            Rows: preview,
            Warnings: warnings);
    }

    public async Task<AdminVocabularyImportResponse> BulkImportVocabularyV2Async(
        string adminId, string adminName, IFormFile file, bool dryRun, string? importBatchId, string? recallSetCode, CancellationToken ct)
    {
        // Mandatory recall set tag — see PreviewVocabularyImportAsync.
        var normalisedRecallSetCode = await EnsureRecallSetCodeOrThrowAsync(recallSetCode, ct);
        var batchId = NormalizeImportBatchId(importBatchId);
        var fileSha256 = await ComputeFileSha256Async(file, ct);
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var rows = await ParseCsvAsync(file, ct);
        var validation = await BuildVocabularyImportValidationContextAsync(ct);

        var imported = 0; var skipped = 0; var duplicates = 0; var failed = 0;
        var inCsvDuplicates = 0;
        var errors = new List<string>();
        var cleanRows = new List<CsvVocabRow>();
        // Maps dedupe-key → line number of FIRST occurrence so subsequent
        // duplicates can cite where the original lives in the file.
        var firstSeenLine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            var (ok, err) = ValidateCsvRow(r, batchId, validation);
            if (!ok)
            {
                failed++;
                if (errors.Count < 20) errors.Add($"Row {r.LineNumber}: {err}");
                continue;
            }

            var duplicateKey = PreviewDuplicateKey(r);
            if (firstSeenLine.TryGetValue(duplicateKey, out var firstLine))
            {
                // In-CSV duplicate. Silently dedupe — first occurrence wins.
                // Counted under `duplicates` for API-shape parity with the
                // preview surface, but tracked separately so the commit gate
                // doesn't block on a class of skip the operator can't fix.
                duplicates++;
                inCsvDuplicates++;
                if (errors.Count < 20)
                    errors.Add($"Row {r.LineNumber}: duplicate-in-csv: Skipped duplicate of line {firstLine}.");
                continue;
            }
            firstSeenLine[duplicateKey] = r.LineNumber;

            var existing = await FindExistingVocabularyTermForImportAsync(BuildImportKey(r), ct);
            if (existing is not null && string.IsNullOrWhiteSpace(r.ExistingId))
            {
                // Increment frequency count on commit only (not dry-run) to
                // avoid double-counting since both passes process the same CSV.
                if (!dryRun)
                {
                    existing.ExamFrequencyCount++;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                }
                duplicates++;
                continue;
            }

            cleanRows.Add(r);
            imported++;
        }

        skipped = duplicates + failed;
        // Blocking skips exclude in-CSV duplicates — those are silently
        // deduped per the relaxed import contract (Term-only / header-less
        // CSVs may legitimately include the same recall term twice).
        var blockingSkips = failed + (duplicates - inCsvDuplicates);
        if (dryRun)
        {
            if (blockingSkips == 0)
            {
                await UpsertVocabularyImportDryRunAsync(batchId, fileSha256, imported, ct);
                await db.SaveChangesAsync(ct);
                await CommitIfOwnedAsync(tx, ct);
            }

            return new AdminVocabularyImportResponse(batchId, imported, skipped, duplicates, failed, errors);
        }

        await RequireMatchingVocabularyImportDryRunAsync(batchId, fileSha256, imported, ct);

        if (blockingSkips > 0)
        {
            throw ApiException.Validation(
                "VOCABULARY_IMPORT_NOT_CLEAN",
                "Commit is blocked because the import no longer has a clean dry run. Re-run preview and dry run for the same batch before committing.");
        }

        await EnsureVocabularyImportCommitLedgerAvailableAsync(batchId, ct);

        var importedTermIds = new List<string>(cleanRows.Count);
        foreach (var r in cleanRows)
        {
            // Upsert: if the CSV has an Id column and the term exists, update it
            if (!string.IsNullOrWhiteSpace(r.ExistingId))
            {
                var existingById = await db.VocabularyTerms.FirstOrDefaultAsync(
                    t => t.Id == r.ExistingId.Trim() && t.Status != "archived", ct);
                if (existingById is not null)
                {
                    UpdateVocabularyTermFromCsvRow(existingById, r, batchId, normalisedRecallSetCode);
                    importedTermIds.Add(existingById.Id);
                    continue;
                }
            }
            var id = $"VOC-{Guid.NewGuid():N}"[..12];
            db.VocabularyTerms.Add(CreateVocabularyTermFromCsvRow(r, id, batchId, normalisedRecallSetCode));
            importedTermIds.Add(id);
        }

        if (!dryRun)
        {
            await CreateVocabularyImportCommitLedgerAsync(batchId, fileSha256, importedTermIds, ct);
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Bulk Import", "VocabularyTerm", "bulk",
                $"Batch {batchId}: imported {imported} vocabulary items, skipped {skipped} (dup={duplicates}, failed={failed}).", ct);
            await CommitIfOwnedAsync(tx, ct);

            // Phase 3 — kick off background TTS generation for the freshly
            // inserted terms. Skip rows that already carry audio metadata
            // (the CSV may include AudioUrl).
            if (vocabularyAudioQueue is not null && importedTermIds.Count > 0)
            {
                var newRows = await db.VocabularyTerms.AsNoTracking()
                    .Where(t => importedTermIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Term, t.AudioUrl, t.AudioMediaAssetId, t.RecallSetCodesJson })
                    .ToListAsync(ct);
                foreach (var row in newRows)
                {
                    if (!string.IsNullOrWhiteSpace(row.AudioUrl)
                        || !string.IsNullOrWhiteSpace(row.AudioMediaAssetId))
                        continue;
                    await vocabularyAudioQueue.EnqueueAsync(
                        new OetLearner.Api.Services.Vocabulary.VocabularyAudioJob(
                            TermId: row.Id,
                            Text: row.Term,
                            Voice: null,
                            Locale: "en-GB",
                            BatchId: batchId,
                            ProviderName: string.IsNullOrWhiteSpace(TryExtractRecallSetCodeFromStored(row.RecallSetCodesJson))
                                ? null
                                : "elevenlabs"),
                        ct);
                }
            }
        }

        return new AdminVocabularyImportResponse(batchId, imported, skipped, duplicates, failed, errors);
    }

    /// <summary>
    /// Phase 3 — enqueue background TTS jobs for vocabulary terms that
    /// have neither <see cref="VocabularyTerm.AudioMediaAssetId"/> nor
    /// <see cref="VocabularyTerm.AudioUrl"/>. When <paramref name="batchId"/>
    /// is non-empty we restrict to rows whose <c>SourceProvenance</c>
    /// starts with <c>batch={id};</c>. Otherwise we sweep up to 5000 rows
    /// platform-wide.
    /// </summary>
    public async Task<object> EnqueueVocabularyAudioBackfillAsync(string? batchId, CancellationToken ct)
    {
        if (vocabularyAudioQueue is null)
            return new { enqueued = 0, skipped = "queue-not-configured" };

        IQueryable<VocabularyTerm> query = db.VocabularyTerms.AsNoTracking()
            .Where(t => t.AudioMediaAssetId == null
                && (t.AudioUrl == null || t.AudioUrl == "")
                && (t.RecallSetCodesJson == null || t.RecallSetCodesJson == "" || t.RecallSetCodesJson == "[]"));

        var normalised = string.IsNullOrWhiteSpace(batchId) ? null : batchId.Trim();
        if (!string.IsNullOrEmpty(normalised))
        {
            var prefix = $"batch={normalised};";
            query = query.Where(t => t.SourceProvenance != null && t.SourceProvenance.StartsWith(prefix));
        }

        var rows = await query
            .OrderBy(t => t.Id)
            .Take(5000)
            .Select(t => new { t.Id, t.Term })
            .ToListAsync(ct);

        var enqueued = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Term)) continue;
            await vocabularyAudioQueue.EnqueueAsync(
                new OetLearner.Api.Services.Vocabulary.VocabularyAudioJob(
                    TermId: row.Id,
                    Text: row.Term,
                    Voice: null,
                    Locale: "en-GB",
                    BatchId: normalised ?? string.Empty),
                ct);
            enqueued++;
        }
        return new { enqueued };
    }

    /// <summary>
    /// Phase Q1 — Voice Studio "Regenerate vocabulary audio" entrypoint.
    /// Enqueues TTS jobs for active vocabulary terms matching the chosen
    /// scope, pinning every job to the specified Qwen3 voice (variant +
    /// preset id OR voicedesign instructions). Returns immediately with a
    /// batch id + projected count; the actual synthesis runs through
    /// <see cref="OetLearner.Api.Services.Vocabulary.VocabularyAudioWorker"/>
    /// which also hard-deletes the orphaned old MediaAsset rows.
    /// </summary>
    /// <param name="scope">"all" | "missing" | "different-voice".</param>
    /// <param name="modelVariant">"flash" | "voicedesign".</param>
    /// <param name="voiceId">Required when variant = flash. e.g. "Cherry".</param>
    /// <param name="instructions">Required when variant = voicedesign.</param>
    /// <param name="professionId">Optional ProfessionId filter.</param>
    /// <param name="dryRun">When true, only counts — no jobs enqueued.</param>
    public async Task<object> EnqueueVocabularyAudioRegenerateAsync(
        string scope, string modelVariant, string? voiceId, string? instructions,
        string? professionId, bool dryRun, CancellationToken ct)
    {
        if (vocabularyAudioQueue is null)
            return new { enqueued = 0, skipped = "queue-not-configured" };

        var normalisedVariant = string.Equals(modelVariant, "voicedesign", StringComparison.OrdinalIgnoreCase) ? "voicedesign" : "flash";
        var normalisedScope = (scope ?? "missing").Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "different-voice" or "different_voice" or "diff" => "different-voice",
            _ => "missing",
        };

        // Compute the "voice signature" the worker will stamp on each row so
        // we can compare against AudioVoice for the different-voice scope.
        string? signature;
        if (normalisedVariant == "voicedesign")
        {
            if (string.IsNullOrWhiteSpace(instructions))
                throw new ArgumentException("instructions required for voicedesign variant", nameof(instructions));
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(instructions))).ToLowerInvariant();
            signature = "vd-" + hash[..8];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                throw new ArgumentException("voiceId required for flash variant", nameof(voiceId));
            signature = voiceId;
        }

        IQueryable<VocabularyTerm> query = db.VocabularyTerms.AsNoTracking()
            .Where(t => t.Status == "active"
                && (t.RecallSetCodesJson == null || t.RecallSetCodesJson == "" || t.RecallSetCodesJson == "[]"));

        if (!string.IsNullOrWhiteSpace(professionId))
            query = query.Where(t => t.ProfessionId == professionId);

        if (normalisedScope == "missing")
            query = query.Where(t => t.AudioMediaAssetId == null && (t.AudioUrl == null || t.AudioUrl == ""));
        else if (normalisedScope == "different-voice")
            query = query.Where(t => t.AudioModelVariant != normalisedVariant || t.AudioVoice != signature || t.AudioVoice == null);
        // "all" → no extra filter

        // Cap at 10k to keep a single button press bounded; admins can re-run.
        var rows = await query
            .OrderBy(t => t.Id)
            .Take(10_000)
            .Select(t => new { t.Id, t.Term })
            .ToListAsync(ct);

        var batchId = "regen-" + Guid.NewGuid().ToString("N")[..12];
        if (dryRun)
            return new { batchId, queuedCount = rows.Count, scope = normalisedScope, dryRun = true, modelVariant = normalisedVariant };

        var enqueued = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Term)) continue;
            await vocabularyAudioQueue.EnqueueAsync(
                new OetLearner.Api.Services.Vocabulary.VocabularyAudioJob(
                    TermId: row.Id,
                    Text: row.Term,
                    Voice: normalisedVariant == "flash" ? voiceId : null,
                    Locale: "en-GB",
                    BatchId: batchId,
                    ModelVariant: normalisedVariant,
                    Instructions: normalisedVariant == "voicedesign" ? instructions : null),
                ct);
            enqueued++;
        }
        return new { batchId, queuedCount = enqueued, scope = normalisedScope, dryRun = false, modelVariant = normalisedVariant };
    }

    public async Task<object> GetVocabularyImportBatchSummaryAsync(string importBatchId, CancellationToken ct)
    {
        var batchId = NormalizeExistingImportBatchId(importBatchId);
        var rows = await GetVocabularyImportBatchRowsAsync(batchId, ct);

        var warnings = new List<string>();
        var active = rows.Count(v => v.Status == "active");
        var draft = rows.Count(v => v.Status == "draft");
        var archived = rows.Count(v => v.Status == "archived");
        if (active > 0) warnings.Add("Batch contains active rows; rollback will not modify active rows.");
        if (!await VocabularyImportCommitLedgerExistsAsync(batchId, ct)) warnings.Add("No immutable commit ledger was found for this import batch id.");
        if (rows.Count == 0) warnings.Add("No rows found for this import batch id.");

        return new
        {
            importBatchId = batchId,
            total = rows.Count,
            draft,
            active,
            archived,
            warnings,
            rows = rows.Select(v => new
            {
                v.Id,
                v.Term,
                v.Definition,
                v.ExampleSentence,
                v.ContextNotes,
                v.ExamTypeCode,
                v.ProfessionId,
                v.Category,
                v.IpaPronunciation,
                v.AmericanSpelling,
                v.AudioUrl,
                v.AudioSlowUrl,
                v.AudioSentenceUrl,
                v.AudioMediaAssetId,
                v.SynonymsJson,
                v.CollocationsJson,
                v.RelatedTermsJson,
                v.SourceProvenance,
                v.Status,
                v.CreatedAt,
                v.UpdatedAt
            })
        };
    }

    public async Task<(byte[] Bytes, string FileName)> ExportVocabularyImportBatchCsvAsync(string importBatchId, CancellationToken ct)
    {
        var batchId = NormalizeExistingImportBatchId(importBatchId);
        var rows = await GetVocabularyImportBatchRowsAsync(batchId, ct);

        var builder = new StringBuilder();
        builder.AppendLine("ImportBatchId,Id,Term,Definition,ExampleSentence,ContextNotes,ExamTypeCode,ProfessionId,Category,IpaPronunciation,AmericanSpelling,AudioUrl,AudioSlowUrl,AudioSentenceUrl,AudioMediaAssetId,ImageUrl,SynonymsJson,CollocationsJson,RelatedTermsJson,SourceProvenance,Status,CreatedAt,UpdatedAt");
        foreach (var row in rows)
        {
            builder.AppendJoin(',',
                EscapeCsv(batchId),
                EscapeCsv(row.Id),
                EscapeCsv(row.Term),
                EscapeCsv(row.Definition),
                EscapeCsv(row.ExampleSentence),
                EscapeCsv(row.ContextNotes),
                EscapeCsv(row.ExamTypeCode),
                EscapeCsv(row.ProfessionId),
                EscapeCsv(row.Category),
                EscapeCsv(row.IpaPronunciation),
                EscapeCsv(row.AmericanSpelling),
                EscapeCsv(row.AudioUrl),
                EscapeCsv(row.AudioSlowUrl),
                EscapeCsv(row.AudioSentenceUrl),
                EscapeCsv(row.AudioMediaAssetId),
                EscapeCsv(row.ImageUrl),
                EscapeCsv(row.SynonymsJson),
                EscapeCsv(row.CollocationsJson),
                EscapeCsv(row.RelatedTermsJson),
                EscapeCsv(row.SourceProvenance),
                EscapeCsv(row.Status),
                EscapeCsv(row.CreatedAt.ToString("O")),
                EscapeCsv(row.UpdatedAt.ToString("O")));
            builder.AppendLine();
        }

        return (Encoding.UTF8.GetBytes(builder.ToString()), $"vocabulary-import-{batchId}-export.csv");
    }

    public async Task<AdminVocabularyImportReconciliationResponse> ReconcileVocabularyImportBatchAsync(
        string importBatchId,
        IFormFile manifestFile,
        CancellationToken ct)
    {
        var batchId = NormalizeExistingImportBatchId(importBatchId);
        if (!await VocabularyImportCommitLedgerExistsAsync(batchId, ct))
            throw ApiException.Validation("VOCABULARY_IMPORT_BATCH_NOT_FOUND", "Reconciliation requires an immutable commit ledger for the import batch.");

        var manifestRows = await ParseCsvAsync(manifestFile, ct);
        var validation = await BuildVocabularyImportValidationContextAsync(ct);
        var storedRows = await GetVocabularyImportBatchRowsAsync(batchId, ct);
        var storedByKey = storedRows.ToDictionary(VocabularyTermDuplicateKey, StringComparer.OrdinalIgnoreCase);
        var seenManifestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reportRows = new List<AdminVocabularyImportReconciliationRow>();

        var matched = 0;
        var missing = 0;
        var extra = 0;
        var mismatched = 0;
        var invalid = 0;

        foreach (var manifestRow in manifestRows)
        {
            var key = PreviewDuplicateKey(manifestRow);
            var (ok, error) = ValidateCsvRow(manifestRow, batchId, validation);
            if (!ok)
            {
                invalid++;
                reportRows.Add(new AdminVocabularyImportReconciliationRow(
                    manifestRow.LineNumber,
                    key,
                    "invalid-manifest-row",
                    Array.Empty<AdminVocabularyImportReconciliationFieldMismatch>(),
                    error));
                continue;
            }

            if (!seenManifestKeys.Add(key))
            {
                invalid++;
                reportRows.Add(new AdminVocabularyImportReconciliationRow(
                    manifestRow.LineNumber,
                    key,
                    "duplicate-manifest-row",
                    Array.Empty<AdminVocabularyImportReconciliationFieldMismatch>(),
                    "Duplicate row in the manifest for the same term, exam type, and profession."));
                continue;
            }

            if (!storedByKey.TryGetValue(key, out var storedRow))
            {
                missing++;
                reportRows.Add(new AdminVocabularyImportReconciliationRow(
                    manifestRow.LineNumber,
                    key,
                    "missing-stored-row",
                    Array.Empty<AdminVocabularyImportReconciliationFieldMismatch>(),
                    "No committed vocabulary term was found for this manifest row."));
                continue;
            }

            var mismatches = CompareVocabularyImportRow(manifestRow, storedRow, batchId);
            if (mismatches.Count == 0)
            {
                matched++;
                reportRows.Add(new AdminVocabularyImportReconciliationRow(
                    manifestRow.LineNumber,
                    key,
                    "matched",
                    Array.Empty<AdminVocabularyImportReconciliationFieldMismatch>(),
                    null));
            }
            else
            {
                mismatched++;
                reportRows.Add(new AdminVocabularyImportReconciliationRow(
                    manifestRow.LineNumber,
                    key,
                    "mismatched",
                    mismatches,
                    null));
            }
        }

        foreach (var storedRow in storedRows)
        {
            var key = VocabularyTermDuplicateKey(storedRow);
            if (seenManifestKeys.Contains(key)) continue;

            extra++;
            reportRows.Add(new AdminVocabularyImportReconciliationRow(
                null,
                key,
                "extra-stored-row",
                Array.Empty<AdminVocabularyImportReconciliationFieldMismatch>(),
                "Committed vocabulary term was not present in the manifest."));
        }

        return new AdminVocabularyImportReconciliationResponse(
            batchId,
            manifestRows.Count,
            storedRows.Count,
            matched,
            missing,
            extra,
            mismatched,
            invalid,
            missing == 0 && extra == 0 && mismatched == 0 && invalid == 0,
            reportRows);
    }

    public async Task<AdminVocabularyImportRollbackResponse> RollbackVocabularyImportBatchAsync(
        string adminId,
        string adminName,
        string importBatchId,
        AdminVocabularyImportRollbackRequest request,
        CancellationToken ct)
    {
        var batchId = NormalizeExistingImportBatchId(importBatchId);
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var rows = await GetVocabularyImportBatchRowsAsync(batchId, ct);
        var errors = new List<string>();
        var deleted = 0;
        var archived = 0;
        var blocked = 0;

        if (request.DeleteDraftRows)
            errors.Add("Physical deletion is disabled for vocabulary import rollback; draft rows are archived instead.");

        if (!await VocabularyImportCommitLedgerExistsAsync(batchId, ct))
        {
            throw ApiException.Validation("VOCABULARY_IMPORT_BATCH_NOT_FOUND", "Rollback requires an immutable commit ledger for the import batch.");
        }

        foreach (var row in rows)
        {
            if (row.Status != "draft")
            {
                blocked++;
                if (errors.Count < 20) errors.Add($"{row.Id}: status '{row.Status}' is not draft; not rolled back.");
                continue;
            }

            row.Status = "archived";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            archived++;
        }

        if (deleted > 0 || archived > 0)
        {
            await db.SaveChangesAsync(ct);
            await LogAuditAsync(adminId, adminName, "Import Rollback", "VocabularyTerm", batchId,
                $"Rolled back batch {batchId}: deleted={deleted}, archived={archived}, blocked={blocked}.", ct);
            await CommitIfOwnedAsync(tx, ct);
        }

        return new AdminVocabularyImportRollbackResponse(batchId, rows.Count, deleted, archived, blocked, errors);
    }

    private static IReadOnlyList<string> SplitList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        var trimmed = raw.Trim();
        // Auto-detect JSON array format: ["term1","term2"]
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsed is not null)
                    return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
            }
            catch (System.Text.Json.JsonException) { /* fall through to delimiter split */ }
        }
        // Pipe or semicolon delimited (legacy format)
        if (trimmed.Contains('|') || trimmed.Contains(';'))
            return trimmed.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Comma-separated fallback (for plain CSV values like "term1, term2, term3")
        if (trimmed.Contains(','))
            return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Single value
        return new[] { trimmed };
    }

    private static VocabularyTerm CreateVocabularyTermFromCsvRow(
        CsvVocabRow row, string id, string importBatchId, string recallSetCode)
    {
        var examType = ExamCodes.Normalize(row.ExamTypeCode);
        return new VocabularyTerm
        {
            Id = id,
            Term = row.Term!.Trim(),
            // Definition / ExampleSentence are optional on import; the publish
            // gate still enforces a non-empty Definition before a row can
            // leave draft.
            Definition = CleanOptional(row.Definition),
            ExampleSentence = CleanOptional(row.ExampleSentence),
            ContextNotes = CleanOptional(row.ContextNotes),
            ExamTypeCode = examType,
            ProfessionId = CleanOptional(row.ProfessionId),
            // Header-less / undeclared rows default to the "recall-term"
            // bucket so they remain discoverable in the admin UI without
            // forcing the uploader to pick a category up-front.
            Category = CleanOptional(row.Category) ?? "recall-term",
            IpaPronunciation = CleanOptional(row.IpaPronunciation),
            AmericanSpelling = CleanOptional(row.AmericanSpelling),
            AudioUrl = CleanOptional(row.AudioUrl),
            AudioSlowUrl = CleanOptional(row.AudioSlowUrl),
            AudioSentenceUrl = CleanOptional(row.AudioSentenceUrl),
            AudioMediaAssetId = CleanOptional(row.AudioMediaAssetId),
            ImageUrl = CleanOptional(row.ImageUrl),
            SynonymsJson = string.IsNullOrWhiteSpace(row.SynonymsRaw) ? "[]" : JsonSupport.Serialize(SplitList(row.SynonymsRaw!)),
            CollocationsJson = string.IsNullOrWhiteSpace(row.CollocationsRaw) ? "[]" : JsonSupport.Serialize(SplitList(row.CollocationsRaw!)),
            RelatedTermsJson = string.IsNullOrWhiteSpace(row.RelatedTermsRaw) ? "[]" : JsonSupport.Serialize(SplitList(row.RelatedTermsRaw!)),
            // Apply the chosen recall-set categorisation to every imported row
            // so admins can filter/maintain the practice-collection later.
            RecallSetCodesJson = System.Text.Json.JsonSerializer.Serialize(new[] { recallSetCode }),
            // BuildBatchSourceProvenance(null, batchId) auto-stamps
            // "batch={id};source=admin-vocabulary-import;date=yyyy-MM-dd"
            // when the CSV row omits provenance.
            SourceProvenance = BuildBatchSourceProvenance(row.SourceProvenance, importBatchId),
            Status = "draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static void UpdateVocabularyTermFromCsvRow(
        VocabularyTerm existing, CsvVocabRow row, string importBatchId, string recallSetCode)
    {
        existing.Term = row.Term!.Trim();
        if (!string.IsNullOrWhiteSpace(row.Definition))
            existing.Definition = CleanOptional(row.Definition);
        if (!string.IsNullOrWhiteSpace(row.ExampleSentence))
            existing.ExampleSentence = CleanOptional(row.ExampleSentence);
        if (!string.IsNullOrWhiteSpace(row.ContextNotes))
            existing.ContextNotes = CleanOptional(row.ContextNotes);
        existing.ExamTypeCode = ExamCodes.Normalize(row.ExamTypeCode);
        if (!string.IsNullOrWhiteSpace(row.ProfessionId))
            existing.ProfessionId = CleanOptional(row.ProfessionId);
        if (!string.IsNullOrWhiteSpace(row.Category))
            existing.Category = CleanOptional(row.Category) ?? existing.Category;
        if (!string.IsNullOrWhiteSpace(row.IpaPronunciation))
            existing.IpaPronunciation = CleanOptional(row.IpaPronunciation);
        if (!string.IsNullOrWhiteSpace(row.AmericanSpelling))
            existing.AmericanSpelling = CleanOptional(row.AmericanSpelling);
        if (!string.IsNullOrWhiteSpace(row.AudioUrl))
            existing.AudioUrl = CleanOptional(row.AudioUrl);
        if (!string.IsNullOrWhiteSpace(row.AudioSlowUrl))
            existing.AudioSlowUrl = CleanOptional(row.AudioSlowUrl);
        if (!string.IsNullOrWhiteSpace(row.AudioSentenceUrl))
            existing.AudioSentenceUrl = CleanOptional(row.AudioSentenceUrl);
        if (!string.IsNullOrWhiteSpace(row.AudioMediaAssetId))
            existing.AudioMediaAssetId = CleanOptional(row.AudioMediaAssetId);
        if (!string.IsNullOrWhiteSpace(row.ImageUrl))
            existing.ImageUrl = CleanOptional(row.ImageUrl);
        if (!string.IsNullOrWhiteSpace(row.SynonymsRaw))
            existing.SynonymsJson = JsonSupport.Serialize(SplitList(row.SynonymsRaw!));
        if (!string.IsNullOrWhiteSpace(row.CollocationsRaw))
            existing.CollocationsJson = JsonSupport.Serialize(SplitList(row.CollocationsRaw!));
        if (!string.IsNullOrWhiteSpace(row.RelatedTermsRaw))
            existing.RelatedTermsJson = JsonSupport.Serialize(SplitList(row.RelatedTermsRaw!));
        // Preserve recall set codes — add the new code if not already present
        var existingCodes = new List<string>();
        try { existingCodes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(existing.RecallSetCodesJson ?? "[]") ?? new(); } catch { }
        if (!existingCodes.Contains(recallSetCode, StringComparer.OrdinalIgnoreCase))
        {
            existingCodes.Add(recallSetCode);
            existing.RecallSetCodesJson = System.Text.Json.JsonSerializer.Serialize(existingCodes);
        }
        existing.SourceProvenance = BuildBatchSourceProvenance(row.SourceProvenance, importBatchId);
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string TryExtractRecallSetCodeFromStored(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return string.Empty;
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return arr.FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> EnsureRecallSetCodeOrThrowAsync(string? recallSetCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recallSetCode))
        {
            throw ApiException.Validation(
                "RECALL_SET_CODE_REQUIRED",
                "A practice-collection (recall set) label is required for every bulk upload. Pick one of: " +
                string.Join(", ", OetLearner.Api.Domain.RecallSetCodes.All) +
                ", or any custom code created in /admin/content/vocabulary/recall-set-tags.");
        }
        var code = recallSetCode.Trim().ToLowerInvariant();
        var inDb = await db.RecallSetTags.AsNoTracking()
            .AnyAsync(t => t.Code == code && t.IsActive, ct);
        if (inDb) return code;
        if (OetLearner.Api.Domain.RecallSetCodes.IsKnown(code))
            return OetLearner.Api.Domain.RecallSetCodes.Normalise(code)!;
        throw ApiException.Validation(
            "RECALL_SET_CODE_UNKNOWN",
            $"Recall set code '{recallSetCode}' is not active. Create or unarchive it in /admin/content/vocabulary/recall-set-tags first.");
    }

    private static async Task<string> ComputeFileSha256Async(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task UpsertVocabularyImportDryRunAsync(string importBatchId, string fileSha256, int imported, CancellationToken ct)
    {
        var payload = new VocabularyImportDryRunLedger(importBatchId, fileSha256, imported, DateTimeOffset.UtcNow);
        await UpsertVocabularyImportLedgerAsync(VocabularyImportDryRunLedgerId(importBatchId), "admin-vocabulary-import-dry-run", importBatchId, payload, ct);
    }

    private async Task RequireMatchingVocabularyImportDryRunAsync(string importBatchId, string fileSha256, int imported, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == VocabularyImportDryRunLedgerId(importBatchId), ct);
        if (record is null)
            throw ApiException.Validation("VOCABULARY_IMPORT_DRY_RUN_REQUIRED", "A clean dry run for this import batch and file is required before commit.");

        var ledger = JsonSupport.Deserialize<VocabularyImportDryRunLedger?>(record.ResponseJson, null);
        if (ledger is null || ledger.FileSha256 != fileSha256 || ledger.Imported != imported)
            throw ApiException.Validation("VOCABULARY_IMPORT_DRY_RUN_MISMATCH", "The commit file does not match the latest clean dry run for this import batch.");
    }

    private async Task EnsureVocabularyImportCommitLedgerAvailableAsync(string importBatchId, CancellationToken ct)
    {
        if (await VocabularyImportCommitLedgerExistsAsync(importBatchId, ct))
            throw ApiException.Validation("VOCABULARY_IMPORT_BATCH_ALREADY_COMMITTED", "This import batch id has already been committed and cannot be reused.");
    }

    private async Task CreateVocabularyImportCommitLedgerAsync(string importBatchId, string fileSha256, IReadOnlyList<string> termIds, CancellationToken ct)
    {
        await EnsureVocabularyImportCommitLedgerAvailableAsync(importBatchId, ct);
        var payload = new VocabularyImportCommitLedger(importBatchId, fileSha256, termIds.ToArray(), DateTimeOffset.UtcNow);
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = VocabularyImportCommitLedgerId(importBatchId),
            Scope = "admin-vocabulary-import-commit",
            Key = importBatchId,
            ResponseJson = JsonSupport.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task UpsertVocabularyImportLedgerAsync(string id, string scope, string key, object payload, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = id,
                Scope = scope,
                Key = key,
                ResponseJson = JsonSupport.Serialize(payload),
                CreatedAt = DateTimeOffset.UtcNow
            });
            return;
        }

        record.Scope = scope;
        record.Key = key;
        record.ResponseJson = JsonSupport.Serialize(payload);
        record.CreatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<IReadOnlyList<string>> GetVocabularyImportCommitTermIdsAsync(string importBatchId, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == VocabularyImportCommitLedgerId(importBatchId), ct);
        var ledger = JsonSupport.Deserialize<VocabularyImportCommitLedger?>(record?.ResponseJson, null);
        return ledger?.TermIds ?? Array.Empty<string>();
    }

    private async Task<bool> VocabularyImportCommitLedgerExistsAsync(string importBatchId, CancellationToken ct)
        => await db.IdempotencyRecords.AsNoTracking().AnyAsync(r => r.Id == VocabularyImportCommitLedgerId(importBatchId), ct);

    private static string VocabularyImportDryRunLedgerId(string importBatchId)
        => $"vocab-dryrun:{importBatchId}";

    private static string VocabularyImportCommitLedgerId(string importBatchId)
        => $"vocab-commit:{importBatchId}";

    private async Task<List<VocabularyTerm>> GetVocabularyImportBatchRowsAsync(string importBatchId, CancellationToken ct)
    {
        var termIds = await GetVocabularyImportCommitTermIdsAsync(importBatchId, ct);
        if (termIds.Count == 0) return [];

        return await db.VocabularyTerms
            .Where(v => termIds.Contains(v.Id))
            .OrderBy(v => v.Term)
            .ThenBy(v => v.Id)
            .ToListAsync(ct);
    }

    private static string BuildBatchSourceProvenance(string? sourceProvenance, string importBatchId)
    {
        var prefix = BatchProvenancePrefix(importBatchId);
        var compact = CleanOptional(sourceProvenance);
        if (compact is null)
            return $"{prefix}source=admin-vocabulary-import;date={DateTimeOffset.UtcNow:yyyy-MM-dd}";
        return $"{prefix}{StripExistingBatchPrefix(compact)}";
    }

    private static string StripExistingBatchPrefix(string value)
    {
        if (!value.StartsWith("batch=", StringComparison.OrdinalIgnoreCase)) return value;
        var delimiter = value.IndexOf(';');
        return delimiter < 0 ? string.Empty : value[(delimiter + 1)..].TrimStart();
    }

    private static string BatchProvenancePrefix(string importBatchId)
        => $"batch={importBatchId};";

    private static string NormalizeImportBatchId(string? importBatchId)
    {
        if (string.IsNullOrWhiteSpace(importBatchId))
            return $"vocab-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..31];
        return NormalizeExistingImportBatchId(importBatchId);
    }

    private static string NormalizeExistingImportBatchId(string importBatchId)
    {
        var normalized = importBatchId.Trim();
        if (normalized.Length is < 3 or > 64)
            throw ApiException.Validation("INVALID_IMPORT_BATCH_ID", "Import batch id must be between 3 and 64 characters.");
        if (normalized.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':')))
            throw ApiException.Validation("INVALID_IMPORT_BATCH_ID", "Import batch id may contain only letters, numbers, dash, underscore, dot, and colon.");
        return normalized;
    }

    private static string PreviewDuplicateKey(CsvVocabRow row)
    {
        var key = BuildImportKey(row);
        return $"{key.Term}|{key.ExamTypeCode}|{key.ProfessionId}";
    }

    private async Task<VocabularyTerm?> FindExistingVocabularyTermForImportAsync(ImportKey key, CancellationToken ct)
        => await db.VocabularyTerms.FirstOrDefaultAsync(t =>
            t.Status != "archived" &&
            t.Term.Trim().ToLower() == key.Term &&
            t.ExamTypeCode.Trim().ToLower() == key.ExamTypeCode &&
            (t.ProfessionId == null ? string.Empty : t.ProfessionId.Trim().ToLower()) == key.ProfessionId,
            ct);

    private static ImportKey BuildImportKey(CsvVocabRow row)
    {
        var examType = string.IsNullOrWhiteSpace(row.ExamTypeCode) ? "oet" : row.ExamTypeCode!.Trim();
        var professionId = CleanOptional(row.ProfessionId) ?? string.Empty;
        return new ImportKey(NormalizeImportKeyPart(row.Term), NormalizeImportKeyPart(examType), NormalizeImportKeyPart(professionId));
    }

    private static string NormalizeImportKeyPart(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private sealed record ImportKey(string Term, string ExamTypeCode, string ProfessionId);

    private sealed record VocabularyImportDryRunLedger(
        string ImportBatchId,
        string FileSha256,
        int Imported,
        DateTimeOffset ConfirmedAt);

    private sealed record VocabularyImportCommitLedger(
        string ImportBatchId,
        string FileSha256,
        IReadOnlyList<string> TermIds,
        DateTimeOffset CommittedAt);

    private sealed record CsvVocabRecord(int LineNumber, List<string> Fields);

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
        string? AmericanSpelling,
        string? AudioUrl,
        string? AudioSlowUrl,
        string? AudioSentenceUrl,
        string? AudioMediaAssetId,
        string? ContextNotes,
        string? SynonymsRaw,
        string? CollocationsRaw,
        string? RelatedTermsRaw,
        string? SourceProvenance,
        string? ImageUrl,
        string? ExistingId);

    private async Task<VocabularyImportValidationContext> BuildVocabularyImportValidationContextAsync(CancellationToken ct)
    {
        var examTypes = await db.ExamTypes.AsNoTracking()
            .Select(e => new { e.Code, e.Status, e.ProfessionIdsJson })
            .ToListAsync(ct);
        var professions = await db.Professions.AsNoTracking()
            .Where(p => p.Status == "active")
            .Select(p => p.Id)
            .ToListAsync(ct);

        var examTypeCodes = examTypes
            .Where(e => !string.Equals(e.Status, "archived", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownProfessionIds = professions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var examTypeProfessionIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var examType in examTypes)
        {
            var ids = JsonSupport.Deserialize<string[]?>(examType.ProfessionIdsJson, null) ?? Array.Empty<string>();
            var allowed = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in allowed) knownProfessionIds.Add(id);
            examTypeProfessionIds[examType.Code] = allowed;
        }

        return new VocabularyImportValidationContext(examTypeCodes, knownProfessionIds, examTypeProfessionIds);
    }

    private static (bool Ok, string? Error) ValidateCsvRow(
        CsvVocabRow r,
        string importBatchId,
        VocabularyImportValidationContext validation)
    {
        // Term is the only hard-required field. Imported rows land as
        // status="draft"; the publish gate enforces Definition + provenance
        // before a row can leave draft, so optional fields here are safe.
        if (string.IsNullOrWhiteSpace(r.Term)) return (false, "Empty 'term'.");
        if (r.Term.Trim().Length > 128) return (false, "Term exceeds 128 characters.");

        if (!string.IsNullOrWhiteSpace(r.Definition) && r.Definition!.Trim().Length > 1024)
            return (false, "Definition exceeds 1024 characters.");
        if (TrimmedLength(r.ExampleSentence) > 2048) return (false, "Example sentence exceeds 2048 characters.");
        if (TrimmedLength(r.ContextNotes) > 1024) return (false, "Context notes exceed 1024 characters.");
        if (TrimmedLength(r.ExamTypeCode) > 16) return (false, "Exam type code exceeds 16 characters.");
        if (TrimmedLength(r.ProfessionId) > 32) return (false, "Profession id exceeds 32 characters.");
        if (TrimmedLength(r.Category) > 64) return (false, "Category exceeds 64 characters.");
        if (TrimmedLength(r.IpaPronunciation) > 64) return (false, "IPA pronunciation exceeds 64 characters.");
        if (TrimmedLength(r.AmericanSpelling) > 128) return (false, "American spelling exceeds 128 characters.");
        if (TrimmedLength(r.AudioUrl) > 256) return (false, "Audio URL exceeds 256 characters.");
        if (TrimmedLength(r.AudioSlowUrl) > 256) return (false, "Slow audio URL exceeds 256 characters.");
        if (TrimmedLength(r.AudioSentenceUrl) > 256) return (false, "Sentence audio URL exceeds 256 characters.");
        if (TrimmedLength(r.AudioMediaAssetId) > 64) return (false, "Audio media asset id exceeds 64 characters.");

        // SourceProvenance is optional on import. When the row supplies a
        // value we still enforce the compact source-pointer contract. When
        // it's missing, CreateVocabularyTermFromCsvRow stamps an auto
        // "batch={id};source=admin-vocabulary-import;date=..." provenance via
        // BuildBatchSourceProvenance(null, batchId), which the publish gate
        // still requires before activation.
        if (!string.IsNullOrWhiteSpace(r.SourceProvenance))
        {
            if (TrimmedLength(r.SourceProvenance) > 512) return (false, "Source provenance exceeds 512 characters.");
            if (!HasCompactSourcePointer(r.SourceProvenance!)) return (false, "Source provenance must include a compact source pointer such as src=..., source=..., or manifest=....");
            if (BuildBatchSourceProvenance(r.SourceProvenance, importBatchId).Length > 512) return (false, "Source provenance plus import batch id exceeds 512 characters.");
        }

        if (!string.IsNullOrWhiteSpace(r.Category))
        {
            var category = r.Category!.Trim();
            if (!ApprovedVocabularyCategories.Contains(category))
                return (false, $"Unknown category '{category}'. Use an approved vocabulary taxonomy value or record editorial approval before import.");
        }

        var examTypeCode = ExamCodes.Normalize(r.ExamTypeCode);
        if (!validation.ExamTypeCodes.Contains(examTypeCode))
            return (false, $"Unknown exam type code '{examTypeCode}'.");

        var professionId = CleanOptional(r.ProfessionId);
        if (professionId is not null)
        {
            if (validation.ExamTypeProfessionIds.TryGetValue(examTypeCode, out var allowedForExam) && allowedForExam.Count > 0)
            {
                if (!allowedForExam.Contains(professionId))
                    return (false, $"Profession id '{professionId}' is not approved for exam type '{examTypeCode}'.");
            }
            else if (!validation.KnownProfessionIds.Contains(professionId))
            {
                return (false, $"Unknown profession id '{professionId}'.");
            }
        }

        return (true, null);
    }

    private static readonly HashSet<string> ApprovedVocabularyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "general",
        "medical",
        "anatomy",
        "pharmacology",
        "procedures",
        "symptoms",
        "conditions",
        "diagnostics",
        "clinical_communication",
        "communication",
        "nursing_care",
        "oral_health",
        "dispensing",
        "counselling",
        // Singular forms (accepted alongside plural for CSV convenience)
        "symptom",
        "condition",
        // Additional OET vocabulary taxonomy categories
        "medication",
        "nutrition",
        "profession",
        "descriptor",
        "activity",
        "function",
        "investigation",
        // Default category for header-less / undeclared rows imported via the
        // term-only CSV path. The publish gate still enforces medical-category
        // pronunciation rules separately, so this is safe to expose.
        "recall-term"
    };

    private sealed record VocabularyImportValidationContext(
        HashSet<string> ExamTypeCodes,
        HashSet<string> KnownProfessionIds,
        Dictionary<string, HashSet<string>> ExamTypeProfessionIds);

    private static int TrimmedLength(string? value)
        => string.IsNullOrWhiteSpace(value) ? 0 : value.Trim().Length;

    private static bool HasCompactSourcePointer(string sourceProvenance)
    {
        var compact = StripExistingBatchPrefix(sourceProvenance).Trim();
        if (compact.Length == 0 || !compact.Contains('=')) return false;

        foreach (var part in compact.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0) continue;

            var key = part[..equalsIndex].Trim();
            var value = part[(equalsIndex + 1)..].Trim().Trim('"', '\'');
            if (!IsSourcePointerKey(key) || !IsSpecificSourcePointerValue(value)) continue;

            return true;
        }

        return false;
    }

    private static bool IsSourcePointerKey(string key)
        => key.ToLowerInvariant() is "src" or "source" or "sourcedocumentid" or "document" or "doc" or "manifest";

    private static bool IsSpecificSourcePointerValue(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.ToLowerInvariant() is not "admin-vocabulary-import" and not "csv" and not "import" and not "unknown" and not "n/a" and not "na" and not "none" and not "null" and not "placeholder";

    // Headers recognised as a valid first-row header. Keep lowercase; the
    // matcher compares trimmed lowercase tokens. If the first record's fields
    // contain ANY of these, the row is treated as a header row and subsequent
    // rows are parsed by column name. Otherwise the file is treated as
    // header-less with positional columns: 0=Term, 1=Definition, 2=Category,
    // 3=Difficulty (all but Term optional).
    private static readonly HashSet<string> KnownVocabularyCsvHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "term", "definition", "examplesentence", "example",
        "category", "difficulty",
        "professionid", "profession",
        "examtypecode", "examtype",
        "ipapronunciation", "ipa", "pronunciation",
        "americanspelling", "american", "usspelling", "usvariant",
        "audiourl", "audio",
        "audioslowurl", "slowaudio", "audioslow",
        "audiosentenceurl", "sentenceaudio", "audiosentence",
        "audiomediaassetid", "audioassetid", "mediaassetid",
        "contextnotes", "context",
        "synonyms", "synonymscsv", "synonymsjson",
        "collocations", "collocationscsv", "collocationsjson",
        "relatedterms", "relatedtermscsv", "relatedtermsjson",
        "sourceprovenance", "provenance",
        "imageurl", "image",
        "id",
        "importbatchid", "status", "createdat", "updatedat",
    };

    private static async Task<List<CsvVocabRow>> ParseCsvAsync(IFormFile file, CancellationToken ct)
    {
        var rows = new List<CsvVocabRow>();
        // Auto-detect encoding: try UTF-8 first (with BOM detection enabled),
        // then fall back to Windows-1252 if the stream contains invalid UTF-8 sequences.
        string content;
        using (var stream = file.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            // Check for UTF-8 BOM (EF BB BF) or UTF-16 LE BOM (FF FE)
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else
            {
                // Try UTF-8; if it produces replacement chars, fall back to Windows-1252
                content = Encoding.UTF8.GetString(bytes);
                if (content.Contains('\uFFFD'))
                {
                    try
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        content = Encoding.GetEncoding(1252).GetString(bytes);
                    }
                    catch { /* stick with UTF-8 if codepage unavailable */ }
                }
            }
        }
        // Defensively strip a leading UTF-8 BOM. StreamReader normally
        // consumes it, but uploaders that pre-encode the file may include a
        // literal U+FEFF as the first character.
        if (content.Length > 0 && content[0] == '\uFEFF') content = content[1..];
        var records = ParseVocabCsvRecords(content);
        if (records.Count == 0)
            throw ApiException.Validation("INVALID_CSV", "CSV file is empty.");

        var firstRecord = records[0];
        var firstFields = firstRecord.Fields.Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var looksLikeHeader = firstFields.Any(f => KnownVocabularyCsvHeaders.Contains(f));

        if (looksLikeHeader)
        {
            var headers = firstFields;
            int Col(params string[] names)
            {
                foreach (var n in names)
                {
                    var idx = Array.IndexOf(headers, n.ToLowerInvariant());
                    if (idx >= 0) return idx;
                }
                return -1;
            }

            var ti = Col("term");
            if (ti < 0)
                throw ApiException.Validation("INVALID_CSV", "CSV header row must include a 'Term' column.");
            var di = Col("definition");
            var ei = Col("examplesentence", "example");
            var ci = Col("category");
            var dfi = Col("difficulty");
            var pi = Col("professionid", "profession");
            var exi = Col("examtypecode", "examtype");
            var ipi = Col("ipapronunciation", "ipa", "pronunciation");
            var usi = Col("americanspelling", "american", "usspelling", "usvariant");
            var ai = Col("audiourl", "audio");
            var asi = Col("audioslowurl", "slowaudio", "audioslow");
            var ati = Col("audiosentenceurl", "sentenceaudio", "audiosentence");
            var ami = Col("audiomediaassetid", "audioassetid", "mediaassetid");
            var cni = Col("contextnotes", "context");
            var sy = Col("synonyms", "synonymscsv", "synonymsjson");
            var co = Col("collocations", "collocationscsv", "collocationsjson");
            var rt = Col("relatedterms", "relatedtermscsv", "relatedtermsjson");
            var sp = Col("sourceprovenance", "provenance");
            var iu = Col("imageurl", "image");
            var eid = Col("id");

            foreach (var record in records.Skip(1))
            {
                var cols = record.Fields;
                string? Get(int idx) => idx >= 0 && cols.Count > idx ? cols[idx].Trim() : null;

                rows.Add(new CsvVocabRow(
                    LineNumber: record.LineNumber,
                    Term: Get(ti),
                    Definition: Get(di),
                    ExampleSentence: Get(ei),
                    Category: Get(ci),
                    Difficulty: Get(dfi),
                    ProfessionId: Get(pi),
                    ExamTypeCode: Get(exi),
                    IpaPronunciation: Get(ipi),
                    AmericanSpelling: Get(usi),
                    AudioUrl: Get(ai),
                    AudioSlowUrl: Get(asi),
                    AudioSentenceUrl: Get(ati),
                    AudioMediaAssetId: Get(ami),
                    ContextNotes: Get(cni),
                    SynonymsRaw: Get(sy),
                    CollocationsRaw: Get(co),
                    RelatedTermsRaw: Get(rt),
                    SourceProvenance: Get(sp),
                    ImageUrl: Get(iu),
                    ExistingId: Get(eid)));
            }
        }
        else
        {
            // Header-less mode. Positional columns:
            //   0 = Term (required)
            //   1 = Definition (optional)
            //   2 = Category (optional)
            //   3 = Difficulty (optional)
            // Everything else is left null; the row will be created as
            // status="draft" with auto-stamped source provenance.
            foreach (var record in records)
            {
                var cols = record.Fields;
                string? Get(int idx) => idx >= 0 && cols.Count > idx ? cols[idx].Trim() : null;

                rows.Add(new CsvVocabRow(
                    LineNumber: record.LineNumber,
                    Term: Get(0),
                    Definition: Get(1),
                    ExampleSentence: null,
                    Category: Get(2),
                    Difficulty: Get(3),
                    ProfessionId: null,
                    ExamTypeCode: null,
                    IpaPronunciation: null,
                    AmericanSpelling: null,
                    AudioUrl: null,
                    AudioSlowUrl: null,
                    AudioSentenceUrl: null,
                    AudioMediaAssetId: null,
                    ContextNotes: null,
                    SynonymsRaw: null,
                    CollocationsRaw: null,
                    RelatedTermsRaw: null,
                    SourceProvenance: null,
                    ImageUrl: null,
                    ExistingId: null));
            }
        }
        return rows;
    }

    private static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<CsvVocabRecord> ParseVocabCsvRecords(string csvContent)
    {
        var records = new List<CsvVocabRecord>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        var recordLineNumber = 1;
        var lineNumber = 1;
        var inQuotes = false;

        // Auto-detect field delimiter from first line (comma, semicolon, or tab)
        var firstLineEnd = csvContent.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = firstLineEnd >= 0 ? csvContent[..firstLineEnd] : csvContent;
        var commas = firstLine.Count(c => c == ',');
        var semicolons = firstLine.Count(c => c == ';');
        var tabs = firstLine.Count(c => c == '\t');
        var delimiter = commas >= semicolons && commas >= tabs ? ','
                      : semicolons >= tabs ? ';'
                      : '\t';

        void AddRecord()
        {
            fields.Add(sb.ToString());
            sb.Clear();
            if (fields.Any(field => !string.IsNullOrWhiteSpace(field)))
                records.Add(new CsvVocabRecord(recordLineNumber, fields.ToList()));
            fields.Clear();
        }

        for (var i = 0; i < csvContent.Length; i++)
        {
            var c = csvContent[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csvContent.Length && csvContent[i + 1] == '"')
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
                    if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                        {
                            sb.Append("\r\n");
                            i++;
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        lineNumber++;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == delimiter)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    AddRecord();
                    if (c == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n') i++;
                    lineNumber++;
                    recordLineNumber = lineNumber;
                }
                else sb.Append(c);
            }
        }

        if (inQuotes)
            throw ApiException.Validation("INVALID_CSV", $"CSV has an unclosed quoted field starting at line {recordLineNumber}.");

        if (fields.Count > 0 || sb.Length > 0)
            AddRecord();

        return records;
    }

    private static string VocabularyTermDuplicateKey(VocabularyTerm row)
        => $"{NormalizeImportKeyPart(row.Term)}|{NormalizeImportKeyPart(row.ExamTypeCode)}|{NormalizeImportKeyPart(row.ProfessionId)}";

    private static IReadOnlyList<AdminVocabularyImportReconciliationFieldMismatch> CompareVocabularyImportRow(
        CsvVocabRow manifestRow,
        VocabularyTerm storedRow,
        string importBatchId)
    {
        // Reconciliation uses the manifest's original recall set code if
        // stored in the row, otherwise falls back to "" so the comparison
        // doesn't accidentally introduce a tag where the source had none.
        var expectedRecallSet = TryExtractRecallSetCodeFromStored(storedRow.RecallSetCodesJson);
        var expected = CreateVocabularyTermFromCsvRow(manifestRow, "expected", importBatchId, expectedRecallSet);
        var mismatches = new List<AdminVocabularyImportReconciliationFieldMismatch>();

        void Compare(string field, string? expectedValue, string? actualValue)
        {
            if (string.Equals(expectedValue ?? string.Empty, actualValue ?? string.Empty, StringComparison.Ordinal)) return;
            mismatches.Add(new AdminVocabularyImportReconciliationFieldMismatch(field, expectedValue, actualValue));
        }

        Compare("term", expected.Term, storedRow.Term);
        Compare("definition", expected.Definition, storedRow.Definition);
        Compare("exampleSentence", expected.ExampleSentence, storedRow.ExampleSentence);
        Compare("contextNotes", expected.ContextNotes, storedRow.ContextNotes);
        Compare("examTypeCode", expected.ExamTypeCode, storedRow.ExamTypeCode);
        Compare("professionId", expected.ProfessionId, storedRow.ProfessionId);
        Compare("category", expected.Category, storedRow.Category);
        Compare("ipaPronunciation", expected.IpaPronunciation, storedRow.IpaPronunciation);
        Compare("americanSpelling", expected.AmericanSpelling, storedRow.AmericanSpelling);
        Compare("audioUrl", expected.AudioUrl, storedRow.AudioUrl);
        Compare("audioSlowUrl", expected.AudioSlowUrl, storedRow.AudioSlowUrl);
        Compare("audioSentenceUrl", expected.AudioSentenceUrl, storedRow.AudioSentenceUrl);
        Compare("audioMediaAssetId", expected.AudioMediaAssetId, storedRow.AudioMediaAssetId);
        Compare("synonymsJson", expected.SynonymsJson, storedRow.SynonymsJson);
        Compare("collocationsJson", expected.CollocationsJson, storedRow.CollocationsJson);
        Compare("relatedTermsJson", expected.RelatedTermsJson, storedRow.RelatedTermsJson);
        CompareSourceProvenance(manifestRow.SourceProvenance, storedRow.SourceProvenance, importBatchId, mismatches);
        Compare("status", expected.Status, storedRow.Status);

        return mismatches;
    }

    private static void CompareSourceProvenance(
        string? manifestSourceProvenance,
        string? storedSourceProvenance,
        string importBatchId,
        List<AdminVocabularyImportReconciliationFieldMismatch> mismatches)
    {
        if (string.IsNullOrWhiteSpace(manifestSourceProvenance))
        {
            var expectedPrefix = $"{BatchProvenancePrefix(importBatchId)}source=admin-vocabulary-import;date=";
            if (storedSourceProvenance?.StartsWith(expectedPrefix, StringComparison.Ordinal) == true) return;
            mismatches.Add(new AdminVocabularyImportReconciliationFieldMismatch("sourceProvenance", expectedPrefix + "yyyy-MM-dd", storedSourceProvenance));
            return;
        }

        var expected = BuildBatchSourceProvenance(manifestSourceProvenance, importBatchId);
        if (!string.Equals(expected, storedSourceProvenance, StringComparison.Ordinal))
            mismatches.Add(new AdminVocabularyImportReconciliationFieldMismatch("sourceProvenance", expected, storedSourceProvenance));
    }

    public async Task<object> BulkImportVocabularyAsync(
        string adminId, string adminName, IFormFile file, CancellationToken ct)
    {
        // Backward-compat thin wrapper: keep legacy callers non-committing.
        // No recall set code on v1 (legacy callers don't supply one) — the v2
        // validator throws RECALL_SET_CODE_REQUIRED, which legacy CLI/scripts
        // can intercept and pass --recall-set-code instead.
        var res = await BulkImportVocabularyV2Async(adminId, adminName, file, dryRun: true, importBatchId: null, recallSetCode: null, ct);
        return new
        {
            importBatchId = res.ImportBatchId,
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
            total,
            page,
            pageSize,
            items = items.Select(t => new
            {
                t.Id,
                t.Title,
                t.TaskTypeCode,
                t.ProfessionId,
                t.Difficulty,
                estimatedDurationSeconds = t.EstimatedDurationSeconds,
                t.Status,
                t.PublishedAtUtc,
                t.CreatedAt,
                t.UpdatedAt,
            })
        };
    }

    public async Task<object> GetConversationTemplateDetailAsync(string templateId, CancellationToken ct)
    {
        var t = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        return new
        {
            t.Id,
            t.Title,
            t.TaskTypeCode,
            t.ProfessionId,
            t.Scenario,
            t.RoleDescription,
            t.PatientContext,
            t.ExpectedOutcomes,
            t.Difficulty,
            estimatedDurationSeconds = t.EstimatedDurationSeconds,
            objectives = JsonSupport.Deserialize<string[]>(t.ObjectivesJson, Array.Empty<string>()),
            expectedRedFlags = JsonSupport.Deserialize<string[]>(t.ExpectedRedFlagsJson, Array.Empty<string>()),
            keyVocabulary = JsonSupport.Deserialize<string[]>(t.KeyVocabularyJson, Array.Empty<string>()),
            patientVoice = JsonSupport.Deserialize<Dictionary<string, object?>>(t.PatientVoiceJson, new Dictionary<string, object?>()),
            t.Status,
            t.PublishedAtUtc,
            t.CreatedAt,
            t.UpdatedAt,
            t.CreatedByUserId,
            t.UpdatedByUserId,
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
            TipsHtml = SafeHtmlSanitizer.SanitizeLimitedHtml(request.TipsHtml),
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
        if (request.TipsHtml is not null) entity.TipsHtml = SafeHtmlSanitizer.SanitizeLimitedHtml(request.TipsHtml);
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
                MaxSpeakingMockSets = 1,
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
            config.MaxSpeakingMockSets,
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
        // Wave 3 — nullable in the request keeps backward compatibility
        // with admin clients that still post the legacy 7-field payload.
        if (request.MaxSpeakingMockSets is int mockSets)
        {
            config.MaxSpeakingMockSets = Math.Max(0, mockSets);
        }
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
            config.MaxSpeakingMockSets,
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
