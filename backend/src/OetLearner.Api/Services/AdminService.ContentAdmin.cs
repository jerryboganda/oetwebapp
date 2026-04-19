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
            v.AudioUrl,
            v.ImageUrl,
            v.SynonymsJson,
            v.CollocationsJson,
            v.RelatedTermsJson,
            v.Status
        };
    }

    public async Task<object> CreateVocabularyItemAsync(
        string adminId, string adminName, AdminVocabularyItemCreateRequest request, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        var id = $"VOC-{Guid.NewGuid():N}"[..12];
        var entity = new VocabularyTerm
        {
            Id = id,
            Term = request.Term,
            Definition = request.Definition,
            ProfessionId = request.ProfessionId,
            Category = request.Category,
            AudioUrl = request.Pronunciation,
            ExampleSentence = request.ExampleSentence,
            Difficulty = request.Difficulty ?? "intermediate",
            Status = "active"
        };
        db.VocabularyTerms.Add(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "VocabularyTerm", id, $"Created vocabulary item: {request.Term}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { id, entity.Term, entity.Status };
    }

    public async Task<object> UpdateVocabularyItemAsync(
        string adminId, string adminName, string itemId, AdminVocabularyItemUpdateRequest request, CancellationToken ct)
    {
        var entity = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw ApiException.NotFound("VOCABULARY_NOT_FOUND", $"Vocabulary item '{itemId}' not found.");

        if (request.Term is not null) entity.Term = request.Term;
        if (request.Definition is not null) entity.Definition = request.Definition;
        if (request.ProfessionId is not null) entity.ProfessionId = request.ProfessionId;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.Pronunciation is not null) entity.AudioUrl = request.Pronunciation;
        if (request.ExampleSentence is not null) entity.ExampleSentence = request.ExampleSentence;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.Status is not null) entity.Status = request.Status;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "VocabularyTerm", itemId, $"Updated vocabulary item: {entity.Term}", ct);

        return new { id = itemId, entity.Status };
    }

    public async Task<object> DeleteVocabularyItemAsync(
        string adminId, string adminName, string itemId, CancellationToken ct)
    {
        var entity = await db.VocabularyTerms.FirstOrDefaultAsync(x => x.Id == itemId, ct)
            ?? throw ApiException.NotFound("VOCABULARY_NOT_FOUND", $"Vocabulary item '{itemId}' not found.");

        db.VocabularyTerms.Remove(entity);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Deleted", "VocabularyTerm", itemId, $"Deleted vocabulary item: {entity.Term}", ct);

        return new { id = itemId, deleted = true };
    }

    public async Task<object> BulkImportVocabularyAsync(
        string adminId, string adminName, IFormFile file, CancellationToken ct)
    {
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw ApiException.Validation("INVALID_CSV", "CSV file is empty or missing header row.");

        var headers = headerLine.Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int Col(string name) => Array.IndexOf(headers, name.ToLowerInvariant());

        var termIdx = Col("term");
        var defIdx = Col("definition");
        if (termIdx < 0 || defIdx < 0)
            throw ApiException.Validation("INVALID_CSV", "CSV must have 'Term' and 'Definition' columns.");

        var exIdx = Col("examplesentence");
        var catIdx = Col("category");
        var diffIdx = Col("difficulty");
        var profIdx = Col("professionid");

        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        string? line;
        var lineNum = 1;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) { skipped++; continue; }

            var cols = line.Split(',');
            if (cols.Length <= termIdx || cols.Length <= defIdx)
            {
                errors.Add($"Row {lineNum}: insufficient columns");
                skipped++;
                continue;
            }

            var term = cols[termIdx].Trim();
            var definition = cols[defIdx].Trim();
            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(definition))
            {
                errors.Add($"Row {lineNum}: empty term or definition");
                skipped++;
                continue;
            }

            var id = $"VOC-{Guid.NewGuid():N}"[..12];
            db.VocabularyTerms.Add(new VocabularyTerm
            {
                Id = id,
                Term = term,
                Definition = definition,
                ExampleSentence = exIdx >= 0 && cols.Length > exIdx ? cols[exIdx].Trim() : null,
                Category = catIdx >= 0 && cols.Length > catIdx ? cols[catIdx].Trim() : null,
                Difficulty = diffIdx >= 0 && cols.Length > diffIdx ? cols[diffIdx].Trim() : "intermediate",
                ProfessionId = profIdx >= 0 && cols.Length > profIdx ? cols[profIdx].Trim() : null,
                Status = "active"
            });
            imported++;
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Bulk Import", "VocabularyTerm", "bulk",
            $"Imported {imported} vocabulary items, skipped {skipped}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new { imported, skipped, errors = errors.Take(20) };
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
                t.ProfessionId,
                t.Difficulty,
                t.EstimatedDurationMinutes,
                t.Status,
                t.CreatedAt
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
            t.ProfessionId,
            t.Scenario,
            t.RoleDescription,
            t.PatientContext,
            t.ExpectedOutcomes,
            t.Difficulty,
            t.EstimatedDurationMinutes,
            t.Status,
            t.CreatedAt,
            t.UpdatedAt
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
            ProfessionId = request.ProfessionId,
            Scenario = request.Scenario,
            RoleDescription = request.RoleDescription,
            PatientContext = request.PatientContext,
            ExpectedOutcomes = request.ExpectedOutcomes,
            Difficulty = request.Difficulty ?? "medium",
            EstimatedDurationMinutes = request.EstimatedDurationMinutes ?? 5,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
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
        if (request.ProfessionId is not null) entity.ProfessionId = request.ProfessionId;
        if (request.Scenario is not null) entity.Scenario = request.Scenario;
        if (request.RoleDescription is not null) entity.RoleDescription = request.RoleDescription;
        if (request.PatientContext is not null) entity.PatientContext = request.PatientContext;
        if (request.ExpectedOutcomes is not null) entity.ExpectedOutcomes = request.ExpectedOutcomes;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.EstimatedDurationMinutes is not null) entity.EstimatedDurationMinutes = request.EstimatedDurationMinutes.Value;
        if (request.Status is not null) entity.Status = request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "ConversationTemplate", templateId, $"Updated conversation template: {entity.Title}", ct);

        return new { id = templateId, entity.Status };
    }

    public async Task<object> ArchiveConversationTemplateAsync(
        string adminId, string adminName, string templateId, CancellationToken ct)
    {
        var entity = await db.ConversationTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw ApiException.NotFound("CONVERSATION_TEMPLATE_NOT_FOUND", $"Conversation template '{templateId}' not found.");

        entity.Status = "archived";
        entity.UpdatedAt = DateTimeOffset.UtcNow;
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
                phoneticTranscription = d.TargetPhoneme,
                d.AudioModelUrl,
                d.Difficulty,
                d.Status
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
            phoneticTranscription = d.TargetPhoneme,
            audioUrl = d.AudioModelUrl,
            d.ExampleWordsJson,
            d.MinimalPairsJson,
            d.SentencesJson,
            d.TipsHtml,
            d.Difficulty,
            d.Status
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
            AudioModelUrl = request.AudioUrl,
            Difficulty = request.Difficulty ?? "intermediate",
            Status = "active"
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
        if (request.AudioUrl is not null) entity.AudioModelUrl = request.AudioUrl;
        if (request.Difficulty is not null) entity.Difficulty = request.Difficulty;
        if (request.Status is not null) entity.Status = request.Status;

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
