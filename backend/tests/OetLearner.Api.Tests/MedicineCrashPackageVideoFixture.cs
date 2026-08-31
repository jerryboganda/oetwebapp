using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.VideoLibrary;

namespace OetLearner.Api.Tests;

/// <summary>
/// Shared fixture for the "VIDEO ACCESS HIERARCHY &amp; ISOLATION RULES" (31 Aug 2026)
/// acceptance suite. It seeds a Medicine learner plus a Video Library whose collection
/// breadcrumbs mirror the live catalog exactly — Crash / Fast-Track folders, the two
/// whitelisted Medicine English collections, the Batch 1 / New Batch full-course folders
/// that must never leak, and other-profession folders — and runs everything through the
/// REAL resolver chain (EffectiveEntitlementResolver → VideoEntitlementService →
/// VideoLibraryLearnerService), never a stubbed context.
/// </summary>
internal static class MedicineCrashPackageVideoFixture
{
    // ── Video ids, named after the live collection each one sits in ────────────────────

    public const string ListeningArabicWorkshops = "vid-listening-ar-workshops";
    public const string ListeningEnglishSessions = "vid-listening-en-sessions";
    public const string ReadingArabicSessions = "vid-reading-ar-sessions";

    public const string WritingNewMedicineCrash = "vid-writing-new-medicine-crash";
    public const string WritingFastTrackCrash = "vid-writing-fast-track-crash";
    public const string WritingMedicineEnglishSessions = "vid-writing-medicine-english-sessions";
    public const string WritingMedicineEnglishWorkshops = "vid-writing-medicine-english-workshops";
    public const string WritingBatch1 = "vid-writing-batch-1";
    public const string WritingNewBatch = "vid-writing-new-batch";
    public const string WritingNursing = "vid-writing-nursing";

    public const string SpeakingMedicineArabic = "vid-speaking-medicine-arabic";
    public const string SpeakingEnglishSessions = "vid-speaking-english-sessions";
    public const string SpeakingEnglishWorkshops = "vid-speaking-english-workshops";
    public const string SpeakingNursing = "vid-speaking-nursing";

    public const string LearnerId = "learner-medicine";

    /// <summary>The §4 Writing whitelist: the two crash/fast-track folders plus the two
    /// explicitly whitelisted Medicine English collections.</summary>
    public static readonly string[] WhitelistedWriting =
    [
        WritingNewMedicineCrash,
        WritingFastTrackCrash,
        WritingMedicineEnglishSessions,
        WritingMedicineEnglishWorkshops,
    ];

    /// <summary>The full Medicine Speaking library (§3A/§3C) in Arabic and English.</summary>
    public static readonly string[] StandardMedicineSpeaking =
    [
        SpeakingMedicineArabic,
        SpeakingEnglishSessions,
        SpeakingEnglishWorkshops,
    ];

    /// <summary>Listening + Reading standard libraries (§3A).</summary>
    public static readonly string[] SharedListeningReading =
    [
        ListeningArabicWorkshops,
        ListeningEnglishSessions,
        ReadingArabicSessions,
    ];

    public sealed record Harness(
        LearnerDbContext Db,
        VideoLibraryLearnerService Service,
        IVideoEntitlementService Entitlements) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();

