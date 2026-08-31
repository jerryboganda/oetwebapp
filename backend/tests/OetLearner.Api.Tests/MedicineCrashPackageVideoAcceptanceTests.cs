using OetLearner.Api.Services.Entitlements;
using static OetLearner.Api.Tests.MedicineCrashPackageVideoFixture;

namespace OetLearner.Api.Tests;

/// <summary>
/// §6 "Developer acceptance tests — must all pass" from the 31 Aug 2026
/// "VIDEO ACCESS HIERARCHY &amp; ISOLATION RULES" specification, one test per numbered row,
/// driven end to end through the real resolver → entitlement → learner-service chain.
/// </summary>
public sealed class MedicineCrashPackageVideoAcceptanceTests
{
    // ── §6.1 Full Crash Course + 3 Letters ─────────────────────────────────────────────

    [Fact]
    public async Task Test1_FullCrashPlus3Letters_ShowsAllFourSubtestsWithFilteredWriting()
    {
        await using var harness = await CreateAsync("crash-3letters", "crash_course_bundle");

        Assert.Equal(
            new HashSet<string> { "listening", "reading", "writing", "speaking" },
            await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();

        // Listening + Reading: the normal standard libraries in Arabic and English.
        Assert.All(SharedListeningReading, id => Assert.Contains(id, visible));

        // Writing: ONLY Crash Course / Fast Track + Medicine English Sessions + Workshops.
        Assert.All(WhitelistedWriting, id => Assert.Contains(id, visible));
        Assert.DoesNotContain(WritingBatch1, visible);
        Assert.DoesNotContain(WritingNewBatch, visible);

        // Speaking: the standard Medicine Speaking library, Arabic and English.
        Assert.All(StandardMedicineSpeaking, id => Assert.Contains(id, visible));
    }

    // ── §6.2 Full Crash Course + 5 Letters — identical video visibility ────────────────

    [Fact]
    public async Task Test2_FullCrashPlus5Letters_HasIdenticalVisibilityToPlus3()
    {
        await using var plus3 = await CreateAsync("crash-3letters", "crash_course_bundle");
        await using var plus5 = await CreateAsync("crash-5letters", "crash_course_bundle");
        await using var basePackage = await CreateAsync("crash-course", "crash_course");

        var three = await plus3.VisibleVideoIdsAsync();
        var five = await plus5.VisibleVideoIdsAsync();
        var baseline = await basePackage.VisibleVideoIdsAsync();

        Assert.True(three.SetEquals(five), "The +3 and +5 letter variants must expose the same videos.");
        Assert.True(three.SetEquals(baseline), "Assessment quantity must not change the video entitlement.");
    }

    // ── §6.3 Recorded Writing Crash Course — Writing card only ────────────────────────

    [Fact]
    public async Task Test3_RecordedWritingCrash_RendersOnlyTheWritingCard()
    {
        await using var harness = await CreateAsync("writing-crash", "writing_crash");

        Assert.Equal(new HashSet<string> { "writing" }, await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();
        Assert.All(WhitelistedWriting, id => Assert.Contains(id, visible));
        Assert.All(SharedListeningReading, id => Assert.DoesNotContain(id, visible));
        Assert.All(StandardMedicineSpeaking, id => Assert.DoesNotContain(id, visible));
    }

    // ── §6.4 Writing Crash + 10 Letter Assessments ────────────────────────────────────

    [Fact]
    public async Task Test4_WritingCrashPlus10Letters_HidesBatchFoldersAndKeepsTheWhitelist()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        Assert.Equal(new HashSet<string> { "writing" }, await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();
        Assert.Contains(WritingNewMedicineCrash, visible);
        Assert.Contains(WritingFastTrackCrash, visible);
        Assert.Contains(WritingMedicineEnglishSessions, visible);
        Assert.Contains(WritingMedicineEnglishWorkshops, visible);
        Assert.DoesNotContain(WritingBatch1, visible);
        Assert.DoesNotContain(WritingNewBatch, visible);
    }

    [Fact]
    public async Task Test4b_EveryWritingCrashLetterVariant_SharesOneVideoLibrary()
    {
        await using var recorded = await CreateAsync("writing-crash", "writing_crash");
        var expected = await recorded.VisibleVideoIdsAsync();

        foreach (var code in new[] { "writing-crash-2", "writing-crash-3", "writing-crash-5", "writing-crash-7", "writing-crash-10" })
        {
            await using var harness = await CreateAsync(code, "writing_crash_bundle");
            var actual = await harness.VisibleVideoIdsAsync();
            Assert.True(expected.SetEquals(actual), $"{code} must share the Writing Crash video library.");
        }
    }

    // ── §6.5 Recorded Speaking Crash Course — must NOT be an empty library ────────────

    [Fact]
    public async Task Test5_RecordedSpeakingCrash_ShowsSpeakingContentAndNothingElse()
    {
        await using var harness = await CreateAsync("speaking-crash", "speaking_crash");

        Assert.Equal(new HashSet<string> { "speaking" }, await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();

        // "The page must not show 0 videos when eligible Speaking content exists."
        Assert.NotEmpty(visible);
        Assert.All(StandardMedicineSpeaking, id => Assert.Contains(id, visible));
        Assert.All(SharedListeningReading, id => Assert.DoesNotContain(id, visible));
        Assert.All(WhitelistedWriting, id => Assert.DoesNotContain(id, visible));
    }

    // ── §6.6 / §6.7 Mega Special + Double Special ─────────────────────────────────────

    [Theory]
    [InlineData("mega-special", "combo_mega")]
    [InlineData("double-special", "combo_double")]
    public async Task Test6And7_SpecialPackages_ShowWritingAndSpeakingOnlyWithSpecialFilters(
        string planCode, string productCategory)
    {
        await using var harness = await CreateAsync(planCode, productCategory);

        Assert.Equal(new HashSet<string> { "writing", "speaking" }, await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();

        // Listening/Reading are absent — not inherited from any "full" package.
        Assert.All(SharedListeningReading, id => Assert.DoesNotContain(id, visible));

        // Writing uses the same whitelist as the Crash family.
        Assert.All(WhitelistedWriting, id => Assert.Contains(id, visible));
        Assert.DoesNotContain(WritingBatch1, visible);
        Assert.DoesNotContain(WritingNewBatch, visible);

        // Speaking uses the special-package filter: the applicable Medicine English speaking
        // sessions/workshops, but NOT unrelated full-course Speaking collections.
        Assert.Contains(SpeakingEnglishSessions, visible);
        Assert.Contains(SpeakingEnglishWorkshops, visible);
        Assert.DoesNotContain(SpeakingMedicineArabic, visible);
    }

    [Fact]
    public async Task Test7_DoubleSpecial_MatchesMegaSpecialExactly()
    {
        await using var mega = await CreateAsync("mega-special", "combo_mega");
        await using var doubleSpecial = await CreateAsync("double-special", "combo_double");

        var megaIds = await mega.VisibleVideoIdsAsync();
        var doubleIds = await doubleSpecial.VisibleVideoIdsAsync();
        Assert.True(megaIds.SetEquals(doubleIds));
    }

    // ── §6.8 Counts are recalculated after filtering ──────────────────────────────────

    [Fact]
    public async Task Test8_Counts_AreComputedAfterEntitlementFiltering()
    {
        await using var writingCrash = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        // 4 whitelisted Writing collections, one video each. The Batch 1 / New Batch /
        // Nursing folders must not be counted.
        Assert.Equal((4, 4), await writingCrash.CountsForAsync("writing"));
        Assert.Equal((0, 0), await writingCrash.CountsForAsync("listening"));
        Assert.Equal((0, 0), await writingCrash.CountsForAsync("reading"));
        Assert.Equal((0, 0), await writingCrash.CountsForAsync("speaking"));

        await using var special = await CreateAsync("mega-special", "combo_mega");
        Assert.Equal((4, 4), await special.CountsForAsync("writing"));
        Assert.Equal((2, 2), await special.CountsForAsync("speaking"));

        await using var fullCrash = await CreateAsync("crash-3letters", "crash_course_bundle");
        Assert.Equal((4, 4), await fullCrash.CountsForAsync("writing"));
        Assert.Equal((3, 3), await fullCrash.CountsForAsync("speaking"));
        Assert.Equal((2, 2), await fullCrash.CountsForAsync("listening"));
        Assert.Equal((1, 1), await fullCrash.CountsForAsync("reading"));
    }

    // ── §6.9 Direct URL isolation — the backend denies, UI hiding is not enough ───────

    [Fact]
    public async Task Test9_DirectUrl_IsDeniedForEveryHiddenSubtestAndCollection()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        foreach (var hidden in SharedListeningReading
            .Concat(StandardMedicineSpeaking)
            .Concat([WritingBatch1, WritingNewBatch, WritingNursing, SpeakingNursing]))
        {
            Assert.Null(await harness.DeepLinkAsync(hidden));
        }

        foreach (var allowed in WhitelistedWriting)
        {
            Assert.NotNull(await harness.DeepLinkAsync(allowed));
        }
    }

    [Fact]
    public async Task Test9b_DirectUrl_SpeakingCrashCannotReachWritingOrListening()
    {
        await using var harness = await CreateAsync("speaking-crash", "speaking_crash");

        Assert.Null(await harness.DeepLinkAsync(WritingNewMedicineCrash));
        Assert.Null(await harness.DeepLinkAsync(WritingMedicineEnglishSessions));
        Assert.Null(await harness.DeepLinkAsync(ListeningEnglishSessions));
        Assert.NotNull(await harness.DeepLinkAsync(SpeakingMedicineArabic));
    }

    [Fact]
    public async Task Test9c_PlaybackEvents_AreRejectedForAHiddenVideo()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        Assert.False(await harness.Service.RecordEventAsync(
            LearnerId, ListeningEnglishSessions, null, "play", 0, default));
        Assert.False(await harness.Service.RecordEventAsync(
            LearnerId, WritingBatch1, null, "play", 0, default));
        Assert.True(await harness.Service.RecordEventAsync(
            LearnerId, WritingFastTrackCrash, null, "play", 0, default));
    }

    [Fact]
    public async Task Test9e_PlaybackEntitlement_404sRatherThanOfferingAnUpgrade()
    {
        // §CORE: excluded content is ABSENT, not locked behind a paywall — a 402
        // content_locked answer would confirm the video exists to a learner probing URLs.
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");
        var hidden = await harness.Db.LibraryVideos.FindAsync(ListeningEnglishSessions);
        Assert.NotNull(hidden);

        var ex = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => harness.Entitlements.RequireAccessAsync(LearnerId, hidden!, default));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("video_not_found", ex.ErrorCode);

        var outOfWhitelist = await harness.Db.LibraryVideos.FindAsync(WritingBatch1);
        var ex2 = await Assert.ThrowsAsync<OetLearner.Api.Services.ApiException>(
            () => harness.Entitlements.RequireAccessAsync(LearnerId, outOfWhitelist!, default));
        Assert.Equal(404, ex2.StatusCode);
        Assert.Equal("video_not_found", ex2.ErrorCode);
    }

    [Fact]
    public async Task Test9d_ProgressAndBookmark_AreRejectedForAHiddenVideo()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        Assert.Null(await harness.Service.UpdateProgressAsync(LearnerId, WritingBatch1, 30, default));
        Assert.Null(await harness.Service.ToggleBookmarkAsync(LearnerId, ListeningEnglishSessions, default));
        Assert.NotNull(await harness.Service.UpdateProgressAsync(LearnerId, WritingFastTrackCrash, 30, default));
    }

