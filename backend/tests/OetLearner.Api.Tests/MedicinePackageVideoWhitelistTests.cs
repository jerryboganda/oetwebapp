using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// §4 "Exact Writing whitelist logic" and the §3D special-package Speaking filter, plus the
/// §2 package-family map and the multi-package union. Pure policy — no database.
/// </summary>
public sealed class MedicinePackageVideoWhitelistTests
{
    // ── §4 ALLOW rows ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Writing / Arabic / New Medicine Crash Course / Sessions / Day 1")]
    [InlineData("Writing / Arabic / New Medicine Crash Course / Workshops")]
    [InlineData("Writing / Medicine / Arabic / Writing Sessions ( Crash Course Old )")]
    [InlineData("writing / medicine / arabic / crash course")]          // case-insensitive
    [InlineData("Writing / Medicine / Arabic / Fast-Track Crash Course")]
    [InlineData("Writing / Medicine / Arabic / Fast Track")]            // spacing variant
    [InlineData("Writing / Medicine / Arabic / Fast-Track")]            // hyphen variant
    [InlineData("Writing / Medicine / Arabic / FastTrack")]             // no-separator variant
    [InlineData("Writing / Medicine / Arabic / FAST  TRACK  Sessions")] // double spacing
    [InlineData("Writing / Medicine / English / Sessions")]
    [InlineData("Writing / Medicine / English / Workshops / Sessions")]
    [InlineData("Writing/Medicine/English/Sessions")]                   // unspaced separators
    public void WritingWhitelist_Allows(string collection)
        => Assert.True(MedicinePackageVideoPolicy.IsWritingAllowed([collection]));

    // ── §4 HIDE rows ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Writing / Medicine / Arabic / Batch 1 / Sessions")]
    [InlineData("Writing / Medicine / Arabic / Batch 1 / Workshops")]
    [InlineData("Writing / Medicine / Arabic / New Batch / Sessions")]
    [InlineData("Writing / Medicine / Arabic / New Batch / Workshops")]
    [InlineData("Writing / Medicine / Arabic / December Batch / Sessions")]
    [InlineData("Writing / Nursing / Sessions")]
    [InlineData("Writing / Pharmacy / Sessions")]
    [InlineData("Writing / Medicine / Arabic / Sessions")]
    [InlineData("")]
    public void WritingWhitelist_Hides(string collection)
        => Assert.False(MedicinePackageVideoPolicy.IsWritingAllowed([collection]));

    [Fact]
    public void WritingWhitelist_AllowsANewBatchFolderThatItselfNamesTheCrashCourse()
        // §4: "Full-course batch content UNLESS the actual path/title also contains
        // Crash Course/Fast Track."
        => Assert.True(MedicinePackageVideoPolicy.IsWritingAllowed(
            ["Writing / Medicine / Arabic / New Batch / Fast-Track Crash Course"]));

    [Fact]
    public void WritingWhitelist_MatchesAnyOneOfTheVideosCollections()
        => Assert.True(MedicinePackageVideoPolicy.IsWritingAllowed(
            ["Lesson 3", "Writing / Medicine / Arabic / Batch 1 / Sessions", "Writing / Medicine / English / Sessions"]));

    [Fact]
    public void WritingWhitelist_HidesAVideoWithNoCollectionAtAll()
        => Assert.False(MedicinePackageVideoPolicy.IsWritingAllowed(["Lesson 3"]));

    // ── §3D special-package Speaking filter ───────────────────────────────────────────

    [Theory]
    [InlineData("Speaking / English / Sessions")]
    [InlineData("Speaking / English / Workshops")]
    [InlineData("Speaking / Medicine / English / Sessions")]
    [InlineData("Speaking / Medicine / Arabic / Fast-Track Crash Course")]
    [InlineData("Speaking / Arabic / New Medicine Crash Course / Sessions")]
    [InlineData("Speaking / Medicine / Arabic / Sessions")]
    [InlineData("Speaking / Medicine / Arabic / Workshops")]
    public void SpecialSpeakingFilter_Allows(string collection)
        => Assert.True(MedicinePackageVideoPolicy.IsSpecialSpeakingAllowed([collection]));

    [Theory]
    [InlineData("Speaking / Medicine / Arabic / Batch 1 / Sessions")]
    [InlineData("Speaking / Medicine / Arabic / New Batch / Workshops")]
    [InlineData("Speaking / Nursing / Sessions")]
    [InlineData("Speaking / Pharmacy / Sessions")]
    public void SpecialSpeakingFilter_HidesUnrelatedFullCourseCollections(string collection)
        => Assert.False(MedicinePackageVideoPolicy.IsSpecialSpeakingAllowed([collection]));

    // ── §2 package family map ─────────────────────────────────────────────────────────

