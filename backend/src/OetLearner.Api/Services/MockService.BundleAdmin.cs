using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

public sealed partial class MockService
{
    public async Task<object> ListBundlesAsync(string? status, string? mockType, string? subtest, CancellationToken ct)
    {
        var query = db.MockBundles.AsNoTracking()
            .Include(x => x.Sections.OrderBy(s => s.SectionOrder))
                .ThenInclude(s => s.ContentPaper)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }
        if (!string.IsNullOrWhiteSpace(mockType))
        {
            var normalizedType = NormalizeMockType(mockType);
            query = query.Where(x => x.MockType == normalizedType);
        }
        if (!string.IsNullOrWhiteSpace(subtest))
        {
            var normalizedSubtest = NormalizeSubtest(subtest);
            query = query.Where(x => x.SubtestCode == normalizedSubtest || x.Sections.Any(s => s.SubtestCode == normalizedSubtest));
        }

        var rows = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);

        return new { items = rows.Select(ProjectBundleAdmin).ToArray() };
    }

    public async Task<object> GetBundleAsync(string id, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: false, ct);
        return ProjectBundleAdmin(bundle);
    }

    public async Task<object> CreateBundleAsync(AdminMockBundleCreateRequest request, string adminId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var mockType = NormalizeMockType(request.MockType);
        var subtest = MockTypes.IsSubShape(mockType) ? NormalizeSubtest(request.SubtestCode) : null;
        var title = RequireText(request.Title, "title");
        var bundle = new MockBundle
        {
            Id = $"mock-bundle-{Guid.NewGuid():N}",
            Title = title,
            Slug = await UniqueSlugAsync(title, ct),
            MockType = mockType,
            SubtestCode = subtest,
            ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim().ToLowerInvariant(),
            AppliesToAllProfessions = request.AppliesToAllProfessions,
            Status = ContentStatus.Draft,
            SourceProvenance = request.SourceProvenance,
            Priority = request.Priority ?? 0,
            TagsCsv = request.TagsCsv ?? string.Empty,
            Difficulty = NormalizeDifficulty(request.Difficulty),
            SourceStatus = NormalizeSourceStatus(request.SourceStatus),
            QualityStatus = NormalizeQualityStatus(request.QualityStatus),
            ReleasePolicy = NormalizeReleasePolicy(request.ReleasePolicy),
            TopicTagsCsv = request.TopicTagsCsv ?? string.Empty,
            SkillTagsCsv = request.SkillTagsCsv ?? string.Empty,
            WatermarkEnabled = request.WatermarkEnabled ?? true,
            RandomiseQuestions = request.RandomiseQuestions ?? false,
            EstimatedDurationMinutes = ComputeBundleDefaultDuration(mockType, subtest),
            CreatedByAdminId = adminId,
            UpdatedByAdminId = adminId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.MockBundles.Add(bundle);
        LogAudit(adminId, "Created", "MockBundle", bundle.Id, $"Created mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return ProjectBundleAdmin(bundle);
    }

    public async Task<object> UpdateBundleAsync(string id, AdminMockBundleUpdateRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            bundle.Title = request.Title.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.MockType))
        {
            bundle.MockType = NormalizeMockType(request.MockType);
        }
        if (request.SubtestCode is not null)
        {
            bundle.SubtestCode = MockTypes.IsSubShape(bundle.MockType) ? NormalizeSubtest(request.SubtestCode) : null;
        }
        if (request.ProfessionId is not null)
        {
            bundle.ProfessionId = string.IsNullOrWhiteSpace(request.ProfessionId) ? null : request.ProfessionId.Trim().ToLowerInvariant();
        }
        if (request.AppliesToAllProfessions.HasValue)
        {
            bundle.AppliesToAllProfessions = request.AppliesToAllProfessions.Value;
        }
        if (request.SourceProvenance is not null) bundle.SourceProvenance = request.SourceProvenance;
        if (request.Priority.HasValue) bundle.Priority = request.Priority.Value;
        if (request.TagsCsv is not null) bundle.TagsCsv = request.TagsCsv;
        if (request.Difficulty is not null) bundle.Difficulty = NormalizeDifficulty(request.Difficulty);
        if (request.SourceStatus is not null) bundle.SourceStatus = NormalizeSourceStatus(request.SourceStatus);
        if (request.QualityStatus is not null) bundle.QualityStatus = NormalizeQualityStatus(request.QualityStatus);
        if (request.ReleasePolicy is not null) bundle.ReleasePolicy = NormalizeReleasePolicy(request.ReleasePolicy);
        if (request.TopicTagsCsv is not null) bundle.TopicTagsCsv = request.TopicTagsCsv;
        if (request.SkillTagsCsv is not null) bundle.SkillTagsCsv = request.SkillTagsCsv;
        if (request.WatermarkEnabled.HasValue) bundle.WatermarkEnabled = request.WatermarkEnabled.Value;
        if (request.RandomiseQuestions.HasValue) bundle.RandomiseQuestions = request.RandomiseQuestions.Value;
        if (request.Status.HasValue && request.Status.Value != ContentStatus.Published)
        {
            bundle.Status = request.Status.Value;
        }
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "Updated", "MockBundle", bundle.Id, $"Updated mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> ArchiveBundleAsync(string id, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        bundle.Status = ContentStatus.Archived;
        bundle.ArchivedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "Archived", "MockBundle", bundle.Id, $"Archived mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return new { id = bundle.Id, status = bundle.Status.ToString().ToLowerInvariant() };
    }

    /// <summary>
    /// True-purge cascade for a mock bundle (the "force-delete" bulk action).
    /// Removes the bundle, its sections, and ALL learner data tied to it — attempts,
    /// section attempts, review reservations, content reviews, proctoring events,
    /// bookings + their live-room transitions, and analytics snapshots — and nulls
    /// the dangling MockAttemptId on credit-ledger rows (accounting history is kept).
    /// Loads + RemoveRange (no ExecuteDelete) for InMemory parity; does NOT
    /// SaveChanges/audit — the bulk caller owns the single transaction, SaveChanges
    /// and the one summary audit row. The bundle must already be Archived.
    /// </summary>
    private async Task ForceDeleteBundleCascadeAsync(string id, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        if (bundle.Status != ContentStatus.Archived)
            throw ApiException.Validation(
                "mock_bundle_force_delete_not_archived",
                "Only archived mock bundles can be permanently deleted. Archive it first.");

        var attemptIds = await db.MockAttempts
            .Where(a => a.MockBundleId == id).Select(a => a.Id).ToListAsync(ct);
        var sectionIds = await db.MockBundleSections
            .Where(s => s.MockBundleId == id).Select(s => s.Id).ToListAsync(ct);
        var bookingIds = await db.MockBookings
            .Where(b => b.MockBundleId == id
                     || (b.MockAttemptId != null && attemptIds.Contains(b.MockAttemptId)))
            .Select(b => b.Id).ToListAsync(ct);

        db.MockLiveRoomTransitions.RemoveRange(
            await db.MockLiveRoomTransitions.Where(t => bookingIds.Contains(t.BookingId)).ToListAsync(ct));
        db.MockProctoringEvents.RemoveRange(
            await db.MockProctoringEvents.Where(e => attemptIds.Contains(e.MockAttemptId)).ToListAsync(ct));
        db.MockReviewReservations.RemoveRange(
            await db.MockReviewReservations.Where(r => attemptIds.Contains(r.MockAttemptId)).ToListAsync(ct));
        db.MockContentReviews.RemoveRange(
            await db.MockContentReviews.Where(r => r.MockBundleId == id
                || (r.MockAttemptId != null && attemptIds.Contains(r.MockAttemptId))).ToListAsync(ct));
        db.MockSectionAttempts.RemoveRange(
            await db.MockSectionAttempts.Where(sa => attemptIds.Contains(sa.MockAttemptId)
                || sectionIds.Contains(sa.MockBundleSectionId)).ToListAsync(ct));
        db.MockBookings.RemoveRange(
            await db.MockBookings.Where(b => bookingIds.Contains(b.Id)).ToListAsync(ct));
        db.MockItemAnalysisSnapshots.RemoveRange(
            await db.MockItemAnalysisSnapshots.Where(s => s.MockBundleId == id).ToListAsync(ct));

        // Credit ledger: a plain nullable reference, not the bundle's own data —
        // keep the accounting row, null the dangling attempt pointer.
        var ledger = await db.MockEntitlementLedgers
            .Where(l => l.MockAttemptId != null && attemptIds.Contains(l.MockAttemptId)).ToListAsync(ct);
        foreach (var l in ledger) l.MockAttemptId = null;

        db.MockAttempts.RemoveRange(
            await db.MockAttempts.Where(a => a.MockBundleId == id).ToListAsync(ct));
        db.MockBundleSections.RemoveRange(
            await db.MockBundleSections.Where(s => s.MockBundleId == id).ToListAsync(ct));
        db.MockBundles.Remove(bundle);
    }

    public async Task<object> AddSectionAsync(string id, AdminMockBundleSectionRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.ContentPaperId, ct)
            ?? throw ApiException.NotFound("content_paper_not_found", "Content paper not found.");

        var subtest = NormalizeSubtest(paper.SubtestCode);
        if (MockTypes.IsSubShape(bundle.MockType) && !string.Equals(bundle.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "mock_section_subtest_mismatch",
                "A sub-test mock can only include sections from its selected sub-test.",
                [new ApiFieldError("contentPaperId", "subtest_mismatch", "Choose a paper from the same sub-test as the bundle.")]);
        }

        var nextOrder = request.SectionOrder ?? (await db.MockBundleSections.Where(x => x.MockBundleId == id).MaxAsync(x => (int?)x.SectionOrder, ct) ?? 0) + 1;
        var section = new MockBundleSection
        {
            Id = $"mock-bundle-section-{Guid.NewGuid():N}",
            MockBundleId = bundle.Id,
            SectionOrder = nextOrder,
            SubtestCode = subtest,
            ContentPaperId = paper.Id,
            TimeLimitMinutes = request.TimeLimitMinutes ?? DefaultTimeLimit(subtest),
            ReviewEligible = request.ReviewEligible ?? ProductiveSubtests.Contains(subtest),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.MockBundleSections.Add(section);
        bundle.EstimatedDurationMinutes = await ComputeEstimatedDurationAsync(bundle.Id, section, ct);
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "AddedSection", "MockBundle", bundle.Id, $"Added {paper.Title} to {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> ReorderSectionsAsync(string id, AdminMockBundleReorderRequest request, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var sections = await db.MockBundleSections.Where(x => x.MockBundleId == bundle.Id).ToListAsync(ct);
        var requested = request.SectionIds?.ToList() ?? [];
        if (requested.Count != sections.Count || requested.Except(sections.Select(x => x.Id)).Any())
        {
            throw ApiException.Validation(
                "mock_reorder_invalid",
                "The reorder request must include every section exactly once.",
                [new ApiFieldError("sectionIds", "invalid", "Submit all section ids in the desired order.")]);
        }

        for (var index = 0; index < requested.Count; index++)
        {
            sections.First(x => x.Id == requested[index]).SectionOrder = index + 1;
        }

        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        bundle.UpdatedByAdminId = adminId;
        LogAudit(adminId, "ReorderedSections", "MockBundle", bundle.Id, $"Reordered sections for {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    public async Task<object> PublishBundleAsync(string id, string adminId, CancellationToken ct)
    {
        var bundle = await GetBundleEntityAsync(id, track: true, ct);
        var sections = await db.MockBundleSections
            .Where(x => x.MockBundleId == bundle.Id)
            .Include(x => x.ContentPaper)
            .OrderBy(x => x.SectionOrder)
            .ToListAsync(ct);

        var errors = ValidatePublishGate(bundle, sections);
        errors.AddRange(await ValidateSectionContentReadinessAsync(sections, ct));
        if (errors.Count > 0)
        {
            throw ApiException.Validation("mock_publish_gate_failed", "The mock bundle is not ready to publish.", errors);
        }

        var publishedAt = DateTimeOffset.UtcNow;
        bundle.Status = ContentStatus.Published;
        bundle.PublishedAt = publishedAt;
        bundle.UpdatedAt = publishedAt;
        bundle.UpdatedByAdminId = adminId;
        bundle.EstimatedDurationMinutes = sections.Sum(x => x.TimeLimitMinutes);

        // Keep the editorial review-stage timeline coherent for bundles that
        // publish directly (without walking academic→…→pilot): record the
        // terminal "published" transition so the stage summary never reports
        // a live bundle as having no stage at all.
        db.MockContentReviews.Add(new MockContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            MockBundleId = bundle.Id,
            ReviewType = Mocks.MockBundleReviewStageService.EditorialReviewType,
            Severity = "info",
            Status = "resolved",
            Stage = "published",
            Notes = "Published directly from the mock bundles admin (bypassed staged review).",
            CreatedAt = publishedAt,
            ResolvedAt = publishedAt,
        });

        LogAudit(adminId, "Published", "MockBundle", bundle.Id, $"Published mock bundle {bundle.Title}.");
        await db.SaveChangesAsync(ct);
        return await GetBundleAsync(bundle.Id, ct);
    }

    /// <summary>
    /// Atomic bulk action over mock bundles. <paramref name="action"/> is one
    /// of <c>publish</c>, <c>archive</c>, or <c>delete</c>. Every requested id
    /// is processed inside a SINGLE transaction and exactly ONE audit row is
    /// written for the whole operation; if any unexpected (non-gate) error is
    /// thrown mid-batch the transaction rolls back and nothing is persisted.
    ///
    /// Per-item status-gate / not-found failures (surfaced as
    /// <see cref="ApiException"/> by the per-item code paths) are caught and
    /// recorded as <c>Failed</c> with a capped error list — they do NOT abort
    /// the batch, mirroring the gold-standard vocabulary bulk endpoints.
    ///
    /// Returns an anonymous object whose shape is wire-compatible with the
    /// shared <c>BulkActionResult</c> record
    /// (<c>{ totalRequested, succeeded, skipped, failed, errors }</c>).
    /// </summary>
    public async Task<object> BulkAsync(string action, string[] ids, string adminId, CancellationToken ct)
    {
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedAction is not ("publish" or "archive" or "delete" or "force-delete"))
        {
            throw ApiException.Validation(
                "mock_bundle_bulk_action_invalid",
                "Action must be one of: publish, archive, delete, force-delete.");
        }

        // Action-specific permission gate. The route only guarantees
        // AdminContentWrite (the minimum shared by all three actions); the
        // stricter publish grant is enforced here because the required
        // permission depends on the request body, not the route. archive and
        // delete need only AdminContentWrite, already enforced by the route.
        // Mirrors the sibling AdminService.BulkSpeakingDrillsAsync gate.
        if (normalizedAction == "publish")
        {
            var perms = await GetEffectiveAdminPermissionsAsync(adminId, ct);
            if (!perms.Contains(AdminPermissions.ContentPublish)
                && !perms.Contains(AdminPermissions.SystemAdmin))
            {
                throw ApiException.Forbidden(
                    "insufficient_permission",
                    "Bulk publishing mock bundles requires the content:publish permission.");
            }
        }
        else if (normalizedAction == "force-delete")
        {
            // Most destructive action — permanently purges the bundle plus all
            // learner attempt/booking/proctoring data tied to it. system_admin only.
            var perms = await GetEffectiveAdminPermissionsAsync(adminId, ct);
            if (!perms.Contains(AdminPermissions.SystemAdmin))
            {
                throw ApiException.Forbidden(
                    "insufficient_permission",
                    "Force-deleting mock bundles requires the system_admin permission.");
            }
        }

        var requestedIds = (ids ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requestedIds.Count == 0)
        {
            throw ApiException.Validation(
                "mock_bundle_bulk_empty",
                "Select at least one mock bundle.");
        }

        if (requestedIds.Count > 2000)
        {
            throw ApiException.Validation(
                "mock_bundle_bulk_limit",
                "Bulk actions are limited to 2000 mock bundles at a time.");
        }

        const int maxErrors = 20;
        var succeeded = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            foreach (var id in requestedIds)
            {
                try
                {
                    var outcome = await ApplyBulkActionToBundleAsync(normalizedAction, id, adminId, ct);
                    if (outcome) succeeded++; else skipped++;
                }
                catch (ApiException ex)
                {
                    failed++;
                    if (errors.Count < maxErrors)
                    {
                        errors.Add($"{id}: {ex.Message}");
                    }
                }
            }

            LogAudit(
                adminId,
                $"BulkBundle.{char.ToUpperInvariant(normalizedAction[0])}{normalizedAction[1..]}",
                "MockBundle",
                "bulk",
                JsonSupport.Serialize(new
                {
                    action = normalizedAction,
                    totalRequested = requestedIds.Count,
                    succeeded,
                    skipped,
                    failed,
                }));

            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        return new
        {
            totalRequested = requestedIds.Count,
            succeeded,
            skipped,
            failed,
            errors = errors.ToArray(),
        };
    }

    /// <summary>
    /// Apply a single bulk action to one bundle WITHOUT committing — the caller
    /// owns the transaction, the single audit row, and the single
    /// <c>SaveChangesAsync</c>. Returns <c>true</c> when the bundle changed and
    /// <c>false</c> when it was already in the target state (skipped). Throws
    /// <see cref="ApiException"/> for not-found / status-gate failures so the
    /// caller can record them per-id.
    /// </summary>
    /// <summary>
    /// Resolve the effective admin permission set for <paramref name="adminId"/>.
    /// Legacy accounts without an AdminUser role record retain the old
    /// implicit system-admin behavior; a role-managed account marked
    /// unassigned remains empty after revocation.
    /// </summary>
    private async Task<HashSet<string>> GetEffectiveAdminPermissionsAsync(string adminId, CancellationToken ct)
    {
        var grants = await db.AdminPermissionGrants
            .AsNoTracking()
            .Where(g => g.AdminUserId == adminId)
            .Select(g => g.Permission)
            .ToListAsync(ct);
        var perms = new HashSet<string>(grants, StringComparer.OrdinalIgnoreCase);
        var catalogRole = await db.AdminUsers
            .AsNoTracking()
            .Where(user => user.Id == adminId)
            .Select(user => user.Role)
            .SingleOrDefaultAsync(ct);
        if (perms.Count == 0 && !string.Equals(catalogRole, "unassigned", StringComparison.OrdinalIgnoreCase))
        {
            perms.Add(AdminPermissions.SystemAdmin);
        }
        return perms;
    }

    private async Task<bool> ApplyBulkActionToBundleAsync(string action, string id, string adminId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (action == "publish")
        {
            var bundle = await GetBundleEntityAsync(id, track: true, ct);
            if (bundle.Status == ContentStatus.Published) return false;

            var sections = await db.MockBundleSections
                .Where(x => x.MockBundleId == bundle.Id)
                .Include(x => x.ContentPaper)
                .OrderBy(x => x.SectionOrder)
                .ToListAsync(ct);

            var gate = ValidatePublishGate(bundle, sections);
            gate.AddRange(await ValidateSectionContentReadinessAsync(sections, ct));
            if (gate.Count > 0)
            {
                throw ApiException.Validation("mock_publish_gate_failed", "The mock bundle is not ready to publish.", gate);
            }

            bundle.Status = ContentStatus.Published;
            bundle.PublishedAt = now;
            bundle.UpdatedAt = now;
            bundle.UpdatedByAdminId = adminId;
            bundle.EstimatedDurationMinutes = sections.Sum(x => x.TimeLimitMinutes);
            return true;
        }

        if (action == "force-delete")
        {
            // Permanent true-purge. Cascade runs without its own SaveChanges so it
            // joins the caller's single bulk transaction + summary audit row.
            await ForceDeleteBundleCascadeAsync(id, ct);
            return true;
        }

        // archive | delete — both perform a soft-archive, matching the per-item
        // DELETE endpoint (which maps to ArchiveBundleAsync). "delete" stays a
        // soft-archive; "force-delete" (above) is the permanent purge.
        var target = await GetBundleEntityAsync(id, track: true, ct);
        if (target.Status == ContentStatus.Archived) return false;

        target.Status = ContentStatus.Archived;
        target.ArchivedAt = now;
        target.UpdatedAt = now;
        target.UpdatedByAdminId = adminId;
        return true;
    }

    /// <summary>
    /// Startability pre-checks the static gate can't see: a Listening paper
    /// publishes with zero constraints (owner decision), so without this a
    /// published mock could contain a Listening section whose player shows
    /// "Audio is not available yet" forever — after the learner's credit was
    /// already spent. Same for a Reading paper with no authored questions
    /// (renders an unanswerable "0/0" exam). Returns per-paper blocking issues.
    /// </summary>
    private async Task<List<ApiFieldError>> ValidateSectionContentReadinessAsync(
        IReadOnlyList<MockBundleSection> sections, CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();

        var listeningPaperIds = sections
            .Where(s => string.Equals(s.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ContentPaperId)
            .Distinct()
            .ToList();
        if (listeningPaperIds.Count > 0)
        {
            var papersWithAudio = await db.ContentPaperAssets.AsNoTracking()
                .Where(a => listeningPaperIds.Contains(a.PaperId) && a.Role == PaperAssetRole.Audio)
                .Select(a => a.PaperId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var section in sections.Where(s =>
                string.Equals(s.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase)
                && !papersWithAudio.Contains(s.ContentPaperId)))
            {
                var title = section.ContentPaper?.Title ?? section.ContentPaperId;
                errors.Add(new ApiFieldError("sections", "listening_audio_missing",
                    $"{title} has no audio yet — learners could not start this section."));
            }
        }

        var readingPaperIds = sections
            .Where(s => string.Equals(s.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.ContentPaperId)
            .Distinct()
            .ToList();
        if (readingPaperIds.Count > 0)
        {
            var papersWithQuestions = await (
                from q in db.ReadingQuestions.AsNoTracking()
                join p in db.ReadingParts.AsNoTracking() on q.ReadingPartId equals p.Id
                where readingPaperIds.Contains(p.PaperId)
                select p.PaperId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var section in sections.Where(s =>
                string.Equals(s.SubtestCode, "reading", StringComparison.OrdinalIgnoreCase)
                && !papersWithQuestions.Contains(s.ContentPaperId)))
            {
                var title = section.ContentPaper?.Title ?? section.ContentPaperId;
                errors.Add(new ApiFieldError("sections", "reading_questions_missing",
                    $"{title} has no authored questions yet — learners would face an empty exam."));
            }
        }

        return errors;
    }

    private static List<ApiFieldError> ValidatePublishGate(MockBundle bundle, IReadOnlyList<MockBundleSection> sections)
    {
        var errors = new List<ApiFieldError>();
        if (string.IsNullOrWhiteSpace(bundle.SourceProvenance))
        {
            errors.Add(new ApiFieldError("sourceProvenance", "required", "Mock bundle provenance is required before publish."));
        }

        var requiredSequence = RequiredSubtestSequence(bundle.MockType);
        if (requiredSequence is not null)
        {
            var actual = sections.Select(x => x.SubtestCode).ToArray();
            if (!actual.SequenceEqual(requiredSequence, StringComparer.OrdinalIgnoreCase))
            {
                var label = MockTypes.Label(bundle.MockType);
                var expected = string.Join(", ", requiredSequence.Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                errors.Add(new ApiFieldError("sections", "wrong_order", $"{label} bundles must contain {expected} in that order."));
            }
        }
        else if (MockTypes.IsSubShape(bundle.MockType))
        {
            if (sections.Count != 1 || !string.Equals(sections[0].SubtestCode, bundle.SubtestCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "subtest_required", "Sub-test mock bundles require exactly one section matching the selected sub-test."));
            }
        }
        else
        {
            // Diagnostic / Remedial / other flexible-shape bundles: require at least one section.
            if (sections.Count == 0)
            {
                errors.Add(new ApiFieldError("sections", "sections_required", "This mock requires at least one section before publishing."));
            }
        }

        foreach (var section in sections)
        {
            var paper = section.ContentPaper;
            if (paper is null)
            {
                errors.Add(new ApiFieldError("sections", "paper_missing", $"Section {section.Id} does not reference a valid content paper."));
                continue;
            }

            if (paper.Status != ContentStatus.Published)
            {
                errors.Add(new ApiFieldError("sections", "paper_unpublished", $"{paper.Title} must be published before this mock can be published."));
            }
            if (!string.Equals(paper.SubtestCode, section.SubtestCode, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "paper_subtest_mismatch", $"{paper.Title} does not match the section sub-test."));
            }
            if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
            {
                errors.Add(new ApiFieldError("sections", "paper_provenance_missing", $"{paper.Title} is missing content provenance."));
            }
            if (!bundle.AppliesToAllProfessions && !paper.AppliesToAllProfessions && !string.Equals(paper.ProfessionId, bundle.ProfessionId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ApiFieldError("sections", "profession_mismatch", $"{paper.Title} does not match the bundle profession."));
            }
        }

        return errors;
    }

    private async Task<int> ComputeEstimatedDurationAsync(string bundleId, MockBundleSection newSection, CancellationToken ct)
    {
        var existing = await db.MockBundleSections.AsNoTracking()
            .Where(x => x.MockBundleId == bundleId)
            .SumAsync(x => x.TimeLimitMinutes, ct);
        return existing + newSection.TimeLimitMinutes;
    }
}
