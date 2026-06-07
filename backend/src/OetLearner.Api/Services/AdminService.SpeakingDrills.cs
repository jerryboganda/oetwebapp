using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services;

// Phase 5 (G) of the OET Speaking module roadmap.
//
// Admin CRUD for `SpeakingDrillItem`. Mirrors the existing
// `AdminService.SpeakingMockSets.cs` style: each operation is audit-
// logged + transactional, and the publish gate flips both the
// underlying `ContentItem.Status` and surfaces the row to learner
// listings.
//
// Each drill is backed by a `ContentItem` (ContentType="speaking_drill",
// SubtestCode="speaking") whose Title/DetailJson hold the learner-facing
// prompt + instruction text. The `SpeakingDrillItem` row holds the
// drill kind, criteria mapping, and recommendation threshold.
//
// Permissions reuse `AdminContentRead` / `AdminContentWrite` /
// `AdminContentPublish` so admins authoring role-play cards already
// have the right grants.
public partial class AdminService
{
    private static readonly HashSet<string> ValidDrillKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Opening","Empathy","Ice","OpenQuestion","LayLanguage","Signposting",
        "CheckingUnderstanding","Reassurance","Closing","Pronunciation","Fluency","Grammar",
    };

    public async Task<object> ListSpeakingDrillsAsync(
        string? drillKind,
        string? professionId,
        string? status,
        CancellationToken ct)
    {
        var q = from drill in db.SpeakingDrillItems.AsNoTracking()
                join content in db.ContentItems.AsNoTracking()
                    on drill.ContentItemId equals content.Id
                select new { drill, content };

        if (!string.IsNullOrWhiteSpace(drillKind)
            && Enum.TryParse<SpeakingDrillKind>(drillKind.Trim(), ignoreCase: true, out var kind))
        {
            q = q.Where(x => x.drill.DrillKind == kind);
        }
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var pid = professionId.Trim();
            q = q.Where(x => x.content.ProfessionId == pid);
        }
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ContentStatus>(status.Trim(), ignoreCase: true, out var st))
        {
            q = q.Where(x => x.content.Status == st);
        }

        var rows = await q
            .OrderBy(x => x.drill.DrillKind)
            .ThenBy(x => x.content.Title)
            .Take(500)
            .ToListAsync(ct);

        return new
        {
            drills = rows.Select(r => ProjectSummaryRow(r.drill, r.content)).ToArray(),
        };
    }

    public async Task<object> GetSpeakingDrillAsync(string drillId, CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");
        return ProjectDetail(drill, content);
    }

    public async Task<object> CreateSpeakingDrillAsync(
        string adminId,
        string adminName,
        AdminDrillCreateRequest request,
        CancellationToken ct)
    {
        ValidateDrillRequest(request.DrillKind, request.Title, request.InstructionText, request.TargetCriteria);
        var kind = ParseDrillKind(request.DrillKind);

        var now = DateTimeOffset.UtcNow;
        var contentId = $"ci-drill-{Guid.NewGuid():N}";
        var drillId = $"sdi-{Guid.NewGuid():N}";

        var content = new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_drill",
            SubtestCode = "speaking",
            ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim(),
            Title = request.Title.Trim(),
            Difficulty = "core",
            EstimatedDurationMinutes = 1,
            CriteriaFocusJson = JsonSupport.Serialize(request.TargetCriteria ?? Array.Empty<string>()),
            ScenarioType = kind.ToString().ToLowerInvariant(),
            ModeSupportJson = JsonSupport.Serialize(new[] { "learning" }),
            PublishedRevisionId = $"rev-{drillId}",
            Status = ContentStatus.Draft,
            CaseNotes = null,
            DetailJson = JsonSupport.Serialize(new
            {
                instructionText = request.InstructionText.Trim(),
                drillKind = kind.ToString(),
                targetCriteria = request.TargetCriteria ?? Array.Empty<string>(),
            }),
            ModelAnswerJson = "{}",
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            DifficultyRating = 1500,
            SourceType = "manual",
            SourceProvenance = "original",
            RightsStatus = "owned",
            QaStatus = "approved",
            FreshnessConfidence = "current",
            InstructionLanguage = "en",
            ContentLanguage = "en",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var drill = new SpeakingDrillItem
        {
            Id = drillId,
            ContentItemId = contentId,
            DrillKind = kind,
            TargetCriteriaJson = JsonSupport.Serialize(request.TargetCriteria ?? Array.Empty<string>()),
            RecommendedAfterSessionScoreBelow = request.RecommendedAfterSessionScoreBelow,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(content);
        db.SpeakingDrillItems.Add(drill);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Created", "SpeakingDrill", drillId,
            $"Created drill: {content.Title} ({kind})", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> UpdateSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        AdminDrillUpdateRequest request,
        CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");

        if (content.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("speaking_drill_archived",
                "Archived drills are read-only.");
        }

        if (request.DrillKind is not null)
        {
            var kind = ParseDrillKind(request.DrillKind);
            drill.DrillKind = kind;
            content.ScenarioType = kind.ToString().ToLowerInvariant();
        }
        if (request.ProfessionId is not null)
        {
            content.ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim();
        }
        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw ApiException.Validation("SPEAKING_DRILL_TITLE_REQUIRED", "Title is required.");
            }
            content.Title = request.Title.Trim();
        }
        if (request.InstructionText is not null)
        {
            if (string.IsNullOrWhiteSpace(request.InstructionText))
            {
                throw ApiException.Validation("SPEAKING_DRILL_INSTRUCTION_REQUIRED", "Instruction text is required.");
            }
            content.DetailJson = JsonSupport.Serialize(new
            {
                instructionText = request.InstructionText.Trim(),
                drillKind = drill.DrillKind.ToString(),
                targetCriteria = request.TargetCriteria ?? ParseTargetCriteria(drill.TargetCriteriaJson),
            });
        }
        if (request.TargetCriteria is not null)
        {
            drill.TargetCriteriaJson = JsonSupport.Serialize(request.TargetCriteria);
            content.CriteriaFocusJson = JsonSupport.Serialize(request.TargetCriteria);
            // Keep DetailJson aligned with the new criteria.
            content.DetailJson = JsonSupport.Serialize(new
            {
                instructionText = ParseInstructionText(content.DetailJson),
                drillKind = drill.DrillKind.ToString(),
                targetCriteria = request.TargetCriteria,
            });
        }
        if (request.RecommendedAfterSessionScoreBelow.HasValue)
        {
            drill.RecommendedAfterSessionScoreBelow = request.RecommendedAfterSessionScoreBelow.Value;
        }

        var now = DateTimeOffset.UtcNow;
        drill.UpdatedAt = now;
        content.UpdatedAt = now;

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Updated", "SpeakingDrill", drillId,
            $"Updated drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> PublishSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        var (drill, content) = await LoadDrillWithContentAsync(drillId, ct);

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        ApplyPublishDrill(drill, content);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Published", "SpeakingDrill", drillId,
            $"Published drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> ArchiveSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        var (drill, content) = await LoadDrillWithContentAsync(drillId, ct);

        // Already-archived drills are an idempotent no-op (no audit row),
        // matching the original singular-endpoint behaviour.
        if (content.Status == ContentStatus.Archived)
        {
            return ProjectDetail(drill, content);
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        ApplyArchiveDrill(drill, content);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Archived", "SpeakingDrill", drillId,
            $"Archived drill: {content.Title}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return ProjectDetail(drill, content);
    }

    public async Task<object> DeleteSpeakingDrillAsync(
        string adminId,
        string adminName,
        string drillId,
        CancellationToken ct)
    {
        // Soft-delete via Archive — keeps audit + analytics intact.
        return await ArchiveSpeakingDrillAsync(adminId, adminName, drillId, ct);
    }

    // ── Bulk (publish | archive | delete) ───────────────────────────────────
    //
    // T3: a single atomic bulk endpoint. The whole batch runs inside ONE
    // transaction and emits exactly ONE audit entry summarising the op.
    // Each id is processed through the SAME no-audit core mutators the
    // per-item endpoints use (ApplyPublishDrill / ApplyArchiveDrill), so the
    // behaviour stays in lock-step with the singular routes. A per-id
    // InvalidOperationException (e.g. publishing an archived drill) is
    // recorded as a Failed row (errors capped at ~20) without aborting the
    // batch; an unexpected/fatal error rolls the whole transaction back.
    private const int BulkDrillLimit = 2000;
    private const int BulkDrillErrorCap = 20;

    public async Task<object> BulkSpeakingDrillsAsync(
        string adminId,
        string adminName,
        string action,
        IReadOnlyList<string> ids,
        CancellationToken ct)
    {
        var normalisedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (normalisedAction is not ("publish" or "archive" or "delete"))
        {
            throw ApiException.Validation("SPEAKING_DRILL_BULK_ACTION_INVALID",
                "Action must be one of: publish, archive, delete.");
        }

        // Action-specific permission gate. The route only guarantees
        // AdminContentWrite (the minimum shared by all three actions); the
        // stricter publish grant is enforced here because the required
        // permission depends on the request body, not the route. archive and
        // delete need only AdminContentWrite, already enforced by the route.
        if (normalisedAction == "publish")
        {
            var perms = await GetEffectivePermissionsAsync(adminId, ct);
            if (!perms.Contains(AdminPermissions.ContentPublish)
                && !perms.Contains(AdminPermissions.SystemAdmin))
            {
                throw ApiException.Forbidden("insufficient_permission",
                    "Bulk publishing speaking drills requires the content:publish permission.");
            }
        }

        var requestedIds = (ids ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requestedIds.Count == 0)
        {
            throw ApiException.Validation("SPEAKING_DRILL_BULK_EMPTY",
                "Select at least one speaking drill.");
        }
        if (requestedIds.Count > BulkDrillLimit)
        {
            throw ApiException.Validation("SPEAKING_DRILL_BULK_LIMIT",
                $"Bulk {normalisedAction} is limited to {BulkDrillLimit} drills at a time.");
        }

        var succeeded = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        foreach (var drillId in requestedIds)
        {
            var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct);
            if (drill is null)
            {
                failed++;
                if (errors.Count < BulkDrillErrorCap) errors.Add($"{drillId}: not found");
                continue;
            }
            var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct);
            if (content is null)
            {
                failed++;
                if (errors.Count < BulkDrillErrorCap) errors.Add($"{drillId}: content item missing");
                continue;
            }

            try
            {
                bool changed = normalisedAction switch
                {
                    "publish" => ApplyPublishDrill(drill, content),
                    // delete is a soft-delete via archive (see DeleteSpeakingDrillAsync).
                    _ => ApplyArchiveDrill(drill, content),
                };
                if (changed) succeeded++; else skipped++;
            }
            catch (ApiException ex)
            {
                // Recoverable per-item failure (e.g. publishing an archived
                // drill → 409 conflict). Record it and keep going; the batch
                // is NOT aborted. Any non-ApiException is treated as fatal and
                // propagates so the whole transaction rolls back.
                failed++;
                if (errors.Count < BulkDrillErrorCap) errors.Add($"{drillId}: {ex.Message}");
            }
        }

        if (succeeded > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var auditAction = normalisedAction switch
        {
            "publish" => "BulkPublished",
            "archive" => "BulkArchived",
            _ => "BulkDeleted",
        };
        await LogAuditAsync(adminId, adminName, auditAction, "SpeakingDrill",
            $"bulk:{normalisedAction}",
            $"Bulk {normalisedAction}: {succeeded} succeeded, {skipped} skipped, {failed} failed "
            + $"of {requestedIds.Count} requested.", ct);

        await CommitIfOwnedAsync(tx, ct);

        return new
        {
            totalRequested = requestedIds.Count,
            succeeded,
            skipped,
            failed,
            errors = errors.ToArray(),
        };
    }

    private async Task<(SpeakingDrillItem drill, ContentItem content)> LoadDrillWithContentAsync(
        string drillId, CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.FirstOrDefaultAsync(x => x.Id == drillId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That speaking drill does not exist.");
        var content = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == drill.ContentItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_content_missing",
                "The underlying content item for this drill is missing.");
        return (drill, content);
    }

    /// <summary>Core publish mutation (no audit, no save). Returns true when
    /// the drill changed state. Throws <see cref="InvalidOperationException"/>
    /// (via <see cref="ApiException.Conflict"/>) when the drill is archived.</summary>
    private static bool ApplyPublishDrill(SpeakingDrillItem drill, ContentItem content)
    {
        if (content.Status == ContentStatus.Archived)
        {
            throw ApiException.Conflict("speaking_drill_archived",
                "Archived drills cannot be published.");
        }
        if (content.Status == ContentStatus.Published)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        content.Status = ContentStatus.Published;
        content.PublishedAt = now;
        content.UpdatedAt = now;
        drill.UpdatedAt = now;
        return true;
    }

    /// <summary>Core archive mutation (no audit, no save). Returns true when
    /// the drill changed state; already-archived drills are a no-op.</summary>
    private static bool ApplyArchiveDrill(SpeakingDrillItem drill, ContentItem content)
    {
        if (content.Status == ContentStatus.Archived)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        content.Status = ContentStatus.Archived;
        content.ArchivedAt = now;
        content.UpdatedAt = now;
        drill.UpdatedAt = now;
        return true;
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static SpeakingDrillKind ParseDrillKind(string raw)
    {
        if (!Enum.TryParse<SpeakingDrillKind>(raw?.Trim(), ignoreCase: true, out var parsed)
            || !ValidDrillKinds.Contains(raw!.Trim()))
        {
            throw ApiException.Validation("SPEAKING_DRILL_KIND_INVALID",
                $"DrillKind must be one of: {string.Join(", ", ValidDrillKinds)}.");
        }
        return parsed;
    }

    private static void ValidateDrillRequest(string drillKind, string title, string instructionText, string[]? targetCriteria)
    {
        if (string.IsNullOrWhiteSpace(drillKind))
        {
            throw ApiException.Validation("SPEAKING_DRILL_KIND_REQUIRED", "DrillKind is required.");
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            throw ApiException.Validation("SPEAKING_DRILL_TITLE_REQUIRED", "Title is required.");
        }
        if (string.IsNullOrWhiteSpace(instructionText))
        {
            throw ApiException.Validation("SPEAKING_DRILL_INSTRUCTION_REQUIRED", "InstructionText is required.");
        }
        if (targetCriteria is null || targetCriteria.Length == 0)
        {
            throw ApiException.Validation("SPEAKING_DRILL_CRITERIA_REQUIRED",
                "At least one TargetCriteria entry is required.");
        }
    }

    private static string[] ParseTargetCriteria(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list.ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string ParseInstructionText(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
            if (doc.RootElement.TryGetProperty("instructionText", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static object ProjectSummaryRow(SpeakingDrillItem drill, ContentItem content)
        => new
        {
            drillId = drill.Id,
            contentItemId = content.Id,
            drillKind = drill.DrillKind.ToString(),
            professionId = content.ProfessionId,
            title = content.Title,
            instructionText = ParseInstructionText(content.DetailJson),
            targetCriteria = ParseTargetCriteria(drill.TargetCriteriaJson),
            recommendedAfterSessionScoreBelow = drill.RecommendedAfterSessionScoreBelow,
            status = content.Status.ToString().ToLowerInvariant(),
            createdAt = drill.CreatedAt,
            updatedAt = drill.UpdatedAt,
            publishedAt = content.PublishedAt,
            archivedAt = content.ArchivedAt,
        };

    private static object ProjectDetail(SpeakingDrillItem drill, ContentItem content)
        => new
        {
            drillId = drill.Id,
            contentItemId = content.Id,
            drillKind = drill.DrillKind.ToString(),
            professionId = content.ProfessionId,
            title = content.Title,
            instructionText = ParseInstructionText(content.DetailJson),
            targetCriteria = ParseTargetCriteria(drill.TargetCriteriaJson),
            recommendedAfterSessionScoreBelow = drill.RecommendedAfterSessionScoreBelow,
            status = content.Status.ToString().ToLowerInvariant(),
            createdAt = drill.CreatedAt,
            updatedAt = drill.UpdatedAt,
            publishedAt = content.PublishedAt,
            archivedAt = content.ArchivedAt,
        };

    // ════════════════════════════════════════════════════════════════════════
    //  Phase 11 (G.11) — AI-assisted drill drafting
    // ════════════════════════════════════════════════════════════════════════
    //
    // Mirrors `AiDraftRolePlayCardAsync` in AdminService.SpeakingRolePlayCards.cs
    // but produces a flat micro-drill instead of a paired card+script. The
    // grounded gateway is required — the prompt embeds the canonical Speaking
    // rulebook + scoring + guardrails, so the AI cannot fabricate criteria
    // outside the rulebook. If the gateway is unparseable or refuses, we fall
    // back to a deterministic starter and surface a `Warning` so the admin
    // knows to edit before publishing.
    //
    // Feature code is `AdminContentGeneration` (platform-only — already in
    // the BYOK refusal allowlist). The method returns the persisted draft
    // projection plus the optional warning so the page can deep-link directly
    // to the drill bank list with the new row visible as a Draft.
    public async Task<AdminSpeakingDrillAiDraftResponse> AiDraftSpeakingDrillAsync(
        IAiGatewayService gateway,
        string adminId,
        string adminName,
        AdminSpeakingDrillAiDraftRequest request,
        CancellationToken ct)
    {
        if (gateway is null) throw new ArgumentNullException(nameof(gateway));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.DrillKind))
        {
            throw ApiException.Validation("SPEAKING_DRILL_KIND_REQUIRED", "DrillKind is required.");
        }
        var kind = ParseDrillKind(request.DrillKind);
        var profession = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim();
        var topic = string.IsNullOrWhiteSpace(request.Topic) ? "general clinical communication" : request.Topic.Trim();
        var criterion = string.IsNullOrWhiteSpace(request.CriterionFocus) ? "fluency" : request.CriterionFocus.Trim();
        var difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "core" : request.Difficulty.Trim().ToLowerInvariant();

        var examProfession = profession is null
            ? ExamProfession.Medicine
            : ParseProfessionToExam(profession);
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = examProfession,
            Task = AiTaskMode.GenerateContent,
            CardType = "drill",
        });

        var userInput =
            $"Produce ONE Speaking micro-drill (kind={kind}). Profession={profession ?? "general"}, " +
            $"topic={topic}, weak criterion={criterion}, difficulty={difficulty}. Reply with strict JSON: " +
            "{ \"title\": string, \"instructionText\": string, \"targetCriteria\": string[], " +
            "\"recommendedAfterSessionScoreBelow\": number|null }. " +
            "Title <= 90 chars. instructionText is 2-4 sentences instructing the candidate. " +
            "targetCriteria must use rulebook criterion ids only (1-3 entries). " +
            "recommendedAfterSessionScoreBelow is one of {300,350,400,null}.";

        AiGatewayResult? aiResult = null;
        ParsedDrillDraft? parsed = null;
        string? warning = null;
        try
        {
            aiResult = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userInput,
                Temperature = 0.4,
                MaxTokens = 1024,
                FeatureCode = AiFeatureCodes.AdminContentGeneration,
                UserId = adminId,
                PromptTemplateId = "drill.draft.v1",
            }, ct);
            parsed = TryParseDrillDraft(aiResult.Completion);
            if (parsed is null)
            {
                warning = "AI reply could not be parsed. A deterministic starter was used. Edit before publishing.";
            }
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch (Exception)
        {
            warning = "AI provider error. A deterministic starter was used. Edit before publishing.";
        }

        parsed ??= BuildFallbackDrillDraft(kind, profession, topic, criterion);

        var now = DateTimeOffset.UtcNow;
        var contentId = $"ci-drill-{Guid.NewGuid():N}";
        var drillId = $"sdi-{Guid.NewGuid():N}";

        var content = new ContentItem
        {
            Id = contentId,
            ContentType = "speaking_drill",
            SubtestCode = "speaking",
            ProfessionId = profession,
            Title = parsed.Title,
            Difficulty = difficulty,
            EstimatedDurationMinutes = 1,
            CriteriaFocusJson = JsonSupport.Serialize(parsed.TargetCriteria),
            ScenarioType = kind.ToString().ToLowerInvariant(),
            ModeSupportJson = JsonSupport.Serialize(new[] { "learning" }),
            PublishedRevisionId = $"rev-{drillId}",
            Status = ContentStatus.Draft,
            CaseNotes = null,
            DetailJson = JsonSupport.Serialize(new
            {
                instructionText = parsed.InstructionText,
                drillKind = kind.ToString(),
                targetCriteria = parsed.TargetCriteria,
            }),
            ModelAnswerJson = "{}",
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            DifficultyRating = 1500,
            SourceType = "ai_draft",
            SourceProvenance = warning is null ? "AI draft via drill.draft.v1" : $"AI draft with template fallback ({warning})",
            RightsStatus = "owned",
            QaStatus = "pending",
            FreshnessConfidence = "current",
            InstructionLanguage = "en",
            ContentLanguage = "en",
            CreatedBy = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var drill = new SpeakingDrillItem
        {
            Id = drillId,
            ContentItemId = contentId,
            DrillKind = kind,
            TargetCriteriaJson = JsonSupport.Serialize(parsed.TargetCriteria),
            RecommendedAfterSessionScoreBelow = parsed.RecommendedAfterSessionScoreBelow,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await BeginTransactionIfNeededAsync(ct);
        db.ContentItems.Add(content);
        db.SpeakingDrillItems.Add(drill);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName,
            warning is null ? "AiDrafted" : "AiDraftedWithFallback",
            "SpeakingDrill", drillId,
            $"AI-drafted speaking drill: {content.Title} ({kind})"
            + (warning is null ? "" : $" — warning: {warning}"),
            ct);
        await CommitIfOwnedAsync(tx, ct);

        return new AdminSpeakingDrillAiDraftResponse(
            DrillId: drillId,
            DrillKind: kind.ToString(),
            ProfessionId: profession,
            Title: content.Title,
            InstructionText: parsed.InstructionText,
            TargetCriteria: parsed.TargetCriteria,
            RecommendedAfterSessionScoreBelow: parsed.RecommendedAfterSessionScoreBelow,
            Status: content.Status.ToString().ToLowerInvariant(),
            CreatedAt: content.CreatedAt,
            Warning: warning);
    }

    private sealed record ParsedDrillDraft(
        string Title,
        string InstructionText,
        string[] TargetCriteria,
        int? RecommendedAfterSessionScoreBelow);

    private static ParsedDrillDraft? TryParseDrillDraft(string? completion)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var jsonText = ExtractJsonObject(completion);
        if (jsonText is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            var instr = root.TryGetProperty("instructionText", out var i) && i.ValueKind == JsonValueKind.String
                ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(instr)) return null;
            var crits = new List<string>();
            if (root.TryGetProperty("targetCriteria", out var c) && c.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in c.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) crits.Add(s.Trim());
                    }
                }
            }
            if (crits.Count == 0) return null;
            int? threshold = null;
            if (root.TryGetProperty("recommendedAfterSessionScoreBelow", out var th)
                && th.ValueKind == JsonValueKind.Number
                && th.TryGetInt32(out var thInt))
            {
                threshold = thInt;
            }
            // Truncate title defensively.
            if (title!.Length > 120) title = title.Substring(0, 120);
            return new ParsedDrillDraft(title.Trim(), instr!.Trim(), crits.Take(3).ToArray(), threshold);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        // The completion may include surrounding chatter; extract the
        // first balanced JSON object substring.
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var depth = 0;
        for (var idx = start; idx < text.Length; idx++)
        {
            var ch = text[idx];
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start, idx - start + 1);
            }
        }
        return null;
    }

    private static ParsedDrillDraft BuildFallbackDrillDraft(
        SpeakingDrillKind kind,
        string? profession,
        string topic,
        string criterion)
    {
        var prof = profession ?? "general";
        var kindLabel = kind.ToString();
        return new ParsedDrillDraft(
            Title: $"{kindLabel} drill — {prof} ({criterion})",
            InstructionText:
                $"Practice {kindLabel} for the {criterion} criterion. " +
                $"Focus on the patient-care scenarios most common for {prof}, including {topic}. " +
                $"Speak for 60–90 seconds, then review the transcript and self-mark against the rubric.",
            TargetCriteria: new[] { criterion },
            RecommendedAfterSessionScoreBelow: 350);
    }
}
