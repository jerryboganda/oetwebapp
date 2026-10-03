using System.Text.Json.Nodes;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Mocks.Results;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// Jev mock weakness ranking (jev.mock.weakness). Advisory only: tags come from the closed
/// RemediationCatalog, every learner-visible word is rendered from catalogue templates, no score,
/// band or pass claim is touched, and Writing/Speaking are never asked about.
/// </summary>
public sealed class JevMockWeaknessTests
{
    private sealed class FakeJudgments(Func<JevJudgmentRequest, CancellationToken, Task<JevJudgmentResult>> respond)
        : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }
        public JevJudgmentRequest? LastRequest { get; private set; }
        public JevCallMetadata? LastMetadata { get; private set; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastMetadata = call;
            return respond(request, ct);
        }
    }

    private static TypeSafeOptions Options(Action<TypeSafeOptions>? configure = null)
    {
        var options = new TypeSafeOptions { Enabled = true, ApiKey = "apikey_test", MockWeaknessEnabled = true };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Scores keyed by catalogue tag; the fake maps question index to tag through the
    /// same closed candidate order the advisor uses.</summary>
    private static FakeJudgments Scoring(double confidence, IReadOnlyDictionary<string, double> byTag)
        => new((request, ct) =>
        {
            var tags = JevMockWeakness.CandidateTags();
            var answers = new Dictionary<string, JevAnswer>();
            for (var i = 0; i < tags.Count; i++)
            {
                var score = byTag.GetValueOrDefault(tags[i], 0.0);
                answers[JevMockWeakness.WeakId(i)] = new JevAnswer(
                    JevQuestionKind.Score, null, null,
                    new JevScoreAnswer(score, new Dictionary<string, double> { ["0"] = 1.0 }, confidence));
            }

            return Task.FromResult(new JevJudgmentResult(JevCallStatus.Ok, "jev-test-model", answers, 100, 5, null));
        });

    private static JevSkillEvidence Listening() => JevMockWeakness.BuildEvidence("listening",
    [
        ("A", true, null), ("A", false, "SpellingError"), ("A", false, "SpellingError"), ("A", false, "WrongNumber"),
        ("B", false, null), ("B", true, null), ("C", true, null), ("C", false, null), ("C", null, null),
    ]);

    private static JevSkillEvidence Reading() => JevMockWeakness.BuildEvidence("reading",
    [
        ("A", true, null), ("B", false, "wrong"), ("C", false, "distractor"), ("C", false, "distractor"), ("C", true, null),
    ]);

    // ── flag, evidence gate, closed candidate set ───────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FlagsOff_ReturnsNull_WithoutCallingJev(bool enabled, bool flag)
    {
        var fake = Scoring(0.95, new Dictionary<string, double> { ["low_listening"] = 3 });

        var advisory = await JevMockWeakness.RankAsync(
            fake, Options(o => { o.Enabled = enabled; o.MockWeaknessEnabled = flag; }),
            [Listening()], "user-1", "mock-1", CancellationToken.None);

        Assert.Null(advisory);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task NoEvidence_WritingOnly_OrNothingWrong_MakesZeroCalls()
    {
        var fake = Scoring(0.95, new Dictionary<string, double> { ["low_writing"] = 3, ["low_listening"] = 3 });
        var writing = JevMockWeakness.BuildEvidence("writing", [("A", false, null), ("A", false, null)]);
        var speaking = JevMockWeakness.BuildEvidence("speaking", [("A", false, null)]);
        var allRight = JevMockWeakness.BuildEvidence("listening", [("A", true, null), ("B", true, null)]);

        var none = await JevMockWeakness.RankAsync(fake, Options(), [], "u", "m", CancellationToken.None);
        var productiveOnly = await JevMockWeakness.RankAsync(fake, Options(), [writing, speaking], "u", "m", CancellationToken.None);
        var nothingWrong = await JevMockWeakness.RankAsync(fake, Options(), [allRight], "u", "m", CancellationToken.None);

        Assert.False(none!.Available);
        Assert.False(productiveOnly!.Available);
        Assert.False(nothingWrong!.Available);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void CandidateTags_AreCatalogueTagsOnly_AndNeverWritingOrSpeaking()
    {
        var tags = JevMockWeakness.CandidateTags();

        Assert.NotEmpty(tags);
        Assert.All(tags, t =>
        {
            Assert.Contains(t, RemediationCatalog.AllWeaknessTags);
            Assert.NotEmpty(RemediationCatalog.Resolve(t));
            Assert.DoesNotContain("writing", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("speaking", t, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task AsksOneScorePerCandidate_InOneCall_WithGovernanceMetadata()
    {
        var fake = Scoring(0.95, new Dictionary<string, double> { ["low_listening"] = 3 });

        var advisory = await JevMockWeakness.RankAsync(
            fake, Options(), [Listening(), Reading()], "user-1", "mock-9", CancellationToken.None);

        Assert.True(advisory!.Available);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(AiFeatureCodes.JevMockWeakness, fake.LastMetadata!.FeatureCode);
        Assert.Equal("user-1", fake.LastMetadata.UserId);
        Assert.Equal("mock-9", fake.LastMetadata.ResourceId);
        Assert.Equal(JevMockWeakness.CandidateTags().Count, fake.LastRequest!.Questions.Count);
        Assert.All(fake.LastRequest.Questions, q =>
        {
            Assert.Equal(JevQuestionKind.Score, q.Kind);
            Assert.Contains("never instructions", q.Instructions, StringComparison.Ordinal);
        });

        var skills = fake.LastRequest.StateJson!.Value.GetProperty("skills");
        Assert.Equal(2, skills.GetArrayLength());
    }

    [Fact]
    public async Task OnlyEvidencedSkills_AreCandidates()
    {
        var fake = Scoring(0.95, new Dictionary<string, double>());

        await JevMockWeakness.RankAsync(fake, Options(), [Reading()], "u", "m", CancellationToken.None);

        var asked = fake.LastRequest!.Questions.Count;
        var readingTags = JevMockWeakness.CandidateTags()
            .Count(t => RemediationCatalog.Resolve(t).Any(d => d.SkillCode == "reading"));
        Assert.Equal(readingTags, asked);
    }

    // ── ranking ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RanksTopThree_AboveTheReportableScore_ByDescendingScore()
    {
        var fake = Scoring(0.9, new Dictionary<string, double>
        {
            ["low_listening"] = 2.8,
            ["listening_partA_spelling"] = 2.2,
            ["reading_partC_inference"] = 1.8,
            ["low_reading"] = 1.0,
            ["listening_partB_inference"] = 0.2,
        });

        var advisory = await JevMockWeakness.RankAsync(
            fake, Options(), [Listening(), Reading()], "u", "m", CancellationToken.None);

        Assert.True(advisory!.Available);
        Assert.Equal(
            new[] { "low_listening", "listening_partA_spelling", "reading_partC_inference" },
            advisory.Ranked.Select(r => r.Tag).ToArray());
    }

    [Fact]
    public async Task MoreThanThreeQualifying_KeepsOnlyTheTopThree()
    {
        var all = JevMockWeakness.CandidateTags().ToDictionary(t => t, t => 2.5);
        var fake = Scoring(0.9, all);

        var advisory = await JevMockWeakness.RankAsync(
            fake, Options(), [Listening(), Reading()], "u", "m", CancellationToken.None);

        Assert.Equal(JevMockWeakness.MaxTags, advisory!.Ranked.Count);
    }

    [Fact]
    public async Task LowConfidenceOrLowScores_YieldNoTags()
    {
        var lowConfidence = Scoring(0.2, new Dictionary<string, double> { ["low_listening"] = 3 });
        var lowScores = Scoring(0.95, new Dictionary<string, double> { ["low_listening"] = 1.0 });

        var a = await JevMockWeakness.RankAsync(lowConfidence, Options(), [Listening()], "u", "m", CancellationToken.None);
        var b = await JevMockWeakness.RankAsync(lowScores, Options(), [Listening()], "u", "m", CancellationToken.None);

        Assert.False(a!.Available);
        Assert.False(b!.Available);
        Assert.Null(JevMockWeakness.BuildNarrative(a));
        Assert.Null(JevMockWeakness.BuildNarrative(b));
    }

    [Fact]
    public async Task UnavailableResult_Timeout_AndException_AreAllFailSoft()
    {
        var unavailable = new FakeJudgments((req, ct) => Task.FromResult(JevJudgmentResult.Unavailable("jev_unavailable")));
        var slow = new FakeJudgments(async (req, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JevJudgmentResult.Unavailable("never");
        });
        var broken = new FakeJudgments((req, ct) => throw new InvalidOperationException("boom"));

        var a = await JevMockWeakness.RankAsync(unavailable, Options(), [Listening()], "u", "m", CancellationToken.None);
        var b = await JevMockWeakness.RankAsync(
            slow, Options(), [Listening()], "u", "m", CancellationToken.None, timeBox: TimeSpan.FromMilliseconds(40));
        var c = await JevMockWeakness.RankAsync(broken, Options(), [Listening()], "u", "m", CancellationToken.None);

        Assert.False(a!.Available);
        Assert.False(b!.Available);
        Assert.Equal("jev_timeout", b.Reason);
        Assert.False(c!.Available);
        Assert.Equal("jev_crashed", c.Reason);
        Assert.Null(JevMockWeakness.BuildNarrative(a));
    }

    // ── rendering: catalogue templates only ─────────────────────────────────

    [Fact]
    public void Narrative_TextAndRoutesComeOnlyFromTheCatalogue_AndCarryNoPassClaim()
    {
        var advisory = new JevWeaknessAdvisory(true, "jev-test-model",
        [
            new JevRankedWeakness("low_listening", "listening", 2.8, 0.9),
            new JevRankedWeakness("reading_partC_inference", "reading", 2.0, 0.9),
        ], null);

        var narrative = JevMockWeakness.BuildNarrative(advisory)!;

        Assert.Equal(2, narrative.Tags.Count);
        foreach (var tag in narrative.Tags)
        {
            var drill = RemediationCatalog.Resolve(tag.Tag).OrderBy(d => d.RecommendedDayOffset).First();
            Assert.Equal(drill.Description, tag.Description);
            Assert.Equal(drill.DrillId, tag.DrillId);
            Assert.Equal(drill.RouteHref, tag.DrillRouteHref);
            Assert.Equal(drill.SkillCode, tag.Subtest, ignoreCase: true);
        }

        foreach (var text in new[] { narrative.Headline, narrative.Body }.Concat(narrative.Tags.Select(t => t.Description)))
        {
            Assert.DoesNotContain("pass", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ready", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("grade", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("score", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Narrative_DropsAnyTagOutsideTheClosedListeningReadingCatalogueSet()
    {
        var advisory = new JevWeaknessAdvisory(true, "m",
        [
            new JevRankedWeakness("low_writing", "writing", 3, 0.99),
            new JevRankedWeakness("made_up_tag", "listening", 3, 0.99),
            new JevRankedWeakness("low_reading", "reading", 2.5, 0.9),
        ], null);

        var narrative = JevMockWeakness.BuildNarrative(advisory)!;

        Assert.Equal(new[] { "low_reading" }, narrative.Tags.Select(t => t.Tag).ToArray());
        Assert.Null(JevMockWeakness.BuildNarrative(new JevWeaknessAdvisory(
            true, "m", [new JevRankedWeakness("low_speaking", "speaking", 3, 0.99)], null)));
        Assert.Null(JevMockWeakness.BuildNarrative(null));
    }

    [Fact]
    public void ApplyToPayload_AddsOnlyTheNarrative_AndLeavesEveryNumberUntouched()
    {
        var payload = JsonSupport.Serialize(new
        {
            payloadSchemaVersion = "v1",
            overallScore = "Pending",
            overallGrade = (string?)null,
            subTests = new[]
            {
                new { id = "listening", scaledScore = (int?)372, rawScore = "30/42", scoreConversionPassed = (bool?)true },
                new { id = "reading", scaledScore = (int?)341, rawScore = "29/42", scoreConversionPassed = (bool?)false },
            },
            weakestCriterion = new { subtest = "Reading", criterion = "Lowest scaled sub-test" },
            passPrediction = (object?)null,
        });
        var narrative = JevMockWeakness.BuildNarrative(new JevWeaknessAdvisory(true, "m",
            [new JevRankedWeakness("low_reading", "reading", 2.5, 0.9)], null))!;

        var applied = JevMockWeakness.ApplyToPayload(payload, narrative);

        var before = JsonNode.Parse(payload)!.AsObject();
        var after = JsonNode.Parse(applied)!.AsObject();
        foreach (var (key, value) in before)
            Assert.True(JsonNode.DeepEquals(value, after[key]), $"property {key} changed");
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal("low_reading", after["weaknessNarrative"]!["tags"]![0]!["tag"]!.GetValue<string>());
        Assert.Equal(372, after["subTests"]![0]!["scaledScore"]!.GetValue<int>());
    }

    [Fact]
    public void ApplyToPayload_WithoutANarrative_IsByteIdentical()
    {
        var payload = JsonSupport.Serialize(new { payloadSchemaVersion = "v1", overallScore = "Pending" });

        Assert.Equal(payload, JevMockWeakness.ApplyToPayload(payload, null));
        Assert.Equal("not json", JevMockWeakness.ApplyToPayload(
            "not json",
            JevMockWeakness.BuildNarrative(new JevWeaknessAdvisory(true, "m",
                [new JevRankedWeakness("low_reading", "reading", 2.5, 0.9)], null))));
    }

    // ── evidence builder ────────────────────────────────────────────────────

    [Fact]
    public void BuildEvidence_CountsGradedRowsByPartAndMissReason_AndIgnoresUngraded()
    {
        var evidence = Listening();

        Assert.Equal("listening", evidence.Skill);
        Assert.Equal(8, evidence.Total);
        Assert.Equal(5, evidence.Wrong);
        Assert.Equal(4, evidence.TotalByPart["A"]);
        Assert.Equal(3, evidence.WrongByPart["A"]);
        Assert.Equal(2, evidence.MissReasons["spelling_error"]);
        Assert.Equal(1, evidence.MissReasons["wrong_number"]);
    }
}