    // ── §6.10 Profession isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Test10_ProfessionIsolation_NoNursingFolderIsEverReachable()
    {
        foreach (var (code, category) in new[]
        {
            ("crash-3letters", "crash_course_bundle"),
            ("writing-crash-10", "writing_crash_bundle"),
            ("speaking-crash", "speaking_crash"),
            ("mega-special", "combo_mega"),
        })
        {
            await using var harness = await CreateAsync(code, category);
            var visible = await harness.VisibleVideoIdsAsync();

            Assert.DoesNotContain(WritingNursing, visible);
            Assert.DoesNotContain(SpeakingNursing, visible);
            Assert.Null(await harness.DeepLinkAsync(WritingNursing));
            Assert.Null(await harness.DeepLinkAsync(SpeakingNursing));
        }
    }

    [Fact]
    public async Task Test10b_ANursingLearnerOnAMedicinePackage_GetsNoWritingOrSpeakingContent()
    {
        // §5: these families are Medicine-only. A Nursing learner holding one must NOT be
        // handed Nursing Writing/Speaking through it — a Nursing crash product would have to
        // be mapped separately.
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle", profession: "nursing");

        var visible = await harness.VisibleVideoIdsAsync();
        Assert.DoesNotContain(WritingNursing, visible);
        Assert.Null(await harness.DeepLinkAsync(WritingNursing));
    }