        /// <summary>Every video id the learner can actually see, via the real home projection.</summary>
        public async Task<HashSet<string>> VisibleVideoIdsAsync()
        {
            var home = await Service.GetHomeAsync(LearnerId, default);
            return home.Categories
                .SelectMany(c => c.Videos)
                .Concat(home.Uncategorized)
                .Select(v => v.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>The subtest cards the Videos dashboard would render — the module of every
        /// non-empty shelf, exactly how the frontend derives them from the collection title.</summary>
        public async Task<HashSet<string>> VisibleSubtestCardsAsync()
        {
            var home = await Service.GetHomeAsync(LearnerId, default);
            return home.Categories
                .Where(c => c.Videos.Count > 0)
                .Select(c => c.Title.Split('/')[0].Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>"X collections · Y videos", computed the way the UI computes it — after filtering.</summary>
        public async Task<(int Collections, int Videos)> CountsForAsync(string subtest)
        {
            var home = await Service.GetHomeAsync(LearnerId, default);
            var shelves = home.Categories
                .Where(c => c.Videos.Count > 0
                    && string.Equals(c.Title.Split('/')[0].Trim(), subtest, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return (shelves.Count, shelves.Sum(c => c.Videos.Count));
        }

        /// <summary>Direct/deep-link access — the same lookup the playback endpoint uses.</summary>
        public Task<LibraryVideo?> DeepLinkAsync(string videoId)
            => Service.FindVisibleVideoAsync(LearnerId, videoId, DateTimeOffset.UtcNow, default);
    }

    /// <summary>
    /// Seeds the Medicine learner on <paramref name="planCode"/> / <paramref name="productCategory"/>
    /// with the full live-shaped video catalog.
    /// </summary>
    public static async Task<Harness> CreateAsync(
        string planCode,
        string productCategory,
        string profession = "medicine")
    {
        var db = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var now = DateTimeOffset.UtcNow;

        foreach (var id in new[] { "medicine", "nursing", "pharmacy" })
        {
            db.Professions.Add(new ProfessionReference { Id = id, Code = id, Label = id });
        }

        db.Users.Add(new LearnerUser
        {
            Id = LearnerId,
            Role = ApplicationUserRoles.Learner,
            DisplayName = "Dr. Test Medicine",
            Email = "learner-medicine@test.dev",
            ActiveProfessionId = profession,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });

        AddPlan(db, planCode, productCategory, now);
        db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = LearnerId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });

        SeedCatalog(db, now);
        await db.SaveChangesAsync();

        var entitlements = new VideoEntitlementService(db, new EffectiveEntitlementResolver(db));
        var service = new VideoLibraryLearnerService(
            db, entitlements, new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()));
        return new Harness(db, service, entitlements);
    }

    /// <summary>Adds a SECOND active package to an existing harness (multi-package union).</summary>
    public static async Task AddPackageAsync(Harness harness, string planCode, string productCategory)
    {
        var now = DateTimeOffset.UtcNow;
        AddPlan(harness.Db, planCode, productCategory, now);
        harness.Db.Subscriptions.Add(new Subscription
        {
            Id = $"sub-{Guid.NewGuid():N}",
            UserId = LearnerId,
            PlanId = planCode,
            Status = SubscriptionStatus.Active,
            StartedAt = now.AddDays(-1),
            ChangedAt = now,
        });
        await harness.Db.SaveChangesAsync();
    }

    private static void AddPlan(LearnerDbContext db, string planCode, string productCategory, DateTimeOffset now)
        => db.BillingPlans.Add(new BillingPlan
        {
            Id = planCode,
            Code = planCode,
            Name = planCode,
            ProductCategory = productCategory,
            // The live catalog ships every crash/special package as profession "all" — the
            // Medicine-only restriction must come from the policy, not from this field.
            Profession = "all",
            DurationMonths = 6,
            AccessDurationDays = 180,
            DashboardModulesJson = """["Recalls","MaterialsLibrary","VideoLibrary"]""",
            IncludedSubtestsJson = "[]",
            EntitlementsJson = "{}",
            Status = BillingPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });

    // ── Live-shaped catalog ────────────────────────────────────────────────────────────

    private static void SeedCatalog(LearnerDbContext db, DateTimeOffset now)
    {
        // (video id, collection breadcrumb, subtest, language, profession targets, VisibilityScope)
        var rows = new (string Id, string Collection, string Subtest, string Lang, string Targets, string Scope)[]
        {
            (ListeningArabicWorkshops, "Listening / Arabic / Workshops", "listening", "ar", "[]", VideoVisibilityScopes.Shared),
            (ListeningEnglishSessions, "Listening / English / Sessions", "listening", "en", "[]", VideoVisibilityScopes.Shared),
            (ReadingArabicSessions, "Reading / Sessions / Arabic", "reading", "ar", "[]", VideoVisibilityScopes.Shared),

            (WritingNewMedicineCrash, "Writing / Arabic / New Medicine Crash Course / Sessions / Day 1",
                "writing", "ar", """["medicine"]""", VideoVisibilityScopes.Crash),
            (WritingFastTrackCrash, "Writing / Medicine / Arabic / Fast-Track Crash Course",
                "writing", "ar", """["medicine"]""", VideoVisibilityScopes.Crash),
            (WritingMedicineEnglishSessions, "Writing / Medicine / English / Sessions",
                "writing", "en", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (WritingMedicineEnglishWorkshops, "Writing / Medicine / English / Workshops / Sessions",
                "writing", "en", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (WritingBatch1, "Writing / Medicine / Arabic / Batch 1 / Sessions",
                "writing", "ar", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (WritingNewBatch, "Writing / Medicine / Arabic / New Batch / Sessions",
                "writing", "ar", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (WritingNursing, "Writing / Nursing / Sessions",
                "writing", "ar", """["nursing"]""", VideoVisibilityScopes.FullNursing),

            (SpeakingMedicineArabic, "Speaking / Medicine / Arabic / Sessions",
                "speaking", "ar", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (SpeakingEnglishSessions, "Speaking / English / Sessions",
                "speaking", "en", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (SpeakingEnglishWorkshops, "Speaking / English / Workshops",
                "speaking", "en", """["medicine"]""", VideoVisibilityScopes.FullMedicine),
            (SpeakingNursing, "Speaking / Nursing / Sessions",
                "speaking", "ar", """["nursing"]""", VideoVisibilityScopes.FullNursing),
        };

        var order = 0;
        foreach (var row in rows)
        {
            db.LibraryVideos.Add(new LibraryVideo
            {
                Id = row.Id,
                // Deliberately a bare lesson title: the whitelist must match on the COLLECTION
                // breadcrumb, not on a conveniently-named video.
                Title = $"Lesson {order + 1}",
                AccessTier = "premium",
                SubtestCode = row.Subtest,
                Language = row.Lang,
                ProfessionIdsJson = row.Targets,
                VisibilityScope = row.Scope,
                Status = ContentStatus.Published,
                SortOrder = order,
            });

            var categoryId = $"cat-{row.Id}";
            db.VideoCategories.Add(new VideoCategory
            {
                Id = categoryId,
                Title = row.Collection,
                Slug = categoryId,
                Status = ContentStatus.Published,
                DisplayOrder = order,
            });
            db.VideoCategoryItems.Add(new VideoCategoryItem
            {
                Id = Guid.NewGuid(),
                CategoryId = categoryId,
                VideoId = row.Id,
                SortOrder = 0,
            });
            order++;
        }
    }
}
