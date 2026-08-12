using System.Text.Json;
using System.Text.Json.Serialization;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening Module — Wave 5b smoke test. Parses every fixture under
/// <c>backend/src/OetLearner.Api/Data/SeedData/listening/</c> and asserts:
///   • The JSON deserialises into the seeder's record shape.
///   • Each fixture has a non-empty slug, ≥ 42 questions, ≥ 1 extract.
///   • Question numbers are unique and 1..42 (no off-by-one in hand-authored content).
///   • Part code distribution matches the canonical 12/12/6/6/6 layout.
/// Catches authoring drift without booting the API.
/// </summary>
public class ListeningStarterFixtureParseTest
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Every_fixture_parses_and_matches_canonical_42_layout()
    {
        var root = LocateFixtureRoot();
        Assert.True(Directory.Exists(root),
            $"Listening starter fixture directory not found at {root}.");

        var fixtures = Directory.EnumerateFiles(root, "*.json").ToList();
        Assert.NotEmpty(fixtures);

        foreach (var path in fixtures)
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<StarterFixture>(json, JsonOpts);
            Assert.NotNull(doc);

            Assert.False(string.IsNullOrWhiteSpace(doc!.Slug),
                $"{Path.GetFileName(path)}: missing slug.");
            Assert.NotNull(doc.Questions);
            Assert.Equal(42, doc.Questions!.Count);
            Assert.NotNull(doc.Extracts);
            Assert.True(doc.Extracts!.Count >= 1);

            var numbers = doc.Questions.Select(q => q.Number).ToList();
            Assert.Equal(42, numbers.Distinct().Count());
            Assert.Equal(1, numbers.Min());
            Assert.Equal(42, numbers.Max());

            var byPart = doc.Questions.GroupBy(q => q.PartCode).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(12, byPart["A1"]);
            Assert.Equal(12, byPart["A2"]);
            // Part B is six independent sub-sections (B1..B6) with one item each.
            foreach (var bCode in new[] { "B1", "B2", "B3", "B4", "B5", "B6" })
            {
                Assert.True(byPart.TryGetValue(bCode, out var bCount) && bCount == 1,
                    $"{Path.GetFileName(path)}: expected exactly 1 item in {bCode}.");
            }
            Assert.False(byPart.ContainsKey("B"),
                $"{Path.GetFileName(path)}: bare 'B' part code is no longer allowed; use B1..B6.");
            Assert.Equal(6,  byPart["C1"]);
            Assert.Equal(6,  byPart["C2"]);

            // Every MCQ row carries exactly 3 options + a correct-answer that
            // appears in that option list. (Catches typos in hand-edits.)
            foreach (var q in doc.Questions.Where(q => q.Type == "multiple_choice_3"))
            {
                Assert.NotNull(q.Options);
                Assert.Equal(3, q.Options!.Count);
                Assert.Contains(q.CorrectAnswer, q.Options);
            }
        }
    }

    private static string LocateFixtureRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "backend", "src", "OetLearner.Api",
                "Data", "SeedData", "listening");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return Path.Combine(AppContext.BaseDirectory, "Data", "SeedData", "listening");
    }

    private sealed record StarterFixture(
        string Slug,
        string Title,
        string? Difficulty,
        string? ProfessionScope,
        string? ProfessionId,
        List<FixtureExtract>? Extracts,
        List<FixtureQuestion>? Questions);

    private sealed record FixtureExtract(
        string PartCode, int DisplayOrder, string Kind, string Title,
        string? AccentCode, int? AudioStartMs, int? AudioEndMs);

    private sealed record FixtureQuestion(
        string Id, int Number, string PartCode, string Type, string Stem,
        List<string>? Options, string CorrectAnswer, List<string>? AcceptedAnswers,
        string? Explanation, string? SkillTag, string? TranscriptExcerpt,
        string? DistractorExplanation, int Points,
        List<string?>? OptionDistractorWhy, List<string?>? OptionDistractorCategory,
        string? SpeakerAttitude, int? TranscriptEvidenceStartMs, int? TranscriptEvidenceEndMs);
}
