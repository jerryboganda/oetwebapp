using OetLearner.Api.Domain;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Tests;

/// <summary>
/// PackageScopePolicy pure mapping tests (spec: video-visibility-rules). Crash detection
/// wins across BOTH the category set and the plan code; Full Course maps to the
/// profession-isolated scope; everything else is SHARED (implicit, empty scope set).
/// </summary>
public sealed class PackageScopePolicyTests
{
    [Theory]
    [InlineData("full_course", "full-medicine-12m", "medicine", VideoVisibilityScopes.FullMedicine)]
    [InlineData("full_course", "full-nursing-12m", "nursing", VideoVisibilityScopes.FullNursing)]
    [InlineData("full_course", "full-pharmacy-12m", "pharmacy", VideoVisibilityScopes.FullPharmacy)]
    [InlineData("full_course", "full-medicine-12m", "physiotherapy", VideoVisibilityScopes.FullMedicine)]
    [InlineData("full_course", "full-medicine-12m", "other-allied-health", VideoVisibilityScopes.FullMedicine)]
    [InlineData("full_course", "full-medicine-12m", "all", VideoVisibilityScopes.FullMedicine)]
    [InlineData("full_course", "full-medicine-12m", "", VideoVisibilityScopes.FullMedicine)]
    [InlineData("full_course", "full-medicine-12m", null, VideoVisibilityScopes.FullMedicine)]
    public void FullCourseCategories_MapToProfessionScope(string category, string code, string? profession, string expected)
    {
        Assert.Equal(expected, PackageScopePolicy.Resolve(category, code, profession));
    }

    [Theory]
    [InlineData("full_course_bundle", "nursing")]
    [InlineData("combo_double", "nursing")]
    [InlineData("combo_mega", "nursing")]
    public void FullBundleAndComboCategories_MapToProfessionScope(string category, string profession)
    {
        Assert.Equal(VideoVisibilityScopes.FullNursing, PackageScopePolicy.Resolve(category, "any-plan-code", profession));
    }

    [Theory]
    [InlineData("crash_course")]
    [InlineData("crash_course_bundle")]
    [InlineData("writing_crash")]
    [InlineData("writing_crash_bundle")]
    [InlineData("speaking_crash")]
    public void CrashCategories_AlwaysResolveToCrash(string category)
    {
        Assert.Equal(VideoVisibilityScopes.Crash, PackageScopePolicy.Resolve(category, "unrelated-code", "medicine"));
    }

    [Fact]
    public void CrashCodePrefix_BeatsFullCategory()
    {
        // Writing crash bundle whose marketing category says full — code prefix must win.
        Assert.Equal(
            VideoVisibilityScopes.Crash,
            PackageScopePolicy.Resolve("writing_crash_bundle", "writing-crash-2", "all"));
    }

    [Theory]
    [InlineData("", "writing-crash-2", "all", VideoVisibilityScopes.Crash)]
    [InlineData("", "crash-course", "all", VideoVisibilityScopes.Crash)]
    [InlineData("", "full-nursing", "nursing", VideoVisibilityScopes.FullNursing)]
    [InlineData("", "speaking-crash", "medicine", VideoVisibilityScopes.Crash)]
    public void CodePrefixAlone_DrivesResolution(string category, string code, string profession, string expected)
    {
        Assert.Equal(expected, PackageScopePolicy.Resolve(category, code, profession));
    }

    [Theory]
    [InlineData("foundation")]
    [InlineData("speaking_session")]
    [InlineData("book")]
    [InlineData("recall_package")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some_unknown_category")]
    public void NonFullNonCrashCategories_ResolveToShared(string? category)
    {
        Assert.Equal(VideoVisibilityScopes.Shared, PackageScopePolicy.Resolve(category, "unrelated-code", "medicine"));
    }

    [Fact]
    public void ResolveSet_SharedIsEmpty_FullIsSingleton()
    {
        Assert.Empty(PackageScopePolicy.ResolveSet("foundation", "foundation-1m", "medicine"));
        Assert.Equal(
            new[] { VideoVisibilityScopes.FullMedicine },
            PackageScopePolicy.ResolveSet("full_course", "full-medicine", "medicine").ToArray());
    }

    [Fact]
    public void UnionScopes_DropsShared_AccumulatesDistinctScopes()
    {
        var union = PackageScopePolicy.UnionScopes(new[]
        {
            VideoVisibilityScopes.FullMedicine,
            VideoVisibilityScopes.Crash,
            VideoVisibilityScopes.Shared,
            " full_medicine ",
            string.Empty,
        });

        Assert.Equal(2, union.Count);
        Assert.Contains(VideoVisibilityScopes.FullMedicine, union);
        Assert.Contains(VideoVisibilityScopes.Crash, union);
    }

    [Fact]
    public void UnionScopes_AllSharedInputs_YieldEmptySet()
    {
        Assert.Empty(PackageScopePolicy.UnionScopes(new[] { VideoVisibilityScopes.Shared, VideoVisibilityScopes.Shared }));
    }
}
