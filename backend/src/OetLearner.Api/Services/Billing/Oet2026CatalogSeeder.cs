using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// One-shot startup seeder that mirrors the canonical OET 2026 catalogue
/// (Dr Ahmed Hesham — Recorded Courses + Separate Packages portfolios) into
/// the live <c>BillingPlans</c>, <c>BillingAddOns</c> and
/// <c>ContentPackages</c> tables.
///
/// <para>Idempotent — rows are matched by <c>Code</c> and updated in place;
/// missing rows are inserted; nothing is deleted. Safe to run every boot.</para>
///
/// <para>Disabled by default. Enable per-environment with
/// <c>Content:Oet2026Catalog:Enabled=true</c>.</para>
///
/// <para>Manifest is shipped at <c>Data/Seeds/oet-2026-catalog.json</c> as a
/// <c>None Include="..." CopyToOutputDirectory=PreserveNewest</c> content
/// file so the bytes are available at runtime in every deployment.</para>
/// </summary>
public sealed class Oet2026CatalogSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<Oet2026CatalogSeedOptions> options,
    IHostEnvironment env,
    ILogger<Oet2026CatalogSeeder> logger) : BackgroundService
{
    private const string SeederAdminId = "system:oet-2026-catalog";
    private const string SeederAdminName = "OET 2026 Catalog Seeder";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Oet2026CatalogSeeder failed.");
        }
    }

    /// <summary>Public entry point so admin tools and tests can drive the
    /// seeder without going through DI. Returns counts of rows touched.</summary>
    public async Task<SeederResult> SeedAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("Oet2026CatalogSeeder disabled (Content:Oet2026Catalog:Enabled=false).");
            return SeederResult.Empty;
        }

        var seedPath = ResolveSeedPath();
        if (!File.Exists(seedPath))
        {
            logger.LogWarning("Oet2026CatalogSeeder skipped — manifest not found at {Path}.", seedPath);
            return SeederResult.Empty;
        }

        CatalogManifest manifest;
        try
        {
            await using var stream = File.OpenRead(seedPath);
            manifest = await JsonSerializer.DeserializeAsync<CatalogManifest>(stream, JsonOptions, ct)
                       ?? throw new InvalidOperationException("Catalog manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Oet2026CatalogSeeder failed to parse manifest at {Path}.", seedPath);
            return SeederResult.Empty;
        }

        var result = new SeederResult();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var now = DateTimeOffset.UtcNow;

        foreach (var plan in manifest.Plans ?? new List<PlanDto>())
        {
            ct.ThrowIfCancellationRequested();
            await UpsertPlanAsync(db, plan, now, result, ct);
            if (options.Value.CreateContentPackages)
            {
                await UpsertPlanPackageAsync(db, plan, now, result, ct);
            }
        }

        foreach (var addon in manifest.AddOns ?? new List<AddOnDto>())
        {
            ct.ThrowIfCancellationRequested();
            await UpsertAddOnAsync(db, addon, now, result, ct);
            if (options.Value.CreateContentPackages)
            {
                await UpsertAddOnPackageAsync(db, addon, now, result, ct);
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Oet2026CatalogSeeder done — plans created={PlansCreated} updated={PlansUpdated}, addons created={AddOnsCreated} updated={AddOnsUpdated}, packages created={PackagesCreated} updated={PackagesUpdated}.",
            result.PlansCreated, result.PlansUpdated, result.AddOnsCreated, result.AddOnsUpdated, result.PackagesCreated, result.PackagesUpdated);

        return result;
    }

    private string ResolveSeedPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.SeedFilePath))
        {
            return Path.IsPathRooted(options.Value.SeedFilePath)
                ? options.Value.SeedFilePath
                : Path.Combine(env.ContentRootPath, options.Value.SeedFilePath);
        }
        return Path.Combine(env.ContentRootPath, "Data", "Seeds", "oet-2026-catalog.json");
    }

    private static async Task UpsertPlanAsync(
        LearnerDbContext db, PlanDto dto, DateTimeOffset now, SeederResult result, CancellationToken ct)
    {
        var existing = await db.BillingPlans.FirstOrDefaultAsync(p => p.Code == dto.Code, ct);
        BillingPlan plan;
        bool isNew = existing is null;

        if (isNew)
        {
            plan = new BillingPlan
            {
                Id = $"plan_{dto.Code}",
                Code = dto.Code,
                CreatedAt = now,
            };
            db.BillingPlans.Add(plan);
        }
        else
        {
            plan = existing!;
        }

        plan.Name = dto.Name;
        plan.Description = dto.Description ?? string.Empty;
        plan.Price = dto.Price;
        plan.Currency = "GBP";
        plan.Interval = "one_time";
        plan.DurationMonths = Math.Max(1, dto.AccessDays / 30);
        plan.IsVisible = dto.IsVisible;
        plan.IsRenewable = false;
        plan.TrialDays = 0;
        plan.DisplayOrder = dto.DisplayOrder;
        plan.IncludedCredits = dto.Bundled?.AiCredits ?? 0;
        plan.Status = BillingPlanStatus.Active;
        plan.UpdatedAt = now;

        // OET 2026 fields
        plan.OriginalPriceGbp = dto.OriginalPrice;
        plan.AccessDurationDays = dto.AccessDays;
        plan.WritingAddonsEnabled = dto.WritingAddons;
        plan.SpeakingAddonsEnabled = dto.SpeakingAddons;
        plan.TutorBookDiscountEnabled = dto.TutorBookDiscount;
        plan.Profession = dto.Profession ?? "all";
        plan.ProductCategory = dto.ProductCategory ?? string.Empty;
        plan.DashboardModulesJson = JsonSerializer.Serialize(dto.DashboardModules ?? new List<string>());
        plan.BundledWritingAssessments = dto.Bundled?.WritingAssessments ?? 0;
        plan.BundledSpeakingSessions = dto.Bundled?.SpeakingSessions ?? 0;
        plan.BundledAiCredits = dto.Bundled?.AiCredits ?? 0;
        plan.BundledTutorBook = dto.Bundled?.TutorBook ?? false;
        plan.BundledBasicEnglish = dto.Bundled?.BasicEnglish ?? false;
        plan.IsDraft = dto.IsDraft;
        plan.ExtensionAllowed = dto.ExtensionAllowed;
        plan.RecallUpdatesEnabled = dto.RecallUpdatesEnabled;

        // Ensure an Active version exists with the same snapshot.
        var activeVersion = await db.BillingPlanVersions
            .FirstOrDefaultAsync(v => v.PlanId == plan.Id && v.Status == BillingPlanStatus.Active, ct);

        if (activeVersion is null)
        {
            activeVersion = new BillingPlanVersion
            {
                Id = $"planv_{dto.Code}_v1",
                PlanId = plan.Id,
                VersionNumber = 1,
                CreatedAt = now,
                CreatedByAdminId = SeederAdminId,
                CreatedByAdminName = SeederAdminName,
            };
            db.BillingPlanVersions.Add(activeVersion);
            plan.ActiveVersionId = activeVersion.Id;
            plan.LatestVersionId = activeVersion.Id;
        }

        CopyPlanIntoVersion(plan, activeVersion);

        if (isNew) result.PlansCreated++;
        else result.PlansUpdated++;
    }

    private static void CopyPlanIntoVersion(BillingPlan src, BillingPlanVersion dst)
    {
        dst.Code = src.Code;
        dst.Name = src.Name;
        dst.Description = src.Description;
        dst.Price = src.Price;
        dst.Currency = src.Currency;
        dst.Interval = src.Interval;
        dst.DurationMonths = src.DurationMonths;
        dst.IsVisible = src.IsVisible;
        dst.IsRenewable = src.IsRenewable;
        dst.TrialDays = src.TrialDays;
        dst.DisplayOrder = src.DisplayOrder;
        dst.IncludedCredits = src.IncludedCredits;
        dst.IncludedSubtestsJson = src.IncludedSubtestsJson;
        dst.EntitlementsJson = src.EntitlementsJson;
        dst.Status = src.Status;
        dst.OriginalPriceGbp = src.OriginalPriceGbp;
        dst.AccessDurationDays = src.AccessDurationDays;
        dst.WritingAddonsEnabled = src.WritingAddonsEnabled;
        dst.SpeakingAddonsEnabled = src.SpeakingAddonsEnabled;
        dst.TutorBookDiscountEnabled = src.TutorBookDiscountEnabled;
        dst.Profession = src.Profession;
        dst.ProductCategory = src.ProductCategory;
        dst.DashboardModulesJson = src.DashboardModulesJson;
        dst.BundledWritingAssessments = src.BundledWritingAssessments;
        dst.BundledSpeakingSessions = src.BundledSpeakingSessions;
        dst.BundledAiCredits = src.BundledAiCredits;
        dst.BundledTutorBook = src.BundledTutorBook;
        dst.BundledBasicEnglish = src.BundledBasicEnglish;
        dst.IsDraft = src.IsDraft;
        dst.ExtensionAllowed = src.ExtensionAllowed;
        dst.RecallUpdatesEnabled = src.RecallUpdatesEnabled;
    }

    private static async Task UpsertAddOnAsync(
        LearnerDbContext db, AddOnDto dto, DateTimeOffset now, SeederResult result, CancellationToken ct)
    {
        var existing = await db.BillingAddOns.FirstOrDefaultAsync(a => a.Code == dto.Code, ct);
        BillingAddOn addon;
        bool isNew = existing is null;

        if (isNew)
        {
            addon = new BillingAddOn
            {
                Id = $"addon_{dto.Code}",
                Code = dto.Code,
                CreatedAt = now,
            };
            db.BillingAddOns.Add(addon);
        }
        else
        {
            addon = existing!;
        }

        addon.Name = dto.Name;
        addon.Description = dto.Description ?? string.Empty;
        addon.Price = dto.Price;
        addon.Currency = "GBP";
        addon.Interval = "one_time";
        addon.Status = BillingAddOnStatus.Active;
        addon.IsRecurring = false;
        addon.DurationDays = 180;
        addon.GrantCredits = dto.GrantCredits;
        addon.GrantEntitlementsJson = BuildGrantEntitlementsJson(dto);
        addon.AppliesToAllPlans = false;
        addon.IsStackable = dto.IsStackable;
        addon.QuantityStep = 1;
        addon.MaxQuantity = dto.IsStackable ? (int?)null : 1;
        addon.DisplayOrder = dto.DisplayOrder;
        addon.UpdatedAt = now;

        // OET 2026 fields
        addon.OriginalPriceGbp = dto.OriginalPrice;
        addon.AddonKind = dto.AddonKind ?? string.Empty;
        addon.RequiresEligibleParent = dto.RequiresEligibleParent;
        addon.EligibilityFlag = dto.EligibilityFlag ?? string.Empty;
        addon.LettersGranted = dto.LettersGranted;
        addon.SessionsGranted = dto.SessionsGranted;

        var activeVersion = await db.BillingAddOnVersions
            .FirstOrDefaultAsync(v => v.AddOnId == addon.Id && v.Status == BillingAddOnStatus.Active, ct);

        if (activeVersion is null)
        {
            activeVersion = new BillingAddOnVersion
            {
                Id = $"addonv_{dto.Code}_v1",
                AddOnId = addon.Id,
                VersionNumber = 1,
                CreatedAt = now,
                CreatedByAdminId = SeederAdminId,
                CreatedByAdminName = SeederAdminName,
            };
            db.BillingAddOnVersions.Add(activeVersion);
            addon.ActiveVersionId = activeVersion.Id;
            addon.LatestVersionId = activeVersion.Id;
        }

        CopyAddOnIntoVersion(addon, activeVersion);

        if (isNew) result.AddOnsCreated++;
        else result.AddOnsUpdated++;
    }

    private static string BuildGrantEntitlementsJson(AddOnDto dto)
    {
        var grants = new Dictionary<string, object>();
        if (dto.LettersGranted > 0) grants["writing_assessments"] = dto.LettersGranted;
        if (dto.SessionsGranted > 0) grants["speaking_sessions"] = dto.SessionsGranted;
        if (dto.AddonKind == "tutor_book") grants["tutor_book"] = true;
        return JsonSerializer.Serialize(grants);
    }

    private static void CopyAddOnIntoVersion(BillingAddOn src, BillingAddOnVersion dst)
    {
        dst.Code = src.Code;
        dst.Name = src.Name;
        dst.Description = src.Description;
        dst.Price = src.Price;
        dst.Currency = src.Currency;
        dst.Interval = src.Interval;
        dst.Status = src.Status;
        dst.IsRecurring = src.IsRecurring;
        dst.DurationDays = src.DurationDays;
        dst.GrantCredits = src.GrantCredits;
        dst.GrantEntitlementsJson = src.GrantEntitlementsJson;
        dst.CompatiblePlanCodesJson = src.CompatiblePlanCodesJson;
        dst.AppliesToAllPlans = src.AppliesToAllPlans;
        dst.IsStackable = src.IsStackable;
        dst.QuantityStep = src.QuantityStep;
        dst.MaxQuantity = src.MaxQuantity;
        dst.DisplayOrder = src.DisplayOrder;
        dst.OriginalPriceGbp = src.OriginalPriceGbp;
        dst.AddonKind = src.AddonKind;
        dst.RequiresEligibleParent = src.RequiresEligibleParent;
        dst.EligibilityFlag = src.EligibilityFlag;
        dst.LettersGranted = src.LettersGranted;
        dst.SessionsGranted = src.SessionsGranted;
    }

    private static async Task UpsertPlanPackageAsync(
        LearnerDbContext db, PlanDto dto, DateTimeOffset now, SeederResult result, CancellationToken ct)
    {
        var existing = await db.ContentPackages.FirstOrDefaultAsync(p => p.Code == dto.Code, ct);
        ContentPackage pkg;
        bool isNew = existing is null;

        if (isNew)
        {
            pkg = new ContentPackage
            {
                Id = $"pkg_{dto.Code}",
                Code = dto.Code,
                CreatedAt = now,
            };
            db.ContentPackages.Add(pkg);
        }
        else
        {
            pkg = existing!;
        }

        pkg.Title = dto.Name;
        pkg.Description = dto.Description;
        pkg.PackageType = MapProductCategoryToPackageType(dto.ProductCategory);
        pkg.ProfessionId = dto.Profession;
        pkg.InstructionLanguage = "en";
        pkg.BillingPlanId = $"plan_{dto.Code}";
        pkg.BillingAddOnId = null;
        pkg.Status = dto.IsDraft ? ContentStatus.Draft : ContentStatus.Published;
        pkg.ComparisonFeaturesJson = JsonSerializer.Serialize(dto.ComparisonFeatures ?? new List<string>());
        pkg.DisplayOrder = dto.DisplayOrder;
        pkg.ExamFamilyCode = "oet";
        pkg.ExamTypeCode = "oet";
        pkg.UpdatedAt = now;
        if (isNew && !dto.IsDraft) pkg.PublishedAt = now;

        if (isNew) result.PackagesCreated++;
        else result.PackagesUpdated++;
    }

    private static async Task UpsertAddOnPackageAsync(
        LearnerDbContext db, AddOnDto dto, DateTimeOffset now, SeederResult result, CancellationToken ct)
    {
        var existing = await db.ContentPackages.FirstOrDefaultAsync(p => p.Code == dto.Code, ct);
        ContentPackage pkg;
        bool isNew = existing is null;

        if (isNew)
        {
            pkg = new ContentPackage
            {
                Id = $"pkg_{dto.Code}",
                Code = dto.Code,
                CreatedAt = now,
            };
            db.ContentPackages.Add(pkg);
        }
        else
        {
            pkg = existing!;
        }

        pkg.Title = dto.Name;
        pkg.Description = dto.Description;
        pkg.PackageType = "standalone";
        pkg.ProfessionId = "all";
        pkg.InstructionLanguage = "en";
        pkg.BillingPlanId = null;
        pkg.BillingAddOnId = $"addon_{dto.Code}";
        pkg.Status = ContentStatus.Published;
        pkg.ComparisonFeaturesJson = JsonSerializer.Serialize(new[] { dto.Description ?? string.Empty });
        pkg.DisplayOrder = dto.DisplayOrder;
        pkg.ExamFamilyCode = "oet";
        pkg.ExamTypeCode = "oet";
        pkg.UpdatedAt = now;
        if (isNew) pkg.PublishedAt = now;

        if (isNew) result.PackagesCreated++;
        else result.PackagesUpdated++;
    }

    private static string MapProductCategoryToPackageType(string? productCategory) => productCategory switch
    {
        "full_course" or "full_course_bundle" => "full_course",
        "crash_course" or "crash_course_bundle" => "crash_course",
        "writing_crash" or "writing_crash_bundle" or "speaking_crash" or "speaking_session" or "writing_addon" or "book" or "book_addon" => "standalone",
        "combo_double" or "combo_mega" => "combo",
        "foundation" => "foundation",
        _ => "standalone"
    };

    // ── DTOs for the manifest ──────────────────────────────────────────────

    public sealed class CatalogManifest
    {
        public List<PlanDto>? Plans { get; set; }
        public List<AddOnDto>? AddOns { get; set; }
    }

    public sealed class PlanDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public int AccessDays { get; set; } = 180;
        public string? ProductCategory { get; set; }
        public string? Profession { get; set; }
        public bool WritingAddons { get; set; }
        public bool SpeakingAddons { get; set; }
        public bool TutorBookDiscount { get; set; }
        public BundledDto? Bundled { get; set; }
        public bool IsDraft { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool ExtensionAllowed { get; set; } = true;
        public bool RecallUpdatesEnabled { get; set; }
        public int DisplayOrder { get; set; }
        public List<string>? DashboardModules { get; set; }
        public List<string>? ComparisonFeatures { get; set; }
    }

    public sealed class BundledDto
    {
        public int WritingAssessments { get; set; }
        public int SpeakingSessions { get; set; }
        public int AiCredits { get; set; }
        public bool TutorBook { get; set; }
        public bool BasicEnglish { get; set; }
    }

    public sealed class AddOnDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? AddonKind { get; set; }
        public string? EligibilityFlag { get; set; }
        public bool RequiresEligibleParent { get; set; } = true;
        public int LettersGranted { get; set; }
        public int SessionsGranted { get; set; }
        public int GrantCredits { get; set; }
        public bool IsStackable { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    public sealed record SeederResult
    {
        public int PlansCreated { get; set; }
        public int PlansUpdated { get; set; }
        public int AddOnsCreated { get; set; }
        public int AddOnsUpdated { get; set; }
        public int PackagesCreated { get; set; }
        public int PackagesUpdated { get; set; }

        public static SeederResult Empty => new();
    }
}