    // ── Composition: a package outside the specification is not narrowed by one inside ──

    [Fact]
    public async Task MultiPackage_CrashPlusFullCourse_KeepsTheFullCourseLibrary()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");
        await AddPackageAsync(harness, "full-condensed-medicine", "full_course");

        var visible = await harness.VisibleVideoIdsAsync();
        Assert.Contains(WritingBatch1, visible);
        Assert.Contains(ListeningEnglishSessions, visible);
        Assert.Contains(SpeakingMedicineArabic, visible);
    }

    [Fact]
    public async Task MultiPackage_WritingCrashPlusRecalls_StaysWritingOnly()
    {
        // A non-video product (Listening Recalls) is neutral: it must not re-expose the
        // subtests the Writing Crash package excludes.
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");
        await AddPackageAsync(harness, "listening-recalls", "recall_package");

        Assert.Equal(new HashSet<string> { "writing" }, await harness.VisibleSubtestCardsAsync());
    }

    [Fact]
    public async Task MultiPackage_WritingCrashPlusSpeakingCrash_UnionsBothSubtests()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");
        await AddPackageAsync(harness, "speaking-crash", "speaking_crash");

        Assert.Equal(new HashSet<string> { "writing", "speaking" }, await harness.VisibleSubtestCardsAsync());

        var visible = await harness.VisibleVideoIdsAsync();
        Assert.All(WhitelistedWriting, id => Assert.Contains(id, visible));
        // Speaking Crash grants the STANDARD Medicine Speaking library, so the Arabic Medicine
        // sessions the special-package filter would hide are visible here.
        Assert.All(StandardMedicineSpeaking, id => Assert.Contains(id, visible));
        Assert.All(SharedListeningReading, id => Assert.DoesNotContain(id, visible));
    }

    // ── The per-card lock badge must agree with visibility ────────────────────────────

    [Fact]
    public async Task VisibleCards_AreNeverRenderedAsRequiresUpgrade()
    {
        await using var harness = await CreateAsync("writing-crash-10", "writing_crash_bundle");

        var home = await harness.Service.GetHomeAsync(LearnerId, default);
        var cards = home.Categories.SelectMany(c => c.Videos).Concat(home.Uncategorized).ToList();

        Assert.NotEmpty(cards);
        Assert.All(cards, card =>
        {
            Assert.True(card.IsAccessible, $"{card.Id} is listed but flagged inaccessible.");
            Assert.False(card.RequiresUpgrade);
        });
    }

    // ── §1.6 the structured classifier, not display titles ───────────────────────────

    [Theory]
    [InlineData("crash_course", "crash-course", MedicinePackageFamily.FullCrash)]
    [InlineData("crash_course_bundle", "crash-3letters", MedicinePackageFamily.FullCrash)]
    [InlineData("crash_course_bundle", "crash-5letters", MedicinePackageFamily.FullCrash)]
    [InlineData("writing_crash", "writing-crash", MedicinePackageFamily.WritingCrash)]
    [InlineData("writing_crash_bundle", "writing-crash-10", MedicinePackageFamily.WritingCrash)]
    [InlineData("speaking_crash", "speaking-crash", MedicinePackageFamily.SpeakingCrash)]
    [InlineData("combo_mega", "mega-special", MedicinePackageFamily.Special)]
    [InlineData("combo_double", "double-special", MedicinePackageFamily.Special)]
    [InlineData("full_course", "full-condensed-medicine", MedicinePackageFamily.None)]
    [InlineData("full_course_bundle", "full-nursing-premium", MedicinePackageFamily.None)]
    [InlineData("foundation", "basic-english", MedicinePackageFamily.None)]
    [InlineData("recall_package", "listening-recalls", MedicinePackageFamily.None)]
    [InlineData("speaking_session", "speaking-1session", MedicinePackageFamily.None)]
    [InlineData("book", "tutor-book", MedicinePackageFamily.None)]
    public void Classify_MapsEveryCatalogPlanToItsFamily(
        string productCategory, string planCode, MedicinePackageFamily expected)
        => Assert.Equal(expected, MedicinePackageVideoPolicy.Classify(productCategory, planCode));

    [Theory]
    [InlineData("writing-crash-10", MedicinePackageFamily.WritingCrash)]
    [InlineData("speaking-crash", MedicinePackageFamily.SpeakingCrash)]
    [InlineData("crash-3letters", MedicinePackageFamily.FullCrash)]
    [InlineData("mega-special", MedicinePackageFamily.Special)]
    [InlineData("full-condensed-medicine", MedicinePackageFamily.None)]
    public void Classify_FallsBackToThePlanCodeWhenTheCategoryIsMissing(
        string planCode, MedicinePackageFamily expected)
        => Assert.Equal(expected, MedicinePackageVideoPolicy.Classify(null, planCode));
}
