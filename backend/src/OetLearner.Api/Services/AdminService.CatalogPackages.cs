using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Services;

/// <summary>
/// Admin > Billing > Subscriptions &amp; Packages: the transactional save of the website-package
/// overlay (with the write-through mirror onto the live billing rows and the linked price edits),
/// the storefront save, and the plan lifecycle hooks that keep the overlay and the billing rows in step.
/// Everything is stored in <c>RuntimeSettings.CatalogPresentationJson</c>; there is no schema change.
/// </summary>
public partial class AdminService
{
    private const int MaxCommercialUpdates = 200;
    private const int AuditBeforeMaxBytes = 128 * 1024;
    private const int MaxOverlayFeatures = 30;
    private const int MaxOverlayFeatureLength = 300;

    private sealed class PresentationUnit
    {
        public required RuntimeSettingsRow Row { get; init; }
        public required JsonObject Root { get; init; }
        public required DateTimeOffset Now { get; init; }
        public string? ActorAuthAccountId { get; init; }
    }

    private sealed record WebsitePackagesCommit(
        JsonObject Root,
        CatalogPresentationRevisions Revisions,
        List<PackageCommercialOutcome> Commercial,
        List<string> Mirrored);

    private sealed record StorefrontCommit(JsonObject Root, CatalogPresentationRevisions Revisions);

    /// <summary>Name, description and features the package overlay owns for one plan code.</summary>
    private sealed record PackageManagedCopy(string? Name, string? Description, List<string>? Features)
    {
        public bool IsManaged => Name is not null || Description is not null || Features is not null;
    }

    private sealed class PackageRows
    {
        public Dictionary<string, BillingPlan> Plans { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, BillingAddOn> AddOns { get; } = new(StringComparer.Ordinal);

        public List<ContentPackage> Packages { get; } = new();

        public BillingPlan? FindPlan(string? code)
        {
            foreach (var candidate in CatalogPackageCodes.Candidates(code))
            {
                if (Plans.TryGetValue(candidate, out var plan)) return plan;
            }

            return null;
        }

        /// <summary>Aliased codes are plan-only: a legacy add-on row sharing the code is never the package.</summary>
        public BillingAddOn? FindAddOn(string? code)
        {
            var candidates = CatalogPackageCodes.Candidates(code);
            return candidates.Count == 1 && AddOns.TryGetValue(candidates[0], out var addOn) ? addOn : null;
        }

        public ContentPackage? PackageFor(BillingPlan plan)
        {
            var candidates = CatalogPackageCodes.Candidates(plan.Code);
            return Packages.FirstOrDefault(p => p.BillingPlanId == plan.Id)
                   ?? Packages.FirstOrDefault(p => p.BillingAddOnId == null && candidates.Contains(CatalogPackageCodes.Normalize(p.Code)));
        }

        public ContentPackage? PackageFor(BillingAddOn addOn)
        {
            var code = CatalogPackageCodes.Normalize(addOn.Code);
            return Packages.FirstOrDefault(p => p.BillingAddOnId == addOn.Id)
                   ?? Packages.FirstOrDefault(p => p.BillingPlanId == null && CatalogPackageCodes.Normalize(p.Code) == code);
        }
    }

    // ── Reads ─────────────────────────────────────────────────────────────