    [Fact]
    public void FullCrashFamily_ShowsAllFourSubtestsWithWritingFiltered()
    {
        var access = MedicinePackageVideoPolicy.Resolve(MedicinePackageFamily.FullCrash);
        Assert.True(access.Applies);
        Assert.Equal(VideoSubtestGrant.Full, access.For("listening"));
        Assert.Equal(VideoSubtestGrant.Full, access.For("reading"));
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("writing"));
        Assert.Equal(VideoSubtestGrant.Full, access.For("speaking"));
    }

    [Fact]
    public void WritingCrashFamily_HidesEverythingButWriting()
    {
        var access = MedicinePackageVideoPolicy.Resolve(MedicinePackageFamily.WritingCrash);
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("listening"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("reading"));
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("writing"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("speaking"));
    }

    [Fact]
    public void SpeakingCrashFamily_HidesEverythingButSpeaking()
    {
        var access = MedicinePackageVideoPolicy.Resolve(MedicinePackageFamily.SpeakingCrash);
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("listening"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("reading"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("writing"));
        Assert.Equal(VideoSubtestGrant.Full, access.For("speaking"));
    }

    [Fact]
    public void SpecialFamily_IsWritingPlusSpeakingOnlyAndNeverInheritsListeningOrReading()
    {
        var access = MedicinePackageVideoPolicy.Resolve(MedicinePackageFamily.Special);
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("listening"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("reading"));
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("writing"));
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("speaking"));
    }

    // ── Union across packages ─────────────────────────────────────────────────────────

    [Fact]
    public void Union_OfWritingAndSpeakingCrash_GrantsBoth()
    {
        var access = MedicineVideoAccess.Union([
            MedicinePackageVideoPolicy.ResolveOrNeutral("writing_crash", "writing-crash"),
            MedicinePackageVideoPolicy.ResolveOrNeutral("speaking_crash", "speaking-crash"),
        ]);

        Assert.True(access.Applies);
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("writing"));
        Assert.Equal(VideoSubtestGrant.Full, access.For("speaking"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("listening"));
    }

    [Fact]
    public void Union_TakesTheWiderGrantForTheSameSubtest()
    {
        // Special filters Speaking; Speaking Crash grants the standard library. The learner
        // holding both must see the standard library.
        var access = MedicineVideoAccess.Union([
            MedicinePackageVideoPolicy.ResolveOrNeutral("combo_mega", "mega-special"),
            MedicinePackageVideoPolicy.ResolveOrNeutral("speaking_crash", "speaking-crash"),
        ]);

        Assert.Equal(VideoSubtestGrant.Full, access.For("speaking"));
    }

    [Fact]
    public void Union_WithAFullCourse_TurnsTheSpecificationOff()
    {
        var access = MedicineVideoAccess.Union([
            MedicinePackageVideoPolicy.ResolveOrNeutral("writing_crash", "writing-crash"),
            MedicinePackageVideoPolicy.ResolveOrNeutral("full_course", "full-condensed-medicine"),
        ]);

        Assert.False(access.Applies);
    }

    [Theory]
    [InlineData("foundation", "basic-english")]
    [InlineData("recall_package", "listening-recalls")]
    [InlineData("book", "tutor-book")]
    [InlineData("speaking_session", "speaking-1session")]
    public void Union_WithANonVideoProduct_DoesNotWidenACrashPackage(string category, string code)
    {
        var access = MedicineVideoAccess.Union([
            MedicinePackageVideoPolicy.ResolveOrNeutral("writing_crash", "writing-crash"),
            MedicinePackageVideoPolicy.ResolveOrNeutral(category, code),
        ]);

        Assert.True(access.Applies);
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("listening"));
        Assert.Equal(VideoSubtestGrant.Hidden, access.For("speaking"));
        Assert.Equal(VideoSubtestGrant.Whitelisted, access.For("writing"));
    }

    [Fact]
    public void Union_OfOnlyNonVideoProducts_LeavesTheLegacyEngineInCharge()
    {
        var access = MedicineVideoAccess.Union([
            MedicinePackageVideoPolicy.ResolveOrNeutral("recall_package", "listening-recalls"),
        ]);

        Assert.False(access.Applies);
    }

    [Fact]
    public void Union_OfNothing_LeavesTheLegacyEngineInCharge()
        => Assert.False(MedicineVideoAccess.Union([]).Applies);

    // ── Subtest resolution ────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveSubtest_PrefersTheVideosOwnSubtestCode()
        => Assert.Equal("writing", MedicinePackageVideoPolicy.ResolveSubtest(
            new LibraryVideo { Id = "v", Title = "Lesson", SubtestCode = "Writing" },
            ["Speaking / English / Sessions"]));

    [Fact]
    public void ResolveSubtest_FallsBackToTheCollectionBreadcrumb()
        => Assert.Equal("speaking", MedicinePackageVideoPolicy.ResolveSubtest(
            new LibraryVideo { Id = "v", Title = "Lesson" },
            ["Speaking / English / Sessions"]));

    [Fact]
    public void ResolveSubtest_ReturnsNullWhenNothingIdentifiesTheSubtest()
        => Assert.Null(MedicinePackageVideoPolicy.ResolveSubtest(
            new LibraryVideo { Id = "v", Title = "Lesson" },
            ["Some / Unknown / Folder"]));

    // ── §1.1 / §5 Medicine-only content ───────────────────────────────────────────────

    [Fact]
    public void MedicineScope_AllowsMedicineWritingAndSpeaking()
    {
        Assert.True(MedicinePackageVideoPolicy.IsMedicineScoped("writing", ["medicine"]));
        Assert.True(MedicinePackageVideoPolicy.IsMedicineScoped("speaking", ["medicine", "nursing"]));
    }

    [Fact]
    public void MedicineScope_RejectsOtherProfessionsAndUntargetedWritingSpeaking()
    {
        Assert.False(MedicinePackageVideoPolicy.IsMedicineScoped("writing", ["nursing"]));
        Assert.False(MedicinePackageVideoPolicy.IsMedicineScoped("speaking", ["pharmacy"]));
        Assert.False(MedicinePackageVideoPolicy.IsMedicineScoped("writing", []));
    }

    [Fact]
    public void MedicineScope_LeavesListeningAndReadingShared()
    {
        Assert.True(MedicinePackageVideoPolicy.IsMedicineScoped("listening", []));
        Assert.True(MedicinePackageVideoPolicy.IsMedicineScoped("reading", []));
    }
}
