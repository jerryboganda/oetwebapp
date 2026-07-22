using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class CourseContentMatrixTests
{
    [Fact]
    public void Every_Profession_Language_And_Subtest_Matches_The_Flowchart()
    {
        foreach (var profession in CourseContentMatrix.Professions)
        foreach (var language in new[] { "en", "ar" })
        foreach (var subtest in CourseContentMatrix.Subtests)
        {
            IReadOnlyList<string> targets = language == "ar" && (subtest is "writing" or "speaking")
                ? profession.Id switch
                {
                    "nursing" => ["nursing"],
                    "pharmacy" => ["pharmacy"],
                    _ => ["medicine", "physiotherapy"],
                }
                : [];

            var appears = CourseContentMatrix.VideoAppearsFor(profession.Id, language, subtest, targets);
            var expected = (subtest is "listening" or "reading")
                || (profession.Id is "medicine" or "nursing" or "pharmacy" or "physiotherapy");
            Assert.Equal(expected, appears);
        }
    }

    [Theory]
    [InlineData("dentistry")]
    [InlineData("radiography")]
    public void Dentistry_And_Radiography_Cannot_Receive_Writing_Or_Speaking(string professionId)
    {
        Assert.False(CourseContentMatrix.VideoAppearsFor(professionId, "en", "writing", []));
        Assert.False(CourseContentMatrix.VideoAppearsFor(professionId.ToUpperInvariant(), "EN", "Writing", []));
        Assert.False(CourseContentMatrix.VideoAppearsFor(professionId, "ar", "speaking", ["medicine", "physiotherapy"]));
        Assert.Throws<ArgumentException>(() => CourseContentMatrix.ExpectedVideoTargets("ar", "writing", professionId));
    }

    [Fact]
    public void Unsupported_Profession_Targets_Fail_Closed()
    {
        Assert.False(CourseContentMatrix.TryValidateVideo("en", "listening", ["other-allied"], out var message));
        Assert.Contains("unsupported profession", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Structured_Material_Scope_Takes_Priority_Over_Legacy_Folder_Names()
    {
        var folder = new MaterialFolder
        {
            Id = "mfd-1", Name = "Medicine", ScopeKind = MaterialScopeKinds.Shared,
        };
        var all = new Dictionary<string, MaterialFolder> { [folder.Id] = folder };
        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Medicine", "Nursing" };
        var nursing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nursing", "Nursing" };

        Assert.True(MaterialAccessService.IsDisciplineVisible(folder, all, universe, nursing));
    }

    [Fact]
    public void Profession_Material_Scope_Is_Inherited_And_Fails_Closed_For_Other_Professions()
    {
        var root = new MaterialFolder
        {
            Id = "medicine-root", Name = "Resources", ScopeKind = MaterialScopeKinds.Profession, ProfessionId = "medicine",
        };
        var child = new MaterialFolder { Id = "child", Name = "Writing", ParentFolderId = root.Id };
        var all = new Dictionary<string, MaterialFolder> { [root.Id] = root, [child.Id] = child };
        var universe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "medicine", "nursing" };

        Assert.True(MaterialAccessService.IsDisciplineVisible(child, all, universe,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "medicine" }));
        Assert.False(MaterialAccessService.IsDisciplineVisible(child, all, universe,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nursing" }));
        Assert.False(MaterialAccessService.IsDisciplineVisible(child, all, universe,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void General_English_Scope_Is_Isolated_From_Oet_Scopes()
    {
        var general = new MaterialFolder { Id = "general", Name = "Anything", ScopeKind = MaterialScopeKinds.GeneralEnglish };
        var shared = new MaterialFolder { Id = "shared", Name = "General English", ScopeKind = MaterialScopeKinds.Shared };
        var all = new Dictionary<string, MaterialFolder> { [general.Id] = general, [shared.Id] = shared };

        Assert.True(MaterialAccessService.IsBasicEnglishFolder(general, all));
        Assert.False(MaterialAccessService.IsBasicEnglishFolder(shared, all));
    }

    [Fact]
    public void Legacy_Material_Ancestry_Backfills_To_The_Expected_Structured_Scopes()
    {
        var listening = new MaterialFolder { Id = "listening", Name = "Listening" };
        var sharedChild = new MaterialFolder { Id = "shared-child", Name = "Set 1", ParentFolderId = listening.Id };
        var writing = new MaterialFolder { Id = "writing", Name = "Writing" };
        var medicine = new MaterialFolder { Id = "medicine", Name = "Medicine", ParentFolderId = writing.Id };
        var professionChild = new MaterialFolder { Id = "profession-child", Name = "Letters", ParentFolderId = medicine.Id };
        var general = new MaterialFolder { Id = "general", Name = "Basic English Course" };
        var generalChild = new MaterialFolder { Id = "general-child", Name = "Grammar", ParentFolderId = general.Id };
        var all = new Dictionary<string, MaterialFolder>
        {
            [listening.Id] = listening, [sharedChild.Id] = sharedChild,
            [writing.Id] = writing, [medicine.Id] = medicine, [professionChild.Id] = professionChild,
            [general.Id] = general, [generalChild.Id] = generalChild,
        };

        Assert.Equal((MaterialScopeKinds.Shared, (string?)null), CourseContentMatrix.ResolveMaterialScope(sharedChild, all));
        Assert.Equal((MaterialScopeKinds.Profession, "medicine"), CourseContentMatrix.ResolveMaterialScope(professionChild, all));
        Assert.Equal((MaterialScopeKinds.GeneralEnglish, (string?)null), CourseContentMatrix.ResolveMaterialScope(generalChild, all));
        Assert.Equal("writing", CourseContentMatrix.ResolveMaterialSubtest(professionChild, all));
    }

    [Fact]
    public void General_English_Files_Are_Isolated_From_Oet_Subtest_Restrictions()
    {
        var general = new MaterialFolder { Id = "general", Name = "General", ScopeKind = MaterialScopeKinds.GeneralEnglish };
        var shared = new MaterialFolder { Id = "shared", Name = "Listening", ScopeKind = MaterialScopeKinds.Shared };
        var all = new Dictionary<string, MaterialFolder> { [general.Id] = general, [shared.Id] = shared };
        var entitlement = new EffectiveEntitlementSnapshot(
            UserId: "u1", HasEligibleSubscription: true, IsTrial: false, Tier: "paid",
            SubscriptionId: "sub1", SubscriptionStatus: SubscriptionStatus.Active,
            PlanId: "plan1", PlanVersionId: null, PlanCode: "writing-only",
            AiQuotaPlanCode: null, AiQuotaPlanCodeSource: null, ActiveAddOnCodes: [],
            IsFrozen: false, Trace: [])
        {
            AllSubtestsIncluded = false,
            IncludedSubtests = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "writing" },
        };

        Assert.True(MaterialAccessService.IsMaterialFileSubtestInScope(
            new MaterialFile { Id = "general-file", FolderId = general.Id, SubtestCode = "listening" }, all, entitlement));
        Assert.False(MaterialAccessService.IsMaterialFileSubtestInScope(
            new MaterialFile { Id = "shared-file", FolderId = shared.Id, SubtestCode = "listening" }, all, entitlement));
    }
}
