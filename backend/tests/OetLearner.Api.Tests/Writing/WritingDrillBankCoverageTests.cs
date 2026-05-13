using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Audit P3-3 closure (May 2026). Locks the per-profession Writing drill
/// bank coverage. Every canonical OET profession (12 in total) must ship at
/// least one minimal-valid drill JSON embedded in the API assembly so the
/// learner-facing remediation plan never falls back to a generic stub.
///
/// Profession folder names follow the kebab-case convention used by the
/// rulebook loader (<c>occupational-therapy</c>, <c>speech-pathology</c>),
/// while the JSON <c>profession</c> field is normalised to the
/// <see cref="ExamProfession"/> spelling that grading code consumes.
/// </summary>
public class WritingDrillBankCoverageTests
{
    /// <summary>
    /// Each entry is (folder slug under <c>rulebooks/writing/drills/</c>,
    /// canonical profession token used by the rule engine).
    /// </summary>
    public static readonly IReadOnlyList<(string Folder, string ProfessionToken)> CanonicalProfessions =
    [
        ("medicine", "medicine"),
        ("nursing", "nursing"),
        ("dentistry", "dentistry"),
        ("pharmacy", "pharmacy"),
        ("physiotherapy", "physiotherapy"),
        ("veterinary", "veterinary"),
        ("optometry", "optometry"),
        ("radiography", "radiography"),
        ("occupational-therapy", "occupational_therapy"),
        ("speech-pathology", "speech_pathology"),
        ("podiatry", "podiatry"),
        ("dietetics", "dietetics"),
    ];

    [Fact]
    public void CanonicalProfessions_AreExactlyTwelve_AndUnique()
    {
        Assert.Equal(12, CanonicalProfessions.Count);

        var folders = CanonicalProfessions.Select(p => p.Folder).ToHashSet();
        var tokens = CanonicalProfessions.Select(p => p.ProfessionToken).ToHashSet();
        Assert.Equal(CanonicalProfessions.Count, folders.Count);
        Assert.Equal(CanonicalProfessions.Count, tokens.Count);
    }

    public static IEnumerable<object[]> AllProfessions =>
        CanonicalProfessions.Select(p => new object[] { p.Folder, p.ProfessionToken });

    [Theory]
    [MemberData(nameof(AllProfessions))]
    public void EveryProfession_HasMinimalValidAbbreviationDrillEmbedded(string folder, string professionToken)
    {
        var stream = OpenDrillResource(folder, "abbreviation", "abbreviation-001.json");
        Assert.NotNull(stream);

        using var _ = stream;
        using var doc = JsonDocument.Parse(stream!);
        var root = doc.RootElement;

        // Required identity + classification fields.
        AssertNonEmptyString(root, "id");
        AssertEqualsCaseInsensitive(root, "type", "abbreviation");
        AssertEqualsCaseInsensitive(root, "profession", professionToken);
        AssertNonEmptyString(root, "title");
        AssertNonEmptyString(root, "brief");
        AssertNonEmptyString(root, "letterType");

        // Minimal viable items array — at least three.
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() >= 3,
            $"Drill {professionToken} must contain at least 3 items, found {items.GetArrayLength()}.");

        foreach (var item in items.EnumerateArray())
        {
            AssertNonEmptyString(item, "id");
            AssertNonEmptyString(item, "abbreviation");
            AssertNonEmptyString(item, "context");
            AssertNonEmptyString(item, "expected");
            AssertNonEmptyString(item, "expansion");
            AssertNonEmptyString(item, "rationale");

            var expected = item.GetProperty("expected").GetString();
            Assert.True(expected is "keep" or "expand",
                $"'expected' must be 'keep' or 'expand' (drill={professionToken}, item={item.GetProperty("id").GetString()}).");
        }
    }

    [Fact]
    public void DrillProfessionTokens_AreParseableByRulebookProfessionParser()
    {
        // Prevents future drift between drill bank metadata and the
        // grading-side profession enum. Every token must round-trip through
        // RulebookProfessionParser without falling back to default(...).
        foreach (var (_, token) in CanonicalProfessions)
        {
            Assert.True(RulebookProfessionParser.TryParse(token, out var profession),
                $"Profession token '{token}' is not parseable by RulebookProfessionParser.");
            // Round-trip must produce a token that normalises back to the
            // original. Defeats accidental aliasing where two tokens collapse
            // onto the same enum value.
            var normalisedRoundTrip = new string(profession.ToString()
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
            var normalisedToken = new string(token
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
            Assert.Equal(normalisedToken, normalisedRoundTrip);
        }
    }

    private static Stream? OpenDrillResource(string professionFolder, string drillType, string fileName)
    {
        // Resolve the drill bank by its on-disk path so the test runs the
        // same way regardless of how the API project embeds resources at
        // build time. The rulebook root is repo/rulebooks; this test lives
        // under repo/backend/tests/... so we walk up to find it.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "rulebooks", "writing", "drills",
                professionFolder, drillType, fileName);
            if (File.Exists(candidate)) return File.OpenRead(candidate);
            dir = dir.Parent;
        }

        // Fall back to the embedded copy on the API assembly so CI containers
        // without the workspace mounted still pass this test.
        var asm = typeof(IRulebookLoader).Assembly;
        var logicalName = $"OetRulebooks/writing/drills/{professionFolder}/{drillType}/{fileName}";
        var candidates = new[]
        {
            logicalName,
            logicalName.Replace('/', '\\'),
            logicalName.Replace('/', '.'),
            logicalName.Replace('\\', '.'),
        };
        foreach (var c in candidates)
        {
            var match = asm.GetManifestResourceNames()
                .FirstOrDefault(n => string.Equals(n, c, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return asm.GetManifestResourceStream(match);
        }
        return null;
    }

    private static void AssertNonEmptyString(JsonElement parent, string property)
    {
        Assert.True(parent.TryGetProperty(property, out var el),
            $"Required property '{property}' missing.");
        Assert.Equal(JsonValueKind.String, el.ValueKind);
        var value = el.GetString();
        Assert.False(string.IsNullOrWhiteSpace(value), $"Property '{property}' must not be empty.");
    }

    private static void AssertEqualsCaseInsensitive(JsonElement parent, string property, string expected)
    {
        Assert.True(parent.TryGetProperty(property, out var el),
            $"Required property '{property}' missing.");
        Assert.Equal(JsonValueKind.String, el.ValueKind);
        Assert.Equal(expected, el.GetString(), ignoreCase: true);
    }
}