    public async Task<List<AdminCatalogPlanSummary>> ListCatalogPlanSummariesAsync(CancellationToken ct)
    {
        var rows = await db.BillingPlans.AsNoTracking()
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Code)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.Price,
                p.Currency,
                p.Interval,
                p.Status,
                p.IsVisible,
                p.IsDraft,
                p.ProductCategory,
                p.Profession,
                p.DisplayOrder,
                p.ActiveSubscribers,
                p.DurationMonths,
                p.AccessDurationDays,
                p.IncludedCredits,
                p.BundledWritingAssessments,
                p.BundledSpeakingSessions,
                p.BundledAiCredits,
                p.BundledTutorBook,
                p.BundledBasicEnglish,
                p.DashboardModulesJson,
                p.IncludedSubtestsJson,
            })
            .ToListAsync(ct);

        return rows.Select(p => new AdminCatalogPlanSummary(
            p.Id,
            p.Code,
            p.Name,
            string.IsNullOrWhiteSpace(p.Description) ? null : p.Description,
            p.Price,
            p.Currency,
            p.Interval,
            p.Status.ToString().ToLowerInvariant(),
            p.IsVisible,
            p.IsDraft,
            p.ProductCategory,
            p.Profession,
            p.DisplayOrder,
            p.ActiveSubscribers,
            p.DurationMonths,
            p.AccessDurationDays,
            p.IncludedCredits,
            p.BundledWritingAssessments,
            p.BundledSpeakingSessions,
            p.BundledAiCredits,
            p.BundledTutorBook,
            p.BundledBasicEnglish,
            ParseStringList(p.DashboardModulesJson),
            ParseStringList(p.IncludedSubtestsJson))).ToList();
    }

    /// <summary>The string items of a stored JSON array; an absent, malformed or non-array value gives an empty list.</summary>
    private static string[] ParseStringList(string? json)
        => JsonSupport.Deserialize<List<string?>>(json, new List<string?>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();

    public async Task<List<AdminCatalogAddOnSummary>> ListCatalogAddOnSummariesAsync(CancellationToken ct)
    {
        var rows = await db.BillingAddOns.AsNoTracking()
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Code)
            .Select(a => new
            {
                a.Id,
                a.Code,
                a.Name,
                a.Description,
                a.Price,
                a.Currency,
                a.Interval,
                a.Status,
                a.AddonKind,
                a.DurationDays,
                a.GrantCredits,
                a.AppliesToAllPlans,
            })
            .ToListAsync(ct);

        return rows.Select(a => new AdminCatalogAddOnSummary(
            a.Id,
            a.Code,
            a.Name,
            string.IsNullOrWhiteSpace(a.Description) ? null : a.Description,
            a.Price,
            a.Currency,
            a.Interval,
            a.Status.ToString().ToLowerInvariant(),
            a.AddonKind ?? string.Empty,
            a.DurationDays,
            a.GrantCredits,
            a.AppliesToAllPlans)).ToList();
    }

    private Task<string?> ReadStoredPresentationJsonAsync(CancellationToken ct)
        => db.RuntimeSettings.AsNoTracking()
            .Where(r => r.Id == "default")
            .Select(r => r.CatalogPresentationJson)
            .FirstOrDefaultAsync(ct);

    private async Task<JsonObject?> LoadWebsitePackagesByCodeAsync(CancellationToken ct)
        => CatalogPresentationDocument.WebsitePackagesByCode(
            CatalogPresentationDocument.Parse(await ReadStoredPresentationJsonAsync(ct)));

    /// <summary>
    /// Name, description and features the overlay holds for a plan code (alias-aware), or null.
    /// Values over the billing column limits are ignored rather than truncated.
    /// </summary>
    private static PackageManagedCopy? ReadManagedCopy(JsonObject? byCode, string? planCode)
    {
        if (byCode is null) return null;
        foreach (var candidate in CatalogPackageCodes.Candidates(planCode))
        {
            if (byCode[candidate] is not JsonObject entry) continue;

            var name = CatalogPresentationDocument.ReadEntryText(entry, "name");
            if (name is { Length: > 128 }) name = null;
            var description = CatalogPresentationDocument.ReadEntryText(entry, "description");
            if (description is { Length: > 1024 }) description = null;
            var features = CatalogPresentationDocument.ReadEntryStrings(entry, "features");

            if (name is not null || description is not null || features is not null)
            {
                return new PackageManagedCopy(name, description, features);
            }
        }

        return null;
    }

    private async Task<PackageManagedCopy?> GetPackageManagedCopyAsync(string planCode, CancellationToken ct)
        => ReadManagedCopy(await LoadWebsitePackagesByCodeAsync(ct), planCode);

    /// <summary>
    /// Same as <see cref="ReadManagedCopy"/> for an add-on row. Aliased codes are plan-only, so a legacy add-on
    /// row that shares one (for example <c>speaking-1session</c>) never takes the plan package's copy.
    /// </summary>
    private static PackageManagedCopy? ReadAddOnManagedCopy(JsonObject? byCode, string? addOnCode)
        => CatalogPackageCodes.Candidates(addOnCode).Count > 1 ? null : ReadManagedCopy(byCode, addOnCode);

    /// <summary>All plan and add-on codes (every status), including plan aliases, as the overlay may key them.</summary>
    private async Task<HashSet<string>> LoadKnownPackageCodesAsync(CancellationToken ct)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in await db.BillingPlans.AsNoTracking().Select(p => p.Code).ToListAsync(ct))
        {
            foreach (var candidate in CatalogPackageCodes.Candidates(code)) known.Add(candidate);
        }

        foreach (var code in await db.BillingAddOns.AsNoTracking().Select(a => a.Code).ToListAsync(ct))
        {
            var candidates = CatalogPackageCodes.Candidates(code);
            if (candidates.Count == 1) known.Add(candidates[0]);
        }

        return known;
    }

    // ── Shared transaction + document plumbing ────────────────────────────

    private static ApiException CatalogPresentationConflict() => ApiException.Conflict(
        "catalog_presentation_conflict",
        "Another admin saved these settings after you opened this page. Reload to see their version, then re-apply your changes.");

    private static ApiException ExpectedRevisionRequired() => ApiException.Validation(
        "expected_revision_required",
        "Reload the page and try again: this save did not say which version it was based on.");

    private static void ThrowIfPresentationInvalid(List<ApiFieldError> errors)
    {
        if (errors.Count == 0) return;
        var message = errors.Count == 1
            ? errors[0].Message
            : $"{errors[0].Message} ({errors.Count - 1} more problem(s) found.)";
        throw ApiException.Validation("catalog_presentation_invalid", message, errors);
    }

    /// <summary>
    /// Runs <paramref name="body"/> against the locked, freshly read presentation document inside one
    /// database transaction (row lock on Postgres), then saves, commits and invalidates the settings cache.
    /// The body does the revision check, mutates <c>unit.Root</c>, calls <see cref="CommitPresentationRoot"/>
    /// and stages every other change; nothing is saved until the body returns.
    /// When a plan create or delete already owns a transaction, that caller commits and invalidates once afterwards.
    /// </summary>
    private async Task<TResult> WithPresentationLockAsync<TResult>(
        string adminId,
        Func<PresentationUnit, Task<TResult>> body,
        CancellationToken ct)
    {
        var nested = db.Database.CurrentTransaction is not null;
        await using var tx = await BeginTransactionIfNeededAsync(ct);
        if (db.Database.CurrentTransaction is not null
            && db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Serialises concurrent presentation writers on the singleton row (Postgres only).
            var lockedRowId = "default";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"RuntimeSettings\" WHERE \"Id\" = {lockedRowId} FOR UPDATE",
                ct);
        }

        var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
        var inserted = row is null;
        if (row is null)
        {
            row = new RuntimeSettingsRow { Id = "default" };
            db.RuntimeSettings.Add(row);
        }

        var unit = new PresentationUnit
        {
            Row = row,
            Root = CatalogPresentationDocument.Parse(row.CatalogPresentationJson),
            Now = DateTimeOffset.UtcNow,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
        };

        var result = await body(unit);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (inserted)
        {
            throw ApiException.Conflict("catalog_presentation_conflict", "Another admin saved at the same moment. Reload and try again.");
        }

        await CommitIfOwnedAsync(tx, ct);
        if (!nested) runtimeSettingsProvider?.Invalidate();
        return result;
    }

    /// <summary>Writes the (canonical) root back to the settings row, enforcing the whole-document size cap.</summary>
    private static void CommitPresentationRoot(PresentationUnit unit)
    {
        var json = unit.Root.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > CatalogPresentationDocument.MaxDocumentBytes)
        {
            throw ApiException.Validation("catalog_presentation_too_large", "The catalog presentation is too large to save.");
        }

        unit.Row.CatalogPresentationJson = unit.Root.Count == 0 ? null : json;
        unit.Row.UpdatedAt = unit.Now;
    }

    private void StageAuditEvent(
        PresentationUnit unit,
        string adminId,
        string adminName,
        string action,
        string resourceType,
        string? resourceId,
        string? details)
        => db.AuditEvents.Add(new AuditEvent
        {
            Id = $"AUD-{Guid.NewGuid():N}",
            OccurredAt = unit.Now,
            ActorId = adminId,
            ActorAuthAccountId = unit.ActorAuthAccountId,
            ActorName = adminName,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });

    private static string BuildPresentationAuditDetails(
        string section,
        string revisionBefore,
        string revisionAfter,
        IEnumerable<string> changedCodes,
        IReadOnlyList<string> droppedCodes,
        string? beforeJson,
        IReadOnlyList<string>? mirroredCodes = null)
    {
        var beforeFits = beforeJson is not null && Encoding.UTF8.GetByteCount(beforeJson) <= AuditBeforeMaxBytes;
        return JsonSupport.Serialize(new
        {
            section,
            revisionBefore,
            revisionAfter,
            changedCodes = changedCodes.Take(50).ToList(),
            droppedCodes,
            mirroredCodes = mirroredCodes?.Take(50).ToList(),
            before = beforeFits ? beforeJson : null,
            beforeOmitted = beforeJson is not null && !beforeFits,
        });
    }

    /// <summary>Codes whose overlay entry differs between two <c>byCode</c> subtrees (sorted).</summary>
    private static List<string> DiffCodes(JsonObject? before, JsonObject? after)
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        if (before is not null)
        {
            foreach (var pair in before) codes.Add(pair.Key);
        }

        if (after is not null)
        {
            foreach (var pair in after) codes.Add(pair.Key);
        }

        var changed = new List<string>();
        foreach (var code in codes)
        {
            if (!string.Equals(before?[code]?.ToJsonString(), after?[code]?.ToJsonString(), StringComparison.Ordinal))
            {
                changed.Add(code);
            }
        }

        return changed;
    }

    private static void AddOverlayCodes(HashSet<string> codes, JsonObject? byCode)
    {
        if (byCode is null) return;
        foreach (var pair in byCode) codes.Add(CatalogPackageCodes.Normalize(pair.Key));
    }

    // ── Website packages save ─────────────────────────────────────────────

    public async Task<WebsitePackagesSaveResult> SaveWebsitePackagesAsync(
        string adminId,
        string adminName,
        SaveWebsitePackagesRequest request,
        Oet2026CatalogSeeder.SeedCatalogCopy? seed,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedRevision)) throw ExpectedRevisionRequired();
        var expectedRevision = request.ExpectedRevision.Trim();

        // Report a stale tab before spending time on the payload; the locked check below is authoritative.
        var stored = CatalogPresentationDocument.Parse(await ReadStoredPresentationJsonAsync(ct));
        if (!string.Equals(CatalogPresentationDocument.Revisions(stored).WebsitePackages, expectedRevision, StringComparison.Ordinal))
        {
            throw CatalogPresentationConflict();
        }

        var errors = new List<ApiFieldError>();
        var droppedCodes = new List<string>();
        if (request.WebsitePackages is null)
        {
            errors.Add(new ApiFieldError("websitePackages", "required", "websitePackages is required."));
        }

        var commercialUpdates = NormalizeCommercialUpdates(request.CommercialUpdates, errors);
        var knownCodes = await LoadKnownPackageCodesAsync(ct);
        var websitePackages = CatalogPresentationDocument.SanitizeWebsitePackages(request.WebsitePackages, knownCodes, errors, droppedCodes);
        ThrowIfPresentationInvalid(errors);

        var commit = await WithPresentationLockAsync<WebsitePackagesCommit>(adminId, async unit =>
        {
            var before = CatalogPresentationDocument.Revisions(unit.Root);
            if (!string.Equals(before.WebsitePackages, expectedRevision, StringComparison.Ordinal))
            {
                throw CatalogPresentationConflict();
            }

            var beforePackages = (unit.Root[CatalogPresentationDocument.WebsitePackagesKey] as JsonObject)?.DeepClone() as JsonObject;
            var beforeJson = beforePackages?.ToJsonString();
            var beforeByCode = beforePackages?[CatalogPresentationDocument.ByCodeKey] as JsonObject;

            CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.WebsitePackagesKey, websitePackages);
            CommitPresentationRoot(unit);
            var afterPackages = unit.Root[CatalogPresentationDocument.WebsitePackagesKey] as JsonObject;
            var afterByCode = afterPackages?[CatalogPresentationDocument.ByCodeKey] as JsonObject;

            var rowCodes = new HashSet<string>(StringComparer.Ordinal);
            AddOverlayCodes(rowCodes, beforeByCode);
            AddOverlayCodes(rowCodes, afterByCode);
            foreach (var update in commercialUpdates) rowCodes.Add(update.Code ?? string.Empty);
            var rows = await LoadPackageRowsAsync(rowCodes, ct);

            var mirrored = MirrorPackageCopy(unit, beforeByCode, afterByCode, rows, seed, adminId, adminName);

            var commercial = new List<PackageCommercialOutcome>();
            foreach (var update in commercialUpdates.OrderBy(u => u.Kind, StringComparer.Ordinal).ThenBy(u => u.Code, StringComparer.Ordinal))
            {
                if (update.Kind == "plan")
                {
                    commercial.Add(await ApplyPlanCommercialAsync(unit, adminId, adminName, rows, update, ct));
                }
                else
                {
                    commercial.Add(await ApplyAddOnCommercialAsync(unit, adminId, adminName, rows, update, ct));
                }
            }

            var after = CatalogPresentationDocument.Revisions(unit.Root);
            var changedCodes = DiffCodes(beforeByCode, afterByCode);
            if (!string.Equals(
                    beforePackages?[CatalogPresentationDocument.SectionsKey]?.ToJsonString(),
                    afterPackages?[CatalogPresentationDocument.SectionsKey]?.ToJsonString(),
                    StringComparison.Ordinal))
            {
                changedCodes.Add("sections");
            }

            StageAuditEvent(
                unit,
                adminId,
                adminName,
                "Updated",
                "CatalogPresentation",
                CatalogPresentationDocument.WebsitePackagesKey,
                BuildPresentationAuditDetails(
                    CatalogPresentationDocument.WebsitePackagesKey,
                    before.WebsitePackages,
                    after.WebsitePackages,
                    changedCodes,
                    droppedCodes,
                    beforeJson,
                    mirrored));

            return new WebsitePackagesCommit(unit.Root, after, commercial, mirrored);
        }, ct);

        var plans = await ListCatalogPlanSummariesAsync(ct);
        var addOns = await ListCatalogAddOnSummariesAsync(ct);
        return new WebsitePackagesSaveResult(
            commit.Root.Count == 0 ? null : commit.Root,
            commit.Revisions,
            droppedCodes,
            plans,
            addOns,
            commit.Commercial,
            commit.Mirrored);
    }

    private static List<PackageCommercialUpdateInput> NormalizeCommercialUpdates(
        IReadOnlyList<PackageCommercialUpdateInput>? updates,
        List<ApiFieldError> errors)
    {
        if (updates is null || updates.Count == 0) return new List<PackageCommercialUpdateInput>();
        if (updates.Count > MaxCommercialUpdates)
        {
            errors.Add(new ApiFieldError(
                "commercialUpdates",
                "too_many",
                $"At most {MaxCommercialUpdates} billing rows can be saved at once; {updates.Count} were sent."));
            return new List<PackageCommercialUpdateInput>();
        }

        var result = new Dictionary<string, PackageCommercialUpdateInput>(StringComparer.Ordinal);
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            if (update is null) continue;

            var field = $"commercialUpdates[{i}]";
            var kind = (update.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is not ("plan" or "addon"))
            {
                errors.Add(new ApiFieldError(field + ".kind", "invalid", "A billing row must be a plan or an add-on."));
                continue;
            }

            var code = CatalogPackageCodes.Normalize(update.Code);
            if (!CatalogPackageCodes.IsValidCode(code))
            {
                errors.Add(new ApiFieldError(field + ".code", "package_code_invalid", "A billing row needs a valid package code."));
                continue;
            }

            // Aliased codes address the same row, so the last entry for a row wins.
            var identity = CatalogPackageCodes.Candidates(code).OrderBy(candidate => candidate, StringComparer.Ordinal).First();
            result[kind + ":" + identity] = update with { Kind = kind, Code = code };
        }

        return result.Values.ToList();
    }

    private async Task<PackageRows> LoadPackageRowsAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var planCodes = new HashSet<string>(StringComparer.Ordinal);
        var addOnCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var candidates = CatalogPackageCodes.Candidates(code);
            foreach (var candidate in candidates) planCodes.Add(candidate);
            if (candidates.Count == 1) addOnCodes.Add(candidates[0]);
        }

        var rows = new PackageRows();
        if (planCodes.Count == 0) return rows;

        var planList = planCodes.ToList();
        var addOnList = addOnCodes.ToList();
        foreach (var plan in await db.BillingPlans.Where(p => planList.Contains(p.Code.ToLower())).ToListAsync(ct))
        {
            rows.Plans[plan.Code.ToLowerInvariant()] = plan;
        }

        foreach (var addOn in await db.BillingAddOns.Where(a => addOnList.Contains(a.Code.ToLower())).ToListAsync(ct))
        {
            rows.AddOns[addOn.Code.ToLowerInvariant()] = addOn;
        }

        var planIds = rows.Plans.Values.Select(p => p.Id).ToList();
        var addOnIds = rows.AddOns.Values.Select(a => a.Id).ToList();
        var packageCodes = planList.Union(addOnList).ToList();
        rows.Packages.AddRange(await db.ContentPackages
            .Where(p => (p.BillingPlanId != null && planIds.Contains(p.BillingPlanId))
                        || (p.BillingAddOnId != null && addOnIds.Contains(p.BillingAddOnId))
                        || packageCodes.Contains(p.Code.ToLower()))
            .ToListAsync(ct));
        return rows;
    }

    // ── Write-through mirror ──────────────────────────────────────────────

    /// <summary>
    /// Copies overlay name/description (and features onto the linked package) to the live rows without
    /// minting a catalog version, and restores the shipped default for a code whose overlay lost them.
    /// </summary>
    private List<string> MirrorPackageCopy(
        PresentationUnit unit,
        JsonObject? beforeByCode,
        JsonObject? afterByCode,
        PackageRows rows,
        Oet2026CatalogSeeder.SeedCatalogCopy? seed,
        string adminId,
        string adminName)
    {
        var mirrored = new List<string>();

        if (afterByCode is not null)
        {
            foreach (var pair in afterByCode)
            {
                if (pair.Value is not JsonObject entry) continue;

                var name = CatalogPresentationDocument.ReadEntryText(entry, "name");
                var description = CatalogPresentationDocument.ReadEntryText(entry, "description");
                var features = CatalogPresentationDocument.ReadEntryStrings(entry, "features");
                if (name is null && description is null && features is null) continue;

                if (ApplyPackageCopy(unit, pair.Key, name, description, features, rows, adminId, adminName, "synced from Subscriptions & Packages"))
                {
                    mirrored.Add(pair.Key);
                }
            }
        }

        if (beforeByCode is not null && seed is not null)
        {
            foreach (var pair in beforeByCode)
            {
                if (pair.Value is not JsonObject previous) continue;

                var previousName = CatalogPresentationDocument.ReadEntryText(previous, "name");
                var previousDescription = CatalogPresentationDocument.ReadEntryText(previous, "description");
                var previousFeatures = CatalogPresentationDocument.ReadEntryStrings(previous, "features");
                if (previousName is null && previousDescription is null && previousFeatures is null) continue;

                var current = afterByCode?[CatalogPackageCodes.Normalize(pair.Key)] as JsonObject;
                var revertName = previousName is not null && CatalogPresentationDocument.ReadEntryText(current, "name") is null;
                var revertDescription = previousDescription is not null && CatalogPresentationDocument.ReadEntryText(current, "description") is null;
                var revertFeatures = previousFeatures is not null && CatalogPresentationDocument.ReadEntryStrings(current, "features") is null;
                if (!revertName && !revertDescription && !revertFeatures) continue;

                // Codes the shipped manifest does not know (custom plans) keep the value already in the database.
                Oet2026CatalogSeeder.SeedCopy? copy = null;
                if (rows.FindPlan(pair.Key) is not null) copy = FindSeedCopy(seed.Plans, pair.Key);
                else if (rows.FindAddOn(pair.Key) is not null) copy = FindSeedCopy(seed.AddOns, pair.Key);
                if (copy is null) continue;

                if (ApplyPackageCopy(
                        unit,
                        pair.Key,
                        revertName ? copy.Name : null,
                        revertDescription ? (copy.Description ?? string.Empty) : null,
                        revertFeatures ? copy.Features.ToList() : null,
                        rows,
                        adminId,
                        adminName,
                        "restored to the shipped default"))
                {
                    mirrored.Add(pair.Key);
                }
            }
        }

        return mirrored.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
    }

    private static Oet2026CatalogSeeder.SeedCopy? FindSeedCopy(
        IReadOnlyDictionary<string, Oet2026CatalogSeeder.SeedCopy> map,
        string code)
    {
        foreach (var candidate in CatalogPackageCodes.Candidates(code))
        {
            if (map.TryGetValue(candidate, out var copy)) return copy;
        }

        return null;
    }

    /// <summary>A null argument leaves that field alone. Returns true when any live row changed.</summary>
    private bool ApplyPackageCopy(
        PresentationUnit unit,
        string code,
        string? name,
        string? description,
        List<string>? features,
        PackageRows rows,
        string adminId,
        string adminName,
        string reason)
    {
        var plan = rows.FindPlan(code);
        if (plan is not null)
        {
            var changed = false;
            if (name is not null && !string.Equals(plan.Name, name, StringComparison.Ordinal))
            {
                plan.Name = name;
                changed = true;
            }

            if (description is not null && !string.Equals(plan.Description, description, StringComparison.Ordinal))
            {
                plan.Description = description;
                changed = true;
            }

            if (changed)
            {
                plan.UpdatedAt = unit.Now;
                StageAuditEvent(
                    unit, adminId, adminName, "Updated", "BillingPlan", plan.Id,
                    $"Name/Description {reason} for plan {plan.Code} (no new catalog version).");
            }

            return ApplyPackageRowCopy(rows.PackageFor(plan), name, description, features, unit.Now) || changed;
        }

        var addOn = rows.FindAddOn(code);
        if (addOn is null) return false;

        var addOnChanged = false;
        if (name is not null && !string.Equals(addOn.Name, name, StringComparison.Ordinal))
        {
            addOn.Name = name;
            addOnChanged = true;
        }

        if (description is not null && !string.Equals(addOn.Description, description, StringComparison.Ordinal))
        {
            addOn.Description = description;
            addOnChanged = true;
        }

        if (addOnChanged)
        {
            addOn.UpdatedAt = unit.Now;
            StageAuditEvent(
                unit, adminId, adminName, "Updated", "BillingAddOn", addOn.Id,
                $"Name/Description {reason} for add-on {addOn.Code} (no new catalog version).");
        }

        return ApplyPackageRowCopy(rows.PackageFor(addOn), name, description, features, unit.Now) || addOnChanged;
    }

    private static bool ApplyPackageRowCopy(
        ContentPackage? package,
        string? name,
        string? description,
        List<string>? features,
        DateTimeOffset now)
    {
        if (package is null) return false;

        var changed = false;
        if (name is not null && !string.Equals(package.Title, name, StringComparison.Ordinal))
        {
            package.Title = name;
            changed = true;
        }

        if (description is not null)
        {
            var value = description.Length == 0 ? null : description;
            if (!string.Equals(package.Description, value, StringComparison.Ordinal))
            {
                package.Description = value;
                changed = true;
            }
        }

        if (features is not null)
        {
            var current = JsonSupport.Deserialize<List<string>>(package.ComparisonFeaturesJson, new List<string>());
            if (!current.SequenceEqual(features))
            {
                package.ComparisonFeaturesJson = JsonSupport.Serialize(features);
                changed = true;
            }
        }

        if (changed) package.UpdatedAt = now;
        return changed;
    }

    // ── Linked billing rows (price, currency, interval, status) ───────────

    private async Task<PackageCommercialOutcome> ApplyPlanCommercialAsync(
        PresentationUnit unit,
        string adminId,
        string adminName,
        PackageRows rows,
        PackageCommercialUpdateInput update,
        CancellationToken ct)
    {
        var requestedCode = update.Code ?? string.Empty;
        var plan = rows.FindPlan(requestedCode)
            ?? throw ApiException.NotFound("billing_plan_not_found", $"Billing plan '{requestedCode}' was not found.");

        var errors = new List<ApiFieldError>();
        var price = update.Price ?? plan.Price;
        if (price < 0) AddCatalogError(errors, "price", "negative", "Price cannot be negative.");
        var currency = string.IsNullOrWhiteSpace(update.Currency) ? plan.Currency : ValidateCatalogCurrency(errors, update.Currency);
        var interval = string.IsNullOrWhiteSpace(update.Interval) ? plan.Interval : ValidateCatalogInterval(errors, update.Interval, PlanIntervals);
        var status = ValidateCatalogEnum(errors, "status", update.Status, plan.Status);
        var isVisible = update.IsVisible ?? plan.IsVisible;
        var isDraft = update.IsDraft ?? plan.IsDraft;
        if (errors.Count > 0)
        {
            throw ApiException.Validation(
                "billing_plan_invalid",
                "Billing plan catalog data is invalid.",
                errors.Select(error => error with { Field = $"commercialUpdates.{requestedCode}.{error.Field}" }).ToList());
        }

        var changes = new List<string>();
        if (price != plan.Price) changes.Add($"price {plan.Price} -> {price}");
        if (!string.Equals(currency, plan.Currency, StringComparison.Ordinal)) changes.Add($"currency {plan.Currency} -> {currency}");
        if (!string.Equals(interval, plan.Interval, StringComparison.Ordinal)) changes.Add($"interval {plan.Interval} -> {interval}");
        if (status != plan.Status) changes.Add($"status {plan.Status} -> {status}");
        if (isVisible != plan.IsVisible) changes.Add($"visible {plan.IsVisible} -> {isVisible}");
        if (isDraft != plan.IsDraft) changes.Add($"draft {plan.IsDraft} -> {isDraft}");
        if (changes.Count == 0) return new PackageCommercialOutcome("plan", requestedCode, false);

        var latestVersionNumber = await EnsureBillingPlanVersionBaselineAsync(plan, adminId, adminName, unit.Now, ct);
        var previousStatus = plan.Status;
        plan.Price = price;
        plan.Currency = currency;
        plan.Interval = interval;
        plan.Status = status;
        plan.IsVisible = isVisible;
        plan.IsDraft = isDraft;
        if (status == BillingPlanStatus.Archived && previousStatus != BillingPlanStatus.Archived) plan.ArchivedAt = unit.Now;
        plan.UpdatedAt = unit.Now;

        var version = CreateBillingPlanVersion(plan, latestVersionNumber + 1, adminId, adminName, unit.Now);
        plan.ActiveVersionId = version.Id;
        plan.LatestVersionId = version.Id;
        db.BillingPlanVersions.Add(version);

        var changeSummary = string.Join("; ", changes);
        StageAuditEvent(
            unit, adminId, adminName, "Updated", "BillingPlan", plan.Id,
            $"Updated plan {plan.Code} from Subscriptions & Packages: {changeSummary}.");
        return new PackageCommercialOutcome("plan", requestedCode, true);
    }

    private async Task<PackageCommercialOutcome> ApplyAddOnCommercialAsync(
        PresentationUnit unit,
        string adminId,
        string adminName,
        PackageRows rows,
        PackageCommercialUpdateInput update,
        CancellationToken ct)
    {
        var requestedCode = update.Code ?? string.Empty;
        var addOn = rows.FindAddOn(requestedCode)
            ?? throw ApiException.NotFound("billing_addon_not_found", $"Billing add-on '{requestedCode}' was not found.");

        var errors = new List<ApiFieldError>();
        var price = update.Price ?? addOn.Price;
        if (price < 0) AddCatalogError(errors, "price", "negative", "Price cannot be negative.");
        var currency = string.IsNullOrWhiteSpace(update.Currency) ? addOn.Currency : ValidateCatalogCurrency(errors, update.Currency);
        var interval = string.IsNullOrWhiteSpace(update.Interval) ? addOn.Interval : ValidateCatalogInterval(errors, update.Interval, AddOnIntervals);
        var status = ValidateCatalogEnum(errors, "status", update.Status, addOn.Status);
        if (errors.Count > 0)
        {
            throw ApiException.Validation(
                "billing_addon_invalid",
                "Billing add-on catalog data is invalid.",
                errors.Select(error => error with { Field = $"commercialUpdates.{requestedCode}.{error.Field}" }).ToList());
        }

        var changes = new List<string>();
        if (price != addOn.Price) changes.Add($"price {addOn.Price} -> {price}");
        if (!string.Equals(currency, addOn.Currency, StringComparison.Ordinal)) changes.Add($"currency {addOn.Currency} -> {currency}");
        if (!string.Equals(interval, addOn.Interval, StringComparison.Ordinal)) changes.Add($"interval {addOn.Interval} -> {interval}");
        if (status != addOn.Status) changes.Add($"status {addOn.Status} -> {status}");
        if (changes.Count == 0) return new PackageCommercialOutcome("addon", requestedCode, false);

        var latestVersionNumber = await EnsureBillingAddOnVersionBaselineAsync(addOn, adminId, adminName, unit.Now, ct);
        addOn.Price = price;
        addOn.Currency = currency;
        addOn.Interval = interval;
        addOn.Status = status;
        addOn.UpdatedAt = unit.Now;

        var version = CreateBillingAddOnVersion(addOn, latestVersionNumber + 1, adminId, adminName, unit.Now);
        addOn.ActiveVersionId = version.Id;
        addOn.LatestVersionId = version.Id;
        db.BillingAddOnVersions.Add(version);

        var changeSummary = string.Join("; ", changes);
        StageAuditEvent(
            unit, adminId, adminName, "Updated", "BillingAddOn", addOn.Id,
            $"Updated add-on {addOn.Code} from Subscriptions & Packages: {changeSummary}.");
        return new PackageCommercialOutcome("addon", requestedCode, true);
    }

    // ── Storefront save ───────────────────────────────────────────────────

    public async Task<StorefrontSaveResult> SaveStorefrontAsync(
        string adminId,
        string adminName,
        SaveStorefrontRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedRevision)) throw ExpectedRevisionRequired();
        var expectedRevision = request.ExpectedRevision.Trim();

        var stored = CatalogPresentationDocument.Parse(await ReadStoredPresentationJsonAsync(ct));
        if (!string.Equals(CatalogPresentationDocument.Revisions(stored).Storefront, expectedRevision, StringComparison.Ordinal))
        {
            throw CatalogPresentationConflict();
        }

        var errors = new List<ApiFieldError>();
        var droppedCodes = new List<string>();
        // Both sections are the full desired state; a missing one would silently clear it.
        if (request.Storefront is null) errors.Add(new ApiFieldError("storefront", "required", "storefront is required."));
        if (request.ByCode is null) errors.Add(new ApiFieldError("byCode", "required", "byCode is required."));
        var knownCodes = await LoadKnownPackageCodesAsync(ct);
        var (storefrontNode, byCodeNode) = CatalogPresentationDocument.SanitizeStorefront(
            request.Storefront, request.ByCode, knownCodes, errors, droppedCodes);
        ThrowIfPresentationInvalid(errors);

        var commit = await WithPresentationLockAsync<StorefrontCommit>(adminId, unit =>
        {
            var before = CatalogPresentationDocument.Revisions(unit.Root);
            if (!string.Equals(before.Storefront, expectedRevision, StringComparison.Ordinal))
            {
                throw CatalogPresentationConflict();
            }

            var beforeSnapshot = new JsonObject();
            if (unit.Root[CatalogPresentationDocument.StorefrontKey] is { } storefrontBefore)
            {
                beforeSnapshot[CatalogPresentationDocument.StorefrontKey] = storefrontBefore.DeepClone();
            }

            if (unit.Root[CatalogPresentationDocument.ByCodeKey] is { } byCodeBefore)
            {
                beforeSnapshot[CatalogPresentationDocument.ByCodeKey] = byCodeBefore.DeepClone();
            }

            var beforeJson = beforeSnapshot.Count == 0 ? null : beforeSnapshot.ToJsonString();
            var beforeCards = unit.Root[CatalogPresentationDocument.ByCodeKey] as JsonObject;
            var storefrontChanged = !string.Equals(
                unit.Root[CatalogPresentationDocument.StorefrontKey]?.ToJsonString(),
                storefrontNode?.ToJsonString(),
                StringComparison.Ordinal);
            var changedCodes = DiffCodes(beforeCards, byCodeNode);
            if (storefrontChanged) changedCodes.Insert(0, "storefront");

            CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.StorefrontKey, storefrontNode);
            CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.ByCodeKey, byCodeNode);
            CommitPresentationRoot(unit);

            var after = CatalogPresentationDocument.Revisions(unit.Root);
            StageAuditEvent(
                unit,
                adminId,
                adminName,
                "Updated",
                "CatalogPresentation",
                CatalogPresentationDocument.StorefrontKey,
                BuildPresentationAuditDetails(
                    CatalogPresentationDocument.StorefrontKey,
                    before.Storefront,
                    after.Storefront,
                    changedCodes,
                    droppedCodes,
                    beforeJson));
            return Task.FromResult(new StorefrontCommit(unit.Root, after));
        }, ct);

        return new StorefrontSaveResult(
            commit.Root.Count == 0 ? null : commit.Root,
            commit.Revisions,
            droppedCodes);
    }

    // ── Legacy whole-document save (obsolete) ─────────────────────────────

    /// <summary>
    /// OBSOLETE: kept for stale admin browser tabs for one release. No revision check; sections that
    /// are absent, null or an empty object are preserved. Only a null presentation clears everything.
    /// </summary>
    public async Task SaveLegacyCatalogPresentationAsync(
        string adminId,
        string adminName,
        JsonElement? presentation,
        Oet2026CatalogSeeder.SeedCatalogCopy? seed,
        CancellationToken ct)
    {
        var clearAll = true;
        JsonElement document = default;
        if (presentation is { } value && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw ApiException.Validation("catalog_presentation_invalid", "presentation must be an object or null.");
            }

            document = value;
            clearAll = false;
        }

        var errors = new List<ApiFieldError>();
        var droppedCodes = new List<string>();
        JsonObject? storefrontNode = null;
        JsonObject? byCodeNode = null;
        JsonObject? packagesNode = null;
        if (!clearAll)
        {
            var knownCodes = await LoadKnownPackageCodesAsync(ct);
            (storefrontNode, byCodeNode) = CatalogPresentationDocument.SanitizeStorefront(
                NonEmptyObject(document, CatalogPresentationDocument.StorefrontKey),
                NonEmptyObject(document, CatalogPresentationDocument.ByCodeKey),
                knownCodes,
                errors,
                droppedCodes);

            if (NonEmptyObject(document, CatalogPresentationDocument.WebsitePackagesKey) is { } packagesPart)
            {
                WebsitePackagesInput? input;
                try
                {
                    input = JsonSerializer.Deserialize<WebsitePackagesInput>(packagesPart.GetRawText(), JsonSupport.Options);
                }
                catch (JsonException)
                {
                    throw ApiException.Validation("catalog_presentation_invalid", "websitePackages has an unsupported shape.");
                }

                packagesNode = CatalogPresentationDocument.SanitizeWebsitePackages(input, knownCodes, errors, droppedCodes);
            }

            ThrowIfPresentationInvalid(errors);
        }

        await WithPresentationLockAsync<bool>(adminId, async unit =>
        {
            var before = CatalogPresentationDocument.Revisions(unit.Root);
            var beforeJson = unit.Root.Count == 0 ? null : unit.Root.ToJsonString();
            var beforeByCode = CatalogPresentationDocument.WebsitePackagesByCode(unit.Root)?.DeepClone() as JsonObject;

            if (clearAll)
            {
                unit.Root.Clear();
            }
            else
            {
                if (storefrontNode is not null) CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.StorefrontKey, storefrontNode);
                if (byCodeNode is not null) CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.ByCodeKey, byCodeNode);
                if (packagesNode is not null) CatalogPresentationDocument.SetSection(unit.Root, CatalogPresentationDocument.WebsitePackagesKey, packagesNode);
            }

            CommitPresentationRoot(unit);
            var afterByCode = CatalogPresentationDocument.WebsitePackagesByCode(unit.Root);

            var mirrored = new List<string>();
            if (clearAll || packagesNode is not null)
            {
                var rowCodes = new HashSet<string>(StringComparer.Ordinal);
                AddOverlayCodes(rowCodes, beforeByCode);
                AddOverlayCodes(rowCodes, afterByCode);
                var rows = await LoadPackageRowsAsync(rowCodes, ct);
                mirrored = MirrorPackageCopy(unit, beforeByCode, afterByCode, rows, seed, adminId, adminName);
            }

            var after = CatalogPresentationDocument.Revisions(unit.Root);
            StageAuditEvent(
                unit,
                adminId,
                adminName,
                "Updated",
                "CatalogPresentation",
                "presentation",
                BuildPresentationAuditDetails(
                    "legacy",
                    before.WebsitePackages,
                    after.WebsitePackages,
                    DiffCodes(beforeByCode, afterByCode),
                    droppedCodes,
                    beforeJson,
                    mirrored));
            return true;
        }, ct);
    }

    private static JsonElement? NonEmptyObject(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var child)
           && child.ValueKind == JsonValueKind.Object
           && child.EnumerateObject().Any()
            ? child
            : null;

    // ── Plan lifecycle hooks (Pricing / Billing Ops) ──────────────────────

    /// <summary>
    /// Creates the marketing ContentPackage linked to a new plan so its "What's included" bullets are
    /// stored instead of silently dropped. A package with no content rules grants nothing.
    /// </summary>
    private async Task EnsureLinkedContentPackageAsync(
        BillingPlan plan,
        string? comparisonFeaturesJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (await db.ContentPackages.AnyAsync(p => p.BillingPlanId == plan.Id || p.Code == plan.Code, ct)) return;

        var packageId = $"pkg_{plan.Code}";
        if (packageId.Length > 64 || await db.ContentPackages.AnyAsync(p => p.Id == packageId, ct))
        {
            packageId = GenerateDomainId("pkg");
        }

        var published = plan.Status == BillingPlanStatus.Active && plan.IsVisible && !plan.IsDraft;
        db.ContentPackages.Add(new ContentPackage
        {
            Id = packageId,
            Code = plan.Code,
            Title = plan.Name,
            Description = string.IsNullOrWhiteSpace(plan.Description) ? null : plan.Description,
            PackageType = Oet2026CatalogSeeder.MapProductCategoryToPackageType(plan.ProductCategory),
            ProfessionId = plan.Profession,
            InstructionLanguage = "en",
            BillingPlanId = plan.Id,
            Status = published ? ContentStatus.Published : ContentStatus.Draft,
            ComparisonFeaturesJson = string.IsNullOrWhiteSpace(comparisonFeaturesJson) ? "[]" : comparisonFeaturesJson.Trim(),
            DisplayOrder = plan.DisplayOrder,
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = published ? now : null,
        });
    }

    /// <summary>
    /// Adds the overlay entry for a newly created plan (name, description, default section, bullets) so it
    /// appears in the package editor and on the learner page. Never replaces an existing entry.
    /// </summary>
    private async Task EnsurePackageOverlayEntryAsync(
        string adminId,
        string adminName,
        BillingPlan plan,
        string? comparisonFeaturesJson,
        CancellationToken ct)
    {
        var code = CatalogPackageCodes.Normalize(plan.Code);
        if (!CatalogPackageCodes.IsValidCode(code)) return;

        var candidates = CatalogPackageCodes.Candidates(code);
        var existing = await LoadWebsitePackagesByCodeAsync(ct);
        if (existing is not null && candidates.Any(candidate => existing[candidate] is not null)) return;

        await WithPresentationLockAsync<bool>(adminId, unit =>
        {
            var current = CatalogPresentationDocument.WebsitePackagesByCode(unit.Root);
            if (current is not null && candidates.Any(candidate => current[candidate] is not null))
            {
                return Task.FromResult(false);
            }

            // Auto-linking must never fail plan creation: with a full entry list the plan simply has no overlay entry yet.
            if (current is not null && current.Count >= CatalogPresentationDocument.MaxOverlays)
            {
                return Task.FromResult(false);
            }

            var entry = new JsonObject();
            var name = CatalogPresentationDocument.CleanText(plan.Name, false);
            if (name.Length is > 0 and <= 128) entry["name"] = name;
            var description = CatalogPresentationDocument.CleanText(plan.Description, true);
            if (description.Length is > 0 and <= 1024) entry["description"] = description;
            entry["section"] = CatalogPackageCodes.DefaultSectionForCategory(plan.ProductCategory);

            var features = new JsonArray();
            foreach (var feature in JsonSupport.Deserialize<List<string>>(comparisonFeaturesJson, new List<string>()))
            {
                var line = CatalogPresentationDocument.CleanText(feature, false);
                if (line.Length == 0) continue;
                if (features.Count >= MaxOverlayFeatures) break;
                // Over-long bullets are cut so a later save of the package editor is never rejected for them.
                var cut = MaxOverlayFeatureLength;
                if (line.Length > cut && char.IsHighSurrogate(line[cut - 1])) cut--;
                features.Add(line.Length > MaxOverlayFeatureLength ? line[..cut] : line);
            }

            if (features.Count > 0) entry["features"] = features;

            var packages = unit.Root[CatalogPresentationDocument.WebsitePackagesKey] as JsonObject;
            if (packages is null)
            {
                packages = new JsonObject();
                unit.Root[CatalogPresentationDocument.WebsitePackagesKey] = packages;
            }

            var byCode = packages[CatalogPresentationDocument.ByCodeKey] as JsonObject;
            if (byCode is null)
            {
                byCode = new JsonObject();
                packages[CatalogPresentationDocument.ByCodeKey] = byCode;
            }

            byCode[code] = entry;
            try
            {
                CommitPresentationRoot(unit);
            }
            catch (ApiException ex) when (ex.ErrorCode == "catalog_presentation_too_large")
            {
                // Nothing reached the settings row (the throw precedes the write), so the plan is created without an entry.
                return Task.FromResult(false);
            }

            StageAuditEvent(
                unit, adminId, adminName, "Created", "CatalogPresentation",
                CatalogPresentationDocument.WebsitePackagesKey,
                $"Auto-linked package record for plan {plan.Code}");
            return Task.FromResult(true);
        }, ct);
    }

    /// <summary>
    /// Removes the overlay entry for a deleted add-on. A code that is an alias (plan-only) or that a plan
    /// still uses belongs to the plan's package and is left alone.
    /// </summary>
    private async Task RemoveAddOnPackageOverlayEntryAsync(
        string adminId,
        string adminName,
        string addOnCode,
        CancellationToken ct)
    {
        var code = CatalogPackageCodes.Normalize(addOnCode);
        if (CatalogPackageCodes.Candidates(code).Count > 1) return;
        if (await db.BillingPlans.AsNoTracking().AnyAsync(p => p.Code.ToLower() == code, ct)) return;

        await RemovePackageOverlayEntryAsync(adminId, adminName, addOnCode, ct, "add-on");
    }

    /// <summary>Removes the overlay entry (and its alias) for a deleted plan or add-on.</summary>
    private async Task RemovePackageOverlayEntryAsync(
        string adminId,
        string adminName,
        string planCode,
        CancellationToken ct,
        string kind = "plan")
    {
        var candidates = CatalogPackageCodes.Candidates(planCode);
        var existing = await LoadWebsitePackagesByCodeAsync(ct);
        if (existing is null || !candidates.Any(candidate => existing[candidate] is not null)) return;

        await WithPresentationLockAsync<bool>(adminId, unit =>
        {
            var packages = unit.Root[CatalogPresentationDocument.WebsitePackagesKey] as JsonObject;
            var byCode = packages?[CatalogPresentationDocument.ByCodeKey] as JsonObject;
            if (packages is null || byCode is null) return Task.FromResult(false);

            var removed = false;
            foreach (var candidate in candidates)
            {
                if (byCode.Remove(candidate)) removed = true;
            }

            if (!removed) return Task.FromResult(false);

            if (byCode.Count == 0) packages.Remove(CatalogPresentationDocument.ByCodeKey);
            if (packages.Count == 0) unit.Root.Remove(CatalogPresentationDocument.WebsitePackagesKey);

            CommitPresentationRoot(unit);
            StageAuditEvent(
                unit, adminId, adminName, "Updated", "CatalogPresentation",
                CatalogPresentationDocument.WebsitePackagesKey,
                $"Removed package record for deleted {kind} {planCode}");
            return Task.FromResult(true);
        }, ct);
    }
}
