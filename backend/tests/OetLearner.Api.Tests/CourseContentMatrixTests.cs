using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
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
        Assert.False(CourseContentMatrix.VideoAppearsFor(professionId, "ar", "speaking", ["medicine", "physiotherapy"]));
        Assert.Throws<ArgumentException>(() => CourseContentMatrix.ExpectedVideoTargets("ar", "writing", professionId));
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
    public void General_English_Scope_Is_Isolated_From_Oet_Scopes()
    {
        var general = new MaterialFolder { Id = "general", Name = "Anything", ScopeKind = MaterialScopeKinds.GeneralEnglish };
        var shared = new MaterialFolder { Id = "shared", Name = "General English", ScopeKind = MaterialScopeKinds.Shared };
        var all = new Dictionary<string, MaterialFolder> { [general.Id] = general, [shared.Id] = shared };

        Assert.True(MaterialAccessService.IsBasicEnglishFolder(general, all));
        Assert.False(MaterialAccessService.IsBasicEnglishFolder(shared, all));
    }
}
