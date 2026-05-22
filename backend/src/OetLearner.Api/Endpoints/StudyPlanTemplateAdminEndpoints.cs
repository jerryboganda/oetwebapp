using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Planner;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin authoring surface for <see cref="StudyPlanTemplate"/>. Templates are the
/// skeletons the StudyPlanGenerator materialises into a learner-specific plan.
/// Endpoints follow the existing admin pattern: group-level AdminOnly + PerUser
/// rate limit, per-endpoint Read/Write permission via the
/// AdminRouteBuilderExtensions helpers.
/// </summary>
public static class StudyPlanTemplateAdminEndpoints
{
    public static IEndpointRouteBuilder MapStudyPlanTemplateAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/study-plan-templates")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/", ListTemplates).WithAdminRead("AdminContentRead");
        admin.MapGet("/{id}", GetTemplate).WithAdminRead("AdminContentRead");
        admin.MapPost("/", CreateTemplate).WithAdminWrite("AdminContentWrite");
        admin.MapPut("/{id}", UpdateTemplate).WithAdminWrite("AdminContentWrite");
        admin.MapDelete("/{id}", SoftDeleteTemplate).WithAdminWrite("AdminContentWrite");
        admin.MapPost("/{id}/duplicate", DuplicateTemplate).WithAdminWrite("AdminContentWrite");
        admin.MapPost("/{id}/validate", ValidateTemplate).WithAdminRead("AdminContentRead");
        admin.MapPost("/{id}/preview", PreviewTemplate).WithAdminRead("AdminContentRead");
        admin.MapPost("/bulk", BulkAction).WithAdminWrite("AdminContentWrite");
        admin.MapGet("/{id}/tiers", GetTemplateTiers).WithAdminRead("AdminContentRead");
        admin.MapPut("/{id}/tiers", SetTemplateTiers).WithAdminWrite("AdminContentWrite");

        var adminPlan = app.MapGroup("/v1/admin/study-plan")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        adminPlan.MapGet("/{userId}", GetLearnerPlan).WithAdminRead("AdminContentRead");
        adminPlan.MapPost("/{userId}/regenerate", ForceRegenerate).WithAdminWrite("AdminContentWrite");
        adminPlan.MapPost("/{userId}/items/{itemId}/override", OverrideItem).WithAdminWrite("AdminContentWrite");

        return app;
    }

    // ── List / Get ─────────────────────────────────────────────────────────

    private static async Task<Ok<List<StudyPlanTemplateListItemDto>>> ListTemplates(
        LearnerDbContext db,
        [FromQuery] string? tier,
        [FromQuery] string? profession,
        [FromQuery] bool? active,
        CancellationToken ct)
    {
        var query = db.StudyPlanTemplates.AsNoTracking();
        if (active is not null) query = query.Where(t => t.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(t => t.ProfessionId == profession);

        IEnumerable<StudyPlanTemplate> templates = await query
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(tier))
        {
            var tierTemplateIds = await db.StudyPlanTemplateTiers
                .AsNoTracking()
                .Where(t => t.TierCode == tier)
                .Select(t => t.TemplateId)
                .ToListAsync(ct);
            var idSet = tierTemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            templates = templates.Where(t => idSet.Contains(t.Id));
        }

        var tierLookup = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .ToListAsync(ct);
        var tierByTemplate = tierLookup
            .GroupBy(t => t.TemplateId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.TierCode).ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = templates.Select(t => new StudyPlanTemplateListItemDto(
            t.Id,
            t.Slug,
            t.Name,
            t.Description,
            t.ExamTypeCode,
            t.MinWeeks,
            t.MaxWeeks,
            t.TargetBand,
            t.ProfessionId,
            ParseTags(t.FocusTagsJson),
            t.DefaultMinutesPerDay,
            t.IsActive,
            t.Version,
            tierByTemplate.TryGetValue(t.Id, out var tiers) ? tiers : new List<string>(),
            t.UpdatedAt
        )).ToList();

        return TypedResults.Ok(rows);
    }

    private static async Task<Results<Ok<StudyPlanTemplateDetailDto>, NotFound>> GetTemplate(
        string id,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.NotFound();

        var tiers = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .Where(t => t.TemplateId == id)
            .Select(t => t.TierCode)
            .ToListAsync(ct);

        return TypedResults.Ok(new StudyPlanTemplateDetailDto(
            template.Id,
            template.Slug,
            template.Name,
            template.Description,
            template.ExamTypeCode,
            template.MinWeeks,
            template.MaxWeeks,
            template.TargetBand,
            template.ProfessionId,
            ParseTags(template.FocusTagsJson),
            template.DefaultMinutesPerDay,
            template.IsActive,
            template.Version,
            tiers,
            template.UpdatedAt,
            ParseBody(template.TemplateBodyJson)));
    }

    // ── Create / Update / Delete ───────────────────────────────────────────

    private static async Task<Results<Created<StudyPlanTemplateDetailDto>, BadRequest<string>>> CreateTemplate(
        HttpContext http,
        StudyPlanTemplateUpsertRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("slug and name are required.");
        }

        var slugTaken = await db.StudyPlanTemplates.AnyAsync(t => t.Slug == request.Slug, ct);
        if (slugTaken) return TypedResults.BadRequest($"slug '{request.Slug}' already exists.");

        var body = request.Body ?? new StudyPlanTemplateBody();
        var (ok, errors) = ValidateTemplateBody(body);
        if (!ok) return TypedResults.BadRequest($"template body invalid: {string.Join("; ", errors)}");

        var now = DateTimeOffset.UtcNow;
        var adminId = ResolveAdminId(http);
        var template = new StudyPlanTemplate
        {
            Id = $"tmpl-{Guid.NewGuid():N}",
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Name = request.Name.Trim(),
            Description = request.Description,
            ExamTypeCode = string.IsNullOrWhiteSpace(request.ExamTypeCode) ? "OET" : request.ExamTypeCode.Trim(),
            MinWeeks = Math.Max(1, request.MinWeeks),
            MaxWeeks = Math.Max(request.MinWeeks, request.MaxWeeks),
            TargetBand = request.TargetBand,
            ProfessionId = request.ProfessionId,
            FocusTagsJson = JsonSerializer.Serialize(request.FocusTags ?? new List<string>()),
            DefaultMinutesPerDay = Math.Clamp(request.DefaultMinutesPerDay, 5, 480),
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = request.IsActive,
            Version = 1,
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.StudyPlanTemplates.Add(template);

        if (request.TierCodes is { Count: > 0 })
        {
            foreach (var tier in request.TierCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                db.StudyPlanTemplateTiers.Add(new StudyPlanTemplateTier
                {
                    Id = $"tpltier-{Guid.NewGuid():N}",
                    TemplateId = template.Id,
                    TierCode = tier
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(db, adminId, "study_plan_template.create", template.Id, ct);

        return TypedResults.Created($"/v1/admin/study-plan-templates/{template.Id}",
            new StudyPlanTemplateDetailDto(
                template.Id, template.Slug, template.Name, template.Description,
                template.ExamTypeCode, template.MinWeeks, template.MaxWeeks,
                template.TargetBand, template.ProfessionId, ParseTags(template.FocusTagsJson),
                template.DefaultMinutesPerDay, template.IsActive, template.Version,
                request.TierCodes ?? new List<string>(),
                template.UpdatedAt, body));
    }

    private static async Task<Results<Ok<StudyPlanTemplateDetailDto>, NotFound, BadRequest<string>>> UpdateTemplate(
        string id,
        HttpContext http,
        StudyPlanTemplateUpsertRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.NotFound();

        var body = request.Body ?? new StudyPlanTemplateBody();
        var (ok, errors) = ValidateTemplateBody(body);
        if (!ok) return TypedResults.BadRequest($"template body invalid: {string.Join("; ", errors)}");

        template.Name = request.Name.Trim();
        template.Description = request.Description;
        template.ExamTypeCode = string.IsNullOrWhiteSpace(request.ExamTypeCode) ? template.ExamTypeCode : request.ExamTypeCode.Trim();
        template.MinWeeks = Math.Max(1, request.MinWeeks);
        template.MaxWeeks = Math.Max(template.MinWeeks, request.MaxWeeks);
        template.TargetBand = request.TargetBand;
        template.ProfessionId = request.ProfessionId;
        template.FocusTagsJson = JsonSerializer.Serialize(request.FocusTags ?? new List<string>());
        template.DefaultMinutesPerDay = Math.Clamp(request.DefaultMinutesPerDay, 5, 480);
        template.TemplateBodyJson = JsonSerializer.Serialize(body);
        template.IsActive = request.IsActive;
        template.Version += 1;
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        var adminId = ResolveAdminId(http);
        await RecordAuditAsync(db, adminId, "study_plan_template.update", template.Id, ct);

        var tiers = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .Where(t => t.TemplateId == id)
            .Select(t => t.TierCode)
            .ToListAsync(ct);

        return TypedResults.Ok(new StudyPlanTemplateDetailDto(
            template.Id, template.Slug, template.Name, template.Description,
            template.ExamTypeCode, template.MinWeeks, template.MaxWeeks,
            template.TargetBand, template.ProfessionId, ParseTags(template.FocusTagsJson),
            template.DefaultMinutesPerDay, template.IsActive, template.Version,
            tiers, template.UpdatedAt, body));
    }

    private static async Task<Results<NoContent, NotFound>> SoftDeleteTemplate(
        string id,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.NotFound();

        template.IsActive = false;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var adminId = ResolveAdminId(http);
        await RecordAuditAsync(db, adminId, "study_plan_template.soft_delete", template.Id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<StudyPlanTemplateDetailDto>, NotFound>> DuplicateTemplate(
        string id,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var src = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (src is null) return TypedResults.NotFound();

        var srcTiers = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .Where(t => t.TemplateId == id)
            .Select(t => t.TierCode)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var adminId = ResolveAdminId(http);
        var copy = new StudyPlanTemplate
        {
            Id = $"tmpl-{Guid.NewGuid():N}",
            Slug = $"{src.Slug}-copy-{now.Ticks % 100000}",
            Name = $"{src.Name} (copy)",
            Description = src.Description,
            ExamTypeCode = src.ExamTypeCode,
            ExamFamilyCode = src.ExamFamilyCode,
            MinWeeks = src.MinWeeks,
            MaxWeeks = src.MaxWeeks,
            TargetBand = src.TargetBand,
            ProfessionId = src.ProfessionId,
            FocusTagsJson = src.FocusTagsJson,
            DefaultMinutesPerDay = src.DefaultMinutesPerDay,
            TemplateBodyJson = src.TemplateBodyJson,
            IsActive = false, // duplicates start inactive — admin opts them in
            Version = 1,
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.StudyPlanTemplates.Add(copy);

        foreach (var tier in srcTiers)
        {
            db.StudyPlanTemplateTiers.Add(new StudyPlanTemplateTier
            {
                Id = $"tpltier-{Guid.NewGuid():N}",
                TemplateId = copy.Id,
                TierCode = tier
            });
        }

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(db, adminId, "study_plan_template.duplicate", copy.Id, ct);

        return TypedResults.Created($"/v1/admin/study-plan-templates/{copy.Id}",
            new StudyPlanTemplateDetailDto(
                copy.Id, copy.Slug, copy.Name, copy.Description,
                copy.ExamTypeCode, copy.MinWeeks, copy.MaxWeeks,
                copy.TargetBand, copy.ProfessionId, ParseTags(copy.FocusTagsJson),
                copy.DefaultMinutesPerDay, copy.IsActive, copy.Version,
                srcTiers, copy.UpdatedAt, ParseBody(copy.TemplateBodyJson)));
    }

    // ── Validate / Preview / Bulk ──────────────────────────────────────────

    private static async Task<Ok<StudyPlanTemplateValidationDto>> ValidateTemplate(
        string id,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.Ok(new StudyPlanTemplateValidationDto(false, new[] { "template not found" }));

        var body = ParseBody(template.TemplateBodyJson);
        var (ok, errors) = ValidateTemplateBody(body);
        return TypedResults.Ok(new StudyPlanTemplateValidationDto(ok, errors));
    }

    private static async Task<Results<Ok<StudyPlanTemplatePreviewDto>, NotFound>> PreviewTemplate(
        string id,
        StudyPlanTemplatePreviewRequest request,
        LearnerDbContext db,
        ContentPicker contentPicker,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.NotFound();

        var body = ParseBody(template.TemplateBodyJson);
        var alreadyPicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dayPreviews = new List<StudyPlanTemplatePreviewDayDto>();
        var totalWeeks = Math.Min(request.WeeksToPreview > 0 ? request.WeeksToPreview : 2, body.Weeks.Count);

        for (var w = 0; w < totalWeeks; w++)
        {
            var weekDef = body.Weeks[w];
            foreach (var day in weekDef.Days)
            {
                var slotDtos = new List<StudyPlanTemplatePreviewSlotDto>();
                foreach (var slot in day.Slots)
                {
                    if (slot.Kind == StudyPlanSlotKinds.SpacedRepReview)
                    {
                        slotDtos.Add(new StudyPlanTemplatePreviewSlotDto(slot.Subtest, slot.Kind, slot.Minutes,
                            Title: "Spaced-rep review (resolved at runtime)", ContentId: null, Route: "/recalls"));
                        continue;
                    }

                    var resolved = await contentPicker.ResolveAsync(slot, "preview-stub", request.ProfessionId, w, alreadyPicked, ct);
                    slotDtos.Add(new StudyPlanTemplatePreviewSlotDto(slot.Subtest, slot.Kind, slot.Minutes,
                        Title: resolved.Title, ContentId: resolved.ContentId, Route: resolved.Route));
                }
                dayPreviews.Add(new StudyPlanTemplatePreviewDayDto(w, day.DayOfWeek, slotDtos));
            }
        }

        return TypedResults.Ok(new StudyPlanTemplatePreviewDto(template.Id, template.Slug, dayPreviews));
    }

    private static async Task<Results<Ok<StudyPlanTemplateBulkResultDto>, BadRequest<string>>> BulkAction(
        HttpContext http,
        StudyPlanTemplateBulkRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (request.TemplateIds is null || request.TemplateIds.Count == 0)
        {
            return TypedResults.BadRequest("templateIds is required.");
        }
        var validActions = new[] { "activate", "deactivate", "duplicate", "soft-delete" };
        if (!validActions.Contains(request.Action, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest($"action must be one of: {string.Join(",", validActions)}");
        }

        var ids = request.TemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templates = await db.StudyPlanTemplates.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var adminId = ResolveAdminId(http);
        var processed = 0;

        foreach (var t in templates)
        {
            switch (request.Action.ToLowerInvariant())
            {
                case "activate":
                    t.IsActive = true;
                    break;
                case "deactivate":
                case "soft-delete":
                    t.IsActive = false;
                    break;
                case "duplicate":
                    db.StudyPlanTemplates.Add(new StudyPlanTemplate
                    {
                        Id = $"tmpl-{Guid.NewGuid():N}",
                        Slug = $"{t.Slug}-copy-{now.Ticks % 100000}",
                        Name = $"{t.Name} (copy)",
                        Description = t.Description,
                        ExamTypeCode = t.ExamTypeCode,
                        ExamFamilyCode = t.ExamFamilyCode,
                        MinWeeks = t.MinWeeks,
                        MaxWeeks = t.MaxWeeks,
                        TargetBand = t.TargetBand,
                        ProfessionId = t.ProfessionId,
                        FocusTagsJson = t.FocusTagsJson,
                        DefaultMinutesPerDay = t.DefaultMinutesPerDay,
                        TemplateBodyJson = t.TemplateBodyJson,
                        IsActive = false,
                        Version = 1,
                        CreatedBy = adminId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    break;
            }
            t.UpdatedAt = now;
            processed++;
        }

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(db, adminId, $"study_plan_template.bulk.{request.Action}", string.Join(",", ids), ct);
        return TypedResults.Ok(new StudyPlanTemplateBulkResultDto(processed));
    }

    // ── Tier links ─────────────────────────────────────────────────────────

    private static async Task<Ok<List<string>>> GetTemplateTiers(string id, LearnerDbContext db, CancellationToken ct)
    {
        var tiers = await db.StudyPlanTemplateTiers
            .AsNoTracking()
            .Where(t => t.TemplateId == id)
            .Select(t => t.TierCode)
            .ToListAsync(ct);
        return TypedResults.Ok(tiers);
    }

    private static async Task<Results<Ok<List<string>>, NotFound>> SetTemplateTiers(
        string id,
        HttpContext http,
        StudyPlanTemplateTiersRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var template = await db.StudyPlanTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return TypedResults.NotFound();

        var existing = await db.StudyPlanTemplateTiers.Where(t => t.TemplateId == id).ToListAsync(ct);
        db.StudyPlanTemplateTiers.RemoveRange(existing);

        var distinct = (request.TierCodes ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        foreach (var tier in distinct)
        {
            db.StudyPlanTemplateTiers.Add(new StudyPlanTemplateTier
            {
                Id = $"tpltier-{Guid.NewGuid():N}",
                TemplateId = id,
                TierCode = tier
            });
        }

        await db.SaveChangesAsync(ct);
        var adminId = ResolveAdminId(http);
        await RecordAuditAsync(db, adminId, "study_plan_template.set_tiers", id, ct);

        return TypedResults.Ok(distinct);
    }

    // ── Learner plan admin ops ─────────────────────────────────────────────

    private static async Task<Results<Ok<object>, NotFound>> GetLearnerPlan(
        string userId,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var plan = await db.StudyPlans
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
        if (plan is null) return TypedResults.NotFound();

        var items = await db.StudyPlanItems
            .AsNoTracking()
            .Where(i => i.StudyPlanId == plan.Id)
            .OrderBy(i => i.WeekIndex)
            .ThenBy(i => i.DueDate)
            .ToListAsync(ct);

        return TypedResults.Ok((object)new
        {
            plan.Id,
            plan.UserId,
            plan.Version,
            plan.GeneratedAt,
            plan.TemplateId,
            plan.TotalWeeks,
            plan.WeekNumber,
            plan.MinutesPerDayBudget,
            plan.EntitlementTierAtGeneration,
            plan.SubtestWeightsJson,
            plan.Checkpoint,
            plan.WeakSkillFocus,
            items = items.Select(i => new
            {
                i.Id,
                i.Title,
                i.SubtestCode,
                i.DurationMinutes,
                i.DueDate,
                i.Status,
                i.Section,
                i.WeekIndex,
                i.SlotKind,
                i.ContentRoute,
                i.SourceContentId,
                i.LinkedReviewItemId,
                i.ReplacedById,
                i.PriorityScore,
                i.CompletedAt,
                i.FeedbackRating
            }).ToList()
        });
    }

    private static async Task<Results<Ok<object>, BadRequest<string>>> ForceRegenerate(
        string userId,
        HttpContext http,
        LearnerDbContext db,
        IStudyPlanGenerator generator,
        CancellationToken ct)
    {
        var result = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.AdminForce, ct);
        var adminId = ResolveAdminId(http);
        await RecordAuditAsync(db, adminId, "study_plan.admin_force_regen", userId, ct);
        return TypedResults.Ok((object)result);
    }

    private static async Task<Results<Ok<object>, NotFound>> OverrideItem(
        string userId,
        string itemId,
        HttpContext http,
        StudyPlanItemOverrideRequest request,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var item = await db.StudyPlanItems
            .Where(i => i.Id == itemId)
            .Join(db.StudyPlans, i => i.StudyPlanId, p => p.Id, (i, p) => new { item = i, plan = p })
            .FirstOrDefaultAsync(x => x.plan.UserId == userId, ct);
        if (item is null) return TypedResults.NotFound();

        if (!string.IsNullOrWhiteSpace(request.Title)) item.item.Title = request.Title;
        if (!string.IsNullOrWhiteSpace(request.Rationale)) item.item.Rationale = request.Rationale;
        if (request.DueDate is not null) item.item.DueDate = request.DueDate.Value;
        if (request.DurationMinutes is not null) item.item.DurationMinutes = request.DurationMinutes.Value;
        if (!string.IsNullOrWhiteSpace(request.ContentRoute)) item.item.ContentRoute = request.ContentRoute;
        if (!string.IsNullOrWhiteSpace(request.Section)) item.item.Section = request.Section;

        await db.SaveChangesAsync(ct);
        var adminId = ResolveAdminId(http);
        await RecordAuditAsync(db, adminId, "study_plan.admin_override_item", $"{userId}:{itemId}", ct);
        return TypedResults.Ok((object)new { item.item.Id, item.item.Title, item.item.DueDate, item.item.DurationMinutes });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ParseTags(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static StudyPlanTemplateBody ParseBody(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new StudyPlanTemplateBody();
        try { return JsonSerializer.Deserialize<StudyPlanTemplateBody>(json) ?? new StudyPlanTemplateBody(); }
        catch { return new StudyPlanTemplateBody(); }
    }

    private static (bool ok, IReadOnlyList<string> errors) ValidateTemplateBody(StudyPlanTemplateBody body)
    {
        var errors = new List<string>();
        if (body.Weeks.Count == 0) errors.Add("at least one week required");
        for (var i = 0; i < body.Weeks.Count; i++)
        {
            var w = body.Weeks[i];
            if (w.Days.Count == 0) errors.Add($"week {i}: no days defined");
            foreach (var d in w.Days)
            {
                if (d.Slots.Count == 0) errors.Add($"week {i} day {d.DayOfWeek}: no slots");
                foreach (var s in d.Slots)
                {
                    if (s.Minutes <= 0) errors.Add($"week {i} day {d.DayOfWeek}: slot has zero minutes");
                    if (string.IsNullOrWhiteSpace(s.Subtest)) errors.Add($"week {i} day {d.DayOfWeek}: slot missing subtest");
                    if (string.IsNullOrWhiteSpace(s.Kind)) errors.Add($"week {i} day {d.DayOfWeek}: slot missing kind");
                }
            }
        }
        return (errors.Count == 0, errors);
    }

    private static string ResolveAdminId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin-unknown";

    private static async Task RecordAuditAsync(LearnerDbContext db, string adminId, string action, string resourceId, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            ActorId = adminId,
            Action = action,
            ResourceType = "study_plan_template",
            ResourceId = resourceId,
            OccurredAt = DateTimeOffset.UtcNow,
            Details = "{}"
        });
        await db.SaveChangesAsync(ct);
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────

public sealed record StudyPlanTemplateListItemDto(
    string Id,
    string Slug,
    string Name,
    string? Description,
    string ExamTypeCode,
    int MinWeeks,
    int MaxWeeks,
    string? TargetBand,
    string? ProfessionId,
    IReadOnlyList<string> FocusTags,
    int DefaultMinutesPerDay,
    bool IsActive,
    int Version,
    IReadOnlyList<string> TierCodes,
    DateTimeOffset UpdatedAt);

public sealed record StudyPlanTemplateDetailDto(
    string Id,
    string Slug,
    string Name,
    string? Description,
    string ExamTypeCode,
    int MinWeeks,
    int MaxWeeks,
    string? TargetBand,
    string? ProfessionId,
    IReadOnlyList<string> FocusTags,
    int DefaultMinutesPerDay,
    bool IsActive,
    int Version,
    IReadOnlyList<string> TierCodes,
    DateTimeOffset UpdatedAt,
    StudyPlanTemplateBody Body);

public sealed record StudyPlanTemplateUpsertRequest(
    string Slug,
    string Name,
    string? Description,
    string? ExamTypeCode,
    int MinWeeks,
    int MaxWeeks,
    string? TargetBand,
    string? ProfessionId,
    List<string>? FocusTags,
    int DefaultMinutesPerDay,
    bool IsActive,
    List<string>? TierCodes,
    StudyPlanTemplateBody? Body);

public sealed record StudyPlanTemplateValidationDto(bool IsValid, IReadOnlyList<string> Errors);

public sealed record StudyPlanTemplatePreviewRequest(
    string? ProfessionId,
    string? TargetBand,
    int WeeksToPreview);

public sealed record StudyPlanTemplatePreviewDto(
    string TemplateId,
    string Slug,
    IReadOnlyList<StudyPlanTemplatePreviewDayDto> Days);

public sealed record StudyPlanTemplatePreviewDayDto(
    int WeekIndex,
    string DayOfWeek,
    IReadOnlyList<StudyPlanTemplatePreviewSlotDto> Slots);

public sealed record StudyPlanTemplatePreviewSlotDto(
    string Subtest,
    string Kind,
    int Minutes,
    string Title,
    string? ContentId,
    string Route);

public sealed record StudyPlanTemplateBulkRequest(
    string Action,
    List<string> TemplateIds);

public sealed record StudyPlanTemplateBulkResultDto(int Processed);

public sealed record StudyPlanTemplateTiersRequest(List<string>? TierCodes);

public sealed record StudyPlanItemOverrideRequest(
    string? Title,
    string? Rationale,
    DateOnly? DueDate,
    int? DurationMinutes,
    string? ContentRoute,
    string? Section);
