using System.Text.Json;
using System.Text.Json.Serialization;
using OetLearner.Api.Services.Listening;
using Xunit;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Authoritative completeness test suite for Listening Part B and Part C across Atlas and Nova.
/// Verifies:
///   - Critical Issue 1: Real clinical question stems present, no "See PDF", no OCR artifacts.
///   - Critical Issue 2: Part B contains all 6 questions (Q25..Q30) with 3 valid options each.
///   - Critical Issue 3: Part C contains all 12 questions (Q31..Q42: C1 31-36, C2 37-42) with valid stems and options.
///   - Paper isolation: Atlas and Nova maintain independent, distinct question sets.
/// </summary>
public class ListeningPartBAndCCompletenessTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Test1_PartB_Contains_Exactly_6_Questions_Numbered_25_Through_30()
    {
        var root = LocateFixtureRoot();
        var fixtures = Directory.EnumerateFiles(root, "*.json").ToList();
        Assert.NotEmpty(fixtures);

        foreach (var path in fixtures)
        {
            var fileName = Path.GetFileName(path);
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<StarterFixture>(json, JsonOpts);
            Assert.NotNull(doc);
            Assert.NotNull(doc!.Questions);

            var partBQuestions = doc.Questions
                .Where(q => q.Number >= 25 && q.Number <= 30)
                .OrderBy(q => q.Number)
                .ToList();

            Assert.True(partBQuestions.Count == 6, $"{fileName}: Part B must contain exactly 6 questions, found {partBQuestions.Count}");

            var numbers = partBQuestions.Select(q => q.Number).ToList();
            Assert.Equal(new[] { 25, 26, 27, 28, 29, 30 }, numbers);
        }
    }

    [Fact]
    public void Test2_PartC_Contains_Exactly_12_Questions_Numbered_31_Through_42()
    {
        var root = LocateFixtureRoot();
        var fixtures = Directory.EnumerateFiles(root, "*.json").ToList();
        Assert.NotEmpty(fixtures);

        foreach (var path in fixtures)
        {
            var fileName = Path.GetFileName(path);
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<StarterFixture>(json, JsonOpts);
            Assert.NotNull(doc);
            Assert.NotNull(doc!.Questions);

            var partCQuestions = doc.Questions
                .Where(q => q.Number >= 31 && q.Number <= 42)
                .OrderBy(q => q.Number)
                .ToList();

            Assert.True(partCQuestions.Count == 12, $"{fileName}: Part C must contain exactly 12 questions, found {partCQuestions.Count}");

            var numbers = partCQuestions.Select(q => q.Number).ToList();
            Assert.Equal(Enumerable.Range(31, 12).ToList(), numbers);

            var c1 = partCQuestions.Where(q => q.PartCode == "C1").ToList();
            var c2 = partCQuestions.Where(q => q.PartCode == "C2").ToList();
            Assert.Equal(6, c1.Count);
            Assert.Equal(6, c2.Count);
            Assert.Equal(new[] { 31, 32, 33, 34, 35, 36 }, c1.Select(q => q.Number));
            Assert.Equal(new[] { 37, 38, 39, 40, 41, 42 }, c2.Select(q => q.Number));
        }
    }

    [Fact]
    public void Test3_All_PartB_And_PartC_Questions_Have_Authentic_Clinical_Stems()
    {
        var root = LocateFixtureRoot();
        var fixtures = Directory.EnumerateFiles(root, "*.json").ToList();
        Assert.NotEmpty(fixtures);

        var forbiddenSentinels = new[] { "see pdf", "cpdf", "pdf", "view pdf" };

        foreach (var path in fixtures)
        {
            var fileName = Path.GetFileName(path);
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<StarterFixture>(json, JsonOpts);
            Assert.NotNull(doc);
            Assert.NotNull(doc!.Questions);

            var mcqQuestions = doc.Questions
                .Where(q => q.Number >= 25 && q.Number <= 42)
                .ToList();

            foreach (var q in mcqQuestions)
            {
                var rawStem = q.Stem?.Trim();
                Assert.False(string.IsNullOrWhiteSpace(rawStem),
                    $"{fileName} Question {q.Number}: Stem must not be empty or null.");

                Assert.False(forbiddenSentinels.Contains(rawStem!.ToLowerInvariant()),
                    $"{fileName} Question {q.Number}: Stem cannot be a sentinel like '{rawStem}'.");

                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(rawStem, @"^PART\s+[BC]\s+QUESTION\s+\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

                var sanitized = ListeningLearnerService.SanitizeQuestionPrompt(rawStem);
                Assert.False(string.IsNullOrWhiteSpace(sanitized),
                    $"{fileName} Question {q.Number}: Sanitized stem must not be empty.");
            }
        }
    }

    [Fact]
    public void Test4_All_PartB_And_PartC_Questions_Have_3_Valid_Options()
    {
        var root = LocateFixtureRoot();
        var fixtures = Directory.EnumerateFiles(root, "*.json").ToList();
        Assert.NotEmpty(fixtures);

        foreach (var path in fixtures)
        {
            var fileName = Path.GetFileName(path);
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<StarterFixture>(json, JsonOpts);
            Assert.NotNull(doc);
            Assert.NotNull(doc!.Questions);

            var mcqQuestions = doc.Questions
                .Where(q => q.Number >= 25 && q.Number <= 42)
                .ToList();

            foreach (var q in mcqQuestions)
            {
                Assert.NotNull(q.Options);
                Assert.True(q.Options!.Count == 3,
                    $"{fileName} Question {q.Number}: Expected exactly 3 options, got {q.Options.Count}");

                foreach (var opt in q.Options)
                {
                    Assert.False(string.IsNullOrWhiteSpace(opt),
                        $"{fileName} Question {q.Number}: Option text must not be empty.");

                    Assert.False(System.Text.RegularExpressions.Regex.IsMatch(opt, @"[-=]{2,}\s*PAGE\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                    Assert.False(System.Text.RegularExpressions.Regex.IsMatch(opt, @"Practice Test\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                }

                // Verify CorrectAnswer matches one of the options or option keys (A/B/C)
                var correct = q.CorrectAnswer?.Trim();
                Assert.False(string.IsNullOrWhiteSpace(correct),
                    $"{fileName} Question {q.Number}: CorrectAnswer must not be empty.");

                var matchesOption = q.Options.Any(o => string.Equals(o.Trim(), correct, StringComparison.OrdinalIgnoreCase))
                    || new[] { "A", "B", "C" }.Contains(correct, StringComparer.OrdinalIgnoreCase);

                Assert.True(matchesOption,
                    $"{fileName} Question {q.Number}: CorrectAnswer '{correct}' must match an option or valid key.");
            }
        }
    }

    [Fact]
    public void Test9_Atlas_And_Nova_Fixtures_Are_Isolated_And_Distinct()
    {
        var root = LocateFixtureRoot();
        var atlasPath = Path.Combine(root, "starter-mock-1.json");
        var novaPath = Path.Combine(root, "starter-mock-2.json");

        Assert.True(File.Exists(atlasPath), "Atlas fixture starter-mock-1.json must exist.");
        Assert.True(File.Exists(novaPath), "Nova fixture starter-mock-2.json must exist.");

        var atlasDoc = JsonSerializer.Deserialize<StarterFixture>(File.ReadAllText(atlasPath), JsonOpts);
        var novaDoc = JsonSerializer.Deserialize<StarterFixture>(File.ReadAllText(novaPath), JsonOpts);

        Assert.NotNull(atlasDoc);
        Assert.NotNull(novaDoc);

        Assert.NotEqual(atlasDoc!.Slug, novaDoc!.Slug);
        Assert.NotEqual(atlasDoc.Title, novaDoc.Title);

        // Verify stems differ across Atlas and Nova
        var atlasQ25 = atlasDoc.Questions!.First(q => q.Number == 25);
        var novaQ25 = novaDoc.Questions!.First(q => q.Number == 25);
        Assert.NotEqual(atlasQ25.Stem, novaQ25.Stem);

        var atlasQ31 = atlasDoc.Questions!.First(q => q.Number == 31);
        var novaQ31 = novaDoc.Questions!.First(q => q.Number == 31);
        Assert.NotEqual(atlasQ31.Stem, novaQ31.Stem);
    }

    private static string LocateFixtureRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "backend", "src", "OetLearner.Api", "Data", "SeedData", "listening");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "backend", "src", "OetLearner.Api", "Data", "SeedData", "listening"));
    }

    private sealed record StarterFixture
    {
        public string? Slug { get; init; }
        public string? Title { get; init; }
        public List<StarterQuestion>? Questions { get; init; }
        public List<JsonElement>? Extracts { get; init; }
    }

    private sealed record StarterQuestion
    {
        public string? Id { get; init; }
        public int Number { get; init; }
        public string? PartCode { get; init; }
        public string? Type { get; init; }
        public string? Stem { get; init; }
        public List<string>? Options { get; init; }
        public string? CorrectAnswer { get; init; }
        public List<string>? AcceptedAnswers { get; init; }
    }
}
