using System.Text.Json;
using System.Text.Json.Serialization;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// Cross-engine parity test for the writing rule engine.
///
/// Loads <c>lib/rulebook/__tests__/__fixtures__/writing-engine-parity.json</c>
/// — the same fixture corpus consumed by the Vitest suite
/// <c>lib/rulebook/__tests__/writing-rule-fixtures.test.ts</c> — and asserts
/// that <see cref="WritingRuleEngine.Lint"/> produces the SAME set of
/// non-info ruleIds for each fixture.
///
/// Makes the docblock claim ("Behaviour MUST match the TypeScript engine")
/// in <see cref="WritingRuleEngine"/> mechanically enforceable.
/// </summary>
public class WritingEngineParityTests
{
    private readonly WritingRuleEngine _engine = new(new RulebookLoader());

    public static IEnumerable<object[]> ParityFixtureIds()
    {
        var file = LoadFixtureFile();
        foreach (var fx in file.Fixtures)
        {
            if (fx.SkipNet) continue;
            yield return new object[] { fx.Id };
        }
    }

    [Theory]
    [MemberData(nameof(ParityFixtureIds))]
    public void Net_Engine_Produces_Locked_RuleId_Set(string fixtureId)
    {
        var file = LoadFixtureFile();
        var fx = file.Fixtures.FirstOrDefault(f => f.Id == fixtureId)
            ?? throw new InvalidOperationException($"Fixture '{fixtureId}' not found.");
        var profession = ParseProfession(fx.Profession);
        var markers = fx.CaseNotesMarkers is null
            ? null
            : new WritingCaseNotesMarkers(
                AtopicCondition: fx.CaseNotesMarkers.AtopicCondition,
                PatientInitiatedReferral: fx.CaseNotesMarkers.PatientInitiatedReferral,
                ConsentDocumented: fx.CaseNotesMarkers.ConsentDocumented,
                ResultsEnclosed: fx.CaseNotesMarkers.ResultsEnclosed,
                FollowUpDate: fx.CaseNotesMarkers.FollowUpDate);

        var input = new WritingLintInput(
            LetterText: fx.Letter,
            LetterType: fx.LetterType,
            RecipientSpecialty: fx.RecipientSpecialty,
            PatientIsMinor: fx.PatientIsMinor,
            CaseNotesMarkers: markers,
            Profession: profession);

        var findings = _engine.Lint(input);
        var actual = findings
            .Where(f => f.Severity != RuleSeverity.Info)
            .Select(f => f.RuleId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var expected = fx.ExpectedRuleIds
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, actual);
    }

    // ---------------------------------------------------------------------
    // Fixture file loading
    // ---------------------------------------------------------------------

    private static ParityFixtureFile LoadFixtureFile()
    {
        var path = ResolveFixturePath();
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        var file = JsonSerializer.Deserialize<ParityFixtureFile>(json, opts)
            ?? throw new InvalidOperationException($"Failed to parse parity fixture file: {path}");
        return file;
    }

    private static string ResolveFixturePath()
    {
        // Walk up from AppContext.BaseDirectory looking for the repo-rooted
        // path. The test binary normally sits at:
        //   <repo>/backend/tests/OetLearner.Api.Tests/bin/<config>/net10.0/
        // so 6 levels up is the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        const string rel = "lib/rulebook/__tests__/__fixtures__/writing-engine-parity.json";
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate parity fixture '{rel}' walking up from '{AppContext.BaseDirectory}'.");
    }

    private static ExamProfession ParseProfession(string s) => s.ToLowerInvariant() switch
    {
        "medicine" => ExamProfession.Medicine,
        "nursing" => ExamProfession.Nursing,
        "dentistry" => ExamProfession.Dentistry,
        "pharmacy" => ExamProfession.Pharmacy,
        "physiotherapy" => ExamProfession.Physiotherapy,
        "veterinary" => ExamProfession.Veterinary,
        "optometry" => ExamProfession.Optometry,
        "radiography" => ExamProfession.Radiography,
        "occupational-therapy" => ExamProfession.OccupationalTherapy,
        "speech-pathology" => ExamProfession.SpeechPathology,
        "podiatry" => ExamProfession.Podiatry,
        "dietetics" => ExamProfession.Dietetics,
        "other-allied-health" => ExamProfession.OtherAlliedHealth,
        _ => throw new ArgumentException($"Unknown profession '{s}'"),
    };

    // -----------------------------------------------------------------
    // DTOs mirroring the JSON shape
    // -----------------------------------------------------------------

    public sealed class ParityFixtureFile
    {
        [JsonPropertyName("fixtures")]
        public List<ParityFixture> Fixtures { get; set; } = new();
    }

    public sealed class ParityFixture
    {
        public string Id { get; set; } = "";
        public string LetterType { get; set; } = "";
        public string Profession { get; set; } = "medicine";
        public bool PatientIsMinor { get; set; }
        public string? RecipientSpecialty { get; set; }
        public ParityCaseNotesMarkers? CaseNotesMarkers { get; set; }
        public string Letter { get; set; } = "";
        public List<string> ExpectedRuleIds { get; set; } = new();
        public bool SkipNet { get; set; }
        public bool SkipTs { get; set; }
    }

    public sealed class ParityCaseNotesMarkers
    {
        public bool AtopicCondition { get; set; }
        public bool PatientInitiatedReferral { get; set; }
        public bool ConsentDocumented { get; set; }
        public bool ResultsEnclosed { get; set; }
        public string? FollowUpDate { get; set; }
    }
}
